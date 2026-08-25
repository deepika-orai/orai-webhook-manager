using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Models;
using OraiWebhookManager.Domain.Entities;
using OraiWebhookManager.Domain.Enums;
using OraiWebhookManager.Domain.Rules;

namespace OraiWebhookManager.Infrastructure.Persistence.Repositories;

public class WebhookProcessorRepository : IWebhookProcessorRepository
{
    private readonly string _connectionString;

    public WebhookProcessorRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection string is not configured.");
    }

    public async Task<IReadOnlyList<WebhookInboxItem>> ClaimBatchAsync(
        Guid lockToken,
        string lockedBy,
        int batchSize,
        int leaseDurationSeconds,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            WITH claimable AS (
                SELECT id FROM webhook_inbox
                WHERE (status = 0 OR (status = 1 AND locked_until < NOW()))
                  AND next_attempt_at <= NOW()
                ORDER BY created_at ASC
                LIMIT @BatchSize
                FOR UPDATE SKIP LOCKED
            )
            UPDATE webhook_inbox i
            SET status = 1,
                lock_token = @LockToken,
                locked_by = @LockedBy,
                locked_until = NOW() + (@LeaseDurationSeconds * INTERVAL '1 second')
            FROM claimable c
            WHERE i.id = c.id
            RETURNING i.id, i.tenant_id AS TenantId, i.endpoint_id AS EndpointId, i.payload_raw AS PayloadRaw,
                      i.headers AS Headers, i.ip_address AS IpAddress, i.status AS Status, i.attempt_count AS AttemptCount,
                      i.last_error AS LastError, i.lock_token AS LockToken, i.locked_by AS LockedBy, i.locked_until AS LockedUntil,
                      i.next_attempt_at AS NextAttemptAt, i.created_at AS CreatedAt, i.processed_at AS ProcessedAt;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<WebhookInboxItem>(
            new CommandDefinition(sql, new
            {
                LockToken = lockToken,
                LockedBy = lockedBy,
                BatchSize = batchSize,
                LeaseDurationSeconds = leaseDurationSeconds
            }, cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<bool> ProcessItemAtomicAsync(
        WebhookInboxItem item,
        Guid lockToken,
        IReadOnlyList<ExtractedStatusEvent> events,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var evt in events)
            {
                var normStatus = evt.Status.Trim().ToLowerInvariant();
                var statusRank = MessageStateEngine.GetStatusRank(normStatus);
                var timestampUnix = evt.StatusTimestamp.ToUnixTimeSeconds();
                var fingerprint = MessageStateEngine.ComputeEventFingerprint(
                    item.TenantId,
                    evt.Wamid,
                    normStatus,
                    timestampUnix,
                    evt.ErrorCode
                );

                // Step 1: Ensure minimal message identity row exists (does not apply incoming status yet)
                const string insertMessageIdentitySql = """
                    INSERT INTO messages (id, tenant_id, endpoint_id, wamid, created_at, updated_at)
                    VALUES (@NewMsgId, @TenantId, @EndpointId, @Wamid, NOW(), NOW())
                    ON CONFLICT (tenant_id, wamid) DO NOTHING;
                    """;

                var newMsgId = Guid.NewGuid();
                await connection.ExecuteAsync(
                    new CommandDefinition(insertMessageIdentitySql, new
                    {
                        NewMsgId = newMsgId,
                        TenantId = item.TenantId,
                        EndpointId = item.EndpointId,
                        Wamid = evt.Wamid
                    }, transaction: transaction, cancellationToken: cancellationToken));

                // Step 2: Resolve the existing or new message_id
                const string resolveMessageIdSql = """
                    SELECT id FROM messages
                    WHERE tenant_id = @TenantId AND wamid = @Wamid
                    LIMIT 1;
                    """;

                var messageId = await connection.ExecuteScalarAsync<Guid>(
                    new CommandDefinition(resolveMessageIdSql, new
                    {
                        TenantId = item.TenantId,
                        Wamid = evt.Wamid
                    }, transaction: transaction, cancellationToken: cancellationToken));

                if (messageId == Guid.Empty)
                {
                    throw new InvalidOperationException($"Failed to resolve message_id for wamid {evt.Wamid}");
                }

                // Step 3: Insert message_status_events using event_fingerprint and ON CONFLICT DO NOTHING RETURNING id
                const string insertStatusEventSql = """
                    INSERT INTO message_status_events (
                        id, message_id, tenant_id, wamid, status, status_timestamp,
                        event_fingerprint, error_code, error_title, error_message, error_details, error_data,
                        raw_event, created_at
                    )
                    VALUES (
                        @EventId, @MessageId, @TenantId, @Wamid, @Status, @StatusTimestamp,
                        @EventFingerprint, @ErrorCode, @ErrorTitle, @ErrorMessage, @ErrorDetails, CAST(@ErrorData AS jsonb),
                        CAST(@RawEvent AS jsonb), NOW()
                    )
                    ON CONFLICT (event_fingerprint) DO NOTHING
                    RETURNING id;
                    """;

                var eventId = Guid.NewGuid();
                var insertedEventId = await connection.ExecuteScalarAsync<Guid?>(
                    new CommandDefinition(insertStatusEventSql, new
                    {
                        EventId = eventId,
                        MessageId = messageId,
                        TenantId = item.TenantId,
                        Wamid = evt.Wamid,
                        Status = normStatus,
                        StatusTimestamp = evt.StatusTimestamp,
                        EventFingerprint = fingerprint,
                        ErrorCode = evt.ErrorCode,
                        ErrorTitle = evt.ErrorTitle,
                        ErrorMessage = evt.ErrorMessage,
                        ErrorDetails = evt.ErrorDetails,
                        ErrorData = evt.ErrorDataJson ?? "null",
                        RawEvent = evt.RawEventSnippetJson
                    }, transaction: transaction, cancellationToken: cancellationToken));

                // Step 4: ONLY when the event insert returns a row (i.e. not a duplicate), apply atomic state-transition matrix
                if (insertedEventId.HasValue && insertedEventId.Value != Guid.Empty)
                {
                    const string updateMessageStateSql = """
                        UPDATE messages
                        SET
                            current_status = CASE
                                -- If already delivered or read, never downgrade to failed
                                WHEN @Status = 'failed' AND status_rank IN (20, 30) THEN current_status
                                -- A failed message cannot become sent due to a delayed sent callback
                                WHEN @Status = 'sent' AND current_status = 'failed' THEN current_status
                                -- Allow delivered/read to recover a failed state only if newer evidence (timestamp > failed.timestamp)
                                WHEN current_status = 'failed' AND @Status IN ('delivered', 'read') AND @StatusTimestamp > last_status_timestamp THEN @Status
                                -- Monotonic forward progression
                                WHEN @StatusRank > COALESCE(status_rank, 0) THEN @Status
                                WHEN @StatusRank = COALESCE(status_rank, 0) AND @StatusTimestamp >= COALESCE(last_status_timestamp, '-infinity'::timestamptz) THEN @Status
                                ELSE current_status
                            END,
                            status_rank = CASE
                                WHEN @Status = 'failed' AND status_rank IN (20, 30) THEN status_rank
                                WHEN @Status = 'sent' AND current_status = 'failed' THEN status_rank
                                WHEN current_status = 'failed' AND @Status IN ('delivered', 'read') AND @StatusTimestamp > last_status_timestamp THEN @StatusRank
                                WHEN @StatusRank > COALESCE(status_rank, 0) THEN @StatusRank
                                ELSE status_rank
                            END,
                            last_status_timestamp = GREATEST(COALESCE(last_status_timestamp, '-infinity'::timestamptz), @StatusTimestamp),
                            phone_number_id = COALESCE(@PhoneNumberId, phone_number_id),
                            display_phone_number = COALESCE(@DisplayPhoneNumber, display_phone_number),
                            recipient_phone = COALESCE(@RecipientPhone, recipient_phone),
                            conversation_id = COALESCE(@ConversationId, conversation_id),
                            conversation_origin_type = COALESCE(@ConversationOriginType, conversation_origin_type),
                            conversation_expires_at = COALESCE(@ConversationExpiresAt, conversation_expires_at),
                            pricing_model = COALESCE(@PricingModel, pricing_model),
                            pricing_category = COALESCE(@PricingCategory, pricing_category),
                            pricing_billable = COALESCE(@PricingBillable, pricing_billable),
                            biz_opaque_callback_data = COALESCE(@BizOpaqueCallbackData, biz_opaque_callback_data),
                            -- Active error fields populated ONLY when status is or becomes 'failed'
                            active_error_code = CASE WHEN @Status = 'failed' AND COALESCE(status_rank, 0) < 20 THEN @ErrorCode WHEN @Status IN ('delivered', 'read') THEN NULL ELSE active_error_code END,
                            active_error_title = CASE WHEN @Status = 'failed' AND COALESCE(status_rank, 0) < 20 THEN @ErrorTitle WHEN @Status IN ('delivered', 'read') THEN NULL ELSE active_error_title END,
                            active_error_message = CASE WHEN @Status = 'failed' AND COALESCE(status_rank, 0) < 20 THEN @ErrorMessage WHEN @Status IN ('delivered', 'read') THEN NULL ELSE active_error_message END,
                            active_error_details = CASE WHEN @Status = 'failed' AND COALESCE(status_rank, 0) < 20 THEN @ErrorDetails WHEN @Status IN ('delivered', 'read') THEN NULL ELSE active_error_details END,
                            active_error_data = CASE WHEN @Status = 'failed' AND COALESCE(status_rank, 0) < 20 THEN CAST(@ErrorData AS jsonb) WHEN @Status IN ('delivered', 'read') THEN NULL ELSE active_error_data END,
                            -- Last failure tracking updated on any failure event without polluting active status
                            last_failure_code = CASE WHEN @Status = 'failed' THEN @ErrorCode ELSE last_failure_code END,
                            last_failure_timestamp = CASE WHEN @Status = 'failed' THEN @StatusTimestamp ELSE last_failure_timestamp END,
                            last_failure_reason = CASE WHEN @Status = 'failed' THEN @ErrorTitle ELSE last_failure_reason END,
                            updated_at = NOW()
                        WHERE id = @MessageId;
                        """;

                    await connection.ExecuteAsync(
                        new CommandDefinition(updateMessageStateSql, new
                        {
                            Status = normStatus,
                            StatusRank = statusRank,
                            StatusTimestamp = evt.StatusTimestamp,
                            PhoneNumberId = evt.PhoneNumberId,
                            DisplayPhoneNumber = evt.DisplayPhoneNumber,
                            RecipientPhone = evt.RecipientPhone,
                            ConversationId = evt.ConversationId,
                            ConversationOriginType = evt.ConversationOriginType,
                            ConversationExpiresAt = evt.ConversationExpiresAt,
                            PricingModel = evt.PricingModel,
                            PricingCategory = evt.PricingCategory,
                            PricingBillable = evt.PricingBillable,
                            BizOpaqueCallbackData = evt.BizOpaqueCallbackData,
                            ErrorCode = evt.ErrorCode,
                            ErrorTitle = evt.ErrorTitle,
                            ErrorMessage = evt.ErrorMessage,
                            ErrorDetails = evt.ErrorDetails,
                            ErrorData = evt.ErrorDataJson ?? "null",
                            MessageId = messageId
                        }, transaction: transaction, cancellationToken: cancellationToken));
                }
            }

            // Step 5: Mark the inbox item Processed with lock token fence check
            const string completeInboxSql = """
                UPDATE webhook_inbox
                SET status = 2, -- Processed
                    processed_at = NOW(),
                    lock_token = NULL,
                    locked_by = NULL,
                    locked_until = NULL,
                    last_error = NULL
                WHERE id = @InboxId AND lock_token = @LockToken;
                """;

            var affectedRows = await connection.ExecuteAsync(
                new CommandDefinition(completeInboxSql, new
                {
                    InboxId = item.Id,
                    LockToken = lockToken
                }, transaction: transaction, cancellationToken: cancellationToken));

            if (affectedRows == 0)
            {
                // Lock token lease expired and claimed by another worker; rollback transaction
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task RecordFailureAsync(
        long inboxId,
        Guid lockToken,
        string errorMessage,
        int attemptCount,
        int maxAttempts,
        CancellationToken cancellationToken = default)
    {
        var nextAttempt = attemptCount >= maxAttempts
            ? DateTimeOffset.UtcNow
            : DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2, Math.Min(attemptCount, 6)) * 5); // Exponential backoff

        var newStatus = attemptCount >= maxAttempts ? (short)4 : (short)3; // 4 = DeadLetter, 3 = Failed

        const string sql = """
            UPDATE webhook_inbox
            SET status = @NewStatus,
                attempt_count = @AttemptCount,
                last_error = @LastError,
                next_attempt_at = @NextAttemptAt,
                lock_token = NULL,
                locked_by = NULL,
                locked_until = NULL
            WHERE id = @InboxId AND lock_token = @LockToken;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(
            new CommandDefinition(sql, new
            {
                NewStatus = newStatus,
                AttemptCount = (short)attemptCount,
                LastError = errorMessage,
                NextAttemptAt = nextAttempt,
                InboxId = inboxId,
                LockToken = lockToken
            }, cancellationToken: cancellationToken));
    }
}

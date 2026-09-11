using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using OraiWebhookManager.Application.Helpers;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Models;

namespace OraiWebhookManager.Infrastructure.Persistence.Repositories;

public class TemplateMappingRepository : ITemplateMappingRepository
{
    private readonly string _connectionString;

    public TemplateMappingRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection string is not configured.");
    }

    public async Task<TemplateMappingExecutionResult> CreateOrEnrichMappingAsync(
        CachedWebhookEndpoint endpoint,
        CreateTemplateMappingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(request);

        var normWamid = request.Wamid.Trim();
        var normRecipientId = PhoneNormalizationHelper.NormalizeRecipientId(request.RecipientId);
        var normTemplateName = request.Template!.Name.Trim();
        var normNamespace = string.IsNullOrWhiteSpace(request.Template.Namespace)
            ? null
            : request.Template.Namespace.Trim();
        var normLanguage = request.Template.Language.Trim().ToLowerInvariant();
        var sentAtUtc = request.SentAt!.Value.ToUniversalTime();

        var normBroadcastId = string.IsNullOrWhiteSpace(request.Broadcast?.ExternalId)
            ? null
            : request.Broadcast.ExternalId.Trim();
        var normBroadcastName = string.IsNullOrWhiteSpace(request.Broadcast?.Name)
            ? null
            : request.Broadcast.Name.Trim();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            const string insertMappingSql = """
                INSERT INTO message_template_mappings (
                    id, tenant_id, endpoint_id, wamid, recipient_id,
                    template_name, template_namespace, template_language, sent_at,
                    broadcast_id, broadcast_name, created_at, updated_at
                )
                VALUES (
                    @Id, @TenantId, @EndpointId, @Wamid, @RecipientId,
                    @TemplateName, @TemplateNamespace, @TemplateLanguage, @SentAt,
                    @BroadcastId, @BroadcastName, NOW(), NOW()
                )
                ON CONFLICT (tenant_id, wamid) DO NOTHING
                RETURNING id;
                """;

            var mappingId = Guid.NewGuid();
            var insertedId = await connection.ExecuteScalarAsync<Guid?>(
                new CommandDefinition(insertMappingSql, new
                {
                    Id = mappingId,
                    TenantId = endpoint.TenantId,
                    EndpointId = endpoint.Id,
                    Wamid = normWamid,
                    RecipientId = normRecipientId,
                    TemplateName = normTemplateName,
                    TemplateNamespace = normNamespace,
                    TemplateLanguage = normLanguage,
                    SentAt = sentAtUtc,
                    BroadcastId = normBroadcastId,
                    BroadcastName = normBroadcastName
                }, transaction: transaction, cancellationToken: cancellationToken));

            if (insertedId.HasValue && insertedId.Value != Guid.Empty)
            {
                // Step 2: Atomic upsert into messages table for dashboard enrichment
                const string upsertMessageSql = """
                    INSERT INTO messages (
                        id, tenant_id, endpoint_id, wamid, recipient_phone,
                        template_name, broadcast_id, broadcast_name, created_at, updated_at
                    )
                    VALUES (
                        @NewMsgId, @TenantId, @EndpointId, @Wamid, @RecipientPhone,
                        @TemplateName, @BroadcastId, @BroadcastName, NOW(), NOW()
                    )
                    ON CONFLICT (tenant_id, wamid) DO UPDATE
                    SET recipient_phone = COALESCE(messages.recipient_phone, EXCLUDED.recipient_phone),
                        template_name = COALESCE(messages.template_name, EXCLUDED.template_name),
                        broadcast_id = COALESCE(messages.broadcast_id, EXCLUDED.broadcast_id),
                        broadcast_name = COALESCE(messages.broadcast_name, EXCLUDED.broadcast_name),
                        updated_at = NOW();
                    """;

                await connection.ExecuteAsync(
                    new CommandDefinition(upsertMessageSql, new
                    {
                        NewMsgId = Guid.NewGuid(),
                        TenantId = endpoint.TenantId,
                        EndpointId = endpoint.Id,
                        Wamid = normWamid,
                        RecipientPhone = normRecipientId,
                        TemplateName = normTemplateName,
                        BroadcastId = normBroadcastId,
                        BroadcastName = normBroadcastName
                    }, transaction: transaction, cancellationToken: cancellationToken));

                await transaction.CommitAsync(cancellationToken);

                var createdDto = new TemplateMappingDataDto(
                    Wamid: normWamid,
                    RecipientId: normRecipientId,
                    Template: new TemplateInfoDto(normTemplateName, normNamespace, normLanguage),
                    SentAt: sentAtUtc,
                    Broadcast: normBroadcastId != null || normBroadcastName != null
                        ? new BroadcastInfoDto(normBroadcastId, normBroadcastName)
                        : null
                );

                return new TemplateMappingExecutionResult(TemplateMappingStatus.Created, createdDto);
            }

            // Step 3: Conflict / Existing resolution - query existing mapping
            const string selectMappingSql = """
                SELECT id AS Id,
                       tenant_id AS TenantId,
                       endpoint_id AS EndpointId,
                       wamid AS Wamid,
                       recipient_id AS RecipientId,
                       template_name AS TemplateName,
                       template_namespace AS TemplateNamespace,
                       template_language AS TemplateLanguage,
                       sent_at AS SentAt,
                       broadcast_id AS BroadcastId,
                       broadcast_name AS BroadcastName
                FROM message_template_mappings
                WHERE tenant_id = @TenantId AND wamid = @Wamid
                LIMIT 1;
                """;

            var existing = await connection.QuerySingleOrDefaultAsync<ExistingMappingRow>(
                new CommandDefinition(selectMappingSql, new
                {
                    TenantId = endpoint.TenantId,
                    Wamid = normWamid
                }, transaction: transaction, cancellationToken: cancellationToken));

            if (existing == null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new TemplateMappingExecutionResult(TemplateMappingStatus.Conflict, null, "A different mapping already exists for this message ID.");
            }

            // If mapping exists under a different endpoint of the same tenant, reject conflict
            if (existing.EndpointId != endpoint.Id)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new TemplateMappingExecutionResult(TemplateMappingStatus.Conflict, null, "A mapping for this message ID already exists under a different endpoint in this tenant.");
            }

            // Verify immutable fields
            var existingRecipientNorm = PhoneNormalizationHelper.NormalizeRecipientId(existing.RecipientId);
            var isRecipientEqual = string.Equals(existingRecipientNorm, normRecipientId, StringComparison.OrdinalIgnoreCase);
            var isTemplateNameEqual = string.Equals(existing.TemplateName.Trim(), normTemplateName, StringComparison.OrdinalIgnoreCase);
            var isNamespaceEqual = string.Equals(existing.TemplateNamespace?.Trim() ?? string.Empty, normNamespace ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            var isLanguageEqual = string.Equals(existing.TemplateLanguage.Trim(), normLanguage, StringComparison.OrdinalIgnoreCase);

            // Time comparison: normalize to UTC and compare at microsecond precision (1 microsecond = 10 ticks)
            var existingSentAtUtc = existing.SentAt.ToUniversalTime();
            var isSentAtEqual = (existingSentAtUtc.Ticks / 10) == (sentAtUtc.Ticks / 10);

            if (!isRecipientEqual || !isTemplateNameEqual || !isNamespaceEqual || !isLanguageEqual || !isSentAtEqual)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new TemplateMappingExecutionResult(TemplateMappingStatus.Conflict, null, "A different mapping already exists for this message ID.");
            }

            // Broadcast conflict / enrichment comparison
            var existingBroadcastId = existing.BroadcastId?.Trim();
            var finalBroadcastId = existingBroadcastId;
            var finalBroadcastName = existing.BroadcastName?.Trim();

            if (!string.IsNullOrEmpty(existingBroadcastId) && !string.IsNullOrEmpty(normBroadcastId))
            {
                if (!string.Equals(existingBroadcastId, normBroadcastId, StringComparison.OrdinalIgnoreCase))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return new TemplateMappingExecutionResult(TemplateMappingStatus.Conflict, null, "A different mapping already exists for this message ID.");
                }
            }
            else if (string.IsNullOrEmpty(existingBroadcastId) && !string.IsNullOrEmpty(normBroadcastId))
            {
                // Safe one-way enrichment of broadcast metadata
                finalBroadcastId = normBroadcastId;
                finalBroadcastName = normBroadcastName;

                const string enrichMappingSql = """
                    UPDATE message_template_mappings
                    SET broadcast_id = @BroadcastId,
                        broadcast_name = COALESCE(@BroadcastName, broadcast_name),
                        updated_at = NOW()
                    WHERE id = @Id;
                    """;

                await connection.ExecuteAsync(
                    new CommandDefinition(enrichMappingSql, new
                    {
                        Id = existing.Id,
                        BroadcastId = normBroadcastId,
                        BroadcastName = normBroadcastName
                    }, transaction: transaction, cancellationToken: cancellationToken));

                const string enrichMessageSql = """
                    UPDATE messages
                    SET broadcast_id = COALESCE(broadcast_id, @BroadcastId),
                        broadcast_name = COALESCE(broadcast_name, @BroadcastName),
                        updated_at = NOW()
                    WHERE tenant_id = @TenantId AND wamid = @Wamid;
                    """;

                await connection.ExecuteAsync(
                    new CommandDefinition(enrichMessageSql, new
                    {
                        TenantId = endpoint.TenantId,
                        Wamid = normWamid,
                        BroadcastId = normBroadcastId,
                        BroadcastName = normBroadcastName
                    }, transaction: transaction, cancellationToken: cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);

            var existingDto = new TemplateMappingDataDto(
                Wamid: existing.Wamid,
                RecipientId: existingRecipientNorm,
                Template: new TemplateInfoDto(existing.TemplateName, existing.TemplateNamespace, existing.TemplateLanguage),
                SentAt: existingSentAtUtc,
                Broadcast: finalBroadcastId != null || finalBroadcastName != null
                    ? new BroadcastInfoDto(finalBroadcastId, finalBroadcastName)
                    : null
            );

            return new TemplateMappingExecutionResult(TemplateMappingStatus.ExistingMatch, existingDto);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private sealed class ExistingMappingRow
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid EndpointId { get; set; }
        public string Wamid { get; set; } = string.Empty;
        public string RecipientId { get; set; } = string.Empty;
        public string TemplateName { get; set; } = string.Empty;
        public string? TemplateNamespace { get; set; }
        public string TemplateLanguage { get; set; } = string.Empty;
        public DateTimeOffset SentAt { get; set; }
        public string? BroadcastId { get; set; }
        public string? BroadcastName { get; set; }
    }
}

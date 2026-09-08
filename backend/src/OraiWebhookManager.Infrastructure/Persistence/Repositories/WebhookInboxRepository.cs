using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Models;
using OraiWebhookManager.Domain.Enums;

namespace OraiWebhookManager.Infrastructure.Persistence.Repositories;

public class WebhookInboxRepository : IWebhookInboxRepository
{
    private readonly string _connectionString;

    public WebhookInboxRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection string is not configured.");
    }

    public async Task<long> EnqueueAsync(
        Guid tenantId,
        Guid endpointId,
        string payloadRaw,
        string headersJson,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO webhook_inbox (
                tenant_id, endpoint_id, payload_raw, headers, ip_address,
                status, attempt_count, next_attempt_at, created_at
            )
            VALUES (
                @TenantId, @EndpointId, CAST(@PayloadRaw AS jsonb), CAST(@HeadersJson AS jsonb), @IpAddress,
                0, 0, NOW(), NOW()
            )
            RETURNING id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var id = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(sql, new
            {
                TenantId = tenantId,
                EndpointId = endpointId,
                PayloadRaw = payloadRaw,
                HeadersJson = headersJson,
                IpAddress = ipAddress
            }, cancellationToken: cancellationToken));

        return id;
    }

    public async Task<PubSubInboxEnqueueResult> EnqueueFromPubSubAsync(
        PubSubWebhookEnvelope envelope,
        string pubsubMessageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (string.IsNullOrWhiteSpace(pubsubMessageId))
        {
            throw new ArgumentException("PubSub message ID cannot be empty or whitespace.", nameof(pubsubMessageId));
        }

        if (pubsubMessageId.Length > 128)
        {
            throw new ArgumentException("PubSub message ID exceeds maximum length of 128 characters.", nameof(pubsubMessageId));
        }

        if (envelope.CorrelationId == Guid.Empty)
        {
            throw new ArgumentException("CorrelationId cannot be empty.", nameof(envelope));
        }

        if (envelope.TenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId cannot be empty.", nameof(envelope));
        }

        if (envelope.EndpointId == Guid.Empty)
        {
            throw new ArgumentException("EndpointId cannot be empty.", nameof(envelope));
        }

        if (string.IsNullOrWhiteSpace(envelope.PayloadRaw))
        {
            throw new ArgumentException("PayloadRaw cannot be empty or whitespace.", nameof(envelope));
        }

        // Validate JSON payload
        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(envelope.PayloadRaw);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new ArgumentException("PayloadRaw must be valid JSON.", nameof(envelope), ex);
        }

        var headersJson = System.Text.Json.JsonSerializer.Serialize(envelope.FilteredHeaders);
        var receivedAtUtc = envelope.ReceivedAtUtc.ToUniversalTime();

        const string insertSql = """
            INSERT INTO webhook_inbox (
                tenant_id, endpoint_id, payload_raw, headers, ip_address,
                status, attempt_count, next_attempt_at, created_at, pubsub_message_id
            )
            VALUES (
                @TenantId, @EndpointId, CAST(@PayloadRaw AS jsonb), CAST(@HeadersJson AS jsonb), @IpAddress,
                0, 0, @ReceivedAtUtc, @ReceivedAtUtc, @PubSubMessageId
            )
            ON CONFLICT (pubsub_message_id) WHERE pubsub_message_id IS NOT NULL DO NOTHING
            RETURNING id;
            """;

        const string selectSql = """
            SELECT id
            FROM webhook_inbox
            WHERE pubsub_message_id = @PubSubMessageId
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Step 1: Attempt insert
        var insertedId = await connection.ExecuteScalarAsync<long?>(
            new CommandDefinition(insertSql, new
            {
                TenantId = envelope.TenantId,
                EndpointId = envelope.EndpointId,
                PayloadRaw = envelope.PayloadRaw,
                HeadersJson = headersJson,
                IpAddress = envelope.SourceIp,
                ReceivedAtUtc = receivedAtUtc,
                PubSubMessageId = pubsubMessageId
            }, cancellationToken: cancellationToken));

        if (insertedId.HasValue && insertedId.Value > 0)
        {
            return new PubSubInboxEnqueueResult(PubSubInboxEnqueueStatus.Created, insertedId.Value);
        }

        // Step 2: Handle conflict / duplicate - query existing row on same connection
        var existingId = await connection.ExecuteScalarAsync<long?>(
            new CommandDefinition(selectSql, new { PubSubMessageId = pubsubMessageId }, cancellationToken: cancellationToken));

        if (existingId.HasValue && existingId.Value > 0)
        {
            return new PubSubInboxEnqueueResult(PubSubInboxEnqueueStatus.AlreadyExists, existingId.Value);
        }

        // Step 3: Bounded retry if concurrent transaction has not yet committed
        int[] retryDelaysMs = [25, 50, 100, 200];
        foreach (var delay in retryDelaysMs)
        {
            await Task.Delay(delay, cancellationToken);

            existingId = await connection.ExecuteScalarAsync<long?>(
                new CommandDefinition(selectSql, new { PubSubMessageId = pubsubMessageId }, cancellationToken: cancellationToken));

            if (existingId.HasValue && existingId.Value > 0)
            {
                return new PubSubInboxEnqueueResult(PubSubInboxEnqueueStatus.AlreadyExists, existingId.Value);
            }
        }

        throw new InvalidOperationException(
            $"Duplicate Pub/Sub message ID '{pubsubMessageId}' was rejected by unique constraint, but existing inbox record could not be resolved within timeout.");
    }

    public async Task<CachedWebhookEndpoint?> GetEndpointByHashAsync(byte[] keyHash, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id AS Id,
                   tenant_id AS TenantId,
                   name AS Name,
                   key_prefix AS KeyPrefix,
                   key_hash AS KeyHash,
                   status AS Status,
                   last_received_at AS LastReceivedAt,
                   revoked_at AS RevokedAt,
                   created_at AS CreatedAt,
                   updated_at AS UpdatedAt
            FROM webhook_endpoints
            WHERE key_hash = @KeyHash
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<WebhookEndpointRow>(
            new CommandDefinition(sql, new { KeyHash = keyHash }, cancellationToken: cancellationToken));

        if (row == null) return null;

        var status = Enum.TryParse<WebhookEndpointStatus>(row.Status, true, out var parsedStatus)
            ? parsedStatus
            : WebhookEndpointStatus.Suspended;

        return new CachedWebhookEndpoint(
            Id: row.Id,
            TenantId: row.TenantId,
            Name: row.Name,
            KeyPrefix: row.KeyPrefix,
            KeyHash: row.KeyHash,
            Status: status
        );
    }

    public sealed class WebhookEndpointRow
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string KeyPrefix { get; set; } = string.Empty;
        public byte[] KeyHash { get; set; } = Array.Empty<byte>();
        public string Status { get; set; } = string.Empty;
        public DateTimeOffset? LastReceivedAt { get; set; }
        public DateTimeOffset? RevokedAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }
}

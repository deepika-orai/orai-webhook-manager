using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using OraiWebhookManager.Application.Interfaces;
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

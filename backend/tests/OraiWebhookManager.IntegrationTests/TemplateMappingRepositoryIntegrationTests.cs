using System.Collections.Concurrent;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Npgsql;
using OraiWebhookManager.Application.Helpers;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Models;
using OraiWebhookManager.Domain.Entities;
using OraiWebhookManager.Domain.Enums;
using OraiWebhookManager.Infrastructure.Persistence.Repositories;
using Xunit;

namespace OraiWebhookManager.IntegrationTests;

public class TemplateMappingRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly string _schemaName = $"test_mapping_{Guid.NewGuid():N}";
    private string _isolatedConnectionString = string.Empty;
    private ITemplateMappingRepository _repository = null!;

    public async Task InitializeAsync()
    {
        var rawConnection = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(rawConnection))
        {
            throw new InvalidOperationException(
                "TEST_POSTGRES_CONNECTION environment variable is required to execute Phase 1 PostgreSQL integration tests. " +
                "No fallback or mock database is permitted.");
        }

        var builder = new NpgsqlConnectionStringBuilder(rawConnection)
        {
            Timeout = 5,
            CommandTimeout = 10
        };

        // Safety Guard 1: Host must be local
        var host = builder.Host?.Trim().ToLowerInvariant();
        if (host != "localhost" && host != "127.0.0.1" && host != "::1")
        {
            throw new InvalidOperationException(
                $"Host '{host}' is rejected. Integration tests are strictly restricted to local hosts (localhost/127.0.0.1/::1).");
        }

        // Safety Guard 2: Database must be an isolated test database
        var db = builder.Database?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(db) || (!db.Equals("orai_webhooks_phase1_test") && !db.EndsWith("_test")))
        {
            throw new InvalidOperationException(
                $"Database '{db}' is rejected. Integration tests must target a dedicated test database (e.g. 'orai_webhooks_phase1_test').");
        }

        builder.SearchPath = _schemaName;
        _isolatedConnectionString = builder.ConnectionString;

        // Initialize isolated test schema and tables
        await using var initConn = new NpgsqlConnection(_isolatedConnectionString);
        await initConn.OpenAsync();

        var initSql = $"""
            CREATE SCHEMA IF NOT EXISTS {_schemaName};
            SET search_path TO {_schemaName}, public;

            CREATE TABLE IF NOT EXISTS tenants (
                id uuid PRIMARY KEY,
                name character varying(255) NOT NULL,
                status smallint NOT NULL DEFAULT 1,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL
            );

            CREATE TABLE IF NOT EXISTS webhook_endpoints (
                id uuid PRIMARY KEY,
                tenant_id uuid NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                name character varying(255) NOT NULL,
                key_prefix character varying(16) NOT NULL,
                key_hash bytea NOT NULL,
                status smallint NOT NULL DEFAULT 1,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL
            );

            CREATE TABLE IF NOT EXISTS message_template_mappings (
                id uuid PRIMARY KEY,
                tenant_id uuid NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                endpoint_id uuid NOT NULL REFERENCES webhook_endpoints(id) ON DELETE CASCADE,
                wamid character varying(255) NOT NULL,
                recipient_id character varying(64) NOT NULL,
                template_name character varying(128) NOT NULL,
                template_namespace character varying(128),
                template_language character varying(32) NOT NULL,
                sent_at timestamp with time zone NOT NULL,
                broadcast_id character varying(128),
                broadcast_name character varying(255),
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ix_template_mappings_tenant_wamid
            ON message_template_mappings (tenant_id, wamid);

            CREATE INDEX IF NOT EXISTS ix_template_mappings_endpoint_created
            ON message_template_mappings (endpoint_id, created_at);

            CREATE TABLE IF NOT EXISTS messages (
                id uuid PRIMARY KEY,
                tenant_id uuid NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                endpoint_id uuid NOT NULL REFERENCES webhook_endpoints(id) ON DELETE CASCADE,
                wamid character varying(255) NOT NULL,
                phone_number_id character varying(64),
                display_phone_number character varying(64),
                recipient_phone character varying(64),
                current_status character varying(32),
                status_rank smallint,
                last_status_timestamp timestamp with time zone,
                conversation_id character varying(128),
                conversation_origin_type character varying(64),
                conversation_expires_at timestamp with time zone,
                pricing_model character varying(32),
                pricing_category character varying(64),
                pricing_billable boolean,
                active_error_code integer,
                active_error_title character varying(255),
                active_error_message text,
                active_error_details text,
                last_failure_code integer,
                last_failure_timestamp timestamp with time zone,
                last_failure_reason text,
                biz_opaque_callback_data text,
                broadcast_id character varying(128),
                broadcast_name character varying(255),
                template_name character varying(128),
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ix_messages_tenant_wamid
            ON messages (tenant_id, wamid);
            """;

        await initConn.ExecuteAsync(initSql);

        var inMemorySettings = new Dictionary<string, string?>
        {
            { "ConnectionStrings:DefaultConnection", _isolatedConnectionString }
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        _repository = new TemplateMappingRepository(configuration);
    }

    public async Task DisposeAsync()
    {
        if (!string.IsNullOrEmpty(_isolatedConnectionString))
        {
            try
            {
                await using var conn = new NpgsqlConnection(_isolatedConnectionString);
                await conn.OpenAsync();
                await conn.ExecuteAsync($"DROP SCHEMA IF EXISTS {_schemaName} CASCADE;");
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    private async Task<(Guid TenantId, CachedWebhookEndpoint Endpoint)> SeedTenantAndEndpointAsync(string name = "Primary Line")
    {
        var tenantId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();
        var keyGen = new Infrastructure.Services.WebhookKeyService().GenerateKey();

        await using var conn = new NpgsqlConnection(_isolatedConnectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync(
            "INSERT INTO tenants (id, name, status, created_at, updated_at) VALUES (@Id, @Name, 1, NOW(), NOW());",
            new { Id = tenantId, Name = "Test Tenant" });

        await conn.ExecuteAsync(
            """
            INSERT INTO webhook_endpoints (id, tenant_id, name, key_prefix, key_hash, status, created_at, updated_at)
            VALUES (@Id, @TenantId, @Name, @Prefix, @Hash, 1, NOW(), NOW());
            """,
            new { Id = endpointId, TenantId = tenantId, Name = name, Prefix = keyGen.KeyPrefix, Hash = keyGen.KeyHash });

        var cachedEndpoint = new CachedWebhookEndpoint(
            Id: endpointId,
            TenantId: tenantId,
            Name: name,
            KeyPrefix: keyGen.KeyPrefix,
            KeyHash: keyGen.KeyHash,
            Status: WebhookEndpointStatus.Active
        );

        return (tenantId, cachedEndpoint);
    }

    [Fact]
    public async Task CreateOrEnrichMappingAsync_FirstInsert_CreatesMappingAndMessagesRows()
    {
        var (tenantId, endpoint) = await SeedTenantAndEndpointAsync();
        const string wamid = "wamid.pg_test_001";
        var sentAt = DateTimeOffset.UtcNow;

        var request = new CreateTemplateMappingRequest
        {
            Wamid = wamid,
            RecipientId = "+919644391241",
            Template = new TemplateInfoRequest
            {
                Name = "order_confirmation",
                Namespace = "ns_ecommerce",
                Language = "en_US"
            },
            SentAt = sentAt,
            Broadcast = new BroadcastInfoRequest
            {
                ExternalId = "BC_SEPT_01",
                Name = "September Promo"
            }
        };

        var result = await _repository.CreateOrEnrichMappingAsync(endpoint, request);

        result.Status.Should().Be(TemplateMappingStatus.Created);
        result.Data.Should().NotBeNull();
        result.Data!.Wamid.Should().Be(wamid);
        result.Data.RecipientId.Should().Be("919644391241");

        // Verify direct PostgreSQL rows
        await using var conn = new NpgsqlConnection(_isolatedConnectionString);
        await conn.OpenAsync();

        var mappingCount = await conn.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM message_template_mappings WHERE tenant_id = @TenantId AND wamid = @Wamid;",
            new { TenantId = tenantId, Wamid = wamid });
        mappingCount.Should().Be(1);

        var msg = await conn.QuerySingleOrDefaultAsync<MessageTestRow>(
            """
            SELECT tenant_id AS "TenantId",
                   endpoint_id AS "EndpointId",
                   wamid AS "Wamid",
                   recipient_phone AS "RecipientPhone",
                   template_name AS "TemplateName",
                   broadcast_id AS "BroadcastId",
                   broadcast_name AS "BroadcastName",
                   current_status AS "CurrentStatus",
                   status_rank AS "StatusRank",
                   last_status_timestamp AS "LastStatusTimestamp"
            FROM messages
            WHERE tenant_id = @TenantId AND wamid = @Wamid;
            """,
            new { TenantId = tenantId, Wamid = wamid });

        msg.Should().NotBeNull();
        msg!.TenantId.Should().Be(tenantId);
        msg.EndpointId.Should().Be(endpoint.Id);
        msg.Wamid.Should().Be(wamid);
        msg.RecipientPhone.Should().Be("919644391241");
        msg.TemplateName.Should().Be("order_confirmation");
        msg.BroadcastId.Should().Be("BC_SEPT_01");
        msg.CurrentStatus.Should().BeNull();
        msg.StatusRank.Should().BeNull();
        msg.LastStatusTimestamp.Should().BeNull();
    }

    [Fact]
    public async Task CreateOrEnrichMappingAsync_IdenticalRetry_ReturnsExistingMatchWithCreatedFalse()
    {
        var (tenantId, endpoint) = await SeedTenantAndEndpointAsync();
        const string wamid = "wamid.pg_retry_001";
        var sentAt = DateTimeOffset.UtcNow;

        var request = new CreateTemplateMappingRequest
        {
            Wamid = wamid,
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest { Name = "invoice_ready", Language = "en" },
            SentAt = sentAt
        };

        var result1 = await _repository.CreateOrEnrichMappingAsync(endpoint, request);
        result1.Status.Should().Be(TemplateMappingStatus.Created);

        var result2 = await _repository.CreateOrEnrichMappingAsync(endpoint, request);
        result2.Status.Should().Be(TemplateMappingStatus.ExistingMatch);
        result2.Data!.Wamid.Should().Be(wamid);
    }

    [Fact]
    public async Task CreateOrEnrichMappingAsync_ConflictingTemplate_ReturnsConflict()
    {
        var (tenantId, endpoint) = await SeedTenantAndEndpointAsync();
        const string wamid = "wamid.pg_conflict_001";
        var sentAt = DateTimeOffset.UtcNow;

        var req1 = new CreateTemplateMappingRequest
        {
            Wamid = wamid,
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest { Name = "template_A", Language = "en" },
            SentAt = sentAt
        };
        await _repository.CreateOrEnrichMappingAsync(endpoint, req1);

        var req2 = new CreateTemplateMappingRequest
        {
            Wamid = wamid,
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest { Name = "template_B_DIFFERENT", Language = "en" },
            SentAt = sentAt
        };

        var result2 = await _repository.CreateOrEnrichMappingAsync(endpoint, req2);
        result2.Status.Should().Be(TemplateMappingStatus.Conflict);
        result2.ErrorMessage.Should().Contain("A different mapping already exists for this message ID.");
    }

    [Fact]
    public async Task CreateOrEnrichMappingAsync_SameSentAtDifferentTimezone_ReturnsExistingMatch()
    {
        var (tenantId, endpoint) = await SeedTenantAndEndpointAsync();
        const string wamid = "wamid.pg_tz_001";
        var utcSentAt = new DateTimeOffset(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);
        var istSentAt = new DateTimeOffset(2026, 9, 9, 15, 30, 0, TimeSpan.FromHours(5.5)); // Exact same instant

        var req1 = new CreateTemplateMappingRequest
        {
            Wamid = wamid,
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest { Name = "tz_test", Language = "en" },
            SentAt = utcSentAt
        };
        var result1 = await _repository.CreateOrEnrichMappingAsync(endpoint, req1);
        result1.Status.Should().Be(TemplateMappingStatus.Created);

        var req2 = new CreateTemplateMappingRequest
        {
            Wamid = wamid,
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest { Name = "tz_test", Language = "en" },
            SentAt = istSentAt
        };
        var result2 = await _repository.CreateOrEnrichMappingAsync(endpoint, req2);
        result2.Status.Should().Be(TemplateMappingStatus.ExistingMatch);
    }

    [Fact]
    public async Task CreateOrEnrichMappingAsync_ArrivalOrder_MappingFirst_StatusLater_PreservesTemplateMetadata()
    {
        var (tenantId, endpoint) = await SeedTenantAndEndpointAsync();
        const string wamid = "wamid.pg_order_map_first_001";
        var sentAt = DateTimeOffset.UtcNow;

        var request = new CreateTemplateMappingRequest
        {
            Wamid = wamid,
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest { Name = "order_track", Language = "en" },
            SentAt = sentAt,
            Broadcast = new BroadcastInfoRequest { ExternalId = "BC_1", Name = "Order Tracking" }
        };

        // 1. Mapping first
        await _repository.CreateOrEnrichMappingAsync(endpoint, request);

        // 2. Webhook status processor updates status later
        await using var conn = new NpgsqlConnection(_isolatedConnectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync(
            """
            UPDATE messages
            SET current_status = 'delivered',
                status_rank = 20,
                last_status_timestamp = NOW(),
                updated_at = NOW()
            WHERE tenant_id = @TenantId AND wamid = @Wamid;
            """,
            new { TenantId = tenantId, Wamid = wamid });

        var msg = await conn.QuerySingleAsync<MessageTestRow>(
            """
            SELECT tenant_id AS "TenantId",
                   endpoint_id AS "EndpointId",
                   wamid AS "Wamid",
                   recipient_phone AS "RecipientPhone",
                   template_name AS "TemplateName",
                   broadcast_id AS "BroadcastId",
                   broadcast_name AS "BroadcastName",
                   current_status AS "CurrentStatus",
                   status_rank AS "StatusRank",
                   last_status_timestamp AS "LastStatusTimestamp"
            FROM messages
            WHERE tenant_id = @TenantId AND wamid = @Wamid;
            """,
            new { TenantId = tenantId, Wamid = wamid });

        msg.CurrentStatus.Should().Be("delivered");
        msg.StatusRank.Should().Be(20);
        msg.TemplateName.Should().Be("order_track");
        msg.BroadcastName.Should().Be("Order Tracking");
    }

    [Fact]
    public async Task CreateOrEnrichMappingAsync_ArrivalOrder_StatusFirst_MappingLater_PreservesStatusAndRank()
    {
        var (tenantId, endpoint) = await SeedTenantAndEndpointAsync();
        const string wamid = "wamid.pg_order_status_first_001";
        var deliveredAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        // 1. Webhook arrives first -> creates message row with status
        await using var conn = new NpgsqlConnection(_isolatedConnectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync(
            """
            INSERT INTO messages (
                id, tenant_id, endpoint_id, wamid, current_status, status_rank,
                last_status_timestamp, created_at, updated_at
            )
            VALUES (
                @Id, @TenantId, @EndpointId, @Wamid, 'read', 30, @DeliveredAt, NOW(), NOW()
            );
            """,
            new { Id = Guid.NewGuid(), TenantId = tenantId, EndpointId = endpoint.Id, Wamid = wamid, DeliveredAt = deliveredAt });

        // 2. Mapping arrives later
        var mapReq = new CreateTemplateMappingRequest
        {
            Wamid = wamid,
            RecipientId = "+919644391241",
            Template = new TemplateInfoRequest { Name = "delivery_alert", Language = "en" },
            SentAt = deliveredAt.AddMinutes(-2),
            Broadcast = new BroadcastInfoRequest { ExternalId = "BC_DELIV", Name = "Delivery Campaign" }
        };

        var result = await _repository.CreateOrEnrichMappingAsync(endpoint, mapReq);
        result.Status.Should().Be(TemplateMappingStatus.Created);

        // Verify status is preserved and template is enriched
        var msg = await conn.QuerySingleAsync<MessageTestRow>(
            """
            SELECT tenant_id AS "TenantId",
                   endpoint_id AS "EndpointId",
                   wamid AS "Wamid",
                   recipient_phone AS "RecipientPhone",
                   template_name AS "TemplateName",
                   broadcast_id AS "BroadcastId",
                   broadcast_name AS "BroadcastName",
                   current_status AS "CurrentStatus",
                   status_rank AS "StatusRank",
                   last_status_timestamp AS "LastStatusTimestamp"
            FROM messages
            WHERE tenant_id = @TenantId AND wamid = @Wamid;
            """,
            new { TenantId = tenantId, Wamid = wamid });

        msg.CurrentStatus.Should().Be("read");
        msg.StatusRank.Should().Be(30);
        msg.TemplateName.Should().Be("delivery_alert");
        msg.BroadcastName.Should().Be("Delivery Campaign");
        msg.RecipientPhone.Should().Be("919644391241");
    }

    [Fact]
    public async Task CreateOrEnrichMappingAsync_SameWamidDifferentEndpointsSameTenant_ReturnsConflict()
    {
        var (tenantId, endpoint1) = await SeedTenantAndEndpointAsync("Line 1");
        var (_, endpoint2) = await SeedTenantAndEndpointAsync("Line 2");

        // Force endpoint2 under the same tenant
        await using var conn = new NpgsqlConnection(_isolatedConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE webhook_endpoints SET tenant_id = @TenantId WHERE id = @Id;",
            new { TenantId = tenantId, Id = endpoint2.Id });

        var sameTenantEndpoint2 = new CachedWebhookEndpoint(
            Id: endpoint2.Id,
            TenantId: tenantId,
            Name: endpoint2.Name,
            KeyPrefix: endpoint2.KeyPrefix,
            KeyHash: endpoint2.KeyHash,
            Status: WebhookEndpointStatus.Active
        );

        const string wamid = "wamid.pg_same_tenant_multi_ep_001";
        var sentAt = DateTimeOffset.UtcNow;
        var request = new CreateTemplateMappingRequest
        {
            Wamid = wamid,
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest { Name = "test", Language = "en" },
            SentAt = sentAt
        };

        var res1 = await _repository.CreateOrEnrichMappingAsync(endpoint1, request);
        res1.Status.Should().Be(TemplateMappingStatus.Created);

        var res2 = await _repository.CreateOrEnrichMappingAsync(sameTenantEndpoint2, request);
        res2.Status.Should().Be(TemplateMappingStatus.Conflict);
        res2.ErrorMessage.Should().Contain("A mapping for this message ID already exists under a different endpoint in this tenant.");
    }

    [Fact]
    public async Task CreateOrEnrichMappingAsync_SameWamidDifferentTenants_BothSucceed()
    {
        var (_, endpoint1) = await SeedTenantAndEndpointAsync("Tenant 1 Line");
        var (_, endpoint2) = await SeedTenantAndEndpointAsync("Tenant 2 Line");

        const string wamid = "wamid.pg_cross_tenant_wamid_001";
        var sentAt = DateTimeOffset.UtcNow;
        var request = new CreateTemplateMappingRequest
        {
            Wamid = wamid,
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest { Name = "test", Language = "en" },
            SentAt = sentAt
        };

        var res1 = await _repository.CreateOrEnrichMappingAsync(endpoint1, request);
        res1.Status.Should().Be(TemplateMappingStatus.Created);

        var res2 = await _repository.CreateOrEnrichMappingAsync(endpoint2, request);
        res2.Status.Should().Be(TemplateMappingStatus.Created);
    }

    [Fact]
    public async Task CreateOrEnrichMappingAsync_ConcurrentIdenticalRequests_ProducesExactlyOneInsert()
    {
        var (tenantId, endpoint) = await SeedTenantAndEndpointAsync();
        const string wamid = "wamid.pg_concurrent_identical_001";
        var sentAt = DateTimeOffset.UtcNow;

        var request = new CreateTemplateMappingRequest
        {
            Wamid = wamid,
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest { Name = "concurrent_blast", Language = "en" },
            SentAt = sentAt
        };

        var task1 = _repository.CreateOrEnrichMappingAsync(endpoint, request);
        var task2 = _repository.CreateOrEnrichMappingAsync(endpoint, request);

        var results = await Task.WhenAll(task1, task2);
        var statuses = results.Select(r => r.Status).ToList();

        statuses.Should().Contain(TemplateMappingStatus.Created);
        statuses.Should().Contain(TemplateMappingStatus.ExistingMatch);

        await using var conn = new NpgsqlConnection(_isolatedConnectionString);
        await conn.OpenAsync();
        var mappingCount = await conn.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM message_template_mappings WHERE tenant_id = @TenantId AND wamid = @Wamid;",
            new { TenantId = tenantId, Wamid = wamid });
        mappingCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateOrEnrichMappingAsync_ConcurrentConflictingRequests_ProducesOneSuccessAndOneConflict()
    {
        var (tenantId, endpoint) = await SeedTenantAndEndpointAsync();
        const string wamid = "wamid.pg_concurrent_conflict_001";
        var sentAt = DateTimeOffset.UtcNow;

        var req1 = new CreateTemplateMappingRequest
        {
            Wamid = wamid,
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest { Name = "template_AAA", Language = "en" },
            SentAt = sentAt
        };

        var req2 = new CreateTemplateMappingRequest
        {
            Wamid = wamid,
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest { Name = "template_BBB", Language = "en" },
            SentAt = sentAt
        };

        var task1 = _repository.CreateOrEnrichMappingAsync(endpoint, req1);
        var task2 = _repository.CreateOrEnrichMappingAsync(endpoint, req2);

        var results = await Task.WhenAll(task1, task2);
        var statuses = results.Select(r => r.Status).ToList();

        statuses.Should().Contain(TemplateMappingStatus.Created);
        statuses.Should().Contain(TemplateMappingStatus.Conflict);

        await using var conn = new NpgsqlConnection(_isolatedConnectionString);
        await conn.OpenAsync();
        var mappingCount = await conn.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM message_template_mappings WHERE tenant_id = @TenantId AND wamid = @Wamid;",
            new { TenantId = tenantId, Wamid = wamid });
        mappingCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateOrEnrichMappingAsync_WhenMessagesEnrichmentFails_RollsBackMappingInsert()
    {
        var (tenantId, endpoint) = await SeedTenantAndEndpointAsync();
        const string wamid = "wamid.pg_rollback_test_001";
        var sentAt = DateTimeOffset.UtcNow;

        // Create a foreign key trigger or constraint on messages table to deliberately force failure
        await using var conn = new NpgsqlConnection(_isolatedConnectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync(
            $"""
            CREATE OR REPLACE FUNCTION {_schemaName}.fail_messages_insert()
            RETURNS TRIGGER AS $$
            BEGIN
                IF NEW.wamid = '{wamid}' THEN
                    RAISE EXCEPTION 'Simulated deliberate database failure on messages table';
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER trg_fail_messages
            BEFORE INSERT ON messages
            FOR EACH ROW
            EXECUTE FUNCTION {_schemaName}.fail_messages_insert();
            """);

        var request = new CreateTemplateMappingRequest
        {
            Wamid = wamid,
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest { Name = "rollback_template", Language = "en" },
            SentAt = sentAt
        };

        Func<Task> act = async () => await _repository.CreateOrEnrichMappingAsync(endpoint, request);
        await act.Should().ThrowAsync<PostgresException>()
            .WithMessage("*Simulated deliberate database failure*");

        // Verify that NO row was committed to message_template_mappings (full rollback)
        var mappingCount = await conn.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM message_template_mappings WHERE tenant_id = @TenantId AND wamid = @Wamid;",
            new { TenantId = tenantId, Wamid = wamid });
        mappingCount.Should().Be(0);

        var messagesCount = await conn.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM messages WHERE tenant_id = @TenantId AND wamid = @Wamid;",
            new { TenantId = tenantId, Wamid = wamid });
        messagesCount.Should().Be(0);
    }

    private sealed class MessageTestRow
    {
        public Guid TenantId { get; set; }
        public Guid EndpointId { get; set; }
        public string Wamid { get; set; } = string.Empty;
        public string? RecipientPhone { get; set; }
        public string? TemplateName { get; set; }
        public string? BroadcastId { get; set; }
        public string? BroadcastName { get; set; }
        public string? CurrentStatus { get; set; }
        public short? StatusRank { get; set; }
        public DateTime? LastStatusTimestamp { get; set; }
    }
}

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OraiWebhookManager.Application.Helpers;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Models;
using OraiWebhookManager.Domain.Entities;
using OraiWebhookManager.Domain.Enums;
using OraiWebhookManager.Domain.Rules;
using Xunit;

namespace OraiWebhookManager.IntegrationTests;

public class TemplateMappingEndpointIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly FakeWebhookInboxRepository _fakeInboxRepo = new();
    private readonly InMemoryStateEngineMappingRepository _mappingRepo = new();
    private readonly IWebhookKeyService _keyService = new Infrastructure.Services.WebhookKeyService();

    public TemplateMappingEndpointIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateCustomClient()
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IWebhookInboxRepository>(_fakeInboxRepo);
                services.AddSingleton<ITemplateMappingRepository>(_mappingRepo);
            });
        }).CreateClient();
    }

    [Fact]
    public async Task CreateMapping_InvalidKey_ReturnsSafe404()
    {
        var client = CreateCustomClient();

        var request = new CreateTemplateMappingRequest
        {
            Wamid = "wamid.HBgMOTE5NjQ0MzkxMjQxFQIAERgSRUVDMTYxMkY1MzJBQUNGOEVDAA==",
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest
            {
                Name = "opd_visit",
                Language = "en"
            },
            SentAt = DateTimeOffset.UtcNow
        };

        var response = await client.PostAsJsonAsync("/api/messages/whk_live_invalidkey1234567890/mappings", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<TemplateMappingErrorResponse>();
        body.Should().NotBeNull();
        body!.Success.Should().BeFalse();
        body.Message.Should().Be("Webhook endpoint not found or inactive.");
    }

    [Fact]
    public async Task CreateMapping_SuspendedOrRevokedKey_ReturnsSafe404()
    {
        var client = CreateCustomClient();
        var keyGen = _keyService.GenerateKey();
        var tenantId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();

        _fakeInboxRepo.SeedEndpoint(new CachedWebhookEndpoint(
            Id: endpointId,
            TenantId: tenantId,
            Name: "Suspended Line",
            KeyPrefix: keyGen.KeyPrefix,
            KeyHash: keyGen.KeyHash,
            Status: WebhookEndpointStatus.Suspended
        ));

        var request = new CreateTemplateMappingRequest
        {
            Wamid = "wamid.HBgMOTE5NjQ0MzkxMjQxFQIAERgSRUVDMTYxMkY1MzJBQUNGOEVDAA==",
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest
            {
                Name = "opd_visit",
                Language = "en"
            },
            SentAt = DateTimeOffset.UtcNow
        };

        var response = await client.PostAsJsonAsync($"/api/messages/{keyGen.PlainKey}/mappings", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateMapping_MissingFields_Returns400()
    {
        var client = CreateCustomClient();
        var keyGen = _keyService.GenerateKey();
        var tenantId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();

        _fakeInboxRepo.SeedEndpoint(new CachedWebhookEndpoint(
            Id: endpointId,
            TenantId: tenantId,
            Name: "Active Line",
            KeyPrefix: keyGen.KeyPrefix,
            KeyHash: keyGen.KeyHash,
            Status: WebhookEndpointStatus.Active
        ));

        // Missing template object
        var request = new
        {
            wamid = "wamid.12345",
            recipientId = "919644391241",
            sentAt = DateTimeOffset.UtcNow
        };

        var response = await client.PostAsJsonAsync($"/api/messages/{keyGen.PlainKey}/mappings", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateMapping_ValidNewRequest_Returns201Created()
    {
        var client = CreateCustomClient();
        var keyGen = _keyService.GenerateKey();
        var tenantId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();

        _fakeInboxRepo.SeedEndpoint(new CachedWebhookEndpoint(
            Id: endpointId,
            TenantId: tenantId,
            Name: "Primary WhatsApp Line",
            KeyPrefix: keyGen.KeyPrefix,
            KeyHash: keyGen.KeyHash,
            Status: WebhookEndpointStatus.Active
        ));

        var sentAt = DateTimeOffset.Parse("2026-09-09T12:45:30Z");
        var request = new CreateTemplateMappingRequest
        {
            Wamid = "wamid.HBgMOTE5NjQ0MzkxMjQxFQIAERgSRUVDMTYxMkY1MzJBQUNGOEVDAA==",
            RecipientId = "+919644391241",
            Template = new TemplateInfoRequest
            {
                Name = "opd_visit",
                Namespace = "24d14f8f_dd39_4d1d_8795_b55eb3d249c7",
                Language = "en"
            },
            SentAt = sentAt,
            Broadcast = new BroadcastInfoRequest
            {
                ExternalId = "BROADCAST_001",
                Name = "OPD Campaign"
            }
        };

        var response = await client.PostAsJsonAsync($"/api/messages/{keyGen.PlainKey}/mappings", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<TemplateMappingResponse>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Created.Should().BeTrue();
        result.Data.Wamid.Should().Be("wamid.HBgMOTE5NjQ0MzkxMjQxFQIAERgSRUVDMTYxMkY1MzJBQUNGOEVDAA==");
        result.Data.RecipientId.Should().Be("919644391241");
        result.Data.Template.Name.Should().Be("opd_visit");
        result.Data.Template.Namespace.Should().Be("24d14f8f_dd39_4d1d_8795_b55eb3d249c7");
        result.Data.Template.Language.Should().Be("en");
        result.Data.Broadcast.Should().NotBeNull();
        result.Data.Broadcast!.ExternalId.Should().Be("BROADCAST_001");
        result.Data.Broadcast.Name.Should().Be("OPD Campaign");
    }

    [Fact]
    public async Task CreateMapping_IdenticalRetry_Returns200OkWithExistingData()
    {
        var client = CreateCustomClient();
        var keyGen = _keyService.GenerateKey();
        var tenantId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();

        _fakeInboxRepo.SeedEndpoint(new CachedWebhookEndpoint(
            Id: endpointId,
            TenantId: tenantId,
            Name: "Primary WhatsApp Line",
            KeyPrefix: keyGen.KeyPrefix,
            KeyHash: keyGen.KeyHash,
            Status: WebhookEndpointStatus.Active
        ));

        var sentAt = DateTimeOffset.Parse("2026-09-09T12:45:30Z");
        var request = new CreateTemplateMappingRequest
        {
            Wamid = "wamid.retry_test_001",
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest
            {
                Name = "opd_visit",
                Language = "en"
            },
            SentAt = sentAt
        };

        // Call 1
        var response1 = await client.PostAsJsonAsync($"/api/messages/{keyGen.PlainKey}/mappings", request);
        response1.StatusCode.Should().Be(HttpStatusCode.Created);

        // Call 2 (Retry)
        var response2 = await client.PostAsJsonAsync($"/api/messages/{keyGen.PlainKey}/mappings", request);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response2.Content.ReadFromJsonAsync<TemplateMappingResponse>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Created.Should().BeFalse();
        result.Data.Wamid.Should().Be("wamid.retry_test_001");
    }

    [Fact]
    public async Task CreateMapping_ConflictingPayload_Returns409Conflict()
    {
        var client = CreateCustomClient();
        var keyGen = _keyService.GenerateKey();
        var tenantId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();

        _fakeInboxRepo.SeedEndpoint(new CachedWebhookEndpoint(
            Id: endpointId,
            TenantId: tenantId,
            Name: "Primary WhatsApp Line",
            KeyPrefix: keyGen.KeyPrefix,
            KeyHash: keyGen.KeyHash,
            Status: WebhookEndpointStatus.Active
        ));

        var sentAt = DateTimeOffset.Parse("2026-09-09T12:45:30Z");
        var request1 = new CreateTemplateMappingRequest
        {
            Wamid = "wamid.conflict_test_001",
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest
            {
                Name = "template_A",
                Language = "en"
            },
            SentAt = sentAt
        };

        await client.PostAsJsonAsync($"/api/messages/{keyGen.PlainKey}/mappings", request1);

        var request2 = new CreateTemplateMappingRequest
        {
            Wamid = "wamid.conflict_test_001",
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest
            {
                Name = "template_B_DIFFERENT",
                Language = "en"
            },
            SentAt = sentAt
        };

        var response = await client.PostAsJsonAsync($"/api/messages/{keyGen.PlainKey}/mappings", request2);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = await response.Content.ReadFromJsonAsync<TemplateMappingErrorResponse>();
        error.Should().NotBeNull();
        error!.Success.Should().BeFalse();
        error.Message.Should().Contain("A different mapping already exists for this message ID.");
    }

    [Fact]
    public async Task ArrivalOrder_MappingFirst_WebhookStatusLater_PreservesTemplateMetadata()
    {
        var client = CreateCustomClient();
        var keyGen = _keyService.GenerateKey();
        var tenantId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();

        _fakeInboxRepo.SeedEndpoint(new CachedWebhookEndpoint(
            Id: endpointId,
            TenantId: tenantId,
            Name: "Primary Line",
            KeyPrefix: keyGen.KeyPrefix,
            KeyHash: keyGen.KeyHash,
            Status: WebhookEndpointStatus.Active
        ));

        const string testWamid = "wamid.order_test_mapping_first_001";
        var sentAt = DateTimeOffset.UtcNow;

        // Step 1: Mapping registered first
        var mapReq = new CreateTemplateMappingRequest
        {
            Wamid = testWamid,
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest { Name = "order_status_update", Language = "en" },
            SentAt = sentAt,
            Broadcast = new BroadcastInfoRequest { ExternalId = "BC_100", Name = "Order Updates" }
        };

        var mapResp = await client.PostAsJsonAsync($"/api/messages/{keyGen.PlainKey}/mappings", mapReq);
        mapResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // Verify message has no fake status initially
        var msg = _mappingRepo.GetMessage(tenantId, testWamid);
        msg.Should().NotBeNull();
        msg!.CurrentStatus.Should().BeNull();
        msg.StatusRank.Should().BeNull();
        msg.TemplateName.Should().Be("order_status_update");
        msg.BroadcastName.Should().Be("Order Updates");

        // Step 2: Webhook status arrives later (sent -> delivered)
        _mappingRepo.ApplyWebhookStatus(tenantId, testWamid, "sent", 10, DateTimeOffset.UtcNow);
        var msgAfterSent = _mappingRepo.GetMessage(tenantId, testWamid);
        msgAfterSent!.CurrentStatus.Should().Be("sent");
        msgAfterSent.TemplateName.Should().Be("order_status_update");
        msgAfterSent.BroadcastName.Should().Be("Order Updates");

        _mappingRepo.ApplyWebhookStatus(tenantId, testWamid, "delivered", 20, DateTimeOffset.UtcNow.AddMinutes(1));
        var msgAfterDelivered = _mappingRepo.GetMessage(tenantId, testWamid);
        msgAfterDelivered!.CurrentStatus.Should().Be("delivered");
        msgAfterDelivered.TemplateName.Should().Be("order_status_update");
    }

    [Fact]
    public async Task ArrivalOrder_WebhookStatusFirst_MappingLater_EnrichesWithoutDowngrading()
    {
        var client = CreateCustomClient();
        var keyGen = _keyService.GenerateKey();
        var tenantId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();

        _fakeInboxRepo.SeedEndpoint(new CachedWebhookEndpoint(
            Id: endpointId,
            TenantId: tenantId,
            Name: "Primary Line",
            KeyPrefix: keyGen.KeyPrefix,
            KeyHash: keyGen.KeyHash,
            Status: WebhookEndpointStatus.Active
        ));

        const string testWamid = "wamid.order_test_webhook_first_001";
        var deliveredTimestamp = DateTimeOffset.UtcNow.AddMinutes(-5);

        // Step 1: Webhook arrives first
        _mappingRepo.ApplyWebhookStatus(tenantId, testWamid, "delivered", 20, deliveredTimestamp);
        var msgBeforeMapping = _mappingRepo.GetMessage(tenantId, testWamid);
        msgBeforeMapping.Should().NotBeNull();
        msgBeforeMapping!.CurrentStatus.Should().Be("delivered");
        msgBeforeMapping.TemplateName.Should().BeNull();

        // Step 2: Mapping arrives later
        var mapReq = new CreateTemplateMappingRequest
        {
            Wamid = testWamid,
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest { Name = "shipping_alert", Language = "en" },
            SentAt = deliveredTimestamp.AddMinutes(-1),
            Broadcast = new BroadcastInfoRequest { ExternalId = "BC_200", Name = "Shipping Alert" }
        };

        var mapResp = await client.PostAsJsonAsync($"/api/messages/{keyGen.PlainKey}/mappings", mapReq);
        mapResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // Verify status and rank are preserved, template is enriched
        var msgAfterMapping = _mappingRepo.GetMessage(tenantId, testWamid);
        msgAfterMapping!.CurrentStatus.Should().Be("delivered");
        msgAfterMapping.StatusRank.Should().Be(20);
        msgAfterMapping.TemplateName.Should().Be("shipping_alert");
        msgAfterMapping.BroadcastName.Should().Be("Shipping Alert");
    }

    [Fact]
    public async Task SameWamid_DifferentEndpoints_SameTenant_Returns409Conflict()
    {
        var client = CreateCustomClient();
        var keyGen1 = _keyService.GenerateKey();
        var keyGen2 = _keyService.GenerateKey();
        var tenantId = Guid.NewGuid();
        var endpointId1 = Guid.NewGuid();
        var endpointId2 = Guid.NewGuid();

        _fakeInboxRepo.SeedEndpoint(new CachedWebhookEndpoint(endpointId1, tenantId, "Line 1", keyGen1.KeyPrefix, keyGen1.KeyHash, WebhookEndpointStatus.Active));
        _fakeInboxRepo.SeedEndpoint(new CachedWebhookEndpoint(endpointId2, tenantId, "Line 2", keyGen2.KeyPrefix, keyGen2.KeyHash, WebhookEndpointStatus.Active));

        const string sharedWamid = "wamid.same_tenant_multi_endpoint_001";
        var sentAt = DateTimeOffset.UtcNow;

        var request = new CreateTemplateMappingRequest
        {
            Wamid = sharedWamid,
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest { Name = "test", Language = "en" },
            SentAt = sentAt
        };

        // Call on Endpoint 1 -> 201 Created
        var resp1 = await client.PostAsJsonAsync($"/api/messages/{keyGen1.PlainKey}/mappings", request);
        resp1.StatusCode.Should().Be(HttpStatusCode.Created);

        // Call on Endpoint 2 for same tenant & same WAMID -> 409 Conflict
        var resp2 = await client.PostAsJsonAsync($"/api/messages/{keyGen2.PlainKey}/mappings", request);
        resp2.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var err = await resp2.Content.ReadFromJsonAsync<TemplateMappingErrorResponse>();
        err!.Message.Should().Contain("A mapping for this message ID already exists under a different endpoint in this tenant.");
    }

    [Fact]
    public async Task SameWamid_DifferentTenants_BothSucceedWithFullIsolation()
    {
        var client = CreateCustomClient();
        var keyGenT1 = _keyService.GenerateKey();
        var keyGenT2 = _keyService.GenerateKey();
        var tenantId1 = Guid.NewGuid();
        var tenantId2 = Guid.NewGuid();

        _fakeInboxRepo.SeedEndpoint(new CachedWebhookEndpoint(Guid.NewGuid(), tenantId1, "T1 Line", keyGenT1.KeyPrefix, keyGenT1.KeyHash, WebhookEndpointStatus.Active));
        _fakeInboxRepo.SeedEndpoint(new CachedWebhookEndpoint(Guid.NewGuid(), tenantId2, "T2 Line", keyGenT2.KeyPrefix, keyGenT2.KeyHash, WebhookEndpointStatus.Active));

        const string sharedWamid = "wamid.cross_tenant_wamid_001";
        var sentAt = DateTimeOffset.UtcNow;

        var request = new CreateTemplateMappingRequest
        {
            Wamid = sharedWamid,
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest { Name = "test", Language = "en" },
            SentAt = sentAt
        };

        // Tenant 1
        var resp1 = await client.PostAsJsonAsync($"/api/messages/{keyGenT1.PlainKey}/mappings", request);
        resp1.StatusCode.Should().Be(HttpStatusCode.Created);

        // Tenant 2 (Different tenant, same WAMID) -> Succeeds independently!
        var resp2 = await client.PostAsJsonAsync($"/api/messages/{keyGenT2.PlainKey}/mappings", request);
        resp2.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ConcurrentIdenticalRequests_ProduceSingleCreatedAndOneMatch()
    {
        var client = CreateCustomClient();
        var keyGen = _keyService.GenerateKey();
        var tenantId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();

        _fakeInboxRepo.SeedEndpoint(new CachedWebhookEndpoint(endpointId, tenantId, "Primary Line", keyGen.KeyPrefix, keyGen.KeyHash, WebhookEndpointStatus.Active));

        const string wamid = "wamid.concurrent_test_001";
        var sentAt = DateTimeOffset.UtcNow;
        var request = new CreateTemplateMappingRequest
        {
            Wamid = wamid,
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest { Name = "promo_blast", Language = "en" },
            SentAt = sentAt
        };

        var task1 = client.PostAsJsonAsync($"/api/messages/{keyGen.PlainKey}/mappings", request);
        var task2 = client.PostAsJsonAsync($"/api/messages/{keyGen.PlainKey}/mappings", request);

        var responses = await Task.WhenAll(task1, task2);
        var statusCodes = responses.Select(r => r.StatusCode).ToList();

        statusCodes.Should().Contain(HttpStatusCode.Created);
        statusCodes.Should().Contain(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ConcurrentConflictingRequests_ProduceOneSuccessAndOneConflict()
    {
        var client = CreateCustomClient();
        var keyGen = _keyService.GenerateKey();
        var tenantId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();

        _fakeInboxRepo.SeedEndpoint(new CachedWebhookEndpoint(endpointId, tenantId, "Primary Line", keyGen.KeyPrefix, keyGen.KeyHash, WebhookEndpointStatus.Active));

        const string wamid = "wamid.concurrent_conflict_ep_001";
        var sentAt = DateTimeOffset.UtcNow;
        var request1 = new CreateTemplateMappingRequest
        {
            Wamid = wamid,
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest { Name = "promo_A", Language = "en" },
            SentAt = sentAt
        };
        var request2 = new CreateTemplateMappingRequest
        {
            Wamid = wamid,
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest { Name = "promo_B", Language = "en" },
            SentAt = sentAt
        };

        var task1 = client.PostAsJsonAsync($"/api/messages/{keyGen.PlainKey}/mappings", request1);
        var task2 = client.PostAsJsonAsync($"/api/messages/{keyGen.PlainKey}/mappings", request2);

        var responses = await Task.WhenAll(task1, task2);
        var statusCodes = responses.Select(r => r.StatusCode).ToList();

        statusCodes.Should().Contain(HttpStatusCode.Created);
        statusCodes.Should().Contain(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateMapping_ConflictingSentAtBy500Milliseconds_Returns409Conflict()
    {
        var client = CreateCustomClient();
        var keyGen = _keyService.GenerateKey();
        var tenantId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();

        _fakeInboxRepo.SeedEndpoint(new CachedWebhookEndpoint(endpointId, tenantId, "Primary Line", keyGen.KeyPrefix, keyGen.KeyHash, WebhookEndpointStatus.Active));

        const string wamid = "wamid.sent_at_diff_500ms_001";
        var sentAt1 = DateTimeOffset.UtcNow;
        var sentAt2 = sentAt1.AddMilliseconds(500);

        var req1 = new CreateTemplateMappingRequest
        {
            Wamid = wamid,
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest { Name = "promo_blast", Language = "en" },
            SentAt = sentAt1
        };
        var req2 = new CreateTemplateMappingRequest
        {
            Wamid = wamid,
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest { Name = "promo_blast", Language = "en" },
            SentAt = sentAt2
        };

        var res1 = await client.PostAsJsonAsync($"/api/messages/{keyGen.PlainKey}/mappings", req1);
        res1.StatusCode.Should().Be(HttpStatusCode.Created);

        var res2 = await client.PostAsJsonAsync($"/api/messages/{keyGen.PlainKey}/mappings", req2);
        res2.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private class InMemoryStateEngineMappingRepository : ITemplateMappingRepository
    {
        private readonly ConcurrentDictionary<string, MessageTemplateMapping> _mappings = new();
        private readonly ConcurrentDictionary<string, Message> _messages = new();
        private readonly object _lock = new();

        public Message? GetMessage(Guid tenantId, string wamid)
        {
            _messages.TryGetValue($"{tenantId}:{wamid}", out var msg);
            return msg;
        }

        public void ApplyWebhookStatus(Guid tenantId, string wamid, string status, short statusRank, DateTimeOffset timestamp)
        {
            var msgKey = $"{tenantId}:{wamid}";
            _messages.AddOrUpdate(msgKey,
                _ => new Message
                {
                    TenantId = tenantId,
                    Wamid = wamid,
                    CurrentStatus = status,
                    StatusRank = statusRank,
                    LastStatusTimestamp = timestamp
                },
                (_, existing) =>
                {
                    if (MessageStateEngine.ShouldApplyStateTransition(existing.CurrentStatus, existing.StatusRank, existing.LastStatusTimestamp, status, statusRank, timestamp))
                    {
                        existing.CurrentStatus = status;
                        existing.StatusRank = statusRank;
                        existing.LastStatusTimestamp = timestamp;
                    }
                    return existing;
                });
        }

        public Task<TemplateMappingExecutionResult> CreateOrEnrichMappingAsync(
            CachedWebhookEndpoint endpoint,
            CreateTemplateMappingRequest request,
            CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                var mapKey = $"{endpoint.TenantId}:{request.Wamid.Trim()}";
                var normRecipient = PhoneNormalizationHelper.NormalizeRecipientId(request.RecipientId);
                var sentAtUtc = request.SentAt!.Value.ToUniversalTime();

                if (_mappings.TryGetValue(mapKey, out var existing))
                {
                    if (existing.EndpointId != endpoint.Id)
                    {
                        return Task.FromResult(new TemplateMappingExecutionResult(
                            TemplateMappingStatus.Conflict,
                            null,
                            "A mapping for this message ID already exists under a different endpoint in this tenant."));
                    }

                    var isRecipientEqual = string.Equals(existing.RecipientId, normRecipient, StringComparison.OrdinalIgnoreCase);
                    var isTemplateNameEqual = string.Equals(existing.TemplateName, request.Template!.Name.Trim(), StringComparison.OrdinalIgnoreCase);
                    var isNamespaceEqual = string.Equals(existing.TemplateNamespace ?? "", request.Template.Namespace?.Trim() ?? "", StringComparison.OrdinalIgnoreCase);
                    var isLanguageEqual = string.Equals(existing.TemplateLanguage, request.Template.Language.Trim(), StringComparison.OrdinalIgnoreCase);
                    var isSentAtEqual = (existing.SentAt.ToUniversalTime().Ticks / 10) == (sentAtUtc.Ticks / 10);

                    if (!isRecipientEqual || !isTemplateNameEqual || !isNamespaceEqual || !isLanguageEqual || !isSentAtEqual)
                    {
                        return Task.FromResult(new TemplateMappingExecutionResult(
                            TemplateMappingStatus.Conflict,
                            null,
                            "A different mapping already exists for this message ID."));
                    }

                    var matchDto = new TemplateMappingDataDto(
                        Wamid: existing.Wamid,
                        RecipientId: existing.RecipientId,
                        Template: new TemplateInfoDto(existing.TemplateName, existing.TemplateNamespace, existing.TemplateLanguage),
                        SentAt: existing.SentAt,
                        Broadcast: existing.BroadcastId != null || existing.BroadcastName != null
                            ? new BroadcastInfoDto(existing.BroadcastId, existing.BroadcastName)
                            : null
                    );
                    return Task.FromResult(new TemplateMappingExecutionResult(TemplateMappingStatus.ExistingMatch, matchDto));
                }

                var newMapping = new MessageTemplateMapping
                {
                    TenantId = endpoint.TenantId,
                    EndpointId = endpoint.Id,
                    Wamid = request.Wamid.Trim(),
                    RecipientId = normRecipient,
                    TemplateName = request.Template!.Name.Trim(),
                    TemplateNamespace = string.IsNullOrWhiteSpace(request.Template.Namespace) ? null : request.Template.Namespace.Trim(),
                    TemplateLanguage = request.Template.Language.Trim(),
                    SentAt = sentAtUtc,
                    BroadcastId = request.Broadcast?.ExternalId?.Trim(),
                    BroadcastName = request.Broadcast?.Name?.Trim()
                };

                _mappings[mapKey] = newMapping;

                // Enrich messages table
                var msgKey = $"{endpoint.TenantId}:{newMapping.Wamid}";
                _messages.AddOrUpdate(msgKey,
                    _ => new Message
                    {
                        TenantId = endpoint.TenantId,
                        EndpointId = endpoint.Id,
                        Wamid = newMapping.Wamid,
                        RecipientPhone = normRecipient,
                        TemplateName = newMapping.TemplateName,
                        BroadcastId = newMapping.BroadcastId,
                        BroadcastName = newMapping.BroadcastName
                    },
                    (_, existingMsg) =>
                    {
                        existingMsg.RecipientPhone ??= normRecipient;
                        existingMsg.TemplateName ??= newMapping.TemplateName;
                        existingMsg.BroadcastId ??= newMapping.BroadcastId;
                        existingMsg.BroadcastName ??= newMapping.BroadcastName;
                        return existingMsg;
                    });

                var createdDto = new TemplateMappingDataDto(
                    Wamid: newMapping.Wamid,
                    RecipientId: newMapping.RecipientId,
                    Template: new TemplateInfoDto(newMapping.TemplateName, newMapping.TemplateNamespace, newMapping.TemplateLanguage),
                    SentAt: newMapping.SentAt,
                    Broadcast: newMapping.BroadcastId != null || newMapping.BroadcastName != null
                        ? new BroadcastInfoDto(newMapping.BroadcastId, newMapping.BroadcastName)
                        : null
                );

                return Task.FromResult(new TemplateMappingExecutionResult(TemplateMappingStatus.Created, createdDto));
            }
        }
    }
}

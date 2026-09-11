using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OraiWebhookManager.Api.Controllers;
using OraiWebhookManager.Api.Middleware;
using OraiWebhookManager.Application.Helpers;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Models;
using OraiWebhookManager.Application.Options;
using OraiWebhookManager.Domain.Enums;
using OraiWebhookManager.Infrastructure.Services;

namespace OraiWebhookManager.UnitTests;

public class TemplateMappingUnitTests
{
    private readonly IWebhookKeyService _keyService = new WebhookKeyService();

    [Theory]
    [InlineData("+919644391241", "919644391241")]
    [InlineData("919644391241", "919644391241")]
    [InlineData(" +14155552671 ", "14155552671")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void PhoneNormalizationHelper_NormalizesPhoneCorrectly(string input, string expected)
    {
        var normalized = PhoneNormalizationHelper.NormalizeRecipientId(input);
        normalized.Should().Be(expected);
    }

    [Fact]
    public async Task WebhookEndpointResolver_WhenKeyIsInvalidOrNonexistent_ReturnsInvalidKey()
    {
        var inboxRepo = new FakeWebhookInboxRepository();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new WebhookIngestionOptions { CacheTtlSeconds = 60 });
        var resolver = new WebhookEndpointResolver(_keyService, inboxRepo, cache, options);

        var emptyResult = await resolver.ResolveEndpointAsync("");
        emptyResult.Status.Should().Be(WebhookEndpointResolutionStatus.InvalidKey);
        emptyResult.Endpoint.Should().BeNull();

        var nonexistentResult = await resolver.ResolveEndpointAsync("whk_live_nonexistent12345");
        nonexistentResult.Status.Should().Be(WebhookEndpointResolutionStatus.InvalidKey);
        nonexistentResult.Endpoint.Should().BeNull();
    }

    [Fact]
    public async Task WebhookEndpointResolver_WhenEndpointIsActive_ReturnsSuccessWithCachedEndpoint()
    {
        var inboxRepo = new FakeWebhookInboxRepository();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new WebhookIngestionOptions { CacheTtlSeconds = 60 });
        var resolver = new WebhookEndpointResolver(_keyService, inboxRepo, cache, options);

        var keyGen = _keyService.GenerateKey();
        var endpointId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        inboxRepo.SeedEndpoint(new CachedWebhookEndpoint(
            Id: endpointId,
            TenantId: tenantId,
            Name: "Test Endpoint",
            KeyPrefix: keyGen.KeyPrefix,
            KeyHash: keyGen.KeyHash,
            Status: WebhookEndpointStatus.Active
        ));

        var result = await resolver.ResolveEndpointAsync(keyGen.PlainKey);
        result.Status.Should().Be(WebhookEndpointResolutionStatus.Success);
        result.Endpoint.Should().NotBeNull();
        result.Endpoint!.Id.Should().Be(endpointId);
        result.Endpoint.TenantId.Should().Be(tenantId);
    }

    [Theory]
    [InlineData(WebhookEndpointStatus.Suspended)]
    [InlineData(WebhookEndpointStatus.Revoked)]
    public async Task WebhookEndpointResolver_WhenEndpointIsSuspendedOrRevoked_ReturnsInactiveOrRevoked(WebhookEndpointStatus status)
    {
        var inboxRepo = new FakeWebhookInboxRepository();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new WebhookIngestionOptions { CacheTtlSeconds = 60 });
        var resolver = new WebhookEndpointResolver(_keyService, inboxRepo, cache, options);

        var keyGen = _keyService.GenerateKey();
        inboxRepo.SeedEndpoint(new CachedWebhookEndpoint(
            Id: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            Name: "Inactive Endpoint",
            KeyPrefix: keyGen.KeyPrefix,
            KeyHash: keyGen.KeyHash,
            Status: status
        ));

        var result = await resolver.ResolveEndpointAsync(keyGen.PlainKey);
        result.Status.Should().Be(WebhookEndpointResolutionStatus.InactiveOrRevoked);
    }

    [Theory]
    [InlineData("/api/webhooks/whatsapp/whk_live_abcdef1234567890", "/api/webhooks/whatsapp/whk_live_***")]
    [InlineData("/api/messages/whk_live_abcdef1234567890/mappings", "/api/messages/whk_live_***/mappings")]
    public async Task WebhookKeyRedactionMiddleware_SanitizesBothWebhookAndMappingPaths(string inputPath, string expectedSanitized)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = inputPath;

        var middleware = new WebhookKeyRedactionMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);

        context.Items["SanitizedPath"].Should().Be(expectedSanitized);
    }

    [Fact]
    public async Task MessagesController_CreateMapping_WhenKeyIsInvalidOrInactive_ReturnsSafe404()
    {
        var (controller, _, _) = CreateMessagesController(out var fakeResolver, out _);

        var request = new CreateTemplateMappingRequest
        {
            Wamid = "wamid.HBgMOTE5NjQ0MzkxMjQxFQIAERgSRUVDMTYxMkY1MzJBQUNGOEVDAA==",
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest
            {
                Name = "test_template",
                Language = "en"
            },
            SentAt = DateTimeOffset.UtcNow
        };

        var response = await controller.CreateMapping("whk_live_invalidkey", request, CancellationToken.None);
        var notFoundResult = response as NotFoundObjectResult;
        notFoundResult.Should().NotBeNull();
        notFoundResult!.StatusCode.Should().Be(404);

        var errorResp = notFoundResult.Value as TemplateMappingErrorResponse;
        errorResp.Should().NotBeNull();
        errorResp!.Success.Should().BeFalse();
        errorResp.Message.Should().Be("Webhook endpoint not found or inactive.");
    }

    [Fact]
    public async Task MessagesController_CreateMapping_MissingTemplateOrSentAt_Returns400()
    {
        var (controller, keyGen, _) = CreateMessagesController(out var fakeResolver, out _);

        // Missing template
        var requestWithoutTemplate = new CreateTemplateMappingRequest
        {
            Wamid = "wamid.HBgMOTE5NjQ0MzkxMjQxFQIAERgSRUVDMTYxMkY1MzJBQUNGOEVDAA==",
            RecipientId = "919644391241",
            Template = null,
            SentAt = DateTimeOffset.UtcNow
        };

        var response1 = await controller.CreateMapping(keyGen.PlainKey, requestWithoutTemplate, CancellationToken.None);
        var badRequestResult1 = response1 as BadRequestObjectResult;
        badRequestResult1.Should().NotBeNull();
        badRequestResult1!.StatusCode.Should().Be(400);

        // Missing SentAt
        var requestWithoutSentAt = new CreateTemplateMappingRequest
        {
            Wamid = "wamid.HBgMOTE5NjQ0MzkxMjQxFQIAERgSRUVDMTYxMkY1MzJBQUNGOEVDAA==",
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest { Name = "test", Language = "en" },
            SentAt = null
        };

        var response2 = await controller.CreateMapping(keyGen.PlainKey, requestWithoutSentAt, CancellationToken.None);
        var badRequestResult2 = response2 as BadRequestObjectResult;
        badRequestResult2.Should().NotBeNull();
        badRequestResult2!.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task MessagesController_CreateMapping_FirstCreation_Returns201()
    {
        var (controller, keyGen, fakeRepo) = CreateMessagesController(out _, out _);

        var sentAt = DateTimeOffset.UtcNow;
        var request = new CreateTemplateMappingRequest
        {
            Wamid = "wamid.test_001",
            RecipientId = "+919644391241",
            Template = new TemplateInfoRequest
            {
                Name = "opd_visit",
                Namespace = "ns_123",
                Language = "en"
            },
            SentAt = sentAt,
            Broadcast = new BroadcastInfoRequest
            {
                ExternalId = "BROADCAST_001",
                Name = "Campaign 1"
            }
        };

        var response = await controller.CreateMapping(keyGen.PlainKey, request, CancellationToken.None);
        var objResult = response as ObjectResult;
        objResult.Should().NotBeNull();
        objResult!.StatusCode.Should().Be(201);

        var mappingResp = objResult.Value as TemplateMappingResponse;
        mappingResp.Should().NotBeNull();
        mappingResp!.Success.Should().BeTrue();
        mappingResp.Created.Should().BeTrue();
        mappingResp.Data.Wamid.Should().Be("wamid.test_001");
        mappingResp.Data.RecipientId.Should().Be("919644391241");
        mappingResp.Data.Template.Name.Should().Be("opd_visit");
        mappingResp.Data.Template.Namespace.Should().Be("ns_123");
        mappingResp.Data.Template.Language.Should().Be("en");
        mappingResp.Data.Broadcast.Should().NotBeNull();
        mappingResp.Data.Broadcast!.ExternalId.Should().Be("BROADCAST_001");
    }

    [Fact]
    public async Task MessagesController_CreateMapping_IdenticalRetry_Returns200()
    {
        var (controller, keyGen, fakeRepo) = CreateMessagesController(out _, out _);

        var sentAt = DateTimeOffset.UtcNow;
        var request = new CreateTemplateMappingRequest
        {
            Wamid = "wamid.test_002",
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest
            {
                Name = "opd_visit",
                Language = "en"
            },
            SentAt = sentAt
        };

        // First call -> 201
        await controller.CreateMapping(keyGen.PlainKey, request, CancellationToken.None);

        // Second identical call -> 200 with created=false
        var response = await controller.CreateMapping(keyGen.PlainKey, request, CancellationToken.None);
        var okResult = response as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(200);

        var mappingResp = okResult.Value as TemplateMappingResponse;
        mappingResp.Should().NotBeNull();
        mappingResp!.Success.Should().BeTrue();
        mappingResp.Created.Should().BeFalse();
        mappingResp.Data.Wamid.Should().Be("wamid.test_002");
    }

    [Fact]
    public async Task MessagesController_CreateMapping_ConflictingSameWamid_Returns409()
    {
        var (controller, keyGen, fakeRepo) = CreateMessagesController(out _, out _);

        var sentAt = DateTimeOffset.UtcNow;
        var request1 = new CreateTemplateMappingRequest
        {
            Wamid = "wamid.test_003",
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest
            {
                Name = "template_A",
                Language = "en"
            },
            SentAt = sentAt
        };

        await controller.CreateMapping(keyGen.PlainKey, request1, CancellationToken.None);

        // Conflicting request with different template name
        var request2 = new CreateTemplateMappingRequest
        {
            Wamid = "wamid.test_003",
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest
            {
                Name = "template_B_DIFFERENT",
                Language = "en"
            },
            SentAt = sentAt
        };

        var response = await controller.CreateMapping(keyGen.PlainKey, request2, CancellationToken.None);
        var objResult = response as ObjectResult;
        objResult.Should().NotBeNull();
        objResult!.StatusCode.Should().Be(409);

        var errorResp = objResult.Value as TemplateMappingErrorResponse;
        errorResp.Should().NotBeNull();
        errorResp!.Success.Should().BeFalse();
        errorResp.Message.Should().Contain("A different mapping already exists for this message ID.");
    }

    [Fact]
    public async Task MessagesController_CreateMapping_EquivalentSentAtWithDifferentTimezoneOffset_Returns200()
    {
        var (controller, keyGen, fakeRepo) = CreateMessagesController(out _, out _);

        var utcTime = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var istTime = new DateTimeOffset(2026, 9, 9, 17, 30, 0, TimeSpan.FromHours(5.5)); // Exactly the same moment in time

        var request1 = new CreateTemplateMappingRequest
        {
            Wamid = "wamid.test_tz_001",
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest { Name = "opd_visit", Language = "en" },
            SentAt = utcTime
        };

        var response1 = await controller.CreateMapping(keyGen.PlainKey, request1, CancellationToken.None);
        (response1 as ObjectResult)?.StatusCode.Should().Be(201);

        var request2 = new CreateTemplateMappingRequest
        {
            Wamid = "wamid.test_tz_001",
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest { Name = "opd_visit", Language = "en" },
            SentAt = istTime
        };

        var response2 = await controller.CreateMapping(keyGen.PlainKey, request2, CancellationToken.None);
        var okResult = response2 as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(200);
        ((TemplateMappingResponse)okResult.Value!).Created.Should().BeFalse();
    }

    [Fact]
    public async Task MessagesController_CreateMapping_ConflictingSentAtBy500Milliseconds_Returns409()
    {
        var (controller, keyGen, _) = CreateMessagesController(out _, out _);

        var utcTime = new DateTimeOffset(2026, 9, 9, 12, 0, 0, 0, TimeSpan.Zero);
        var differentTime = utcTime.AddMilliseconds(500); // 500ms different instant in time

        var request1 = new CreateTemplateMappingRequest
        {
            Wamid = "wamid.test_tz_conflict_001",
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest { Name = "opd_visit", Language = "en" },
            SentAt = utcTime
        };

        var response1 = await controller.CreateMapping(keyGen.PlainKey, request1, CancellationToken.None);
        (response1 as ObjectResult)?.StatusCode.Should().Be(201);

        var request2 = new CreateTemplateMappingRequest
        {
            Wamid = "wamid.test_tz_conflict_001",
            RecipientId = "919644391241",
            Template = new TemplateInfoRequest { Name = "opd_visit", Language = "en" },
            SentAt = differentTime
        };

        var response2 = await controller.CreateMapping(keyGen.PlainKey, request2, CancellationToken.None);
        var objResult = response2 as ObjectResult;
        objResult.Should().NotBeNull();
        objResult!.StatusCode.Should().Be(409);
        var errorResp = objResult.Value as TemplateMappingErrorResponse;
        errorResp.Should().NotBeNull();
        errorResp!.Success.Should().BeFalse();
        errorResp.Message.Should().Contain("A different mapping already exists for this message ID.");
    }

    private (MessagesController Controller, WebhookKeyGenerateResult KeyGen, FakeTemplateMappingRepository Repo)
        CreateMessagesController(out FakeWebhookEndpointResolver resolver, out FakeTemplateMappingRepository mappingRepo)
    {
        var keyGen = _keyService.GenerateKey();
        var endpointId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var activeEndpoint = new CachedWebhookEndpoint(
            Id: endpointId,
            TenantId: tenantId,
            Name: "Test Endpoint",
            KeyPrefix: keyGen.KeyPrefix,
            KeyHash: keyGen.KeyHash,
            Status: WebhookEndpointStatus.Active
        );

        resolver = new FakeWebhookEndpointResolver(keyGen.PlainKey, activeEndpoint);
        mappingRepo = new FakeTemplateMappingRepository();

        var dashboardRepo = new FakeDashboardRepository();
        var userContext = new FakeCurrentUserContext();

        var controller = new MessagesController(
            dashboardRepo,
            userContext,
            resolver,
            mappingRepo,
            NullLogger<MessagesController>.Instance
        );

        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        return (controller, keyGen, mappingRepo);
    }

    private class FakeWebhookEndpointResolver : IWebhookEndpointResolver
    {
        private readonly string _validKey;
        private readonly CachedWebhookEndpoint _endpoint;

        public FakeWebhookEndpointResolver(string validKey, CachedWebhookEndpoint endpoint)
        {
            _validKey = validKey;
            _endpoint = endpoint;
        }

        public Task<WebhookEndpointResolutionResult> ResolveEndpointAsync(string webhookKey, CancellationToken cancellationToken = default)
        {
            if (webhookKey == _validKey)
            {
                return Task.FromResult(new WebhookEndpointResolutionResult(WebhookEndpointResolutionStatus.Success, _endpoint));
            }

            return Task.FromResult(new WebhookEndpointResolutionResult(WebhookEndpointResolutionStatus.InvalidKey, null));
        }
    }

    private class FakeTemplateMappingRepository : ITemplateMappingRepository
    {
        private readonly Dictionary<string, (CreateTemplateMappingRequest Request, Guid TenantId, Guid EndpointId)> _store = new();

        public Task<TemplateMappingExecutionResult> CreateOrEnrichMappingAsync(
            CachedWebhookEndpoint endpoint,
            CreateTemplateMappingRequest request,
            CancellationToken cancellationToken = default)
        {
            var key = $"{endpoint.Id}:{request.Wamid.Trim()}";
            var normRecipient = PhoneNormalizationHelper.NormalizeRecipientId(request.RecipientId);
            var sentAtUtc = request.SentAt!.Value.ToUniversalTime();

            if (!_store.TryGetValue(key, out var existing))
            {
                _store[key] = (request, endpoint.TenantId, endpoint.Id);
                var createdDto = new TemplateMappingDataDto(
                    Wamid: request.Wamid.Trim(),
                    RecipientId: normRecipient,
                    Template: new TemplateInfoDto(request.Template!.Name.Trim(), request.Template.Namespace?.Trim(), request.Template.Language.Trim()),
                    SentAt: sentAtUtc,
                    Broadcast: request.Broadcast != null ? new BroadcastInfoDto(request.Broadcast.ExternalId, request.Broadcast.Name) : null
                );
                return Task.FromResult(new TemplateMappingExecutionResult(TemplateMappingStatus.Created, createdDto));
            }

            // Existing comparison
            var existingReq = existing.Request;
            var existingNormRecipient = PhoneNormalizationHelper.NormalizeRecipientId(existingReq.RecipientId);
            var isRecipientEqual = string.Equals(existingNormRecipient, normRecipient, StringComparison.OrdinalIgnoreCase);
            var isTemplateNameEqual = string.Equals(existingReq.Template!.Name.Trim(), request.Template!.Name.Trim(), StringComparison.OrdinalIgnoreCase);
            var isNamespaceEqual = string.Equals(existingReq.Template.Namespace?.Trim() ?? "", request.Template.Namespace?.Trim() ?? "", StringComparison.OrdinalIgnoreCase);
            var isLanguageEqual = string.Equals(existingReq.Template.Language.Trim(), request.Template.Language.Trim(), StringComparison.OrdinalIgnoreCase);
            var existingSentAtUtc = existingReq.SentAt!.Value.ToUniversalTime();
            var isSentAtEqual = (existingSentAtUtc.Ticks / 10) == (sentAtUtc.Ticks / 10);

            if (!isRecipientEqual || !isTemplateNameEqual || !isNamespaceEqual || !isLanguageEqual || !isSentAtEqual)
            {
                return Task.FromResult(new TemplateMappingExecutionResult(TemplateMappingStatus.Conflict, null, "A different mapping already exists for this message ID."));
            }

            // Broadcast conflict check
            var existingBroadcastId = existingReq.Broadcast?.ExternalId?.Trim();
            var incomingBroadcastId = request.Broadcast?.ExternalId?.Trim();
            if (!string.IsNullOrEmpty(existingBroadcastId) && !string.IsNullOrEmpty(incomingBroadcastId))
            {
                if (!string.Equals(existingBroadcastId, incomingBroadcastId, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new TemplateMappingExecutionResult(TemplateMappingStatus.Conflict, null, "A different mapping already exists for this message ID."));
                }
            }

            var finalBroadcastId = existingBroadcastId ?? incomingBroadcastId;
            var finalBroadcastName = existingReq.Broadcast?.Name ?? request.Broadcast?.Name;

            var matchDto = new TemplateMappingDataDto(
                Wamid: existingReq.Wamid.Trim(),
                RecipientId: existingNormRecipient,
                Template: new TemplateInfoDto(existingReq.Template.Name.Trim(), existingReq.Template.Namespace?.Trim(), existingReq.Template.Language.Trim()),
                SentAt: existingReq.SentAt.Value.ToUniversalTime(),
                Broadcast: finalBroadcastId != null || finalBroadcastName != null ? new BroadcastInfoDto(finalBroadcastId, finalBroadcastName) : null
            );

            return Task.FromResult(new TemplateMappingExecutionResult(TemplateMappingStatus.ExistingMatch, matchDto));
        }
    }

    private class FakeWebhookInboxRepository : IWebhookInboxRepository
    {
        public CachedWebhookEndpoint? SeededEndpoint { get; private set; }

        public void SeedEndpoint(CachedWebhookEndpoint endpoint)
        {
            SeededEndpoint = endpoint;
        }

        public Task<long> EnqueueAsync(
            Guid tenantId,
            Guid endpointId,
            string payloadRaw,
            string headersJson,
            string? ipAddress,
            CancellationToken cancellationToken = default) => Task.FromResult(1L);

        public Task<PubSubInboxEnqueueResult> EnqueueFromPubSubAsync(
            PubSubWebhookEnvelope envelope,
            string pubsubMessageId,
            CancellationToken cancellationToken = default) => Task.FromResult(new PubSubInboxEnqueueResult(PubSubInboxEnqueueStatus.Created, 1L));

        public Task<CachedWebhookEndpoint?> GetEndpointByHashAsync(byte[] keyHash, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SeededEndpoint);
        }
    }

    private class FakeCurrentUserContext : ICurrentUserContext
    {
        public Guid? TenantId => Guid.NewGuid();
        public Guid? UserId => Guid.NewGuid();
        public bool IsPlatformAdmin => false;
        public TenantRole? Role => TenantRole.TenantAdmin;
    }

    private class FakeDashboardRepository : IDashboardRepository
    {
        public Task<DashboardSummaryDto> GetSummaryAsync(Guid tenantId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagedResult<MessageListItemDto>> GetMessagesAsync(Guid tenantId, MessageFilterParams filter, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<MessageStatusEventDto>?> GetMessageEventsAsync(Guid tenantId, Guid messageId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<WebhookEndpointDto>> GetWebhookEndpointsAsync(Guid tenantId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<StatusLogExportRow>> GetStatusLogsForExportAsync(Guid tenantId, MessageFilterParams filter, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> ValidateTenantActiveAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}

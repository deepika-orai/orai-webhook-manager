using System.IO;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OraiWebhookManager.Api.Controllers;
using OraiWebhookManager.Application.Exceptions;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Models;
using OraiWebhookManager.Application.Options;
using OraiWebhookManager.Domain.Enums;
using Xunit;

namespace OraiWebhookManager.UnitTests;

public class WhatsAppWebhookControllerUnitTests
{
    private class FakeWebhookInboxRepository : IWebhookInboxRepository
    {
        public List<(Guid TenantId, Guid EndpointId, string Payload, string Headers, string? Ip)> EnqueuedDirectItems { get; } = new();
        public CachedWebhookEndpoint? SeededEndpoint { get; set; }

        public Task<long> EnqueueAsync(
            Guid tenantId,
            Guid endpointId,
            string payloadRaw,
            string headersJson,
            string? ipAddress,
            CancellationToken cancellationToken = default)
        {
            EnqueuedDirectItems.Add((tenantId, endpointId, payloadRaw, headersJson, ipAddress));
            return Task.FromResult(9999L);
        }

        public Task<PubSubInboxEnqueueResult> EnqueueFromPubSubAsync(
            PubSubWebhookEnvelope envelope,
            string pubsubMessageId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PubSubInboxEnqueueResult(PubSubInboxEnqueueStatus.Created, 9999L));
        }

        public Task<CachedWebhookEndpoint?> GetEndpointByHashAsync(byte[] keyHash, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SeededEndpoint);
        }
    }

    private class FakeWebhookKeyService : IWebhookKeyService
    {
        public WebhookKeyGenerateResult GenerateKey()
        {
            return new WebhookKeyGenerateResult("whk_live_testkey123456", "whk_live_test", new byte[] { 1, 2, 3 });
        }

        public byte[] ComputeKeyHash(string plainKey) => new byte[] { 1, 2, 3 };
        public string ExtractPrefix(string plainKey) => "whk_live_test";
    }

    private class FakeWebhookBufferPublisher : IWebhookBufferPublisher
    {
        public List<PubSubWebhookEnvelope> PublishedEnvelopes { get; } = new();
        public Func<PubSubWebhookEnvelope, Task<string>>? PublishHandler { get; set; }

        public Task<string> PublishAsync(PubSubWebhookEnvelope envelope, CancellationToken cancellationToken = default)
        {
            PublishedEnvelopes.Add(envelope);
            if (PublishHandler != null)
            {
                return PublishHandler(envelope);
            }
            return Task.FromResult("pubsub-msg-12345");
        }
    }

    private static (WhatsAppWebhookController Controller, FakeWebhookInboxRepository InboxRepo, FakeWebhookBufferPublisher Publisher)
        CreateController(bool usePubSubBuffer)
    {
        var inboxRepo = new FakeWebhookInboxRepository();
        var keyService = new FakeWebhookKeyService();
        var publisher = new FakeWebhookBufferPublisher();
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var ingestionOptions = Options.Create(new WebhookIngestionOptions { CacheTtlSeconds = 60 });
        var pubSubOptions = Options.Create(new GooglePubSubOptions { UsePubSubBuffer = usePubSubBuffer });

        var controller = new WhatsAppWebhookController(
            keyService,
            inboxRepo,
            publisher,
            memoryCache,
            ingestionOptions,
            pubSubOptions,
            NullLogger<WhatsAppWebhookController>.Instance
        );

        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        return (controller, inboxRepo, publisher);
    }

    private static void SetRequestBody(ControllerBase controller, string bodyContent, Dictionary<string, string>? headers = null)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bodyContent));
        controller.HttpContext.Request.Body = stream;
        controller.HttpContext.Request.ContentLength = stream.Length;
        controller.HttpContext.Request.ContentType = "application/json";

        if (headers != null)
        {
            foreach (var kvp in headers)
            {
                controller.HttpContext.Request.Headers[kvp.Key] = kvp.Value;
            }
        }
    }

    [Fact]
    public async Task IngestWebhook_WhenUsePubSubBufferIsFalse_CallsRepositoryOnceAndPublisherZeroTimes()
    {
        var (controller, inboxRepo, publisher) = CreateController(usePubSubBuffer: false);
        var tenantId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();

        inboxRepo.SeededEndpoint = new CachedWebhookEndpoint(
            Id: endpointId,
            TenantId: tenantId,
            Name: "Test Endpoint",
            KeyPrefix: "whk_live_test",
            KeyHash: new byte[] { 1, 2, 3 },
            Status: WebhookEndpointStatus.Active
        );

        const string samplePayload = "{\"entry\":[{\"id\":\"123\"}]}";
        SetRequestBody(controller, samplePayload, new Dictionary<string, string>
        {
            { "User-Agent", "WhatsAppBot/1.0" },
            { "X-Hub-Signature-256", "sha256=12345" }
        });

        var result = await controller.IngestWebhook("whk_live_testkey123456", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("received").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("inbox_id").GetInt64().Should().Be(9999);

        inboxRepo.EnqueuedDirectItems.Should().HaveCount(1);
        inboxRepo.EnqueuedDirectItems[0].TenantId.Should().Be(tenantId);
        inboxRepo.EnqueuedDirectItems[0].EndpointId.Should().Be(endpointId);
        inboxRepo.EnqueuedDirectItems[0].Payload.Should().Be(samplePayload);

        publisher.PublishedEnvelopes.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestWebhook_WhenUsePubSubBufferIsTrue_CallsPublisherOnceAndRepositoryZeroTimes()
    {
        var (controller, inboxRepo, publisher) = CreateController(usePubSubBuffer: true);
        var tenantId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();

        inboxRepo.SeededEndpoint = new CachedWebhookEndpoint(
            Id: endpointId,
            TenantId: tenantId,
            Name: "Test Endpoint",
            KeyPrefix: "whk_live_test",
            KeyHash: new byte[] { 1, 2, 3 },
            Status: WebhookEndpointStatus.Active
        );

        const string samplePayload = "{\"entry\":[{\"id\":\"meta-webhook-1\"}]}";
        SetRequestBody(controller, samplePayload, new Dictionary<string, string>
        {
            { "User-Agent", "facebookexternalua" },
            { "X-Hub-Signature-256", "sha256=9999" },
            { "Authorization", "Bearer secret_token" }
        });

        var result = await controller.IngestWebhook("whk_live_testkey123456", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("received").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("buffered").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("correlation_id").GetString().Should().NotBeNullOrWhiteSpace();
        doc.RootElement.GetProperty("queue_message_id").GetString().Should().Be("pubsub-msg-12345");
        doc.RootElement.GetProperty("inbox_id").ValueKind.Should().Be(JsonValueKind.Null);

        // Verify publisher was called once with sanitized envelope
        publisher.PublishedEnvelopes.Should().HaveCount(1);
        var envelope = publisher.PublishedEnvelopes[0];
        envelope.CorrelationId.Should().NotBeEmpty();
        envelope.TenantId.Should().Be(tenantId);
        envelope.EndpointId.Should().Be(endpointId);
        envelope.PayloadRaw.Should().Be(samplePayload);
        envelope.FilteredHeaders.Should().ContainKey("User-Agent");
        envelope.FilteredHeaders.Should().NotContainKey("X-Hub-Signature-256");
        envelope.FilteredHeaders.Should().NotContainKey("Authorization");

        // Verify direct repository was NOT called
        inboxRepo.EnqueuedDirectItems.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestWebhook_WhenPublisherThrowsPublishException_Returns503AndNeverFallsBackToDirectDb()
    {
        var (controller, inboxRepo, publisher) = CreateController(usePubSubBuffer: true);
        var tenantId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();

        inboxRepo.SeededEndpoint = new CachedWebhookEndpoint(
            Id: endpointId,
            TenantId: tenantId,
            Name: "Test Endpoint",
            KeyPrefix: "whk_live_test",
            KeyHash: new byte[] { 1, 2, 3 },
            Status: WebhookEndpointStatus.Active
        );

        publisher.PublishHandler = env =>
            throw new WebhookBufferPublishException("Pub/Sub broker unavailable", env.CorrelationId, isAmbiguousTimeout: false);

        SetRequestBody(controller, "{\"entry\":[]}");

        var result = await controller.IngestWebhook("whk_live_testkey123456", CancellationToken.None);

        result.Should().BeOfType<ObjectResult>();
        var objResult = (ObjectResult)result;
        objResult.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);

        controller.Response.Headers.RetryAfter.ToString().Should().Be("5");

        var json = JsonSerializer.Serialize(objResult.Value);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("error").GetString().Should().Contain("temporarily unavailable");
        doc.RootElement.GetProperty("retry_after_seconds").GetInt32().Should().Be(5);
        doc.RootElement.GetProperty("correlation_id").GetString().Should().NotBeNullOrWhiteSpace();

        // Direct DB MUST NEVER be called on failure
        inboxRepo.EnqueuedDirectItems.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestWebhook_WhenPublisherTimesOutAmbiguously_Returns503AndNeverFallsBackToDirectDb()
    {
        var (controller, inboxRepo, publisher) = CreateController(usePubSubBuffer: true);
        var tenantId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();

        inboxRepo.SeededEndpoint = new CachedWebhookEndpoint(
            Id: endpointId,
            TenantId: tenantId,
            Name: "Test Endpoint",
            KeyPrefix: "whk_live_test",
            KeyHash: new byte[] { 1, 2, 3 },
            Status: WebhookEndpointStatus.Active
        );

        publisher.PublishHandler = env =>
            throw new WebhookBufferPublishException("Ambiguous timeout", env.CorrelationId, isAmbiguousTimeout: true);

        SetRequestBody(controller, "{\"entry\":[]}");

        var result = await controller.IngestWebhook("whk_live_testkey123456", CancellationToken.None);

        result.Should().BeOfType<ObjectResult>();
        var objResult = (ObjectResult)result;
        objResult.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);

        controller.Response.Headers.RetryAfter.ToString().Should().Be("5");

        // Direct DB MUST NEVER be called after ambiguous timeout
        inboxRepo.EnqueuedDirectItems.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestWebhook_InvalidOrRevokedKey_NeverPublishesAndReturnsUnauthorized()
    {
        var (controller, inboxRepo, publisher) = CreateController(usePubSubBuffer: true);

        inboxRepo.SeededEndpoint = new CachedWebhookEndpoint(
            Id: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            Name: "Revoked Endpoint",
            KeyPrefix: "whk_live_test",
            KeyHash: new byte[] { 1, 2, 3 },
            Status: WebhookEndpointStatus.Revoked
        );

        SetRequestBody(controller, "{\"entry\":[]}");

        var result = await controller.IngestWebhook("whk_live_testkey123456", CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>();
        publisher.PublishedEnvelopes.Should().BeEmpty();
        inboxRepo.EnqueuedDirectItems.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestWebhook_EmptyBody_NeverPublishesAndReturnsBadRequest()
    {
        var (controller, inboxRepo, publisher) = CreateController(usePubSubBuffer: true);

        inboxRepo.SeededEndpoint = new CachedWebhookEndpoint(
            Id: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            Name: "Active Endpoint",
            KeyPrefix: "whk_live_test",
            KeyHash: new byte[] { 1, 2, 3 },
            Status: WebhookEndpointStatus.Active
        );

        SetRequestBody(controller, "   ");

        var result = await controller.IngestWebhook("whk_live_testkey123456", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        publisher.PublishedEnvelopes.Should().BeEmpty();
        inboxRepo.EnqueuedDirectItems.Should().BeEmpty();
    }
}

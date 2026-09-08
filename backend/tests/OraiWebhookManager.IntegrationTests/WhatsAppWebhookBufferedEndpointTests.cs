using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OraiWebhookManager.Application.Exceptions;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Models;
using OraiWebhookManager.Domain.Enums;
using Xunit;

namespace OraiWebhookManager.IntegrationTests;

public class WhatsAppWebhookBufferedEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly FakeWebhookInboxRepository _fakeInboxRepo = new();
    private readonly FakeIntegrationBufferPublisher _fakePublisher = new();
    private readonly IWebhookKeyService _keyService;

    public WhatsAppWebhookBufferedEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _keyService = new Infrastructure.Services.WebhookKeyService();
    }

    private HttpClient CreateBufferedClient()
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("GooglePubSub:UsePubSubBuffer", "true");
            builder.UseSetting("GooglePubSub:ProjectId", "test-project");
            builder.UseSetting("GooglePubSub:TopicId", "test-topic");
            builder.UseSetting("GooglePubSub:SubscriptionId", "test-sub");

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IWebhookInboxRepository>(_fakeInboxRepo);
                services.AddSingleton<IWebhookBufferPublisher>(_fakePublisher);
            });
        }).CreateClient();
    }

    [Fact]
    public async Task IngestWebhook_BufferedMode_ValidActiveKey_ReturnsOkWithAdditiveContractAndPublishesSanitizedEnvelope()
    {
        var client = CreateBufferedClient();
        var keyGen = _keyService.GenerateKey();
        var tenantId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();

        _fakeInboxRepo.SeedEndpoint(new CachedWebhookEndpoint(
            Id: endpointId,
            TenantId: tenantId,
            Name: "Buffered WhatsApp Line",
            KeyPrefix: keyGen.KeyPrefix,
            KeyHash: keyGen.KeyHash,
            Status: WebhookEndpointStatus.Active
        ));

        const string samplePayload = """
        {
          "object": "whatsapp_business_account",
          "entry": [
            {
              "id": "12345",
              "changes": [
                {
                  "field": "messages",
                  "value": {
                    "messaging_product": "whatsapp",
                    "statuses": [
                      {
                        "id": "wamid.HBgLMTY1MDY5Nzg1MjYVAgASGBgyMjhBRDM2M0JBMzM3QjgyQkY1MEQ0OEIwMzgzOTg0NQA=",
                        "status": "delivered",
                        "timestamp": "1740000000"
                      }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/webhooks/whatsapp/{keyGen.PlainKey}")
        {
            Content = new StringContent(samplePayload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("User-Agent", "facebookexternalua");
        request.Headers.Add("X-Hub-Signature-256", "sha256=abcdef123456");
        request.Headers.Add("Authorization", "Bearer sensitive_auth_token");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        doc.Should().NotBeNull();
        var root = doc!.RootElement;

        // Verify additive contract
        root.GetProperty("received").GetBoolean().Should().BeTrue();
        root.GetProperty("buffered").GetBoolean().Should().BeTrue();
        root.GetProperty("correlation_id").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("queue_message_id").GetString().Should().Be("integration-msg-123");
        root.GetProperty("inbox_id").ValueKind.Should().Be(JsonValueKind.Null);

        // Verify publisher was called exactly once
        _fakePublisher.PublishedEnvelopes.Should().HaveCount(1);
        var envelope = _fakePublisher.PublishedEnvelopes[0];
        envelope.TenantId.Should().Be(tenantId);
        envelope.EndpointId.Should().Be(endpointId);
        envelope.PayloadRaw.Should().Be(samplePayload);

        // Verify sensitive headers were strictly excluded
        envelope.FilteredHeaders.Should().ContainKey("User-Agent");
        envelope.FilteredHeaders.Should().NotContainKey("X-Hub-Signature-256");
        envelope.FilteredHeaders.Should().NotContainKey("Authorization");

        // Verify direct repository was NOT called
        _fakeInboxRepo.EnqueuedItems.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestWebhook_BufferedMode_PublisherThrowsException_Returns503WithRetryAfterAndNeverCallsDirectDb()
    {
        var client = CreateBufferedClient();
        var keyGen = _keyService.GenerateKey();
        var tenantId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();

        _fakeInboxRepo.SeedEndpoint(new CachedWebhookEndpoint(
            Id: endpointId,
            TenantId: tenantId,
            Name: "Buffered WhatsApp Line",
            KeyPrefix: keyGen.KeyPrefix,
            KeyHash: keyGen.KeyHash,
            Status: WebhookEndpointStatus.Active
        ));

        _fakePublisher.PublishHandler = env =>
            throw new WebhookBufferPublishException("Pub/Sub broker is temporarily down", env.CorrelationId, isAmbiguousTimeout: false);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/webhooks/whatsapp/{keyGen.PlainKey}")
        {
            Content = new StringContent("{\"entry\":[]}", Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Headers.Contains("Retry-After").Should().BeTrue();
        response.Headers.GetValues("Retry-After").First().Should().Be("5");

        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        doc.Should().NotBeNull();
        var root = doc!.RootElement;
        root.GetProperty("error").GetString().Should().Contain("temporarily unavailable");
        root.GetProperty("retry_after_seconds").GetInt32().Should().Be(5);
        root.GetProperty("correlation_id").GetString().Should().NotBeNullOrWhiteSpace();

        // Direct DB MUST NEVER be called on failure
        _fakeInboxRepo.EnqueuedItems.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestWebhook_BufferedMode_PublisherTimesOutAmbiguously_Returns503WithRetryAfterAndNeverCallsDirectDb()
    {
        var client = CreateBufferedClient();
        var keyGen = _keyService.GenerateKey();
        var tenantId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();

        _fakeInboxRepo.SeedEndpoint(new CachedWebhookEndpoint(
            Id: endpointId,
            TenantId: tenantId,
            Name: "Buffered WhatsApp Line",
            KeyPrefix: keyGen.KeyPrefix,
            KeyHash: keyGen.KeyHash,
            Status: WebhookEndpointStatus.Active
        ));

        _fakePublisher.PublishHandler = env =>
            throw new WebhookBufferPublishException("Pub/Sub publish timed out after 5s. Publish outcome is ambiguous.", env.CorrelationId, isAmbiguousTimeout: true);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/webhooks/whatsapp/{keyGen.PlainKey}")
        {
            Content = new StringContent("{\"entry\":[]}", Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Headers.Contains("Retry-After").Should().BeTrue();
        response.Headers.GetValues("Retry-After").First().Should().Be("5");

        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        doc.Should().NotBeNull();
        var root = doc!.RootElement;
        root.GetProperty("error").GetString().Should().Contain("temporarily unavailable");
        root.GetProperty("retry_after_seconds").GetInt32().Should().Be(5);

        // Direct DB MUST NEVER be called after ambiguous timeout
        _fakeInboxRepo.EnqueuedItems.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestWebhook_BufferedMode_InvalidKey_ReturnsUnauthorizedAndNeverCallsPublisher()
    {
        var client = CreateBufferedClient();

        var response = await client.PostAsync(
            "/api/webhooks/whatsapp/whk_live_nonexistentkey123456",
            new StringContent("{}", Encoding.UTF8, "application/json")
        );

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _fakePublisher.PublishedEnvelopes.Should().BeEmpty();
        _fakeInboxRepo.EnqueuedItems.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestWebhook_BufferedMode_EmptyPayload_ReturnsBadRequestAndNeverCallsPublisher()
    {
        var client = CreateBufferedClient();
        var keyGen = _keyService.GenerateKey();

        _fakeInboxRepo.SeedEndpoint(new CachedWebhookEndpoint(
            Id: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            Name: "Active Endpoint",
            KeyPrefix: keyGen.KeyPrefix,
            KeyHash: keyGen.KeyHash,
            Status: WebhookEndpointStatus.Active
        ));

        var response = await client.PostAsync(
            $"/api/webhooks/whatsapp/{keyGen.PlainKey}",
            new StringContent("", Encoding.UTF8, "application/json")
        );

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _fakePublisher.PublishedEnvelopes.Should().BeEmpty();
        _fakeInboxRepo.EnqueuedItems.Should().BeEmpty();
    }
}

public class FakeIntegrationBufferPublisher : IWebhookBufferPublisher
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
        return Task.FromResult("integration-msg-123");
    }
}

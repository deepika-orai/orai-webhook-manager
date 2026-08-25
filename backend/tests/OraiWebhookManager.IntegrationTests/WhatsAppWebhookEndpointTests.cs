using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Domain.Enums;

namespace OraiWebhookManager.IntegrationTests;

public class WhatsAppWebhookEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly FakeWebhookInboxRepository _fakeInboxRepo = new();
    private readonly IWebhookKeyService _keyService;

    public WhatsAppWebhookEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _keyService = new Infrastructure.Services.WebhookKeyService();
    }

    private HttpClient CreateCustomClient()
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IWebhookInboxRepository>(_fakeInboxRepo);
            });
        }).CreateClient();
    }

    [Fact]
    public async Task IngestWebhook_InvalidKey_ReturnsUnauthorized()
    {
        var client = CreateCustomClient();

        var response = await client.PostAsync(
            "/api/webhooks/whatsapp/whk_live_invalidkey1234567890",
            new StringContent("{}", Encoding.UTF8, "application/json")
        );

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IngestWebhook_ValidActiveKey_ReturnsOkAndEnqueuesDurableItem()
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

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        doc.Should().NotBeNull();
        doc!.RootElement.GetProperty("received").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("inbox_id").GetInt64().Should().BeGreaterThan(0);

        _fakeInboxRepo.EnqueuedItems.Should().HaveCount(1);
        _fakeInboxRepo.EnqueuedItems[0].TenantId.Should().Be(tenantId);
        _fakeInboxRepo.EnqueuedItems[0].EndpointId.Should().Be(endpointId);
    }

    [Fact]
    public async Task IngestWebhook_RevokedKey_ReturnsUnauthorized()
    {
        var client = CreateCustomClient();
        var keyGen = _keyService.GenerateKey();

        _fakeInboxRepo.SeedEndpoint(new CachedWebhookEndpoint(
            Id: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            Name: "Old Line",
            KeyPrefix: keyGen.KeyPrefix,
            KeyHash: keyGen.KeyHash,
            Status: WebhookEndpointStatus.Revoked
        ));

        var response = await client.PostAsync(
            $"/api/webhooks/whatsapp/{keyGen.PlainKey}",
            new StringContent("{}", Encoding.UTF8, "application/json")
        );

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IngestWebhook_ActiveEndpointWithNullOptionalTimestamps_ReturnsOkAndEnqueuesDurableItem()
    {
        var client = CreateCustomClient();
        var keyGen = _keyService.GenerateKey();
        var tenantId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();

        // Seed Active endpoint with non-nullable Guid fields and default/null timestamps
        _fakeInboxRepo.SeedEndpoint(new CachedWebhookEndpoint(
            Id: endpointId,
            TenantId: tenantId,
            Name: "Active Line With Null Timestamps",
            KeyPrefix: keyGen.KeyPrefix,
            KeyHash: keyGen.KeyHash,
            Status: WebhookEndpointStatus.Active
        ));

        var response = await client.PostAsync(
            $"/api/webhooks/whatsapp/{keyGen.PlainKey}",
            new StringContent("{\"entry\":[]}", Encoding.UTF8, "application/json")
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fakeInboxRepo.EnqueuedItems.Should().HaveCount(1);
        _fakeInboxRepo.EnqueuedItems[0].TenantId.Should().Be(tenantId);
        _fakeInboxRepo.EnqueuedItems[0].EndpointId.Should().Be(endpointId);
    }

    [Fact]
    public async Task IngestWebhook_LogCapture_RawKeyIsNeverLogged_SafeRedactedPrefixIsLogged()
    {
        var logSink = new TestLogSink();
        var keyGen = _keyService.GenerateKey();
        var rawKey = keyGen.PlainKey; // e.g. whk_live_32byte_hex...
        var tenantId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();

        _fakeInboxRepo.SeedEndpoint(new CachedWebhookEndpoint(
            Id: endpointId,
            TenantId: tenantId,
            Name: "Redaction Logging Test Line",
            KeyPrefix: keyGen.KeyPrefix,
            KeyHash: keyGen.KeyHash,
            Status: WebhookEndpointStatus.Active
        ));

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ILoggerProvider>(logSink);
                services.AddSingleton<IWebhookInboxRepository>(_fakeInboxRepo);
            });
        }).CreateClient();

        var response = await client.PostAsync(
            $"/api/webhooks/whatsapp/{rawKey}",
            new StringContent("{\"entry\":[]}", Encoding.UTF8, "application/json")
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert: Log capture contains log entries from request lifecycle
        logSink.Logs.Should().NotBeEmpty();

        // Assert: Raw key is strictly absent from every single log entry
        foreach (var log in logSink.Logs)
        {
            log.Should().NotContain(rawKey, because: $"Raw webhook key '{rawKey}' must never be exposed in any log line");
        }

        // Assert: Safe redacted path/prefix is logged in hosting / routing logs
        logSink.Logs.Should().Contain(log => log.Contains("whk_live_***") || log.Contains("whk_***"),
            because: "Redacted key placeholder should be present in request lifecycle logs");
    }
}

public class TestLogSink : ILoggerProvider
{
    public System.Collections.Concurrent.ConcurrentBag<string> Logs { get; } = new();

    public ILogger CreateLogger(string categoryName) => new SinkLogger(categoryName, this);

    public void Dispose() { }

    private class SinkLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly TestLogSink _sink;

        public SinkLogger(string categoryName, TestLogSink sink)
        {
            _categoryName = categoryName;
            _sink = sink;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var msg = formatter(state, exception);
            _sink.Logs.Add($"[{_categoryName}] {msg}");
        }
    }
}

public class FakeWebhookInboxRepository : IWebhookInboxRepository
{
    private readonly Dictionary<string, CachedWebhookEndpoint> _endpoints = new();
    public List<(Guid TenantId, Guid EndpointId, string Payload, string Headers, string? Ip)> EnqueuedItems { get; } = new();
    private long _currentId = 1000;

    public void SeedEndpoint(CachedWebhookEndpoint endpoint)
    {
        var hashHex = Convert.ToHexString(endpoint.KeyHash);
        _endpoints[hashHex] = endpoint;
    }

    public Task<long> EnqueueAsync(
        Guid tenantId,
        Guid endpointId,
        string payloadRaw,
        string headersJson,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var id = Interlocked.Increment(ref _currentId);
        lock (EnqueuedItems)
        {
            EnqueuedItems.Add((tenantId, endpointId, payloadRaw, headersJson, ipAddress));
        }
        return Task.FromResult(id);
    }

    public Task<CachedWebhookEndpoint?> GetEndpointByHashAsync(byte[] keyHash, CancellationToken cancellationToken = default)
    {
        var hashHex = Convert.ToHexString(keyHash);
        _endpoints.TryGetValue(hashHex, out var ep);
        return Task.FromResult(ep);
    }
}

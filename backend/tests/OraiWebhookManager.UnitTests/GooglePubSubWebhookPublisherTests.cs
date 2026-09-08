using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Google.Cloud.PubSub.V1;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OraiWebhookManager.Application.Exceptions;
using OraiWebhookManager.Application.Models;
using OraiWebhookManager.Application.Options;
using OraiWebhookManager.Infrastructure.PubSub;
using OraiWebhookManager.Infrastructure.Services;
using Xunit;

namespace OraiWebhookManager.UnitTests;

public class GooglePubSubWebhookPublisherTests
{
    private class FakePubSubPublisherClient : IPubSubPublisherClient
    {
        public List<PubsubMessage> PublishedMessages { get; } = new();
        public Func<PubsubMessage, Task<string>>? PublishHandler { get; set; }
        public bool IsDisposed { get; private set; }
        public bool ShutdownCalled { get; private set; }

        public Task<string> PublishAsync(PubsubMessage message)
        {
            PublishedMessages.Add(message);
            if (PublishHandler != null)
            {
                return PublishHandler(message);
            }
            return Task.FromResult("msg-" + Guid.NewGuid().ToString("N"));
        }

        public Task ShutdownAsync(TimeSpan timeout = default, CancellationToken cancellationToken = default)
        {
            ShutdownCalled = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task PublishAsync_NullEnvelope_ThrowsArgumentNullException()
    {
        var fakeClient = new FakePubSubPublisherClient();
        var options = Options.Create(new GooglePubSubOptions());
        var publisher = new GooglePubSubWebhookPublisher(fakeClient, options, NullLogger<GooglePubSubWebhookPublisher>.Instance);

        var act = () => publisher.PublishAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PublishAsync_ValidEnvelope_SerializesEnvelopeAndSetsExactAttributes()
    {
        var fakeClient = new FakePubSubPublisherClient();
        var options = Options.Create(new GooglePubSubOptions { PublishTimeoutSeconds = 5 });
        var publisher = new GooglePubSubWebhookPublisher(fakeClient, options, NullLogger<GooglePubSubWebhookPublisher>.Instance);

        var correlationId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();
        var receivedAtUtc = DateTimeOffset.UtcNow;
        const string rawPayload = "{\"entry\":[{\"id\":\"12345\"}]}";

        var envelope = PubSubWebhookEnvelope.Create(
            correlationId: correlationId,
            tenantId: tenantId,
            endpointId: endpointId,
            receivedAtUtc: receivedAtUtc,
            payloadRaw: rawPayload,
            rawHeaders: new Dictionary<string, string>
            {
                { "User-Agent", "WhatsAppBot/1.0" },
                { "X-Hub-Signature-256", "sha256=abcdef" },
                { "Authorization", "Bearer secret" }
            },
            sourceIp: "10.0.0.1",
            contentType: "application/json"
        );

        var messageId = await publisher.PublishAsync(envelope);

        messageId.Should().StartWith("msg-");
        fakeClient.PublishedMessages.Should().HaveCount(1);

        var published = fakeClient.PublishedMessages[0];

        // 1. Validate Attributes
        published.Attributes.Should().ContainKey("correlation_id").WhoseValue.Should().Be(correlationId.ToString());
        published.Attributes.Should().ContainKey("tenant_id").WhoseValue.Should().Be(tenantId.ToString());
        published.Attributes.Should().ContainKey("endpoint_id").WhoseValue.Should().Be(endpointId.ToString());
        published.Attributes.Should().ContainKey("schema_version").WhoseValue.Should().Be("1");
        published.Attributes.Count.Should().Be(4);

        // Ensure NO sensitive data in attributes
        published.Attributes.Keys.Should().NotContain("key");
        published.Attributes.Keys.Should().NotContain("hash");
        published.Attributes.Keys.Should().NotContain("signature");
        published.Attributes.Keys.Should().NotContain("authorization");

        // 2. Validate Data (UTF-8 JSON envelope)
        var utf8Bytes = published.Data.ToByteArray();
        var deserializedEnvelope = JsonSerializer.Deserialize<PubSubWebhookEnvelope>(utf8Bytes);
        deserializedEnvelope.Should().NotBeNull();
        deserializedEnvelope!.CorrelationId.Should().Be(correlationId);
        deserializedEnvelope.TenantId.Should().Be(tenantId);
        deserializedEnvelope.EndpointId.Should().Be(endpointId);
        deserializedEnvelope.PayloadRaw.Should().Be(rawPayload);

        // FilteredHeaders in envelope must not contain signatures or authorization
        deserializedEnvelope.FilteredHeaders.Should().ContainKey("User-Agent");
        deserializedEnvelope.FilteredHeaders.Should().NotContainKey("X-Hub-Signature-256");
        deserializedEnvelope.FilteredHeaders.Should().NotContainKey("Authorization");
    }

    [Fact]
    public async Task PublishAsync_AmbiguousTimeout_ThrowsWebhookBufferPublishExceptionWithIsAmbiguousTimeoutTrue()
    {
        var fakeClient = new FakePubSubPublisherClient
        {
            PublishHandler = async msg =>
            {
                // Delay longer than PublishTimeoutSeconds
                await Task.Delay(TimeSpan.FromSeconds(2));
                return "late-msg-id";
            }
        };

        // Set short 1s timeout for fast test execution
        var options = Options.Create(new GooglePubSubOptions { PublishTimeoutSeconds = 1 });
        var publisher = new GooglePubSubWebhookPublisher(fakeClient, options, NullLogger<GooglePubSubWebhookPublisher>.Instance);

        var correlationId = Guid.NewGuid();
        var envelope = PubSubWebhookEnvelope.Create(
            correlationId: correlationId,
            tenantId: Guid.NewGuid(),
            endpointId: Guid.NewGuid(),
            receivedAtUtc: DateTimeOffset.UtcNow,
            payloadRaw: "{\"test\":\"timeout\"}",
            rawHeaders: new Dictionary<string, string>()
        );

        var act = () => publisher.PublishAsync(envelope);

        var ex = await act.Should().ThrowAsync<WebhookBufferPublishException>();
        ex.Which.IsAmbiguousTimeout.Should().BeTrue();
        ex.Which.CorrelationId.Should().Be(correlationId);
        ex.Which.Message.Should().Contain("timed out");
    }

    [Fact]
    public async Task PublishAsync_BrokerFailure_ThrowsWebhookBufferPublishExceptionWithIsAmbiguousTimeoutFalse()
    {
        var fakeClient = new FakePubSubPublisherClient
        {
            PublishHandler = msg => Task.FromException<string>(new InvalidOperationException("gRPC connection broken"))
        };

        var options = Options.Create(new GooglePubSubOptions { PublishTimeoutSeconds = 5 });
        var publisher = new GooglePubSubWebhookPublisher(fakeClient, options, NullLogger<GooglePubSubWebhookPublisher>.Instance);

        var correlationId = Guid.NewGuid();
        var envelope = PubSubWebhookEnvelope.Create(
            correlationId: correlationId,
            tenantId: Guid.NewGuid(),
            endpointId: Guid.NewGuid(),
            receivedAtUtc: DateTimeOffset.UtcNow,
            payloadRaw: "{\"test\":\"failure\"}",
            rawHeaders: new Dictionary<string, string>()
        );

        var act = () => publisher.PublishAsync(envelope);

        var ex = await act.Should().ThrowAsync<WebhookBufferPublishException>();
        ex.Which.IsAmbiguousTimeout.Should().BeFalse();
        ex.Which.CorrelationId.Should().Be(correlationId);
        ex.Which.InnerException.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task PublishAsync_CallerCancelled_ThrowsOperationCanceledException()
    {
        var fakeClient = new FakePubSubPublisherClient
        {
            PublishHandler = async msg =>
            {
                await Task.Delay(5000);
                return "msg-123";
            }
        };

        var options = Options.Create(new GooglePubSubOptions { PublishTimeoutSeconds = 10 });
        var publisher = new GooglePubSubWebhookPublisher(fakeClient, options, NullLogger<GooglePubSubWebhookPublisher>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled

        var envelope = PubSubWebhookEnvelope.Create(
            correlationId: Guid.NewGuid(),
            tenantId: Guid.NewGuid(),
            endpointId: Guid.NewGuid(),
            receivedAtUtc: DateTimeOffset.UtcNow,
            payloadRaw: "{\"test\":\"cancelled\"}",
            rawHeaders: new Dictionary<string, string>()
        );

        var act = () => publisher.PublishAsync(envelope, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Publisher_ReusesPublisherClientAcrossMultipleCalls()
    {
        var fakeClient = new FakePubSubPublisherClient();
        var options = Options.Create(new GooglePubSubOptions { PublishTimeoutSeconds = 5 });
        var publisher = new GooglePubSubWebhookPublisher(fakeClient, options, NullLogger<GooglePubSubWebhookPublisher>.Instance);

        for (int i = 0; i < 5; i++)
        {
            var envelope = PubSubWebhookEnvelope.Create(
                correlationId: Guid.NewGuid(),
                tenantId: Guid.NewGuid(),
                endpointId: Guid.NewGuid(),
                receivedAtUtc: DateTimeOffset.UtcNow,
                payloadRaw: $"{{\"i\":{i}}}",
                rawHeaders: new Dictionary<string, string>()
            );

            await publisher.PublishAsync(envelope);
        }

        fakeClient.PublishedMessages.Should().HaveCount(5);
    }

    [Fact]
    public async Task DisposeAsync_DisposesUnderlyingClient()
    {
        var fakeClient = new FakePubSubPublisherClient();
        var options = Options.Create(new GooglePubSubOptions());
        var publisher = new GooglePubSubWebhookPublisher(fakeClient, options, NullLogger<GooglePubSubWebhookPublisher>.Instance);

        await publisher.DisposeAsync();

        fakeClient.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void TotalMessageSize_WithMax1MBPayload_IsSafelyBelowPubSub10MBLimit()
    {
        // Pub/Sub hard limit is 10 MB (10,485,760 bytes).
        // Webhook size limit is 1 MB (1,048,576 bytes).
        var largePayload = new string('A', 1_048_576);

        var envelope = PubSubWebhookEnvelope.Create(
            correlationId: Guid.NewGuid(),
            tenantId: Guid.NewGuid(),
            endpointId: Guid.NewGuid(),
            receivedAtUtc: DateTimeOffset.UtcNow,
            payloadRaw: largePayload,
            rawHeaders: new Dictionary<string, string>
            {
                { "User-Agent", "facebookexternalua" },
                { "X-Forwarded-For", "192.168.1.100" },
                { "TraceParent", "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01" },
                { "Content-Type", "application/json" }
            },
            sourceIp: "192.168.1.100",
            contentType: "application/json"
        );

        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);

        // Overhead check: Total byte size is ~1.05 MB, well below 10 MB limit
        jsonBytes.Length.Should().BeLessThan(2_000_000);
        jsonBytes.Length.Should().BeLessThan(10_485_760);
    }
}

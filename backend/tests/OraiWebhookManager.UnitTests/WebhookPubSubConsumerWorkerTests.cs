using System.Text;
using System.Text.Json;
using FluentAssertions;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Models;
using OraiWebhookManager.Application.Options;
using OraiWebhookManager.Infrastructure.Persistence;
using OraiWebhookManager.Infrastructure.PubSub;
using OraiWebhookManager.Infrastructure.Workers;
using Xunit;

namespace OraiWebhookManager.UnitTests;

public class WebhookPubSubConsumerWorkerTests
{
    private class FakePubSubSubscriberClient : IPubSubSubscriberClient
    {
        public Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>? MessageHandler { get; private set; }
        public bool IsStarted { get; private set; }
        public bool IsStopped { get; private set; }
        public bool IsDisposed { get; private set; }
        public int StartCallCount { get; private set; }
        public int StopCallCount { get; private set; }
        public TaskCompletionSource<bool> StartedTcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> StoppedTcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> RunningTcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task StartAsync(Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>> handler)
        {
            StartCallCount++;
            IsStarted = true;
            MessageHandler = handler;
            StartedTcs.TrySetResult(true);
            return RunningTcs.Task;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCallCount++;
            IsStopped = true;
            StoppedTcs.TrySetResult(true);
            RunningTcs.TrySetResult(true);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private class FakeDatabaseReadinessChecker : IPubSubDatabaseReadinessChecker
    {
        public bool IsReady { get; set; } = true;
        public string? ErrorMessage { get; set; }
        public int CheckCount { get; private set; }

        public Task<DatabaseReadinessResult> CheckReadinessAsync(CancellationToken cancellationToken = default)
        {
            CheckCount++;
            return Task.FromResult(new DatabaseReadinessResult(IsReady, ErrorMessage));
        }

        public Task EnsureSchemaReadyAsync(CancellationToken cancellationToken = default)
        {
            CheckCount++;
            if (!IsReady)
            {
                throw new InvalidOperationException($"Pub/Sub Consumer database readiness check failed: {ErrorMessage}");
            }
            return Task.CompletedTask;
        }
    }

    private class FakeWebhookInboxRepository : IWebhookInboxRepository
    {
        public Func<PubSubWebhookEnvelope, string, CancellationToken, Task<PubSubInboxEnqueueResult>>? EnqueueHandler { get; set; }
        public List<(PubSubWebhookEnvelope Envelope, string MessageId)> EnqueuedMessages { get; } = new();

        public Task<long> EnqueueAsync(Guid tenantId, Guid endpointId, string payloadRaw, string headersJson, string? ipAddress, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("Consumer worker must never call direct EnqueueAsync");
        }

        public Task<PubSubInboxEnqueueResult> EnqueueFromPubSubAsync(PubSubWebhookEnvelope envelope, string pubsubMessageId, CancellationToken cancellationToken = default)
        {
            EnqueuedMessages.Add((envelope, pubsubMessageId));
            if (EnqueueHandler != null)
            {
                return EnqueueHandler(envelope, pubsubMessageId, cancellationToken);
            }
            return Task.FromResult(new PubSubInboxEnqueueResult(PubSubInboxEnqueueStatus.Created, 12345L));
        }

        public Task<CachedWebhookEndpoint?> GetEndpointByHashAsync(byte[] keyHash, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("Consumer worker must never query endpoint cache directly");
        }
    }

    private class TestLogger<T> : ILogger<T>
    {
        public List<string> LoggedMessages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var msg = formatter(state, exception);
            LoggedMessages.Add(msg);
        }
    }

    private static (WebhookPubSubConsumerWorker worker, FakePubSubSubscriberClient fakeSubscriber, FakeDatabaseReadinessChecker fakeChecker, FakeWebhookInboxRepository fakeRepo, TestLogger<WebhookPubSubConsumerWorker> logger)
        CreateTestContext(Action<GooglePubSubOptions>? configureOptions = null)
    {
        var optionsObj = new GooglePubSubOptions
        {
            EnableSubscriber = true,
            ProjectId = "test-project",
            SubscriptionId = "test-subscription",
            SubscriberClientCount = 1,
            MaxOutstandingElementCount = 100,
            MaxOutstandingByteCount = 20_971_520
        };
        configureOptions?.Invoke(optionsObj);
        var options = Options.Create(optionsObj);

        var fakeSubscriber = new FakePubSubSubscriberClient();
        var fakeChecker = new FakeDatabaseReadinessChecker();
        var fakeRepo = new FakeWebhookInboxRepository();
        var logger = new TestLogger<WebhookPubSubConsumerWorker>();

        var services = new ServiceCollection();
        services.AddScoped<IWebhookInboxRepository>(_ => fakeRepo);
        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var worker = new WebhookPubSubConsumerWorker(
            scopeFactory,
            fakeSubscriber,
            fakeChecker,
            options,
            logger);

        return (worker, fakeSubscriber, fakeChecker, fakeRepo, logger);
    }

    private static PubsubMessage CreateValidPubsubMessage(
        string messageId = "msg_valid_123",
        string payloadRaw = "{\"entry\":[{\"id\":\"123\"}]}",
        int schemaVersion = 1,
        Guid? correlationId = null,
        Guid? tenantId = null,
        Guid? endpointId = null,
        Dictionary<string, string>? filteredHeaders = null)
    {
        var envelope = new
        {
            schema_version = schemaVersion,
            correlation_id = correlationId ?? Guid.NewGuid(),
            tenant_id = tenantId ?? Guid.NewGuid(),
            endpoint_id = endpointId ?? Guid.NewGuid(),
            received_at_utc = DateTimeOffset.UtcNow,
            payload_raw = payloadRaw,
            filtered_headers = filteredHeaders ?? new Dictionary<string, string> { { "User-Agent", "WhatsApp/2.0" } },
            source_ip = "127.0.0.1",
            content_type = "application/json"
        };

        var json = JsonSerializer.Serialize(envelope);
        return new PubsubMessage
        {
            MessageId = messageId,
            Data = ByteString.CopyFromUtf8(json),
            Attributes =
            {
                { "correlation_id", envelope.correlation_id.ToString() },
                { "schema_version", schemaVersion.ToString() }
            }
        };
    }

    [Fact]
    public async Task HandleMessageAsync_CreatedEnqueueResult_ReturnsAck()
    {
        var (worker, _, _, fakeRepo, logger) = CreateTestContext();
        var msg = CreateValidPubsubMessage(messageId: "msg_created_1");

        fakeRepo.EnqueueHandler = (env, msgId, ct) =>
            Task.FromResult(new PubSubInboxEnqueueResult(PubSubInboxEnqueueStatus.Created, 98765L));

        var reply = await worker.HandleMessageAsync(msg, CancellationToken.None);

        reply.Should().Be(SubscriberClient.Reply.Ack);
        fakeRepo.EnqueuedMessages.Should().HaveCount(1);
        fakeRepo.EnqueuedMessages[0].MessageId.Should().Be("msg_created_1");
        logger.LoggedMessages.Should().Contain(m => m.Contains("durably inserted") && m.Contains("98765"));
    }

    [Fact]
    public async Task HandleMessageAsync_AlreadyExistsEnqueueResult_ReturnsAck()
    {
        var (worker, _, _, fakeRepo, logger) = CreateTestContext();
        var msg = CreateValidPubsubMessage(messageId: "msg_dup_1");

        fakeRepo.EnqueueHandler = (env, msgId, ct) =>
            Task.FromResult(new PubSubInboxEnqueueResult(PubSubInboxEnqueueStatus.AlreadyExists, 54321L));

        var reply = await worker.HandleMessageAsync(msg, CancellationToken.None);

        reply.Should().Be(SubscriberClient.Reply.Ack);
        fakeRepo.EnqueuedMessages.Should().HaveCount(1);
        logger.LoggedMessages.Should().Contain(m => m.Contains("duplicate resolved") && m.Contains("54321"));
    }

    [Fact]
    public async Task HandleMessageAsync_RepositoryThrowsException_ReturnsNack()
    {
        var (worker, _, _, fakeRepo, logger) = CreateTestContext();
        var msg = CreateValidPubsubMessage();

        fakeRepo.EnqueueHandler = (env, msgId, ct) =>
            throw new InvalidOperationException("Postgres connection failed");

        var reply = await worker.HandleMessageAsync(msg, CancellationToken.None);

        reply.Should().Be(SubscriberClient.Reply.Nack);
        logger.LoggedMessages.Should().Contain(m => m.Contains("persistence failed"));
    }

    [Fact]
    public async Task HandleMessageAsync_BoundedDuplicateLookupFailure_ReturnsNack()
    {
        var (worker, _, _, fakeRepo, logger) = CreateTestContext();
        var msg = CreateValidPubsubMessage();

        fakeRepo.EnqueueHandler = (env, msgId, ct) =>
            throw new InvalidOperationException("Duplicate Pub/Sub message ID was rejected by unique constraint, but existing inbox record could not be resolved within timeout.");

        var reply = await worker.HandleMessageAsync(msg, CancellationToken.None);

        reply.Should().Be(SubscriberClient.Reply.Nack);
        logger.LoggedMessages.Should().Contain(m => m.Contains("persistence failed"));
    }

    [Fact]
    public async Task HandleMessageAsync_MalformedJsonEnvelope_ReturnsNack()
    {
        var (worker, _, _, fakeRepo, logger) = CreateTestContext();
        var msg = new PubsubMessage
        {
            MessageId = "msg_malformed_1",
            Data = ByteString.CopyFromUtf8("NOT_VALID_JSON{{{")
        };

        var reply = await worker.HandleMessageAsync(msg, CancellationToken.None);

        reply.Should().Be(SubscriberClient.Reply.Nack);
        fakeRepo.EnqueuedMessages.Should().BeEmpty();
        logger.LoggedMessages.Should().Contain(m => m.Contains("malformed JSON envelope"));
    }

    [Fact]
    public async Task HandleMessageAsync_EmptyDataBytes_ReturnsNack()
    {
        var (worker, _, _, fakeRepo, logger) = CreateTestContext();
        var msg = new PubsubMessage
        {
            MessageId = "msg_empty_data",
            Data = ByteString.Empty
        };

        var reply = await worker.HandleMessageAsync(msg, CancellationToken.None);

        reply.Should().Be(SubscriberClient.Reply.Nack);
        fakeRepo.EnqueuedMessages.Should().BeEmpty();
        logger.LoggedMessages.Should().Contain(m => m.Contains("message data is empty"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public async Task HandleMessageAsync_UnsupportedSchemaVersion_ReturnsNack(int unsupportedVersion)
    {
        var (worker, _, _, fakeRepo, logger) = CreateTestContext();
        var msg = CreateValidPubsubMessage(schemaVersion: unsupportedVersion);

        var reply = await worker.HandleMessageAsync(msg, CancellationToken.None);

        reply.Should().Be(SubscriberClient.Reply.Nack);
        fakeRepo.EnqueuedMessages.Should().BeEmpty();
        logger.LoggedMessages.Should().Contain(m => m.Contains("unsupported schema_version"));
    }

    [Fact]
    public async Task HandleMessageAsync_EmptyRequiredGuids_ReturnsNack()
    {
        var (worker, _, _, fakeRepo, logger) = CreateTestContext();
        var msg = CreateValidPubsubMessage(correlationId: Guid.Empty);

        var reply = await worker.HandleMessageAsync(msg, CancellationToken.None);

        reply.Should().Be(SubscriberClient.Reply.Nack);
        fakeRepo.EnqueuedMessages.Should().BeEmpty();
        logger.LoggedMessages.Should().Contain(m => m.Contains("empty required GUID"));
    }

    [Fact]
    public async Task HandleMessageAsync_EmptyPayloadRaw_ReturnsNack()
    {
        var (worker, _, _, fakeRepo, logger) = CreateTestContext();
        var msg = CreateValidPubsubMessage(payloadRaw: "");

        var reply = await worker.HandleMessageAsync(msg, CancellationToken.None);

        reply.Should().Be(SubscriberClient.Reply.Nack);
        fakeRepo.EnqueuedMessages.Should().BeEmpty();
        logger.LoggedMessages.Should().Contain(m => m.Contains("PayloadRaw is empty"));
    }

    [Fact]
    public async Task HandleMessageAsync_InvalidJsonPayloadRaw_ReturnsNack()
    {
        var (worker, _, _, fakeRepo, logger) = CreateTestContext();
        var msg = CreateValidPubsubMessage(payloadRaw: "invalid_json_text");

        var reply = await worker.HandleMessageAsync(msg, CancellationToken.None);

        reply.Should().Be(SubscriberClient.Reply.Nack);
        fakeRepo.EnqueuedMessages.Should().BeEmpty();
        logger.LoggedMessages.Should().Contain(m => m.Contains("PayloadRaw is not valid JSON"));
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("Cookie")]
    [InlineData("Set-Cookie")]
    [InlineData("X-Webhook-Key")]
    [InlineData("X-Hub-Signature-256")]
    [InlineData("Api-Key")]
    [InlineData("x-secret-token")]
    public async Task HandleMessageAsync_ForbiddenSensitiveHeader_ReturnsNack(string forbiddenHeader)
    {
        var (worker, _, _, fakeRepo, logger) = CreateTestContext();
        var msg = CreateValidPubsubMessage(filteredHeaders: new Dictionary<string, string>
        {
            { "User-Agent", "WhatsApp/2.0" },
            { forbiddenHeader, "secret_value" }
        });

        var reply = await worker.HandleMessageAsync(msg, CancellationToken.None);

        reply.Should().Be(SubscriberClient.Reply.Nack);
        fakeRepo.EnqueuedMessages.Should().BeEmpty();
        logger.LoggedMessages.Should().Contain(m => m.Contains("forbidden sensitive header key"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleMessageAsync_BlankMessageId_ReturnsNack(string? invalidMessageId)
    {
        var (worker, _, _, fakeRepo, logger) = CreateTestContext();
        var msg = CreateValidPubsubMessage();
        msg.MessageId = invalidMessageId ?? string.Empty;

        var reply = await worker.HandleMessageAsync(msg, CancellationToken.None);

        reply.Should().Be(SubscriberClient.Reply.Nack);
        fakeRepo.EnqueuedMessages.Should().BeEmpty();
        logger.LoggedMessages.Should().Contain(m => m.Contains("message ID is blank or exceeds 128 chars"));
    }

    [Fact]
    public async Task HandleMessageAsync_OversizedMessageId_ReturnsNack()
    {
        var (worker, _, _, fakeRepo, logger) = CreateTestContext();
        var oversizedId = new string('x', 129);
        var msg = CreateValidPubsubMessage(messageId: oversizedId);

        var reply = await worker.HandleMessageAsync(msg, CancellationToken.None);

        reply.Should().Be(SubscriberClient.Reply.Nack);
        fakeRepo.EnqueuedMessages.Should().BeEmpty();
        logger.LoggedMessages.Should().Contain(m => m.Contains("message ID is blank or exceeds 128 chars"));
    }

    [Fact]
    public async Task HandleMessageAsync_CancellationBeforePersistence_ReturnsNack()
    {
        var (worker, _, _, fakeRepo, logger) = CreateTestContext();
        var msg = CreateValidPubsubMessage();

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Already cancelled

        fakeRepo.EnqueueHandler = (env, msgId, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new PubSubInboxEnqueueResult(PubSubInboxEnqueueStatus.Created, 1L));
        };

        var reply = await worker.HandleMessageAsync(msg, cts.Token);

        reply.Should().Be(SubscriberClient.Reply.Nack);
        logger.LoggedMessages.Should().Contain(m => m.Contains("cancelled during shutdown prior to persistence confirmation"));
    }

    [Fact]
    public async Task HandleMessageAsync_CancellationAfterConfirmedPersistence_ReturnsAck()
    {
        var (worker, _, _, fakeRepo, logger) = CreateTestContext();
        var msg = CreateValidPubsubMessage();

        using var cts = new CancellationTokenSource();

        fakeRepo.EnqueueHandler = (env, msgId, ct) =>
        {
            // Successfully commit to DB, then token is cancelled right after
            cts.Cancel();
            return Task.FromResult(new PubSubInboxEnqueueResult(PubSubInboxEnqueueStatus.Created, 777L));
        };

        var reply = await worker.HandleMessageAsync(msg, cts.Token);

        reply.Should().Be(SubscriberClient.Reply.Ack);
        logger.LoggedMessages.Should().Contain(m => m.Contains("durably inserted") && m.Contains("777"));
    }

    [Fact]
    public async Task ExecuteAsync_DatabaseReadinessFailure_ThrowsAndDoesNotStartSubscriber()
    {
        var (worker, fakeSubscriber, fakeChecker, _, logger) = CreateTestContext();
        fakeChecker.IsReady = false;
        fakeChecker.ErrorMessage = "Column 'pubsub_message_id' does not exist.";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await worker.StartAsync(cts.Token);

        var act = async () => await worker.ExecuteTask!;

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*readiness check failed*Column 'pubsub_message_id' does not exist*");

        fakeSubscriber.IsStarted.Should().BeFalse();
        fakeSubscriber.StartCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDatabaseReady_StartsSubscriberAndGracefullyStops()
    {
        var (worker, fakeSubscriber, fakeChecker, _, logger) = CreateTestContext();
        fakeChecker.IsReady = true;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Start worker
        await worker.StartAsync(cts.Token);

        // Deterministically wait for subscriber StartAsync to be entered
        using var startTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await fakeSubscriber.StartedTcs.Task.WaitAsync(startTimeoutCts.Token);

        fakeSubscriber.IsStarted.Should().BeTrue();
        fakeSubscriber.StartCallCount.Should().Be(1);

        // Stop worker
        using var stopTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StopAsync(stopTimeoutCts.Token);

        await fakeSubscriber.StoppedTcs.Task.WaitAsync(stopTimeoutCts.Token);
        fakeSubscriber.IsStopped.Should().BeTrue();

        // Ensure ExecuteAsync completes cleanly
        await worker.ExecuteTask!.WaitAsync(stopTimeoutCts.Token);

        logger.LoggedMessages.Should().Contain(m => m.Contains("stopped cleanly"));
    }

    [Fact]
    public async Task StopAsync_ToleratesRepeatedCallsSafely()
    {
        var (worker, fakeSubscriber, _, _, _) = CreateTestContext();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await worker.StartAsync(cts.Token);

        using var startTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await fakeSubscriber.StartedTcs.Task.WaitAsync(startTimeoutCts.Token);

        await worker.StopAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        fakeSubscriber.IsStopped.Should().BeTrue();
        fakeSubscriber.StopCallCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task LogsDoNotContainRawPayloadOrSensitiveKeys()
    {
        var (worker, _, _, fakeRepo, logger) = CreateTestContext();
        const string secretPayload = "{\"secret_credit_card\":\"1234-5678-9012-3456\"}";
        var msg = CreateValidPubsubMessage(payloadRaw: secretPayload);

        await worker.HandleMessageAsync(msg, CancellationToken.None);

        foreach (var logMessage in logger.LoggedMessages)
        {
            logMessage.Should().NotContain("1234-5678-9012-3456");
            logMessage.Should().NotContain("secret_credit_card");
        }
    }
}

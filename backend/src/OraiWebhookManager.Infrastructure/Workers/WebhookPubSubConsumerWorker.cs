using System.Diagnostics;
using System.Text.Json;
using Google.Cloud.PubSub.V1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OraiWebhookManager.Application.Helpers;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Models;
using OraiWebhookManager.Application.Options;
using OraiWebhookManager.Infrastructure.Persistence;
using OraiWebhookManager.Infrastructure.PubSub;

namespace OraiWebhookManager.Infrastructure.Workers;

public class WebhookPubSubConsumerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPubSubSubscriberClient _subscriberClient;
    private readonly IPubSubDatabaseReadinessChecker _readinessChecker;
    private readonly GooglePubSubOptions _options;
    private readonly ILogger<WebhookPubSubConsumerWorker> _logger;

    public WebhookPubSubConsumerWorker(
        IServiceScopeFactory scopeFactory,
        IPubSubSubscriberClient subscriberClient,
        IPubSubDatabaseReadinessChecker readinessChecker,
        IOptions<GooglePubSubOptions> options,
        ILogger<WebhookPubSubConsumerWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _subscriberClient = subscriberClient ?? throw new ArgumentNullException(nameof(subscriberClient));
        _readinessChecker = readinessChecker ?? throw new ArgumentNullException(nameof(readinessChecker));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "WebhookPubSubConsumerWorker starting. ProjectId: {ProjectId}, SubscriptionId: {SubscriptionId}",
            _options.ProjectId, _options.SubscriptionId);

        // 1. Verify Database Schema Readiness before pulling messages
        await _readinessChecker.EnsureSchemaReadyAsync(stoppingToken);

        // 2. Register cancellation callback to signal StopAsync to the subscriber client
        using var registration = stoppingToken.Register(() =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await _subscriberClient.StopAsync(shutdownCts.Token);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Exception stopping Pub/Sub subscriber client during cancellation.");
                }
            });
        });

        // 3. Start receiving messages (StartAsync runs until StopAsync is invoked)
        try
        {
            await _subscriberClient.StartAsync(HandleMessageAsync);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected clean shutdown
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Pub/Sub SubscriberClient terminated unexpectedly with error.");
            throw;
        }

        _logger.LogInformation("WebhookPubSubConsumerWorker stopped cleanly.");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            await _subscriberClient.StopAsync(linkedCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error calling StopAsync on Pub/Sub subscriber during worker StopAsync.");
        }

        await base.StopAsync(cancellationToken);
    }

    public async Task<SubscriberClient.Reply> HandleMessageAsync(PubsubMessage message, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var pubsubMessageId = message.MessageId;

        // Step 1: Validate broker message ID
        if (string.IsNullOrWhiteSpace(pubsubMessageId) || pubsubMessageId.Length > 128)
        {
            _logger.LogWarning(
                "Pub/Sub message rejected: message ID is blank or exceeds 128 chars. Length: {Length}",
                pubsubMessageId?.Length ?? 0);
            return SubscriberClient.Reply.Nack;
        }

        // Step 2: Deserialize envelope
        PubSubWebhookEnvelope? envelope;
        try
        {
            if (message.Data == null || message.Data.Length == 0)
            {
                _logger.LogWarning("Pub/Sub message {PubSubMessageId} rejected: message data is empty.", pubsubMessageId);
                return SubscriberClient.Reply.Nack;
            }

            envelope = JsonSerializer.Deserialize<PubSubWebhookEnvelope>(message.Data.ToByteArray());
            if (envelope == null)
            {
                _logger.LogWarning("Pub/Sub message {PubSubMessageId} rejected: deserialized envelope is null.", pubsubMessageId);
                return SubscriberClient.Reply.Nack;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pub/Sub message {PubSubMessageId} rejected: malformed JSON envelope.", pubsubMessageId);
            return SubscriberClient.Reply.Nack;
        }

        // Step 3: Validate transport invariants
        if (envelope.SchemaVersion != 1)
        {
            _logger.LogWarning(
                "Pub/Sub message {PubSubMessageId} rejected: unsupported schema_version {SchemaVersion}. Expected 1.",
                pubsubMessageId, envelope.SchemaVersion);
            return SubscriberClient.Reply.Nack;
        }

        if (envelope.CorrelationId == Guid.Empty || envelope.TenantId == Guid.Empty || envelope.EndpointId == Guid.Empty)
        {
            _logger.LogWarning(
                "Pub/Sub message {PubSubMessageId} rejected: empty required GUID(s). CorrelationId: {CorrelationId}, TenantId: {TenantId}, EndpointId: {EndpointId}",
                pubsubMessageId, envelope.CorrelationId, envelope.TenantId, envelope.EndpointId);
            return SubscriberClient.Reply.Nack;
        }

        if (envelope.ReceivedAtUtc == default)
        {
            _logger.LogWarning(
                "Pub/Sub message {PubSubMessageId} rejected: invalid default ReceivedAtUtc.",
                pubsubMessageId);
            return SubscriberClient.Reply.Nack;
        }

        if (string.IsNullOrWhiteSpace(envelope.PayloadRaw))
        {
            _logger.LogWarning(
                "Pub/Sub message {PubSubMessageId} rejected: PayloadRaw is empty or whitespace.",
                pubsubMessageId);
            return SubscriberClient.Reply.Nack;
        }

        // Step 4: Verify PayloadRaw is syntactically valid JSON (PostgreSQL jsonb invariant)
        try
        {
            using var _ = JsonDocument.Parse(envelope.PayloadRaw);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Pub/Sub message {PubSubMessageId} rejected: PayloadRaw is not valid JSON.",
                pubsubMessageId);
            return SubscriberClient.Reply.Nack;
        }

        // Step 5: Verify FilteredHeaders contains no forbidden sensitive keys
        if (envelope.FilteredHeaders != null)
        {
            foreach (var key in envelope.FilteredHeaders.Keys)
            {
                if (WebhookHeaderSanitizer.IsSensitiveHeader(key))
                {
                    _logger.LogWarning(
                        "Pub/Sub message {PubSubMessageId} rejected: contains forbidden sensitive header key '{HeaderKey}'.",
                        pubsubMessageId, key);
                    return SubscriberClient.Reply.Nack;
                }
            }
        }

        // Step 6: Persist to webhook_inbox via repository
        PubSubInboxEnqueueResult enqueueResult;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IWebhookInboxRepository>();

            enqueueResult = await repository.EnqueueFromPubSubAsync(envelope, pubsubMessageId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogWarning(
                "Pub/Sub message {PubSubMessageId} processing cancelled during shutdown prior to persistence confirmation. DurationMs: {DurationMs}",
                pubsubMessageId, sw.ElapsedMilliseconds);
            return SubscriberClient.Reply.Nack;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(
                ex,
                "Pub/Sub message {PubSubMessageId} persistence failed. CorrelationId: {CorrelationId}, TenantId: {TenantId}, EndpointId: {EndpointId}, DurationMs: {DurationMs}, Error: {ErrorMessage}",
                pubsubMessageId, envelope.CorrelationId, envelope.TenantId, envelope.EndpointId, sw.ElapsedMilliseconds, ex.Message);
            return SubscriberClient.Reply.Nack;
        }

        sw.Stop();

        // Step 7: ACK confirmed persistence (Created or AlreadyExists)
        if (enqueueResult.Status == PubSubInboxEnqueueStatus.Created)
        {
            _logger.LogInformation(
                "Pub/Sub message {PubSubMessageId} durably inserted into webhook_inbox. InboxId: {InboxId}, CorrelationId: {CorrelationId}, TenantId: {TenantId}, EndpointId: {EndpointId}, Result: Created, DurationMs: {DurationMs}",
                pubsubMessageId, enqueueResult.InboxId, envelope.CorrelationId, envelope.TenantId, envelope.EndpointId, sw.ElapsedMilliseconds);
            return SubscriberClient.Reply.Ack;
        }

        if (enqueueResult.Status == PubSubInboxEnqueueStatus.AlreadyExists)
        {
            _logger.LogInformation(
                "Pub/Sub message {PubSubMessageId} duplicate resolved. InboxId: {InboxId}, CorrelationId: {CorrelationId}, TenantId: {TenantId}, EndpointId: {EndpointId}, Result: AlreadyExists, DurationMs: {DurationMs}",
                pubsubMessageId, enqueueResult.InboxId, envelope.CorrelationId, envelope.TenantId, envelope.EndpointId, sw.ElapsedMilliseconds);
            return SubscriberClient.Reply.Ack;
        }

        return SubscriberClient.Reply.Nack;
    }
}

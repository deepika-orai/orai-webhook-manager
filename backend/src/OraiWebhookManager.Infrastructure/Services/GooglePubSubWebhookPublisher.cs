using System.Diagnostics;
using System.Text.Json;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OraiWebhookManager.Application.Exceptions;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Models;
using OraiWebhookManager.Application.Options;
using OraiWebhookManager.Infrastructure.PubSub;

namespace OraiWebhookManager.Infrastructure.Services;

public sealed class GooglePubSubWebhookPublisher : IWebhookBufferPublisher, IAsyncDisposable
{
    private readonly IPubSubPublisherClient _client;
    private readonly GooglePubSubOptions _options;
    private readonly ILogger<GooglePubSubWebhookPublisher> _logger;

    public GooglePubSubWebhookPublisher(
        IPubSubPublisherClient client,
        IOptions<GooglePubSubOptions> options,
        ILogger<GooglePubSubWebhookPublisher> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> PublishAsync(PubSubWebhookEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);

        var pubsubMessage = new PubsubMessage
        {
            Data = ByteString.CopyFrom(jsonBytes),
            Attributes =
            {
                { "correlation_id", envelope.CorrelationId.ToString() },
                { "tenant_id", envelope.TenantId.ToString() },
                { "endpoint_id", envelope.EndpointId.ToString() },
                { "schema_version", envelope.SchemaVersion.ToString() }
            }
        };

        var sw = Stopwatch.StartNew();
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.PublishTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var publishTask = _client.PublishAsync(pubsubMessage);
            var messageId = await publishTask.WaitAsync(linkedCts.Token);
            sw.Stop();

            _logger.LogInformation(
                "Pub/Sub webhook envelope published successfully. CorrelationId: {CorrelationId}, TenantId: {TenantId}, EndpointId: {EndpointId}, PubSubMessageId: {PubSubMessageId}, PayloadBytes: {PayloadBytes}, DurationMs: {DurationMs}",
                envelope.CorrelationId,
                envelope.TenantId,
                envelope.EndpointId,
                messageId,
                jsonBytes.Length,
                sw.ElapsedMilliseconds);

            return messageId;
        }
        catch (OperationCanceledException ex)
        {
            sw.Stop();

            bool isCallerCancelled = cancellationToken.IsCancellationRequested;
            bool isTimeout = !isCallerCancelled && timeoutCts.IsCancellationRequested;

            if (isTimeout)
            {
                _logger.LogWarning(
                    ex,
                    "Pub/Sub publish timed out after {TimeoutSeconds}s (ambiguous publish state - underlying broker publish may still complete). CorrelationId: {CorrelationId}, TenantId: {TenantId}, EndpointId: {EndpointId}, PayloadBytes: {PayloadBytes}, DurationMs: {DurationMs}",
                    _options.PublishTimeoutSeconds,
                    envelope.CorrelationId,
                    envelope.TenantId,
                    envelope.EndpointId,
                    jsonBytes.Length,
                    sw.ElapsedMilliseconds);

                throw new WebhookBufferPublishException(
                    $"Pub/Sub publish timed out after {_options.PublishTimeoutSeconds} seconds. Publish outcome is ambiguous.",
                    envelope.CorrelationId,
                    isAmbiguousTimeout: true,
                    innerException: ex);
            }

            _logger.LogWarning(
                ex,
                "Pub/Sub publish was cancelled by caller. CorrelationId: {CorrelationId}, TenantId: {TenantId}, EndpointId: {EndpointId}, DurationMs: {DurationMs}",
                envelope.CorrelationId,
                envelope.TenantId,
                envelope.EndpointId,
                sw.ElapsedMilliseconds);

            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();

            _logger.LogError(
                ex,
                "Pub/Sub publish failed with error. CorrelationId: {CorrelationId}, TenantId: {TenantId}, EndpointId: {EndpointId}, PayloadBytes: {PayloadBytes}, DurationMs: {DurationMs}, Error: {ErrorMessage}",
                envelope.CorrelationId,
                envelope.TenantId,
                envelope.EndpointId,
                jsonBytes.Length,
                sw.ElapsedMilliseconds,
                ex.Message);

            throw new WebhookBufferPublishException(
                $"Failed to publish webhook to Pub/Sub: {ex.Message}",
                envelope.CorrelationId,
                isAmbiguousTimeout: false,
                innerException: ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
    }
}

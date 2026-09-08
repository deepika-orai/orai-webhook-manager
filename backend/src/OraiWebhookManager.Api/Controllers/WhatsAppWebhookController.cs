using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OraiWebhookManager.Application.Exceptions;
using OraiWebhookManager.Application.Helpers;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Models;
using OraiWebhookManager.Application.Options;
using OraiWebhookManager.Domain.Enums;

namespace OraiWebhookManager.Api.Controllers;

[ApiController]
[Route("api/webhooks/whatsapp")]
public class WhatsAppWebhookController : ControllerBase
{
    private readonly IWebhookKeyService _keyService;
    private readonly IWebhookInboxRepository _inboxRepository;
    private readonly IWebhookBufferPublisher _bufferPublisher;
    private readonly IMemoryCache _memoryCache;
    private readonly WebhookIngestionOptions _options;
    private readonly GooglePubSubOptions _pubSubOptions;
    private readonly ILogger<WhatsAppWebhookController> _logger;

    private static readonly HashSet<string> AllowlistedHeaders = WebhookHeaderSanitizer.DirectIngestionAllowlistedHeaders;

    public WhatsAppWebhookController(
        IWebhookKeyService keyService,
        IWebhookInboxRepository inboxRepository,
        IWebhookBufferPublisher bufferPublisher,
        IMemoryCache memoryCache,
        IOptions<WebhookIngestionOptions> options,
        IOptions<GooglePubSubOptions> pubSubOptions,
        ILogger<WhatsAppWebhookController> logger)
    {
        _keyService = keyService;
        _inboxRepository = inboxRepository;
        _bufferPublisher = bufferPublisher;
        _memoryCache = memoryCache;
        _options = options.Value;
        _pubSubOptions = pubSubOptions.Value;
        _logger = logger;
    }

    [HttpPost("{webhookKey}")]
    [RequestSizeLimit(1_048_576)] // 1 MB payload limit
    public async Task<IActionResult> IngestWebhook(
        [FromRoute] string webhookKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(webhookKey))
        {
            return Unauthorized(new { error = "Invalid webhook key format." });
        }

        // Compute SHA-256 hash as bytea
        var keyHash = _keyService.ComputeKeyHash(webhookKey);
        var cacheKey = $"whk_endpoint_{Convert.ToHexString(keyHash)}";

        if (!_memoryCache.TryGetValue(cacheKey, out CachedWebhookEndpoint? endpoint))
        {
            endpoint = await _inboxRepository.GetEndpointByHashAsync(keyHash, cancellationToken);
            if (endpoint != null)
            {
                _memoryCache.Set(cacheKey, endpoint, TimeSpan.FromSeconds(_options.CacheTtlSeconds));
            }
        }

        if (endpoint == null || endpoint.Status != WebhookEndpointStatus.Active)
        {
            return Unauthorized(new { error = "Webhook endpoint is invalid, inactive, or revoked." });
        }

        // Read raw body
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var rawPayload = await reader.ReadToEndAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return BadRequest(new { error = "Webhook payload cannot be empty." });
        }

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        // 1. Direct PostgreSQL ingestion path (when UsePubSubBuffer is false)
        if (!_pubSubOptions.UsePubSubBuffer)
        {
            // Extract allowlisted headers only
            var headerDict = new Dictionary<string, string>();
            foreach (var header in Request.Headers)
            {
                if (AllowlistedHeaders.Contains(header.Key))
                {
                    headerDict[header.Key] = header.Value.ToString();
                }
            }

            var headersJson = JsonSerializer.Serialize(headerDict);

            // Durable ingestion into webhook_inbox
            var inboxId = await _inboxRepository.EnqueueAsync(
                endpoint.TenantId,
                endpoint.Id,
                rawPayload,
                headersJson,
                ipAddress,
                cancellationToken
            );

            return Ok(new
            {
                received = true,
                inbox_id = inboxId
            });
        }

        // 2. Google Cloud Pub/Sub buffered path (when UsePubSubBuffer is true)
        var correlationId = Guid.NewGuid();
        var traceId = HttpContext.TraceIdentifier;

        var envelope = PubSubWebhookEnvelope.Create(
            correlationId: correlationId,
            tenantId: endpoint.TenantId,
            endpointId: endpoint.Id,
            receivedAtUtc: DateTimeOffset.UtcNow,
            payloadRaw: rawPayload,
            rawHeaders: Request.Headers.Select(h => new KeyValuePair<string, string>(h.Key, h.Value.ToString())),
            sourceIp: ipAddress,
            contentType: Request.ContentType
        );

        try
        {
            var messageId = await _bufferPublisher.PublishAsync(envelope, cancellationToken);

            return Ok(new
            {
                received = true,
                buffered = true,
                correlation_id = correlationId.ToString(),
                queue_message_id = messageId,
                inbox_id = (long?)null
            });
        }
        catch (WebhookBufferPublishException ex)
        {
            _logger.LogError(
                ex,
                "Pub/Sub ingestion failed. Returning HTTP 503. CorrelationId: {CorrelationId}, TraceId: {TraceId}, TenantId: {TenantId}, EndpointId: {EndpointId}, IsAmbiguousTimeout: {IsAmbiguousTimeout}",
                correlationId,
                traceId,
                endpoint.TenantId,
                endpoint.Id,
                ex.IsAmbiguousTimeout);

            Response.Headers.RetryAfter = "5";
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = "Webhook ingestion buffer is temporarily unavailable. Please retry.",
                correlation_id = correlationId.ToString(),
                retry_after_seconds = 5
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Client closed connection / request was cancelled upstream
            return StatusCode(499);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error during Pub/Sub buffered ingestion. CorrelationId: {CorrelationId}, TraceId: {TraceId}, TenantId: {TenantId}, EndpointId: {EndpointId}",
                correlationId,
                traceId,
                endpoint.TenantId,
                endpoint.Id);

            Response.Headers.RetryAfter = "5";
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = "Webhook ingestion buffer is temporarily unavailable. Please retry.",
                correlation_id = correlationId.ToString(),
                retry_after_seconds = 5
            });
        }
    }
}

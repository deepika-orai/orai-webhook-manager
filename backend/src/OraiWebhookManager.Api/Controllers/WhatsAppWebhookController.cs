using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Options;
using OraiWebhookManager.Domain.Enums;

namespace OraiWebhookManager.Api.Controllers;

[ApiController]
[Route("api/webhooks/whatsapp")]
public class WhatsAppWebhookController : ControllerBase
{
    private readonly IWebhookKeyService _keyService;
    private readonly IWebhookInboxRepository _inboxRepository;
    private readonly IMemoryCache _memoryCache;
    private readonly WebhookIngestionOptions _options;
    private readonly ILogger<WhatsAppWebhookController> _logger;

    private static readonly HashSet<string> AllowlistedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "User-Agent",
        "X-Hub-Signature-256",
        "X-Forwarded-For",
        "TraceParent",
        "Content-Type"
    };

    public WhatsAppWebhookController(
        IWebhookKeyService keyService,
        IWebhookInboxRepository inboxRepository,
        IMemoryCache memoryCache,
        IOptions<WebhookIngestionOptions> options,
        ILogger<WhatsAppWebhookController> logger)
    {
        _keyService = keyService;
        _inboxRepository = inboxRepository;
        _memoryCache = memoryCache;
        _options = options.Value;
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
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

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
}

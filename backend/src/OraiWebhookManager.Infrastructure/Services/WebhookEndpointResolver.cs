using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Options;
using OraiWebhookManager.Domain.Enums;

namespace OraiWebhookManager.Infrastructure.Services;

public class WebhookEndpointResolver : IWebhookEndpointResolver
{
    private readonly IWebhookKeyService _keyService;
    private readonly IWebhookInboxRepository _inboxRepository;
    private readonly IMemoryCache _memoryCache;
    private readonly WebhookIngestionOptions _options;

    public WebhookEndpointResolver(
        IWebhookKeyService keyService,
        IWebhookInboxRepository inboxRepository,
        IMemoryCache memoryCache,
        IOptions<WebhookIngestionOptions> options)
    {
        _keyService = keyService;
        _inboxRepository = inboxRepository;
        _memoryCache = memoryCache;
        _options = options.Value;
    }

    public async Task<WebhookEndpointResolutionResult> ResolveEndpointAsync(string webhookKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(webhookKey))
        {
            return new WebhookEndpointResolutionResult(WebhookEndpointResolutionStatus.InvalidKey, null);
        }

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

        if (endpoint == null)
        {
            return new WebhookEndpointResolutionResult(WebhookEndpointResolutionStatus.InvalidKey, null);
        }

        if (endpoint.Status != WebhookEndpointStatus.Active)
        {
            return new WebhookEndpointResolutionResult(WebhookEndpointResolutionStatus.InactiveOrRevoked, endpoint);
        }

        return new WebhookEndpointResolutionResult(WebhookEndpointResolutionStatus.Success, endpoint);
    }
}

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using OraiWebhookManager.Application.Interfaces;

namespace OraiWebhookManager.Infrastructure.Services;

public class CacheInvalidationService : ICacheInvalidator
{
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<CacheInvalidationService> _logger;

    public CacheInvalidationService(IMemoryCache memoryCache, ILogger<CacheInvalidationService> logger)
    {
        _memoryCache = memoryCache;
        _logger = logger;
    }

    public Task PublishEndpointInvalidationAsync(byte[] keyHash, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"whk_endpoint_{Convert.ToHexString(keyHash)}";
        _memoryCache.Remove(cacheKey);
        _logger.LogInformation("Evicted webhook endpoint cache key: {CacheKey}", cacheKey);

        // In multi-instance deployments, a PostgreSQL LISTEN/NOTIFY or Redis pub/sub channel
        // can be triggered here to evict the key across all peer API nodes.
        return Task.CompletedTask;
    }
}

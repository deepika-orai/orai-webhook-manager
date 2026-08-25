using OraiWebhookManager.Application.Models;
using OraiWebhookManager.Domain.Entities;
using OraiWebhookManager.Domain.Enums;

namespace OraiWebhookManager.Application.Interfaces;

public record WebhookKeyGenerateResult(string PlainKey, string KeyPrefix, byte[] KeyHash);

public record CachedWebhookEndpoint(
    Guid Id,
    Guid TenantId,
    string Name,
    string KeyPrefix,
    byte[] KeyHash,
    WebhookEndpointStatus Status
);

public interface IWebhookKeyService
{
    WebhookKeyGenerateResult GenerateKey();
    byte[] ComputeKeyHash(string plainKey);
    string ExtractPrefix(string plainKey);
}

public interface IMetaWebhookParser
{
    IReadOnlyList<ExtractedStatusEvent> ExtractStatusEvents(string rawJson);
}

public interface ICurrentUserContext
{
    Guid? TenantId { get; }
    Guid? UserId { get; }
    bool IsPlatformAdmin { get; }
    TenantRole? Role { get; }
}

public interface IWebhookInboxRepository
{
    Task<long> EnqueueAsync(
        Guid tenantId,
        Guid endpointId,
        string payloadRaw,
        string headersJson,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<CachedWebhookEndpoint?> GetEndpointByHashAsync(byte[] keyHash, CancellationToken cancellationToken = default);
}

public interface IWebhookProcessorRepository
{
    Task<IReadOnlyList<WebhookInboxItem>> ClaimBatchAsync(
        Guid lockToken,
        string lockedBy,
        int batchSize,
        int leaseDurationSeconds,
        CancellationToken cancellationToken = default);

    Task<bool> ProcessItemAtomicAsync(
        WebhookInboxItem item,
        Guid lockToken,
        IReadOnlyList<ExtractedStatusEvent> events,
        CancellationToken cancellationToken = default);

    Task RecordFailureAsync(
        long inboxId,
        Guid lockToken,
        string errorMessage,
        int attemptCount,
        int maxAttempts,
        CancellationToken cancellationToken = default);
}

public interface ICacheInvalidator
{
    Task PublishEndpointInvalidationAsync(byte[] keyHash, CancellationToken cancellationToken = default);
}

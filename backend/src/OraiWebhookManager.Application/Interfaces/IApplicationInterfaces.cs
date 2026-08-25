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

public interface IDashboardRepository
{
    Task<DashboardSummaryDto> GetSummaryAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<PagedResult<MessageListItemDto>> GetMessagesAsync(Guid tenantId, MessageFilterParams filter, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MessageStatusEventDto>?> GetMessageEventsAsync(Guid tenantId, Guid messageId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WebhookEndpointDto>> GetWebhookEndpointsAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<bool> ValidateTenantActiveAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public interface IPasswordService
{
    string HashPassword(User user, string password);
    bool VerifyPassword(User user, string hashedPassword, string providedPassword);
    string GenerateSecurePassword(int length = 16);
}

public interface IJwtTokenService
{
    string GenerateAccessToken(User user, TenantMembership? membership, Guid? sessionId = null);
    (string PlainRefreshToken, byte[] TokenHash) GenerateRefreshToken();
    byte[] HashRefreshToken(string plainToken);
}

public interface IAuthService
{
    Task<LoginResult> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<RefreshResult> RefreshSessionAsync(string plainRefreshToken, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<bool> LogoutAsync(string? plainRefreshToken, CancellationToken cancellationToken = default);
    Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default);
    Task<AuthSessionDto?> GetCurrentSessionAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> BootstrapAdminAsync(string email, string password, string fullName = "Super Admin", CancellationToken cancellationToken = default);
}

public interface IAdminService
{
    Task<PagedResult<AdminTenantListItemDto>> GetTenantsAsync(AdminTenantFilterParams filter, CancellationToken cancellationToken = default);
    Task<AdminTenantSummaryDto?> GetTenantSummaryAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<CreateTenantResult> CreateTenantAsync(CreateTenantRequest request, Guid adminUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<bool> UpdateTenantStatusAsync(Guid tenantId, bool isActive, Guid adminUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<ResetClientPasswordResult> ResetClientPasswordAsync(Guid tenantId, Guid adminUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<RotateKeyResult> RotateWebhookKeyAsync(Guid endpointId, Guid adminUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<PlatformSummaryDto> GetPlatformSummaryAsync(CancellationToken cancellationToken = default);
}

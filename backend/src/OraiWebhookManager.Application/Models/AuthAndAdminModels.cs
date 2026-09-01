using OraiWebhookManager.Domain.Enums;

namespace OraiWebhookManager.Application.Models;

public record LoginRequest(string Email, string Password);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record UserDto(
    Guid Id,
    string Email,
    string FullName,
    bool IsPlatformAdmin,
    bool MustChangePassword,
    bool IsActive
);

public record TenantDto(
    Guid Id,
    string Name,
    string Slug,
    bool IsActive,
    TenantRole Role
);

public record AuthSessionDto(
    UserDto User,
    TenantDto? Tenant
);

public record LoginResult(
    bool Succeeded,
    string? ErrorMessage,
    string? AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    UserDto? User,
    TenantDto? Tenant,
    bool MustChangePassword = false
);

public record RefreshResult(
    bool Succeeded,
    string? ErrorMessage,
    string? AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    UserDto? User,
    TenantDto? Tenant,
    bool MustChangePassword = false
);

public record CreateTenantRequest(
    string Name,
    string Slug,
    string AdminEmail,
    string AdminFullName
);

public record CreateTenantResult(
    Guid TenantId,
    string Name,
    string Slug,
    Guid AdminUserId,
    string AdminEmail,
    string TempPassword,
    Guid WebhookEndpointId,
    string WebhookEndpointName,
    string WebhookUrl,
    string WebhookPlainKey,
    string WebhookKeyPrefix
);

public record AdminTenantListItemDto(
    Guid Id,
    string Name,
    string Slug,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int EndpointsCount,
    int MessagesCount,
    string? AdminEmail,
    string? AdminFullName
);

public record AdminTenantUserDto(
    Guid UserId,
    string Email,
    string FullName,
    TenantRole Role,
    bool IsActive,
    bool MustChangePassword,
    DateTimeOffset CreatedAt
);

public record AdminTenantEndpointDto(
    Guid EndpointId,
    string Name,
    string KeyPrefix,
    WebhookEndpointStatus Status,
    DateTimeOffset? LastReceivedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset CreatedAt
);

public record AdminTenantSummaryDto(
    Guid Id,
    string Name,
    string Slug,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<AdminTenantUserDto> Users,
    IReadOnlyList<AdminTenantEndpointDto> Endpoints,
    long TotalMessages,
    long FailedMessages
);

public record UpdateTenantStatusRequest(bool IsActive);

public record ResetClientPasswordResult(
    Guid UserId,
    string Email,
    string TempPassword
);

public record RotateKeyResult(
    Guid EndpointId,
    string PlainKey,
    string KeyPrefix,
    string WebhookUrl
);

public record AdminTenantFilterParams(
    string? Search = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 20
);

public record PlatformSummaryDto(
    int TotalTenants,
    int ActiveTenants,
    int SuspendedTenants,
    long TotalMessages,
    long FailedMessages,
    long PendingInbox,
    long DeadLetterInbox
);

using System.Security.Claims;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Domain.Enums;

namespace OraiWebhookManager.Api.Services;

public class CurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? TenantId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            var tenantClaim = user?.FindFirst("tenant_id")?.Value
                ?? _httpContextAccessor.HttpContext?.Request.Headers["X-Tenant-Id"].FirstOrDefault();

            return Guid.TryParse(tenantClaim, out var parsed) ? parsed : null;
        }
    }

    public Guid? UserId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            var userClaim = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user?.FindFirst("sub")?.Value;

            return Guid.TryParse(userClaim, out var parsed) ? parsed : null;
        }
    }

    public bool IsPlatformAdmin
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            return user?.IsInRole("PlatformAdmin") ?? false;
        }
    }

    public TenantRole? Role
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            var roleClaim = user?.FindFirst(ClaimTypes.Role)?.Value ?? user?.FindFirst("tenant_role")?.Value;

            return Enum.TryParse<TenantRole>(roleClaim, true, out var role) ? role : null;
        }
    }
}

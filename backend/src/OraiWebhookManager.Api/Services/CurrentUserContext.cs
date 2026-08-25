using System.Security.Claims;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Domain.Enums;

namespace OraiWebhookManager.Api.Services;

public class CurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IWebHostEnvironment _environment;

    public CurrentUserContext(IHttpContextAccessor httpContextAccessor, IWebHostEnvironment environment)
    {
        _httpContextAccessor = httpContextAccessor;
        _environment = environment;
    }

    public Guid? TenantId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            var isAuthenticated = user?.Identity?.IsAuthenticated ?? false;

            if (isAuthenticated)
            {
                var tenantClaim = user?.FindFirst("tenant_id")?.Value;
                if (Guid.TryParse(tenantClaim, out var parsed))
                {
                    return parsed;
                }

                if (IsPlatformAdmin)
                {
                    var tenantHeader = _httpContextAccessor.HttpContext?.Request.Headers["X-Tenant-Id"].FirstOrDefault();
                    if (Guid.TryParse(tenantHeader, out var inspectedTenantId))
                    {
                        return inspectedTenantId;
                    }
                }

                return null;
            }

            // In Development ONLY, allow fallback to X-Tenant-Id header for unauthenticated MVP testing
            if (_environment.IsDevelopment())
            {
                var tenantHeader = _httpContextAccessor.HttpContext?.Request.Headers["X-Tenant-Id"].FirstOrDefault();
                return Guid.TryParse(tenantHeader, out var parsed) ? parsed : null;
            }

            return null;
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
            return (user?.IsInRole("PlatformAdmin") ?? false)
                || (user?.FindFirst("is_platform_admin")?.Value == "true");
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

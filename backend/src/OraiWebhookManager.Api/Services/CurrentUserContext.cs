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
            var tenantClaim = user?.FindFirst("tenant_id")?.Value;

            // In Development ONLY, allow fallback to X-Tenant-Id header for MVP testing
            if (string.IsNullOrEmpty(tenantClaim) && _environment.IsDevelopment())
            {
                tenantClaim = _httpContextAccessor.HttpContext?.Request.Headers["X-Tenant-Id"].FirstOrDefault();
            }

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

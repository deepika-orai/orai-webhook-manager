using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Models;
using OraiWebhookManager.Domain.Entities;
using OraiWebhookManager.Domain.Enums;
using OraiWebhookManager.Infrastructure.Persistence;

namespace OraiWebhookManager.Infrastructure.Services;

public class AdminService : IAdminService
{
    private readonly IPlatformAdminDbContext _dbContext;
    private readonly IPasswordService _passwordService;
    private readonly IWebhookKeyService _webhookKeyService;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly ILogger<AdminService> _logger;

    public AdminService(
        IPlatformAdminDbContext dbContext,
        IPasswordService passwordService,
        IWebhookKeyService webhookKeyService,
        ICacheInvalidator cacheInvalidator,
        ILogger<AdminService> logger)
    {
        _dbContext = dbContext;
        _passwordService = passwordService;
        _webhookKeyService = webhookKeyService;
        _cacheInvalidator = cacheInvalidator;
        _logger = logger;
    }

    public async Task<PagedResult<AdminTenantListItemDto>> GetTenantsAsync(
        AdminTenantFilterParams filter,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Tenants.AsNoTracking().AsQueryable();

        if (filter.IsActive.HasValue)
        {
            query = query.Where(t => t.IsActive == filter.IsActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToLower();
            query = query.Where(t =>
                t.Name.ToLower().Contains(search) ||
                t.Slug.ToLower().Contains(search) ||
                t.Memberships.Any(m => m.User != null && (m.User.Email.ToLower().Contains(search) || m.User.FullName.ToLower().Contains(search)))
            );
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var tenants = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.Slug,
                t.IsActive,
                t.CreatedAt,
                t.UpdatedAt,
                EndpointsCount = t.WebhookEndpoints.Count(),
                MessagesCount = t.Messages.Count(),
                AdminUser = t.Memberships
                    .Where(m => m.Role == TenantRole.TenantAdmin && m.User != null)
                    .Select(m => new { m.User!.Email, m.User.FullName })
                    .FirstOrDefault() ??
                    t.Memberships
                    .Where(m => m.User != null)
                    .Select(m => new { m.User!.Email, m.User.FullName })
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var items = tenants.Select(t => new AdminTenantListItemDto(
            t.Id,
            t.Name,
            t.Slug,
            t.IsActive,
            t.CreatedAt,
            t.UpdatedAt,
            t.EndpointsCount,
            t.MessagesCount,
            t.AdminUser?.Email,
            t.AdminUser?.FullName
        )).ToList();

        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
        return new PagedResult<AdminTenantListItemDto>(items, totalCount, page, pageSize, totalPages);
    }

    public async Task<AdminTenantSummaryDto?> GetTenantSummaryAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _dbContext.Tenants
            .AsNoTracking()
            .Include(t => t.Memberships)
                .ThenInclude(m => m.User)
            .Include(t => t.WebhookEndpoints)
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

        if (tenant == null)
        {
            return null;
        }

        var totalMessages = await _dbContext.Messages.CountAsync(m => m.TenantId == tenantId, cancellationToken);
        var failedMessages = await _dbContext.Messages.CountAsync(m => m.TenantId == tenantId && m.CurrentStatus == "failed", cancellationToken);

        var users = tenant.Memberships
            .Where(m => m.User != null)
            .Select(m => new AdminTenantUserDto(
                m.User!.Id,
                m.User.Email,
                m.User.FullName,
                m.Role,
                m.User.IsActive && m.IsActive,
                m.User.MustChangePassword,
                m.CreatedAt
            ))
            .ToList();

        var endpoints = tenant.WebhookEndpoints
            .Select(e => new AdminTenantEndpointDto(
                e.Id,
                e.Name,
                e.KeyPrefix,
                e.Status,
                e.LastReceivedAt,
                e.RevokedAt,
                e.CreatedAt
            ))
            .ToList();

        return new AdminTenantSummaryDto(
            tenant.Id,
            tenant.Name,
            tenant.Slug,
            tenant.IsActive,
            tenant.CreatedAt,
            tenant.UpdatedAt,
            users,
            endpoints,
            totalMessages,
            failedMessages
        );
    }

    public async Task<CreateTenantResult> CreateTenantAsync(
        CreateTenantRequest request,
        Guid adminUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Tenant name is required.", nameof(request.Name));
        if (string.IsNullOrWhiteSpace(request.Slug))
            throw new ArgumentException("Tenant slug is required.", nameof(request.Slug));
        if (string.IsNullOrWhiteSpace(request.AdminEmail))
            throw new ArgumentException("Admin email is required.", nameof(request.AdminEmail));
        if (string.IsNullOrWhiteSpace(request.AdminFullName))
            throw new ArgumentException("Admin full name is required.", nameof(request.AdminFullName));

        var cleanSlug = request.Slug.Trim().ToLowerInvariant();
        var cleanEmail = request.AdminEmail.Trim().ToLowerInvariant();

        var slugExists = await _dbContext.Tenants.AnyAsync(t => t.Slug == cleanSlug, cancellationToken);
        if (slugExists)
        {
            throw new InvalidOperationException($"A tenant with slug '{cleanSlug}' already exists.");
        }

        var emailExists = await _dbContext.Users.AnyAsync(u => u.Email == cleanEmail, cancellationToken);
        if (emailExists)
        {
            throw new InvalidOperationException($"A user with email '{cleanEmail}' already exists.");
        }

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Slug = cleanSlug,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var tempPassword = _passwordService.GenerateSecurePassword(16);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = cleanEmail,
            FullName = request.AdminFullName.Trim(),
            IsPlatformAdmin = false,
            MustChangePassword = true,
            EmailConfirmed = true,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        user.PasswordHash = _passwordService.HashPassword(user, tempPassword);

        var membership = new TenantMembership
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserId = user.Id,
            Role = TenantRole.TenantAdmin,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var keyGen = _webhookKeyService.GenerateKey();

        var endpoint = new WebhookEndpoint
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = "Default WhatsApp Ingestion",
            KeyPrefix = keyGen.KeyPrefix,
            KeyHash = keyGen.KeyHash,
            Status = WebhookEndpointStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var audit = new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserId = adminUserId,
            Action = "Tenant.Created",
            EntityType = "Tenant",
            EntityId = tenant.Id.ToString(),
            NewValues = JsonSerializer.Serialize(new
            {
                tenant.Name,
                tenant.Slug,
                AdminEmail = user.Email,
                AdminName = user.FullName,
                WebhookEndpoint = endpoint.Name,
                endpoint.KeyPrefix
            }),
            IpAddress = ipAddress,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.Tenants.Add(tenant);
        _dbContext.Users.Add(user);
        _dbContext.TenantMemberships.Add(membership);
        _dbContext.WebhookEndpoints.Add(endpoint);
        _dbContext.AuditLogs.Add(audit);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Tenant {TenantSlug} successfully onboarded by Admin {AdminId}", tenant.Slug, adminUserId);

        var webhookUrl = $"/api/webhooks/whatsapp?key={keyGen.PlainKey}";

        return new CreateTenantResult(
            tenant.Id,
            tenant.Name,
            tenant.Slug,
            user.Id,
            user.Email,
            tempPassword,
            endpoint.Id,
            endpoint.Name,
            webhookUrl,
            keyGen.PlainKey,
            keyGen.KeyPrefix
        );
    }

    public async Task<bool> UpdateTenantStatusAsync(
        Guid tenantId,
        bool isActive,
        Guid adminUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant == null)
        {
            return false;
        }

        var oldStatus = tenant.IsActive;
        tenant.IsActive = isActive;
        tenant.UpdatedAt = DateTimeOffset.UtcNow;

        if (!isActive)
        {
            // Revoke active sessions for all users belonging to this tenant
            var tenantUserIds = await _dbContext.TenantMemberships
                .Where(m => m.TenantId == tenantId)
                .Select(m => m.UserId)
                .ToListAsync(cancellationToken);

            var sessions = await _dbContext.UserSessions
                .Where(s => tenantUserIds.Contains(s.UserId) && s.RevokedAt == null)
                .ToListAsync(cancellationToken);

            foreach (var s in sessions)
            {
                s.RevokedAt = DateTimeOffset.UtcNow;
            }
        }

        var audit = new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = adminUserId,
            Action = "Tenant.StatusUpdated",
            EntityType = "Tenant",
            EntityId = tenantId.ToString(),
            OldValues = JsonSerializer.Serialize(new { IsActive = oldStatus }),
            NewValues = JsonSerializer.Serialize(new { IsActive = isActive }),
            IpAddress = ipAddress,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.AuditLogs.Add(audit);

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Tenant {TenantId} status updated to {IsActive} by Admin {AdminId}", tenantId, isActive, adminUserId);
        return true;
    }

    public async Task<ResetClientPasswordResult> ResetClientPasswordAsync(
        Guid tenantId,
        Guid adminUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var membership = await _dbContext.TenantMemberships
            .Include(m => m.User)
            .Where(m => m.TenantId == tenantId && m.Role == TenantRole.TenantAdmin && m.User != null)
            .FirstOrDefaultAsync(cancellationToken);

        if (membership == null || membership.User == null)
        {
            // Fallback to any user for this tenant
            membership = await _dbContext.TenantMemberships
                .Include(m => m.User)
                .Where(m => m.TenantId == tenantId && m.User != null)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (membership == null || membership.User == null)
        {
            throw new InvalidOperationException("No user found for this tenant.");
        }

        var user = membership.User;
        var tempPassword = _passwordService.GenerateSecurePassword(16);

        user.PasswordHash = _passwordService.HashPassword(user, tempPassword);
        user.MustChangePassword = true;
        user.AuthVersion++;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        // Revoke existing sessions
        var activeSessions = await _dbContext.UserSessions
            .Where(s => s.UserId == user.Id && s.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var s in activeSessions)
        {
            s.RevokedAt = DateTimeOffset.UtcNow;
        }

        var audit = new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = adminUserId,
            Action = "Tenant.PasswordReset",
            EntityType = "User",
            EntityId = user.Id.ToString(),
            NewValues = JsonSerializer.Serialize(new { user.Email, ResetAt = DateTimeOffset.UtcNow }),
            IpAddress = ipAddress,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.AuditLogs.Add(audit);

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Password reset generated for User {UserId} (Tenant {TenantId}) by Admin {AdminId}", user.Id, tenantId, adminUserId);

        return new ResetClientPasswordResult(user.Id, user.Email, tempPassword);
    }

    public async Task<RotateKeyResult> RotateWebhookKeyAsync(
        Guid endpointId,
        Guid adminUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var endpoint = await _dbContext.WebhookEndpoints.FirstOrDefaultAsync(e => e.Id == endpointId, cancellationToken);
        if (endpoint == null)
        {
            throw new InvalidOperationException("Webhook endpoint not found.");
        }

        var oldKeyHash = endpoint.KeyHash;
        var keyGen = _webhookKeyService.GenerateKey();

        endpoint.KeyPrefix = keyGen.KeyPrefix;
        endpoint.KeyHash = keyGen.KeyHash;
        endpoint.UpdatedAt = DateTimeOffset.UtcNow;

        var audit = new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = endpoint.TenantId,
            UserId = adminUserId,
            Action = "WebhookEndpoint.KeyRotated",
            EntityType = "WebhookEndpoint",
            EntityId = endpoint.Id.ToString(),
            NewValues = JsonSerializer.Serialize(new { endpoint.Name, keyGen.KeyPrefix }),
            IpAddress = ipAddress,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.AuditLogs.Add(audit);

        await _cacheInvalidator.PublishEndpointInvalidationAsync(oldKeyHash, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Webhook endpoint {EndpointId} key rotated by Admin {AdminId}", endpoint.Id, adminUserId);

        return new RotateKeyResult(endpoint.Id, keyGen.PlainKey, keyGen.KeyPrefix);
    }

    public async Task<PlatformSummaryDto> GetPlatformSummaryAsync(CancellationToken cancellationToken = default)
    {
        var totalTenants = await _dbContext.Tenants.CountAsync(cancellationToken);
        var activeTenants = await _dbContext.Tenants.CountAsync(t => t.IsActive, cancellationToken);
        var suspendedTenants = totalTenants - activeTenants;

        var totalMessages = await _dbContext.Messages.LongCountAsync(cancellationToken);
        var failedMessages = await _dbContext.Messages.LongCountAsync(m => m.CurrentStatus == "failed", cancellationToken);

        var pendingInbox = await _dbContext.WebhookInboxItems.LongCountAsync(i => i.Status == InboxStatus.Pending || i.Status == InboxStatus.Processing, cancellationToken);
        var deadLetterInbox = await _dbContext.WebhookInboxItems.LongCountAsync(i => i.Status == InboxStatus.DeadLetter, cancellationToken);

        return new PlatformSummaryDto(
            totalTenants,
            activeTenants,
            suspendedTenants,
            totalMessages,
            failedMessages,
            pendingInbox,
            deadLetterInbox
        );
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Models;
using OraiWebhookManager.Application.Options;
using OraiWebhookManager.Domain.Entities;
using OraiWebhookManager.Infrastructure.Persistence;

namespace OraiWebhookManager.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly IPlatformAdminDbContext _dbContext;
    private readonly IPasswordService _passwordService;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IPlatformAdminDbContext dbContext,
        IPasswordService passwordService,
        IJwtTokenService jwtTokenService,
        IOptions<JwtOptions> jwtOptions,
        ILogger<AuthService> logger)
    {
        _dbContext = dbContext;
        _passwordService = passwordService;
        _jwtTokenService = jwtTokenService;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
    }

    public async Task<LoginResult> LoginAsync(
        LoginRequest request,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return new LoginResult(false, "Invalid email or password", null, null, null, null, null);
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .Include(u => u.Memberships)
                .ThenInclude(m => m.Tenant)
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (user == null || !_passwordService.VerifyPassword(user, user.PasswordHash, request.Password))
        {
            _logger.LogWarning("Failed login attempt for email {Email}", normalizedEmail);
            return new LoginResult(false, "Invalid email or password", null, null, null, null, null);
        }

        if (!user.IsActive)
        {
            _logger.LogWarning("Login attempt for suspended user {UserId}", user.Id);
            return new LoginResult(false, "Invalid email or password", null, null, null, null, null);
        }

        TenantMembership? activeMembership = null;
        if (!user.IsPlatformAdmin)
        {
            activeMembership = user.Memberships.FirstOrDefault(m => m.IsActive && m.Tenant != null && m.Tenant.IsActive);
            if (activeMembership == null)
            {
                _logger.LogWarning("User {UserId} has no active tenant memberships", user.Id);
                return new LoginResult(false, "Invalid email or password", null, null, null, null, null);
            }
        }
        else
        {
            activeMembership = user.Memberships.FirstOrDefault(m => m.IsActive && m.Tenant != null && m.Tenant.IsActive);
        }

        var (plainRefreshToken, tokenHash) = _jwtTokenService.GenerateRefreshToken();
        var expiresAt = DateTimeOffset.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays);

        var session = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RefreshTokenHash = tokenHash,
            ExpiresAt = expiresAt,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.UserSessions.Add(session);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var accessToken = _jwtTokenService.GenerateAccessToken(user, activeMembership, session.Id);
        var accessExpiresAt = DateTimeOffset.UtcNow.AddMinutes(_jwtOptions.AccessTokenExpirationMinutes);

        var userDto = new UserDto(
            user.Id,
            user.Email,
            user.FullName,
            user.IsPlatformAdmin,
            user.MustChangePassword,
            user.IsActive
        );

        TenantDto? tenantDto = null;
        if (activeMembership?.Tenant != null)
        {
            tenantDto = new TenantDto(
                activeMembership.Tenant.Id,
                activeMembership.Tenant.Name,
                activeMembership.Tenant.Slug,
                activeMembership.Tenant.IsActive,
                activeMembership.Role
            );
        }

        _logger.LogInformation("Successful login for user {UserId}", user.Id);

        return new LoginResult(
            true,
            null,
            accessToken,
            plainRefreshToken,
            accessExpiresAt,
            userDto,
            tenantDto,
            user.MustChangePassword
        );
    }

    public async Task<RefreshResult> RefreshSessionAsync(
        string plainRefreshToken,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plainRefreshToken))
        {
            return new RefreshResult(false, "Invalid refresh session", null, null, null, null, null);
        }

        var tokenHash = _jwtTokenService.HashRefreshToken(plainRefreshToken);

        var session = await _dbContext.UserSessions
            .Include(s => s.User)
                .ThenInclude(u => u!.Memberships)
                    .ThenInclude(m => m.Tenant)
            .FirstOrDefaultAsync(s => s.RefreshTokenHash == tokenHash, cancellationToken);

        // Fallback for EF Core InMemory provider where byte[] == byte[] checks reference equality
        if (session == null)
        {
            var sessions = await _dbContext.UserSessions
                .Include(s => s.User)
                    .ThenInclude(u => u!.Memberships)
                        .ThenInclude(m => m.Tenant)
                .ToListAsync(cancellationToken);

            session = sessions.FirstOrDefault(s => s.RefreshTokenHash.SequenceEqual(tokenHash));
        }

        if (session == null)
        {
            _logger.LogWarning("Refresh session lookup failed (token not found)");
            return new RefreshResult(false, "Invalid refresh session", null, null, null, null, null);
        }

        // Token reuse detection: if already revoked, revoke all sessions for that user for security
        if (session.RevokedAt != null)
        {
            _logger.LogWarning("Refresh token reuse detected for user {UserId}. Revoking all sessions.", session.UserId);
            var allSessions = await _dbContext.UserSessions
                .Where(s => s.UserId == session.UserId && s.RevokedAt == null)
                .ToListAsync(cancellationToken);

            foreach (var s in allSessions)
            {
                s.RevokedAt = DateTimeOffset.UtcNow;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            return new RefreshResult(false, "Session invalid or expired", null, null, null, null, null);
        }

        if (session.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            session.RevokedAt = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new RefreshResult(false, "Session expired", null, null, null, null, null);
        }

        var user = session.User;
        if (user == null || !user.IsActive)
        {
            session.RevokedAt = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new RefreshResult(false, "User account is inactive", null, null, null, null, null);
        }

        TenantMembership? activeMembership = null;
        if (!user.IsPlatformAdmin)
        {
            activeMembership = user.Memberships.FirstOrDefault(m => m.IsActive && m.Tenant != null && m.Tenant.IsActive);
            if (activeMembership == null)
            {
                session.RevokedAt = DateTimeOffset.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
                return new RefreshResult(false, "Tenant is inactive", null, null, null, null, null);
            }
        }
        else
        {
            activeMembership = user.Memberships.FirstOrDefault(m => m.IsActive && m.Tenant != null && m.Tenant.IsActive);
        }

        // Rotate session
        session.RevokedAt = DateTimeOffset.UtcNow;

        var (newPlainRefreshToken, newTokenHash) = _jwtTokenService.GenerateRefreshToken();
        var newExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays);

        var newSession = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RefreshTokenHash = newTokenHash,
            ExpiresAt = newExpiresAt,
            IpAddress = ipAddress ?? session.IpAddress,
            UserAgent = userAgent ?? session.UserAgent,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.UserSessions.Add(newSession);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var accessToken = _jwtTokenService.GenerateAccessToken(user, activeMembership, newSession.Id);
        var accessExpiresAt = DateTimeOffset.UtcNow.AddMinutes(_jwtOptions.AccessTokenExpirationMinutes);

        var userDto = new UserDto(
            user.Id,
            user.Email,
            user.FullName,
            user.IsPlatformAdmin,
            user.MustChangePassword,
            user.IsActive
        );

        TenantDto? tenantDto = null;
        if (activeMembership?.Tenant != null)
        {
            tenantDto = new TenantDto(
                activeMembership.Tenant.Id,
                activeMembership.Tenant.Name,
                activeMembership.Tenant.Slug,
                activeMembership.Tenant.IsActive,
                activeMembership.Role
            );
        }

        return new RefreshResult(
            true,
            null,
            accessToken,
            newPlainRefreshToken,
            accessExpiresAt,
            userDto,
            tenantDto,
            user.MustChangePassword
        );
    }

    public async Task<bool> LogoutAsync(string? plainRefreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plainRefreshToken))
        {
            return true;
        }

        var tokenHash = _jwtTokenService.HashRefreshToken(plainRefreshToken);
        var session = await _dbContext.UserSessions.FirstOrDefaultAsync(s => s.RefreshTokenHash == tokenHash, cancellationToken);
        if (session == null)
        {
            var sessions = await _dbContext.UserSessions.ToListAsync(cancellationToken);
            session = sessions.FirstOrDefault(s => s.RefreshTokenHash.SequenceEqual(tokenHash));
        }

        if (session != null && session.RevokedAt == null)
        {
            session.RevokedAt = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task<bool> ChangePasswordAsync(
        Guid userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return false;
        }

        if (request.NewPassword.Length < 8)
        {
            return false;
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null || !_passwordService.VerifyPassword(user, user.PasswordHash, request.CurrentPassword))
        {
            return false;
        }

        user.PasswordHash = _passwordService.HashPassword(user, request.NewPassword);
        user.MustChangePassword = false;
        user.AuthVersion++;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        // Revoke all existing sessions on password change
        var activeSessions = await _dbContext.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var s in activeSessions)
        {
            s.RevokedAt = DateTimeOffset.UtcNow;
        }

        // Record audit log
        var audit = new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Action = "User.PasswordChanged",
            EntityType = "User",
            EntityId = userId.ToString(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.AuditLogs.Add(audit);

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Password changed and sessions revoked for user {UserId}", userId);
        return true;
    }

    public async Task<AuthSessionDto?> GetCurrentSessionAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .Include(u => u.Memberships)
                .ThenInclude(m => m.Tenant)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null || !user.IsActive)
        {
            return null;
        }

        var userDto = new UserDto(
            user.Id,
            user.Email,
            user.FullName,
            user.IsPlatformAdmin,
            user.MustChangePassword,
            user.IsActive
        );

        var activeMembership = user.Memberships.FirstOrDefault(m => m.IsActive && m.Tenant != null && m.Tenant.IsActive);
        TenantDto? tenantDto = null;
        if (activeMembership?.Tenant != null)
        {
            tenantDto = new TenantDto(
                activeMembership.Tenant.Id,
                activeMembership.Tenant.Name,
                activeMembership.Tenant.Slug,
                activeMembership.Tenant.IsActive,
                activeMembership.Role
            );
        }

        return new AuthSessionDto(userDto, tenantDto);
    }

    public async Task<bool> BootstrapAdminAsync(
        string email,
        string password,
        string fullName = "Super Admin",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        var hasAdmin = await _dbContext.Users.AnyAsync(u => u.IsPlatformAdmin, cancellationToken);
        if (hasAdmin)
        {
            _logger.LogWarning("Bootstrap admin rejected: Platform admin already exists.");
            return false;
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();

        var existingUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (existingUser != null)
        {
            _logger.LogWarning("Bootstrap admin rejected: User with email {Email} already exists.", normalizedEmail);
            return false;
        }

        var adminUser = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            FullName = fullName,
            IsPlatformAdmin = true,
            MustChangePassword = false,
            EmailConfirmed = true,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        adminUser.PasswordHash = _passwordService.HashPassword(adminUser, password);

        _dbContext.Users.Add(adminUser);

        var audit = new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = adminUser.Id,
            Action = "PlatformAdmin.Bootstrapped",
            EntityType = "User",
            EntityId = adminUser.Id.ToString(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.AuditLogs.Add(audit);

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Super admin bootstrapped successfully for email {Email}", normalizedEmail);
        return true;
    }
}

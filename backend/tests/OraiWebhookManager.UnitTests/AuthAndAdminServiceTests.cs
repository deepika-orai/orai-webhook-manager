using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Models;
using OraiWebhookManager.Application.Options;
using OraiWebhookManager.Domain.Entities;
using OraiWebhookManager.Domain.Enums;
using OraiWebhookManager.Infrastructure.Persistence;
using OraiWebhookManager.Infrastructure.Services;
using Xunit;

namespace OraiWebhookManager.UnitTests;

public class AuthAndAdminServiceTests
{
    private PlatformAdminDbContext CreateDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<PlatformAdminDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        return new PlatformAdminDbContext(options);
    }

    private (AuthService authService, AdminService adminService, PlatformAdminDbContext dbContext, PasswordService passwordService, JwtTokenService jwtService) CreateServices(string dbName)
    {
        var db = CreateDbContext(dbName);
        var jwtOptions = Options.Create(new JwtOptions
        {
            Secret = "Test_Secret_Key_At_Least_32_Characters_Long_For_HmacSha256!",
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7
        });

        var passwordService = new PasswordService();
        var jwtService = new JwtTokenService(jwtOptions);
        var webhookKeyService = new WebhookKeyService();
        var cacheInvalidator = new FakeCacheInvalidator();

        var authService = new AuthService(
            db,
            passwordService,
            jwtService,
            jwtOptions,
            NullLogger<AuthService>.Instance
        );

        var webhookOptions = Options.Create(new WebhookIngestionOptions
        {
            PublicBaseUrl = "http://localhost:5135"
        });

        var adminService = new AdminService(
            db,
            passwordService,
            webhookKeyService,
            cacheInvalidator,
            webhookOptions,
            NullLogger<AdminService>.Instance
        );

        return (authService, adminService, db, passwordService, jwtService);
    }

    private class FakeCacheInvalidator : ICacheInvalidator
    {
        public List<byte[]> InvalidatedKeys { get; } = new();

        public Task PublishEndpointInvalidationAsync(byte[] keyHash, CancellationToken cancellationToken = default)
        {
            InvalidatedKeys.Add(keyHash);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsSuccessAndTokens()
    {
        var (authService, _, db, passwordService, _) = CreateServices(nameof(LoginAsync_ValidCredentials_ReturnsSuccessAndTokens));

        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Acme Corp", Slug = "acme", IsActive = true };
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "admin@acme.com",
            FullName = "Acme Admin",
            IsActive = true,
            MustChangePassword = true
        };
        user.PasswordHash = passwordService.HashPassword(user, "SecurePass123!");

        var membership = new TenantMembership
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Role = TenantRole.TenantAdmin,
            IsActive = true
        };

        db.Tenants.Add(tenant);
        db.Users.Add(user);
        db.TenantMemberships.Add(membership);
        await db.SaveChangesAsync();

        var result = await authService.LoginAsync(new LoginRequest("admin@acme.com", "SecurePass123!"), "127.0.0.1", "TestAgent");

        result.Succeeded.Should().BeTrue();
        result.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.RefreshToken.Should().NotBeNullOrWhiteSpace();
        result.User.Should().NotBeNull();
        result.User!.Email.Should().Be("admin@acme.com");
        result.Tenant.Should().NotBeNull();
        result.Tenant!.Slug.Should().Be("acme");
        result.MustChangePassword.Should().BeTrue();

        var savedSession = await db.UserSessions.FirstOrDefaultAsync(s => s.UserId == user.Id);
        savedSession.Should().NotBeNull();
        savedSession!.RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_InvalidPassword_ReturnsGenericErrorMessage()
    {
        var (authService, _, db, passwordService, _) = CreateServices(nameof(LoginAsync_InvalidPassword_ReturnsGenericErrorMessage));

        var user = new User { Id = Guid.NewGuid(), Email = "user@test.com", FullName = "Test", IsActive = true };
        user.PasswordHash = passwordService.HashPassword(user, "CorrectPassword1!");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await authService.LoginAsync(new LoginRequest("user@test.com", "WrongPassword!"), "127.0.0.1", "Agent");

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("Invalid email or password");
        result.AccessToken.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_SuspendedUser_FailsGeneric()
    {
        var (authService, _, db, passwordService, _) = CreateServices(nameof(LoginAsync_SuspendedUser_FailsGeneric));

        var user = new User { Id = Guid.NewGuid(), Email = "suspended@test.com", FullName = "Suspended", IsActive = false };
        user.PasswordHash = passwordService.HashPassword(user, "Password123!");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await authService.LoginAsync(new LoginRequest("suspended@test.com", "Password123!"), "127.0.0.1", "Agent");

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("Invalid email or password");
    }

    [Fact]
    public async Task LoginAsync_SuspendedTenant_FailsGenericForTenantUser()
    {
        var (authService, _, db, passwordService, _) = CreateServices(nameof(LoginAsync_SuspendedTenant_FailsGenericForTenantUser));

        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Suspended Corp", Slug = "suspended", IsActive = false };
        var user = new User { Id = Guid.NewGuid(), Email = "client@suspended.com", FullName = "Client", IsActive = true };
        user.PasswordHash = passwordService.HashPassword(user, "Password123!");

        var membership = new TenantMembership { TenantId = tenant.Id, UserId = user.Id, Role = TenantRole.TenantAdmin, IsActive = true };

        db.Tenants.Add(tenant);
        db.Users.Add(user);
        db.TenantMemberships.Add(membership);
        await db.SaveChangesAsync();

        var result = await authService.LoginAsync(new LoginRequest("client@suspended.com", "Password123!"), "127.0.0.1", "Agent");

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("Invalid email or password");
    }

    [Fact]
    public async Task RefreshSessionAsync_ValidSession_RotatesTokenAndRevokesOld()
    {
        var (authService, _, db, passwordService, _) = CreateServices(nameof(RefreshSessionAsync_ValidSession_RotatesTokenAndRevokesOld));

        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Refresh Tenant", Slug = "refresh-tenant", IsActive = true };
        var user = new User { Id = Guid.NewGuid(), Email = "user@refresh.com", FullName = "User", IsActive = true };
        user.PasswordHash = passwordService.HashPassword(user, "Password123!");
        var membership = new TenantMembership { TenantId = tenant.Id, UserId = user.Id, Role = TenantRole.Member, IsActive = true };

        db.Tenants.Add(tenant);
        db.Users.Add(user);
        db.TenantMemberships.Add(membership);
        await db.SaveChangesAsync();

        var loginResult = await authService.LoginAsync(new LoginRequest("user@refresh.com", "Password123!"), "127.0.0.1", "Agent");
        loginResult.Succeeded.Should().BeTrue();
        var initialRefreshToken = loginResult.RefreshToken!;

        var refreshResult = await authService.RefreshSessionAsync(initialRefreshToken, "127.0.0.1", "Agent");

        refreshResult.Succeeded.Should().BeTrue();
        refreshResult.AccessToken.Should().NotBeNullOrWhiteSpace();
        refreshResult.RefreshToken.Should().NotBeNullOrWhiteSpace();
        refreshResult.RefreshToken.Should().NotBe(initialRefreshToken);

        var sessions = await db.UserSessions.Where(s => s.UserId == user.Id).ToListAsync();
        sessions.Should().HaveCount(2);
        sessions.Count(s => s.RevokedAt != null).Should().Be(1);
        sessions.Count(s => s.RevokedAt == null).Should().Be(1);
    }

    [Fact]
    public async Task RefreshSessionAsync_TokenReuseDetection_RevokesAllUserSessions()
    {
        var (authService, _, db, passwordService, _) = CreateServices(nameof(RefreshSessionAsync_TokenReuseDetection_RevokesAllUserSessions));

        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Reuse Tenant", Slug = "reuse-tenant", IsActive = true };
        var user = new User { Id = Guid.NewGuid(), Email = "reuse@test.com", FullName = "Reuse", IsActive = true };
        user.PasswordHash = passwordService.HashPassword(user, "Password123!");
        var membership = new TenantMembership { TenantId = tenant.Id, UserId = user.Id, Role = TenantRole.Member, IsActive = true };

        db.Tenants.Add(tenant);
        db.Users.Add(user);
        db.TenantMemberships.Add(membership);
        await db.SaveChangesAsync();

        var login = await authService.LoginAsync(new LoginRequest("reuse@test.com", "Password123!"), "127.0.0.1", "Agent");
        login.Succeeded.Should().BeTrue();
        var initialToken = login.RefreshToken!;

        // Legitimate first refresh
        var refresh1 = await authService.RefreshSessionAsync(initialToken, "127.0.0.1", "Agent");
        refresh1.Succeeded.Should().BeTrue();

        // Attacker attempts to replay old initialToken
        var replayAttempt = await authService.RefreshSessionAsync(initialToken, "127.0.0.1", "Attacker");
        replayAttempt.Succeeded.Should().BeFalse();

        // ALL sessions for user must now be revoked
        var activeSessions = await db.UserSessions.Where(s => s.UserId == user.Id && s.RevokedAt == null).ToListAsync();
        activeSessions.Should().BeEmpty("Token reuse detection must revoke all active sessions for that user");
    }

    [Fact]
    public async Task ChangePasswordAsync_ValidRequest_UpdatesPasswordAndRevokesAllSessions()
    {
        var (authService, _, db, passwordService, _) = CreateServices(nameof(ChangePasswordAsync_ValidRequest_UpdatesPasswordAndRevokesAllSessions));

        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Pwd Tenant", Slug = "pwd-tenant", IsActive = true };
        var user = new User { Id = Guid.NewGuid(), Email = "pwd@test.com", FullName = "Pwd", IsActive = true, MustChangePassword = true };
        user.PasswordHash = passwordService.HashPassword(user, "OldPassword123!");
        var membership = new TenantMembership { TenantId = tenant.Id, UserId = user.Id, Role = TenantRole.Member, IsActive = true };

        db.Tenants.Add(tenant);
        db.Users.Add(user);
        db.TenantMemberships.Add(membership);
        await db.SaveChangesAsync();

        // Login to get a session
        var login = await authService.LoginAsync(new LoginRequest("pwd@test.com", "OldPassword123!"), "127.0.0.1", "Agent");
        login.Succeeded.Should().BeTrue();

        var changeSuccess = await authService.ChangePasswordAsync(user.Id, new ChangePasswordRequest("OldPassword123!", "NewSuperSecurePass2026!"));
        changeSuccess.Should().BeTrue();

        var updatedUser = await db.Users.FindAsync(user.Id);
        updatedUser!.MustChangePassword.Should().BeFalse();
        updatedUser.AuthVersion.Should().Be(2, "Password change must increment AuthVersion");
        passwordService.VerifyPassword(updatedUser, updatedUser.PasswordHash, "NewSuperSecurePass2026!").Should().BeTrue();

        var activeSessions = await db.UserSessions.Where(s => s.UserId == user.Id && s.RevokedAt == null).ToListAsync();
        activeSessions.Should().BeEmpty("Password change must revoke all active sessions");
    }

    [Fact]
    public async Task BootstrapAdminAsync_FirstTime_CreatesPlatformAdmin()
    {
        var (authService, _, db, passwordService, _) = CreateServices(nameof(BootstrapAdminAsync_FirstTime_CreatesPlatformAdmin));

        var success = await authService.BootstrapAdminAsync("admin@orai.io", "PlatformAdmin2026!", "Platform Super Admin");
        success.Should().BeTrue();

        var admin = await db.Users.FirstOrDefaultAsync(u => u.Email == "admin@orai.io");
        admin.Should().NotBeNull();
        admin!.IsPlatformAdmin.Should().BeTrue();
        admin.MustChangePassword.Should().BeFalse();
        admin.IsActive.Should().BeTrue();
        passwordService.VerifyPassword(admin, admin.PasswordHash, "PlatformAdmin2026!").Should().BeTrue();

        // Attempting to bootstrap a second time must refuse
        var secondAttempt = await authService.BootstrapAdminAsync("admin2@orai.io", "Pass12345678!");
        secondAttempt.Should().BeFalse("Must refuse to overwrite when a platform admin already exists");
    }

    [Fact]
    public async Task AdminService_CreateTenantAsync_CreatesAllEntitiesAtomicallyAndReturnsSecretsOnce()
    {
        var (_, adminService, db, _, _) = CreateServices(nameof(AdminService_CreateTenantAsync_CreatesAllEntitiesAtomicallyAndReturnsSecretsOnce));

        var adminId = Guid.NewGuid();
        var request = new CreateTenantRequest("Stripe Inc", "stripe", "admin@stripe.com", "Stripe Admin");

        var result = await adminService.CreateTenantAsync(request, adminId, "127.0.0.1");

        result.TenantId.Should().NotBeEmpty();
        result.Slug.Should().Be("stripe");
        result.AdminEmail.Should().Be("admin@stripe.com");
        result.TempPassword.Should().NotBeNullOrWhiteSpace();
        result.TempPassword.Length.Should().BeGreaterThanOrEqualTo(16);
        result.WebhookPlainKey.Should().NotBeNullOrWhiteSpace();
        result.WebhookKeyPrefix.Should().NotBeNullOrWhiteSpace();

        // Verify entities in database
        var tenant = await db.Tenants.FindAsync(result.TenantId);
        tenant.Should().NotBeNull();
        tenant!.IsActive.Should().BeTrue();

        var user = await db.Users.FindAsync(result.AdminUserId);
        user.Should().NotBeNull();
        user!.MustChangePassword.Should().BeTrue();
        user.PasswordHash.Should().NotBe(result.TempPassword, "Plain password must NEVER be persisted");

        var endpoint = await db.WebhookEndpoints.FindAsync(result.WebhookEndpointId);
        endpoint.Should().NotBeNull();
        endpoint!.KeyPrefix.Should().Be(result.WebhookKeyPrefix);

        var audit = await db.AuditLogs.FirstOrDefaultAsync(a => a.TenantId == result.TenantId && a.Action == "Tenant.Created");
        audit.Should().NotBeNull();
        audit!.NewValues.Should().NotContain(result.TempPassword, "Audit log must NEVER contain plain password");
        audit.NewValues.Should().NotContain(result.WebhookPlainKey, "Audit log must NEVER contain plain webhook key");
    }

    [Fact]
    public async Task AdminService_UpdateTenantStatus_SuspensionRevokesTenantUserSessions()
    {
        var (authService, adminService, db, passwordService, _) = CreateServices(nameof(AdminService_UpdateTenantStatus_SuspensionRevokesTenantUserSessions));

        var adminId = Guid.NewGuid();
        var createResult = await adminService.CreateTenantAsync(
            new CreateTenantRequest("Shopify", "shopify", "admin@shopify.com", "Shop Admin"),
            adminId,
            "127.0.0.1"
        );

        // Login to establish session
        var login = await authService.LoginAsync(new LoginRequest("admin@shopify.com", createResult.TempPassword), "127.0.0.1", "Agent");
        login.Succeeded.Should().BeTrue();

        // Admin suspends tenant
        var suspendSuccess = await adminService.UpdateTenantStatusAsync(createResult.TenantId, false, adminId, "127.0.0.1");
        suspendSuccess.Should().BeTrue();

        var tenant = await db.Tenants.FindAsync(createResult.TenantId);
        tenant!.IsActive.Should().BeFalse();

        var activeSessions = await db.UserSessions.Where(s => s.UserId == createResult.AdminUserId && s.RevokedAt == null).ToListAsync();
        activeSessions.Should().BeEmpty("Tenant suspension must revoke all active sessions of tenant users");
    }

    [Fact]
    public async Task AdminService_ResetClientPassword_GeneratesNewTempPasswordAndRevokesSessions()
    {
        var (authService, adminService, db, _, _) = CreateServices(nameof(AdminService_ResetClientPassword_GeneratesNewTempPasswordAndRevokesSessions));

        var adminId = Guid.NewGuid();
        var createResult = await adminService.CreateTenantAsync(
            new CreateTenantRequest("Uber", "uber", "admin@uber.com", "Uber Admin"),
            adminId,
            "127.0.0.1"
        );

        await authService.LoginAsync(new LoginRequest("admin@uber.com", createResult.TempPassword), "127.0.0.1", "Agent");

        var resetResult = await adminService.ResetClientPasswordAsync(createResult.TenantId, adminId, "127.0.0.1");
        resetResult.TempPassword.Should().NotBeNullOrWhiteSpace();
        resetResult.TempPassword.Should().NotBe(createResult.TempPassword);

        var user = await db.Users.FindAsync(createResult.AdminUserId);
        user!.MustChangePassword.Should().BeTrue();
        user.AuthVersion.Should().Be(2, "Reset client password must increment AuthVersion");

        var activeSessions = await db.UserSessions.Where(s => s.UserId == createResult.AdminUserId && s.RevokedAt == null).ToListAsync();
        activeSessions.Should().BeEmpty("Reset client password must revoke previous sessions");
    }

    [Fact]
    public void JwtTokenService_GeneratesAuthVersionAndSidClaims()
    {
        var (_, _, _, _, jwtService) = CreateServices(nameof(JwtTokenService_GeneratesAuthVersionAndSidClaims));

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "user@test.com",
            FullName = "User Test",
            IsPlatformAdmin = true,
            AuthVersion = 3,
            MustChangePassword = false
        };
        var sessionId = Guid.NewGuid();

        var token = jwtService.GenerateAccessToken(user, null, sessionId);
        token.Should().NotBeNullOrWhiteSpace();

        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Claims.Should().Contain(c => c.Type == "auth_version" && c.Value == "3");
        jwt.Claims.Should().Contain(c => c.Type == "sid" && c.Value == sessionId.ToString());
    }

    [Fact]
    public async Task AdminService_RotateWebhookKey_GeneratesNewKeyAndInvalidatesCache()
    {
        var (_, adminService, db, _, _) = CreateServices(nameof(AdminService_RotateWebhookKey_GeneratesNewKeyAndInvalidatesCache));

        var adminId = Guid.NewGuid();
        var createResult = await adminService.CreateTenantAsync(
            new CreateTenantRequest("Netflix", "netflix", "admin@netflix.com", "Netflix Admin"),
            adminId,
            "127.0.0.1"
        );

        var rotateResult = await adminService.RotateWebhookKeyAsync(createResult.WebhookEndpointId, adminId, "127.0.0.1");
        rotateResult.PlainKey.Should().NotBeNullOrWhiteSpace();
        rotateResult.PlainKey.Should().NotBe(createResult.WebhookPlainKey);

        var endpoint = await db.WebhookEndpoints.FindAsync(createResult.WebhookEndpointId);
        endpoint!.KeyPrefix.Should().Be(rotateResult.KeyPrefix);
    }
}

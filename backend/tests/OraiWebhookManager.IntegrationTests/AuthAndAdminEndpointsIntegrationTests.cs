using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Models;
using OraiWebhookManager.Domain.Entities;
using OraiWebhookManager.Domain.Enums;
using OraiWebhookManager.Infrastructure.Persistence;
using Xunit;

namespace OraiWebhookManager.IntegrationTests;

public class AuthAndAdminEndpointsIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly FakeAuthService _fakeAuthService = new();
    private readonly FakeAdminService _fakeAdminService = new();

    public AuthAndAdminEndpointsIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private PlatformAdminDbContext CreateInMemoryDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<PlatformAdminDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        return new PlatformAdminDbContext(options);
    }

    private HttpClient CreateTestClient(
        string? environment = "Development",
        Action<IServiceCollection>? configureServices = null,
        string? dbName = null)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            if (!string.IsNullOrEmpty(environment))
            {
                builder.UseEnvironment(environment);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<IAuthService>(_ => _fakeAuthService);
                services.AddScoped<IAdminService>(_ => _fakeAdminService);

                if (!string.IsNullOrEmpty(dbName))
                {
                    var descriptors = services.Where(d =>
                        d.ServiceType.Name.Contains("DbContext") ||
                        d.ServiceType == typeof(DbContextOptions) ||
                        (d.ImplementationType != null && d.ImplementationType.Name.Contains("DbContext")) ||
                        d.ServiceType == typeof(IAppDbContext) ||
                        d.ServiceType == typeof(IPlatformAdminDbContext)
                    ).ToList();

                    foreach (var d in descriptors)
                    {
                        services.Remove(d);
                    }

                    services.AddDbContext<AppDbContext>(options =>
                        options.UseInMemoryDatabase(databaseName: dbName));
                    services.AddDbContext<PlatformAdminDbContext>(options =>
                        options.UseInMemoryDatabase(databaseName: dbName));
                    services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
                    services.AddScoped<IPlatformAdminDbContext>(sp => sp.GetRequiredService<PlatformAdminDbContext>());
                }

                configureServices?.Invoke(services);
            });
        }).CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });
    }

    private async Task<(HttpClient Client, string CsrfToken)> CreateClientWithCsrfAsync(
        string environment = "Development",
        Action<IServiceCollection>? configureServices = null,
        string? dbName = null)
    {
        var client = CreateTestClient(environment, configureServices, dbName);
        var csrfResponse = await client.GetAsync("/api/auth/csrf");
        csrfResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await csrfResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token);
        return (client, token);
    }

    // ---------------- CSRF Protection Tests ----------------

    [Fact]
    public async Task CsrfEndpoint_ReturnsToken_AndSetsXsrfCookie()
    {
        var client = CreateTestClient();
        var response = await client.GetAsync("/api/auth/csrf");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("token", out var tokenProp).Should().BeTrue();
        tokenProp.GetString().Should().NotBeNullOrWhiteSpace();

        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        cookies!.Should().Contain(c => c.Contains("XSRF-TOKEN"));
    }

    [Fact]
    public async Task Login_WithValidCsrfToken_SetsHttpOnlyCookies_AndReturnsSession()
    {
        var (client, _) = await CreateClientWithCsrfAsync();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin@orai.io", "AdminPass123!"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify Set-Cookie headers
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        var cookieList = cookies!.ToList();
        cookieList.Should().Contain(c => c.Contains("orai_access_token"));
        cookieList.Should().Contain(c => c.Contains("orai_refresh_token"));
        cookieList.Should().Contain(c => c.Contains("httponly", StringComparison.OrdinalIgnoreCase));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("succeeded").GetBoolean().Should().BeTrue();
        body.GetProperty("user").GetProperty("email").GetString().Should().Be("admin@orai.io");
    }

    [Fact]
    public async Task Login_MissingCsrfToken_ReturnsBadRequest()
    {
        var client = CreateTestClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin@orai.io", "AdminPass123!"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Contain("Antiforgery");
    }

    [Fact]
    public async Task Login_InvalidCsrfToken_ReturnsBadRequest()
    {
        var client = CreateTestClient();
        await client.GetAsync("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", "invalid_forged_csrf_token");

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin@orai.io", "AdminPass123!"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Refresh_MissingCsrfToken_ReturnsBadRequest()
    {
        var client = CreateTestClient();
        var response = await client.PostAsync("/api/auth/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Logout_MissingCsrfToken_ReturnsBadRequest()
    {
        var client = CreateTestClient();
        var response = await client.PostAsync("/api/auth/logout", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangePassword_MissingCsrfToken_ReturnsBadRequest()
    {
        var dbName = nameof(ChangePassword_MissingCsrfToken_ReturnsBadRequest);
        using (var db = CreateInMemoryDbContext(dbName))
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = "user@test.com",
                FullName = "User",
                IsActive = true,
                AuthVersion = 1
            };
            var session = new UserSession
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                RevokedAt = null
            };
            db.Users.Add(user);
            db.UserSessions.Add(session);
            await db.SaveChangesAsync();

            using var scope = _factory.Services.CreateScope();
            var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
            var token = jwtService.GenerateAccessToken(user, null, session.Id);

            var client = CreateTestClient(dbName: dbName);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("oldpassword", "newpassword123"));
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("error").GetString().Should().Contain("Antiforgery");
        }
    }

    [Fact]
    public async Task AdminCreateTenant_MissingCsrfToken_ReturnsBadRequest()
    {
        var dbName = nameof(AdminCreateTenant_MissingCsrfToken_ReturnsBadRequest);
        using (var db = CreateInMemoryDbContext(dbName))
        {
            var adminUser = new User
            {
                Id = Guid.NewGuid(),
                Email = "admin@orai.io",
                FullName = "Admin",
                IsPlatformAdmin = true,
                IsActive = true,
                AuthVersion = 1
            };
            var session = new UserSession
            {
                Id = Guid.NewGuid(),
                UserId = adminUser.Id,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                RevokedAt = null
            };
            db.Users.Add(adminUser);
            db.UserSessions.Add(session);
            await db.SaveChangesAsync();

            using var scope = _factory.Services.CreateScope();
            var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
            var token = jwtService.GenerateAccessToken(adminUser, null, session.Id);

            var client = CreateTestClient(dbName: dbName);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.PostAsJsonAsync("/api/admin/tenants", new CreateTenantRequest("A", "a", "a@a.com", "A"));
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("error").GetString().Should().Contain("Antiforgery");
        }
    }

    // ---------------- Cookie Attributes Tests (Dev vs Prod) ----------------

    [Fact]
    public async Task Login_DevelopmentEnvironment_SetsExplicitDevCookies()
    {
        var (client, _) = await CreateClientWithCsrfAsync(environment: "Development");

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin@orai.io", "AdminPass123!"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        var cookieList = cookies!.ToList();

        var accessCookie = cookieList.First(c => c.Contains("orai_access_token"));
        accessCookie.Should().Contain("path=/;");
        accessCookie.Should().Contain("samesite=lax");
        accessCookie.Should().Contain("httponly");

        var refreshCookie = cookieList.First(c => c.Contains("orai_refresh_token"));
        refreshCookie.Should().Contain("path=/api/auth");
        refreshCookie.Should().Contain("samesite=strict");
        refreshCookie.Should().Contain("httponly");
    }

    [Fact]
    public async Task Login_ProductionEnvironment_AlwaysSetsSecureCookies()
    {
        var (client, _) = await CreateClientWithCsrfAsync(environment: "Production");

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin@orai.io", "AdminPass123!"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        var cookieList = cookies!.ToList();

        var accessCookie = cookieList.First(c => c.Contains("orai_access_token"));
        accessCookie.Should().Contain("secure", "Production auth cookie MUST always have Secure flag");
        accessCookie.Should().Contain("httponly");

        var refreshCookie = cookieList.First(c => c.Contains("orai_refresh_token"));
        refreshCookie.Should().Contain("secure", "Production refresh cookie MUST always have Secure flag");
        refreshCookie.Should().Contain("httponly");
    }

    // ---------------- Immediate Revocation & Security Version Tests ----------------

    [Fact]
    public async Task ImmediateRevocation_SuspendedUser_FailsJwtValidation()
    {
        var dbName = nameof(ImmediateRevocation_SuspendedUser_FailsJwtValidation);
        using (var db = CreateInMemoryDbContext(dbName))
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = "suspended@orai.io",
                FullName = "Suspended User",
                IsPlatformAdmin = true,
                IsActive = false,
                AuthVersion = 1
            };
            var session = new UserSession
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                RevokedAt = null
            };
            db.Users.Add(user);
            db.UserSessions.Add(session);
            await db.SaveChangesAsync();

            using var scope = _factory.Services.CreateScope();
            var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
            var token = jwtService.GenerateAccessToken(user, null, session.Id);

            var client = CreateTestClient(dbName: dbName);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.GetAsync("/api/admin/tenants");
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task ImmediateRevocation_SuspendedTenant_FailsJwtValidation()
    {
        var dbName = nameof(ImmediateRevocation_SuspendedTenant_FailsJwtValidation);
        using (var db = CreateInMemoryDbContext(dbName))
        {
            var tenant = new Tenant
            {
                Id = Guid.NewGuid(),
                Name = "Suspended Corp",
                Slug = "suspended-corp",
                IsActive = false
            };
            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = "client@suspended.com",
                FullName = "Client",
                IsPlatformAdmin = false,
                IsActive = true,
                AuthVersion = 1
            };
            var membership = new TenantMembership
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                UserId = user.Id,
                Tenant = tenant,
                Role = TenantRole.TenantAdmin,
                IsActive = true
            };
            var session = new UserSession
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                RevokedAt = null
            };
            db.Tenants.Add(tenant);
            db.Users.Add(user);
            db.TenantMemberships.Add(membership);
            db.UserSessions.Add(session);
            await db.SaveChangesAsync();

            using var scope = _factory.Services.CreateScope();
            var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
            var token = jwtService.GenerateAccessToken(user, membership, session.Id);

            var client = CreateTestClient(dbName: dbName);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.GetAsync("/api/auth/me");
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task ImmediateRevocation_MismatchedAuthVersion_FailsJwtValidation()
    {
        var dbName = nameof(ImmediateRevocation_MismatchedAuthVersion_FailsJwtValidation);
        using (var db = CreateInMemoryDbContext(dbName))
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = "admin@orai.io",
                FullName = "Admin",
                IsPlatformAdmin = true,
                IsActive = true,
                AuthVersion = 2 // Database has version 2 after password change/reset
            };
            var session = new UserSession
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                RevokedAt = null
            };
            db.Users.Add(user);
            db.UserSessions.Add(session);
            await db.SaveChangesAsync();

            using var scope = _factory.Services.CreateScope();
            var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

            // Token generated with old auth_version = 1
            var staleUser = new User
            {
                Id = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                IsPlatformAdmin = true,
                IsActive = true,
                AuthVersion = 1
            };
            var staleToken = jwtService.GenerateAccessToken(staleUser, null, session.Id);

            var client = CreateTestClient(dbName: dbName);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staleToken);

            var response = await client.GetAsync("/api/admin/tenants");
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task ImmediateRevocation_RevokedSession_FailsJwtValidation()
    {
        var dbName = nameof(ImmediateRevocation_RevokedSession_FailsJwtValidation);
        using (var db = CreateInMemoryDbContext(dbName))
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = "admin@orai.io",
                FullName = "Admin",
                IsPlatformAdmin = true,
                IsActive = true,
                AuthVersion = 1
            };
            var session = new UserSession
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                RevokedAt = DateTimeOffset.UtcNow // Session revoked
            };
            db.Users.Add(user);
            db.UserSessions.Add(session);
            await db.SaveChangesAsync();

            using var scope = _factory.Services.CreateScope();
            var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
            var token = jwtService.GenerateAccessToken(user, null, session.Id);

            var client = CreateTestClient(dbName: dbName);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.GetAsync("/api/admin/tenants");
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task AdminEndpoints_WithPlatformAdminToken_ReturnsOk()
    {
        var dbName = nameof(AdminEndpoints_WithPlatformAdminToken_ReturnsOk);
        using (var db = CreateInMemoryDbContext(dbName))
        {
            var adminUser = new User
            {
                Id = Guid.NewGuid(),
                Email = "superadmin@orai.io",
                FullName = "Super Admin",
                IsPlatformAdmin = true,
                IsActive = true,
                AuthVersion = 1
            };
            var session = new UserSession
            {
                Id = Guid.NewGuid(),
                UserId = adminUser.Id,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                RevokedAt = null
            };
            db.Users.Add(adminUser);
            db.UserSessions.Add(session);
            await db.SaveChangesAsync();

            using var scope = _factory.Services.CreateScope();
            var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
            var token = jwtService.GenerateAccessToken(adminUser, null, session.Id);

            var client = CreateTestClient(dbName: dbName);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.GetAsync("/api/admin/tenants");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task AdminEndpoints_WithRegularTenantUserToken_ReturnsForbidden()
    {
        var dbName = nameof(AdminEndpoints_WithRegularTenantUserToken_ReturnsForbidden);
        using (var db = CreateInMemoryDbContext(dbName))
        {
            var tenant = new Tenant
            {
                Id = Guid.NewGuid(),
                Name = "Regular Tenant",
                Slug = "regular-tenant",
                IsActive = true
            };
            var tenantUser = new User
            {
                Id = Guid.NewGuid(),
                Email = "member@tenant.com",
                FullName = "Member",
                IsPlatformAdmin = false,
                IsActive = true,
                AuthVersion = 1
            };
            var membership = new TenantMembership
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                UserId = tenantUser.Id,
                Tenant = tenant,
                Role = TenantRole.TenantAdmin,
                IsActive = true
            };
            var session = new UserSession
            {
                Id = Guid.NewGuid(),
                UserId = tenantUser.Id,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                RevokedAt = null
            };
            db.Tenants.Add(tenant);
            db.Users.Add(tenantUser);
            db.TenantMemberships.Add(membership);
            db.UserSessions.Add(session);
            await db.SaveChangesAsync();

            using var scope = _factory.Services.CreateScope();
            var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
            var token = jwtService.GenerateAccessToken(tenantUser, membership, session.Id);

            var client = CreateTestClient(dbName: dbName);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.GetAsync("/api/admin/tenants");
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task CreateTenant_AsPlatformAdmin_WithCsrf_ReturnsCreated_WithOneTimeSecrets()
    {
        var dbName = nameof(CreateTenant_AsPlatformAdmin_WithCsrf_ReturnsCreated_WithOneTimeSecrets);
        using (var db = CreateInMemoryDbContext(dbName))
        {
            var adminUser = new User
            {
                Id = Guid.NewGuid(),
                Email = "superadmin@orai.io",
                FullName = "Super Admin",
                IsPlatformAdmin = true,
                IsActive = true,
                AuthVersion = 1
            };
            var session = new UserSession
            {
                Id = Guid.NewGuid(),
                UserId = adminUser.Id,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                RevokedAt = null
            };
            db.Users.Add(adminUser);
            db.UserSessions.Add(session);
            await db.SaveChangesAsync();

            using var scope = _factory.Services.CreateScope();
            var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
            var token = jwtService.GenerateAccessToken(adminUser, null, session.Id);

            var client = CreateTestClient(dbName: dbName);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var csrfResponse = await client.GetAsync("/api/auth/csrf");
            csrfResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var csrfBody = await csrfResponse.Content.ReadFromJsonAsync<JsonElement>();
            var csrfToken = csrfBody.GetProperty("token").GetString()!;
            client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", csrfToken);

            var createRequest = new CreateTenantRequest("HubSpot", "hubspot", "admin@hubspot.com", "Hub Admin");
            var response = await client.PostAsJsonAsync("/api/admin/tenants", createRequest);
            var errorText = await response.Content.ReadAsStringAsync();

            response.StatusCode.Should().Be(HttpStatusCode.Created, because: errorText);
            var body = await response.Content.ReadFromJsonAsync<CreateTenantResult>();
            body.Should().NotBeNull();
            body!.Slug.Should().Be("hubspot");
            body.TempPassword.Should().NotBeNullOrWhiteSpace();
            body.WebhookPlainKey.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task SuperAdmin_OnboardTenant_RealFlow_AnonymousCsrf_Login_RefreshedCsrf_Succeeds()
    {
        var dbName = nameof(SuperAdmin_OnboardTenant_RealFlow_AnonymousCsrf_Login_RefreshedCsrf_Succeeds);
        using (var db = CreateInMemoryDbContext(dbName))
        {
            var adminUser = new User
            {
                Id = Guid.NewGuid(),
                Email = "admin@orai.io",
                FullName = "Super Admin",
                IsPlatformAdmin = true,
                IsActive = true,
                AuthVersion = 1
            };
            var session = new UserSession
            {
                Id = Guid.NewGuid(),
                UserId = adminUser.Id,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                RevokedAt = null
            };
            db.Users.Add(adminUser);
            db.UserSessions.Add(session);
            await db.SaveChangesAsync();

            using var scope = _factory.Services.CreateScope();
            var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
            var validJwt = jwtService.GenerateAccessToken(adminUser, null, session.Id);
            _fakeAuthService.CustomAccessToken = validJwt;

            var client = CreateTestClient(dbName: dbName);

            // 1. Initial page load on /login: fetch anonymous CSRF token
            var csrfRes = await client.GetAsync("/api/auth/csrf");
            csrfRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var anonCsrfBody = await csrfRes.Content.ReadFromJsonAsync<JsonElement>();
            var anonCsrf = anonCsrfBody.GetProperty("token").GetString()!;

            // 2. Perform Login with anonymous CSRF token
            var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new LoginRequest("admin@orai.io", "AdminPass123!"))
            };
            loginRequest.Headers.Add("X-XSRF-TOKEN", anonCsrf);
            var loginRes = await client.SendAsync(loginRequest);
            loginRes.StatusCode.Should().Be(HttpStatusCode.OK);

            // 3. Post-login: fetch fresh authenticated CSRF token
            var authCsrfRes = await client.GetAsync("/api/auth/csrf");
            authCsrfRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var authCsrfBody = await authCsrfRes.Content.ReadFromJsonAsync<JsonElement>();
            var authCsrf = authCsrfBody.GetProperty("token").GetString()!;

            // 4. Onboard new client as Super Admin using cookie-based auth and fresh authenticated CSRF token
            var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/admin/tenants")
            {
                Content = JsonContent.Create(new CreateTenantRequest("Stripe Corp", "stripe-corp", "admin@stripe.com", "Stripe Admin"))
            };
            createRequest.Headers.Add("X-XSRF-TOKEN", authCsrf);
            var createRes = await client.SendAsync(createRequest);
            var createError = await createRes.Content.ReadAsStringAsync();

            createRes.StatusCode.Should().Be(HttpStatusCode.Created, because: createError);
            var body = await createRes.Content.ReadFromJsonAsync<CreateTenantResult>();
            body.Should().NotBeNull();
            body!.Slug.Should().Be("stripe-corp");
            body.TempPassword.Should().NotBeNullOrWhiteSpace();
            body.WebhookPlainKey.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task SuperAdmin_OnboardTenant_WithStaleAnonymousCsrf_FailsWith400_ThenRetryingWithFreshTokenSucceeds()
    {
        var dbName = nameof(SuperAdmin_OnboardTenant_WithStaleAnonymousCsrf_FailsWith400_ThenRetryingWithFreshTokenSucceeds);
        using (var db = CreateInMemoryDbContext(dbName))
        {
            var adminUser = new User
            {
                Id = Guid.NewGuid(),
                Email = "admin@orai.io",
                FullName = "Super Admin",
                IsPlatformAdmin = true,
                IsActive = true,
                AuthVersion = 1
            };
            var session = new UserSession
            {
                Id = Guid.NewGuid(),
                UserId = adminUser.Id,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                RevokedAt = null
            };
            db.Users.Add(adminUser);
            db.UserSessions.Add(session);
            await db.SaveChangesAsync();

            using var scope = _factory.Services.CreateScope();
            var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
            var validJwt = jwtService.GenerateAccessToken(adminUser, null, session.Id);
            _fakeAuthService.CustomAccessToken = validJwt;

            var client = CreateTestClient(dbName: dbName);

            // 1. Fetch anonymous CSRF token
            var csrfRes = await client.GetAsync("/api/auth/csrf");
            csrfRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var anonCsrfBody = await csrfRes.Content.ReadFromJsonAsync<JsonElement>();
            var anonCsrf = anonCsrfBody.GetProperty("token").GetString()!;

            // 2. Perform Login with anonymous CSRF token
            var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new LoginRequest("admin@orai.io", "AdminPass123!"))
            };
            loginRequest.Headers.Add("X-XSRF-TOKEN", anonCsrf);
            var loginRes = await client.SendAsync(loginRequest);
            loginRes.StatusCode.Should().Be(HttpStatusCode.OK);

            // 3. Attempt to create tenant with the stale anonymous CSRF token -> fails with 400 Antiforgery
            var staleCreateRequest = new HttpRequestMessage(HttpMethod.Post, "/api/admin/tenants")
            {
                Content = JsonContent.Create(new CreateTenantRequest("Stale Corp", "stale-corp", "admin@stale.com", "Stale Admin"))
            };
            staleCreateRequest.Headers.Add("X-XSRF-TOKEN", anonCsrf);
            var staleRes = await client.SendAsync(staleCreateRequest);

            staleRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var staleBody = await staleRes.Content.ReadFromJsonAsync<JsonElement>();
            staleBody.GetProperty("error").GetString().Should().Contain("Antiforgery");

            // 4. Client auto-recovery: fetch fresh authenticated CSRF token
            var authCsrfRes = await client.GetAsync("/api/auth/csrf");
            authCsrfRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var authCsrfBody = await authCsrfRes.Content.ReadFromJsonAsync<JsonElement>();
            var authCsrf = authCsrfBody.GetProperty("token").GetString()!;

            // 5. Retry POST /api/admin/tenants with fresh token (single retry) -> succeeds with 201 Created
            var retryCreateRequest = new HttpRequestMessage(HttpMethod.Post, "/api/admin/tenants")
            {
                Content = JsonContent.Create(new CreateTenantRequest("Stale Corp", "stale-corp", "admin@stale.com", "Stale Admin"))
            };
            retryCreateRequest.Headers.Add("X-XSRF-TOKEN", authCsrf);
            var retryRes = await client.SendAsync(retryCreateRequest);
            var retryError = await retryRes.Content.ReadAsStringAsync();

            retryRes.StatusCode.Should().Be(HttpStatusCode.Created, because: retryError);
            var body = await retryRes.Content.ReadFromJsonAsync<CreateTenantResult>();
            body.Should().NotBeNull();
            body!.Slug.Should().Be("stale-corp");
        }
    }

    [Fact]
    public async Task AdminEndpoints_MutatingOperations_RequireValidCsrf()
    {
        var dbName = nameof(AdminEndpoints_MutatingOperations_RequireValidCsrf);
        using (var db = CreateInMemoryDbContext(dbName))
        {
            var adminUser = new User
            {
                Id = Guid.NewGuid(),
                Email = "admin@orai.io",
                FullName = "Super Admin",
                IsPlatformAdmin = true,
                IsActive = true,
                AuthVersion = 1
            };
            var session = new UserSession
            {
                Id = Guid.NewGuid(),
                UserId = adminUser.Id,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                RevokedAt = null
            };
            db.Users.Add(adminUser);
            db.UserSessions.Add(session);
            await db.SaveChangesAsync();

            using var scope = _factory.Services.CreateScope();
            var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
            var validJwt = jwtService.GenerateAccessToken(adminUser, null, session.Id);
            _fakeAuthService.CustomAccessToken = validJwt;

            var client = CreateTestClient(dbName: dbName);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", validJwt);

            var tenantId = Guid.NewGuid();
            var endpointId = Guid.NewGuid();

            // Status Update without CSRF -> 400
            var patchRes1 = await client.PatchAsJsonAsync($"/api/admin/tenants/{tenantId}/status", new UpdateTenantStatusRequest(false));
            patchRes1.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // Password Reset without CSRF -> 400
            var resetRes1 = await client.PostAsync($"/api/admin/tenants/{tenantId}/reset-client-password", null);
            resetRes1.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // Rotate Key without CSRF -> 400
            var rotateRes1 = await client.PostAsync($"/api/admin/webhook-endpoints/{endpointId}/rotate-key", null);
            rotateRes1.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // Fetch authenticated CSRF token
            var csrfRes = await client.GetAsync("/api/auth/csrf");
            csrfRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var csrfBody = await csrfRes.Content.ReadFromJsonAsync<JsonElement>();
            var csrfToken = csrfBody.GetProperty("token").GetString()!;
            client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", csrfToken);

            // Status Update with CSRF -> 200
            var patchRes2 = await client.PatchAsJsonAsync($"/api/admin/tenants/{tenantId}/status", new UpdateTenantStatusRequest(false));
            patchRes2.StatusCode.Should().Be(HttpStatusCode.OK);

            // Password Reset with CSRF -> 200
            var resetRes2 = await client.PostAsync($"/api/admin/tenants/{tenantId}/reset-client-password", null);
            resetRes2.StatusCode.Should().Be(HttpStatusCode.OK);

            // Rotate Key with CSRF -> 200
            var rotateRes2 = await client.PostAsync($"/api/admin/webhook-endpoints/{endpointId}/rotate-key", null);
            rotateRes2.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task AuthenticatedTenant_GetOwnMessageEvents_SucceedsWithEventTimeline()
    {
        var dbName = nameof(AuthenticatedTenant_GetOwnMessageEvents_SucceedsWithEventTimeline);
        var tenantId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var fakeDashboard = new FakeDashboardRepository();
        fakeDashboard.SetTenantActive(tenantId, true);

        var evt1 = new MessageStatusEventDto(
            Id: Guid.NewGuid(),
            MessageId: messageId,
            Wamid: "wamid.test12345",
            Status: "sent",
            StatusTimestamp: DateTimeOffset.UtcNow.AddMinutes(-10),
            ErrorCode: null,
            ErrorTitle: null,
            ErrorMessage: null,
            ErrorDetails: null,
            ErrorData: null,
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-10)
        );
        var evt2 = new MessageStatusEventDto(
            Id: Guid.NewGuid(),
            MessageId: messageId,
            Wamid: "wamid.test12345",
            Status: "delivered",
            StatusTimestamp: DateTimeOffset.UtcNow.AddMinutes(-5),
            ErrorCode: null,
            ErrorTitle: null,
            ErrorMessage: null,
            ErrorDetails: null,
            ErrorData: null,
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-5)
        );
        fakeDashboard.SetEvents(tenantId, messageId, new List<MessageStatusEventDto> { evt1, evt2 });

        using (var db = CreateInMemoryDbContext(dbName))
        {
            var tenant = new Tenant
            {
                Id = tenantId,
                Name = "Acme Corp",
                Slug = "acme-corp",
                IsActive = true
            };
            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = "user@acme.com",
                FullName = "Acme User",
                IsPlatformAdmin = false,
                IsActive = true,
                AuthVersion = 1
            };
            var membership = new TenantMembership
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = user.Id,
                Role = TenantRole.TenantAdmin,
                IsActive = true,
                Tenant = tenant,
                User = user
            };
            user.Memberships.Add(membership);
            var session = new UserSession
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                RevokedAt = null
            };

            db.Tenants.Add(tenant);
            db.Users.Add(user);
            db.TenantMemberships.Add(membership);
            db.UserSessions.Add(session);
            await db.SaveChangesAsync();

            using var scope = _factory.Services.CreateScope();
            var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
            var token = jwtService.GenerateAccessToken(user, membership, session.Id);

            var client = CreateTestClient(
                environment: "Production",
                configureServices: services => services.AddScoped<IDashboardRepository>(_ => fakeDashboard),
                dbName: dbName);

            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/messages/{messageId}/events");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var events = await response.Content.ReadFromJsonAsync<List<MessageStatusEventDto>>();
            events.Should().NotBeNull();
            events.Should().HaveCount(2);
            events![0].Status.Should().Be("sent");
            events[1].Status.Should().Be("delivered");
        }
    }

    [Fact]
    public async Task AuthenticatedTenant_GetCrossTenantMessageEvents_Returns404NotFound_PreservingIsolation()
    {
        var dbName = nameof(AuthenticatedTenant_GetCrossTenantMessageEvents_Returns404NotFound_PreservingIsolation);
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var messageBId = Guid.NewGuid();

        var fakeDashboard = new FakeDashboardRepository();
        fakeDashboard.SetTenantActive(tenantAId, true);
        fakeDashboard.SetTenantActive(tenantBId, true);

        // Message B belongs to Tenant B
        fakeDashboard.SetEvents(tenantBId, messageBId, new List<MessageStatusEventDto>
        {
            new(Guid.NewGuid(), messageBId, "wamid.b", "delivered", DateTimeOffset.UtcNow, null, null, null, null, null, DateTimeOffset.UtcNow)
        });

        using (var db = CreateInMemoryDbContext(dbName))
        {
            var tenantA = new Tenant
            {
                Id = tenantAId,
                Name = "Tenant A",
                Slug = "tenant-a",
                IsActive = true
            };
            var userA = new User
            {
                Id = Guid.NewGuid(),
                Email = "user@tenant-a.com",
                FullName = "Tenant A User",
                IsPlatformAdmin = false,
                IsActive = true,
                AuthVersion = 1
            };
            var membershipA = new TenantMembership
            {
                Id = Guid.NewGuid(),
                TenantId = tenantAId,
                UserId = userA.Id,
                Role = TenantRole.Member,
                IsActive = true,
                Tenant = tenantA,
                User = userA
            };
            userA.Memberships.Add(membershipA);
            var sessionA = new UserSession
            {
                Id = Guid.NewGuid(),
                UserId = userA.Id,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                RevokedAt = null
            };

            db.Tenants.Add(tenantA);
            db.Users.Add(userA);
            db.TenantMemberships.Add(membershipA);
            db.UserSessions.Add(sessionA);
            await db.SaveChangesAsync();

            using var scope = _factory.Services.CreateScope();
            var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
            var tokenA = jwtService.GenerateAccessToken(userA, membershipA, sessionA.Id);

            var client = CreateTestClient(
                environment: "Production",
                configureServices: services => services.AddScoped<IDashboardRepository>(_ => fakeDashboard),
                dbName: dbName);

            // User A requests Message B's events (even attempting spoofed X-Tenant-Id header)
            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/messages/{messageBId}/events");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
            request.Headers.Add("X-Tenant-Id", tenantBId.ToString());

            var response = await client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound,
                because: "Tenant isolation must return 404 without revealing cross-tenant message existence");
        }
    }

    [Fact]
    public async Task PlatformAdmin_WithInspectTenantHeader_GetMessageEvents_Succeeds()
    {
        var dbName = nameof(PlatformAdmin_WithInspectTenantHeader_GetMessageEvents_Succeeds);
        var inspectedTenantId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        var fakeDashboard = new FakeDashboardRepository();
        fakeDashboard.SetTenantActive(inspectedTenantId, true);
        fakeDashboard.SetEvents(inspectedTenantId, messageId, new List<MessageStatusEventDto>
        {
            new(Guid.NewGuid(), messageId, "wamid.admin.view", "read", DateTimeOffset.UtcNow, null, null, null, null, null, DateTimeOffset.UtcNow)
        });

        using (var db = CreateInMemoryDbContext(dbName))
        {
            var adminUser = new User
            {
                Id = Guid.NewGuid(),
                Email = "superadmin@orai.io",
                FullName = "Super Admin",
                IsPlatformAdmin = true,
                IsActive = true,
                AuthVersion = 1
            };
            var session = new UserSession
            {
                Id = Guid.NewGuid(),
                UserId = adminUser.Id,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                RevokedAt = null
            };

            db.Users.Add(adminUser);
            db.UserSessions.Add(session);
            await db.SaveChangesAsync();

            using var scope = _factory.Services.CreateScope();
            var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
            var adminToken = jwtService.GenerateAccessToken(adminUser, null, session.Id);

            var client = CreateTestClient(
                environment: "Production",
                configureServices: services => services.AddScoped<IDashboardRepository>(_ => fakeDashboard),
                dbName: dbName);

            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/messages/{messageId}/events");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            request.Headers.Add("X-Tenant-Id", inspectedTenantId.ToString());

            var response = await client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var events = await response.Content.ReadFromJsonAsync<List<MessageStatusEventDto>>();
            events.Should().NotBeNull();
            events.Should().HaveCount(1);
            events![0].Status.Should().Be("read");
        }
    }

    [Fact]
    public async Task Cors_ConfiguredOrigins_AllowsCredentials_AndRejectsUntrustedOrigins()
    {
        var client = CreateTestClient();

        // 1. Preflight from configured origin (http://localhost:3000)
        var preflightReq = new HttpRequestMessage(HttpMethod.Options, "/api/auth/csrf");
        preflightReq.Headers.Add("Origin", "http://localhost:3000");
        preflightReq.Headers.Add("Access-Control-Request-Method", "POST");
        preflightReq.Headers.Add("Access-Control-Request-Headers", "X-XSRF-TOKEN,Content-Type");

        var preflightRes = await client.SendAsync(preflightReq);
        preflightRes.StatusCode.Should().Be(HttpStatusCode.NoContent);
        preflightRes.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowOriginValues).Should().BeTrue();
        allowOriginValues!.First().Should().Be("http://localhost:3000");
        preflightRes.Headers.TryGetValues("Access-Control-Allow-Credentials", out var allowCredsValues).Should().BeTrue();
        allowCredsValues!.First().Should().Be("true");

        // 2. Request from untrusted origin (http://malicious-site.com)
        var untrustedReq = new HttpRequestMessage(HttpMethod.Get, "/api/auth/csrf");
        untrustedReq.Headers.Add("Origin", "http://malicious-site.com");

        var untrustedRes = await client.SendAsync(untrustedReq);
        untrustedRes.Headers.TryGetValues("Access-Control-Allow-Origin", out var _).Should().BeFalse(
            "CORS policy must reject or not return Access-Control-Allow-Origin for untrusted origins");
    }

    private class FakeAuthService : IAuthService
    {
        public string? CustomAccessToken { get; set; }
        public string? CustomRefreshToken { get; set; }

        public Task<LoginResult> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
        {
            if (request.Email == "admin@orai.io" && request.Password == "AdminPass123!")
            {
                var user = new UserDto(Guid.NewGuid(), "admin@orai.io", "Admin", true, false, true);
                var token = CustomAccessToken ?? "fake-jwt-access-token";
                var refresh = CustomRefreshToken ?? "fake-refresh-token";
                return Task.FromResult(new LoginResult(true, null, token, refresh, DateTimeOffset.UtcNow.AddMinutes(15), user, null));
            }

            return Task.FromResult(new LoginResult(false, "Invalid email or password", null, null, null, null, null));
        }

        public Task<RefreshResult> RefreshSessionAsync(string plainRefreshToken, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
        {
            if (plainRefreshToken == "fake-refresh-token")
            {
                var user = new UserDto(Guid.NewGuid(), "admin@orai.io", "Admin", true, false, true);
                return Task.FromResult(new RefreshResult(true, null, "new-jwt-access-token", "new-refresh-token", DateTimeOffset.UtcNow.AddMinutes(15), user, null));
            }

            return Task.FromResult(new RefreshResult(false, "Session invalid", null, null, null, null, null));
        }

        public Task<bool> LogoutAsync(string? plainRefreshToken, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<AuthSessionDto?> GetCurrentSessionAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var user = new UserDto(userId, "user@orai.io", "User", true, false, true);
            return Task.FromResult<AuthSessionDto?>(new AuthSessionDto(user, null));
        }

        public Task<bool> BootstrapAdminAsync(string email, string password, string fullName = "Super Admin", CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private class FakeAdminService : IAdminService
    {
        public Task<PagedResult<AdminTenantListItemDto>> GetTenantsAsync(AdminTenantFilterParams filter, CancellationToken cancellationToken = default)
        {
            var items = new List<AdminTenantListItemDto>
            {
                new(Guid.NewGuid(), "Demo Tenant", "demo", true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, 10, "admin@demo.com", "Demo Admin")
            };
            return Task.FromResult(new PagedResult<AdminTenantListItemDto>(items, 1, 1, 20, 1));
        }

        public Task<AdminTenantSummaryDto?> GetTenantSummaryAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<AdminTenantSummaryDto?>(new AdminTenantSummaryDto(
                tenantId,
                "Demo Tenant",
                "demo",
                true,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                new List<AdminTenantUserDto>(),
                new List<AdminTenantEndpointDto>(),
                100,
                2
            ));
        }

        public Task<CreateTenantResult> CreateTenantAsync(CreateTenantRequest request, Guid adminUserId, string? ipAddress, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CreateTenantResult(
                Guid.NewGuid(),
                request.Name,
                request.Slug,
                Guid.NewGuid(),
                request.AdminEmail,
                "TempPassword123!",
                Guid.NewGuid(),
                "Default WhatsApp",
                $"/api/webhooks/whatsapp?key=whk_demo12345",
                "whk_demo12345",
                "whk_demo"
            ));
        }

        public Task<bool> UpdateTenantStatusAsync(Guid tenantId, bool isActive, Guid adminUserId, string? ipAddress, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<ResetClientPasswordResult> ResetClientPasswordAsync(Guid tenantId, Guid adminUserId, string? ipAddress, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ResetClientPasswordResult(Guid.NewGuid(), "admin@tenant.com", "NewTempPass123!"));
        }

        public Task<RotateKeyResult> RotateWebhookKeyAsync(Guid endpointId, Guid adminUserId, string? ipAddress, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new RotateKeyResult(endpointId, "whk_newrotated123", "whk_newr"));
        }

        public Task<PlatformSummaryDto> GetPlatformSummaryAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PlatformSummaryDto(5, 4, 1, 1500, 25, 3, 0));
        }
    }
}

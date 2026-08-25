using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OraiWebhookManager.Api.Logging;
using OraiWebhookManager.Api.Middleware;
using OraiWebhookManager.Api.Services;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Options;
using OraiWebhookManager.Infrastructure;
using OraiWebhookManager.Infrastructure.Persistence;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Webhook key log redaction across all providers and diagnostics
builder.Services.AddWebhookKeyRedactionLogging();

// Add services to the container.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();

// Infrastructure layer (EF Core, Repositories, Workers, Services)
builder.Services.AddInfrastructure(builder.Configuration);

// Antiforgery / CSRF protection
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = ".AspNetCore.Antiforgery";
    options.Cookie.Path = "/";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// JWT Authentication
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret));

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = signingKey,
        ValidateIssuer = true,
        ValidIssuer = jwtOptions.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtOptions.Audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30)
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            // First try Authorization Bearer header
            var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                context.Token = authHeader["Bearer ".Length..].Trim();
                return Task.CompletedTask;
            }

            // Fallback to HttpOnly cookie
            if (context.Request.Cookies.TryGetValue("orai_access_token", out var cookieToken))
            {
                context.Token = cookieToken;
            }

            return Task.CompletedTask;
        },
        OnTokenValidated = async context =>
        {
            var dbContext = context.HttpContext.RequestServices.GetService<IPlatformAdminDbContext>();
            if (dbContext == null) return;

            var subClaim = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!Guid.TryParse(subClaim, out var userId))
            {
                context.Fail("Invalid user subject in token.");
                return;
            }

            var user = await dbContext.Users
                .Include(u => u.Memberships)
                    .ThenInclude(m => m.Tenant)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null || !user.IsActive)
            {
                context.Fail("User account is inactive or not found.");
                return;
            }

            // 1. Validate auth_version (immediate revocation on password change, reset, or version increment)
            var authVersionClaim = context.Principal?.FindFirst("auth_version")?.Value;
            if (string.IsNullOrEmpty(authVersionClaim) || !int.TryParse(authVersionClaim, out var tokenAuthVersion) || tokenAuthVersion != user.AuthVersion)
            {
                context.Fail("Security version mismatch. Token has been revoked.");
                return;
            }

            // 2. Validate sid (session id)
            var sidClaim = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sid)?.Value
                ?? context.Principal?.FindFirst("sid")?.Value;

            if (!Guid.TryParse(sidClaim, out var sessionId))
            {
                context.Fail("Session identifier (sid) is missing or invalid.");
                return;
            }

            var session = await dbContext.UserSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId);

            if (session == null || session.RevokedAt != null || session.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                context.Fail("Session is revoked, expired, or invalid.");
                return;
            }

            // 3. Validate tenant and membership for non-platform admins
            if (!user.IsPlatformAdmin)
            {
                var tenantClaim = context.Principal?.FindFirst("tenant_id")?.Value;
                if (Guid.TryParse(tenantClaim, out var tenantId))
                {
                    var membership = user.Memberships.FirstOrDefault(m => m.TenantId == tenantId && m.IsActive);
                    if (membership == null || membership.Tenant == null || !membership.Tenant.IsActive)
                    {
                        context.Fail("Tenant is inactive or membership revoked.");
                        return;
                    }
                }
            }
        }
    };
});

// Authorization policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("PlatformAdmin", policy =>
    {
        policy.RequireAssertion(context =>
            context.User.IsInRole("PlatformAdmin") ||
            context.User.HasClaim("is_platform_admin", "true"));
    });
});

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("AuthLimiter", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ip,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });
});

builder.Services.AddControllers();

// OpenAPI Document configuration
builder.Services.AddOpenApi();

// Forwarded Headers for Azure / Reverse Proxies
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// CORS configuration
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? ["http://localhost:3000"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendCorsPolicy", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// CLI Super Admin Bootstrap Command
if (args.Contains("--bootstrap-admin"))
{
    using var scope = app.Services.CreateScope();
    var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();

    string? email = null;
    string name = "Super Admin";

    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--email" && i + 1 < args.Length) email = args[i + 1];
        if (args[i] == "--name" && i + 1 < args.Length) name = args[i + 1];
    }

    email ??= app.Configuration["BOOTSTRAP_ADMIN_EMAIL"] ?? app.Configuration["BootstrapAdmin:Email"] ?? Environment.GetEnvironmentVariable("BOOTSTRAP_ADMIN_EMAIL");
    name = app.Configuration["BOOTSTRAP_ADMIN_NAME"] ?? app.Configuration["BootstrapAdmin:Name"] ?? Environment.GetEnvironmentVariable("BOOTSTRAP_ADMIN_NAME") ?? name;

    string? password = app.Configuration["BOOTSTRAP_ADMIN_PASSWORD"] ?? app.Configuration["BootstrapAdmin:Password"] ?? Environment.GetEnvironmentVariable("BOOTSTRAP_ADMIN_PASSWORD");

    if (string.IsNullOrWhiteSpace(password))
    {
        if (!Console.IsInputRedirected)
        {
            Console.Write("Enter Bootstrap Super Admin Password: ");
            password = ReadPasswordMasked();
            Console.WriteLine();
        }
        else
        {
            Console.WriteLine("Error: Super Admin bootstrap password must be provided via environment variable (BOOTSTRAP_ADMIN_PASSWORD) or .NET User Secrets/Configuration (BootstrapAdmin:Password) when running in non-interactive mode.");
            Environment.ExitCode = 1;
            return;
        }
    }

    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
    {
        Console.WriteLine("Error: Super Admin bootstrap requires --email (or BOOTSTRAP_ADMIN_EMAIL) and a non-empty password.");
        Environment.ExitCode = 1;
        return;
    }

    var success = await authService.BootstrapAdminAsync(email, password, name);
    if (success)
    {
        Console.WriteLine($"[Bootstrap] Platform Super Admin for '{email}' successfully created.");
    }
    else
    {
        Console.WriteLine($"[Bootstrap Error] Platform Super Admin already exists or invalid parameters. Refusing to overwrite.");
        Environment.ExitCode = 1;
    }
    return;
}

static string ReadPasswordMasked()
{
    var password = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
            break;
        if (key.Key == ConsoleKey.Backspace)
        {
            if (password.Length > 0)
            {
                password.Remove(password.Length - 1, 1);
            }
        }
        else if (!char.IsControl(key.KeyChar))
        {
            password.Append(key.KeyChar);
        }
    }
    return password.ToString();
}

app.UseForwardedHeaders();

// Webhook key URL/telemetry redaction middleware
app.UseMiddleware<WebhookKeyRedactionMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseCors("FrontendCorsPolicy");

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }

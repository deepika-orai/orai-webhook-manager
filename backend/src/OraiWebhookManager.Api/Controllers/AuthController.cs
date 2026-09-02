using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using OraiWebhookManager.Api.Filters;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Models;
using OraiWebhookManager.Application.Options;

namespace OraiWebhookManager.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IWebHostEnvironment _environment;
    private readonly JwtOptions _jwtOptions;

    public AuthController(
        IAuthService _authService,
        ICurrentUserContext currentUserContext,
        IWebHostEnvironment environment,
        IOptions<JwtOptions> jwtOptions)
    {
        this._authService = _authService;
        _currentUserContext = currentUserContext;
        _environment = environment;
        _jwtOptions = jwtOptions.Value;
    }

    [HttpGet("csrf")]
    [AllowAnonymous]
    public IActionResult GetCsrfToken([FromServices] IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        var isProduction = !_environment.IsDevelopment();

        Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            Secure = isProduction ? true : Request.IsHttps,
            SameSite = isProduction ? SameSiteMode.None : SameSiteMode.Lax,
            Path = "/"
        });

        return Ok(new { token = tokens.RequestToken });
    }

    [HttpPost("login")]
    [ValidateCsrf]
    [EnableRateLimiting("AuthLimiter")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        var result = await _authService.LoginAsync(request, ipAddress, userAgent, cancellationToken);
        if (!result.Succeeded || string.IsNullOrEmpty(result.AccessToken))
        {
            return Unauthorized(new { error = result.ErrorMessage ?? "Invalid email or password" });
        }

        SetAuthCookies(result.AccessToken, result.RefreshToken, result.ExpiresAt);

        return Ok(new
        {
            succeeded = true,
            user = result.User,
            tenant = result.Tenant,
            mustChangePassword = result.MustChangePassword
        });
    }

    [HttpPost("refresh")]
    [ValidateCsrf]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        var refreshToken = Request.Cookies["orai_refresh_token"];
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Unauthorized(new { error = "No refresh token cookie found" });
        }

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        var result = await _authService.RefreshSessionAsync(refreshToken, ipAddress, userAgent, cancellationToken);
        if (!result.Succeeded || string.IsNullOrEmpty(result.AccessToken))
        {
            ClearAuthCookies();
            return Unauthorized(new { error = result.ErrorMessage ?? "Session expired or invalid" });
        }

        SetAuthCookies(result.AccessToken, result.RefreshToken, result.ExpiresAt);

        return Ok(new
        {
            succeeded = true,
            user = result.User,
            tenant = result.Tenant,
            mustChangePassword = result.MustChangePassword
        });
    }

    [HttpPost("logout")]
    [ValidateCsrf]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var refreshToken = Request.Cookies["orai_refresh_token"];
        await _authService.LogoutAsync(refreshToken, cancellationToken);
        ClearAuthCookies();
        return Ok(new { message = "Logged out successfully" });
    }

    [HttpPost("change-password")]
    [Authorize]
    [ValidateCsrf]
    [EnableRateLimiting("AuthLimiter")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        if (!_currentUserContext.UserId.HasValue)
        {
            return Unauthorized(new { error = "User identity not established" });
        }

        var success = await _authService.ChangePasswordAsync(_currentUserContext.UserId.Value, request, cancellationToken);
        if (!success)
        {
            return BadRequest(new { error = "Invalid current password or new password does not meet security requirements (min 8 characters)" });
        }

        ClearAuthCookies();
        return Ok(new { succeeded = true, message = "Password updated successfully. Please log in with your new password." });
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetCurrentUser(CancellationToken cancellationToken)
    {
        if (!_currentUserContext.UserId.HasValue)
        {
            return Unauthorized(new { error = "User identity not established" });
        }

        var session = await _authService.GetCurrentSessionAsync(_currentUserContext.UserId.Value, cancellationToken);
        if (session == null)
        {
            ClearAuthCookies();
            return Unauthorized(new { error = "User not found or inactive" });
        }

        return Ok(session);
    }

    private void SetAuthCookies(string accessToken, string? refreshToken, DateTimeOffset? expiresAt)
    {
        var isProduction = !_environment.IsDevelopment();

        var accessCookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = isProduction ? true : Request.IsHttps,
            SameSite = isProduction ? SameSiteMode.None : SameSiteMode.Lax,
            Path = "/",
            Expires = expiresAt?.UtcDateTime ?? DateTime.UtcNow.AddMinutes(_jwtOptions.AccessTokenExpirationMinutes)
        };

        Response.Cookies.Append("orai_access_token", accessToken, accessCookieOptions);

        if (!string.IsNullOrEmpty(refreshToken))
        {
            var refreshCookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = isProduction ? true : Request.IsHttps,
                SameSite = isProduction ? SameSiteMode.None : SameSiteMode.Lax,
                Path = "/",
                Expires = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays)
            };

            Response.Cookies.Append("orai_refresh_token", refreshToken, refreshCookieOptions);
        }
    }

    private void ClearAuthCookies()
    {
        var isProduction = !_environment.IsDevelopment();

        var deleteCookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = isProduction ? true : Request.IsHttps,
            SameSite = isProduction ? SameSiteMode.None : SameSiteMode.Lax,
            Path = "/"
        };

        Response.Cookies.Delete("orai_access_token", deleteCookieOptions);
        Response.Cookies.Delete("orai_refresh_token", deleteCookieOptions);
    }
}

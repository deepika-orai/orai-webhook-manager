using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Options;
using OraiWebhookManager.Domain.Entities;

namespace OraiWebhookManager.Infrastructure.Services;

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;
    private readonly SymmetricSecurityKey _signingKey;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
    }

    public string GenerateAccessToken(User user, TenantMembership? membership, Guid? sessionId = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("name", user.FullName),
            new("is_platform_admin", user.IsPlatformAdmin ? "true" : "false"),
            new("must_change_password", user.MustChangePassword ? "true" : "false"),
            new("auth_version", user.AuthVersion.ToString())
        };

        if (sessionId.HasValue)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Sid, sessionId.Value.ToString()));
            claims.Add(new Claim("sid", sessionId.Value.ToString()));
        }

        if (user.IsPlatformAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "PlatformAdmin"));
        }

        if (membership != null)
        {
            claims.Add(new Claim("tenant_id", membership.TenantId.ToString()));
            claims.Add(new Claim("tenant_role", membership.Role.ToString()));
            claims.Add(new Claim(ClaimTypes.Role, membership.Role.ToString()));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_options.AccessTokenExpirationMinutes),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256Signature)
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public (string PlainRefreshToken, byte[] TokenHash) GenerateRefreshToken()
    {
        var randomBytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }

        var plainToken = Convert.ToBase64String(randomBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');

        var tokenHash = HashRefreshToken(plainToken);
        return (plainToken, tokenHash);
    }

    public byte[] HashRefreshToken(string plainToken)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(plainToken));
    }
}

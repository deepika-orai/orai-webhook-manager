using System.Security.Cryptography;
using System.Text;
using OraiWebhookManager.Application.Interfaces;

namespace OraiWebhookManager.Infrastructure.Services;

public class WebhookKeyService : IWebhookKeyService
{
    private const string KeyPrefixTag = "whk_live_";

    public WebhookKeyGenerateResult GenerateKey()
    {
        // 32 bytes of cryptographically secure random bytes -> 256 bits entropy
        var randomBytes = new byte[32];
        RandomNumberGenerator.Fill(randomBytes);
        var base64Token = Convert.ToHexString(randomBytes).ToLowerInvariant();
        var plainKey = $"{KeyPrefixTag}{base64Token}";

        var keyPrefix = plainKey.Length >= 16 ? plainKey[..16] : plainKey;
        var keyHash = ComputeKeyHash(plainKey);

        return new WebhookKeyGenerateResult(plainKey, keyPrefix, keyHash);
    }

    public byte[] ComputeKeyHash(string plainKey)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(plainKey.Trim()));
    }

    public string ExtractPrefix(string plainKey)
    {
        var trimmed = plainKey.Trim();
        return trimmed.Length >= 16 ? trimmed[..16] : trimmed;
    }
}

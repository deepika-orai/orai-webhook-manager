using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using OraiWebhookManager.Infrastructure.Services;
using Xunit;

namespace OraiWebhookManager.UnitTests;

public class WebhookKeyServiceTests
{
    private readonly WebhookKeyService _sut = new();

    [Fact]
    public void GenerateKey_ReturnsPrefixWhkLiveAndExact31Characters_With22CharacterBase64UrlToken()
    {
        // Act
        var result = _sut.GenerateKey();

        // Assert
        result.PlainKey.Should().StartWith("whk_live_");
        result.PlainKey.Length.Should().Be(31, "whk_live_ (9 chars) + 22 Base64URL chars = 31 chars");
        result.PlainKey[9..].Length.Should().Be(22, "Base64URL token must be exactly 22 characters");
        result.PlainKey[9..].Should().MatchRegex("^[a-zA-Z0-9_-]{22}$", "Token must only contain URL-safe unpadded Base64URL characters");
        result.PlainKey.Should().NotContain("=");
        result.PlainKey.Should().NotContain("+");
        result.PlainKey.Should().NotContain("/");
        result.KeyPrefix.Should().Be(result.PlainKey[..16]);
        result.KeyPrefix.Length.Should().Be(16, "KeyPrefix must be 16 characters for display");
        result.KeyHash.Should().NotBeNull();
        result.KeyHash.Length.Should().Be(32, "SHA-256 hash output is 32 bytes (256 bits)");
        result.KeyHash.Should().Equal(SHA256.HashData(Encoding.UTF8.GetBytes(result.PlainKey)));
    }

    [Fact]
    public void GenerateKey_ProducesHighEntropyUniqueKeys_NonRepeating()
    {
        // Arrange
        const int count = 1000;
        var keys = new HashSet<string>(count);
        var hashes = new HashSet<string>(count);

        // Act
        for (var i = 0; i < count; i++)
        {
            var gen = _sut.GenerateKey();
            keys.Add(gen.PlainKey);
            hashes.Add(Convert.ToHexString(gen.KeyHash));
        }

        // Assert
        keys.Should().HaveCount(count, "All generated webhook keys must be unique with 128-bit cryptographic entropy");
        hashes.Should().HaveCount(count, "All generated key hashes must be unique");
    }

    [Fact]
    public void ComputeKeyHash_DeterministicAndConsistent()
    {
        // Arrange
        const string plainKey = "whk_live_abcdefghijklmnopqrstuv";
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(plainKey));

        // Act
        var hash1 = _sut.ComputeKeyHash(plainKey);
        var hash2 = _sut.ComputeKeyHash($"  {plainKey}  \n");

        // Assert
        hash1.Should().Equal(expectedHash);
        hash2.Should().Equal(expectedHash, "Whitespace should be trimmed before hashing");
    }

    [Fact]
    public void ComputeKeyHash_Legacy41CharacterKey_ProducesExpectedHash_ForBackwardCompatibility()
    {
        // Arrange: Legacy 41-character key (whk_live_ + 32 hex chars)
        const string legacy41CharKey = "whk_live_0123456789abcdef0123456789abcdef";
        legacy41CharKey.Length.Should().Be(41);

        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(legacy41CharKey));

        // Act
        var computedHash = _sut.ComputeKeyHash(legacy41CharKey);

        // Assert
        computedHash.Should().Equal(expectedHash);
        computedHash.Length.Should().Be(32);
    }

    [Fact]
    public void ComputeKeyHash_Legacy73CharacterKey_ProducesExpectedHash_ForBackwardCompatibility()
    {
        // Arrange: Legacy 73-character key (whk_live_ + 64 hex chars)
        const string legacy73CharKey = "whk_live_e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        legacy73CharKey.Length.Should().Be(73);

        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(legacy73CharKey));

        // Act
        var computedHash = _sut.ComputeKeyHash(legacy73CharKey);

        // Assert
        computedHash.Should().Equal(expectedHash);
        computedHash.Length.Should().Be(32);
    }

    [Theory]
    [InlineData("whk_live_abcdefghijklmnopqrstuv", "whk_live_abcdefg")] // 31 chars -> 16 char prefix
    [InlineData("whk_live_0123456789abcdef0123456789abcdef", "whk_live_0123456")] // 41 chars -> 16 char prefix
    [InlineData("whk_live_e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", "whk_live_e3b0c44")] // 73 chars -> 16 char prefix
    [InlineData("whk_short_key", "whk_short_key")] // < 16 chars -> returns trimmed string
    public void ExtractPrefix_Extracts16CharacterPrefix_Correctly(string inputKey, string expectedPrefix)
    {
        var prefix = _sut.ExtractPrefix(inputKey);
        prefix.Should().Be(expectedPrefix);
        if (inputKey.Length >= 16)
        {
            prefix.Length.Should().Be(16);
        }
    }

    [Fact]
    public void ProductionUrl_CalculatedLength_IsStrictly87Characters()
    {
        // Arrange
        const string baseUrl = "https://oraiapi.azurewebsites.net"; // 33 chars
        const string route = "/api/webhooks/whatsapp/"; // 23 chars
        var keyGen = _sut.GenerateKey(); // 31 chars

        // Act
        var completeUrl = $"{baseUrl}{route}{keyGen.PlainKey}";

        // Assert
        baseUrl.Length.Should().Be(33);
        route.Length.Should().Be(23);
        keyGen.PlainKey.Length.Should().Be(31);
        completeUrl.Length.Should().Be(87, "33 (base) + 23 (route) + 31 (key) = 87 characters");
        completeUrl.Should().Be($"https://oraiapi.azurewebsites.net/api/webhooks/whatsapp/{keyGen.PlainKey}");
    }
}

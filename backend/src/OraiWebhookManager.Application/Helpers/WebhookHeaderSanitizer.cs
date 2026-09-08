namespace OraiWebhookManager.Application.Helpers;

public static class WebhookHeaderSanitizer
{
    /// <summary>
    /// Safe operational headers permitted in Pub/Sub envelopes.
    /// Excludes signatures, auth credentials, tokens, cookies, and secrets.
    /// </summary>
    public static readonly HashSet<string> EnvelopeAllowlistedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "User-Agent",
        "X-Forwarded-For",
        "TraceParent",
        "Content-Type"
    };

    /// <summary>
    /// Default allowlist matching EnvelopeAllowlistedHeaders.
    /// </summary>
    public static readonly HashSet<string> AllowlistedHeaders = EnvelopeAllowlistedHeaders;

    /// <summary>
    /// Direct controller ingestion allowlist preserved for backward-compatible direct storage.
    /// </summary>
    public static readonly HashSet<string> DirectIngestionAllowlistedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "User-Agent",
        "X-Hub-Signature-256",
        "X-Forwarded-For",
        "TraceParent",
        "Content-Type"
    };

    /// <summary>
    /// Explicit blocklist of sensitive headers that must NEVER be included in envelope metadata.
    /// </summary>
    private static readonly HashSet<string> ExplicitBlocklistedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie",
        "Set-Cookie",
        "X-Webhook-Key",
        "X-Api-Key",
        "Api-Key",
        "X-Hub-Signature-256",
        "Proxy-Authorization"
    };

    private static readonly string[] SensitiveKeywords =
    [
        "password",
        "secret",
        "token",
        "signature",
        "webhook-key"
    ];

    public static bool IsSensitiveHeader(string headerName)
    {
        if (string.IsNullOrWhiteSpace(headerName)) return true;

        var trimmed = headerName.Trim();

        if (ExplicitBlocklistedHeaders.Contains(trimmed))
        {
            return true;
        }

        foreach (var keyword in SensitiveKeywords)
        {
            if (trimmed.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static Dictionary<string, string> SanitizeEnvelopeHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>>? headers)
    {
        var sanitized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (headers == null)
        {
            return sanitized;
        }

        foreach (var header in headers)
        {
            if (string.IsNullOrWhiteSpace(header.Key))
            {
                continue;
            }

            var key = header.Key.Trim();

            // Strict security: Reject any sensitive header or signature
            if (IsSensitiveHeader(key))
            {
                continue;
            }

            // Only permit safe operational headers
            if (EnvelopeAllowlistedHeaders.Contains(key))
            {
                var val = string.Join(",", header.Value);
                sanitized[key] = val;
            }
        }

        return sanitized;
    }

    public static Dictionary<string, string> SanitizeEnvelopeHeaders(IEnumerable<KeyValuePair<string, string>>? headers)
    {
        var sanitized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (headers == null)
        {
            return sanitized;
        }

        foreach (var header in headers)
        {
            if (string.IsNullOrWhiteSpace(header.Key))
            {
                continue;
            }

            var key = header.Key.Trim();

            // Strict security: Reject any sensitive header or signature
            if (IsSensitiveHeader(key))
            {
                continue;
            }

            // Only permit safe operational headers
            if (EnvelopeAllowlistedHeaders.Contains(key))
            {
                sanitized[key] = header.Value;
            }
        }

        return sanitized;
    }

    public static Dictionary<string, string> SanitizeHeaders(IEnumerable<KeyValuePair<string, string>>? headers)
        => SanitizeEnvelopeHeaders(headers);

    public static Dictionary<string, string> SanitizeHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>>? headers)
        => SanitizeEnvelopeHeaders(headers);
}

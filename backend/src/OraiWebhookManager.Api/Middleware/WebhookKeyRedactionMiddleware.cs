using System.Text.RegularExpressions;
using OraiWebhookManager.Api.Logging;

namespace OraiWebhookManager.Api.Middleware;

public class WebhookKeyRedactionMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly Regex WebhookPathRegex = new(
        @"^/api/webhooks/whatsapp/(whk_[a-zA-Z0-9_-]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    public WebhookKeyRedactionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value;
        if (!string.IsNullOrEmpty(path) && WebhookPathRegex.IsMatch(path))
        {
            // Store sanitized path in HttpContext Items for logging / telemetry enrichment
            var sanitizedPath = WebhookPathRegex.Replace(path, m =>
            {
                var key = m.Groups[1].Value;
                var redacted = RedactingLogger.Redact(key);
                return $"/api/webhooks/whatsapp/{redacted}";
            });
            context.Items["SanitizedPath"] = sanitizedPath;
        }

        await _next(context);
    }
}

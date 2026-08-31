using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace OraiWebhookManager.Api.Middleware;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var path = context.Items.TryGetValue("SanitizedPath", out var sanitized)
                ? sanitized?.ToString()
                : context.Request.Path.Value;

            _logger.LogError(
                ex,
                "Unhandled exception processing HTTP {Method} {Path}. TraceIdentifier: {TraceIdentifier}",
                context.Request.Method,
                path,
                context.TraceIdentifier);

            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json";

                var responsePayload = new
                {
                    error = "An unexpected error occurred while processing your request. Please try again later.",
                    traceId = context.TraceIdentifier
                };

                await context.Response.WriteAsJsonAsync(responsePayload);
            }
        }
    }
}

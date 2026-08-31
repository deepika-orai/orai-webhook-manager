using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using OraiWebhookManager.Api.Middleware;
using Xunit;

namespace OraiWebhookManager.UnitTests;

public class GlobalExceptionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WhenNextThrowsException_CatchesAndReturns500WithTraceId()
    {
        var middleware = new GlobalExceptionMiddleware(
            next: _ => throw new InvalidOperationException("Simulated database failure"),
            logger: NullLogger<GlobalExceptionMiddleware>.Instance
        );

        var context = new DefaultHttpContext();
        context.TraceIdentifier = "test-trace-12345";
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        context.Response.ContentType.Should().StartWith("application/json");

        responseBody.Seek(0, SeekOrigin.Begin);
        var jsonDoc = await JsonDocument.ParseAsync(responseBody);
        var root = jsonDoc.RootElement;

        root.GetProperty("error").GetString().Should().Be("An unexpected error occurred while processing your request. Please try again later.");
        root.GetProperty("traceId").GetString().Should().Be("test-trace-12345");
    }

    [Fact]
    public async Task InvokeAsync_WhenNextSucceeds_PassesThrough()
    {
        var middleware = new GlobalExceptionMiddleware(
            next: ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status200OK;
                return Task.CompletedTask;
            },
            logger: NullLogger<GlobalExceptionMiddleware>.Instance
        );

        var context = new DefaultHttpContext();
        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }
}

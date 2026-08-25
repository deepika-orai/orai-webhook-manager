using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using OraiWebhookManager.Api.Controllers;
using OraiWebhookManager.Api.Models;

namespace OraiWebhookManager.UnitTests;

public class HealthControllerTests
{
    private readonly HealthController _controller;

    public HealthControllerTests()
    {
        _controller = new HealthController();
    }

    [Fact]
    public void GetHealth_ShouldReturnOkResult_WithExpectedPayload()
    {
        // Act
        var actionResult = _controller.GetHealth();

        // Assert
        var okResult = actionResult.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<HealthResponse>().Subject;

        response.Status.Should().Be("healthy");
        response.Service.Should().Be("ORAI Webhook Manager API");
        response.TimestampUtc.Should().NotBeNullOrWhiteSpace();

        // Verify valid ISO 8601 UTC timestamp format
        var parseSuccess = DateTimeOffset.TryParse(response.TimestampUtc, out var parsedDate);
        parseSuccess.Should().BeTrue();
        parsedDate.Offset.Should().Be(TimeSpan.Zero);
    }
}

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using OraiWebhookManager.Api.Models;

namespace OraiWebhookManager.IntegrationTests;

public class HealthEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetHealth_ReturnsOk_WithValidHealthResponse()
    {
        // Act
        var response = await _client.GetAsync("/api/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();
        payload.Should().NotBeNull();
        payload!.Status.Should().Be("healthy");
        payload.Service.Should().Be("ORAI Webhook Manager API");
        payload.TimestampUtc.Should().NotBeNullOrWhiteSpace();

        var parseSuccess = DateTimeOffset.TryParse(payload.TimestampUtc, out var parsedDate);
        parseSuccess.Should().BeTrue();
        parsedDate.Offset.Should().Be(TimeSpan.Zero);
    }
}

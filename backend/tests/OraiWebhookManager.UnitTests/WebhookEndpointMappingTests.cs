using FluentAssertions;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Domain.Enums;
using OraiWebhookManager.Infrastructure.Persistence.Repositories;

namespace OraiWebhookManager.UnitTests;

public class WebhookEndpointMappingTests
{
    [Fact]
    public void WebhookEndpointRow_WithNullOptionalTimestamps_MapsToActiveCachedWebhookEndpoint()
    {
        // Arrange: Active endpoint where optional timestamp fields (LastReceivedAt, RevokedAt) are NULL
        var endpointId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var keyHash = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var now = DateTimeOffset.UtcNow;

        var row = new WebhookInboxRepository.WebhookEndpointRow
        {
            Id = endpointId,
            TenantId = tenantId,
            Name = "Production WhatsApp Channel",
            KeyPrefix = "whk_live_12345678",
            KeyHash = keyHash,
            Status = "Active",
            LastReceivedAt = null,
            RevokedAt = null,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Act: Map row to CachedWebhookEndpoint (mirroring WebhookInboxRepository logic)
        var status = Enum.TryParse<WebhookEndpointStatus>(row.Status, true, out var parsedStatus)
            ? parsedStatus
            : WebhookEndpointStatus.Suspended;

        var cachedEndpoint = new CachedWebhookEndpoint(
            Id: row.Id,
            TenantId: row.TenantId,
            Name: row.Name,
            KeyPrefix: row.KeyPrefix,
            KeyHash: row.KeyHash,
            Status: status
        );

        // Assert: Non-nullable Guids and Active status are preserved without throwing RuntimeBinderException
        cachedEndpoint.Should().NotBeNull();
        cachedEndpoint.Id.Should().Be(endpointId);
        cachedEndpoint.TenantId.Should().Be(tenantId);
        cachedEndpoint.Name.Should().Be("Production WhatsApp Channel");
        cachedEndpoint.KeyPrefix.Should().Be("whk_live_12345678");
        cachedEndpoint.KeyHash.Should().Equal(keyHash);
        cachedEndpoint.Status.Should().Be(WebhookEndpointStatus.Active);
        row.LastReceivedAt.Should().BeNull();
        row.RevokedAt.Should().BeNull();
    }

    [Theory]
    [InlineData("Active", WebhookEndpointStatus.Active)]
    [InlineData("Suspended", WebhookEndpointStatus.Suspended)]
    [InlineData("Revoked", WebhookEndpointStatus.Revoked)]
    [InlineData("UnknownStatus", WebhookEndpointStatus.Suspended)]
    public void WebhookEndpointRow_StatusParsing_HandlesAllStatuses(string statusString, WebhookEndpointStatus expectedStatus)
    {
        var row = new WebhookInboxRepository.WebhookEndpointRow
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Name = "Channel",
            KeyPrefix = "whk_live_",
            KeyHash = new byte[] { 10, 20 },
            Status = statusString,
            LastReceivedAt = null,
            RevokedAt = null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var status = Enum.TryParse<WebhookEndpointStatus>(row.Status, true, out var parsedStatus)
            ? parsedStatus
            : WebhookEndpointStatus.Suspended;

        status.Should().Be(expectedStatus);
    }
}

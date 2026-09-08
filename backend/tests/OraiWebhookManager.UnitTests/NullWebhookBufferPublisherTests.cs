using FluentAssertions;
using OraiWebhookManager.Application.Models;
using OraiWebhookManager.Infrastructure.Services;
using Xunit;

namespace OraiWebhookManager.UnitTests;

public class NullWebhookBufferPublisherTests
{
    [Fact]
    public async Task PublishAsync_AlwaysThrowsInvalidOperationException()
    {
        var publisher = new NullWebhookBufferPublisher();
        var envelope = PubSubWebhookEnvelope.Create(
            correlationId: Guid.NewGuid(),
            tenantId: Guid.NewGuid(),
            endpointId: Guid.NewGuid(),
            receivedAtUtc: DateTimeOffset.UtcNow,
            payloadRaw: "{}",
            rawHeaders: new Dictionary<string, string>()
        );

        var act = () => publisher.PublishAsync(envelope);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*UsePubSubBuffer is false*");
    }
}

using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Models;

namespace OraiWebhookManager.Infrastructure.Services;

public sealed class NullWebhookBufferPublisher : IWebhookBufferPublisher
{
    public Task<string> PublishAsync(PubSubWebhookEnvelope envelope, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(
            "Pub/Sub buffer publishing is disabled (GooglePubSub:UsePubSubBuffer is false). Webhooks must be ingested directly into PostgreSQL.");
    }
}

using OraiWebhookManager.Application.Models;

namespace OraiWebhookManager.Application.Interfaces;

public interface IWebhookBufferPublisher
{
    /// <summary>
    /// Publishes a sanitized webhook envelope to the message buffer.
    /// Returns the assigned broker message ID upon success.
    /// Throws WebhookBufferPublishException if the publish fails or times out.
    /// </summary>
    Task<string> PublishAsync(PubSubWebhookEnvelope envelope, CancellationToken cancellationToken = default);
}

using Google.Cloud.PubSub.V1;

namespace OraiWebhookManager.Infrastructure.PubSub;

public interface IPubSubPublisherClient : IAsyncDisposable
{
    Task<string> PublishAsync(PubsubMessage message);
    Task ShutdownAsync(TimeSpan timeout = default, CancellationToken cancellationToken = default);
}

using Google.Cloud.PubSub.V1;

namespace OraiWebhookManager.Infrastructure.PubSub;

public interface IPubSubSubscriberClient : IAsyncDisposable
{
    Task StartAsync(Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>> handler);
    Task StopAsync(CancellationToken cancellationToken = default);
}

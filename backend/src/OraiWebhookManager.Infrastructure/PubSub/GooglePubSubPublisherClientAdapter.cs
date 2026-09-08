using Google.Cloud.PubSub.V1;

namespace OraiWebhookManager.Infrastructure.PubSub;

public sealed class GooglePubSubPublisherClientAdapter : IPubSubPublisherClient
{
    private readonly PublisherClient _client;

    public GooglePubSubPublisherClientAdapter(PublisherClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public Task<string> PublishAsync(PubsubMessage message)
    {
        return _client.PublishAsync(message);
    }

    public async Task ShutdownAsync(TimeSpan timeout = default, CancellationToken cancellationToken = default)
    {
        if (timeout > TimeSpan.Zero)
        {
            using var cts = new CancellationTokenSource(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);
            await _client.ShutdownAsync(linked.Token);
        }
        else
        {
            await _client.ShutdownAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _client.ShutdownAsync(cts.Token);
        }
        catch
        {
            // Suppress shutdown exceptions during final disposal
        }
    }
}

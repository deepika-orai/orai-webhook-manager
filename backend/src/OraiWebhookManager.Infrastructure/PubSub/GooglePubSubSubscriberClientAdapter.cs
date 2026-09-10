using Google.Cloud.PubSub.V1;

namespace OraiWebhookManager.Infrastructure.PubSub;

public sealed class GooglePubSubSubscriberClientAdapter : IPubSubSubscriberClient
{
    private readonly SubscriberClient _client;
    private int _stopped;
    private int _disposed;

    public GooglePubSubSubscriberClientAdapter(SubscriberClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public Task StartAsync(Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>> handler)
    {
        return _client.StartAsync(handler);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1)
        {
            return;
        }

#pragma warning disable CS0618 // SubscriberClient.StopAsync(CancellationToken)
        await _client.StopAsync(cancellationToken);
#pragma warning restore CS0618
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await StopAsync(cts.Token);
        }
        catch
        {
            // Suppress shutdown exceptions during final disposal
        }
    }
}

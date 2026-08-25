using System.Collections.Concurrent;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace OraiWebhookManager.Infrastructure.Services;

public interface IEndpointActivityBuffer
{
    void RecordActivity(Guid endpointId, DateTimeOffset timestamp);
    Task FlushAsync(CancellationToken cancellationToken = default);
}

public class EndpointActivityBuffer : BackgroundService, IEndpointActivityBuffer
{
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _activityTimestamps = new();
    private readonly string _connectionString;
    private readonly ILogger<EndpointActivityBuffer> _logger;

    public EndpointActivityBuffer(IConfiguration configuration, ILogger<EndpointActivityBuffer> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection string is not configured.");
        _logger = logger;
    }

    public void RecordActivity(Guid endpointId, DateTimeOffset timestamp)
    {
        _activityTimestamps.AddOrUpdate(
            endpointId,
            timestamp,
            (_, existing) => timestamp > existing ? timestamp : existing
        );
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_activityTimestamps.IsEmpty) return;

        var snapshot = new List<KeyValuePair<Guid, DateTimeOffset>>();
        foreach (var key in _activityTimestamps.Keys)
        {
            if (_activityTimestamps.TryRemove(key, out var ts))
            {
                snapshot.Add(new KeyValuePair<Guid, DateTimeOffset>(key, ts));
            }
        }

        if (snapshot.Count == 0) return;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string sql = """
                UPDATE webhook_endpoints
                SET last_received_at = GREATEST(COALESCE(last_received_at, '-infinity'::timestamptz), @LastReceivedAt),
                    updated_at = NOW()
                WHERE id = @EndpointId;
                """;

            foreach (var item in snapshot)
            {
                await connection.ExecuteAsync(
                    new CommandDefinition(sql, new
                    {
                        EndpointId = item.Key,
                        LastReceivedAt = item.Value
                    }, cancellationToken: cancellationToken));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while flushing endpoint activity buffer.");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                await FlushAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in EndpointActivityBuffer background loop.");
            }
        }

        // Final flush on shutdown
        await FlushAsync(CancellationToken.None);
    }
}

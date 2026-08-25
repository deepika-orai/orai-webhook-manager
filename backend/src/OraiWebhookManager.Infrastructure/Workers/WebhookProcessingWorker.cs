using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Options;
using OraiWebhookManager.Infrastructure.Services;

namespace OraiWebhookManager.Infrastructure.Workers;

public class WebhookProcessingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMetaWebhookParser _parser;
    private readonly IEndpointActivityBuffer _activityBuffer;
    private readonly WebhookIngestionOptions _options;
    private readonly ILogger<WebhookProcessingWorker> _logger;
    private readonly string _workerInstanceId;

    public WebhookProcessingWorker(
        IServiceScopeFactory scopeFactory,
        IMetaWebhookParser parser,
        IEndpointActivityBuffer activityBuffer,
        IOptions<WebhookIngestionOptions> options,
        ILogger<WebhookProcessingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _parser = parser;
        _activityBuffer = activityBuffer;
        _options = options.Value;
        _logger = logger;
        _workerInstanceId = $"worker-{Environment.MachineName}-{Guid.NewGuid():N}"[..32];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WebhookProcessingWorker [{WorkerId}] started.", _workerInstanceId);

        while (!stoppingToken.IsCancellationRequested)
        {
            var processedCount = 0;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var processorRepository = scope.ServiceProvider.GetRequiredService<IWebhookProcessorRepository>();

                var lockToken = Guid.NewGuid();
                var batch = await processorRepository.ClaimBatchAsync(
                    lockToken,
                    _workerInstanceId,
                    _options.WorkerBatchSize,
                    _options.LeaseDurationSeconds,
                    stoppingToken
                );

                if (batch.Count > 0)
                {
                    processedCount = batch.Count;
                    foreach (var item in batch)
                    {
                        await ProcessSingleItemAsync(processorRepository, item, lockToken, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in WebhookProcessingWorker main loop.");
            }

            if (processedCount == 0)
            {
                // No items found in queue; idle wait for 500ms
                await Task.Delay(500, stoppingToken);
            }
        }

        _logger.LogInformation("WebhookProcessingWorker [{WorkerId}] stopped.", _workerInstanceId);
    }

    private async Task ProcessSingleItemAsync(
        IWebhookProcessorRepository processorRepository,
        Domain.Entities.WebhookInboxItem item,
        Guid lockToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var events = _parser.ExtractStatusEvents(item.PayloadRaw);

            // Record endpoint activity
            _activityBuffer.RecordActivity(item.EndpointId, DateTimeOffset.UtcNow);

            var success = await processorRepository.ProcessItemAtomicAsync(
                item,
                lockToken,
                events,
                cancellationToken
            );

            if (!success)
            {
                _logger.LogWarning(
                    "Inbox item {InboxId} could not be completed because lease token was lost.",
                    item.Id
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing inbox item {InboxId}. Attempt {Attempt}.", item.Id, item.AttemptCount + 1);

            try
            {
                await processorRepository.RecordFailureAsync(
                    item.Id,
                    lockToken,
                    ex.Message,
                    item.AttemptCount + 1,
                    _options.MaxRetryAttempts,
                    cancellationToken
                );
            }
            catch (Exception recordEx)
            {
                _logger.LogError(recordEx, "Failed to record error for inbox item {InboxId}.", item.Id);
            }
        }
    }
}

namespace OraiWebhookManager.Application.Options;

public class RetentionOptions
{
    public const string SectionName = "Retention";

    public int ProcessedInboxRetentionDays { get; set; } = 14;
    public int DeadLetterRetentionDays { get; set; } = 30;
    public int StatusEventsRetentionDays { get; set; } = 90;
    public int AuditLogsRetentionDays { get; set; } = 365;
}

public class WebhookIngestionOptions
{
    public const string SectionName = "WebhookIngestion";

    public int CacheTtlSeconds { get; set; } = 60;
    public int MaxPayloadSizeBytes { get; set; } = 1_048_576; // 1 MB
    public int WorkerBatchSize { get; set; } = 100;
    public int LeaseDurationSeconds { get; set; } = 120;
    public int MaxRetryAttempts { get; set; } = 5;
}

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

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = "Dev_Default_Secret_Key_Minimum_32_Characters_Length_For_HmacSha256!";
    public string Issuer { get; set; } = "OraiWebhookManager";
    public string Audience { get; set; } = "OraiWebhookManagerClient";
    public int AccessTokenExpirationMinutes { get; set; } = 15;
    public int RefreshTokenExpirationDays { get; set; } = 7;
}

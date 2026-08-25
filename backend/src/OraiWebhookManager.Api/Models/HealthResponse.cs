namespace OraiWebhookManager.Api.Models;

public record HealthResponse(
    string Status,
    string Service,
    string TimestampUtc
);

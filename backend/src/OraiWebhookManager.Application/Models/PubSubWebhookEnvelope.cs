using System.Text.Json.Serialization;
using OraiWebhookManager.Application.Helpers;

namespace OraiWebhookManager.Application.Models;

public sealed class PubSubWebhookEnvelope
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("correlation_id")]
    public Guid CorrelationId { get; init; }

    [JsonPropertyName("tenant_id")]
    public Guid TenantId { get; init; }

    [JsonPropertyName("endpoint_id")]
    public Guid EndpointId { get; init; }

    [JsonPropertyName("received_at_utc")]
    public DateTimeOffset ReceivedAtUtc { get; init; }

    [JsonPropertyName("payload_raw")]
    public string PayloadRaw { get; init; } = string.Empty;

    [JsonPropertyName("filtered_headers")]
    public IReadOnlyDictionary<string, string> FilteredHeaders { get; init; } = new Dictionary<string, string>();

    [JsonPropertyName("source_ip")]
    public string? SourceIp { get; init; }

    [JsonPropertyName("content_type")]
    public string? ContentType { get; init; }

    public static PubSubWebhookEnvelope Create(
        Guid correlationId,
        Guid tenantId,
        Guid endpointId,
        DateTimeOffset receivedAtUtc,
        string payloadRaw,
        IEnumerable<KeyValuePair<string, string>> rawHeaders,
        string? sourceIp = null,
        string? contentType = null)
    {
        if (correlationId == Guid.Empty)
            throw new ArgumentException("CorrelationId cannot be empty.", nameof(correlationId));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId cannot be empty.", nameof(tenantId));
        if (endpointId == Guid.Empty)
            throw new ArgumentException("EndpointId cannot be empty.", nameof(endpointId));
        if (string.IsNullOrWhiteSpace(payloadRaw))
            throw new ArgumentException("PayloadRaw cannot be empty.", nameof(payloadRaw));

        var sanitizedHeaders = WebhookHeaderSanitizer.SanitizeHeaders(rawHeaders);

        return new PubSubWebhookEnvelope
        {
            SchemaVersion = 1,
            CorrelationId = correlationId,
            TenantId = tenantId,
            EndpointId = endpointId,
            ReceivedAtUtc = receivedAtUtc.ToUniversalTime(),
            PayloadRaw = payloadRaw,
            FilteredHeaders = sanitizedHeaders,
            SourceIp = sourceIp,
            ContentType = contentType
        };
    }
}

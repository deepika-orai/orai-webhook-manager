using System.Text.Json.Serialization;

namespace OraiWebhookManager.Application.Models;

public class MetaWebhookPayload
{
    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("entry")]
    public List<MetaEntry> Entry { get; set; } = new();
}

public class MetaEntry
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("changes")]
    public List<MetaChange> Changes { get; set; } = new();
}

public class MetaChange
{
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    [JsonPropertyName("value")]
    public MetaValue? Value { get; set; }
}

public class MetaValue
{
    [JsonPropertyName("messaging_product")]
    public string? MessagingProduct { get; set; }

    [JsonPropertyName("metadata")]
    public MetaMetadata? Metadata { get; set; }

    [JsonPropertyName("statuses")]
    public List<MetaStatus>? Statuses { get; set; }

    [JsonPropertyName("errors")]
    public List<MetaError>? Errors { get; set; }
}

public class MetaMetadata
{
    [JsonPropertyName("display_phone_number")]
    public string? DisplayPhoneNumber { get; set; }

    [JsonPropertyName("phone_number_id")]
    public string? PhoneNumberId { get; set; }
}

public class MetaStatus
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("recipient_id")]
    public string? RecipientId { get; set; }

    [JsonPropertyName("conversation")]
    public MetaConversation? Conversation { get; set; }

    [JsonPropertyName("pricing")]
    public MetaPricing? Pricing { get; set; }

    [JsonPropertyName("errors")]
    public List<MetaError>? Errors { get; set; }

    [JsonPropertyName("biz_opaque_callback_data")]
    public string? BizOpaqueCallbackData { get; set; }
}

public class MetaConversation
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("expiration_timestamp")]
    public string? ExpirationTimestamp { get; set; }

    [JsonPropertyName("origin")]
    public MetaOrigin? Origin { get; set; }
}

public class MetaOrigin
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

public class MetaPricing
{
    [JsonPropertyName("billable")]
    public bool? Billable { get; set; }

    [JsonPropertyName("pricing_model")]
    public string? PricingModel { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }
}

public class MetaError
{
    [JsonPropertyName("code")]
    public int? Code { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("error_data")]
    public MetaErrorData? ErrorData { get; set; }
}

public class MetaErrorData
{
    [JsonPropertyName("details")]
    public string? Details { get; set; }
}

public record ExtractedStatusEvent(
    string Wamid,
    string Status,
    DateTimeOffset StatusTimestamp,
    string? PhoneNumberId,
    string? DisplayPhoneNumber,
    string? RecipientPhone,
    string? ConversationId,
    string? ConversationOriginType,
    DateTimeOffset? ConversationExpiresAt,
    string? PricingModel,
    string? PricingCategory,
    bool? PricingBillable,
    string? ErrorCode,
    string? ErrorTitle,
    string? ErrorMessage,
    string? ErrorDetails,
    string? ErrorDataJson,
    string? BizOpaqueCallbackData,
    string RawEventSnippetJson
);

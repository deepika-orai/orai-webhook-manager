using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OraiWebhookManager.Application.Models;

public class CreateTemplateMappingRequest
{
    [Required(ErrorMessage = "wamid is required.")]
    [StringLength(255, MinimumLength = 1, ErrorMessage = "wamid must be between 1 and 255 characters.")]
    [JsonPropertyName("wamid")]
    public string Wamid { get; set; } = string.Empty;

    [Required(ErrorMessage = "recipientId is required.")]
    [StringLength(64, MinimumLength = 1, ErrorMessage = "recipientId must be between 1 and 64 characters.")]
    [JsonPropertyName("recipientId")]
    public string RecipientId { get; set; } = string.Empty;

    [Required(ErrorMessage = "template object is required.")]
    [JsonPropertyName("template")]
    public TemplateInfoRequest? Template { get; set; }

    [Required(ErrorMessage = "sentAt is required.")]
    [JsonPropertyName("sentAt")]
    public DateTimeOffset? SentAt { get; set; }

    [JsonPropertyName("broadcast")]
    public BroadcastInfoRequest? Broadcast { get; set; }
}

public class TemplateInfoRequest
{
    [Required(ErrorMessage = "template.name is required.")]
    [StringLength(128, MinimumLength = 1, ErrorMessage = "template.name must be between 1 and 128 characters.")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(128, ErrorMessage = "template.namespace must not exceed 128 characters.")]
    [JsonPropertyName("namespace")]
    public string? Namespace { get; set; }

    [Required(ErrorMessage = "template.language is required.")]
    [StringLength(32, MinimumLength = 1, ErrorMessage = "template.language must be between 1 and 32 characters.")]
    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;
}

public class BroadcastInfoRequest
{
    [StringLength(128, ErrorMessage = "broadcast.externalId must not exceed 128 characters.")]
    [JsonPropertyName("externalId")]
    public string? ExternalId { get; set; }

    [StringLength(255, ErrorMessage = "broadcast.name must not exceed 255 characters.")]
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public record TemplateMappingDataDto(
    [property: JsonPropertyName("wamid")] string Wamid,
    [property: JsonPropertyName("recipientId")] string RecipientId,
    [property: JsonPropertyName("template")] TemplateInfoDto Template,
    [property: JsonPropertyName("sentAt")] DateTimeOffset SentAt,
    [property: JsonPropertyName("broadcast"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BroadcastInfoDto? Broadcast = null
);

public record TemplateInfoDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("namespace"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Namespace,
    [property: JsonPropertyName("language")] string Language
);

public record BroadcastInfoDto(
    [property: JsonPropertyName("externalId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExternalId,
    [property: JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name
);

public record TemplateMappingResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("created")] bool Created,
    [property: JsonPropertyName("data")] TemplateMappingDataDto Data
);

public record TemplateMappingErrorResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("message")] string Message
);

public enum TemplateMappingStatus
{
    Created,
    ExistingMatch,
    Conflict
}

public record TemplateMappingExecutionResult(
    TemplateMappingStatus Status,
    TemplateMappingDataDto? Data = null,
    string? ErrorMessage = null
);

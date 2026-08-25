namespace OraiWebhookManager.Application.Models;

public record DashboardSummaryDto(
    long TotalMessages,
    long Sent,
    long Delivered,
    long Read,
    long Failed,
    double DeliveredRate,
    double ReadRate,
    double FailedRate,
    long PendingInboxCount,
    long DeadLetterCount
);

public class MessageFilterParams
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Status { get; set; }
    public string? Search { get; set; }
    public DateTimeOffset? DateFrom { get; set; }
    public DateTimeOffset? DateTo { get; set; }
}

public record PagedResult<T>(
    IReadOnlyList<T> Items,
    long TotalCount,
    int Page,
    int PageSize,
    int TotalPages
);

public record MessageListItemDto(
    Guid Id,
    Guid EndpointId,
    string EndpointName,
    string Wamid,
    string? PhoneNumberId,
    string? DisplayPhoneNumber,
    string? RecipientPhone,
    string? CurrentStatus,
    short? StatusRank,
    DateTimeOffset? LastStatusTimestamp,
    string? ConversationId,
    string? ConversationOriginType,
    DateTimeOffset? ConversationExpiresAt,
    string? PricingModel,
    string? PricingCategory,
    bool? PricingBillable,
    string? ActiveErrorCode,
    string? ActiveErrorTitle,
    string? ActiveErrorMessage,
    string? ActiveErrorDetails,
    string? LastFailureCode,
    DateTimeOffset? LastFailureTimestamp,
    string? LastFailureReason,
    string? BizOpaqueCallbackData,
    string? BroadcastId,
    string? BroadcastName,
    string? TemplateName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record MessageStatusEventDto(
    Guid Id,
    Guid MessageId,
    string Wamid,
    string Status,
    DateTimeOffset StatusTimestamp,
    string? ErrorCode,
    string? ErrorTitle,
    string? ErrorMessage,
    string? ErrorDetails,
    string? ErrorData,
    DateTimeOffset CreatedAt
);

public record WebhookEndpointDto(
    Guid Id,
    string Name,
    string KeyPrefix,
    string Status,
    DateTimeOffset? LastReceivedAt,
    DateTimeOffset CreatedAt
);

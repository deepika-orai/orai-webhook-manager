using OraiWebhookManager.Domain.Enums;

namespace OraiWebhookManager.Domain.Entities;

public class WebhookEndpoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public byte[] KeyHash { get; set; } = Array.Empty<byte>();
    public WebhookEndpointStatus Status { get; set; } = WebhookEndpointStatus.Active;
    public DateTimeOffset? LastReceivedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public ICollection<WebhookInboxItem> InboxItems { get; set; } = new List<WebhookInboxItem>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}

public class WebhookInboxItem
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EndpointId { get; set; }
    public string PayloadRaw { get; set; } = "{}";
    public string Headers { get; set; } = "{}";
    public string? IpAddress { get; set; }
    public InboxStatus Status { get; set; } = InboxStatus.Pending;
    public short AttemptCount { get; set; } = 0;
    public string? LastError { get; set; }
    public Guid? LockToken { get; set; }
    public string? LockedBy { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }

    // Navigation properties
    public WebhookEndpoint? Endpoint { get; set; }
}

public class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid EndpointId { get; set; }
    public string Wamid { get; set; } = string.Empty;
    public string? PhoneNumberId { get; set; }
    public string? DisplayPhoneNumber { get; set; }
    public string? RecipientPhone { get; set; }
    public string? CurrentStatus { get; set; }
    public short? StatusRank { get; set; }
    public DateTimeOffset? LastStatusTimestamp { get; set; }
    public string? ConversationId { get; set; }
    public string? ConversationOriginType { get; set; }
    public DateTimeOffset? ConversationExpiresAt { get; set; }
    public string? PricingModel { get; set; }
    public string? PricingCategory { get; set; }
    public bool? PricingBillable { get; set; }

    // Active failure fields (populated only when current_status == "failed")
    public string? ActiveErrorCode { get; set; }
    public string? ActiveErrorTitle { get; set; }
    public string? ActiveErrorMessage { get; set; }
    public string? ActiveErrorDetails { get; set; }
    public string? ActiveErrorData { get; set; }

    // Non-terminal failure tracking
    public string? LastFailureCode { get; set; }
    public DateTimeOffset? LastFailureTimestamp { get; set; }
    public string? LastFailureReason { get; set; }

    // Extensibility fields
    public string? BizOpaqueCallbackData { get; set; }
    public string? BroadcastId { get; set; }
    public string? BroadcastName { get; set; }
    public string? TemplateName { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public WebhookEndpoint? Endpoint { get; set; }
    public ICollection<MessageStatusEvent> StatusEvents { get; set; } = new List<MessageStatusEvent>();
}

public class MessageStatusEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MessageId { get; set; }
    public Guid TenantId { get; set; }
    public string Wamid { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset StatusTimestamp { get; set; }
    public byte[] EventFingerprint { get; set; } = Array.Empty<byte>();
    public string? ErrorCode { get; set; }
    public string? ErrorTitle { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorDetails { get; set; }
    public string? ErrorData { get; set; }
    public string RawEvent { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation property
    public Message? Message { get; set; }
}

public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? IpAddress { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

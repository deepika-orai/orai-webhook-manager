namespace OraiWebhookManager.Domain.Enums;

public enum TenantRole
{
    TenantAdmin,
    Member,
    ReadOnly
}

public enum WebhookEndpointStatus
{
    Active,
    Suspended,
    Revoked
}

public enum InboxStatus : short
{
    Pending = 0,
    Processing = 1,
    Processed = 2,
    Failed = 3,
    DeadLetter = 4
}

public enum MessageStatus
{
    Sent,
    Delivered,
    Read,
    Failed
}

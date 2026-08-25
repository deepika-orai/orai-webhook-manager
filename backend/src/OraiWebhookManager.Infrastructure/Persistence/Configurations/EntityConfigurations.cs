using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OraiWebhookManager.Domain.Entities;

namespace OraiWebhookManager.Infrastructure.Persistence.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
        builder.Property(t => t.Slug).HasColumnName("slug").HasMaxLength(64).IsRequired();
        builder.Property(t => t.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(t => t.Slug).IsUnique().HasDatabaseName("ix_tenants_slug");
    }
}

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("id");
        builder.Property(u => u.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
        builder.Property(u => u.PasswordHash).HasColumnName("password_hash").HasMaxLength(255).IsRequired();
        builder.Property(u => u.FullName).HasColumnName("full_name").HasMaxLength(128).IsRequired();
        builder.Property(u => u.IsPlatformAdmin).HasColumnName("is_platform_admin").IsRequired();
        builder.Property(u => u.EmailConfirmed).HasColumnName("email_confirmed").IsRequired();
        builder.Property(u => u.MfaEnabled).HasColumnName("mfa_enabled").IsRequired();
        builder.Property(u => u.MfaSecret).HasColumnName("mfa_secret").HasMaxLength(128);
        builder.Property(u => u.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(u => u.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(u => u.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(u => u.Email).IsUnique().HasDatabaseName("ix_users_email");
    }
}

public class TenantMembershipConfiguration : IEntityTypeConfiguration<TenantMembership>
{
    public void Configure(EntityTypeBuilder<TenantMembership> builder)
    {
        builder.ToTable("tenant_memberships");

        builder.HasKey(tm => tm.Id);
        builder.Property(tm => tm.Id).HasColumnName("id");
        builder.Property(tm => tm.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(tm => tm.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(tm => tm.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(tm => tm.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(tm => tm.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(tm => tm.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(tm => new { tm.TenantId, tm.UserId }).IsUnique().HasDatabaseName("ix_tenant_memberships_tenant_user");

        builder.HasOne(tm => tm.Tenant)
            .WithMany(t => t.Memberships)
            .HasForeignKey(tm => tm.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(tm => tm.User)
            .WithMany(u => u.Memberships)
            .HasForeignKey(tm => tm.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("user_sessions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(s => s.RefreshTokenHash).HasColumnName("refresh_token_hash").HasColumnType("bytea").IsRequired();
        builder.Property(s => s.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(s => s.RevokedAt).HasColumnName("revoked_at");
        builder.Property(s => s.IpAddress).HasColumnName("ip_address").HasMaxLength(64);
        builder.Property(s => s.UserAgent).HasColumnName("user_agent").HasMaxLength(255);
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(s => s.RefreshTokenHash).IsUnique().HasDatabaseName("ix_user_sessions_refresh_token");

        builder.HasOne(s => s.User)
            .WithMany(u => u.Sessions)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class WebhookEndpointConfiguration : IEntityTypeConfiguration<WebhookEndpoint>
{
    public void Configure(EntityTypeBuilder<WebhookEndpoint> builder)
    {
        builder.ToTable("webhook_endpoints");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
        builder.Property(e => e.KeyPrefix).HasColumnName("key_prefix").HasMaxLength(16).IsRequired();
        builder.Property(e => e.KeyHash).HasColumnName("key_hash").HasColumnType("bytea").IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.LastReceivedAt).HasColumnName("last_received_at");
        builder.Property(e => e.RevokedAt).HasColumnName("revoked_at");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(e => e.KeyHash).IsUnique().HasDatabaseName("ix_webhook_endpoints_key_hash");
        builder.HasIndex(e => new { e.TenantId, e.CreatedAt }).HasDatabaseName("ix_webhook_endpoints_tenant_created");

        builder.HasOne(e => e.Tenant)
            .WithMany(t => t.WebhookEndpoints)
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class WebhookInboxItemConfiguration : IEntityTypeConfiguration<WebhookInboxItem>
{
    public void Configure(EntityTypeBuilder<WebhookInboxItem> builder)
    {
        builder.ToTable("webhook_inbox");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(i => i.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(i => i.EndpointId).HasColumnName("endpoint_id").IsRequired();
        builder.Property(i => i.PayloadRaw).HasColumnName("payload_raw").HasColumnType("jsonb").IsRequired();
        builder.Property(i => i.Headers).HasColumnName("headers").HasColumnType("jsonb").IsRequired();
        builder.Property(i => i.IpAddress).HasColumnName("ip_address").HasMaxLength(64);
        builder.Property(i => i.Status).HasColumnName("status").HasColumnType("smallint").IsRequired();
        builder.Property(i => i.AttemptCount).HasColumnName("attempt_count").HasColumnType("smallint").IsRequired();
        builder.Property(i => i.LastError).HasColumnName("last_error");
        builder.Property(i => i.LockToken).HasColumnName("lock_token");
        builder.Property(i => i.LockedBy).HasColumnName("locked_by").HasMaxLength(64);
        builder.Property(i => i.LockedUntil).HasColumnName("locked_until");
        builder.Property(i => i.NextAttemptAt).HasColumnName("next_attempt_at").IsRequired();
        builder.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(i => i.ProcessedAt).HasColumnName("processed_at");

        // Partial index for worker queue polling
        builder.HasIndex(i => new { i.NextAttemptAt, i.CreatedAt })
            .HasDatabaseName("ix_webhook_inbox_queue")
            .HasFilter("status IN (0, 1)");

        builder.HasIndex(i => new { i.TenantId, i.CreatedAt })
            .HasDatabaseName("ix_webhook_inbox_tenant_created");

        builder.HasOne(i => i.Endpoint)
            .WithMany(e => e.InboxItems)
            .HasForeignKey(i => i.EndpointId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(m => m.EndpointId).HasColumnName("endpoint_id").IsRequired();
        builder.Property(m => m.Wamid).HasColumnName("wamid").HasMaxLength(255).IsRequired();
        builder.Property(m => m.PhoneNumberId).HasColumnName("phone_number_id").HasMaxLength(64);
        builder.Property(m => m.DisplayPhoneNumber).HasColumnName("display_phone_number").HasMaxLength(32);
        builder.Property(m => m.RecipientPhone).HasColumnName("recipient_phone").HasMaxLength(32);
        builder.Property(m => m.CurrentStatus).HasColumnName("current_status").HasMaxLength(32);
        builder.Property(m => m.StatusRank).HasColumnName("status_rank").HasColumnType("smallint");
        builder.Property(m => m.LastStatusTimestamp).HasColumnName("last_status_timestamp");
        builder.Property(m => m.ConversationId).HasColumnName("conversation_id").HasMaxLength(128);
        builder.Property(m => m.ConversationOriginType).HasColumnName("conversation_origin_type").HasMaxLength(64);
        builder.Property(m => m.ConversationExpiresAt).HasColumnName("conversation_expires_at");
        builder.Property(m => m.PricingModel).HasColumnName("pricing_model").HasMaxLength(32);
        builder.Property(m => m.PricingCategory).HasColumnName("pricing_category").HasMaxLength(64);
        builder.Property(m => m.PricingBillable).HasColumnName("pricing_billable");

        builder.Property(m => m.ActiveErrorCode).HasColumnName("active_error_code").HasMaxLength(32);
        builder.Property(m => m.ActiveErrorTitle).HasColumnName("active_error_title").HasMaxLength(255);
        builder.Property(m => m.ActiveErrorMessage).HasColumnName("active_error_message");
        builder.Property(m => m.ActiveErrorDetails).HasColumnName("active_error_details");
        builder.Property(m => m.ActiveErrorData).HasColumnName("active_error_data").HasColumnType("jsonb");

        builder.Property(m => m.LastFailureCode).HasColumnName("last_failure_code").HasMaxLength(32);
        builder.Property(m => m.LastFailureTimestamp).HasColumnName("last_failure_timestamp");
        builder.Property(m => m.LastFailureReason).HasColumnName("last_failure_reason").HasMaxLength(255);

        builder.Property(m => m.BizOpaqueCallbackData).HasColumnName("biz_opaque_callback_data");
        builder.Property(m => m.BroadcastId).HasColumnName("broadcast_id").HasMaxLength(128);
        builder.Property(m => m.BroadcastName).HasColumnName("broadcast_name").HasMaxLength(255);
        builder.Property(m => m.TemplateName).HasColumnName("template_name").HasMaxLength(128);

        builder.Property(m => m.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(m => new { m.TenantId, m.Wamid }).IsUnique().HasDatabaseName("ix_messages_tenant_wamid");
        builder.HasIndex(m => new { m.TenantId, m.CreatedAt }).HasDatabaseName("ix_messages_tenant_created");
        builder.HasIndex(m => new { m.TenantId, m.CurrentStatus, m.CreatedAt }).HasDatabaseName("ix_messages_tenant_status_created");
        builder.HasIndex(m => new { m.TenantId, m.RecipientPhone }).HasDatabaseName("ix_messages_tenant_recipient");

        builder.HasOne(m => m.Tenant)
            .WithMany(t => t.Messages)
            .HasForeignKey(m => m.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Endpoint)
            .WithMany(e => e.Messages)
            .HasForeignKey(m => m.EndpointId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class MessageStatusEventConfiguration : IEntityTypeConfiguration<MessageStatusEvent>
{
    public void Configure(EntityTypeBuilder<MessageStatusEvent> builder)
    {
        builder.ToTable("message_status_events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.MessageId).HasColumnName("message_id").IsRequired();
        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.Wamid).HasColumnName("wamid").HasMaxLength(255).IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(e => e.StatusTimestamp).HasColumnName("status_timestamp").IsRequired();
        builder.Property(e => e.EventFingerprint).HasColumnName("event_fingerprint").HasColumnType("bytea").IsRequired();
        builder.Property(e => e.ErrorCode).HasColumnName("error_code").HasMaxLength(32);
        builder.Property(e => e.ErrorTitle).HasColumnName("error_title").HasMaxLength(255);
        builder.Property(e => e.ErrorMessage).HasColumnName("error_message");
        builder.Property(e => e.ErrorDetails).HasColumnName("error_details");
        builder.Property(e => e.ErrorData).HasColumnName("error_data").HasColumnType("jsonb");
        builder.Property(e => e.RawEvent).HasColumnName("raw_event").HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(e => e.EventFingerprint).IsUnique().HasDatabaseName("ix_status_events_fingerprint");
        builder.HasIndex(e => new { e.MessageId, e.StatusTimestamp }).HasDatabaseName("ix_status_events_message_timestamp");
        builder.HasIndex(e => new { e.TenantId, e.CreatedAt }).HasDatabaseName("ix_status_events_tenant_created");

        builder.HasOne(e => e.Message)
            .WithMany(m => m.StatusEvents)
            .HasForeignKey(e => e.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.TenantId).HasColumnName("tenant_id");
        builder.Property(a => a.UserId).HasColumnName("user_id");
        builder.Property(a => a.Action).HasColumnName("action").HasMaxLength(64).IsRequired();
        builder.Property(a => a.EntityType).HasColumnName("entity_type").HasMaxLength(64).IsRequired();
        builder.Property(a => a.EntityId).HasColumnName("entity_id").HasMaxLength(128).IsRequired();
        builder.Property(a => a.OldValues).HasColumnName("old_values").HasColumnType("jsonb");
        builder.Property(a => a.NewValues).HasColumnName("new_values").HasColumnType("jsonb");
        builder.Property(a => a.IpAddress).HasColumnName("ip_address").HasMaxLength(64);
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(a => new { a.TenantId, a.CreatedAt }).HasDatabaseName("ix_audit_logs_tenant_created");
    }
}

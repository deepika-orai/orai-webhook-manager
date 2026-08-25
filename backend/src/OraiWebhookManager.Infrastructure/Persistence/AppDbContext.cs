using Microsoft.EntityFrameworkCore;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Domain.Entities;
using OraiWebhookManager.Infrastructure.Persistence.Configurations;

namespace OraiWebhookManager.Infrastructure.Persistence;

public interface IAppDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<User> Users { get; }
    DbSet<TenantMembership> TenantMemberships { get; }
    DbSet<UserSession> UserSessions { get; }
    DbSet<WebhookEndpoint> WebhookEndpoints { get; }
    DbSet<WebhookInboxItem> WebhookInboxItems { get; }
    DbSet<Message> Messages { get; }
    DbSet<MessageStatusEvent> MessageStatusEvents { get; }
    DbSet<AuditLog> AuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IPlatformAdminDbContext : IAppDbContext
{
}

public class AppDbContext : DbContext, IAppDbContext
{
    private readonly ICurrentUserContext? _currentUserContext;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ICurrentUserContext? currentUserContext = null)
        : base(options)
    {
        _currentUserContext = currentUserContext;
    }

    protected AppDbContext(
        DbContextOptions options,
        ICurrentUserContext? currentUserContext)
        : base(options)
    {
        _currentUserContext = currentUserContext;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<WebhookEndpoint> WebhookEndpoints => Set<WebhookEndpoint>();
    public DbSet<WebhookInboxItem> WebhookInboxItems => Set<WebhookInboxItem>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<MessageStatusEvent> MessageStatusEvents => Set<MessageStatusEvent>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new TenantConfiguration());
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new TenantMembershipConfiguration());
        modelBuilder.ApplyConfiguration(new UserSessionConfiguration());
        modelBuilder.ApplyConfiguration(new WebhookEndpointConfiguration());
        modelBuilder.ApplyConfiguration(new WebhookInboxItemConfiguration());
        modelBuilder.ApplyConfiguration(new MessageConfiguration());
        modelBuilder.ApplyConfiguration(new MessageStatusEventConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogConfiguration());

        // Multi-tenant Global Query Filters
        if (_currentUserContext != null)
        {
            modelBuilder.Entity<TenantMembership>()
                .HasQueryFilter(tm => !_currentUserContext.TenantId.HasValue || tm.TenantId == _currentUserContext.TenantId.Value);

            modelBuilder.Entity<WebhookEndpoint>()
                .HasQueryFilter(e => !_currentUserContext.TenantId.HasValue || e.TenantId == _currentUserContext.TenantId.Value);

            modelBuilder.Entity<WebhookInboxItem>()
                .HasQueryFilter(i => !_currentUserContext.TenantId.HasValue || i.TenantId == _currentUserContext.TenantId.Value);

            modelBuilder.Entity<Message>()
                .HasQueryFilter(m => !_currentUserContext.TenantId.HasValue || m.TenantId == _currentUserContext.TenantId.Value);

            modelBuilder.Entity<MessageStatusEvent>()
                .HasQueryFilter(e => !_currentUserContext.TenantId.HasValue || e.TenantId == _currentUserContext.TenantId.Value);

            modelBuilder.Entity<AuditLog>()
                .HasQueryFilter(a => !_currentUserContext.TenantId.HasValue || a.TenantId == _currentUserContext.TenantId.Value);
        }
    }
}

public class PlatformAdminDbContext : AppDbContext, IPlatformAdminDbContext
{
    public PlatformAdminDbContext(DbContextOptions<PlatformAdminDbContext> options)
        : base(options, null)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // PlatformAdminDbContext has no query filters
    }
}

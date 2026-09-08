using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Options;
using OraiWebhookManager.Infrastructure.Persistence;
using OraiWebhookManager.Infrastructure.Persistence.Repositories;
using OraiWebhookManager.Infrastructure.Services;
using OraiWebhookManager.Infrastructure.Workers;

namespace OraiWebhookManager.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Database connection string 'ConnectionStrings:DefaultConnection' is missing or empty. " +
                "Please configure 'ConnectionStrings__DefaultConnection' via environment variables or .NET User Secrets.");
        }

        // Configure options
        services.Configure<RetentionOptions>(configuration.GetSection(RetentionOptions.SectionName));
        services.Configure<WebhookIngestionOptions>(configuration.GetSection(WebhookIngestionOptions.SectionName));
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services.AddOptions<GooglePubSubOptions>()
            .Bind(configuration.GetSection(GooglePubSubOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<GooglePubSubOptions>, GooglePubSubOptionsValidator>();

        // Memory Cache
        services.AddMemoryCache();

        // EF Core DbContexts
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
            });
        });

        services.AddDbContext<PlatformAdminDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
            });
        });

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IPlatformAdminDbContext>(sp => sp.GetRequiredService<PlatformAdminDbContext>());

        // Repositories & Services
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddSingleton<IWebhookKeyService, WebhookKeyService>();
        services.AddSingleton<IMetaWebhookParser, MetaWebhookParser>();
        services.AddScoped<IWebhookInboxRepository, WebhookInboxRepository>();
        services.AddScoped<IWebhookProcessorRepository, WebhookProcessorRepository>();
        services.AddScoped<IDashboardRepository, DashboardRepository>();
        services.AddSingleton<ICacheInvalidator, CacheInvalidationService>();

        // Google Cloud Pub/Sub Buffer Services
        var pubSubOptions = configuration.GetSection(GooglePubSubOptions.SectionName).Get<GooglePubSubOptions>() ?? new GooglePubSubOptions();
        if (pubSubOptions.UsePubSubBuffer)
        {
            services.AddSingleton<OraiWebhookManager.Infrastructure.PubSub.IPubSubPublisherClient>(sp =>
            {
                var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GooglePubSubOptions>>().Value;
                var topicName = Google.Cloud.PubSub.V1.TopicName.FromProjectTopic(opts.ProjectId, opts.TopicId);
                var client = Google.Cloud.PubSub.V1.PublisherClient.Create(topicName);
                return new OraiWebhookManager.Infrastructure.PubSub.GooglePubSubPublisherClientAdapter(client);
            });
            services.AddSingleton<IWebhookBufferPublisher, GooglePubSubWebhookPublisher>();
        }
        else
        {
            services.AddSingleton<IWebhookBufferPublisher, NullWebhookBufferPublisher>();
        }

        // Activity Buffer Singleton & Hosted Service
        services.AddSingleton<EndpointActivityBuffer>();
        services.AddSingleton<IEndpointActivityBuffer>(sp => sp.GetRequiredService<EndpointActivityBuffer>());
        services.AddHostedService(sp => sp.GetRequiredService<EndpointActivityBuffer>());

        // Background Worker
        services.AddHostedService<WebhookProcessingWorker>();

        return services;
    }
}

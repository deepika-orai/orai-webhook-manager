using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Infrastructure.Persistence;

namespace OraiWebhookManager.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _defaultDbName = "IntegrationTestDb_" + Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Port=5432;Database=orai_webhooks_test;Username=postgres;Password=test;");

        builder.ConfigureTestServices(services =>
        {
            var descriptors = services.Where(d =>
                d.ServiceType.Name.Contains("DbContext") ||
                d.ServiceType == typeof(DbContextOptions) ||
                (d.ImplementationType != null && d.ImplementationType.Name.Contains("DbContext")) ||
                d.ServiceType == typeof(IAppDbContext) ||
                d.ServiceType == typeof(IPlatformAdminDbContext)
            ).ToList();

            foreach (var d in descriptors)
            {
                services.Remove(d);
            }

            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(_defaultDbName));
            services.AddDbContext<PlatformAdminDbContext>(options =>
                options.UseInMemoryDatabase(_defaultDbName));

            services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
            services.AddScoped<IPlatformAdminDbContext>(sp => sp.GetRequiredService<PlatformAdminDbContext>());
        });
    }
}

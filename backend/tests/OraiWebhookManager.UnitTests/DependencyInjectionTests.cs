using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OraiWebhookManager.Infrastructure;

namespace OraiWebhookManager.UnitTests;

public class DependencyInjectionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddInfrastructure_MissingOrEmptyConnectionString_ThrowsInvalidOperationException(string? connectionString)
    {
        // Arrange
        var services = new ServiceCollection();
        var inMemorySettings = new Dictionary<string, string?>();
        if (connectionString != null)
        {
            inMemorySettings["ConnectionStrings:DefaultConnection"] = connectionString;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        // Act
        var act = () => services.AddInfrastructure(configuration);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Database connection string 'ConnectionStrings:DefaultConnection' is missing or empty*ConnectionStrings__DefaultConnection*");
    }

    [Fact]
    public void AddInfrastructure_ValidConnectionString_RegistersServicesSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5432;Database=orai_webhooks_phase1_test;Username=test;Password=test;"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        // Act
        var act = () => services.AddInfrastructure(configuration);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void AddInfrastructure_WhenUsePubSubBufferIsFalse_RegistersNullWebhookBufferPublisher()
    {
        // Arrange
        var services = new ServiceCollection();
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5432;Database=orai_webhooks_phase1_test;Username=test;Password=test;",
            ["GooglePubSub:UsePubSubBuffer"] = "false",
            ["GooglePubSub:EnableSubscriber"] = "false"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        // Act
        services.AddInfrastructure(configuration);
        var provider = services.BuildServiceProvider();

        // Assert
        var publisher = provider.GetRequiredService<OraiWebhookManager.Application.Interfaces.IWebhookBufferPublisher>();
        publisher.Should().NotBeNull();
        publisher.Should().BeOfType<OraiWebhookManager.Infrastructure.Services.NullWebhookBufferPublisher>();

        var readinessChecker = provider.GetRequiredService<OraiWebhookManager.Infrastructure.Persistence.IPubSubDatabaseReadinessChecker>();
        readinessChecker.Should().NotBeNull();
        readinessChecker.Should().BeOfType<OraiWebhookManager.Infrastructure.Persistence.PubSubDatabaseReadinessChecker>();

        // When EnableSubscriber is false, IPubSubSubscriberClient is NOT registered
        var subscriber = provider.GetService<OraiWebhookManager.Infrastructure.PubSub.IPubSubSubscriberClient>();
        subscriber.Should().BeNull();

        // When EnableSubscriber is false, WebhookPubSubConsumerWorker is NOT registered as hosted service
        var hostedServices = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>();
        hostedServices.Should().NotContain(s => s is OraiWebhookManager.Infrastructure.Workers.WebhookPubSubConsumerWorker);
    }
}

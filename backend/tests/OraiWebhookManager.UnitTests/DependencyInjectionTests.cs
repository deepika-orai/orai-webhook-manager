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
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5432;Database=orai_webhooks;Username=test;Password=test;"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        // Act
        var act = () => services.AddInfrastructure(configuration);

        // Assert
        act.Should().NotThrow();
    }
}

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OraiWebhookManager.Infrastructure.Persistence;
using Xunit;

namespace OraiWebhookManager.UnitTests;

public class PubSubDatabaseReadinessCheckerTests
{
    [Fact]
    public void Constructor_MissingConnectionString_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder().Build();

        var act = () => new PubSubDatabaseReadinessChecker(config, NullLogger<PubSubDatabaseReadinessChecker>.Instance);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DefaultConnection string is not configured*");
    }

    [Fact]
    public async Task CheckReadinessAsync_InvalidHost_ReturnsNotReadyWithErrorMessage()
    {
        var inMemory = new Dictionary<string, string?>
        {
            { "ConnectionStrings:DefaultConnection", "Host=127.0.0.1;Port=59999;Database=dummy;Username=dummy;Password=dummy;Timeout=1;CommandTimeout=1;" }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        var checker = new PubSubDatabaseReadinessChecker(config, NullLogger<PubSubDatabaseReadinessChecker>.Instance);

        var result = await checker.CheckReadinessAsync(CancellationToken.None);

        result.IsReady.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        result.ErrorMessage.Should().Contain("Database connectivity or query error");
    }

    [Fact]
    public async Task EnsureSchemaReadyAsync_WhenNotReady_ThrowsInvalidOperationExceptionWithMigrationGuidance()
    {
        var inMemory = new Dictionary<string, string?>
        {
            { "ConnectionStrings:DefaultConnection", "Host=127.0.0.1;Port=59999;Database=dummy;Username=dummy;Password=dummy;Timeout=1;CommandTimeout=1;" }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        var checker = new PubSubDatabaseReadinessChecker(config, NullLogger<PubSubDatabaseReadinessChecker>.Instance);

        var act = () => checker.EnsureSchemaReadyAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Pub/Sub Consumer database readiness check failed*Phase 1 migration*20260908092727_AddPubSubMessageIdToInbox*");
    }
}

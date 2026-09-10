namespace OraiWebhookManager.Infrastructure.Persistence;

public record DatabaseReadinessResult(bool IsReady, string? ErrorMessage = null);

public interface IPubSubDatabaseReadinessChecker
{
    Task<DatabaseReadinessResult> CheckReadinessAsync(CancellationToken cancellationToken = default);
    Task EnsureSchemaReadyAsync(CancellationToken cancellationToken = default);
}

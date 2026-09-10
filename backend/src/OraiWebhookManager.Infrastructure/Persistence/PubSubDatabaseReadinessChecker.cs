using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace OraiWebhookManager.Infrastructure.Persistence;

public class PubSubDatabaseReadinessChecker : IPubSubDatabaseReadinessChecker
{
    private readonly string _connectionString;
    private readonly ILogger<PubSubDatabaseReadinessChecker> _logger;

    public PubSubDatabaseReadinessChecker(
        IConfiguration configuration,
        ILogger<PubSubDatabaseReadinessChecker> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection string is not configured.");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DatabaseReadinessResult> CheckReadinessAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // Step 1: Check table and column existence + capacity
            const string columnCheckSql = """
                SELECT
                    data_type,
                    character_maximum_length
                FROM information_schema.columns
                WHERE table_schema = current_schema()
                  AND table_name = 'webhook_inbox'
                  AND column_name = 'pubsub_message_id';
                """;

            await using (var columnCmd = new NpgsqlCommand(columnCheckSql, connection))
            await using (var reader = await columnCmd.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    // Check if table itself exists
                    await reader.CloseAsync();

                    const string tableCheckSql = """
                        SELECT EXISTS (
                            SELECT 1 FROM information_schema.tables
                            WHERE table_schema = current_schema() AND table_name = 'webhook_inbox'
                        );
                        """;
                    await using var tableCmd = new NpgsqlCommand(tableCheckSql, connection);
                    var tableExists = (bool?)await tableCmd.ExecuteScalarAsync(cancellationToken) ?? false;

                    if (!tableExists)
                    {
                        return new DatabaseReadinessResult(false, "Table 'webhook_inbox' does not exist in the current database schema.");
                    }

                    return new DatabaseReadinessResult(false, "Column 'pubsub_message_id' does not exist on table 'webhook_inbox'.");
                }

                var dataType = reader.GetString(0);
                var charMaxLength = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1);

                if (string.Equals(dataType, "character varying", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(dataType, "varchar", StringComparison.OrdinalIgnoreCase))
                {
                    if (charMaxLength.HasValue && charMaxLength.Value < 128)
                    {
                        return new DatabaseReadinessResult(
                            false,
                            $"Column 'pubsub_message_id' on 'webhook_inbox' has capacity of {charMaxLength.Value} characters, which is less than the required 128 characters.");
                    }
                }
            }

            // Step 2: Check index existence, validity, readiness, uniqueness, indexed column, and partial predicate
            const string indexCheckSql = """
                SELECT
                    idx.indisvalid,
                    idx.indisready,
                    idx.indisunique,
                    pg_get_expr(idx.indpred, idx.indrelid) AS index_pred,
                    att.attname AS indexed_column
                FROM pg_class t
                JOIN pg_namespace n ON n.oid = t.relnamespace
                JOIN pg_index idx ON idx.indrelid = t.oid
                JOIN pg_class idx_cls ON idx_cls.oid = idx.indexrelid
                JOIN pg_attribute att ON att.attrelid = t.oid AND att.attnum = ANY(idx.indkey)
                WHERE t.relname = 'webhook_inbox'
                  AND n.nspname = current_schema()
                  AND idx_cls.relname = 'ix_webhook_inbox_pubsub_message_id';
                """;

            await using (var indexCmd = new NpgsqlCommand(indexCheckSql, connection))
            await using (var reader = await indexCmd.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    return new DatabaseReadinessResult(
                        false,
                        "Index 'ix_webhook_inbox_pubsub_message_id' does not exist on table 'webhook_inbox'.");
                }

                var isValid = reader.GetBoolean(0);
                var isReady = reader.GetBoolean(1);
                var isUnique = reader.GetBoolean(2);
                var indexPred = reader.IsDBNull(3) ? null : reader.GetString(3);
                var indexedColumn = reader.IsDBNull(4) ? null : reader.GetString(4);

                if (!isValid)
                {
                    return new DatabaseReadinessResult(
                        false,
                        "Index 'ix_webhook_inbox_pubsub_message_id' exists but is invalid (indisvalid = false). The index creation may have failed concurrently.");
                }

                if (!isReady)
                {
                    return new DatabaseReadinessResult(
                        false,
                        "Index 'ix_webhook_inbox_pubsub_message_id' exists but is not ready (indisready = false).");
                }

                if (!isUnique)
                {
                    return new DatabaseReadinessResult(
                        false,
                        "Index 'ix_webhook_inbox_pubsub_message_id' exists but is not a unique index (indisunique = false).");
                }

                if (!string.Equals(indexedColumn, "pubsub_message_id", StringComparison.OrdinalIgnoreCase))
                {
                    return new DatabaseReadinessResult(
                        false,
                        $"Index 'ix_webhook_inbox_pubsub_message_id' indexes column '{indexedColumn}' instead of 'pubsub_message_id'.");
                }

                if (string.IsNullOrWhiteSpace(indexPred) || !indexPred.Contains("pubsub_message_id IS NOT NULL", StringComparison.OrdinalIgnoreCase))
                {
                    return new DatabaseReadinessResult(
                        false,
                        $"Index 'ix_webhook_inbox_pubsub_message_id' lacks the required partial predicate 'pubsub_message_id IS NOT NULL'. Found: '{indexPred}'.");
                }
            }

            return new DatabaseReadinessResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database schema readiness check encountered an unexpected error.");
            return new DatabaseReadinessResult(false, $"Database connectivity or query error: {ex.Message}");
        }
    }

    public async Task EnsureSchemaReadyAsync(CancellationToken cancellationToken = default)
    {
        var result = await CheckReadinessAsync(cancellationToken);
        if (!result.IsReady)
        {
            var diagnosticMessage =
                $"Pub/Sub Consumer database readiness check failed: {result.ErrorMessage} " +
                "Please apply and verify the Phase 1 migration (20260908092727_AddPubSubMessageIdToInbox / 004_add_pubsub_message_id_to_inbox.sql) " +
                "before enabling GooglePubSub:EnableSubscriber.";

            _logger.LogCritical("{DiagnosticMessage}", diagnosticMessage);
            throw new InvalidOperationException(diagnosticMessage);
        }

        _logger.LogInformation("Pub/Sub database schema readiness check succeeded: pubsub_message_id column and unique partial index are valid and ready.");
    }
}

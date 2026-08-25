using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Models;

namespace OraiWebhookManager.Infrastructure.Persistence.Repositories;

public class DashboardRepository : IDashboardRepository
{
    private readonly string _connectionString;

    public DashboardRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection string is not configured.");
    }

    public async Task<bool> ValidateTenantActiveAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT is_active
            FROM tenants
            WHERE id = @TenantId
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var isActive = await connection.QuerySingleOrDefaultAsync<bool?>(
            new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: cancellationToken));

        return isActive == true;
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                COUNT(*) AS TotalMessages,
                COUNT(*) FILTER (WHERE current_status = 'sent') AS Sent,
                COUNT(*) FILTER (WHERE current_status = 'delivered') AS Delivered,
                COUNT(*) FILTER (WHERE current_status = 'read') AS Read,
                COUNT(*) FILTER (WHERE current_status = 'failed') AS Failed
            FROM messages
            WHERE tenant_id = @TenantId;

            SELECT
                COUNT(*) FILTER (WHERE status IN (0, 1)) AS PendingInboxCount,
                COUNT(*) FILTER (WHERE status = 4) AS DeadLetterCount
            FROM webhook_inbox
            WHERE tenant_id = @TenantId;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: cancellationToken));

        var msgStats = await multi.ReadSingleAsync<MessageStatsRow>();
        var inboxStats = await multi.ReadSingleAsync<InboxStatsRow>();

        var total = msgStats.TotalMessages;
        var deliveredRate = total > 0 ? Math.Round((double)(msgStats.Delivered + msgStats.Read) / total * 100.0, 2) : 0.0;
        var readRate = total > 0 ? Math.Round((double)msgStats.Read / total * 100.0, 2) : 0.0;
        var failedRate = total > 0 ? Math.Round((double)msgStats.Failed / total * 100.0, 2) : 0.0;

        return new DashboardSummaryDto(
            TotalMessages: total,
            Sent: msgStats.Sent,
            Delivered: msgStats.Delivered,
            Read: msgStats.Read,
            Failed: msgStats.Failed,
            DeliveredRate: deliveredRate,
            ReadRate: readRate,
            FailedRate: failedRate,
            PendingInboxCount: inboxStats.PendingInboxCount,
            DeadLetterCount: inboxStats.DeadLetterCount
        );
    }

    public async Task<PagedResult<MessageListItemDto>> GetMessagesAsync(
        Guid tenantId,
        MessageFilterParams filter,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize <= 0 ? 20 : filter.PageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var searchPattern = string.IsNullOrWhiteSpace(filter.Search) ? null : $"%{filter.Search.Trim()}%";
        var status = string.IsNullOrWhiteSpace(filter.Status) ? null : filter.Status.Trim();

        const string countSql = """
            SELECT COUNT(*)
            FROM messages m
            WHERE m.tenant_id = @TenantId
              AND (@Status IS NULL OR LOWER(m.current_status) = LOWER(@Status))
              AND (@SearchPattern IS NULL OR m.wamid ILIKE @SearchPattern OR m.recipient_phone ILIKE @SearchPattern)
              AND (@DateFrom IS NULL OR m.created_at >= @DateFrom)
              AND (@DateTo IS NULL OR m.created_at <= @DateTo);
            """;

        const string dataSql = """
            SELECT
                m.id AS Id,
                m.endpoint_id AS EndpointId,
                COALESCE(e.name, 'Unknown') AS EndpointName,
                m.wamid AS Wamid,
                m.phone_number_id AS PhoneNumberId,
                m.display_phone_number AS DisplayPhoneNumber,
                m.recipient_phone AS RecipientPhone,
                m.current_status AS CurrentStatus,
                m.status_rank AS StatusRank,
                m.last_status_timestamp AS LastStatusTimestamp,
                m.conversation_id AS ConversationId,
                m.conversation_origin_type AS ConversationOriginType,
                m.conversation_expires_at AS ConversationExpiresAt,
                m.pricing_model AS PricingModel,
                m.pricing_category AS PricingCategory,
                m.pricing_billable AS PricingBillable,
                m.active_error_code AS ActiveErrorCode,
                m.active_error_title AS ActiveErrorTitle,
                m.active_error_message AS ActiveErrorMessage,
                m.active_error_details AS ActiveErrorDetails,
                m.last_failure_code AS LastFailureCode,
                m.last_failure_timestamp AS LastFailureTimestamp,
                m.last_failure_reason AS LastFailureReason,
                m.biz_opaque_callback_data AS BizOpaqueCallbackData,
                m.broadcast_id AS BroadcastId,
                m.broadcast_name AS BroadcastName,
                m.template_name AS TemplateName,
                m.created_at AS CreatedAt,
                m.updated_at AS UpdatedAt
            FROM messages m
            LEFT JOIN webhook_endpoints e ON m.endpoint_id = e.id AND e.tenant_id = @TenantId
            WHERE m.tenant_id = @TenantId
              AND (@Status IS NULL OR LOWER(m.current_status) = LOWER(@Status))
              AND (@SearchPattern IS NULL OR m.wamid ILIKE @SearchPattern OR m.recipient_phone ILIKE @SearchPattern)
              AND (@DateFrom IS NULL OR m.created_at >= @DateFrom)
              AND (@DateTo IS NULL OR m.created_at <= @DateTo)
            ORDER BY m.created_at DESC
            OFFSET @Offset LIMIT @Limit;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var parameters = new
        {
            TenantId = tenantId,
            Status = status,
            SearchPattern = searchPattern,
            DateFrom = filter.DateFrom,
            DateTo = filter.DateTo,
            Offset = offset,
            Limit = pageSize
        };

        var totalCount = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(countSql, parameters, cancellationToken: cancellationToken));

        var rows = await connection.QueryAsync<MessageListItemRow>(
            new CommandDefinition(dataSql, parameters, cancellationToken: cancellationToken));

        var items = rows.Select(r => r.ToDto()).ToList();
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        return new PagedResult<MessageListItemDto>(
            Items: items,
            TotalCount: totalCount,
            Page: page,
            PageSize: pageSize,
            TotalPages: totalPages
        );
    }

    public async Task<IReadOnlyList<MessageStatusEventDto>> GetMessageEventsAsync(
        Guid tenantId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                e.id AS Id,
                e.message_id AS MessageId,
                e.wamid AS Wamid,
                e.status AS Status,
                e.status_timestamp AS StatusTimestamp,
                e.error_code AS ErrorCode,
                e.error_title AS ErrorTitle,
                e.error_message AS ErrorMessage,
                e.error_details AS ErrorDetails,
                CASE
                    WHEN e.error_data IS NULL OR e.error_data = 'null'::jsonb THEN NULL
                    ELSE e.error_data::text
                END AS ErrorData,
                e.created_at AS CreatedAt
            FROM message_status_events e
            WHERE e.tenant_id = @TenantId
              AND e.message_id = @MessageId
            ORDER BY e.status_timestamp ASC, e.created_at ASC;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<MessageStatusEventRow>(
            new CommandDefinition(sql, new
            {
                TenantId = tenantId,
                MessageId = messageId
            }, cancellationToken: cancellationToken));

        return rows.Select(r => r.ToDto()).ToList();
    }

    public async Task<IReadOnlyList<WebhookEndpointDto>> GetWebhookEndpointsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                id AS Id,
                name AS Name,
                key_prefix AS KeyPrefix,
                status AS Status,
                last_received_at AS LastReceivedAt,
                created_at AS CreatedAt
            FROM webhook_endpoints
            WHERE tenant_id = @TenantId
            ORDER BY created_at DESC;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<WebhookEndpointRow>(
            new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: cancellationToken));

        return rows.Select(r => r.ToDto()).ToList();
    }

    private static DateTimeOffset ToUtcOffset(DateTime dt) =>
        dt.Kind == DateTimeKind.Utc
            ? new DateTimeOffset(dt, TimeSpan.Zero)
            : new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero);

    private static DateTimeOffset? ToUtcOffset(DateTime? dt) =>
        dt.HasValue ? ToUtcOffset(dt.Value) : null;

    private static string? NormalizeJsonString(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var trimmed = json.Trim();
        return string.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase) ? null : trimmed;
    }

    internal sealed class MessageStatsRow
    {
        public long TotalMessages { get; set; }
        public long Sent { get; set; }
        public long Delivered { get; set; }
        public long Read { get; set; }
        public long Failed { get; set; }
    }

    internal sealed class InboxStatsRow
    {
        public long PendingInboxCount { get; set; }
        public long DeadLetterCount { get; set; }
    }

    internal sealed class MessageListItemRow
    {
        public Guid Id { get; set; }
        public Guid EndpointId { get; set; }
        public string? EndpointName { get; set; }
        public string Wamid { get; set; } = string.Empty;
        public string? PhoneNumberId { get; set; }
        public string? DisplayPhoneNumber { get; set; }
        public string? RecipientPhone { get; set; }
        public string? CurrentStatus { get; set; }
        public short? StatusRank { get; set; }
        public DateTime? LastStatusTimestamp { get; set; }
        public string? ConversationId { get; set; }
        public string? ConversationOriginType { get; set; }
        public DateTime? ConversationExpiresAt { get; set; }
        public string? PricingModel { get; set; }
        public string? PricingCategory { get; set; }
        public bool? PricingBillable { get; set; }
        public string? ActiveErrorCode { get; set; }
        public string? ActiveErrorTitle { get; set; }
        public string? ActiveErrorMessage { get; set; }
        public string? ActiveErrorDetails { get; set; }
        public string? LastFailureCode { get; set; }
        public DateTime? LastFailureTimestamp { get; set; }
        public string? LastFailureReason { get; set; }
        public string? BizOpaqueCallbackData { get; set; }
        public string? BroadcastId { get; set; }
        public string? BroadcastName { get; set; }
        public string? TemplateName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public MessageListItemDto ToDto()
        {
            return new MessageListItemDto(
                Id: Id,
                EndpointId: EndpointId,
                EndpointName: EndpointName ?? "Unknown",
                Wamid: Wamid,
                PhoneNumberId: PhoneNumberId,
                DisplayPhoneNumber: DisplayPhoneNumber,
                RecipientPhone: RecipientPhone,
                CurrentStatus: CurrentStatus,
                StatusRank: StatusRank,
                LastStatusTimestamp: ToUtcOffset(LastStatusTimestamp),
                ConversationId: ConversationId,
                ConversationOriginType: ConversationOriginType,
                ConversationExpiresAt: ToUtcOffset(ConversationExpiresAt),
                PricingModel: PricingModel,
                PricingCategory: PricingCategory,
                PricingBillable: PricingBillable,
                ActiveErrorCode: ActiveErrorCode,
                ActiveErrorTitle: ActiveErrorTitle,
                ActiveErrorMessage: ActiveErrorMessage,
                ActiveErrorDetails: ActiveErrorDetails,
                LastFailureCode: LastFailureCode,
                LastFailureTimestamp: ToUtcOffset(LastFailureTimestamp),
                LastFailureReason: LastFailureReason,
                BizOpaqueCallbackData: BizOpaqueCallbackData,
                BroadcastId: BroadcastId,
                BroadcastName: BroadcastName,
                TemplateName: TemplateName,
                CreatedAt: ToUtcOffset(CreatedAt),
                UpdatedAt: ToUtcOffset(UpdatedAt)
            );
        }
    }

    internal sealed class MessageStatusEventRow
    {
        public Guid Id { get; set; }
        public Guid MessageId { get; set; }
        public string Wamid { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime StatusTimestamp { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorTitle { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ErrorDetails { get; set; }
        public string? ErrorData { get; set; }
        public DateTime CreatedAt { get; set; }

        public MessageStatusEventDto ToDto()
        {
            return new MessageStatusEventDto(
                Id: Id,
                MessageId: MessageId,
                Wamid: Wamid,
                Status: Status,
                StatusTimestamp: ToUtcOffset(StatusTimestamp),
                ErrorCode: ErrorCode,
                ErrorTitle: ErrorTitle,
                ErrorMessage: ErrorMessage,
                ErrorDetails: ErrorDetails,
                ErrorData: NormalizeJsonString(ErrorData),
                CreatedAt: ToUtcOffset(CreatedAt)
            );
        }
    }

    internal sealed class WebhookEndpointRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string KeyPrefix { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime? LastReceivedAt { get; set; }
        public DateTime CreatedAt { get; set; }

        public WebhookEndpointDto ToDto()
        {
            return new WebhookEndpointDto(
                Id: Id,
                Name: Name,
                KeyPrefix: KeyPrefix,
                Status: Status,
                LastReceivedAt: ToUtcOffset(LastReceivedAt),
                CreatedAt: ToUtcOffset(CreatedAt)
            );
        }
    }
}

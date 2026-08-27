using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Models;
using Xunit;

namespace OraiWebhookManager.IntegrationTests;

public class DashboardEndpointsIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly FakeDashboardRepository _fakeDashboardRepo = new();

    public DashboardEndpointsIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClientWithFakeDashboard(string? environment = "Development")
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            if (!string.IsNullOrEmpty(environment))
            {
                builder.UseEnvironment(environment);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<IDashboardRepository>(_ => _fakeDashboardRepo);
            });
        }).CreateClient();
    }

    [Fact]
    public async Task GetSummary_WithoutTenantContext_ReturnsUnauthorized()
    {
        var client = CreateClientWithFakeDashboard();

        var response = await client.GetAsync("/api/dashboard/summary");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSummary_WithInactiveTenant_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        _fakeDashboardRepo.SetTenantActive(tenantId, false);

        var client = CreateClientWithFakeDashboard();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard/summary");
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetSummary_WithValidTenant_ReturnsSummaryDto()
    {
        var tenantId = Guid.NewGuid();
        _fakeDashboardRepo.SetTenantActive(tenantId, true);
        _fakeDashboardRepo.SetSummary(tenantId, new DashboardSummaryDto(
            TotalMessages: 150,
            Sent: 20,
            Delivered: 100,
            Read: 80,
            Failed: 10,
            DeliveredRate: 66.67,
            ReadRate: 53.33,
            FailedRate: 6.67,
            PendingInboxCount: 3,
            DeadLetterCount: 1
        ));

        var client = CreateClientWithFakeDashboard();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard/summary");
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await response.Content.ReadFromJsonAsync<DashboardSummaryDto>();
        summary.Should().NotBeNull();
        summary!.TotalMessages.Should().Be(150);
        summary.Delivered.Should().Be(100);
        summary.DeliveredRate.Should().Be(66.67);
        summary.PendingInboxCount.Should().Be(3);
        summary.DeadLetterCount.Should().Be(1);
    }

    [Fact]
    public async Task GetMessages_WithoutTenantContext_ReturnsUnauthorized()
    {
        var client = CreateClientWithFakeDashboard();

        var response = await client.GetAsync("/api/messages");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMessages_WithValidTenant_ReturnsPagedResult()
    {
        var tenantId = Guid.NewGuid();
        _fakeDashboardRepo.SetTenantActive(tenantId, true);

        var sampleMessage = new MessageListItemDto(
            Id: Guid.NewGuid(),
            EndpointId: Guid.NewGuid(),
            EndpointName: "Customer Support Line",
            Wamid: "wamid.HBgL1234567890",
            PhoneNumberId: "10987654321",
            DisplayPhoneNumber: "+15551234567",
            RecipientPhone: "+15559876543",
            CurrentStatus: "delivered",
            StatusRank: 20,
            LastStatusTimestamp: DateTimeOffset.UtcNow,
            ConversationId: "conv_abc123",
            ConversationOriginType: "user_initiated",
            ConversationExpiresAt: DateTimeOffset.UtcNow.AddHours(24),
            PricingModel: "CBP",
            PricingCategory: "service",
            PricingBillable: true,
            ActiveErrorCode: null,
            ActiveErrorTitle: null,
            ActiveErrorMessage: null,
            ActiveErrorDetails: null,
            LastFailureCode: null,
            LastFailureTimestamp: null,
            LastFailureReason: null,
            BizOpaqueCallbackData: "campaign_1",
            BroadcastId: "bc_100",
            BroadcastName: "Summer Promo",
            TemplateName: "summer_discount_v1",
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        );

        _fakeDashboardRepo.SetMessages(tenantId, new PagedResult<MessageListItemDto>(
            Items: new List<MessageListItemDto> { sampleMessage },
            TotalCount: 1,
            Page: 1,
            PageSize: 20,
            TotalPages: 1
        ));

        var client = CreateClientWithFakeDashboard();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/messages?page=1&pageSize=20&status=delivered");
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<MessageListItemDto>>();
        result.Should().NotBeNull();
        result!.TotalCount.Should().Be(1);
        result.Items.Should().HaveCount(1);
        result.Items[0].Wamid.Should().Be("wamid.HBgL1234567890");
        result.Items[0].EndpointName.Should().Be("Customer Support Line");
        result.Items[0].CurrentStatus.Should().Be("delivered");
    }

    [Fact]
    public async Task GetMessageEvents_ReturnsChronologicalEvents()
    {
        var tenantId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        _fakeDashboardRepo.SetTenantActive(tenantId, true);

        var event1 = new MessageStatusEventDto(
            Id: Guid.NewGuid(),
            MessageId: messageId,
            Wamid: "wamid.123",
            Status: "sent",
            StatusTimestamp: DateTimeOffset.UtcNow.AddMinutes(-5),
            ErrorCode: null,
            ErrorTitle: null,
            ErrorMessage: null,
            ErrorDetails: null,
            ErrorData: null,
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-5)
        );

        var event2 = new MessageStatusEventDto(
            Id: Guid.NewGuid(),
            MessageId: messageId,
            Wamid: "wamid.123",
            Status: "delivered",
            StatusTimestamp: DateTimeOffset.UtcNow.AddMinutes(-2),
            ErrorCode: null,
            ErrorTitle: null,
            ErrorMessage: null,
            ErrorDetails: null,
            ErrorData: null,
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-2)
        );

        _fakeDashboardRepo.SetEvents(tenantId, messageId, new List<MessageStatusEventDto> { event1, event2 });

        var client = CreateClientWithFakeDashboard();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/messages/{messageId}/events");
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var events = await response.Content.ReadFromJsonAsync<List<MessageStatusEventDto>>();
        events.Should().NotBeNull();
        events.Should().HaveCount(2);
        events![0].Status.Should().Be("sent");
        events[1].Status.Should().Be("delivered");
    }

    [Fact]
    public async Task GetWebhookEndpoints_NeverExposesKeyHashOrRawKeys()
    {
        var tenantId = Guid.NewGuid();
        _fakeDashboardRepo.SetTenantActive(tenantId, true);

        _fakeDashboardRepo.SetEndpoints(tenantId, new List<WebhookEndpointDto>
        {
            new(
                Id: Guid.NewGuid(),
                Name: "Main Marketing Line",
                KeyPrefix: "whk_live_a1b2",
                Status: "Active",
                LastReceivedAt: DateTimeOffset.UtcNow,
                CreatedAt: DateTimeOffset.UtcNow.AddDays(-10)
            )
        });

        var client = CreateClientWithFakeDashboard();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/webhook-endpoints");
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rawJson = await response.Content.ReadAsStringAsync();

        rawJson.Should().NotContain("keyHash", because: "key_hash must never be returned in API response");
        rawJson.Should().NotContain("key_hash");
        rawJson.Should().Contain("whk_live_a1b2");
        rawJson.Should().Contain("Main Marketing Line");
    }

    [Fact]
    public async Task InProduction_XTenantIdHeaderIsIgnored_ReturnsUnauthorized()
    {
        var tenantId = Guid.NewGuid();
        _fakeDashboardRepo.SetTenantActive(tenantId, true);

        // Run client in Production environment
        var client = CreateClientWithFakeDashboard("Production");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard/summary");
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: "Production environment must ignore X-Tenant-Id header and require real authentication");
    }

    [Fact]
    public async Task GetMessageEvents_WhenMessageDoesNotExistForTenant_Returns404NotFound()
    {
        var tenantId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        _fakeDashboardRepo.SetTenantActive(tenantId, true);

        // Do not register events/message for this tenant
        var client = CreateClientWithFakeDashboard();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/messages/{messageId}/events");
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetMessageEvents_SentDeliveredRead_ReturnsAllThreeEventsInChronologicalOrder()
    {
        var tenantId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        _fakeDashboardRepo.SetTenantActive(tenantId, true);

        var t0 = DateTimeOffset.UtcNow.AddMinutes(-10);
        var t1 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var t2 = DateTimeOffset.UtcNow.AddMinutes(-1);

        var events = new List<MessageStatusEventDto>
        {
            new(Guid.NewGuid(), messageId, "wamid.lifecycle.1", "sent", t0, null, null, null, null, null, t0),
            new(Guid.NewGuid(), messageId, "wamid.lifecycle.1", "delivered", t1, null, null, null, null, null, t1),
            new(Guid.NewGuid(), messageId, "wamid.lifecycle.1", "read", t2, null, null, null, null, null, t2),
        };

        _fakeDashboardRepo.SetEvents(tenantId, messageId, events);

        var client = CreateClientWithFakeDashboard();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/messages/{messageId}/events");
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<List<MessageStatusEventDto>>();
        result.Should().NotBeNull();
        result.Should().HaveCount(3);
        result![0].Status.Should().Be("sent");
        result[1].Status.Should().Be("delivered");
        result[2].Status.Should().Be("read");
    }

    [Fact]
    public async Task GetMessageEvents_SentFailed_ReturnsErrorDetailsInTimeline()
    {
        var tenantId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        _fakeDashboardRepo.SetTenantActive(tenantId, true);

        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var t1 = DateTimeOffset.UtcNow.AddMinutes(-2);

        var events = new List<MessageStatusEventDto>
        {
            new(Guid.NewGuid(), messageId, "wamid.fail.1", "sent", t0, null, null, null, null, null, t0),
            new(Guid.NewGuid(), messageId, "wamid.fail.1", "failed", t1, "131026", "Message Undeliverable", "Recipient is not a valid WhatsApp user.", "User deregistered", "{\"error_subcode\":131026}", t1),
        };

        _fakeDashboardRepo.SetEvents(tenantId, messageId, events);

        var client = CreateClientWithFakeDashboard();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/messages/{messageId}/events");
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<List<MessageStatusEventDto>>();
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result![1].Status.Should().Be("failed");
        result[1].ErrorCode.Should().Be("131026");
        result[1].ErrorTitle.Should().Be("Message Undeliverable");
        result[1].ErrorMessage.Should().Be("Recipient is not a valid WhatsApp user.");
    }

    [Fact]
    public async Task GetMessageEvents_WhenOnlyReadEventStored_ReturnsOnlyReadEventWithoutFabricatingSentOrDelivered()
    {
        var tenantId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        _fakeDashboardRepo.SetTenantActive(tenantId, true);

        var tRead = DateTimeOffset.UtcNow.AddMinutes(-3);

        var events = new List<MessageStatusEventDto>
        {
            new(Guid.NewGuid(), messageId, "wamid.onlyread.1", "read", tRead, null, null, null, null, null, tRead),
        };

        _fakeDashboardRepo.SetEvents(tenantId, messageId, events);

        var client = CreateClientWithFakeDashboard();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/messages/{messageId}/events");
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<List<MessageStatusEventDto>>();
        result.Should().NotBeNull();
        result.Should().HaveCount(1, because: "API must only return stored events and never fabricate synthetic sent or delivered events");
        result![0].Status.Should().Be("read");
    }

    [Fact]
    public async Task ExportStatusLogsCsv_WithDateRangeAndFilters_ReturnsFilteredCsvContentAndHeader()
    {
        var tenantId = Guid.NewGuid();
        _fakeDashboardRepo.SetTenantActive(tenantId, true);

        var now = DateTimeOffset.UtcNow;
        var exportLogs = new List<StatusLogExportRow>
        {
            new("wamid.export.1", "+15551234567", "delivered", now.AddDays(-2), "+15550000000", "conv-1", "utility", "CBP", "", "", now.AddDays(-2)),
            new("wamid.export.2", "+15559876543", "read", now.AddDays(-10), "+15550000000", "conv-2", "marketing", "CBP", "", "", now.AddDays(-10)),
            new("wamid.export.3", "+15551112233", "failed", now.AddDays(-40), "+15550000000", "conv-3", "service", "CBP", "131047", "Rate limit", now.AddDays(-40)),
        };

        _fakeDashboardRepo.SetExportLogs(tenantId, exportLogs);

        var client = CreateClientWithFakeDashboard();

        // Query with Last 7 Days rolling date range: from now-7d to now
        var dateFrom = now.AddDays(-7).ToString("O");
        var dateTo = now.ToString("O");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/messages/export?dateFrom={Uri.EscapeDataString(dateFrom)}&dateTo={Uri.EscapeDataString(dateTo)}&status=delivered");
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response.Content.Headers.ContentDisposition.Should().NotBeNull();
        response.Content.Headers.ContentDisposition!.FileName.Should().Contain($"whatsapp_status_logs_{tenantId:N}_");

        var csvText = await response.Content.ReadAsStringAsync();
        csvText.Should().Contain("wamid.export.1");
        csvText.Should().NotContain("wamid.export.2", because: "date is outside the 7-day range and status is read");
        csvText.Should().NotContain("wamid.export.3", because: "date is outside the 7-day range");
    }

    [Fact]
    public async Task ExportStatusLogsCsv_EnforcesTenantIsolation()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        _fakeDashboardRepo.SetTenantActive(tenantA, true);
        _fakeDashboardRepo.SetTenantActive(tenantB, true);

        var now = DateTimeOffset.UtcNow;
        _fakeDashboardRepo.SetExportLogs(tenantA, new List<StatusLogExportRow>
        {
            new("wamid.tenantA.1", "+15551111111", "delivered", now, "+15550000000", "conv-A", "utility", "CBP", "", "", now),
        });

        _fakeDashboardRepo.SetExportLogs(tenantB, new List<StatusLogExportRow>
        {
            new("wamid.tenantB.1", "+15552222222", "delivered", now, "+15550000000", "conv-B", "utility", "CBP", "", "", now),
        });

        var client = CreateClientWithFakeDashboard();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/messages/export");
        request.Headers.Add("X-Tenant-Id", tenantA.ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var csvText = await response.Content.ReadAsStringAsync();
        csvText.Should().Contain("wamid.tenantA.1");
        csvText.Should().NotContain("wamid.tenantB.1", because: "Tenant A must never see Tenant B's export data");
    }
}

public class FakeDashboardRepository : IDashboardRepository
{
    private readonly Dictionary<Guid, bool> _tenantActiveMap = new();
    private readonly Dictionary<Guid, DashboardSummaryDto> _summaries = new();
    private readonly Dictionary<Guid, PagedResult<MessageListItemDto>> _messages = new();
    private readonly Dictionary<(Guid TenantId, Guid MessageId), IReadOnlyList<MessageStatusEventDto>> _events = new();
    private readonly Dictionary<Guid, IReadOnlyList<WebhookEndpointDto>> _endpoints = new();
    private readonly Dictionary<Guid, List<StatusLogExportRow>> _exportLogs = new();

    public void SetTenantActive(Guid tenantId, bool isActive) => _tenantActiveMap[tenantId] = isActive;
    public void SetSummary(Guid tenantId, DashboardSummaryDto summary) => _summaries[tenantId] = summary;
    public void SetMessages(Guid tenantId, PagedResult<MessageListItemDto> messages) => _messages[tenantId] = messages;
    public void SetEvents(Guid tenantId, Guid messageId, IReadOnlyList<MessageStatusEventDto> events) => _events[(tenantId, messageId)] = events;
    public void SetEndpoints(Guid tenantId, IReadOnlyList<WebhookEndpointDto> endpoints) => _endpoints[tenantId] = endpoints;
    public void SetExportLogs(Guid tenantId, List<StatusLogExportRow> logs) => _exportLogs[tenantId] = logs;

    public Task<bool> ValidateTenantActiveAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        _tenantActiveMap.TryGetValue(tenantId, out var isActive);
        return Task.FromResult(isActive);
    }

    public Task<DashboardSummaryDto> GetSummaryAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        _summaries.TryGetValue(tenantId, out var summary);
        return Task.FromResult(summary ?? new DashboardSummaryDto(0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
    }

    public Task<PagedResult<MessageListItemDto>> GetMessagesAsync(Guid tenantId, MessageFilterParams filter, CancellationToken cancellationToken = default)
    {
        _messages.TryGetValue(tenantId, out var result);
        return Task.FromResult(result ?? new PagedResult<MessageListItemDto>(Array.Empty<MessageListItemDto>(), 0, filter.Page, filter.PageSize, 0));
    }

    public Task<IReadOnlyList<MessageStatusEventDto>?> GetMessageEventsAsync(Guid tenantId, Guid messageId, CancellationToken cancellationToken = default)
    {
        if (_events.TryGetValue((tenantId, messageId), out var events))
        {
            return Task.FromResult<IReadOnlyList<MessageStatusEventDto>?>(events);
        }
        return Task.FromResult<IReadOnlyList<MessageStatusEventDto>?>(null);
    }

    public Task<IReadOnlyList<WebhookEndpointDto>> GetWebhookEndpointsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        _endpoints.TryGetValue(tenantId, out var endpoints);
        return Task.FromResult(endpoints ?? (IReadOnlyList<WebhookEndpointDto>)Array.Empty<WebhookEndpointDto>());
    }

    public Task<IReadOnlyList<StatusLogExportRow>> GetStatusLogsForExportAsync(Guid tenantId, MessageFilterParams filter, CancellationToken cancellationToken = default)
    {
        if (!_exportLogs.TryGetValue(tenantId, out var logs))
        {
            return Task.FromResult<IReadOnlyList<StatusLogExportRow>>(Array.Empty<StatusLogExportRow>());
        }

        var query = logs.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(filter.Status) && !filter.Status.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(r => r.Status.Equals(filter.Status, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim();
            query = query.Where(r => r.MessageId.Contains(s, StringComparison.OrdinalIgnoreCase) || r.RecipientId.Contains(s, StringComparison.OrdinalIgnoreCase));
        }
        if (filter.DateFrom.HasValue)
        {
            query = query.Where(r => r.StatusTimestamp >= filter.DateFrom.Value);
        }
        if (filter.DateTo.HasValue)
        {
            query = query.Where(r => r.StatusTimestamp <= filter.DateTo.Value);
        }

        return Task.FromResult<IReadOnlyList<StatusLogExportRow>>(query.ToList());
    }
}

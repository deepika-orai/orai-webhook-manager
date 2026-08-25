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
}

public class FakeDashboardRepository : IDashboardRepository
{
    private readonly Dictionary<Guid, bool> _tenantActiveMap = new();
    private readonly Dictionary<Guid, DashboardSummaryDto> _summaries = new();
    private readonly Dictionary<Guid, PagedResult<MessageListItemDto>> _messages = new();
    private readonly Dictionary<(Guid TenantId, Guid MessageId), IReadOnlyList<MessageStatusEventDto>> _events = new();
    private readonly Dictionary<Guid, IReadOnlyList<WebhookEndpointDto>> _endpoints = new();

    public void SetTenantActive(Guid tenantId, bool isActive) => _tenantActiveMap[tenantId] = isActive;
    public void SetSummary(Guid tenantId, DashboardSummaryDto summary) => _summaries[tenantId] = summary;
    public void SetMessages(Guid tenantId, PagedResult<MessageListItemDto> messages) => _messages[tenantId] = messages;
    public void SetEvents(Guid tenantId, Guid messageId, IReadOnlyList<MessageStatusEventDto> events) => _events[(tenantId, messageId)] = events;
    public void SetEndpoints(Guid tenantId, IReadOnlyList<WebhookEndpointDto> endpoints) => _endpoints[tenantId] = endpoints;

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

    public Task<IReadOnlyList<MessageStatusEventDto>> GetMessageEventsAsync(Guid tenantId, Guid messageId, CancellationToken cancellationToken = default)
    {
        _events.TryGetValue((tenantId, messageId), out var events);
        return Task.FromResult(events ?? (IReadOnlyList<MessageStatusEventDto>)Array.Empty<MessageStatusEventDto>());
    }

    public Task<IReadOnlyList<WebhookEndpointDto>> GetWebhookEndpointsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        _endpoints.TryGetValue(tenantId, out var endpoints);
        return Task.FromResult(endpoints ?? (IReadOnlyList<WebhookEndpointDto>)Array.Empty<WebhookEndpointDto>());
    }
}

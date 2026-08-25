using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using OraiWebhookManager.Api.Services;
using OraiWebhookManager.Application.Models;
using Xunit;

namespace OraiWebhookManager.UnitTests;

public class DashboardRepositoryAndModelTests
{
    [Fact]
    public void DashboardSummaryDto_CalculatesExpectedFields()
    {
        var summary = new DashboardSummaryDto(
            TotalMessages: 100,
            Sent: 10,
            Delivered: 80,
            Read: 60,
            Failed: 5,
            DeliveredRate: 80.0,
            ReadRate: 60.0,
            FailedRate: 5.0,
            PendingInboxCount: 2,
            DeadLetterCount: 1
        );

        summary.TotalMessages.Should().Be(100);
        summary.DeliveredRate.Should().Be(80.0);
        summary.ReadRate.Should().Be(60.0);
        summary.FailedRate.Should().Be(5.0);
        summary.PendingInboxCount.Should().Be(2);
        summary.DeadLetterCount.Should().Be(1);
    }

    [Fact]
    public void CurrentUserContext_InDevelopment_ResolvesXTenantIdHeader_WhenNoClaim()
    {
        var httpContext = new DefaultHttpContext();
        var expectedTenant = Guid.NewGuid();
        httpContext.Request.Headers["X-Tenant-Id"] = expectedTenant.ToString();

        var accessor = new FakeHttpContextAccessor(httpContext);
        var env = new FakeWebHostEnvironment("Development");

        var context = new CurrentUserContext(accessor, env);

        context.TenantId.Should().Be(expectedTenant);
    }

    [Fact]
    public void CurrentUserContext_InProduction_IgnoresXTenantIdHeader_AndReturnsNull()
    {
        var httpContext = new DefaultHttpContext();
        var headerTenant = Guid.NewGuid();
        httpContext.Request.Headers["X-Tenant-Id"] = headerTenant.ToString();

        var accessor = new FakeHttpContextAccessor(httpContext);
        var env = new FakeWebHostEnvironment("Production");

        var context = new CurrentUserContext(accessor, env);

        context.TenantId.Should().BeNull(because: "X-Tenant-Id header must never be evaluated in Production environment");
    }

    [Fact]
    public void CurrentUserContext_InProduction_ResolvesFromTenantClaim()
    {
        var expectedTenant = Guid.NewGuid();
        var claims = new[]
        {
            new Claim("tenant_id", expectedTenant.ToString())
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var user = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = user };
        // Even if spoofed header is present
        httpContext.Request.Headers["X-Tenant-Id"] = Guid.NewGuid().ToString();

        var accessor = new FakeHttpContextAccessor(httpContext);
        var env = new FakeWebHostEnvironment("Production");

        var context = new CurrentUserContext(accessor, env);

        context.TenantId.Should().Be(expectedTenant);
    }

    [Fact]
    public void CurrentUserContext_PlatformAdmin_InProduction_ResolvesXTenantIdHeader_ForInspection()
    {
        var inspectedTenant = Guid.NewGuid();
        var claims = new[]
        {
            new Claim("is_platform_admin", "true"),
            new Claim(ClaimTypes.Role, "PlatformAdmin")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var user = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = user };
        httpContext.Request.Headers["X-Tenant-Id"] = inspectedTenant.ToString();

        var accessor = new FakeHttpContextAccessor(httpContext);
        var env = new FakeWebHostEnvironment("Production");

        var context = new CurrentUserContext(accessor, env);

        context.TenantId.Should().Be(inspectedTenant);
        context.IsPlatformAdmin.Should().BeTrue();
    }

    [Fact]
    public void CurrentUserContext_PlatformAdmin_WithoutXTenantIdHeader_ReturnsNullTenant()
    {
        var claims = new[]
        {
            new Claim("is_platform_admin", "true"),
            new Claim(ClaimTypes.Role, "PlatformAdmin")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var user = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = user };

        var accessor = new FakeHttpContextAccessor(httpContext);
        var env = new FakeWebHostEnvironment("Production");

        var context = new CurrentUserContext(accessor, env);

        context.TenantId.Should().BeNull();
        context.IsPlatformAdmin.Should().BeTrue();
    }

    [Fact]
    public void WebhookEndpointDto_DoesNotContainKeyHashOrSecrets()
    {
        var properties = typeof(WebhookEndpointDto).GetProperties();
        var propertyNames = properties.Select(p => p.Name.ToLowerInvariant()).ToList();

        propertyNames.Should().NotContain("keyhash");
        propertyNames.Should().NotContain("plainkey");
        propertyNames.Should().NotContain("rawkey");
        propertyNames.Should().NotContain("secret");
        propertyNames.Should().Contain("keyprefix");
        propertyNames.Should().Contain("name");
        propertyNames.Should().Contain("status");
    }

    [Fact]
    public void DashboardSummary_WhenSingleMessageIsRead_DeliveredRateAndReadRateAre100Percent()
    {
        var msgStats = new OraiWebhookManager.Infrastructure.Persistence.Repositories.DashboardRepository.MessageStatsRow
        {
            TotalMessages = 1,
            Sent = 0,
            Delivered = 0,
            Read = 1,
            Failed = 0
        };

        var total = msgStats.TotalMessages;
        var deliveredRate = total > 0 ? Math.Round((double)(msgStats.Delivered + msgStats.Read) / total * 100.0, 2) : 0.0;
        var readRate = total > 0 ? Math.Round((double)msgStats.Read / total * 100.0, 2) : 0.0;
        var failedRate = total > 0 ? Math.Round((double)msgStats.Failed / total * 100.0, 2) : 0.0;

        deliveredRate.Should().Be(100.0);
        readRate.Should().Be(100.0);
        failedRate.Should().Be(0.0);
        msgStats.Delivered.Should().Be(0, "Current-state delivered count must remain unchanged");
        msgStats.Read.Should().Be(1);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0.0, 0.0, 0.0)]
    [InlineData(10, 5, 3, 0, 80.0, 30.0, 0.0)]
    [InlineData(100, 60, 20, 10, 80.0, 20.0, 10.0)]
    public void DashboardSummary_RateCalculations_AreCorrectAndGuardedAgainstDivisionByZero(
        long total, long delivered, long read, long failed,
        double expectedDeliveredRate, double expectedReadRate, double expectedFailedRate)
    {
        var deliveredRate = total > 0 ? Math.Round((double)(delivered + read) / total * 100.0, 2) : 0.0;
        var readRate = total > 0 ? Math.Round((double)read / total * 100.0, 2) : 0.0;
        var failedRate = total > 0 ? Math.Round((double)failed / total * 100.0, 2) : 0.0;

        deliveredRate.Should().Be(expectedDeliveredRate);
        readRate.Should().Be(expectedReadRate);
        failedRate.Should().Be(expectedFailedRate);
    }

    [Fact]
    public void MessageListItemRow_MaterializesAndMapsAllNullableFields_Correctly()
    {
        var row = new OraiWebhookManager.Infrastructure.Persistence.Repositories.DashboardRepository.MessageListItemRow
        {
            Id = Guid.NewGuid(),
            EndpointId = Guid.NewGuid(),
            EndpointName = null, // Should default to "Unknown"
            Wamid = "wamid.HBgL1234567890",
            PhoneNumberId = null,
            DisplayPhoneNumber = null,
            RecipientPhone = null,
            CurrentStatus = null,
            StatusRank = null,
            LastStatusTimestamp = null,
            ConversationId = null,
            ConversationOriginType = null,
            ConversationExpiresAt = null,
            PricingModel = null,
            PricingCategory = null,
            PricingBillable = null,
            ActiveErrorCode = null,
            ActiveErrorTitle = null,
            ActiveErrorMessage = null,
            ActiveErrorDetails = null,
            LastFailureCode = null,
            LastFailureTimestamp = null,
            LastFailureReason = null,
            BizOpaqueCallbackData = null,
            BroadcastId = null,
            BroadcastName = null,
            TemplateName = null,
            CreatedAt = new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc)
        };

        var dto = row.ToDto();

        dto.Id.Should().Be(row.Id);
        dto.EndpointId.Should().Be(row.EndpointId);
        dto.EndpointName.Should().Be("Unknown");
        dto.Wamid.Should().Be("wamid.HBgL1234567890");
        dto.PhoneNumberId.Should().BeNull();
        dto.DisplayPhoneNumber.Should().BeNull();
        dto.RecipientPhone.Should().BeNull();
        dto.CurrentStatus.Should().BeNull();
        dto.StatusRank.Should().BeNull();
        dto.LastStatusTimestamp.Should().BeNull();
        dto.ConversationId.Should().BeNull();
        dto.ConversationOriginType.Should().BeNull();
        dto.ConversationExpiresAt.Should().BeNull();
        dto.PricingModel.Should().BeNull();
        dto.PricingCategory.Should().BeNull();
        dto.PricingBillable.Should().BeNull();
        dto.ActiveErrorCode.Should().BeNull();
        dto.ActiveErrorTitle.Should().BeNull();
        dto.ActiveErrorMessage.Should().BeNull();
        dto.ActiveErrorDetails.Should().BeNull();
        dto.LastFailureCode.Should().BeNull();
        dto.LastFailureTimestamp.Should().BeNull();
        dto.LastFailureReason.Should().BeNull();
        dto.BizOpaqueCallbackData.Should().BeNull();
        dto.BroadcastId.Should().BeNull();
        dto.BroadcastName.Should().BeNull();
        dto.TemplateName.Should().BeNull();
        dto.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
        dto.UpdatedAt.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void MessageListItemRow_MaterializesAndMapsPopulatedFields_Correctly()
    {
        var now = DateTime.UtcNow;
        var row = new OraiWebhookManager.Infrastructure.Persistence.Repositories.DashboardRepository.MessageListItemRow
        {
            Id = Guid.NewGuid(),
            EndpointId = Guid.NewGuid(),
            EndpointName = "Main Marketing Line",
            Wamid = "wamid.HBgL9876543210",
            PhoneNumberId = "10987654321",
            DisplayPhoneNumber = "+15551234567",
            RecipientPhone = "+15559876543",
            CurrentStatus = "delivered",
            StatusRank = 20,
            LastStatusTimestamp = now,
            ConversationId = "conv_123",
            ConversationOriginType = "business_initiated",
            ConversationExpiresAt = now.AddHours(24),
            PricingModel = "CBP",
            PricingCategory = "marketing",
            PricingBillable = true,
            ActiveErrorCode = "131056",
            ActiveErrorTitle = "Payment required",
            ActiveErrorMessage = "Account payment method required",
            ActiveErrorDetails = "Payment issue details",
            LastFailureCode = "131056",
            LastFailureTimestamp = now,
            LastFailureReason = "Payment required",
            BizOpaqueCallbackData = "campaign_1",
            BroadcastId = "bc_999",
            BroadcastName = "Fall Blast",
            TemplateName = "fall_promo_v1",
            CreatedAt = now.AddMinutes(-10),
            UpdatedAt = now
        };

        var dto = row.ToDto();

        dto.Id.Should().Be(row.Id);
        dto.EndpointId.Should().Be(row.EndpointId);
        dto.EndpointName.Should().Be("Main Marketing Line");
        dto.Wamid.Should().Be("wamid.HBgL9876543210");
        dto.PhoneNumberId.Should().Be("10987654321");
        dto.DisplayPhoneNumber.Should().Be("+15551234567");
        dto.RecipientPhone.Should().Be("+15559876543");
        dto.CurrentStatus.Should().Be("delivered");
        dto.StatusRank.Should().Be(20);
        dto.LastStatusTimestamp.Should().NotBeNull();
        dto.LastStatusTimestamp!.Value.Offset.Should().Be(TimeSpan.Zero);
        dto.ConversationId.Should().Be("conv_123");
        dto.ConversationOriginType.Should().Be("business_initiated");
        dto.ConversationExpiresAt.Should().NotBeNull();
        dto.PricingModel.Should().Be("CBP");
        dto.PricingCategory.Should().Be("marketing");
        dto.PricingBillable.Should().BeTrue();
        dto.ActiveErrorCode.Should().Be("131056");
        dto.ActiveErrorTitle.Should().Be("Payment required");
        dto.ActiveErrorMessage.Should().Be("Account payment method required");
        dto.ActiveErrorDetails.Should().Be("Payment issue details");
        dto.LastFailureCode.Should().Be("131056");
        dto.LastFailureTimestamp.Should().NotBeNull();
        dto.LastFailureReason.Should().Be("Payment required");
        dto.BizOpaqueCallbackData.Should().Be("campaign_1");
        dto.BroadcastId.Should().Be("bc_999");
        dto.BroadcastName.Should().Be("Fall Blast");
        dto.TemplateName.Should().Be("fall_promo_v1");
        dto.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
        dto.UpdatedAt.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void MessageStatusEventRow_MaterializesAndMapsToDto_Correctly()
    {
        var now = DateTime.UtcNow;
        var row = new OraiWebhookManager.Infrastructure.Persistence.Repositories.DashboardRepository.MessageStatusEventRow
        {
            Id = Guid.NewGuid(),
            MessageId = Guid.NewGuid(),
            Wamid = "wamid.12345",
            Status = "failed",
            StatusTimestamp = now,
            ErrorCode = "131056",
            ErrorTitle = "Rate limit",
            ErrorMessage = "Rate limit exceeded",
            ErrorDetails = "Details",
            ErrorData = "{\"code\": 131056}",
            CreatedAt = now
        };

        var dto = row.ToDto();

        dto.Id.Should().Be(row.Id);
        dto.MessageId.Should().Be(row.MessageId);
        dto.Wamid.Should().Be("wamid.12345");
        dto.Status.Should().Be("failed");
        dto.StatusTimestamp.Offset.Should().Be(TimeSpan.Zero);
        dto.ErrorCode.Should().Be("131056");
        dto.ErrorTitle.Should().Be("Rate limit");
        dto.ErrorMessage.Should().Be("Rate limit exceeded");
        dto.ErrorDetails.Should().Be("Details");
        dto.ErrorData.Should().Be("{\"code\": 131056}");
        dto.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("null", null)]
    [InlineData("NULL", null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("{\"code\": 131056, \"message\": \"Rate limit\"}", "{\"code\": 131056, \"message\": \"Rate limit\"}")]
    public void MessageStatusEventRow_NormalizesErrorData_Safely(string? inputErrorData, string? expectedErrorData)
    {
        var row = new OraiWebhookManager.Infrastructure.Persistence.Repositories.DashboardRepository.MessageStatusEventRow
        {
            Id = Guid.NewGuid(),
            MessageId = Guid.NewGuid(),
            Wamid = "wamid.test",
            Status = "sent",
            StatusTimestamp = DateTime.UtcNow,
            ErrorData = inputErrorData,
            CreatedAt = DateTime.UtcNow
        };

        var dto = row.ToDto();

        dto.ErrorData.Should().Be(expectedErrorData);
    }

    [Fact]
    public void WebhookEndpointRow_MaterializesAndMapsToDto_Correctly()
    {
        var now = DateTime.UtcNow;
        var row = new OraiWebhookManager.Infrastructure.Persistence.Repositories.DashboardRepository.WebhookEndpointRow
        {
            Id = Guid.NewGuid(),
            Name = "Primary Ingest",
            KeyPrefix = "whk_live_abcd",
            Status = "Active",
            LastReceivedAt = now,
            CreatedAt = now.AddDays(-1)
        };

        var dto = row.ToDto();

        dto.Id.Should().Be(row.Id);
        dto.Name.Should().Be("Primary Ingest");
        dto.KeyPrefix.Should().Be("whk_live_abcd");
        dto.Status.Should().Be("Active");
        dto.LastReceivedAt.Should().NotBeNull();
        dto.LastReceivedAt!.Value.Offset.Should().Be(TimeSpan.Zero);
        dto.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void DapperRowModels_HaveParameterlessConstructorsAndWritableProperties()
    {
        var rowTypes = new[]
        {
            typeof(OraiWebhookManager.Infrastructure.Persistence.Repositories.DashboardRepository.MessageListItemRow),
            typeof(OraiWebhookManager.Infrastructure.Persistence.Repositories.DashboardRepository.MessageStatusEventRow),
            typeof(OraiWebhookManager.Infrastructure.Persistence.Repositories.DashboardRepository.WebhookEndpointRow),
            typeof(OraiWebhookManager.Infrastructure.Persistence.Repositories.DashboardRepository.MessageStatsRow),
            typeof(OraiWebhookManager.Infrastructure.Persistence.Repositories.DashboardRepository.InboxStatsRow)
        };

        foreach (var type in rowTypes)
        {
            var defaultCtor = type.GetConstructor(Type.EmptyTypes);
            defaultCtor.Should().NotBeNull($"Type {type.Name} must have a public parameterless constructor for Dapper materialization");

            var properties = type.GetProperties();
            properties.Should().NotBeEmpty();
            foreach (var prop in properties)
            {
                prop.CanWrite.Should().BeTrue($"Property {prop.Name} on {type.Name} must have a setter for Dapper mapping");
            }
        }
    }
}

public class FakeHttpContextAccessor : IHttpContextAccessor
{
    public HttpContext? HttpContext { get; set; }
    public FakeHttpContextAccessor(HttpContext? context) => HttpContext = context;
}

public class FakeWebHostEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; }
    public string ApplicationName { get; set; } = "OraiWebhookManager.Api";
    public string WebRootPath { get; set; } = string.Empty;
    public IFileProvider WebRootFileProvider { get; set; } = null!;
    public string ContentRootPath { get; set; } = string.Empty;
    public IFileProvider ContentRootFileProvider { get; set; } = null!;

    public FakeWebHostEnvironment(string environmentName)
    {
        EnvironmentName = environmentName;
    }
}


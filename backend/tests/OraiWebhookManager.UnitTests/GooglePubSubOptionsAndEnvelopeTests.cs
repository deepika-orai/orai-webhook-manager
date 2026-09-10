using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OraiWebhookManager.Application.Helpers;
using OraiWebhookManager.Application.Models;
using OraiWebhookManager.Application.Options;
using OraiWebhookManager.Domain.Entities;
using OraiWebhookManager.Infrastructure.Persistence;
using Xunit;

namespace OraiWebhookManager.UnitTests;

public class GooglePubSubOptionsAndEnvelopeTests
{
    [Fact]
    public void GooglePubSubOptions_HasConservativeDefaults()
    {
        var options = new GooglePubSubOptions();

        options.UsePubSubBuffer.Should().BeFalse();
        options.EnableSubscriber.Should().BeFalse();
        options.ProjectId.Should().Be("orai-official");
        options.TopicId.Should().Be("whatsapp-webhook-inbox");
        options.SubscriptionId.Should().Be("whatsapp-webhook-inbox-sub");
        options.PublishTimeoutSeconds.Should().Be(5);
        options.SubscriberClientCount.Should().Be(1);
        options.MaxOutstandingElementCount.Should().Be(100);
        options.MaxOutstandingByteCount.Should().Be(20_971_520); // 20 MB
    }

    [Fact]
    public void Validator_WhenBothFlagsAreFalse_SucceedsEvenIfFieldsAreEmpty()
    {
        var options = new GooglePubSubOptions
        {
            UsePubSubBuffer = false,
            EnableSubscriber = false,
            ProjectId = "",
            TopicId = "",
            SubscriptionId = "",
            PublishTimeoutSeconds = 0,
            SubscriberClientCount = 0,
            MaxOutstandingElementCount = 0,
            MaxOutstandingByteCount = 0
        };

        var validator = new GooglePubSubOptionsValidator();
        var result = validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "topic", 5)]
    [InlineData("proj", "", 5)]
    [InlineData("proj", "topic", 0)]
    [InlineData("proj", "topic", -1)]
    public void Validator_WhenUsePubSubBufferIsTrue_FailsOnInvalidOrMissingPublisherFields(
        string projectId,
        string topicId,
        int timeout)
    {
        var options = new GooglePubSubOptions
        {
            UsePubSubBuffer = true,
            EnableSubscriber = false,
            ProjectId = projectId,
            TopicId = topicId,
            PublishTimeoutSeconds = timeout,
            // Subscriber fields may be blank when subscriber is disabled
            SubscriptionId = "",
            SubscriberClientCount = 0,
            MaxOutstandingElementCount = 0,
            MaxOutstandingByteCount = 0
        };

        var validator = new GooglePubSubOptionsValidator();
        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Validator_WhenUsePubSubBufferIsTrueAndPublisherFieldsValid_SucceedsWithoutSubscriberFields()
    {
        var options = new GooglePubSubOptions
        {
            UsePubSubBuffer = true,
            EnableSubscriber = false,
            ProjectId = "my-gcp-project",
            TopicId = "my-topic",
            PublishTimeoutSeconds = 10,
            SubscriptionId = "",
            SubscriberClientCount = 0,
            MaxOutstandingElementCount = 0,
            MaxOutstandingByteCount = 0
        };

        var validator = new GooglePubSubOptionsValidator();
        var result = validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "sub", 1, 100, 20971520)]
    [InlineData("proj", "", 1, 100, 20971520)]
    [InlineData("proj", "sub", 0, 100, 20971520)]
    [InlineData("proj", "sub", 1, 0, 20971520)]
    [InlineData("proj", "sub", 1, 100, 0)]
    public void Validator_WhenEnableSubscriberIsTrue_FailsOnInvalidOrMissingSubscriberFields(
        string projectId,
        string subId,
        int clientCount,
        int maxElements,
        long maxBytes)
    {
        var options = new GooglePubSubOptions
        {
            UsePubSubBuffer = false,
            EnableSubscriber = true,
            ProjectId = projectId,
            SubscriptionId = subId,
            SubscriberClientCount = clientCount,
            MaxOutstandingElementCount = maxElements,
            MaxOutstandingByteCount = maxBytes,
            // Publisher fields may be blank when publisher is disabled
            TopicId = "",
            PublishTimeoutSeconds = 0
        };

        var validator = new GooglePubSubOptionsValidator();
        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Validator_WhenEnableSubscriberIsTrueAndSubscriberFieldsValid_SucceedsWithoutPublisherFields()
    {
        var options = new GooglePubSubOptions
        {
            UsePubSubBuffer = false,
            EnableSubscriber = true,
            ProjectId = "my-gcp-project",
            SubscriptionId = "my-sub",
            SubscriberClientCount = 2,
            MaxOutstandingElementCount = 50,
            MaxOutstandingByteCount = 10_000_000,
            TopicId = "",
            PublishTimeoutSeconds = 0
        };

        var validator = new GooglePubSubOptionsValidator();
        var result = validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validator_WhenBothFlagsAreTrue_ValidatesBothPublisherAndSubscriberFields()
    {
        var options = new GooglePubSubOptions
        {
            UsePubSubBuffer = true,
            EnableSubscriber = true,
            ProjectId = "my-gcp-project",
            TopicId = "my-topic",
            SubscriptionId = "my-sub",
            PublishTimeoutSeconds = 10,
            SubscriberClientCount = 2,
            MaxOutstandingElementCount = 50,
            MaxOutstandingByteCount = 10_000_000
        };

        var validator = new GooglePubSubOptionsValidator();
        var result = validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void PubSubWebhookEnvelope_SchemaVersion_IsFixedAtOne()
    {
        var envelope = new PubSubWebhookEnvelope();
        envelope.SchemaVersion.Should().Be(1);
    }

    [Fact]
    public void PubSubWebhookEnvelope_SerializesExactAllowedFields()
    {
        var correlationId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();
        var receivedAtUtc = DateTimeOffset.UtcNow;
        var rawPayload = "{\"entry\":[{\"id\":\"123\"}]}";
        var headers = new Dictionary<string, string>
        {
            { "User-Agent", "facebookexternalua" },
            { "Content-Type", "application/json" }
        };

        var envelope = PubSubWebhookEnvelope.Create(
            correlationId: correlationId,
            tenantId: tenantId,
            endpointId: endpointId,
            receivedAtUtc: receivedAtUtc,
            payloadRaw: rawPayload,
            rawHeaders: headers,
            sourceIp: "192.168.1.1",
            contentType: "application/json"
        );

        var json = JsonSerializer.Serialize(envelope);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("schema_version").GetInt32().Should().Be(1);
        root.GetProperty("correlation_id").GetGuid().Should().Be(correlationId);
        root.GetProperty("tenant_id").GetGuid().Should().Be(tenantId);
        root.GetProperty("endpoint_id").GetGuid().Should().Be(endpointId);
        root.GetProperty("received_at_utc").GetDateTimeOffset().Should().BeCloseTo(receivedAtUtc, TimeSpan.FromSeconds(1));
        root.GetProperty("payload_raw").GetString().Should().Be(rawPayload);
        root.GetProperty("filtered_headers").GetProperty("User-Agent").GetString().Should().Be("facebookexternalua");
        root.GetProperty("source_ip").GetString().Should().Be("192.168.1.1");
        root.GetProperty("content_type").GetString().Should().Be("application/json");
    }

    [Fact]
    public void PubSubWebhookEnvelope_DoesNotContainSecretProperties()
    {
        var propertyNames = typeof(PubSubWebhookEnvelope)
            .GetProperties()
            .Select(p => p.Name.ToLowerInvariant())
            .ToList();

        propertyNames.Should().NotContain("key");
        propertyNames.Should().NotContain("rawkey");
        propertyNames.Should().NotContain("plainkey");
        propertyNames.Should().NotContain("keyhash");
        propertyNames.Should().NotContain("password");
        propertyNames.Should().NotContain("secret");
        propertyNames.Should().NotContain("cookie");
        propertyNames.Should().NotContain("authorization");
    }

    [Fact]
    public void WebhookHeaderSanitizer_ExcludesSignaturesAuthorizationCookiesAndSensitiveHeadersFromEnvelope()
    {
        var incomingHeaders = new Dictionary<string, string>
        {
            { "User-Agent", "TestAgent/1.0" },
            { "X-Forwarded-For", "10.0.0.1" },
            { "TraceParent", "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01" },
            { "Content-Type", "application/json" },
            { "X-Hub-Signature-256", "sha256=abcdef1234567890" },
            { "x-hub-signature-256", "sha256=abcdef1234567890" },
            { "X-HUB-SIGNATURE-256", "sha256=abcdef1234567890" },
            { "Authorization", "Bearer secret_token_123" },
            { "Cookie", "session=abc; secret=xyz" },
            { "Set-Cookie", "auth=xyz" },
            { "X-Webhook-Key", "whk_live_supersecretkey" },
            { "X-Api-Key", "api_key_secret" },
            { "Api-Key", "api_key_secret_2" },
            { "X-Custom-Token", "custom_token_val" },
            { "X-Custom-Signature", "sig_val" },
            { "X-Password-Reset", "pwd_val" },
            { "X-Secret-Data", "secret_val" },
            { "Custom-Unrelated-Header", "should_be_ignored" }
        };

        var sanitized = WebhookHeaderSanitizer.SanitizeEnvelopeHeaders(incomingHeaders);

        // Allowed safe operational headers
        sanitized.Should().ContainKey("User-Agent");
        sanitized.Should().ContainKey("X-Forwarded-For");
        sanitized.Should().ContainKey("TraceParent");
        sanitized.Should().ContainKey("Content-Type");
        sanitized.Count.Should().Be(4);

        // Strictly excluded headers
        sanitized.Should().NotContainKey("X-Hub-Signature-256");
        sanitized.Should().NotContainKey("x-hub-signature-256");
        sanitized.Should().NotContainKey("X-HUB-SIGNATURE-256");
        sanitized.Should().NotContainKey("Authorization");
        sanitized.Should().NotContainKey("Cookie");
        sanitized.Should().NotContainKey("Set-Cookie");
        sanitized.Should().NotContainKey("X-Webhook-Key");
        sanitized.Should().NotContainKey("X-Api-Key");
        sanitized.Should().NotContainKey("Api-Key");
        sanitized.Should().NotContainKey("X-Custom-Token");
        sanitized.Should().NotContainKey("X-Custom-Signature");
        sanitized.Should().NotContainKey("X-Password-Reset");
        sanitized.Should().NotContainKey("X-Secret-Data");
        sanitized.Should().NotContainKey("Custom-Unrelated-Header");
    }

    [Theory]
    [InlineData("X-Hub-Signature-256")]
    [InlineData("x-hub-signature-256")]
    [InlineData("X-HUB-SIGNATURE-256")]
    [InlineData("X-Signature")]
    [InlineData("Signature")]
    [InlineData("Webhook-Signature")]
    [InlineData("X-Webhook-Key")]
    [InlineData("Authorization")]
    [InlineData("Cookie")]
    [InlineData("Set-Cookie")]
    [InlineData("X-Api-Key")]
    [InlineData("Api-Key")]
    [InlineData("X-Access-Token")]
    [InlineData("X-Client-Secret")]
    [InlineData("X-User-Password")]
    public void PubSubWebhookEnvelope_Create_StrictlyExcludesSignatureAndSensitiveHeadersFromFilteredHeaders(string sensitiveHeaderName)
    {
        var headers = new Dictionary<string, string>
        {
            { "User-Agent", "WhatsAppBot/1.0" },
            { sensitiveHeaderName, "sensitive_value_12345" }
        };

        var envelope = PubSubWebhookEnvelope.Create(
            correlationId: Guid.NewGuid(),
            tenantId: Guid.NewGuid(),
            endpointId: Guid.NewGuid(),
            receivedAtUtc: DateTimeOffset.UtcNow,
            payloadRaw: "{\"entry\":[]}",
            rawHeaders: headers
        );

        envelope.FilteredHeaders.Should().ContainKey("User-Agent");
        envelope.FilteredHeaders.Should().NotContainKey(sensitiveHeaderName);

        var serializedJson = JsonSerializer.Serialize(envelope);
        serializedJson.Should().NotContain(sensitiveHeaderName);
        serializedJson.Should().NotContain("sensitive_value_12345");
    }

    [Fact]
    public void WebhookHeaderSanitizer_DirectIngestionAllowlistedHeaders_PreservesExistingFiveHeaders()
    {
        var directHeaders = WebhookHeaderSanitizer.DirectIngestionAllowlistedHeaders;

        directHeaders.Should().Contain("User-Agent");
        directHeaders.Should().Contain("X-Hub-Signature-256");
        directHeaders.Should().Contain("X-Forwarded-For");
        directHeaders.Should().Contain("TraceParent");
        directHeaders.Should().Contain("Content-Type");
        directHeaders.Count.Should().Be(5);
    }

    [Fact]
    public void PubSubWebhookEnvelope_PreservesPayloadRawLosslessly_EvenWhenContainingSensitiveKeywords()
    {
        // Webhook JSON payloads may legitimately contain fields named password, token, key, etc.
        const string sensitivePayload = """
        {
          "entry": [
            {
              "id": "12345",
              "changes": [
                {
                  "field": "messages",
                  "value": {
                    "text": { "body": "My password reset token is 123456 and api_key is secret_val" },
                    "key": "sample_key_data",
                    "authorization": "legacy_payload_field"
                  }
                }
              ]
            }
          ]
        }
        """;

        var envelope = PubSubWebhookEnvelope.Create(
            correlationId: Guid.NewGuid(),
            tenantId: Guid.NewGuid(),
            endpointId: Guid.NewGuid(),
            receivedAtUtc: DateTimeOffset.UtcNow,
            payloadRaw: sensitivePayload,
            rawHeaders: new Dictionary<string, string> { { "User-Agent", "WhatsApp/2.0" } }
        );

        envelope.PayloadRaw.Should().Be(sensitivePayload, because: "PayloadRaw must be preserved losslessly without mutation or redaction");
    }

    [Fact]
    public void EfCoreModel_ConfiguresPubSubMessageId_ColumnAndUniquePartialIndex()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=orai_test_metadata;Username=test;Password=test;")
            .Options;

        using var context = new AppDbContext(options);
        var entityType = context.Model.FindEntityType(typeof(WebhookInboxItem));

        entityType.Should().NotBeNull();

        // Check property metadata
        var prop = entityType!.FindProperty(nameof(WebhookInboxItem.PubSubMessageId));
        prop.Should().NotBeNull();
        prop!.GetColumnName().Should().Be("pubsub_message_id");
        prop.GetMaxLength().Should().Be(128);
        prop.IsNullable.Should().BeTrue();

        // Check index metadata
        var index = entityType.GetIndexes()
            .FirstOrDefault(i => i.GetDatabaseName() == "ix_webhook_inbox_pubsub_message_id");

        index.Should().NotBeNull();
        index!.IsUnique.Should().BeTrue();
        index.GetFilter().Should().Be("pubsub_message_id IS NOT NULL");
        index.Properties.Select(p => p.Name).Should().Contain(nameof(WebhookInboxItem.PubSubMessageId));
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000", "e7f7e9a8-3f41-4e17-b70f-111111111111", "e7f7e9a8-3f41-4e17-b70f-222222222222", "{\"key\":\"val\"}")]
    [InlineData("e7f7e9a8-3f41-4e17-b70f-111111111111", "00000000-0000-0000-0000-000000000000", "e7f7e9a8-3f41-4e17-b70f-222222222222", "{\"key\":\"val\"}")]
    [InlineData("e7f7e9a8-3f41-4e17-b70f-111111111111", "e7f7e9a8-3f41-4e17-b70f-222222222222", "00000000-0000-0000-0000-000000000000", "{\"key\":\"val\"}")]
    [InlineData("e7f7e9a8-3f41-4e17-b70f-111111111111", "e7f7e9a8-3f41-4e17-b70f-222222222222", "e7f7e9a8-3f41-4e17-b70f-333333333333", "")]
    [InlineData("e7f7e9a8-3f41-4e17-b70f-111111111111", "e7f7e9a8-3f41-4e17-b70f-222222222222", "e7f7e9a8-3f41-4e17-b70f-333333333333", "   ")]
    public void PubSubWebhookEnvelope_Create_RejectsEmptyGuidsOrPayload(
        string correlationIdStr,
        string tenantIdStr,
        string endpointIdStr,
        string payloadRaw)
    {
        var action = () => PubSubWebhookEnvelope.Create(
            Guid.Parse(correlationIdStr),
            Guid.Parse(tenantIdStr),
            Guid.Parse(endpointIdStr),
            DateTimeOffset.UtcNow,
            payloadRaw,
            new Dictionary<string, string>()
        );

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task WebhookInboxRepository_EnqueueFromPubSubAsync_ValidatesInputArguments()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:DefaultConnection", "Host=localhost;Database=orai_test;Username=test;Password=test;" }
            })
            .Build();

        var repo = new OraiWebhookManager.Infrastructure.Persistence.Repositories.WebhookInboxRepository(config);

        var validEnvelope = PubSubWebhookEnvelope.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            "{\"valid\":\"json\"}",
            new Dictionary<string, string>()
        );

        // 1. Null envelope
        var actNullEnv = () => repo.EnqueueFromPubSubAsync(null!, "msg-123");
        await actNullEnv.Should().ThrowAsync<ArgumentNullException>();

        // 2. Empty pubsubMessageId
        var actEmptyMsg = () => repo.EnqueueFromPubSubAsync(validEnvelope, "");
        await actEmptyMsg.Should().ThrowAsync<ArgumentException>();

        // 3. Excessive length pubsubMessageId > 128
        var longMsgId = new string('x', 129);
        var actLongMsg = () => repo.EnqueueFromPubSubAsync(validEnvelope, longMsgId);
        await actLongMsg.Should().ThrowAsync<ArgumentException>();

        // 4. Invalid JSON payload
        var invalidJsonEnvelope = new PubSubWebhookEnvelope
        {
            CorrelationId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            EndpointId = Guid.NewGuid(),
            ReceivedAtUtc = DateTimeOffset.UtcNow,
            PayloadRaw = "invalid-json-text",
            FilteredHeaders = new Dictionary<string, string>()
        };

        var actInvalidJson = () => repo.EnqueueFromPubSubAsync(invalidJsonEnvelope, "msg-123");
        await actInvalidJson.Should().ThrowAsync<ArgumentException>().WithMessage("*valid JSON*");
    }
}

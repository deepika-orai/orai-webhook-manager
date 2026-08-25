using FluentAssertions;
using OraiWebhookManager.Infrastructure.Services;

namespace OraiWebhookManager.UnitTests;

public class MetaWebhookParserTests
{
    private readonly MetaWebhookParser _parser = new();

    [Fact]
    public void ExtractStatusEvents_MultipleStatusesInSinglePayload_ParsesAllEvents()
    {
        const string payload = """
        {
          "object": "whatsapp_business_account",
          "entry": [
            {
              "id": "WHATSAPP_BUSINESS_ACCOUNT_ID",
              "changes": [
                {
                  "field": "messages",
                  "value": {
                    "messaging_product": "whatsapp",
                    "metadata": {
                      "display_phone_number": "16505553333",
                      "phone_number_id": "123456789012345"
                    },
                    "statuses": [
                      {
                        "id": "wamid.HBgLMTY1MDY5Nzg1MjYVAgASGBgyMjhBRDM2M0JBMzM3QjgyQkY1MEQ0OEIwMzgzOTg0NQA=",
                        "status": "delivered",
                        "timestamp": "1740000000",
                        "recipient_id": "16505551234",
                        "conversation": {
                          "id": "CONVERSATION_ID_1",
                          "expiration_timestamp": "1740086400",
                          "origin": {
                            "type": "utility"
                          }
                        },
                        "pricing": {
                          "billable": true,
                          "pricing_model": "CBP",
                          "category": "utility"
                        },
                        "biz_opaque_callback_data": "campaign_alpha_42"
                      },
                      {
                        "id": "wamid.HBgLMTY1MDY5Nzg1MjYVAgASGBgyMjhBRDM2M0JBMzM3QjgyQkY1MEQ0OEIwMzgzOTg0NQB=",
                        "status": "failed",
                        "timestamp": "1740000010",
                        "recipient_id": "16505555678",
                        "errors": [
                          {
                            "code": 131026,
                            "title": "Message Undeliverable",
                            "message": "Receiver is incapable of receiving this message",
                            "error_data": {
                              "details": "User has blocked business messages"
                            }
                          }
                        ]
                      }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

        var events = _parser.ExtractStatusEvents(payload);

        events.Should().HaveCount(2);

        // Event 1: Delivered
        var evt1 = events[0];
        evt1.Wamid.Should().Be("wamid.HBgLMTY1MDY5Nzg1MjYVAgASGBgyMjhBRDM2M0JBMzM3QjgyQkY1MEQ0OEIwMzgzOTg0NQA=");
        evt1.Status.Should().Be("delivered");
        evt1.PhoneNumberId.Should().Be("123456789012345");
        evt1.DisplayPhoneNumber.Should().Be("16505553333");
        evt1.RecipientPhone.Should().Be("16505551234");
        evt1.ConversationId.Should().Be("CONVERSATION_ID_1");
        evt1.ConversationOriginType.Should().Be("utility");
        evt1.PricingBillable.Should().BeTrue();
        evt1.PricingModel.Should().Be("CBP");
        evt1.PricingCategory.Should().Be("utility");
        evt1.BizOpaqueCallbackData.Should().Be("campaign_alpha_42");

        // Event 2: Failed
        var evt2 = events[1];
        evt2.Wamid.Should().Be("wamid.HBgLMTY1MDY5Nzg1MjYVAgASGBgyMjhBRDM2M0JBMzM3QjgyQkY1MEQ0OEIwMzgzOTg0NQB=");
        evt2.Status.Should().Be("failed");
        evt2.ErrorCode.Should().Be("131026");
        evt2.ErrorTitle.Should().Be("Message Undeliverable");
        evt2.ErrorMessage.Should().Be("Receiver is incapable of receiving this message");
        evt2.ErrorDetails.Should().Be("User has blocked business messages");
    }

    [Fact]
    public void ExtractStatusEvents_MalformedJson_ReturnsEmptyListWithoutException()
    {
        var events = _parser.ExtractStatusEvents("{ invalid json format }");
        events.Should().BeEmpty();
    }
}

public class WebhookKeyServiceTests
{
    private readonly WebhookKeyService _service = new();

    [Fact]
    public void GenerateKey_ShouldReturnValidKeyAndMatchingHash()
    {
        var result = _service.GenerateKey();

        result.PlainKey.Should().StartWith("whk_live_");
        result.KeyPrefix.Should().Be(result.PlainKey[..16]);
        result.KeyHash.Should().HaveCount(32);

        var computed = _service.ComputeKeyHash(result.PlainKey);
        computed.Should().Equal(result.KeyHash);
    }
}

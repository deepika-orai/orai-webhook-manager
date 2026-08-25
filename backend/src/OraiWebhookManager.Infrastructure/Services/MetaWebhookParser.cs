using System.Text.Json;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Models;

namespace OraiWebhookManager.Infrastructure.Services;

public class MetaWebhookParser : IMetaWebhookParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public IReadOnlyList<ExtractedStatusEvent> ExtractStatusEvents(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return Array.Empty<ExtractedStatusEvent>();
        }

        try
        {
            var payload = JsonSerializer.Deserialize<MetaWebhookPayload>(rawJson, JsonOptions);
            if (payload?.Entry == null || payload.Entry.Count == 0)
            {
                return Array.Empty<ExtractedStatusEvent>();
            }

            var results = new List<ExtractedStatusEvent>();

            foreach (var entry in payload.Entry)
            {
                if (entry.Changes == null) continue;

                foreach (var change in entry.Changes)
                {
                    var value = change.Value;
                    if (value == null) continue;

                    var phoneNumberId = value.Metadata?.PhoneNumberId;
                    var displayPhoneNumber = value.Metadata?.DisplayPhoneNumber;

                    if (value.Statuses != null)
                    {
                        foreach (var status in value.Statuses)
                        {
                            if (string.IsNullOrWhiteSpace(status.Id) || string.IsNullOrWhiteSpace(status.Status))
                            {
                                continue;
                            }

                            var statusTimestamp = DateTimeOffset.UtcNow;
                            if (!string.IsNullOrWhiteSpace(status.Timestamp) && long.TryParse(status.Timestamp, out var unixSeconds))
                            {
                                statusTimestamp = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
                            }

                            DateTimeOffset? convExpiresAt = null;
                            if (!string.IsNullOrWhiteSpace(status.Conversation?.ExpirationTimestamp) &&
                                long.TryParse(status.Conversation.ExpirationTimestamp, out var expUnix))
                            {
                                convExpiresAt = DateTimeOffset.FromUnixTimeSeconds(expUnix);
                            }

                            // Extract first error if present
                            string? errCode = null;
                            string? errTitle = null;
                            string? errMsg = null;
                            string? errDetails = null;
                            string? errDataJson = null;

                            if (status.Errors != null && status.Errors.Count > 0)
                            {
                                var firstErr = status.Errors[0];
                                errCode = firstErr.Code?.ToString();
                                errTitle = firstErr.Title;
                                errMsg = firstErr.Message;
                                errDetails = firstErr.ErrorData?.Details;
                                errDataJson = JsonSerializer.Serialize(status.Errors, JsonOptions);
                            }

                            var rawSnippetJson = JsonSerializer.Serialize(status, JsonOptions);

                            results.Add(new ExtractedStatusEvent(
                                Wamid: status.Id.Trim(),
                                Status: status.Status.Trim().ToLowerInvariant(),
                                StatusTimestamp: statusTimestamp,
                                PhoneNumberId: phoneNumberId,
                                DisplayPhoneNumber: displayPhoneNumber,
                                RecipientPhone: status.RecipientId,
                                ConversationId: status.Conversation?.Id,
                                ConversationOriginType: status.Conversation?.Origin?.Type,
                                ConversationExpiresAt: convExpiresAt,
                                PricingModel: status.Pricing?.PricingModel,
                                PricingCategory: status.Pricing?.Category,
                                PricingBillable: status.Pricing?.Billable,
                                ErrorCode: errCode,
                                ErrorTitle: errTitle,
                                ErrorMessage: errMsg,
                                ErrorDetails: errDetails,
                                ErrorDataJson: errDataJson,
                                BizOpaqueCallbackData: status.BizOpaqueCallbackData,
                                RawEventSnippetJson: rawSnippetJson
                            ));
                        }
                    }
                }
            }

            return results;
        }
        catch (JsonException)
        {
            return Array.Empty<ExtractedStatusEvent>();
        }
    }
}

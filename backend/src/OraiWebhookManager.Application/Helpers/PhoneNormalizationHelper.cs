namespace OraiWebhookManager.Application.Helpers;

public static class PhoneNormalizationHelper
{
    public static string NormalizeRecipientId(string rawRecipientId)
    {
        if (string.IsNullOrWhiteSpace(rawRecipientId))
        {
            return string.Empty;
        }

        var trimmed = rawRecipientId.Trim();
        if (trimmed.StartsWith('+'))
        {
            trimmed = trimmed.Substring(1).Trim();
        }

        return trimmed;
    }
}

using System.Security.Cryptography;
using System.Text;
using OraiWebhookManager.Domain.Enums;

namespace OraiWebhookManager.Domain.Rules;

public static class MessageStateEngine
{
    public const short RankSent = 10;
    public const short RankDelivered = 20;
    public const short RankRead = 30;
    public const short RankFailed = 90;

    public static short GetStatusRank(string status) => status.ToLowerInvariant() switch
    {
        "sent" => RankSent,
        "delivered" => RankDelivered,
        "read" => RankRead,
        "failed" => RankFailed,
        _ => 0
    };

    public static byte[] ComputeEventFingerprint(
        Guid tenantId,
        string wamid,
        string status,
        long statusTimestampUnix,
        string? errorCode)
    {
        var rawString = $"{tenantId:N}|{wamid.Trim()}|{status.Trim().ToLowerInvariant()}|{statusTimestampUnix}|{errorCode?.Trim() ?? string.Empty}";
        return SHA256.HashData(Encoding.UTF8.GetBytes(rawString));
    }

    public static bool ShouldApplyStateTransition(
        string? currentStatus,
        short? currentRank,
        DateTimeOffset? currentTimestamp,
        string incomingStatus,
        short incomingRank,
        DateTimeOffset incomingTimestamp)
    {
        var normCurrent = currentStatus?.Trim().ToLowerInvariant();
        var normIncoming = incomingStatus.Trim().ToLowerInvariant();

        // 1. Initial unseen state -> accept any incoming status
        if (string.IsNullOrEmpty(normCurrent) || !currentRank.HasValue || currentRank == 0)
        {
            return true;
        }

        // 2. If current is DELIVERED (20) or READ (30), do NOT downgrade to FAILED or SENT
        if (normCurrent is "delivered" or "read")
        {
            if (normIncoming is "failed" or "sent")
            {
                return false;
            }
            if (normCurrent == "read" && normIncoming == "delivered")
            {
                return false;
            }
            if (normCurrent == normIncoming && incomingTimestamp >= (currentTimestamp ?? DateTimeOffset.MinValue))
            {
                return true;
            }
            if (incomingRank > currentRank.Value)
            {
                return true;
            }
            return false;
        }

        // 3. If current is FAILED
        if (normCurrent == "failed")
        {
            // A failed message cannot become SENT
            if (normIncoming == "sent")
            {
                return false;
            }
            // If DELIVERED or READ arrives with newer evidence (timestamp > failed timestamp), recover state
            if (normIncoming is "delivered" or "read")
            {
                return incomingTimestamp > (currentTimestamp ?? DateTimeOffset.MinValue);
            }
            return false;
        }

        // 4. If current is SENT
        if (normCurrent == "sent")
        {
            if (normIncoming is "delivered" or "read" or "failed")
            {
                return true;
            }
            if (normIncoming == "sent" && incomingTimestamp >= (currentTimestamp ?? DateTimeOffset.MinValue))
            {
                return true;
            }
            return false;
        }

        // Standard rank comparison fallback
        if (incomingRank > currentRank.Value)
        {
            return true;
        }

        if (incomingRank == currentRank.Value && incomingTimestamp >= (currentTimestamp ?? DateTimeOffset.MinValue))
        {
            return true;
        }

        return false;
    }
}

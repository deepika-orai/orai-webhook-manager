using System.Text;
using OraiWebhookManager.Application.Models;

namespace OraiWebhookManager.Application.Helpers;

public static class CsvExportHelper
{
    public const string Header = "Message ID,Recipient ID,Status,Status Timestamp,Display Phone Number or Phone Number ID,Conversation ID,Category,Pricing Model,Error Code,Error Message,Received At";

    public static string EscapeCsvValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var str = value;

        // Formula injection protection: Excel, Google Sheets, Calc treat =, +, -, @, \t, \r at the start as formulas
        if (str.StartsWith('=') || str.StartsWith('+') || str.StartsWith('-') || str.StartsWith('@') || str.StartsWith('\t') || str.StartsWith('\r'))
        {
            str = "'" + str;
        }

        // If string contains comma, double quote, newline, carriage return, or quote character, wrap in double quotes and escape quotes
        if (str.Contains(',') || str.Contains('"') || str.Contains('\n') || str.Contains('\r') || str.Contains('\''))
        {
            str = "\"" + str.Replace("\"", "\"\"") + "\"";
        }

        return str;
    }

    public static string FormatRow(StatusLogExportRow row)
    {
        var sb = new StringBuilder();
        sb.Append(EscapeCsvValue(row.MessageId)).Append(',');
        sb.Append(EscapeCsvValue(row.RecipientId)).Append(',');
        sb.Append(EscapeCsvValue(row.Status)).Append(',');
        sb.Append(EscapeCsvValue(row.StatusTimestamp.ToString("yyyy-MM-ddTHH:mm:ssZ"))).Append(',');
        sb.Append(EscapeCsvValue(row.DisplayPhoneNumberOrId)).Append(',');
        sb.Append(EscapeCsvValue(row.ConversationId)).Append(',');
        sb.Append(EscapeCsvValue(row.Category)).Append(',');
        sb.Append(EscapeCsvValue(row.PricingModel)).Append(',');
        sb.Append(EscapeCsvValue(row.ErrorCode)).Append(',');
        sb.Append(EscapeCsvValue(row.ErrorMessage)).Append(',');
        sb.Append(EscapeCsvValue(row.ReceivedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")));
        return sb.ToString();
    }

    public static byte[] GenerateStatusLogsCsvBytes(IEnumerable<StatusLogExportRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Header);
        foreach (var row in rows)
        {
            sb.AppendLine(FormatRow(row));
        }

        var utf8WithBom = new UTF8Encoding(true);
        return utf8WithBom.GetBytes(sb.ToString());
    }
}

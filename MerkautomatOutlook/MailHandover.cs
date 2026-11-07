using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace MerkautomatOutlook;

public sealed record MailData(string Subject, string Sender, DateTime Received, string Body, string EntryId, string? StoreId);

/// <summary>Writes one mail as a small JSON file; written to a temporary name first so Merkautomat never sees half a file.</summary>
public static class MailHandover
{
    public const int MaxMailsPerClick = 20;
    public const int MaxBody = 20_000;

    public static string Write(string directory, MailData mail, DateTime now)
    {
        Directory.CreateDirectory(directory);
        var name = $"mail-{now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var temp = Path.Combine(directory, name + ".tmp");
        var target = Path.Combine(directory, name + ".json");
        File.WriteAllText(temp, ToJson(mail), new UTF8Encoding(false));
        File.Move(temp, target);
        return target;
    }

    public static string ToJson(MailData mail)
    {
        var body = mail.Body.Length > MaxBody ? mail.Body.Substring(0, MaxBody) : mail.Body;
        var sb = new StringBuilder("{");
        Field(sb, "kind", "outlook-mail").Append(',');
        Field(sb, "subject", mail.Subject).Append(',');
        Field(sb, "sender", mail.Sender).Append(',');
        Field(sb, "received", mail.Received.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)).Append(',');
        Field(sb, "entryId", mail.EntryId).Append(',');
        Field(sb, "storeId", mail.StoreId ?? "").Append(',');
        Field(sb, "body", body);
        return sb.Append('}').ToString();
    }

    private static StringBuilder Field(StringBuilder sb, string name, string value)
    {
        sb.Append('"').Append(name).Append("\":\"");
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"');
    }
}

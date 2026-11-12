using System;
using System.Globalization;
using System.IO;
using System.Text;
using Merkautomat.Services;

namespace MerkautomatOutlook;

public sealed record MailData(string Subject, string Sender, DateTime Received, string Body, string EntryId, string? StoreId);

/// <summary>Writes one mail as a small JSON file; written to a temporary name first so Merkautomat never sees half a file.</summary>
public static class MailHandover
{
    public const int MaxMailsPerClick = 20;
    public const int MaxBody = 20_000;

    public static string ResolveInboxDirectory(string? dataDirectory, string? applicationPath)
    {
        if (!string.IsNullOrWhiteSpace(dataDirectory) && Path.IsPathRooted(dataDirectory))
            return Path.Combine(Path.GetFullPath(dataDirectory), "inbox");
        if (!string.IsNullOrWhiteSpace(applicationPath) && Path.IsPathRooted(applicationPath)
            && string.Equals(Path.GetFileName(applicationPath), "Merkautomat.exe", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(ToolDataPaths.ResolveDataDir(Path.GetDirectoryName(applicationPath)!), "inbox");
        throw new InvalidOperationException("Der Datenordner von Merkautomat ist nicht bekannt. Bitte das Outlook-Add-in in der Kontrollstation erneut einrichten.");
    }

    public static string Write(string directory, MailData mail, DateTime now)
    {
        Directory.CreateDirectory(directory);
        var name = $"mail-{now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var temp = Path.Combine(directory, name + ".tmp");
        var target = Path.Combine(directory, name + ".json");
        File.WriteAllBytes(temp, Protect(ToJson(mail)));
        File.Move(temp, target);
        return target;
    }

    /// <summary>Marker of a handover file protected for the current Windows user (DPAPI); Merkautomat checks it.</summary>
    public static readonly byte[] ProtectedMagic = Encoding.ASCII.GetBytes("MKD1");
    public static readonly byte[] Entropy = Encoding.ASCII.GetBytes("Merkautomat.Inbox.v1");

    /// <summary>The mail never lies in the inbox folder as readable text, not even for the moment until it is picked up.</summary>
    public static byte[] Protect(string json)
    {
        var data = System.Security.Cryptography.ProtectedData.Protect(new UTF8Encoding(false).GetBytes(json), Entropy,
            System.Security.Cryptography.DataProtectionScope.CurrentUser);
        var result = new byte[ProtectedMagic.Length + data.Length];
        Buffer.BlockCopy(ProtectedMagic, 0, result, 0, ProtectedMagic.Length);
        Buffer.BlockCopy(data, 0, result, ProtectedMagic.Length, data.Length);
        return result;
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

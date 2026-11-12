using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Merkautomat.Services;

public sealed record InboxMail(string Subject, string Sender, DateTime Received, string Body, string EntryId);

/// <summary>
/// Mails handed over by the Outlook add-in as JSON files in data\inbox. Each file becomes one task;
/// unreadable files are moved to inbox\fehler instead of being retried forever.
/// </summary>
public static partial class MailInbox
{
    public const long MaxFileBytes = 512 * 1024;
    public const string ErrorFolder = "fehler";

    public static IReadOnlyList<string> PendingFiles(string directory)
        => Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList()
            : [];

    private static readonly byte[] ProtectedMagic = "MKD1"u8.ToArray();
    private static readonly byte[] Entropy = "Merkautomat.Inbox.v1"u8.ToArray();

    /// <summary>Text of a handover file: protected for the current user (DPAPI), or plain JSON from older add-ins.</summary>
    public static string Decode(byte[] data)
    {
        if (!data.AsSpan().StartsWith(ProtectedMagic)) return SecureFile.DecodeText(data);
        try
        {
            var plain = System.Security.Cryptography.ProtectedData.Unprotect(data[ProtectedMagic.Length..], Entropy,
                System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return new UTF8Encoding(false).GetString(plain);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            throw new InvalidDataException("Die Übergabedatei ist für ein anderes Windows-Konto geschützt.", ex);
        }
    }

    public static InboxMail Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (Text(root, "kind") != "outlook-mail") throw new InvalidDataException("Unbekannte Übergabe.");
            var received = DateTime.TryParseExact(Text(root, "received"), "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var r) ? r : DateTime.MinValue;
            return new InboxMail(Text(root, "subject").Trim(), Text(root, "sender").Trim(), received, Text(root, "body"),
                Text(root, "entryId").Trim());
        }
        catch (JsonException ex) { throw new InvalidDataException("Die Übergabedatei ist kein gültiges JSON.", ex); }
    }

    public static KbTask ToTask(InboxMail mail, DateTime now)
    {
        var subject = SubjectWithoutPrefixes(mail.Subject);
        var header = new StringBuilder();
        if (mail.Sender.Length > 0) header.Append("Von: ").Append(mail.Sender).Append('\n');
        if (mail.Received != DateTime.MinValue)
            header.Append("Empfangen: ").Append(mail.Received.ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("de-DE"))).Append('\n');
        header.Append("Betreff: ").Append(mail.Subject).Append("\n\n");
        var text = header + CleanBody(mail.Body);
        return new KbTask
        {
            Title = TaskStore.TitleFrom(subject.Length > 0 ? subject : "Mail ohne Betreff"),
            Created = now,
            SourceKey = "mail:" + (mail.EntryId.Length > 0 ? mail.EntryId : Guid.NewGuid().ToString("N")),
            SourceTitle = (mail.Sender.Length > 0 ? $"Mail von {mail.Sender}: " : "Mail: ") + (mail.Subject.Length > 0 ? mail.Subject : "(ohne Betreff)"),
            SourceText = text.Length <= TaskStore.MaxSourceText ? text : text[..TaskStore.MaxSourceText] + "…",
        };
    }

    /// <summary>
    /// Plain mail text without the invisible filler characters newsletters put into their preview line, without
    /// indentation and trailing spaces, and with at most one blank line in a row.
    /// </summary>
    public static string CleanBody(string body)
    {
        var text = Invisible().Replace(body, "").Replace(' ', ' ').Replace("\r\n", "\n").Replace('\r', '\n');
        text = string.Join("\n", text.Split('\n').Select(line => line.Trim()));
        return BlankLines().Replace(text, "\n\n").Trim();
    }

    /// <summary>"AW: WG: RE: Angebot" becomes "Angebot".</summary>
    public static string SubjectWithoutPrefixes(string subject) => ReplyPrefix().Replace(subject.Trim(), "").Trim();

    private static string Text(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    [GeneratedRegex(@"^((AW|WG|RE|FW|FWD|Antw|Fwd)\s*(\[\d+\])?\s*:\s*)+", RegexOptions.IgnoreCase)]
    private static partial Regex ReplyPrefix();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();

    // Soft hyphen, combining grapheme joiner, zero-width and bidi marks, word joiners, BOM and Hangul fillers.
    [GeneratedRegex(@"[­͏؜ᅟᅠ឴឵᠎​-‏‪-‮⁠-⁤⁪-⁯ㅤ﻿ﾠ]")]
    private static partial Regex Invisible();
}

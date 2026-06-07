using System.Text.RegularExpressions;
using ClipwellWin.Models;

namespace ClipwellWin.Services;

public static class ContentKindService
{
    private static readonly Regex TomlSectionRx    = new(@"^\s*\[[\w.-]+\]\s*$",                                                               RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex TomlKeyValueRx   = new(@"^\s*[\w.-]+\s*=\s*.+$",                                                            RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex XmlDeclRx        = new(@"^\s*<\?xml\b",                                                                      RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex XmlOpenTagRx     = new(@"^\s*<[A-Za-z][\w:.-]*(\s|>|/>)",                                                   RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex XmlCloseTagRx    = new(@"</[A-Za-z][\w:.-]*>\s*$",                                                          RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex HtmlDoctypeRx    = new(@"^\s*<!doctype\s+html\b",                                                            RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HtmlRootRx       = new(@"^\s*<html\b",                                                                       RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex HtmlTagRx        = new(@"</?(head|body|main|section|article|header|footer|div|span|button|input|script|style|link|meta|title)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex IniSectionRx     = new(@"^\s*\[[^\]]+\]\s*$",                                                               RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex IniKeyValueRx    = new(@"^\s*[\w.-]+\s*=\s*[^=]+$",                                                        RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex EnvLineRx        = new(@"^\s*[A-Z_][A-Z0-9_]*=.+$",                                                        RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex DockerfileRx     = new(@"^\s*(FROM|RUN|COPY|ADD|CMD|ENTRYPOINT|WORKDIR|ENV|ARG)\b",                         RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex ConfigXmlRx      = new(@"^\s*<configuration\b",                                                             RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex KeyValueLineRx   = new(@"^([A-Za-z_][\w.-]{1,40})\s*([:=])\s*(\S.*?)\s*$",                                 RegexOptions.Compiled);

    public static string? DetectTextKind(string text, string? language)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();

        if (language == "PowerShell") return "PS1";
        if (language == "Bash") return "SH";
        if (language is "Python" or "JavaScript" or "TypeScript" or "PHP" or "Ruby") return "SCRIPT";
        if (language == "JSON") return "JSON";
        if (language == "HTML") return LooksLikeHtml(t) ? "HTML" : LooksLikeXml(t) ? "XML" : "HTML";
        if (language == "XML") return "XML";
        if (language == "YAML") return "YAML";
        if (language == "SQL") return "SQL";

        if (LooksLikeXml(t)) return "XML";
        if (LooksLikeEnv(t)) return "ENV";
        if (LooksLikeToml(t)) return "TOML";
        if (LooksLikeIni(t)) return "INI";
        if (LooksLikeProperties(t)) return "PROPS";
        if (LooksLikeDockerfile(t)) return "DOCKER";
        if (LooksLikeConfig(t)) return "CONFIG";

        return language;
    }

    public static string? DetectImageKind(byte[]? data)
    {
        if (data is not { Length: >= 4 }) return "IMG";

        if (StartsWith(data, [0x89, 0x50, 0x4E, 0x47])) return "PNG";
        if (StartsWith(data, [0xFF, 0xD8, 0xFF])) return "JPG";
        if (StartsWith(data, [0x47, 0x49, 0x46, 0x38])) return "GIF";
        if (StartsWith(data, [0x42, 0x4D])) return "BMP";
        if (StartsWith(data, [0x49, 0x49, 0x2A, 0x00]) || StartsWith(data, [0x4D, 0x4D, 0x00, 0x2A])) return "TIFF";
        if (data.Length >= 12
            && data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46
            && data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50) return "WEBP";

        return "IMG";
    }

    public static string? DetectUrlKind(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? uri.Host[4..]
            : uri.Host;
    }

    public static string PrimaryBadge(ClipboardEntry entry) => entry.Type switch
    {
        EntryType.Url   => "URL",
        EntryType.Image => "IMG",
        EntryType.Code  => "CODE",
        EntryType.Color => "COLOR",
        _ => entry.ContentKind ?? "TEXT",
    };

    private static bool StartsWith(byte[] data, byte[] prefix)
    {
        if (data.Length < prefix.Length) return false;
        for (var i = 0; i < prefix.Length; i++)
            if (data[i] != prefix[i]) return false;
        return true;
    }

    private static bool LooksLikeToml(string text)
        => TomlSectionRx.IsMatch(text) && TomlKeyValueRx.IsMatch(text);

    private static bool LooksLikeXml(string text)
        => XmlDeclRx.IsMatch(text)
           || XmlOpenTagRx.IsMatch(text) && XmlCloseTagRx.IsMatch(text);

    private static bool LooksLikeHtml(string text)
        => HtmlDoctypeRx.IsMatch(text)
           || HtmlRootRx.IsMatch(text)
           || HtmlTagRx.IsMatch(text);

    private static bool LooksLikeIni(string text)
        => IniSectionRx.IsMatch(text) && IniKeyValueRx.IsMatch(text);

    private static bool LooksLikeProperties(string text)
    {
        var stats = AnalyzeKeyValueLines(text);
        if (stats.KeyValueLines < 2 || !HasConfigLineRatio(stats)) return false;

        return stats.EqualsLines >= 2
               || stats.DottedKeys >= 1
               || stats.DashedKeys >= 1
               || stats.UnderscoredKeys >= 1;
    }

    private static bool LooksLikeEnv(string text)
        => EnvLineRx.Matches(text).Count >= 2;

    private static bool LooksLikeDockerfile(string text)
        => DockerfileRx.IsMatch(text);

    private static bool LooksLikeConfig(string text)
    {
        if (ConfigXmlRx.IsMatch(text))
            return true;

        var stats = AnalyzeKeyValueLines(text);
        return stats.KeyValueLines >= 3
               && HasConfigLineRatio(stats)
               && (stats.EqualsLines >= 1 || stats.KnownConfigKeys >= 2);
    }

    private static bool HasConfigLineRatio(KeyValueStats stats)
        => stats.NonEmptyLines > 0 && stats.KeyValueLines >= Math.Max(2, (int)Math.Ceiling(stats.NonEmptyLines * 0.7));

    private static KeyValueStats AnalyzeKeyValueLines(string text)
    {
        var nonEmptyLines = 0;
        var keyValueLines = 0;
        var equalsLines = 0;
        var dottedKeys = 0;
        var dashedKeys = 0;
        var underscoredKeys = 0;
        var knownConfigKeys = 0;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            nonEmptyLines++;

            var match = KeyValueLineRx.Match(line);
            if (!match.Success) continue;

            var key = match.Groups[1].Value;
            var separator = match.Groups[2].Value;
            var value = match.Groups[3].Value;
            if (value.EndsWith(".", StringComparison.Ordinal) && separator == ":")
                continue;

            keyValueLines++;
            if (separator == "=") equalsLines++;
            if (key.Contains('.')) dottedKeys++;
            if (key.Contains('-')) dashedKeys++;
            if (key.Contains('_')) underscoredKeys++;
            if (IsKnownConfigKey(key)) knownConfigKeys++;
        }

        return new KeyValueStats(
            nonEmptyLines,
            keyValueLines,
            equalsLines,
            dottedKeys,
            dashedKeys,
            underscoredKeys,
            knownConfigKeys);
    }

    private static bool IsKnownConfigKey(string key)
    {
        var k = key.ToLowerInvariant();
        return k is "host" or "port" or "server" or "url" or "path" or "name"
            or "enabled" or "timeout" or "mode" or "level" or "user" or "username"
            or "database" or "cache" or "region" or "environment";
    }

    private readonly record struct KeyValueStats(
        int NonEmptyLines,
        int KeyValueLines,
        int EqualsLines,
        int DottedKeys,
        int DashedKeys,
        int UnderscoredKeys,
        int KnownConfigKeys);
}

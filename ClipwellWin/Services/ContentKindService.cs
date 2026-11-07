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
    private static readonly Regex IniSectionRx     = new(@"^\s*\[[^\]]+\]\s*$",                                                               RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex IniKeyValueRx    = new(@"^\s*[\w.-]+\s*=\s*[^=]+$",                                                        RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex EnvLineRx        = new(@"^\s*[A-Z_][A-Z0-9_]*=.+$",                                                        RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex DockerfileRx     = new(@"^\s*(FROM|RUN|COPY|ADD|CMD|ENTRYPOINT|WORKDIR|ENV|ARG)\b",                         RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex ConfigXmlRx      = new(@"^\s*<configuration\b",                                                             RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex KeyValueLineRx   = new(@"^([A-Za-z_][\w.-]{1,40})\s*([:=])\s*(\S.*?)\s*$",                                 RegexOptions.Compiled);
    private static readonly Regex MarkdownHeaderRx  = new(@"^#{1,6}\s+\S",                                                                    RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex MarkdownListRx    = new(@"^(\s*[-*+]|\s*\d+\.)\s+\S",                                                      RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex MarkdownFenceRx   = new(@"^```",                                                                            RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex MarkdownLinkRx    = new(@"\[.+?\]\(.+?\)",                                                                  RegexOptions.Compiled);
    private static readonly Regex MarkdownEmphRx    = new(@"(\*\*|__|_|\*).+?\1",                                                             RegexOptions.Compiled);
    private static readonly Regex YamlDocSepRx      = new(@"^---\s*$",                                                                        RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex YamlMappingRx     = new(@"^[ \t]*[a-zA-Z_][\w.-]*\s*:\s*\S",                                               RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex YamlListItemRx    = new(@"^[ \t]*-[ \t]+\S",                                                               RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex YamlIndentedRx    = new(@"^  +[a-zA-Z_][\w.-]*\s*:",                                                       RegexOptions.Multiline | RegexOptions.Compiled);

    public static string? DetectTextKind(string text, string? language)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();

        if (!string.IsNullOrWhiteSpace(language)) return "CODE";

        if (LooksLikeXml(t)) return "CODE";
        if (LooksLikeYaml(t)) return "YAML";
        if (LooksLikeEnv(t)) return "CODE";
        if (LooksLikeToml(t)) return "CODE";
        if (LooksLikeIni(t)) return "CODE";
        if (LooksLikeProperties(t)) return "CODE";
        if (LooksLikeDockerfile(t)) return "CODE";
        if (LooksLikeConfig(t)) return "CODE";
        if (LooksLikeMarkdown(t)) return "MARKDOWN";

        return language;
    }

    public static bool LooksLikeMarkdown(string text)
    {
        int signals = 0;
        if (MarkdownHeaderRx.IsMatch(text))  signals++;
        if (MarkdownListRx.IsMatch(text))    signals++;
        if (MarkdownFenceRx.IsMatch(text))   signals++;
        if (MarkdownLinkRx.IsMatch(text))    signals++;
        if (MarkdownEmphRx.IsMatch(text))    signals++;
        // Needs at least 2 independent Markdown signals to avoid false positives
        return signals >= 2;
    }

    private static readonly Regex FencedBlockRx = new(@"^ {0,3}(```|~~~)[^\n]*\n.*?^ {0,3}\1[ \t]*$", RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));
    private static readonly Regex InlineCodeRx = new(@"`[^`\n]+`", RegexOptions.Compiled);
    private static readonly Regex MarkdownStrongRx = new(@"(\*\*|__)\S[^\n]*?\S\1", RegexOptions.Compiled);
    private static readonly Regex MarkdownTableRx = new(@"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?\s*$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex MarkdownQuoteRx = new(@"^>\s+\S", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex ListMarkerRx = new(@"^\s*(?:[-*+]|\d+[.)])\s+", RegexOptions.Compiled);
    private static readonly Regex CodeStartRx = new(@"^\s*(?:def|class|import|from|function|const|let|var|public|private|protected|static|return|using|namespace|package|#include|if\s*\(|for\s*\(|while\s*\(|\}|\{|<\?php|<\/?[a-z][\w-]*[\s>])", RegexOptions.Compiled);
    private static readonly Regex WordRx = new(@"\p{L}{2,}", RegexOptions.Compiled);
    private const double MaxCodeLineShare = 0.25;

    /// <summary>
    /// Markdown prose (e.g. a chat answer with code samples), checked before code detection. Code blocks are set aside;
    /// the rest needs at least two Markdown signals and must read as prose: at most a quarter of its lines may look
    /// like code (code keyword at the start, ending in ; { or }, dense in code punctuation, or without any word).
    /// </summary>
    public static bool LooksLikeMarkdownDocument(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Replace("\r\n", "\n").Trim();

        string outside;
        try { outside = FencedBlockRx.Replace(t, "\n"); }
        catch (RegexMatchTimeoutException) { return false; }
        var hasFence = outside.Length != t.Length;

        var signals = hasFence ? 1 : 0;
        if (MarkdownHeaderRx.IsMatch(outside)) signals++;
        if (MarkdownListRx.IsMatch(outside)) signals++;
        if (MarkdownLinkRx.IsMatch(outside)) signals++;
        if (MarkdownStrongRx.IsMatch(outside)) signals++;
        if (MarkdownTableRx.IsMatch(outside)) signals++;
        if (MarkdownQuoteRx.IsMatch(outside)) signals++;
        if (InlineCodeRx.IsMatch(outside)) signals++;
        if (signals < 2) return false;

        var considered = 0;
        var codeLike = 0;
        foreach (var raw in outside.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('|') || line.StartsWith('>')
                || line.All(c => c is '-' or '*' or '_' or '=' or ' ')) continue;
            considered++;
            if (IsCodeLikeLine(line)) codeLike++;
        }
        if (considered == 0) return hasFence;
        return codeLike <= considered * MaxCodeLineShare;
    }

    private static bool IsCodeLikeLine(string line)
    {
        if (CodeStartRx.IsMatch(line)) return true;
        var prose = InlineCodeRx.Replace(ListMarkerRx.Replace(line, ""), " ");
        prose = MarkdownLinkRx.Replace(prose, "link");
        if (prose.EndsWith(';') || prose.EndsWith('{') || prose.EndsWith('}')) return true;
        var codeChars = prose.Count(c => c is '{' or '}' or '(' or ')' or ';' or '=' or '<' or '>' or '[' or ']');
        if (prose.Length >= 8 && codeChars > prose.Length * 0.12) return true;
        return WordRx.Matches(prose).Count == 0;
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

    public const string MarkdownKind = "MARKDOWN";

    public static string PrimaryBadge(ClipboardEntry entry) => entry.Type switch
    {
        EntryType.Url   => "URL",
        EntryType.Image => "IMG",
        EntryType.Code  => "CODE",
        EntryType.Color => "COLOR",
        _ when string.Equals(entry.ContentKind, MarkdownKind, StringComparison.OrdinalIgnoreCase) => "MD",
        _ => entry.ContentKind ?? "TEXT",
    };

    private static bool StartsWith(byte[] data, byte[] prefix)
    {
        if (data.Length < prefix.Length) return false;
        for (var i = 0; i < prefix.Length; i++)
            if (data[i] != prefix[i]) return false;
        return true;
    }

    private static bool LooksLikeYaml(string text)
    {
        bool hasSep      = YamlDocSepRx.IsMatch(text);
        bool hasMapping  = YamlMappingRx.Matches(text).Count >= 2;
        bool hasList     = YamlListItemRx.IsMatch(text);
        bool hasIndented = YamlIndentedRx.IsMatch(text);
        // Strong: document separator + any structure
        if (hasSep && (hasMapping || hasList)) return true;
        // Indented nested structure + mappings
        if (hasIndented && hasMapping) return true;
        // Mappings + list items (compact YAML without separator)
        if (hasMapping && hasList) return true;
        return false;
    }

    private static bool LooksLikeToml(string text)
        => TomlSectionRx.IsMatch(text) && TomlKeyValueRx.IsMatch(text);

    private static bool LooksLikeXml(string text)
        => XmlDeclRx.IsMatch(text)
           || XmlOpenTagRx.IsMatch(text) && XmlCloseTagRx.IsMatch(text);

    private static bool LooksLikeIni(string text)
        => IniSectionRx.IsMatch(text) && IniKeyValueRx.IsMatch(text);

    private static bool LooksLikeProperties(string text)
    {
        var stats = AnalyzeKeyValueLines(text);
        if (stats.KeyValueLines < 2 || !HasConfigLineRatio(stats)) return false;
        if (stats.EnvLines == stats.KeyValueLines && stats.EnvLines < 3) return false;

        return stats.EqualsLines >= 2
               || stats.DottedKeys >= 1
               || stats.DashedKeys >= 1
               || stats.UnderscoredKeys >= 1;
    }

    private static bool LooksLikeEnv(string text)
        => EnvLineRx.Matches(text).Count >= 3;

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
        var envLines = 0;

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
            if (EnvLineRx.IsMatch(line)) envLines++;
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
            knownConfigKeys,
            envLines);
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
        int KnownConfigKeys,
        int EnvLines);
}

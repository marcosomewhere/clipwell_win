using System.Text.RegularExpressions;
using ClipwellWin.Models;

namespace ClipwellWin.Services;

public static class ContentKindService
{
    public static string? DetectTextKind(string text, string? language)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();

        if (language == "PowerShell") return "PS1";
        if (language == "Bash") return "SH";
        if (language is "Python" or "JavaScript" or "TypeScript" or "PHP" or "Ruby") return "SCRIPT";
        if (language == "JSON") return "JSON";
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
        => Regex.IsMatch(text, @"^\s*\[[\w.-]+\]\s*$", RegexOptions.Multiline)
           && Regex.IsMatch(text, @"^\s*[\w.-]+\s*=\s*.+$", RegexOptions.Multiline);

    private static bool LooksLikeXml(string text)
        => Regex.IsMatch(text, @"^\s*<\?xml\b", RegexOptions.IgnoreCase)
           || Regex.IsMatch(text, @"^\s*<[A-Za-z][\w:.-]*(\s|>|/>)", RegexOptions.Multiline)
              && Regex.IsMatch(text, @"</[A-Za-z][\w:.-]*>\s*$", RegexOptions.Multiline);

    private static bool LooksLikeIni(string text)
        => Regex.IsMatch(text, @"^\s*\[[^\]]+\]\s*$", RegexOptions.Multiline)
           && Regex.IsMatch(text, @"^\s*[\w.-]+\s*=\s*[^=]+$", RegexOptions.Multiline);

    private static bool LooksLikeProperties(string text)
        => Regex.Matches(text, @"^\s*[\w.-]+\s*[:=]\s*.+$", RegexOptions.Multiline).Count >= 2;

    private static bool LooksLikeEnv(string text)
        => Regex.Matches(text, @"^\s*[A-Z_][A-Z0-9_]*=.+$", RegexOptions.Multiline).Count >= 2;

    private static bool LooksLikeDockerfile(string text)
        => Regex.IsMatch(text, @"^\s*(FROM|RUN|COPY|ADD|CMD|ENTRYPOINT|WORKDIR|ENV|ARG)\b", RegexOptions.Multiline);

    private static bool LooksLikeConfig(string text)
        => Regex.Matches(text, @"^\s*[\w.-]+\s*[:=]\s*.+$", RegexOptions.Multiline).Count >= 2
           || Regex.IsMatch(text, @"^\s*<configuration\b", RegexOptions.IgnoreCase);
}

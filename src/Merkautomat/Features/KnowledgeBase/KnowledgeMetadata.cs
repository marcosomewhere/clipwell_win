using System.Text.Json;
using Merkautomat.Models;

namespace Merkautomat.Services;

public static class KnowledgeMetadata
{
    private const string Prefix = "<!-- merkautomat-metadata: ";
    private const string Suffix = " -->";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(ContentMetadata metadata) => JsonSerializer.Serialize(metadata, JsonOptions);

    public static ContentMetadata? Combine(ContentMetadata? first, ContentMetadata? next)
    {
        if (first == null) return next;
        if (next == null) return first;
        var sources = (first.AdditionalSources ?? []).Append(next with { AdditionalSources = null })
            .Concat(next.AdditionalSources ?? []).DistinctBy(Serialize).ToArray();
        return first with { AdditionalSources = sources };
    }

    public static IEnumerable<string> Domains(ContentMetadata? metadata)
        => new[] { metadata }.Concat(metadata?.AdditionalSources ?? [])
            .Select(m => SemanticChunk.DomainOf(m?.SourceUrl)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase);

    public static ContentMetadata? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<ContentMetadata>(json); }
        catch (JsonException) { return null; }
    }

    public static ContentMetadata ForClipboard(ClipboardEntry entry)
    {
        var metadata = entry.Metadata ?? new ContentMetadata();
        var (width, height) = entry.ImageSize;
        return metadata with
        {
            CapturedAt = metadata.CapturedAt ?? entry.Timestamp,
            CaptureMethod = metadata.CaptureMethod ?? "Zwischenablage",
            ContentType = metadata.ContentType ?? KnowledgeExport.ClipboardTypeName(entry),
            SourceUrl = metadata.SourceUrl ?? (entry.Type == EntryType.Url ? entry.Content :
                SemanticChunk.DomainOf(entry.SourceUrl) != null ? entry.SourceUrl : null),
            SourcePath = metadata.SourcePath ?? (FileContentReader.SourcePaths(entry.SourceUrl).Count > 0 ? entry.SourceUrl : null),
            ImageWidth = width > 0 ? width : metadata.ImageWidth,
            ImageHeight = height > 0 ? height : metadata.ImageHeight,
        };
    }

    public static ContentMetadata? ReadMarkdown(string markdown)
    {
        ContentMetadata? result = null;
        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
            if (TryReadLine(line, out var metadata)) result = Combine(result, metadata);
        return result;
    }

    private static bool TryReadLine(string line, out ContentMetadata? metadata)
    {
        metadata = null;
        if (!line.StartsWith(Prefix, StringComparison.Ordinal) || !line.EndsWith(Suffix, StringComparison.Ordinal)) return false;
        metadata = Deserialize(line[Prefix.Length..^Suffix.Length]);
        return metadata != null;
    }

    public static string WithoutMetadata(string markdown)
        => string.Join("\n", markdown.Replace("\r\n", "\n").Split('\n').Where(line => !TryReadLine(line, out _)));

    public static string WithMetadata(string text, ContentMetadata metadata)
        => WithoutMetadata(text).TrimEnd() + "\n\n" + Prefix + Serialize(metadata) + Suffix + "\n";

    public static string ForDisplay(string markdown)
        => string.Join("\n", markdown.Replace("\r\n", "\n").Split('\n').Select(line =>
            TryReadLine(line, out var metadata) ? Describe(metadata) : line));

    public static string Describe(ContentMetadata? metadata)
    {
        if (metadata == null) return "";
        var lines = new List<string>();
        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                lines.Add($"- {label}: {value.Replace('\r', ' ').Replace('\n', ' ')}");
        }
        Add("Erfasst", metadata.CapturedAt?.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
        Add("Erfassungsart", metadata.CaptureMethod);
        Add("Inhaltstyp", metadata.ContentType);
        Add("Anwendung", metadata.Application);
        Add("Fenstertitel", metadata.WindowTitle);
        Add("Quell-URL", metadata.SourceUrl);
        Add("Quelldatei", metadata.SourcePath);
        if (metadata.ImageWidth > 0 && metadata.ImageHeight > 0)
            Add("Bildgröße", $"{metadata.ImageWidth} × {metadata.ImageHeight} Pixel");
        Add("Diktatmodell", metadata.SpeechModel);
        Add("Diktatsprache", metadata.SpeechLanguage);
        foreach (var source in metadata.AdditionalSources ?? [])
            lines.Add("Weitere Quelle:\n" + Describe(source with { AdditionalSources = null }));
        return string.Join("\n", lines);
    }

    public static bool DifferentSources(ClipboardEntry first, ClipboardEntry second)
    {
        static bool Different(string? a, string? b) => !string.IsNullOrWhiteSpace(a)
            && !string.IsNullOrWhiteSpace(b) && !string.Equals(a, b, StringComparison.Ordinal);
        var a = ForClipboard(first);
        var b = ForClipboard(second);
        return Different(a.SourceUrl, b.SourceUrl) || Different(a.SourcePath, b.SourcePath)
            || Different(a.Application, b.Application) || Different(a.WindowTitle, b.WindowTitle)
            || Different(a.CaptureMethod, b.CaptureMethod);
    }
}

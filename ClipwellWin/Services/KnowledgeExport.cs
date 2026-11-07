using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using ClipwellWin.Models;

namespace ClipwellWin.Services;

public enum KbArticleKind { Note, Recording, Clipboard }

/// <summary>A KB article (exported note, recording or clipboard entry) prepared for export.</summary>
public sealed record KbArticle(KbArticleKind Kind, string Title, DateTime Date, string FileName, string Content,
    CategoryPath Category = default)
{
    /// <summary>Content with the category line; unchanged without category.</summary>
    public string ExportContent => KnowledgeExport.WithCategoryLine(Content, Category);
}

public static class KnowledgeExport
{
    public const string OverviewFileName = "uebersicht.md";
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static string KindLabel(KbArticleKind kind) => kind switch
    {
        KbArticleKind.Note => AppNames.VoiceNotes,
        KbArticleKind.Recording => "Bildschirmaufnahmen",
        _ => "Zwischenablage",
    };

    public static string KindName(KbArticleKind kind) => kind switch
    {
        KbArticleKind.Note => "Sprachnotiz",
        KbArticleKind.Recording => "Bildschirmaufnahme",
        _ => "Zwischenablage",
    };

    public static string ClipboardTypeName(ClipboardEntry e) => e.Type switch
    {
        EntryType.Code => string.IsNullOrWhiteSpace(e.Language) ? "Code" : $"Code ({e.Language})",
        EntryType.Url => "URL",
        EntryType.Image => "Bild",
        EntryType.Color => "Farbe",
        _ => "Text",
    };

    /// <summary>List title of a clipboard entry: URL title, first text line, OCR line or color value.</summary>
    public static string ClipboardTitle(ClipboardEntry e)
    {
        var text = e.Type switch
        {
            EntryType.Url when !string.IsNullOrWhiteSpace(e.UrlTitle) => e.UrlTitle,
            EntryType.Image => string.IsNullOrWhiteSpace(e.OcrText) ? "Bild" : "Bild: " + e.OcrText,
            EntryType.Color => e.HexColor ?? e.Content,
            _ => e.Content,
        };
        var line = (text ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        if (line.Length == 0) return "(leer)";
        return line.Length > 90 ? line[..90] + "…" : line;
    }

    public static string ClipboardFileName(ClipboardEntry e)
        => $"{e.Timestamp.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture)}_{e.Id}.md";

    /// <summary>Markdown view of a clipboard entry for the KB: short header, then the content.</summary>
    public static string ClipboardMarkdown(ClipboardEntry e)
    {
        var sb = new StringBuilder();
        sb.Append("# Zwischenablage: ").Append(ClipboardTitle(e)).Append("\n\n");
        sb.Append("- Kopiert: ").Append(e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("- Typ: ").Append(ClipboardTypeName(e)).Append('\n');
        if (!string.IsNullOrWhiteSpace(e.SourceUrl)) sb.Append("- Quelle: ").Append(e.SourceUrl).Append('\n');
        if (e.UseCount > 0) sb.Append("- Verwendet: ").Append(e.UseCount.ToString(De)).Append("x\n");
        if (e.IsPinned) sb.Append("- Gepinnt\n");
        sb.Append('\n');

        switch (e.Type)
        {
            case EntryType.Code:
                var fence = (e.Content ?? "").Contains("```", StringComparison.Ordinal) ? "````" : "```";
                sb.Append(fence).Append(e.Language?.ToLowerInvariant() ?? "").Append('\n')
                  .Append((e.Content ?? "").TrimEnd()).Append('\n').Append(fence).Append('\n');
                break;
            case EntryType.Url:
                if (!string.IsNullOrWhiteSpace(e.UrlTitle)) sb.Append(e.UrlTitle!.Trim()).Append("\n\n");
                sb.Append('<').Append((e.Content ?? "").Trim()).Append(">\n");
                break;
            case EntryType.Image:
                sb.Append(string.IsNullOrWhiteSpace(e.OcrText)
                    ? "(Bild ohne erkannten Text)\n"
                    : "Erkannter Text (OCR):\n\n" + e.OcrText!.Trim() + "\n");
                break;
            case EntryType.Color:
                sb.Append(e.HexColor ?? e.Content).Append('\n');
                break;
            default:
                sb.Append((e.Content ?? "").TrimEnd()).Append('\n');
                break;
        }
        return sb.ToString();
    }

    /// <summary>One Markdown document with a table of contents; article headings are shifted one level down.</summary>
    public static string BuildCombined(IReadOnlyList<KbArticle> articles, DateTime now, string? overview = null)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(AppNames.KnowledgeBase).Append(" - Export\n\n");
        sb.Append("Stand: ").Append(now.ToString("dd.MM.yyyy HH:mm", De)).Append('\n');
        sb.Append("Artikel: ").Append(articles.Count.ToString(De)).Append("\n\n");

        if (articles.Count > 0)
        {
            sb.Append("## Inhalt\n\n");
            for (var i = 0; i < articles.Count; i++)
            {
                var a = articles[i];
                sb.Append(i + 1).Append(". ").Append(KindName(a.Kind))
                  .Append(": ").Append(a.Title).Append(" (").Append(a.Date.ToString("dd.MM.yyyy HH:mm", De)).Append(')');
                if (!a.Category.IsEmpty) sb.Append(" · ").Append(a.Category.ToString());
                sb.Append('\n');
            }
            sb.Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(overview))
            sb.Append("---\n\n").Append(DemoteHeadings(overview.Trim())).Append("\n\n");

        foreach (var a in articles)
            sb.Append("---\n\n").Append(DemoteHeadings(a.ExportContent.Trim())).Append("\n\n");

        return sb.ToString().TrimEnd() + "\n";
    }

    public const string CategoryLinePrefix = "Kategorie: ";
    public const string UncategorizedFolder = "ohne kategorie";

    /// <summary>Inserts "Kategorie: Haupt › Unter" after the leading heading, or at the top without one.</summary>
    public static string WithCategoryLine(string content, CategoryPath category)
    {
        if (category.IsEmpty) return content;
        var line = CategoryLinePrefix + category;
        var text = content.Replace("\r\n", "\n");
        var start = 0;
        while (start < text.Length && text[start] == '\n') start++;
        if (start < text.Length && text[start] == '#')
        {
            var end = text.IndexOf('\n', start);
            var heading = end < 0 ? text[start..] : text[start..end];
            if (heading.TrimStart('#').StartsWith(' '))
            {
                var rest = end < 0 ? "" : text[(end + 1)..].TrimStart('\n');
                return text[..start] + heading + "\n\n" + line + "\n\n" + rest;
            }
        }
        return line + "\n\n" + text[start..];
    }

    /// <summary>
    /// ZIP folder of an article. Without any category in the export the layout stays by kind (sprechwerk/,
    /// bildschirmaufnahmen/, zwischenablage/); otherwise main category, optional subcategory, then kind.
    /// </summary>
    public static string ZipFolder(KbArticle article, bool byCategory)
    {
        var kind = KindLabel(article.Kind).ToLowerInvariant();
        if (!byCategory) return kind;
        if (article.Category.IsEmpty) return UncategorizedFolder + "/" + kind;
        var folder = SafeFolderName(article.Category.Main);
        if (article.Category.HasSub) folder += "/" + SafeFolderName(article.Category.Sub);
        return folder + "/" + kind;
    }

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string SafeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) || c is '/' or '\\' ? '_' : c).ToArray())
            .Trim().TrimEnd('.', ' ');
        if (cleaned.Length == 0 || cleaned is "." or "..") return "_";
        return ReservedNames.Contains(cleaned) ? "_" + cleaned : cleaned;
    }

    /// <summary>
    /// Writes each article as its own file, grouped by category when any article has one (see <see cref="ZipFolder"/>),
    /// plus an optional overview.
    /// </summary>
    public static void WriteZip(string zipPath, IReadOnlyList<KbArticle> articles, string? overview = null)
    {
        var tmp = zipPath + ".tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(overview))
                    AddEntry(zip, used, OverviewFileName, overview);
                var byCategory = articles.Any(a => !a.Category.IsEmpty);
                foreach (var a in articles)
                    AddEntry(zip, used, ZipFolder(a, byCategory) + "/" + SafeFileName(a.FileName), a.ExportContent);
            }
            File.Move(tmp, zipPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    private static void AddEntry(ZipArchive zip, HashSet<string> used, string name, string content)
    {
        var unique = name;
        var stem = Path.ChangeExtension(name, null);
        var ext = Path.GetExtension(name);
        for (var n = 2; !used.Add(unique); n++) unique = $"{stem}-{n}{ext}";
        var entry = zip.CreateEntry(unique, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), Utf8NoBom);
        writer.Write(content.Replace("\r\n", "\n"));
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        if (cleaned.Length == 0) cleaned = "artikel";
        return cleaned.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? cleaned : cleaned + ".md";
    }

    public static string DemoteHeadings(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var inFence = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) { inFence = !inFence; continue; }
            if (inFence || !line.StartsWith('#')) continue;
            var level = line.TakeWhile(c => c == '#').Count();
            if (level < 6 && line.Length > level && line[level] == ' ') lines[i] = "#" + line;
        }
        return string.Join('\n', lines);
    }
}

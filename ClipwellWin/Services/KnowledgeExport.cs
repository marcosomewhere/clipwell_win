using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ClipwellWin.Services;

public enum KbArticleKind { Note, Recording }

/// <summary>A KB article (exported note or recording) prepared for export.</summary>
public sealed record KbArticle(KbArticleKind Kind, string Title, DateTime Date, string FileName, string Content);

public static class KnowledgeExport
{
    public const string OverviewFileName = "uebersicht.md";
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static readonly string[] DateGroupOrder = ["Heute", "Gestern", "Diese Woche", "Diesen Monat", "Früher"];

    public static string DateGroup(DateTime date, DateTime today)
    {
        var d = date.Date;
        today = today.Date;
        if (d >= today) return "Heute";
        if (d == today.AddDays(-1)) return "Gestern";
        if (d > today.AddDays(-7)) return "Diese Woche";
        if (d.Year == today.Year && d.Month == today.Month) return "Diesen Monat";
        return "Früher";
    }

    public static string KindLabel(KbArticleKind kind) => kind == KbArticleKind.Note ? "Sprachnotizen" : "Bildschirmaufnahmen";

    public static string KindName(KbArticleKind kind) => kind == KbArticleKind.Note ? "Sprachnotiz" : "Bildschirmaufnahme";

    /// <summary>One Markdown document with a table of contents; article headings are shifted one level down.</summary>
    public static string BuildCombined(IReadOnlyList<KbArticle> articles, DateTime now, string? overview = null)
    {
        var sb = new StringBuilder();
        sb.Append("# Clipwell KB - Export\n\n");
        sb.Append("Stand: ").Append(now.ToString("dd.MM.yyyy HH:mm", De)).Append('\n');
        sb.Append("Artikel: ").Append(articles.Count.ToString(De)).Append("\n\n");

        if (articles.Count > 0)
        {
            sb.Append("## Inhalt\n\n");
            for (var i = 0; i < articles.Count; i++)
            {
                var a = articles[i];
                sb.Append(i + 1).Append(". ").Append(KindName(a.Kind))
                  .Append(": ").Append(a.Title).Append(" (").Append(a.Date.ToString("dd.MM.yyyy HH:mm", De)).Append(")\n");
            }
            sb.Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(overview))
            sb.Append("---\n\n").Append(DemoteHeadings(overview.Trim())).Append("\n\n");

        foreach (var a in articles)
            sb.Append("---\n\n").Append(DemoteHeadings(a.Content.Trim())).Append("\n\n");

        return sb.ToString().TrimEnd() + "\n";
    }

    /// <summary>Writes each article as its own file into sprachnotizen/ and bildschirmaufnahmen/, plus an optional overview.</summary>
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
                foreach (var a in articles)
                {
                    var folder = KindLabel(a.Kind).ToLowerInvariant();
                    AddEntry(zip, used, folder + "/" + SafeFileName(a.FileName), a.Content);
                }
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

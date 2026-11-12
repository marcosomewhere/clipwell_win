using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Merkautomat.Services;

public sealed record BrainSource(string Key, string Title, string Text, string Location, DateTime Date);

public static class BrainKnowledge
{
    public static string Summarize(string title, string category, IReadOnlyList<BrainSource> sources)
    {
        var sb = new StringBuilder().Append("# ").AppendLine(OneLine(title)).AppendLine();
        if (!string.IsNullOrWhiteSpace(category)) sb.Append("Kategorie: ").AppendLine(OneLine(category)).AppendLine();
        sb.AppendLine("## Kernaussagen").AppendLine();
        sb.AppendLine("Ausgewählte Originalaussagen aus den markierten Quellen; keine frei formulierte Gesamtaussage.").AppendLine();
        var selected = KeyStatements(sources.Select(s => s.Text).ToList(), 12, 3);
        foreach (var c in selected)
            sb.Append("- ").Append(c.Text).Append(" [").Append(c.Source + 1).AppendLine("]");
        if (selected.Count == 0) sb.AppendLine("Keine ausreichend langen Originalaussagen gefunden. Bitte Zusammenfassung ergänzen.");
        sb.AppendLine().AppendLine("## Quellen").AppendLine();
        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            sb.Append('[').Append(i + 1).Append("] ").Append(OneLine(source.Title)).Append(" · ")
                .AppendLine(source.Date.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture));
            sb.Append("Quelle: ").AppendLine(OneLine(source.Location));
            sb.Append("Eintrag: ").AppendLine(source.Key).AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// Extractive selection: original sentences or lines of at least 20 characters, ranked by how common their terms
    /// are across all texts, without near repeats; at most <paramref name="perSource"/> per text. Returned in text order.
    /// </summary>
    public static List<(string Text, int Source)> KeyStatements(IReadOnlyList<string> texts, int max, int perSource)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<(string Text, int Source, int Order, HashSet<string> Terms)>();
        for (var i = 0; i < texts.Count; i++)
        {
            var order = 0;
            foreach (var raw in Regex.Split(texts[i], @"(?<=[.!?])\s+|\r?\n+"))
            {
                var line = OneLine(raw).TrimStart('#', '-', '*', ' ');
                if (line.Length < 20 || !seen.Add(line)) continue;
                candidates.Add((line, i, order++, SemanticRanking.Terms(line).ToHashSet()));
            }
        }
        var frequency = candidates.SelectMany(c => c.Terms).GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
        var ranked = candidates.Select(c => (c, Score: c.Terms.Sum(t => Math.Log(1 + frequency[t])) / Math.Sqrt(Math.Max(1, c.Terms.Count))))
            .OrderByDescending(x => x.Score).ThenBy(x => x.c.Source).ThenBy(x => x.c.Order).ToList();
        var selected = new List<(string Text, int Source, int Order, HashSet<string> Terms)>();
        foreach (var (candidate, _) in ranked)
        {
            if (selected.Count >= max) break;
            if (selected.Count(c => c.Source == candidate.Source) >= perSource) continue;
            if (selected.Any(c => c.Terms.Count > 0 && candidate.Terms.Count > 0
                && (double)c.Terms.Intersect(candidate.Terms).Count() / c.Terms.Union(candidate.Terms).Count() > 0.85)) continue;
            selected.Add(candidate);
        }
        return selected.OrderBy(c => c.Source).ThenBy(c => c.Order).Select(c => (c.Text, c.Source)).ToList();
    }

    private static string OneLine(string value) => Regex.Replace(value, @"\s+", " ").Trim();

    /// <summary>Only an explicitly linked summary is updated; source notes are never overwritten.</summary>
    public static (string NoteId, string KbPath) Save(string notesDir, string kbDir, string? summaryId, string text, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Die Zusammenfassung ist leer.", nameof(text));
        if (summaryId == null)
        {
            var (note, path) = NoteStore.CreateInKb(notesDir, kbDir, text, now,
                new Models.ContentMetadata { CapturedAt = now, CaptureMethod = "Brain-Zusammenfassung", Application = AppNames.App });
            return (note.Id, path);
        }
        if (Path.GetFileName(summaryId) != summaryId || summaryId.IndexOfAny(['/', '\\', ':']) >= 0
            || !NoteStore.TryParseId(summaryId, out var created)) throw new ArgumentException("Ungültige Sprachnotiz-ID.", nameof(summaryId));
        var notePath = Path.Combine(notesDir, summaryId + ".md");
        var kbPath = NoteStore.KbPathFor(kbDir, summaryId);
        // An externally deleted summary is recreated with its linked id, not under a second id.
        Directory.CreateDirectory(notesDir);
        Directory.CreateDirectory(kbDir);
        if (File.Exists(notePath)) File.Copy(notePath, notePath + ".bak", overwrite: true);
        if (File.Exists(kbPath)) File.Copy(kbPath, kbPath + ".bak", overwrite: true);
        var updated = new Note { Id = summaryId, Path = notePath, Created = created };
        NoteStore.Save(updated, text);
        NoteStore.ExportToKb(updated, kbDir, now > updated.Modified ? now : updated.Modified);
        return (summaryId, kbPath);
    }
}

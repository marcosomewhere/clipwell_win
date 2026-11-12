using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Merkautomat.Models;

namespace Merkautomat.Services;

/// <summary>One entry to merge with its complete original text.</summary>
/// <param name="Removable">The source can be deleted after a verified merge (text clipboard entries and notes).</param>
/// <param name="KeepReason">Why the source stays even when removing is requested.</param>
public sealed record MergeSource(string Key, SemanticSource Source, string Title, DateTime Date, string Text, string Origin,
    bool Removable, string KeepReason = "");

/// <summary>
/// Merging Brain entries without losing anything: the new note contains every original text in full, each with its
/// origin; only identical texts are written once (with all their origins). Before a source may be removed,
/// <see cref="Missing"/> checks the saved note against every original text. Category, collections, relations and
/// summary sources move to the new note.
/// </summary>
public static partial class BrainMerge
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public static string Build(IReadOnlyList<MergeSource> sources, string title, DateTime now)
    {
        var groups = new List<(string Normalized, List<MergeSource> Members)>();
        foreach (var source in sources)
        {
            var normalized = Normalize(source.Text);
            var same = groups.FindIndex(g => g.Normalized == normalized);
            if (same >= 0) groups[same].Members.Add(source);
            else groups.Add((normalized, [source]));
        }
        var sb = new StringBuilder();
        sb.Append("# ").Append(OneLine(title)).Append("\n\n");
        sb.Append($"Zusammengeführt am {now.ToString("dd.MM.yyyy HH:mm", De)} aus {sources.Count} Einträgen. ")
          .Append("Jeder Abschnitt enthält den vollständigen Originaltext; gleiche Texte stehen einmal mit allen Herkünften.\n\n");
        for (var i = 0; i < groups.Count; i++)
        {
            var members = groups[i].Members;
            sb.Append("## ").Append(i + 1).Append(". ").Append(OneLine(members[0].Title)).Append("\n\n");
            foreach (var m in members)
            {
                sb.Append("- ").Append(SemanticChunk.SourceName(m.Source)).Append(" · ").Append(m.Date.ToString("dd.MM.yyyy HH:mm", De));
                if (m.Origin.Length > 0) sb.Append(" · ").Append(OneLine(m.Origin));
                sb.Append(" · Eintrag ").Append(m.Key).Append('\n');
            }
            sb.Append('\n').Append(members[0].Text.Replace("\r\n", "\n").TrimEnd()).Append("\n\n");
        }
        return sb.ToString().TrimEnd() + "\n";
    }

    /// <summary>Whitespace collapsed, so line breaks or trailing spaces changed when saving do not count as a loss.</summary>
    public static string Normalize(string text) => Whitespace().Replace(text, " ").Trim();

    /// <summary>Keys of sources whose text is not contained completely in <paramref name="saved"/>.</summary>
    public static List<string> Missing(string saved, IReadOnlyList<MergeSource> sources)
    {
        var all = Normalize(saved);
        return sources.Where(s => !all.Contains(Normalize(s.Text), StringComparison.Ordinal)).Select(s => s.Key).ToList();
    }

    /// <summary>The deepest category all categorized sources share; without such a common part the fallback.</summary>
    public static CategoryPath SharedCategory(IReadOnlyList<CategoryPath> paths, CategoryPath fallback)
    {
        var set = paths.Where(p => !p.IsEmpty).ToList();
        if (set.Count == 0) return fallback;
        var levels = new List<string>();
        for (var depth = 0; depth < KnowledgeCategories.MaxDepth; depth++)
        {
            if (set.Any(p => p.Depth <= depth)) break;
            var name = set[0].Levels[depth];
            if (!set.All(p => string.Equals(p.Levels[depth], name, StringComparison.OrdinalIgnoreCase))) break;
            levels.Add(name);
        }
        return levels.Count > 0 ? KnowledgeCategories.Build(levels) : fallback;
    }

    /// <summary>
    /// The new note takes the place of the merged entries: category, membership in collections, manual relations
    /// (self references dropped, duplicates removed) and summary sources.
    /// </summary>
    public static void Transfer(AppSettings s, IReadOnlyCollection<string> from, string to, CategoryPath category)
    {
        var merged = from.ToHashSet(StringComparer.Ordinal);
        if (!category.IsEmpty) KnowledgeCategories.Assign(s, [to], category);
        foreach (var c in s.BrainCollections ?? [])
            if (c.Keys.Any(merged.Contains) && !c.Keys.Contains(to)) c.Keys.Add(to);
        if (s.BrainRelations is { Count: > 0 } relations)
        {
            string Map(string key) => merged.Contains(key) ? to : key;
            s.BrainRelations = relations
                .Select(r => r with { FromKey = Map(r.FromKey), ToKey = Map(r.ToKey) })
                .Where(r => r.FromKey != r.ToKey)
                .DistinctBy(r => (r.FromKey, r.ToKey, r.Kind))
                .ToList();
        }
        foreach (var summary in s.BrainSummaries ?? [])
            if (summary.SourceKeys.Any(merged.Contains))
                summary.SourceKeys = summary.SourceKeys.Select(k => merged.Contains(k) ? to : k).Distinct(StringComparer.Ordinal).ToList();
    }

    private static string OneLine(string value) => Whitespace().Replace(value, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

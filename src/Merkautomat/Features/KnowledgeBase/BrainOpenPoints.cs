using System.Text.RegularExpressions;

namespace Merkautomat.Services;

/// <summary>A line that reads like an open to-do, with the item it came from.</summary>
public sealed record OpenPoint(string ItemKey, string Title, string SourceTitle, string Context);

/// <summary>
/// Finds explicitly marked open points in item texts: unchecked Markdown boxes and lines starting with TODO, FIXME,
/// "Offen:", "Aufgabe:" or "Nächster Schritt:". Only explicit markers, so ordinary sentences never become tasks.
/// </summary>
public static partial class BrainOpenPoints
{
    public const int MaxPoints = 100;
    private const int ContextLines = 3;

    public static List<OpenPoint> Find(IEnumerable<BrainSource> sources, IEnumerable<string> existingTaskTitles)
    {
        var seen = existingTaskTitles.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var points = new List<OpenPoint>();
        foreach (var source in sources)
        {
            var lines = (source.Text ?? "").ReplaceLineEndings("\n").Split('\n');
            for (var i = 0; i < lines.Length && points.Count < MaxPoints; i++)
            {
                var text = Match(lines[i]);
                if (text == null) continue;
                var title = TaskStore.TitleFrom(text);
                if (title.Length < 3 || !seen.Add(Normalize(title))) continue;
                var context = string.Join("\n", lines.Skip(i).Take(ContextLines + 1).Select(l => l.TrimEnd()).TakeWhile((l, k) => k == 0 || l.Trim().Length > 0));
                points.Add(new OpenPoint(source.Key, title, source.Title, context.Trim()));
            }
        }
        return points;
    }

    /// <summary>The to-do text of a marked line, or null.</summary>
    public static string? Match(string line)
    {
        var trimmed = line.Trim().TrimStart('>').Trim();
        var box = OpenBox().Match(trimmed);
        if (box.Success) return box.Groups[1].Value.Trim();
        var marker = Marker().Match(trimmed);
        return marker.Success && marker.Groups[1].Value.Trim().Length > 0 ? marker.Groups[1].Value.Trim() : null;
    }

    public static KbTask ToTask(OpenPoint point, string? sourceKey, DateTime now) => new()
    {
        Title = point.Title,
        Created = now,
        SourceKey = sourceKey,
        SourceTitle = point.SourceTitle,
        SourceText = point.Context.Length <= TaskStore.MaxSourceText ? point.Context : point.Context[..TaskStore.MaxSourceText] + "…",
    };

    private static string Normalize(string title) => Whitespace().Replace(title.Trim().TrimEnd('.', ':', '!'), " ");

    [GeneratedRegex(@"^(?:[-*+]|\d+[.)])\s*\[ \]\s+(.+)$")]
    private static partial Regex OpenBox();

    // TODO and FIXME also without a colon; the other words only with ":" or a spaced dash ("Offen - ..."), so that
    // "Offene Fragen" or "Aufgabenliste" do not match.
    [GeneratedRegex(@"^(?:[-*+]\s+)?(?:(?:@?todo|fixme)(?:\s*:\s*|\s+[-–]\s+|\s+)|(?:to-do|offen|aufgabe|n(?:ä|ae)chster schritt|next step|action item)(?:\s*:\s*|\s+[-–]\s+))(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex Marker();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

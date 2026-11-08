using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ClipwellWin.Services;

public sealed record RecordingSnapshot(DateTime Time, string App, string Title);

public sealed record RecordingSnapshotText(DateTime Time, string App, string Title, string Body);

public sealed class RecordingSession
{
    public string Path { get; init; } = "";
    public DateTime Start { get; init; }
    public DateTime? End { get; init; }
    public List<RecordingSnapshot> Snapshots { get; } = [];

    public DateTime LastActivity => End ?? (Snapshots.Count > 0 ? Snapshots[^1].Time : Start);
    public TimeSpan Duration => LastActivity > Start ? LastActivity - Start : TimeSpan.Zero;
    public bool IsOpen => End == null;
}

/// <summary>Markdown layout for desktop recordings. Writer and parser share this file so the format stays in sync.</summary>
public static partial class RecordingMarkdown
{
    public const string FileTimestampFormat = "yyyy-MM-dd_HH-mm-ss";
    private const string TimeFormat = "HH:mm:ss";
    private const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";
    private const int MaxLineLength = 2000;
    public const int MaxLinesPerSnapshot = 400;
    private const double FullContentOverlapThreshold = 0.3;

    [GeneratedRegex(@"^# Desktop-Aufnahme (\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\s*$")]
    private static partial Regex SessionHeaderRegex();

    [GeneratedRegex(@"^## (\d{2}:\d{2}:\d{2}) - ([^:]+): (.*)$")]
    private static partial Regex SnapshotHeaderRegex();

    [GeneratedRegex(@"^- Ende: (\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\s*$")]
    private static partial Regex SessionEndRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^\|(?: ?:?-{3,}:? ?\|)+$")]
    private static partial Regex TableSeparatorRegex();

    public static string FileNameFor(DateTime start)
        => start.ToString(FileTimestampFormat, CultureInfo.InvariantCulture) + ".md";

    public static string SessionHeader(DateTime start)
    {
        var sb = new StringBuilder();
        sb.Append("# Desktop-Aufnahme ").Append(start.ToString(DateTimeFormat, CultureInfo.InvariantCulture)).Append('\n');
        sb.Append('\n');
        sb.Append("- Start: ").Append(start.ToString(DateTimeFormat, CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("- Erfassung: UI Automation (sichtbarer Text, keine Bildaufnahme)\n");
        sb.Append('\n');
        return sb.ToString();
    }

    public static string SessionFooter(DateTime start, DateTime end, int snapshotCount)
    {
        var sb = new StringBuilder();
        sb.Append("---\n\n");
        sb.Append("- Ende: ").Append(end.ToString(DateTimeFormat, CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("- Dauer: ").Append(FormatDuration(end - start)).Append('\n');
        sb.Append("- Einträge: ").Append(snapshotCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
        return sb.ToString();
    }

    /// <summary>Trims, collapses whitespace, drops empties and duplicates, and caps line count and length.</summary>
    public static List<string> NormalizeLines(IEnumerable<string?> raw)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in raw)
        {
            if (string.IsNullOrWhiteSpace(item)) continue;
            foreach (var part in item.Split('\n'))
            {
                // U+FFFC is the placeholder Chromium reports for embedded objects.
                var line = WhitespaceRegex().Replace(part.Replace('￼', ' '), " ").Trim();
                if (line.Length == 0) continue;
                // Every table repeats the same separator row, so it is exempt from the duplicate check.
                var separator = TableSeparatorRegex().IsMatch(line);
                if (!separator && !line.Any(char.IsLetterOrDigit)) continue;
                if (line.Length > MaxLineLength) line = line[..MaxLineLength] + "…";
                if (!separator && !seen.Add(line)) continue;
                result.Add(line);
                if (result.Count >= MaxLinesPerSnapshot) return result;
            }
        }
        return result;
    }

    private static readonly HashSet<string> WindowChromeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Minimieren", "Maximieren", "Schließen", "Wiederherstellen", "Verkleinern",
        "Minimize", "Maximize", "Close", "Restore", "Restore Down", "System",
        "Zurück um kleine Menge", "Zurück um große Menge", "Vorwärts um kleine Menge", "Vorwärts um große Menge",
        "Line up", "Line down", "Page up", "Page down",
    };

    /// <summary>True for caption and scroll button names that carry no content.</summary>
    public static bool IsWindowChromeName(string name)
    {
        var n = name.Trim();
        // Store apps label caption buttons as "\"Rechner\" minimieren".
        return WindowChromeNames.Contains(n)
            || (n.StartsWith('"') && (n.EndsWith("\" minimieren", StringComparison.OrdinalIgnoreCase)
                || n.EndsWith("\" maximieren", StringComparison.OrdinalIgnoreCase)
                || n.EndsWith("\" schließen", StringComparison.OrdinalIgnoreCase)
                || n.EndsWith("\" wiederherstellen", StringComparison.OrdinalIgnoreCase)));
    }

    public static string CleanAppName(string? app)
    {
        var name = WhitespaceRegex().Replace((app ?? "").Replace(':', ' '), " ").Trim();
        return name.Length == 0 ? "Unbekannt" : name;
    }

    public static string CleanTitle(string? title)
    {
        var t = WhitespaceRegex().Replace(title ?? "", " ").Trim();
        return t.Length == 0 ? "(ohne Titel)" : t;
    }

    /// <summary>
    /// Formats one snapshot. When lines of this window were already logged and the content still overlaps
    /// enough, only lines not logged before are written to keep the log readable.
    /// </summary>
    public static string FormatSnapshot(
        DateTime time,
        string app,
        string title,
        IReadOnlyList<string> lines,
        IReadOnlyList<string>? alreadyLoggedLines,
        string? previousWindowLabel)
    {
        var sb = new StringBuilder();
        sb.Append("## ").Append(time.ToString(TimeFormat, CultureInfo.InvariantCulture))
          .Append(" - ").Append(CleanAppName(app)).Append(": ").Append(CleanTitle(title)).Append('\n').Append('\n');

        var change = new List<string>();
        if (previousWindowLabel != null)
            change.Add($"Fensterwechsel von \"{previousWindowLabel}\".");

        var useDiff = false;
        List<string> added = [];
        if (alreadyLoggedLines != null && alreadyLoggedLines.Count > 0)
        {
            var logged = new HashSet<string>(alreadyLoggedLines, StringComparer.Ordinal);
            added = lines.Where(l => !logged.Contains(l)).ToList();
            var overlap = lines.Count == 0 ? 0 : (double)(lines.Count - added.Count) / lines.Count;
            useDiff = overlap >= FullContentOverlapThreshold;
        }

        if (useDiff)
        {
            if (added.Count > 0)
            {
                sb.Append("### Neu sichtbar\n\n");
                AppendBullets(sb, added);
            }
            change.Add(added.Count == 1 ? "1 neue Zeile." : $"{added.Count} neue Zeilen.");
        }
        else
        {
            sb.Append("### Sichtbarer Inhalt\n\n");
            if (lines.Count == 0) sb.Append("- (kein lesbarer Text)\n\n");
            else AppendBullets(sb, lines);
            if (alreadyLoggedLines != null && alreadyLoggedLines.Count > 0)
                change.Add("Inhalt weitgehend neu.");
        }

        if (change.Count > 0)
        {
            sb.Append("### Änderung\n\n");
            foreach (var c in change) sb.Append(c).Append('\n');
            sb.Append('\n');
        }
        return sb.ToString();
    }

    public static bool IsTableLine(string line)
        => line.Length > 1 && line[0] == '|' && line[^1] == '|';

    /// <summary>Writes lines as a bullet list; Markdown table rows are written as is, set off from bullets by a blank line.</summary>
    private static void AppendBullets(StringBuilder sb, IEnumerable<string> lines)
    {
        var inTable = false;
        var first = true;
        foreach (var line in lines)
        {
            var table = IsTableLine(line);
            if (table != inTable && !first) sb.Append('\n');
            inTable = table;
            first = false;
            if (!table) sb.Append("- ");
            sb.Append(line).Append('\n');
        }
        sb.Append('\n');
    }

    public static RecordingSession? ParseSession(string path, string text)
    {
        DateTime? start = null;
        DateTime? end = null;
        var snapshots = new List<RecordingSnapshot>();
        var lastTime = DateTime.MinValue;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (start == null)
            {
                var h = SessionHeaderRegex().Match(line);
                if (h.Success && TryParseDateTime(h.Groups[1].Value, out var s)) { start = s; lastTime = s; }
                continue;
            }

            var m = SnapshotHeaderRegex().Match(line);
            if (m.Success && TimeSpan.TryParseExact(m.Groups[1].Value, @"hh\:mm\:ss", CultureInfo.InvariantCulture, out var tod))
            {
                var time = lastTime.Date + tod;
                if (time < lastTime) time = time.AddDays(1);
                lastTime = time;
                snapshots.Add(new RecordingSnapshot(time, m.Groups[2].Value.Trim(), m.Groups[3].Value.Trim()));
                continue;
            }

            var e = SessionEndRegex().Match(line);
            if (e.Success && TryParseDateTime(e.Groups[1].Value, out var endTime)) end = endTime;
        }

        if (start == null) return null;
        var session = new RecordingSession { Path = path, Start = start.Value, End = end };
        session.Snapshots.AddRange(snapshots);
        return session;
    }

    /// <summary>
    /// Splits a recording into its snapshots. The body keeps only the captured text: no headings, no change notes,
    /// no bullet markers.
    /// </summary>
    public static List<RecordingSnapshotText> SplitSnapshots(string text)
    {
        var result = new List<RecordingSnapshotText>();
        DateTime? start = null;
        var lastTime = DateTime.MinValue;
        (DateTime Time, string App, string Title)? current = null;
        var body = new List<string>();
        var skipSection = false;

        void Flush()
        {
            if (current is { } c) result.Add(new RecordingSnapshotText(c.Time, c.App, c.Title, string.Join("\n", body)));
            body.Clear();
            current = null;
        }

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (start == null)
            {
                var h = SessionHeaderRegex().Match(line);
                if (h.Success && TryParseDateTime(h.Groups[1].Value, out var s)) { start = s; lastTime = s; }
                continue;
            }

            var m = SnapshotHeaderRegex().Match(line);
            if (m.Success && TimeSpan.TryParseExact(m.Groups[1].Value, @"hh\:mm\:ss", CultureInfo.InvariantCulture, out var tod))
            {
                Flush();
                var time = lastTime.Date + tod;
                if (time < lastTime) time = time.AddDays(1);
                lastTime = time;
                current = (time, m.Groups[2].Value.Trim(), m.Groups[3].Value.Trim());
                skipSection = false;
                continue;
            }
            if (current == null) continue;
            if (line == "---") { Flush(); continue; }
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                skipSection = line == "### Änderung";
                continue;
            }
            if (skipSection) continue;

            var content = line.StartsWith("- ", StringComparison.Ordinal) ? line[2..] : line;
            content = content.Trim();
            if (content.Length == 0 || content == "(kein lesbarer Text)") continue;
            body.Add(content);
        }
        Flush();
        return result;
    }

    private static bool TryParseDateTime(string value, out DateTime result)
        => DateTime.TryParseExact(value, DateTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    public static string FormatDuration(TimeSpan d)
        => d < TimeSpan.Zero ? "00:00:00" : $"{(int)d.TotalHours:00}:{d.Minutes:00}:{d.Seconds:00}";

    public static List<RecordingSession> LoadSessions(string directory)
    {
        var sessions = new List<RecordingSession>();
        if (!Directory.Exists(directory)) return sessions;
        foreach (var file in Directory.EnumerateFiles(directory, "*.md"))
        {
            try
            {
                var session = ParseSession(file, ReadShared(file));
                if (session != null) sessions.Add(session);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.Cryptography.CryptographicException ex) { App.LogCrash(ex); }
        }
        return sessions.OrderByDescending(s => s.Start).ToList();
    }

    /// <summary>Reads a file that the recorder may still have open for appending.</summary>
    public static string ReadShared(string path) => SecureFile.ReadAllText(path);
}

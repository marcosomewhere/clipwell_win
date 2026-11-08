using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClipwellWin.Models;

namespace ClipwellWin.Services;

/// <summary>One task in the Ideenkombinat. Source fields keep a copy, so the task survives deleting its origin.</summary>
public sealed class KbTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool Done { get; set; }
    public DateTime Created { get; set; }
    public DateTime? Completed { get; set; }
    public DateTime? Due { get; set; }
    /// <summary>Origin as item key ("clip:42") or file path of a note or recording; null for manual tasks.</summary>
    public string? SourceKey { get; set; }
    public string? SourceTitle { get; set; }
    public string? SourceText { get; set; }
}

/// <summary>Tasks as a single JSON file next to the settings; written atomically via a temporary file.</summary>
public static partial class TaskStore
{
    public const int MaxTitle = 200;
    public const int MaxNotes = 20_000;
    public const int MaxSourceText = 4000;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static List<KbTask> Load(string path)
    {
        if (!File.Exists(path)) return [];
        try
        {
            var json = SecureFile.ReadAllText(path);
            if (json.Trim().Length == 0) return [];
            return (JsonSerializer.Deserialize<List<KbTask>>(json, Json) ?? []).Where(t => t != null && t.Id.Length > 0).ToList();
        }
        catch (Exception ex) when (ex is JsonException or System.Security.Cryptography.CryptographicException or InvalidDataException)
        {
            // Keep the unreadable file for manual recovery instead of overwriting it with the next save.
            File.Move(path, path + $".defekt-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
            return [];
        }
    }

    public static void Save(string path, IEnumerable<KbTask> tasks)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        SecureFile.WriteAllText(temp, JsonSerializer.Serialize(tasks.ToList(), Json));
        File.Move(temp, path, overwrite: true);
    }

    public static KbTask Create(string title, DateTime now)
        => new() { Title = TitleFrom(title), Created = now };

    public static KbTask FromEntry(ClipboardEntry entry, string sourceKey, DateTime now)
    {
        var text = entry.Type switch
        {
            EntryType.Image => entry.OcrText ?? "",
            EntryType.Url => string.IsNullOrWhiteSpace(entry.UrlTitle) ? entry.Content ?? "" : $"{entry.UrlTitle}\n{entry.Content}",
            _ => entry.Content ?? "",
        };
        var title = KnowledgeExport.ClipboardTitle(entry);
        return new()
        {
            Title = TitleFrom(title),
            Created = now,
            SourceKey = sourceKey,
            SourceTitle = title,
            SourceText = Limit(text.Trim(), MaxSourceText),
        };
    }

    /// <summary>First non-empty line without Markdown heading or list markers, at most <see cref="MaxTitle"/> characters.</summary>
    public static string TitleFrom(string text)
    {
        var line = (text ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        line = Marker().Replace(line, "").Trim();
        line = Whitespace().Replace(line, " ");
        if (line.Length == 0) return "Neue Aufgabe";
        return line.Length > MaxTitle ? line[..(MaxTitle - 1)].TrimEnd() + "…" : line;
    }

    /// <summary>Open tasks first (due date, then newest), done tasks after them, most recently completed first.</summary>
    public static List<KbTask> Sorted(IEnumerable<KbTask> tasks)
        => tasks.OrderBy(t => t.Done)
            .ThenBy(t => t.Done ? 0 : t.Due.HasValue ? 0 : 1)
            .ThenBy(t => t.Done ? DateTime.MinValue : t.Due ?? DateTime.MaxValue)
            .ThenByDescending(t => t.Done ? t.Completed ?? t.Created : t.Created)
            .ToList();

    public static void SetDone(KbTask task, bool done, DateTime now)
    {
        task.Done = done;
        task.Completed = done ? now : null;
    }

    public static List<KbTask> OpenDueOn(IEnumerable<KbTask> tasks, DateTime day)
        => tasks.Where(t => !t.Done && t.Due?.Date == day.Date).ToList();

    /// <summary>Marks every open task due on <paramref name="day"/> as done; returns the tasks that changed.</summary>
    public static List<KbTask> CompleteDueOn(IEnumerable<KbTask> tasks, DateTime day, DateTime now)
    {
        var due = OpenDueOn(tasks, day);
        foreach (var task in due) SetDone(task, true, now);
        return due;
    }

    public static bool IsOverdue(KbTask task, DateTime today) => !task.Done && task.Due is { } due && due.Date < today.Date;

    /// <summary>Accepts "31.12.2026", "31.12." (this year) and ISO dates; empty text clears the due date.</summary>
    public static bool TryParseDue(string text, DateTime today, out DateTime? due)
    {
        due = null;
        var value = text.Trim();
        if (value.Length == 0) return true;
        var de = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
        string[] formats = ["d.M.yyyy", "d.M.yy", "yyyy-MM-dd"];
        if (DateTime.TryParseExact(value, formats, de, System.Globalization.DateTimeStyles.None, out var parsed))
        {
            due = parsed.Date;
            return true;
        }
        if (DateTime.TryParseExact(value.TrimEnd('.'), "d.M", de, System.Globalization.DateTimeStyles.None, out parsed))
        {
            due = new DateTime(today.Year, parsed.Month, parsed.Day);
            return true;
        }
        return false;
    }

    private static string Limit(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    [GeneratedRegex(@"^(#{1,6}\s+|[-*+]\s+(\[[ xX]\]\s+)?|\d+[.)]\s+)")]
    private static partial Regex Marker();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

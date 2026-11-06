using System.Globalization;
using System.IO;
using System.Text;

namespace ClipwellWin.Services;

public sealed class Note
{
    public string Id { get; init; } = "";
    public string Path { get; init; } = "";
    public DateTime Created { get; init; }
    public DateTime Modified { get; set; }
    public string Text { get; set; } = "";
    public DateTime? KbExportedAt { get; set; }

    public string Title => NoteStore.TitleOf(Text);
    public bool IsInKb => KbExportedAt != null;
    public bool KbOutdated => KbExportedAt is { } exported && Modified > exported.AddSeconds(1);
}

/// <summary>A note exported into the knowledge base; a snapshot that stays independent of later edits to the note.</summary>
public sealed record KbNote(string Id, string Path, string Title, DateTime Created, DateTime Exported);

/// <summary>
/// Notes are plain Markdown files named after their creation time. Exporting to the KB writes a snapshot copy
/// into the KB folder, so the KB keeps the exported state even when the note is edited or deleted later.
/// </summary>
public static class NoteStore
{
    public const string IdFormat = "yyyy-MM-dd_HH-mm-ss";
    private const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";
    private const string KbHeaderPrefix = "# Notiz: ";
    private const string KbCreatedPrefix = "- Erstellt: ";
    private const string KbExportedPrefix = "- Exportiert: ";
    private const int MaxTitleLength = 80;
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static string TitleOf(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('#').Trim();
            if (line.Length == 0) continue;
            return line.Length > MaxTitleLength ? line[..MaxTitleLength] + "…" : line;
        }
        return "(leere Notiz)";
    }

    public static bool TryParseId(string id, out DateTime created)
    {
        // Ids created within the same second get a numeric suffix.
        var stem = id.Length > IdFormat.Length ? id[..IdFormat.Length] : id;
        return DateTime.TryParseExact(stem, IdFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out created);
    }

    public static List<Note> Load(string notesDir, string kbNotesDir)
    {
        var result = new List<Note>();
        if (!Directory.Exists(notesDir)) return result;

        var exports = LoadKbNotes(kbNotesDir).ToDictionary(k => k.Id, k => k.Exported, StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(notesDir, "*.md"))
        {
            var id = System.IO.Path.GetFileNameWithoutExtension(path);
            if (!TryParseId(id, out var created)) continue;
            string text;
            try { text = File.ReadAllText(path, Encoding.UTF8); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            result.Add(new Note
            {
                Id = id,
                Path = path,
                Created = created,
                Modified = File.GetLastWriteTime(path),
                Text = NormalizeNewlines(text),
                KbExportedAt = exports.TryGetValue(id, out var exported) ? exported : null,
            });
        }
        return result.OrderByDescending(n => n.Created).ThenByDescending(n => n.Id, StringComparer.Ordinal).ToList();
    }

    public static Note Create(string notesDir, string text, DateTime now)
    {
        Directory.CreateDirectory(notesDir);
        var baseId = now.ToString(IdFormat, CultureInfo.InvariantCulture);
        var id = baseId;
        for (var i = 2; File.Exists(System.IO.Path.Combine(notesDir, id + ".md")); i++)
            id = $"{baseId}-{i}";

        var path = System.IO.Path.Combine(notesDir, id + ".md");
        var normalized = NormalizeNewlines(text);
        File.WriteAllText(path, normalized, Utf8NoBom);
        return new Note
        {
            Id = id,
            Path = path,
            Created = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, now.Kind),
            Modified = File.GetLastWriteTime(path),
            Text = normalized,
        };
    }

    public static void Save(Note note, string text)
    {
        var normalized = NormalizeNewlines(text);
        File.WriteAllText(note.Path, normalized, Utf8NoBom);
        note.Text = normalized;
        note.Modified = File.GetLastWriteTime(note.Path);
    }

    public static void Delete(Note note)
    {
        if (File.Exists(note.Path)) File.Delete(note.Path);
    }

    public static string KbPathFor(string kbNotesDir, string id) => System.IO.Path.Combine(kbNotesDir, id + ".md");

    public static string BuildKbMarkdown(Note note, DateTime exported)
    {
        var sb = new StringBuilder();
        sb.Append(KbHeaderPrefix).Append(note.Title).Append("\n\n");
        sb.Append(KbCreatedPrefix).Append(note.Created.ToString(DateTimeFormat, CultureInfo.InvariantCulture)).Append('\n');
        sb.Append(KbExportedPrefix).Append(exported.ToString(DateTimeFormat, CultureInfo.InvariantCulture)).Append("\n\n");
        sb.Append(note.Text.TrimEnd()).Append('\n');
        return sb.ToString();
    }

    public static string ExportToKb(Note note, string kbNotesDir, DateTime now)
    {
        Directory.CreateDirectory(kbNotesDir);
        var path = KbPathFor(kbNotesDir, note.Id);
        File.WriteAllText(path, BuildKbMarkdown(note, now), Utf8NoBom);
        note.KbExportedAt = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, now.Kind);
        return path;
    }

    /// <summary>Quick notes: saved as a regular note and exported to the KB in one step.</summary>
    public static (Note Note, string KbPath) CreateInKb(string notesDir, string kbNotesDir, string text, DateTime now)
    {
        var note = Create(notesDir, text, now);
        // Export no earlier than the file's write time, otherwise the note would show up as "KB veraltet".
        var exported = note.Modified > now ? note.Modified : now;
        return (note, ExportToKb(note, kbNotesDir, exported));
    }

    public const long MaxImportBytes = 2 * 1024 * 1024;
    public static readonly string[] ImportExtensions = [".md", ".markdown", ".txt"];

    public static bool CanImport(string path)
        => ImportExtensions.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Text of an imported file: UTF-8 or UTF-16 by byte order mark, otherwise strict UTF-8, and files that are
    /// not valid UTF-8 (older Windows text files) as Windows-1252-compatible Latin-1.
    /// </summary>
    public static string DecodeImport(byte[] bytes)
    {
        if (bytes.Length >= 2 && (bytes[0] == 0xFF && bytes[1] == 0xFE || bytes[0] == 0xFE && bytes[1] == 0xFF))
            return new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true).ReadToEnd();
        var skip = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        try { return new UTF8Encoding(false, true).GetString(bytes, skip, bytes.Length - skip); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }

    /// <summary>Note text for an imported file; the file name becomes the heading unless the text starts with one.</summary>
    public static string ImportText(string fileName, string content)
    {
        var text = NormalizeNewlines(content).Trim();
        if (text.Length == 0) return "";
        if (text.StartsWith('#')) return text;
        return $"# {System.IO.Path.GetFileNameWithoutExtension(fileName)}\n\n{text}";
    }

    /// <summary>Text of a KB note without the header written by <see cref="BuildKbMarkdown"/>.</summary>
    public static string KbBody(string markdown)
    {
        var lines = NormalizeNewlines(markdown).Split('\n');
        var i = 0;
        while (i < lines.Length && (lines[i].Trim().Length == 0
               || lines[i].StartsWith(KbHeaderPrefix, StringComparison.Ordinal)
               || lines[i].StartsWith(KbCreatedPrefix, StringComparison.Ordinal)
               || lines[i].StartsWith(KbExportedPrefix, StringComparison.Ordinal)))
            i++;
        return string.Join("\n", lines.Skip(i)).Trim();
    }

    public static List<KbNote> LoadKbNotes(string kbNotesDir)
    {
        var result = new List<KbNote>();
        if (!Directory.Exists(kbNotesDir)) return result;
        foreach (var path in Directory.EnumerateFiles(kbNotesDir, "*.md"))
        {
            var id = System.IO.Path.GetFileNameWithoutExtension(path);
            if (!TryParseId(id, out var created)) continue;
            string[] lines;
            try { lines = File.ReadLines(path, Encoding.UTF8).Take(8).ToArray(); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            var title = "(ohne Titel)";
            var exported = File.GetLastWriteTime(path);
            foreach (var line in lines)
            {
                if (line.StartsWith(KbHeaderPrefix, StringComparison.Ordinal))
                    title = line[KbHeaderPrefix.Length..].Trim();
                else if (line.StartsWith(KbExportedPrefix, StringComparison.Ordinal)
                    && DateTime.TryParseExact(line[KbExportedPrefix.Length..].Trim(), DateTimeFormat,
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    exported = parsed;
            }
            result.Add(new KbNote(id, path, title, created, exported));
        }
        return result.OrderByDescending(n => n.Created).ToList();
    }

    private static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
}

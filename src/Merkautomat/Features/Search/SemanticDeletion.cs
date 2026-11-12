using System.IO;

namespace Merkautomat.Services;

public sealed record SemanticDeleteTarget(string Key, SemanticSource Source, string Title, long EntryId, IReadOnlyList<string> Paths);
public sealed record SemanticDeletePlan(IReadOnlyList<SemanticDeleteTarget> Targets, int SkippedRecordings);

public static class SemanticDeletion
{
    /// <param name="keep">Entries that must survive, e.g. the kept copy of a duplicate group. A target that would also
    /// remove one of them (same history entry, note or recording file) is left out.</param>
    public static SemanticDeletePlan Plan(IEnumerable<SemanticChunk> chunks, string notesDir, string kbNotesDir, string recordingsDir, string? currentRecording,
        IEnumerable<SemanticChunk>? keep = null)
    {
        if (keep != null)
        {
            var kept = Plan(keep, notesDir, kbNotesDir, recordingsDir, null).Targets.Select(t => t.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var all = Plan(chunks, notesDir, kbNotesDir, recordingsDir, currentRecording);
            return new(all.Targets.Where(t => !kept.Contains(t.Key)).ToList(), all.SkippedRecordings);
        }
        var targets = new Dictionary<string, SemanticDeleteTarget>(StringComparer.OrdinalIgnoreCase);
        var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static bool InDirectory(string file, string directory)
            => string.Equals(Path.GetDirectoryName(Path.GetFullPath(file)), Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        foreach (var chunk in chunks)
        {
            if (chunk.Source == SemanticSource.KnowledgeFolder) continue;
            if (chunk.Source == SemanticSource.Clipboard)
            {
                if (chunk.EntryId <= 0) continue;
                var key = "c:" + chunk.EntryId;
                targets.TryAdd(key, new(key, chunk.Source, chunk.Title, chunk.EntryId, []));
                continue;
            }
            if (chunk.FilePath == null || !string.Equals(Path.GetExtension(chunk.FilePath), ".md", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Die Quelle hat keinen gültigen Markdown-Dateipfad.");
            var path = Path.GetFullPath(chunk.FilePath);
            if (chunk.Source == SemanticSource.Recording)
            {
                if (!InDirectory(path, recordingsDir)) throw new ArgumentException("Die Aufnahme liegt außerhalb des Aufnahmeordners.");
                if (currentRecording != null && string.Equals(path, Path.GetFullPath(currentRecording), StringComparison.OrdinalIgnoreCase)) { skipped.Add(path); continue; }
                targets.TryAdd(path, new(path, chunk.Source, chunk.Title, 0, [path]));
            }
            else
            {
                if (!InDirectory(path, notesDir) && !InDirectory(path, kbNotesDir)) throw new ArgumentException("Die Sprachnotiz liegt außerhalb der Notizordner.");
                var id = Path.GetFileNameWithoutExtension(path);
                if (!NoteStore.TryParseId(id, out _)) throw new ArgumentException("Ungültige Sprachnotiz-ID.");
                var original = Path.Combine(notesDir, id + ".md");
                var exported = Path.Combine(kbNotesDir, id + ".md");
                targets.TryAdd("n:" + id + ".md", new("n:" + id + ".md", chunk.Source, chunk.Title, 0,
                    [original, exported, original + ".bak", exported + ".bak"]));
            }
        }
        return new(targets.Values.ToList(), skipped.Count);
    }
}

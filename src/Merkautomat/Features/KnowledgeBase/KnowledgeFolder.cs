using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Merkautomat.Services;

public sealed record KnowledgeFile(string Id, string RelativePath, string Text, string Hash, DateTime Modified)
{
    public string ItemKey => "f:" + Id;
}

public sealed record KnowledgeFolderState(string Root, KnowledgeFile[] Files);

/// <summary>A read-only external source with an encrypted last-known snapshot.</summary>
public sealed class KnowledgeFolder
{
    public const string FileName = "knowledge-folder.json";
    private readonly string _cachePath;
    private readonly string[] _excluded;
    private KnowledgeFolderState _state = new("", []);
    private bool _loaded;

    public KnowledgeFolder(string cachePath, params string[] excluded)
    {
        _cachePath = cachePath;
        _excluded = excluded.Select(Path.GetFullPath).ToArray();
    }

    public KnowledgeFolderState Snapshot => Volatile.Read(ref _state);
    public string Status { get; private set; } = "Noch nicht abgeglichen.";

    public static bool IsWithin(string path, string root)
        => string.Equals(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase)
           || path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public bool Scan(string root, bool enabled, string extensions, int maxKb, CancellationToken ct)
    {
        var restored = false;
        if (!_loaded)
        {
            if (File.Exists(_cachePath))
            {
                var saved = JsonSerializer.Deserialize<KnowledgeFolderState>(SecureFile.ReadAllText(_cachePath));
                if (saved == null || saved.Files == null) throw new InvalidDataException("Der Knowledgeordner-Speicher ist ungültig.");
                Volatile.Write(ref _state, saved);
                restored = saved.Files.Length > 0;
            }
            _loaded = true;
        }
        root = string.IsNullOrWhiteSpace(root) ? "" : Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var old = Snapshot;
        var rootChanged = !string.Equals(root, old.Root, StringComparison.OrdinalIgnoreCase);
        if (rootChanged)
        {
            Publish(new(root, []));
            old = Snapshot;
        }
        if (root.Length == 0 || !enabled)
        {
            Status = root.Length == 0 ? "Kein Knowledgeordner ausgewählt." : $"Synchronisierung pausiert · {old.Files.Length} Dateien im letzten Stand.";
            return rootChanged || restored;
        }
        if (_excluded.Any(p => IsWithin(root, p)))
        {
            Status = "Der Daten- oder Modellordner kann nicht als Knowledgeordner verwendet werden.";
            return rootChanged || restored;
        }

        var allowed = FileContentReader.ParseExtensions(extensions);
        var previous = old.Files.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);
        var found = new Dictionary<string, KnowledgeFile>(StringComparer.OrdinalIgnoreCase);
        var protectedPaths = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        var skipped = 0;
        var errors = 0;
        while (pending.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Materialize first: a failed enumeration must not look like confirmed deletions.
                var paths = Directory.GetFileSystemEntries(directory);
                foreach (var path in paths)
                {
                    ct.ThrowIfCancellationRequested();
                    var relative = Path.GetRelativePath(root, path);
                    try
                    {
                        var attributes = File.GetAttributes(path);
                        if ((attributes & FileAttributes.ReparsePoint) != 0 || _excluded.Any(p => IsWithin(path, p)))
                        {
                            skipped++;
                            continue;
                        }
                        if ((attributes & FileAttributes.Directory) != 0) { pending.Push(path); continue; }
                        if (!FileContentReader.IsReadable(path, allowed)) { skipped++; continue; }
                        var info = new FileInfo(path);
                        var stamp = info.LastWriteTimeUtc;
                        var limit = (long)Math.Clamp(maxKb, FileContentReader.MinMaxKb, FileContentReader.MaxMaxKb) * 1024;
                        if (info.Length == 0 || info.Length > limit) { skipped++; continue; }
                        byte[] bytes;
                        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        {
                            if (stream.Length > limit) { skipped++; continue; }
                            bytes = new byte[checked((int)stream.Length)];
                            stream.ReadExactly(bytes);
                            if (stream.ReadByte() != -1) throw new IOException("Die Datei wurde während des Lesens geändert.");
                        }
                        if (File.GetLastWriteTimeUtc(path) != stamp) throw new IOException("Die Datei wurde während des Lesens geändert.");
                        if (FileContentReader.LooksBinary(bytes)) { skipped++; continue; }
                        var text = NoteStore.DecodeImport(bytes).Replace("\r\n", "\n").Replace('\r', '\n').Trim();
                        if (text.Length == 0) { skipped++; continue; }
                        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
                        var id = previous.TryGetValue(relative, out var existing) ? existing.Id : Guid.NewGuid().ToString("N");
                        found[relative] = new(id, relative, text, hash, stamp.ToLocalTime());
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        errors++;
                        protectedPaths.Add(path);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors++;
                protectedPaths.Add(directory);
            }
        }
        // Keep unavailable files/subtrees. A disconnected drive must never remove the whole snapshot.
        foreach (var file in old.Files)
        {
            var path = Path.Combine(root, file.RelativePath);
            if (!found.ContainsKey(file.RelativePath) && FileContentReader.IsReadable(path, allowed)
                && protectedPaths.Any(p => IsWithin(path, p))) found[file.RelativePath] = file;
        }
        // Only unambiguous moves preserve identity; identical copies remain separate documents.
        var missing = old.Files.Where(f => !found.ContainsKey(f.RelativePath)).GroupBy(f => f.Hash)
            .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
        foreach (var group in found.Values.Where(f => !previous.ContainsKey(f.RelativePath)).GroupBy(f => f.Hash).ToArray())
            if (group.Count() == 1 && old.Files.Count(f => f.Hash == group.Key) == 1
                && found.Values.Count(f => f.Hash == group.Key) == 1 && missing.TryGetValue(group.Key, out var moved))
            {
                var file = group.Single();
                found[file.RelativePath] = file with { Id = moved.Id };
            }
        var files = found.Values.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
        var changed = !old.Files.SequenceEqual(files);
        ct.ThrowIfCancellationRequested();
        if (changed) Publish(new(root, files));
        Status = $"{files.Length} Dateien · {skipped} übersprungen · {errors} Lesefehler · geprüft {DateTime.Now:dd.MM. HH:mm:ss}";
        return rootChanged || changed || restored;
    }

    private void Publish(KnowledgeFolderState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        SecureFile.WriteAllTextAtomic(_cachePath, JsonSerializer.Serialize(state));
        Volatile.Write(ref _state, state);
    }

    public IReadOnlyList<SemanticChunk> Chunks()
    {
        var state = Snapshot;
        return state.Files.SelectMany(file => SemanticChunker.Split(file.Text, SemanticChunker.MaxChunkChars)
            .Where(text => SemanticChunker.IsWorthIndexing(text, allowSingleWord: false)).Select((text, index) => new SemanticChunk(
                $"{file.ItemKey}:{index}", file.ItemKey, SemanticSource.KnowledgeFolder,
                file.RelativePath, file.Modified, text, FilePath: Path.Combine(state.Root, file.RelativePath),
                Metadata: new() { SourcePath = Path.Combine(state.Root, file.RelativePath), CaptureMethod = "knowledge-folder", ContentType = "text" }))).ToArray();
    }
}

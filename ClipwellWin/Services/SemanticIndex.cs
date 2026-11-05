using System.IO;
using System.Security.Cryptography;
using System.Text;
using ClipwellWin.Models;
using Microsoft.Data.Sqlite;

namespace ClipwellWin.Services;

/// <summary>
/// Vector index for the KB semantic search. Only keys, content hashes and vectors are stored (semantic.db),
/// never the text itself; texts come from the live sources on every sync.
/// </summary>
public sealed class SemanticIndex : IDisposable
{
    public const string FileName = "semantic.db";
    private const int BatchSize = 16;

    private readonly string _dbPath;
    private readonly string _modelId;
    private readonly Func<ITextEmbedder> _createEmbedder;
    private readonly SemaphoreSlim _modelLock = new(1, 1);
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly object _gate = new();
    private readonly Dictionary<string, (long Stamp, List<SemanticChunk> Chunks)> _fileCache = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, (string Hash, float[] Vector)>? _stored;
    private IndexedChunk[] _index = [];
    private ITextEmbedder? _embedder;
    private SqliteConnection? _conn;

    public SemanticIndex(string dbPath, string modelId, Func<ITextEmbedder> createEmbedder)
    {
        _dbPath = dbPath;
        _modelId = modelId;
        _createEmbedder = createEmbedder;
    }

    /// <summary>Raised on a worker thread while a sync runs and when it ends.</summary>
    public event EventHandler? Changed;

    public bool IsSyncing { get; private set; }
    public int PendingTotal { get; private set; }
    public int PendingDone { get; private set; }
    public int Count => Snapshot().Count;

    public IReadOnlyList<IndexedChunk> Snapshot()
    {
        lock (_gate) return _index;
    }

    // ---- Sources ---------------------------------------------------------------------------------------------

    /// <summary>Chunks from the clipboard history, the KB notes and the recordings. Files are cached by write time.</summary>
    public List<SemanticChunk> Collect(IEnumerable<ClipboardEntry> entries, string kbNotesDir, string recordingsDir)
    {
        var chunks = SemanticChunker.FromClipboard(entries).ToList();
        lock (_fileCache)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var note in SafeLoadNotes(kbNotesDir))
            {
                seen.Add(note.Path);
                chunks.AddRange(FromFile(note.Path, text => SemanticChunker.FromNote(note.Path, text, note.Title, note.Exported)));
            }
            if (Directory.Exists(recordingsDir))
            {
                foreach (var path in Directory.EnumerateFiles(recordingsDir, "*.md"))
                {
                    seen.Add(path);
                    chunks.AddRange(FromFile(path, text => SemanticChunker.FromRecording(path, text)));
                }
            }
            foreach (var gone in _fileCache.Keys.Where(k => !seen.Contains(k)).ToList()) _fileCache.Remove(gone);
        }
        return chunks;
    }

    private static List<KbNote> SafeLoadNotes(string dir)
    {
        try { return NoteStore.LoadKbNotes(dir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private List<SemanticChunk> FromFile(string path, Func<string, IEnumerable<SemanticChunk>> build)
    {
        try
        {
            var info = new FileInfo(path);
            var stamp = info.LastWriteTimeUtc.Ticks ^ (info.Length << 1);
            if (_fileCache.TryGetValue(path, out var cached) && cached.Stamp == stamp) return cached.Chunks;
            var chunks = build(RecordingMarkdown.ReadShared(path)).ToList();
            _fileCache[path] = (stamp, chunks);
            return chunks;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    // ---- Sync ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Brings the index in line with <paramref name="chunks"/>: reuses stored vectors with matching hash, embeds
    /// the rest (newest first, searchable batch by batch) and drops vectors of vanished sources.
    /// </summary>
    public async Task SyncAsync(IReadOnlyList<SemanticChunk> chunks, CancellationToken ct = default)
    {
        await _syncLock.WaitAsync(ct);
        try
        {
            IsSyncing = true;
            Changed?.Invoke(this, EventArgs.Empty);
            await Task.Run(() => Sync(chunks, ct), ct);
        }
        finally
        {
            IsSyncing = false;
            PendingTotal = PendingDone = 0;
            _syncLock.Release();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Sync(IReadOnlyList<SemanticChunk> chunks, CancellationToken ct)
    {
        var stored = EnsureStore();
        var unique = new Dictionary<string, SemanticChunk>();
        foreach (var c in chunks) unique.TryAdd(c.Key, c);

        var ready = new List<IndexedChunk>(unique.Count);
        var pending = new List<(SemanticChunk Chunk, string Hash)>();
        foreach (var chunk in unique.Values)
        {
            var hash = HashOf(chunk.Text);
            if (stored.TryGetValue(chunk.Key, out var row) && row.Hash == hash) ready.Add(new IndexedChunk(chunk, row.Vector));
            else pending.Add((chunk, hash));
        }

        var stale = stored.Keys.Where(k => !unique.ContainsKey(k)).ToList();
        if (stale.Count > 0)
        {
            using var tx = _conn!.BeginTransaction();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Vectors WHERE Key = $k";
            var p = cmd.Parameters.Add("$k", SqliteType.Text);
            foreach (var key in stale)
            {
                p.Value = key;
                cmd.ExecuteNonQuery();
                stored.Remove(key);
            }
            tx.Commit();
        }

        Publish(ready);
        PendingTotal = pending.Count;
        PendingDone = 0;
        if (pending.Count == 0) return;
        Changed?.Invoke(this, EventArgs.Empty);

        foreach (var batch in pending.OrderByDescending(p => p.Chunk.Date).Chunk(BatchSize))
        {
            ct.ThrowIfCancellationRequested();
            var vectors = WithModel(m => m.EmbedPassages(batch.Select(b => b.Chunk.Text).ToList()));

            using (var tx = _conn!.BeginTransaction())
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "INSERT OR REPLACE INTO Vectors (Key, Hash, Model, Vector) VALUES ($k, $h, $m, $v)";
                var pk = cmd.Parameters.Add("$k", SqliteType.Text);
                var ph = cmd.Parameters.Add("$h", SqliteType.Text);
                cmd.Parameters.AddWithValue("$m", _modelId);
                var pv = cmd.Parameters.Add("$v", SqliteType.Blob);
                for (var i = 0; i < batch.Length; i++)
                {
                    pk.Value = batch[i].Chunk.Key;
                    ph.Value = batch[i].Hash;
                    pv.Value = ToBytes(vectors[i]);
                    cmd.ExecuteNonQuery();
                    stored[batch[i].Chunk.Key] = (batch[i].Hash, vectors[i]);
                    ready.Add(new IndexedChunk(batch[i].Chunk, vectors[i]));
                }
                tx.Commit();
            }

            PendingDone += batch.Length;
            Publish(ready);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Publish(List<IndexedChunk> ready)
    {
        var snapshot = ready.ToArray();
        lock (_gate) _index = snapshot;
    }

    private string HashOf(string text)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(_modelId + "\n" + text)))[..32];

    private Dictionary<string, (string Hash, float[] Vector)> EnsureStore()
    {
        if (_stored != null) return _stored;
        OpenConnection();
        var stored = new Dictionary<string, (string, float[])>();
        using (var cmd = _conn!.CreateCommand())
        {
            cmd.CommandText = "SELECT Key, Hash, Vector FROM Vectors WHERE Model = $m";
            cmd.Parameters.AddWithValue("$m", _modelId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) stored[reader.GetString(0)] = (reader.GetString(1), FromBytes((byte[])reader[2]));
        }
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM Vectors WHERE Model <> $m";
            cmd.Parameters.AddWithValue("$m", _modelId);
            cmd.ExecuteNonQuery();
        }
        return _stored = stored;
    }

    private void OpenConnection()
    {
        if (_conn != null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        _conn = new SqliteConnection($"Data Source={_dbPath};Default Timeout=5;Pooling=False");
        _conn.Open();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"PRAGMA journal_mode=WAL;
                            PRAGMA secure_delete=ON;
                            CREATE TABLE IF NOT EXISTS Vectors (
                                Key    TEXT PRIMARY KEY,
                                Hash   TEXT NOT NULL,
                                Model  TEXT NOT NULL,
                                Vector BLOB NOT NULL
                            );";
        cmd.ExecuteNonQuery();
    }

    /// <summary>Removes vectors of clipboard entries that no longer exist. Cheap: reads keys only.</summary>
    public void PruneClipboard(IReadOnlySet<long> liveEntryIds)
    {
        if (!File.Exists(_dbPath)) return;
        _syncLock.Wait();
        try
        {
            OpenConnection();
            var dead = new HashSet<string>();
            using (var cmd = _conn!.CreateCommand())
            {
                cmd.CommandText = "SELECT Key FROM Vectors WHERE Key LIKE 'c:%'";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var key = reader.GetString(0);
                    var end = key.IndexOf(':', 2);
                    if (end < 0 || !long.TryParse(key.AsSpan(2, end - 2), out var id) || !liveEntryIds.Contains(id)) dead.Add(key);
                }
            }
            if (dead.Count == 0) return;
            using var tx = _conn.BeginTransaction();
            using var del = _conn.CreateCommand();
            del.CommandText = "DELETE FROM Vectors WHERE Key = $k";
            var p = del.Parameters.Add("$k", SqliteType.Text);
            foreach (var key in dead)
            {
                p.Value = key;
                del.ExecuteNonQuery();
                _stored?.Remove(key);
            }
            tx.Commit();
            lock (_gate) _index = _index.Where(c => !dead.Contains(c.Chunk.Key)).ToArray();
        }
        finally { _syncLock.Release(); }
    }

    /// <summary>Deletes every stored vector. The next sync rebuilds the index.</summary>
    public async Task ClearAsync()
    {
        await _syncLock.WaitAsync();
        try
        {
            OpenConnection();
            using (var cmd = _conn!.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM Vectors; VACUUM;";
                cmd.ExecuteNonQuery();
            }
            _stored = new Dictionary<string, (string, float[])>();
            lock (_gate) _index = [];
        }
        finally { _syncLock.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---- Queries ---------------------------------------------------------------------------------------------

    public List<SemanticHit> Search(string query, SemanticSource? source, int limit = 40)
    {
        var chunks = Filter(source);
        if (chunks.Count == 0 || query.Trim().Length == 0) return [];
        var vector = WithModel(m => m.EmbedQuery(query.Trim()));
        return SemanticRanking.Search(vector, query, chunks, limit);
    }

    public List<SemanticHit> Similar(string itemKey, SemanticSource? source, int limit = 30)
    {
        var origin = Snapshot().FirstOrDefault(c => c.Chunk.ItemKey == itemKey);
        return origin == null ? [] : SemanticRanking.Similar(origin, Filter(source), limit);
    }

    public List<SemanticHit> Duplicates(SemanticSource? source) => SemanticRanking.DuplicateGroups(Filter(source));

    public List<SemanticHit> Browse(SemanticSource? source) => SemanticRanking.Browse(Filter(source));

    /// <summary>Number of indexed items (not chunks) per source.</summary>
    public Dictionary<SemanticSource, int> ItemCounts()
        => Snapshot()
            .Select(c => c.Chunk)
            .DistinctBy(c => c.ItemKey)
            .GroupBy(c => c.Source)
            .ToDictionary(g => g.Key, g => g.Count());

    private IReadOnlyList<IndexedChunk> Filter(SemanticSource? source)
    {
        var all = Snapshot();
        return source == null ? all : all.Where(c => c.Chunk.Source == source).ToList();
    }

    private T WithModel<T>(Func<ITextEmbedder, T> action)
    {
        _modelLock.Wait();
        try { return action(_embedder ??= _createEmbedder()); }
        finally { _modelLock.Release(); }
    }

    /// <summary>Frees the model memory; it is loaded again on the next query or sync.</summary>
    public void ReleaseModel()
    {
        _modelLock.Wait();
        try
        {
            (_embedder as IDisposable)?.Dispose();
            _embedder = null;
        }
        finally { _modelLock.Release(); }
    }

    private static byte[] ToBytes(float[] v)
    {
        var bytes = new byte[v.Length * sizeof(float)];
        Buffer.BlockCopy(v, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] FromBytes(byte[] bytes)
    {
        var v = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, v, 0, v.Length * sizeof(float));
        return v;
    }

    public void Dispose()
    {
        ReleaseModel();
        _conn?.Dispose();
        _conn = null;
    }
}

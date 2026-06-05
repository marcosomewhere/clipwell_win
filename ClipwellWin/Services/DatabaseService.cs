using System.IO;
using System.Text.Json;
using ClipwellWin.Models;
using Microsoft.Data.Sqlite;

namespace ClipwellWin.Services;

public class DatabaseService : IDisposable
{
    public static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "Clipwell");

    public static readonly string DbPath = Path.Combine(DataDir, "history.db");

    private readonly SqliteConnection _conn;
    private readonly string _dbPath;

    public DatabaseService(string? dbPath = null)
    {
        _dbPath = dbPath ?? DbPath;
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        _conn = new SqliteConnection($"Data Source={_dbPath};Default Timeout=2");
        _conn.Open();
        InitSchema();
        VerifyDatabase();
    }

    private void InitSchema()
    {
        Exec(@"PRAGMA journal_mode=WAL;
               CREATE TABLE IF NOT EXISTS History (
                   Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                   Type        INTEGER NOT NULL,
                   Content     TEXT,
                   ImageData   BLOB,
                   OcrText     TEXT,
                   Language    TEXT,
                   UrlTitle    TEXT,
                   UrlFavicon  BLOB,
                   HexColor    TEXT,
                   ContentKind TEXT,
                   DetectionReason TEXT,
                   IsPinned    INTEGER NOT NULL DEFAULT 0,
                   Timestamp   TEXT NOT NULL
               );
               CREATE TABLE IF NOT EXISTS UrlCache (
                   Url         TEXT PRIMARY KEY,
                   Title       TEXT,
                   Favicon     BLOB,
                   CachedAt    TEXT NOT NULL
               );");
        EnsureColumn("History", "UrlTitle", "TEXT");
        EnsureColumn("History", "UrlFavicon", "BLOB");
        EnsureColumn("History", "ContentKind", "TEXT");
        EnsureColumn("History", "DetectionReason", "TEXT");
    }

    private void VerifyDatabase()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "PRAGMA quick_check";
        var result = cmd.ExecuteScalar()?.ToString();
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            throw new SqliteException($"SQLite quick_check failed: {result}", 11);
    }

    private void EnsureColumn(string table, string column, string type)
    {
        using var info = _conn.CreateCommand();
        info.CommandText = $"PRAGMA table_info({table})";
        using var reader = info.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return;
        }

        Exec($"ALTER TABLE {table} ADD COLUMN {column} {type}");
    }

    public long Insert(ClipboardEntry e)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO History
            (Type, Content, ImageData, OcrText, Language, UrlTitle, UrlFavicon, HexColor, ContentKind, DetectionReason, IsPinned, Timestamp)
            VALUES ($type,$content,$img,$ocr,$lang,$urlTitle,$urlFavicon,$hex,$kind,$reason,$pin,$ts);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$type", (int)e.Type);
        cmd.Parameters.AddWithValue("$content", (object?)e.Content ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$img", (object?)e.ImageData ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ocr", (object?)e.OcrText ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lang", (object?)e.Language ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$urlTitle", (object?)e.UrlTitle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$urlFavicon", (object?)e.UrlFavicon ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$hex", (object?)e.HexColor ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$kind", (object?)e.ContentKind ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$reason", (object?)e.DetectionReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pin", e.IsPinned ? 1 : 0);
        cmd.Parameters.AddWithValue("$ts", e.Timestamp.ToString("o"));
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    public void UpdateOcr(long id, string ocrText)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE History SET OcrText=$ocr WHERE Id=$id";
        cmd.Parameters.AddWithValue("$ocr", ocrText);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void SetPinned(long id, bool pinned)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE History SET IsPinned=$p WHERE Id=$id";
        cmd.Parameters.AddWithValue("$p", pinned ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void SetType(long id, EntryType type, string? language, string? reason)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE History SET Type=$type, Language=$language, ContentKind=$kind, DetectionReason=$reason WHERE Id=$id";
        cmd.Parameters.AddWithValue("$type", (int)type);
        cmd.Parameters.AddWithValue("$language", (object?)language ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$kind", (object?)language ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$reason", (object?)reason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM History WHERE Id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void Purge(int maxItems)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"DELETE FROM History
            WHERE IsPinned=0
              AND Id NOT IN (
                  SELECT Id FROM History WHERE IsPinned=0
                  ORDER BY Timestamp DESC LIMIT $max)";
        cmd.Parameters.AddWithValue("$max", maxItems);
        cmd.ExecuteNonQuery();
    }

    public List<ClipboardEntry> LoadAll()
    {
        var list = new List<ClipboardEntry>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM History ORDER BY IsPinned DESC, Timestamp DESC";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadEntry(reader));
        return list;
    }

    private static ClipboardEntry ReadEntry(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("Id")),
        Type = (EntryType)r.GetInt32(r.GetOrdinal("Type")),
        Content = r.IsDBNull(r.GetOrdinal("Content")) ? null : r.GetString(r.GetOrdinal("Content")),
        ImageData = r.IsDBNull(r.GetOrdinal("ImageData")) ? null : (byte[])r["ImageData"],
        OcrText = r.IsDBNull(r.GetOrdinal("OcrText")) ? null : r.GetString(r.GetOrdinal("OcrText")),
        Language = r.IsDBNull(r.GetOrdinal("Language")) ? null : r.GetString(r.GetOrdinal("Language")),
        UrlTitle = r.IsDBNull(r.GetOrdinal("UrlTitle")) ? null : r.GetString(r.GetOrdinal("UrlTitle")),
        UrlFavicon = r.IsDBNull(r.GetOrdinal("UrlFavicon")) ? null : (byte[])r["UrlFavicon"],
        HexColor = r.IsDBNull(r.GetOrdinal("HexColor")) ? null : r.GetString(r.GetOrdinal("HexColor")),
        ContentKind = r.IsDBNull(r.GetOrdinal("ContentKind")) ? null : r.GetString(r.GetOrdinal("ContentKind")),
        DetectionReason = r.IsDBNull(r.GetOrdinal("DetectionReason")) ? null : r.GetString(r.GetOrdinal("DetectionReason")),
        IsPinned = r.GetInt32(r.GetOrdinal("IsPinned")) == 1,
        Timestamp = DateTime.Parse(r.GetString(r.GetOrdinal("Timestamp"))),
    };

    public (string? title, byte[]? favicon) GetUrlCache(string url)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"SELECT Title, Favicon FROM UrlCache
                            WHERE Url=$url AND CachedAt > $ttl";
        cmd.Parameters.AddWithValue("$url", url);
        cmd.Parameters.AddWithValue("$ttl", DateTime.UtcNow.AddDays(-7).ToString("o"));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return (null, null);
        var title = r.IsDBNull(0) ? null : r.GetString(0);
        var fav = r.IsDBNull(1) ? null : (byte[])r["Favicon"];
        return (title, fav);
    }

    public void UpsertUrlCache(string url, string? title, byte[]? favicon)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"INSERT OR REPLACE INTO UrlCache (Url, Title, Favicon, CachedAt)
                            VALUES ($url,$title,$fav,$ts)";
        cmd.Parameters.AddWithValue("$url", url);
        cmd.Parameters.AddWithValue("$title", (object?)title ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fav", (object?)favicon ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public void UpdateUrlMetadata(long entryId, string? title, byte[]? favicon)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE History SET UrlTitle=$title, UrlFavicon=$favicon WHERE Id=$id AND Type=2";
        cmd.Parameters.AddWithValue("$title", (object?)title ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$favicon", (object?)favicon ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", entryId);
        cmd.ExecuteNonQuery();
    }

    public void PurgeByAge(int maxAgeInDays)
    {
        if (maxAgeInDays <= 0) return;
        var cutoff = DateTime.Now.AddDays(-maxAgeInDays).ToString("o");
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM History WHERE IsPinned=0 AND Timestamp < $cutoff";
        cmd.Parameters.AddWithValue("$cutoff", cutoff);
        cmd.ExecuteNonQuery();
    }

    public long GetDbSizeInBytes()
    {
        try { return new FileInfo(_dbPath).Length; }
        catch { return 0; }
    }

    public void PurgeBySizeIfNeeded(int maxSizeInMb)
    {
        if (maxSizeInMb <= 0) return;
        long maxBytes = (long)maxSizeInMb * 1024 * 1024;
        if (GetDbSizeInBytes() <= maxBytes) return;

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"DELETE FROM History WHERE IsPinned=0 AND Id IN (
            SELECT Id FROM History WHERE IsPinned=0 ORDER BY Timestamp ASC
            LIMIT MAX(1, (SELECT COUNT(*) / 10 FROM History WHERE IsPinned=0))
        )";
        cmd.ExecuteNonQuery();
    }

    public void ClearHistory(bool keepPinned = false)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = keepPinned
            ? "DELETE FROM History WHERE IsPinned=0"
            : "DELETE FROM History";
        cmd.ExecuteNonQuery();
    }

    public void ClearUrlCache()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM UrlCache";
        cmd.ExecuteNonQuery();
    }

    public void ExportAsJson(string filePath)
    {
        var entries = LoadAll();
        var exportData = entries.Select(e => new
        {
            e.Id,
            Type = e.Type.ToString(),
            e.Content,
            e.OcrText,
            e.Language,
            e.UrlTitle,
            e.HexColor,
            e.ContentKind,
            e.DetectionReason,
            e.IsPinned,
            Timestamp = e.Timestamp.ToString("o"),
            ImageDataBase64 = e.ImageData != null ? Convert.ToBase64String(e.ImageData) : null,
            FaviconBase64 = e.UrlFavicon != null ? Convert.ToBase64String(e.UrlFavicon) : null,
        }).ToList();

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(filePath, JsonSerializer.Serialize(exportData, options));
    }

    public void ExportAsSqliteCopy(string filePath)
    {
        // WAL-Checkpoint erzwingen, dann Datei kopieren
        Exec("PRAGMA wal_checkpoint(TRUNCATE)");
        File.Copy(_dbPath, filePath, overwrite: true);
    }

    private bool EntryExists(DateTime timestamp, string? content)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM History WHERE Timestamp=$ts AND Content IS $content";
        cmd.Parameters.AddWithValue("$ts", timestamp.ToString("o"));
        cmd.Parameters.AddWithValue("$content", (object?)content ?? DBNull.Value);
        return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
    }

    public void SecureDelete(long id)
    {
        Delete(id);
        Exec("VACUUM");
    }

    public int ImportFromJson(string filePath)
    {
        var json = File.ReadAllText(filePath);
        var items = JsonSerializer.Deserialize<List<JsonElement>>(json);
        if (items == null) return 0;

        int count = 0;
        foreach (var item in items)
        {
            try
            {
                string typeStr = item.GetProperty("Type").GetString()!;
                var ts = item.TryGetProperty("Timestamp", out var tsProp)
                    ? DateTime.Parse(tsProp.GetString()!)
                    : DateTime.Now;
                var content = TryGetStr(item, "Content");

                // Duplikat-Erkennung via Timestamp + Content
                if (EntryExists(ts, content)) continue;

                var entry = new ClipboardEntry
                {
                    Type = Enum.Parse<EntryType>(typeStr, ignoreCase: true),
                    Content       = content,
                    OcrText       = TryGetStr(item, "OcrText"),
                    Language      = TryGetStr(item, "Language"),
                    UrlTitle      = TryGetStr(item, "UrlTitle"),
                    HexColor      = TryGetStr(item, "HexColor"),
                    ContentKind   = TryGetStr(item, "ContentKind"),
                    DetectionReason = TryGetStr(item, "DetectionReason"),
                    IsPinned      = item.TryGetProperty("IsPinned", out var ip) && ip.GetBoolean(),
                    Timestamp     = ts,
                    ImageData     = TryGetBytes(item, "ImageDataBase64"),
                    UrlFavicon    = TryGetBytes(item, "FaviconBase64"),
                };
                Insert(entry);
                count++;
            }
            catch { /* defekten Eintrag überspringen */ }
        }
        return count;

        static string? TryGetStr(JsonElement el, string key)
            => el.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null
                ? v.GetString() : null;

        static byte[]? TryGetBytes(JsonElement el, string key)
        {
            if (!el.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null) return null;
            try { return Convert.FromBase64String(v.GetString()!); }
            catch { return null; }
        }
    }

    public void AutoBackupIfNeeded(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, $"clipwell-autobackup-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        ExportAsJson(target);

        var old = Directory.GetFiles(directory, "clipwell-autobackup-*.json")
            .OrderByDescending(f => f)
            .Skip(7);
        foreach (var f in old)
            try { File.Delete(f); } catch { }
    }

    public static void BackupAndReset()
    {
        Directory.CreateDirectory(DataDir);
        var suffix = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        foreach (var path in new[] { DbPath, $"{DbPath}-wal", $"{DbPath}-shm" })
        {
            if (!File.Exists(path)) continue;
            var backupPath = $"{path}.broken-{suffix}";
            File.Move(path, backupPath, overwrite: true);
        }
    }

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _conn.Dispose();
        SqliteConnection.ClearPool(_conn);
    }
}

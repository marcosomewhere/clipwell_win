using System.IO;
using System.Globalization;
using System.Text.Json;
using ClipwellWin.Models;
using Microsoft.Data.Sqlite;

namespace ClipwellWin.Services;

public class DatabaseService : IDisposable
{
    public static readonly string DataDir = AppPaths.DataDir;

    public static readonly string DbPath = Path.Combine(DataDir, "history.db");

    private readonly SqliteConnection _conn;
    private readonly string _dbPath;
    private bool _ftsAvailable;

    // SqliteConnection ist nicht thread-safe; alle Zugriffe über dieses Gate serialisieren. Monitor (lock) ist reentrant.
    private readonly object _gate = new();

    public DatabaseService(string? dbPath = null)
    {
        _dbPath = dbPath ?? DbPath;
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        _conn = new SqliteConnection($"Data Source={_dbPath};Default Timeout=5");
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
                   PinOrder    INTEGER NOT NULL DEFAULT 0,
                   Timestamp   TEXT NOT NULL
               );
               CREATE TABLE IF NOT EXISTS UrlCache (
                   Url         TEXT PRIMARY KEY,
                   Title       TEXT,
                   Favicon     BLOB,
                   CachedAt    TEXT NOT NULL
               );
               CREATE INDEX IF NOT EXISTS IX_History_Pinned_Timestamp
                   ON History (IsPinned, Timestamp);");
        EnsureColumn("History", "UrlTitle", "TEXT");
        EnsureColumn("History", "UrlFavicon", "BLOB");
        EnsureColumn("History", "ContentKind", "TEXT");
        EnsureColumn("History", "DetectionReason", "TEXT");
        EnsureColumn("History", "PinOrder", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn("History", "ThumbnailData", "BLOB");
        EnsureColumn("History", "UseCount", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn("History", "LastUsedAt", "TEXT");
        MigrateYamlTextEntries();
        InitFts();
    }

    private void InitFts()
    {
        try
        {
            DropFtsTriggers();
            Exec(@"CREATE VIRTUAL TABLE IF NOT EXISTS HistoryFts
                       USING fts5(content, ocrtext, urltitle, language, tokenize='unicode61')");
            CreateFtsTriggers();

            if (IsFtsBackfillNeeded())
                RebuildFtsIndex();

            _ftsAvailable = true;
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            _ftsAvailable = false;
            App.LogCrash(new InvalidOperationException("FTS initialization failed; search will use linear scan.", ex));
            try { DropFtsObjects(); }
            catch (Exception cleanupEx) { App.LogCrash(cleanupEx); }
        }
    }

    private void MigrateYamlTextEntries()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"UPDATE History
                            SET Type = $codeType
                            WHERE Type = $textType
                              AND UPPER(COALESCE(ContentKind, '')) = 'YAML'";
        cmd.Parameters.AddWithValue("$codeType", (int)EntryType.Code);
        cmd.Parameters.AddWithValue("$textType", (int)EntryType.Text);
        cmd.ExecuteNonQuery();
    }

    private void DropFtsTriggers()
    {
        Exec("DROP TRIGGER IF EXISTS HistoryFts_ai");
        Exec("DROP TRIGGER IF EXISTS HistoryFts_ad");
        Exec("DROP TRIGGER IF EXISTS HistoryFts_au");
    }

    private void CreateFtsTriggers()
    {
        Exec(@"CREATE TRIGGER HistoryFts_ai AFTER INSERT ON History BEGIN
                   INSERT INTO HistoryFts(rowid, content, ocrtext, urltitle, language)
                   VALUES (new.Id, new.Content, new.OcrText, new.UrlTitle, new.Language);
               END");
        Exec(@"CREATE TRIGGER HistoryFts_ad AFTER DELETE ON History BEGIN
                   DELETE FROM HistoryFts WHERE rowid = old.Id;
               END");
        Exec(@"CREATE TRIGGER HistoryFts_au AFTER UPDATE ON History BEGIN
                   DELETE FROM HistoryFts WHERE rowid = old.Id;
                   INSERT INTO HistoryFts(rowid, content, ocrtext, urltitle, language)
                   VALUES (new.Id, new.Content, new.OcrText, new.UrlTitle, new.Language);
               END");
    }

    private bool IsFtsBackfillNeeded()
    {
        var historyCount = ScalarLong("SELECT COUNT(*) FROM History");
        if (historyCount == 0) return false;

        var ftsCount = ScalarLong("SELECT COUNT(*) FROM HistoryFts");
        if (historyCount != ftsCount) return true;

        return ScalarLong(@"SELECT COUNT(*) FROM History
                            WHERE Id NOT IN (SELECT rowid FROM HistoryFts)") > 0;
    }

    private void RebuildFtsIndex()
    {
        Exec(@"DELETE FROM HistoryFts;
               INSERT INTO HistoryFts(rowid, content, ocrtext, urltitle, language)
               SELECT Id, Content, OcrText, UrlTitle, Language FROM History");
    }

    private void DropFtsObjects()
    {
        DropFtsTriggers();
        Exec("DROP TABLE IF EXISTS HistoryFts");
    }

    private long ScalarLong(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    public HashSet<long>? SearchFts(string query)
    {
        lock (_gate)
        {
            if (!_ftsAvailable) return null;

            var ids = new HashSet<long>();
            try
            {
                var terms = query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (terms.Length == 0) return null;
                var ftsQuery = string.Join(" ", terms
                    .Select(t => t.Replace("\"", "").Replace("*", "").Replace("(", "").Replace(")", ""))
                    .Where(t => t.Length > 0)
                    .Select(t => t + "*"));
                if (string.IsNullOrWhiteSpace(ftsQuery)) return null;
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = "SELECT rowid FROM HistoryFts WHERE HistoryFts MATCH $q ORDER BY rank";
                cmd.Parameters.AddWithValue("$q", ftsQuery);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    ids.Add(reader.GetInt64(0));
                return ids;
            }
            catch { return null; }
        }
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
        lock (_gate)
        {
            var pinOrder = e.IsPinned
                ? e.PinOrder > 0 ? e.PinOrder : NextPinOrder()
                : 0;
            var useCount = Math.Max(0, e.UseCount);

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO History
                (Type, Content, ImageData, ThumbnailData, OcrText, Language, UrlTitle, UrlFavicon, HexColor, ContentKind, DetectionReason, IsPinned, PinOrder, UseCount, LastUsedAt, Timestamp)
                VALUES ($type,$content,$img,$thumb,$ocr,$lang,$urlTitle,$urlFavicon,$hex,$kind,$reason,$pin,$pinOrder,$useCount,$lastUsedAt,$ts);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$type", (int)e.Type);
            cmd.Parameters.AddWithValue("$content", (object?)e.Content ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$img", (object?)e.ImageData ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$thumb", (object?)e.ThumbnailData ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ocr", (object?)e.OcrText ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$lang", (object?)e.Language ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$urlTitle", (object?)e.UrlTitle ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$urlFavicon", (object?)e.UrlFavicon ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$hex", (object?)e.HexColor ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$kind", (object?)e.ContentKind ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$reason", (object?)e.DetectionReason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$pin", e.IsPinned ? 1 : 0);
            cmd.Parameters.AddWithValue("$pinOrder", pinOrder);
            cmd.Parameters.AddWithValue("$useCount", useCount);
            cmd.Parameters.AddWithValue("$lastUsedAt", e.LastUsedAt.HasValue ? e.LastUsedAt.Value.ToString("o") : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$ts", e.Timestamp.ToString("o"));
            var id = (long)(cmd.ExecuteScalar() ?? 0L);
            e.PinOrder = pinOrder;
            e.UseCount = useCount;
            return id;
        }
    }

    public void UpdateOcr(long id, string ocrText)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE History SET OcrText=$ocr WHERE Id=$id";
            cmd.Parameters.AddWithValue("$ocr", ocrText);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public void UpdateTimestamp(long id, DateTime timestamp)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE History SET Timestamp=$ts WHERE Id=$id";
            cmd.Parameters.AddWithValue("$ts", timestamp.ToString("o"));
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public long SetPinned(long id, bool pinned)
    {
        lock (_gate)
        {
            var pinOrder = pinned ? NextPinOrder() : 0;
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE History SET IsPinned=$pin, PinOrder=$order WHERE Id=$id";
            cmd.Parameters.AddWithValue("$pin", pinned ? 1 : 0);
            cmd.Parameters.AddWithValue("$order", pinOrder);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
            return pinOrder;
        }
    }

    private long NextPinOrder()
        => ScalarLong("SELECT COALESCE(MAX(PinOrder), 0) + 1 FROM History WHERE IsPinned=1");

    public void UpdatePinOrder(long id, long pinOrder)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE History SET PinOrder=$order WHERE Id=$id";
            cmd.Parameters.AddWithValue("$order", pinOrder);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public void SetType(long id, EntryType type, string? language, string? kind, string? reason)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE History SET Type=$type, Language=$language, ContentKind=$kind, DetectionReason=$reason WHERE Id=$id";
            cmd.Parameters.AddWithValue("$type", (int)type);
            cmd.Parameters.AddWithValue("$language", (object?)language ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$kind", (object?)kind ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$reason", (object?)reason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public void UpdateContent(long id, string? content, string? kind, string? reason, string? language = null)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE History SET Content=$content, Language=$language, ContentKind=$kind, DetectionReason=$reason WHERE Id=$id";
            cmd.Parameters.AddWithValue("$content", (object?)content ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$language", (object?)language ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$kind", (object?)kind ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$reason", (object?)reason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public void Delete(long id)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM History WHERE Id=$id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public int Purge(int maxItems)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"DELETE FROM History
                WHERE IsPinned=0
                  AND Id NOT IN (
                      SELECT Id FROM History WHERE IsPinned=0
                      ORDER BY Timestamp DESC LIMIT $max)";
            cmd.Parameters.AddWithValue("$max", maxItems);
            return cmd.ExecuteNonQuery();
        }
    }

    public List<ClipboardEntry> LoadAll()
    {
        lock (_gate)
        {
            var list = new List<ClipboardEntry>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM History ORDER BY IsPinned DESC, Timestamp DESC";
            using var reader = cmd.ExecuteReader();
            var columns = HistoryColumns.From(reader, includeImageData: true);
            while (reader.Read())
                list.Add(ReadEntry(reader, columns));
            return list;
        }
    }

    // ID-only load: skips BLOBs for cheap post-purge reconciliation.
    public HashSet<long> LoadAllIds()
    {
        lock (_gate)
        {
            var ids = new HashSet<long>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT Id FROM History";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                ids.Add(reader.GetInt64(0));
            return ids;
        }
    }

    private sealed class HistoryColumns
    {
        public required int Id { get; init; }
        public required int Type { get; init; }
        public required int Content { get; init; }
        public required int ImageData { get; init; }
        public required int ThumbnailData { get; init; }
        public required int OcrText { get; init; }
        public required int Language { get; init; }
        public required int UrlTitle { get; init; }
        public required int UrlFavicon { get; init; }
        public required int HexColor { get; init; }
        public required int ContentKind { get; init; }
        public required int DetectionReason { get; init; }
        public required int IsPinned { get; init; }
        public required int Timestamp { get; init; }
        public required int PinOrder { get; init; }
        public required int UseCount { get; init; }
        public required int LastUsedAt { get; init; }

        public static HistoryColumns From(SqliteDataReader r, bool includeImageData)
            => new()
            {
                Id = r.GetOrdinal("Id"),
                Type = r.GetOrdinal("Type"),
                Content = r.GetOrdinal("Content"),
                ImageData = includeImageData ? r.GetOrdinal("ImageData") : -1,
                ThumbnailData = r.GetOrdinal("ThumbnailData"),
                OcrText = r.GetOrdinal("OcrText"),
                Language = r.GetOrdinal("Language"),
                UrlTitle = r.GetOrdinal("UrlTitle"),
                UrlFavicon = r.GetOrdinal("UrlFavicon"),
                HexColor = r.GetOrdinal("HexColor"),
                ContentKind = r.GetOrdinal("ContentKind"),
                DetectionReason = r.GetOrdinal("DetectionReason"),
                IsPinned = r.GetOrdinal("IsPinned"),
                Timestamp = r.GetOrdinal("Timestamp"),
                PinOrder = r.GetOrdinal("PinOrder"),
                UseCount = r.GetOrdinal("UseCount"),
                LastUsedAt = r.GetOrdinal("LastUsedAt"),
            };
    }

    private static ClipboardEntry ReadEntry(SqliteDataReader r, HistoryColumns col)
    {
        return new ClipboardEntry
        {
            Id              = r.GetInt64(col.Id),
            Type            = (EntryType)r.GetInt32(col.Type),
            Content         = r.IsDBNull(col.Content) ? null : r.GetString(col.Content),
            ImageData       = col.ImageData < 0 || r.IsDBNull(col.ImageData) ? null : (byte[])r[col.ImageData],
            ThumbnailData   = r.IsDBNull(col.ThumbnailData) ? null : (byte[])r[col.ThumbnailData],
            OcrText         = r.IsDBNull(col.OcrText) ? null : r.GetString(col.OcrText),
            Language        = r.IsDBNull(col.Language) ? null : r.GetString(col.Language),
            UrlTitle        = r.IsDBNull(col.UrlTitle) ? null : r.GetString(col.UrlTitle),
            UrlFavicon      = r.IsDBNull(col.UrlFavicon) ? null : (byte[])r[col.UrlFavicon],
            HexColor        = r.IsDBNull(col.HexColor) ? null : r.GetString(col.HexColor),
            ContentKind     = r.IsDBNull(col.ContentKind) ? null : r.GetString(col.ContentKind),
            DetectionReason = r.IsDBNull(col.DetectionReason) ? null : r.GetString(col.DetectionReason),
            IsPinned        = r.GetInt32(col.IsPinned) == 1,
            PinOrder        = r.GetInt64(col.PinOrder),
            UseCount        = r.IsDBNull(col.UseCount) ? 0 : r.GetInt32(col.UseCount),
            LastUsedAt      = r.IsDBNull(col.LastUsedAt) ? null : ParseDbTimestamp(r.GetString(col.LastUsedAt)),
            Timestamp       = ParseDbTimestamp(r.GetString(col.Timestamp)),
        };
    }

    public List<ClipboardEntry> LoadAllLite()
    {
        lock (_gate)
        {
            var list = new List<ClipboardEntry>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"SELECT Id, Type, Content, ThumbnailData, OcrText, Language,
                                       UrlTitle, UrlFavicon, HexColor, ContentKind, DetectionReason,
                                       IsPinned, PinOrder, Timestamp, UseCount, LastUsedAt
                                FROM History ORDER BY IsPinned DESC, Timestamp DESC";
            using var reader = cmd.ExecuteReader();
            var columns = HistoryColumns.From(reader, includeImageData: false);
            while (reader.Read())
                list.Add(ReadEntry(reader, columns));
            return list;
        }
    }

    public byte[]? LoadImageData(long id)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT ImageData FROM History WHERE Id=$id";
            cmd.Parameters.AddWithValue("$id", id);
            using var r = cmd.ExecuteReader();
            if (!r.Read() || r.IsDBNull(0)) return null;
            return (byte[])r[0];
        }
    }

    public DateTime IncrementUseCount(long id)
    {
        var usedAt = DateTime.Now;
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE History SET UseCount = UseCount + 1, LastUsedAt = $ts WHERE Id = $id";
            cmd.Parameters.AddWithValue("$ts", usedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        return usedAt;
    }

    public (string? title, byte[]? favicon) GetUrlCache(string url, int ttlDays = 7)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"SELECT Title, Favicon FROM UrlCache
                                WHERE Url=$url AND CachedAt > $ttl";
            cmd.Parameters.AddWithValue("$url", url);
            cmd.Parameters.AddWithValue("$ttl", DateTime.UtcNow.AddDays(-Math.Max(1, ttlDays)).ToString("o"));
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return (null, null);
            var title = r.IsDBNull(0) ? null : r.GetString(0);
            var fav = r.IsDBNull(1) ? null : (byte[])r["Favicon"];
            return (title, fav);
        }
    }

    public void UpsertUrlCache(string url, string? title, byte[]? favicon)
    {
        lock (_gate)
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
    }

    public void UpdateUrlMetadata(long entryId, string? title, byte[]? favicon)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE History SET UrlTitle=$title, UrlFavicon=$favicon WHERE Id=$id AND Type=2";
            cmd.Parameters.AddWithValue("$title", (object?)title ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$favicon", (object?)favicon ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$id", entryId);
            cmd.ExecuteNonQuery();
        }
    }

    public int PurgeByAge(int maxAgeInDays)
    {
        if (maxAgeInDays <= 0) return 0;
        lock (_gate)
        {
            var cutoff = DateTime.Now.AddDays(-maxAgeInDays).ToString("o");
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM History WHERE IsPinned=0 AND Timestamp < $cutoff";
            cmd.Parameters.AddWithValue("$cutoff", cutoff);
            return cmd.ExecuteNonQuery();
        }
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

        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"DELETE FROM History WHERE IsPinned=0 AND Id IN (
                SELECT Id FROM History WHERE IsPinned=0 ORDER BY Timestamp ASC
                LIMIT MAX(1, (SELECT COUNT(*) / 10 FROM History WHERE IsPinned=0))
            )";
            cmd.ExecuteNonQuery();
        }
    }

    public void ClearHistory(bool keepPinned = false)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = keepPinned
                ? "DELETE FROM History WHERE IsPinned=0"
                : "DELETE FROM History";
            cmd.ExecuteNonQuery();
        }
    }

    public void ClearUrlCache()
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM UrlCache";
            cmd.ExecuteNonQuery();
        }
    }

    public void ExportAsJson(string filePath) => ExportAsJson(LoadAll(), filePath);

    public void ExportAsJson(IEnumerable<ClipboardEntry> entries, string filePath)
    {
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
            e.PinOrder,
            e.UseCount,
            LastUsedAt = e.LastUsedAt?.ToString("o"),
            Timestamp = e.Timestamp.ToString("o"),
            ImageDataBase64 = e.ImageData != null ? Convert.ToBase64String(e.ImageData) : null,
            ThumbnailDataBase64 = e.ThumbnailData != null ? Convert.ToBase64String(e.ThumbnailData) : null,
            FaviconBase64 = e.UrlFavicon != null ? Convert.ToBase64String(e.UrlFavicon) : null,
        }).ToList();

        var options = new JsonSerializerOptions { WriteIndented = true };
        WriteFileAtomic(filePath, JsonSerializer.Serialize(exportData, options));
    }

    public void ExportAsSqliteCopy(string filePath)
    {
        lock (_gate)
        {
            var tmp = filePath + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
            using (var dest = new SqliteConnection($"Data Source={tmp};Pooling=False"))
            {
                dest.Open();
                _conn.BackupDatabase(dest);
            }

            if (File.Exists(filePath))
                File.Replace(tmp, filePath, null);
            else
                File.Move(tmp, filePath);
        }
    }

    public void ExportAsCsv(string filePath)
    {
        var entries = LoadAll();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Id,Typ,Art,Sprache,Gepinnt,Mal_verwendet,Zeitstempel,Inhalt,OCR_Text,URL_Titel");
        foreach (var e in entries)
        {
            sb.AppendLine(string.Join(",",
                e.Id,
                CsvEscape(e.Type.ToString()),
                CsvEscape(e.ContentKind),
                CsvEscape(e.Language),
                e.IsPinned ? "1" : "0",
                e.UseCount,
                CsvEscape(e.Timestamp.ToString("o")),
                CsvEscape(e.Content),
                CsvEscape(e.OcrText),
                CsvEscape(e.UrlTitle)));
        }
        WriteFileAtomic(filePath, sb.ToString());

        static string CsvEscape(string? v)
        {
            if (v == null) return "";
            v = v.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
            if (v.Contains(',') || v.Contains('"'))
                return "\"" + v.Replace("\"", "\"\"") + "\"";
            return v;
        }
    }

    public void ExportAsMarkdown(string filePath)
    {
        var entries = LoadAll();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Clipwell History Export");
        sb.AppendLine();
        sb.AppendLine($"Exportiert: {DateTime.Now:dd.MM.yyyy HH:mm}  ");
        sb.AppendLine($"Eintraege: {entries.Count}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        foreach (var e in entries)
        {
            var preview = (e.Content ?? e.OcrText ?? e.UrlTitle ?? "").Trim();
            if (preview.Length > 120) preview = preview[..120] + "...";
            preview = preview.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
            var badge = e.ContentKind ?? e.Type.ToString();
            var pinMark = e.IsPinned ? " *" : "";
            sb.AppendLine($"- [{badge}] {preview}{pinMark}  ");
            sb.AppendLine($"  *{e.Timestamp:dd.MM.yyyy HH:mm}*");
        }
        WriteFileAtomic(filePath, sb.ToString());
    }

    private bool EntryExists(DateTime timestamp, string? content)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM History WHERE Timestamp=$ts AND Content IS $content";
            cmd.Parameters.AddWithValue("$ts", timestamp.ToString("o"));
            cmd.Parameters.AddWithValue("$content", (object?)content ?? DBNull.Value);
            return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
        }
    }

    public void SecureDelete(long id)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM History WHERE Id=$id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
            Exec("VACUUM");
        }
    }

    public (int imported, int skipped, List<string> errors) ImportFromJsonDetailed(string filePath)
    {
        List<JsonElement>? items;
        try
        {
            var json = File.ReadAllText(filePath);
            items = JsonSerializer.Deserialize<List<JsonElement>>(json);
        }
        catch (Exception ex)
        {
            return (0, 0, [$"Datei konnte nicht gelesen werden: {ex.Message}"]);
        }

        if (items == null) return (0, 0, ["Datei enthält kein gültiges JSON-Array."]);

        int count = 0, skipped = 0;
        var errors = new List<string>();
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            try
            {
                string typeStr = item.GetProperty("Type").GetString()!;
                var ts = item.TryGetProperty("Timestamp", out var tsProp)
                    ? ParseDbTimestamp(tsProp.GetString()!)
                    : DateTime.Now;
                var content = TryGetStr(item, "Content");

                if (EntryExists(ts, content)) { skipped++; continue; }

                var entry = new ClipboardEntry
                {
                    Type = Enum.Parse<EntryType>(typeStr, ignoreCase: true),
                    Content         = content,
                    OcrText         = TryGetStr(item, "OcrText"),
                    Language        = TryGetStr(item, "Language"),
                    UrlTitle        = TryGetStr(item, "UrlTitle"),
                    HexColor        = TryGetStr(item, "HexColor"),
                    ContentKind     = TryGetStr(item, "ContentKind"),
                    DetectionReason = TryGetStr(item, "DetectionReason"),
                    IsPinned        = item.TryGetProperty("IsPinned", out var ip) && ip.GetBoolean(),
                    PinOrder        = TryGetLong(item, "PinOrder"),
                    UseCount        = TryGetInt(item, "UseCount"),
                    LastUsedAt      = TryGetDateTime(item, "LastUsedAt"),
                    Timestamp       = ts,
                    ImageData       = TryGetBytes(item, "ImageDataBase64"),
                    ThumbnailData   = TryGetBytes(item, "ThumbnailDataBase64"),
                    UrlFavicon      = TryGetBytes(item, "FaviconBase64"),
                };
                Insert(entry);
                count++;
            }
            catch (Exception ex)
            {
                errors.Add($"Eintrag {i + 1}: {ex.Message}");
            }
        }
        return (count, skipped, errors);

        static string? TryGetStr(JsonElement el, string key)
            => el.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null
                ? v.GetString() : null;

        static byte[]? TryGetBytes(JsonElement el, string key)
        {
            if (!el.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null) return null;
            try { return Convert.FromBase64String(v.GetString()!); }
            catch { return null; }
        }

        static int TryGetInt(JsonElement el, string key)
            => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var value)
                ? value
                : 0;

        static long TryGetLong(JsonElement el, string key)
            => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var value)
                ? value
                : 0;

        static DateTime? TryGetDateTime(JsonElement el, string key)
        {
            if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String || v.GetString() is not { } value)
                return null;
            try { return ParseDbTimestamp(value); }
            catch { return null; }
        }
    }

    public void AutoBackupIfNeeded(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, $"clipwell-autobackup-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        ExportAsJsonStreaming(target);

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

    // atomic: temp + replace prevents partial writes on crash
    private static void WriteFileAtomic(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        if (File.Exists(path))
            File.Replace(tmp, path, null);
        else
            File.Move(tmp, path);
    }

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void ExportAsJsonStreaming(string filePath)
    {
        var tmp = filePath + ".tmp";
        lock (_gate)
        {
            using (var fs = File.Create(tmp))
            using (var writer = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true }))
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = "SELECT * FROM History ORDER BY IsPinned DESC, Timestamp DESC";
                using var reader = cmd.ExecuteReader();

                writer.WriteStartArray();
                while (reader.Read())
                    WriteEntryJson(writer, reader);
                writer.WriteEndArray();
            }

            if (File.Exists(filePath))
                File.Replace(tmp, filePath, null);
            else
                File.Move(tmp, filePath);
        }
    }

    private static void WriteEntryJson(Utf8JsonWriter writer, SqliteDataReader r)
    {
        writer.WriteStartObject();
        writer.WriteNumber("Id", r.GetInt64(r.GetOrdinal("Id")));
        writer.WriteString("Type", ((EntryType)r.GetInt32(r.GetOrdinal("Type"))).ToString());
        WriteNullableString(writer, "Content", r, "Content");
        WriteNullableString(writer, "OcrText", r, "OcrText");
        WriteNullableString(writer, "Language", r, "Language");
        WriteNullableString(writer, "UrlTitle", r, "UrlTitle");
        WriteNullableString(writer, "HexColor", r, "HexColor");
        WriteNullableString(writer, "ContentKind", r, "ContentKind");
        WriteNullableString(writer, "DetectionReason", r, "DetectionReason");
        writer.WriteBoolean("IsPinned", r.GetInt32(r.GetOrdinal("IsPinned")) == 1);
        writer.WriteNumber("PinOrder", r.GetInt64(r.GetOrdinal("PinOrder")));
        writer.WriteNumber("UseCount", r.GetInt32(r.GetOrdinal("UseCount")));
        WriteNullableString(writer, "LastUsedAt", r, "LastUsedAt");
        writer.WriteString("Timestamp", r.GetString(r.GetOrdinal("Timestamp")));
        WriteNullableBase64(writer, "ImageDataBase64", r, "ImageData");
        WriteNullableBase64(writer, "ThumbnailDataBase64", r, "ThumbnailData");
        WriteNullableBase64(writer, "FaviconBase64", r, "UrlFavicon");
        writer.WriteEndObject();
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string propertyName, SqliteDataReader r, string columnName)
    {
        var ordinal = r.GetOrdinal(columnName);
        if (r.IsDBNull(ordinal)) writer.WriteNull(propertyName);
        else writer.WriteString(propertyName, r.GetString(ordinal));
    }

    private static void WriteNullableBase64(Utf8JsonWriter writer, string propertyName, SqliteDataReader r, string columnName)
    {
        var ordinal = r.GetOrdinal(columnName);
        if (r.IsDBNull(ordinal)) writer.WriteNull(propertyName);
        else writer.WriteBase64String(propertyName, (byte[])r[ordinal]);
    }

    private static DateTime ParseDbTimestamp(string value)
    {
        if (DateTime.TryParseExact(value, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            return parsed;
        return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _conn.Dispose();
            SqliteConnection.ClearPool(_conn);
        }
    }
}

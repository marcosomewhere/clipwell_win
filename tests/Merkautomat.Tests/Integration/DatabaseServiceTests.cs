using System.IO;
using Merkautomat.Models;
using Merkautomat.Services;
using Xunit;

namespace Merkautomat.Tests;

public class DatabaseServiceTests
{
    [Fact]
    public void AppPaths_UsesToolDirectory()
    {
        var dataDir = AppPaths.ResolveDataDir(@"C:\Tools\Merkautomat");

        Assert.Equal(@"C:\Tools\Merkautomat\data", dataDir);
    }

    [Fact]
    public void ExportAndImportJson_DeduplicatesImportedEntries()
    {
        var sourcePath = TempDbPath();
        var targetPath = TempDbPath();
        var exportPath = Path.Combine(Path.GetTempPath(), $"merkautomat-export-{Guid.NewGuid():N}.json");

        try
        {
            using (var source = new DatabaseService(sourcePath))
            {
                source.Insert(new ClipboardEntry
                {
                    Type = EntryType.Text,
                    Content = "exported value",
                    Timestamp = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Local),
                    DetectionReason = "test",
                });
                source.ExportAsJson(exportPath);
            }

            using var target = new DatabaseService(targetPath);
            Assert.Equal(1, target.ImportFromJsonDetailed(exportPath).imported);
            Assert.Equal(0, target.ImportFromJsonDetailed(exportPath).imported);
            Assert.Single(target.LoadAll());
        }
        finally
        {
            DeleteDb(sourcePath);
            DeleteDb(targetPath);
            DeleteIfExists(exportPath);
        }
    }

    [Fact]
    public void ExportAsJsonOverload_ExportsOnlyGivenEntries()
    {
        var sourcePath = TempDbPath();
        var targetPath = TempDbPath();
        var exportPath = Path.Combine(Path.GetTempPath(), $"merkautomat-export-{Guid.NewGuid():N}.json");

        try
        {
            using (var source = new DatabaseService(sourcePath))
            {
                source.Insert(new ClipboardEntry { Type = EntryType.Text, Content = "keep", Timestamp = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Local) });
                source.Insert(new ClipboardEntry { Type = EntryType.Text, Content = "skip", Timestamp = new DateTime(2026, 6, 5, 12, 1, 0, DateTimeKind.Local) });

                var subset = source.LoadAll().Where(e => e.Content == "keep").ToList();
                source.ExportAsJson(subset, exportPath);
            }

            using var target = new DatabaseService(targetPath);
            Assert.Equal(1, target.ImportFromJsonDetailed(exportPath).imported);
            Assert.Equal("keep", target.LoadAll().Single().Content);
        }
        finally
        {
            DeleteDb(sourcePath);
            DeleteDb(targetPath);
            DeleteIfExists(exportPath);
        }
    }

    [Fact]
    public void ExportAndImportJson_RoundTripsMetadata()
    {
        var sourcePath = TempDbPath();
        var targetPath = TempDbPath();
        var exportPath = Path.Combine(Path.GetTempPath(), $"merkautomat-export-{Guid.NewGuid():N}.json");
        var lastUsedAt = new DateTime(2026, 6, 27, 13, 0, 0, DateTimeKind.Utc);

        try
        {
            using (var source = new DatabaseService(sourcePath))
            {
                source.Insert(new ClipboardEntry
                {
                    Type = EntryType.Image,
                    ContentKind = "PNG",
                    ImageData = [1, 2, 3, 4],
                    ThumbnailData = [4, 3, 2, 1],
                    IsPinned = true,
                    PinOrder = 42,
                    UseCount = 7,
                    LastUsedAt = lastUsedAt,
                    Timestamp = new DateTime(2026, 6, 27, 12, 0, 0, DateTimeKind.Utc),
                });
                source.ExportAsJson(exportPath);
            }

            using var target = new DatabaseService(targetPath);
            Assert.Equal(1, target.ImportFromJsonDetailed(exportPath).imported);

            var entry = target.LoadAll().Single();
            Assert.True(entry.IsPinned);
            Assert.Equal(42, entry.PinOrder);
            Assert.Equal(7, entry.UseCount);
            Assert.Equal(lastUsedAt, entry.LastUsedAt);
            Assert.Equal(new byte[] { 4, 3, 2, 1 }, entry.ThumbnailData);
        }
        finally
        {
            DeleteDb(sourcePath);
            DeleteDb(targetPath);
            DeleteIfExists(exportPath);
        }
    }

    [Fact]
    public void ClearHistory_CanKeepPinnedEntries()
    {
        var dbPath = TempDbPath();
        try
        {
            using var db = new DatabaseService(dbPath);
            var pinnedId = db.Insert(new ClipboardEntry
            {
                Type = EntryType.Text,
                Content = "pinned",
                IsPinned = true,
                Timestamp = DateTime.Now,
            });
            db.Insert(new ClipboardEntry
            {
                Type = EntryType.Text,
                Content = "unpinned",
                Timestamp = DateTime.Now,
            });

            db.ClearHistory(keepPinned: true);

            var remaining = db.LoadAll();
            Assert.Single(remaining);
            Assert.Equal(pinnedId, remaining[0].Id);
            Assert.True(remaining[0].IsPinned);
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public void Insert_AssignsPinOrderForPinnedEntries()
    {
        var dbPath = TempDbPath();
        try
        {
            using var db = new DatabaseService(dbPath);
            var first = new ClipboardEntry
            {
                Type = EntryType.Text,
                Content = "first",
                IsPinned = true,
                Timestamp = DateTime.Now,
            };
            var second = new ClipboardEntry
            {
                Type = EntryType.Text,
                Content = "second",
                IsPinned = true,
                Timestamp = DateTime.Now,
            };

            db.Insert(first);
            db.Insert(second);

            Assert.Equal(1, first.PinOrder);
            Assert.Equal(2, second.PinOrder);
            Assert.Equal(new long[] { 1, 2 }, db.LoadAll().OrderBy(e => e.PinOrder).Select(e => e.PinOrder).ToArray());
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public void UrlCache_RoundTripsAndClears()
    {
        var dbPath = TempDbPath();
        try
        {
            using var db = new DatabaseService(dbPath);
            var favicon = new byte[] { 1, 2, 3 };

            db.UpsertUrlCache("https://example.com", "Example", favicon);
            var cached = db.GetUrlCache("https://example.com");

            Assert.Equal("Example", cached.title);
            Assert.Equal(favicon, cached.favicon);

            db.ClearUrlCache();
            Assert.Equal((null, null), db.GetUrlCache("https://example.com"));
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public void SecureDelete_RemovesEntry()
    {
        var dbPath = TempDbPath();
        try
        {
            using var db = new DatabaseService(dbPath);
            var id = db.Insert(new ClipboardEntry
            {
                Type = EntryType.Text,
                Content = "delete me",
                Timestamp = DateTime.Now,
            });

            db.SecureDelete(id);

            Assert.Empty(db.LoadAll());
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public void UpdateTimestamp_MakesEntryNewest()
    {
        var dbPath = TempDbPath();
        try
        {
            using var db = new DatabaseService(dbPath);
            var oldTimestamp = new DateTime(2026, 6, 6, 10, 0, 0);
            var newerTimestamp = oldTimestamp.AddMinutes(1);
            var newestTimestamp = oldTimestamp.AddMinutes(2);
            var id = db.Insert(new ClipboardEntry
            {
                Type = EntryType.Text,
                Content = "old",
                Timestamp = oldTimestamp,
            });
            db.Insert(new ClipboardEntry
            {
                Type = EntryType.Text,
                Content = "newer",
                Timestamp = newerTimestamp,
            });

            db.UpdateTimestamp(id, newestTimestamp);

            var entries = db.LoadAll();
            Assert.Equal(id, entries[0].Id);
            Assert.Equal(newestTimestamp, entries[0].Timestamp);
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public void Timestamps_RoundTripWithKind()
    {
        var dbPath = TempDbPath();
        try
        {
            using var db = new DatabaseService(dbPath);
            var timestamp = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);
            db.Insert(new ClipboardEntry
            {
                Type = EntryType.Text,
                Content = "utc",
                Timestamp = timestamp,
            });

            var entry = db.LoadAll().Single();

            Assert.Equal(timestamp, entry.Timestamp);
            Assert.Equal(DateTimeKind.Utc, entry.Timestamp.Kind);
        }
        finally { DeleteDb(dbPath); }
    }

    [Fact]
    public void ExportAsSqliteCopy_CreatesReadableBackup()
    {
        var dbPath = TempDbPath();
        var backupPath = TempDbPath();
        try
        {
            using (var db = new DatabaseService(dbPath))
            {
                db.Insert(new ClipboardEntry
                {
                    Type = EntryType.Text,
                    Content = "backup value",
                    Timestamp = DateTime.Now,
                });
                db.ExportAsSqliteCopy(backupPath);
            }

            using var backup = new DatabaseService(backupPath);
            Assert.Equal("backup value", backup.LoadAll().Single().Content);
        }
        finally
        {
            DeleteDb(dbPath);
            DeleteDb(backupPath);
        }
    }

    [Fact]
    public void AutoBackupIfNeeded_ExportsJson()
    {
        var dbPath = TempDbPath();
        var backupDir = Path.Combine(Path.GetTempPath(), $"merkautomat-backup-{Guid.NewGuid():N}");
        try
        {
            using var db = new DatabaseService(dbPath);
            db.Insert(new ClipboardEntry
            {
                Type = EntryType.Text,
                Content = "auto backup value",
                Timestamp = DateTime.Now,
            });

            db.AutoBackupIfNeeded(backupDir);

            var backup = Directory.GetFiles(backupDir, "merkautomat-autobackup-*.json").Single();
            Assert.Contains("auto backup value", SecureFile.ReadAllText(backup));
            Assert.True(SecureFile.IsEncryptedFile(backup));
            Assert.DoesNotContain("auto backup value", File.ReadAllText(backup));
        }
        finally
        {
            DeleteDb(dbPath);
            if (Directory.Exists(backupDir))
                Directory.Delete(backupDir, recursive: true);
        }
    }

    [Fact]
    public void AutoBackupIfNeeded_RotatesNewNamesAndPreservesLegacyBackups()
    {
        var dbPath = TempDbPath();
        var backupDir = Path.Combine(Path.GetTempPath(), $"merkautomat-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(backupDir);
        try
        {
            using var db = new DatabaseService(dbPath);
            var legacy = Path.Combine(backupDir, "clipwell-autobackup-19990101-000000.json");
            SecureFile.WriteAllText(legacy, "legacy backup");
            for (var day = 1; day <= 8; day++)
                SecureFile.WriteAllText(Path.Combine(backupDir, $"merkautomat-autobackup-200001{day:00}-000000.json"), "backup");

            db.AutoBackupIfNeeded(backupDir);

            Assert.Equal("legacy backup", SecureFile.ReadAllText(legacy));
            Assert.Equal(7, Directory.GetFiles(backupDir, "merkautomat-autobackup-*.json").Length);
            Assert.False(File.Exists(Path.Combine(backupDir, "merkautomat-autobackup-20000101-000000.json")));
            Assert.True(File.Exists(Path.Combine(backupDir, "merkautomat-autobackup-20000108-000000.json")));
        }
        finally
        {
            DeleteDb(dbPath);
            Directory.Delete(backupDir, recursive: true);
        }
    }

    [Fact]
    public void LoadAllIds_ReturnsInsertedIdsWithoutBlobs()
    {
        var dbPath = TempDbPath();
        try
        {
            using var db = new DatabaseService(dbPath);
            var id1 = db.Insert(new ClipboardEntry { Type = EntryType.Text, Content = "a", Timestamp = DateTime.Now });
            var id2 = db.Insert(new ClipboardEntry { Type = EntryType.Text, Content = "b", Timestamp = DateTime.Now });

            var ids = db.LoadAllIds();

            Assert.Equal(2, ids.Count);
            Assert.Contains(id1, ids);
            Assert.Contains(id2, ids);
        }
        finally { DeleteDb(dbPath); }
    }

    [Fact]
    public void SetType_StoresKindSeparatelyFromLanguage()
    {
        var dbPath = TempDbPath();
        try
        {
            using var db = new DatabaseService(dbPath);
            var id = db.Insert(new ClipboardEntry { Type = EntryType.Text, Content = "x", Timestamp = DateTime.Now });

            db.SetType(id, EntryType.Code, language: "C#", kind: "CODE", reason: "manual");

            var entry = db.LoadAll().Single();
            Assert.Equal(EntryType.Code, entry.Type);
            Assert.Equal("C#", entry.Language);
            Assert.Equal("CODE", entry.ContentKind);
        }
        finally { DeleteDb(dbPath); }
    }

    [Fact]
    public void InitSchema_MigratesYamlTextEntriesToCode()
    {
        var dbPath = TempDbPath();
        try
        {
            using (var db = new DatabaseService(dbPath))
            {
                db.Insert(new ClipboardEntry
                {
                    Type = EntryType.Text,
                    Content = "services:\n  app:\n    image: merkautomat",
                    ContentKind = "YAML",
                    Timestamp = DateTime.Now,
                });
            }

            using var reopened = new DatabaseService(dbPath);

            var entry = reopened.LoadAll().Single();
            Assert.Equal(EntryType.Code, entry.Type);
            Assert.Equal("YAML", entry.ContentKind);
        }
        finally { DeleteDb(dbPath); }
    }

    [Fact]
    public void SearchFts_WorksAfterDatabaseReopen()
    {
        var dbPath = TempDbPath();
        try
        {
            long id;
            using (var db = new DatabaseService(dbPath))
            {
                id = db.Insert(new ClipboardEntry
                {
                    Type = EntryType.Text,
                    Content = "needle searchable value",
                    Timestamp = DateTime.Now,
                });
                db.Insert(new ClipboardEntry
                {
                    Type = EntryType.Text,
                    Content = "unrelated value",
                    Timestamp = DateTime.Now,
                });

                Assert.Contains(id, db.SearchFts("needle") ?? new HashSet<long>());
            }

            using var reopened = new DatabaseService(dbPath);

            Assert.Contains(id, reopened.SearchFts("needle") ?? new HashSet<long>());
        }
        finally { DeleteDb(dbPath); }
    }

    [Fact]
    public void UpdateContent_PersistsEditedTextAndKind()
    {
        var dbPath = TempDbPath();
        try
        {
            using var db = new DatabaseService(dbPath);
            var id = db.Insert(new ClipboardEntry
            {
                Type = EntryType.Text,
                Content = "old",
                Timestamp = DateTime.Now,
                ContentKind = "TEXT",
                DetectionReason = "test",
            });

            db.UpdateContent(id, "edited note", "NOTE", "Notiz manuell bearbeitet.");

            var entry = db.LoadAll().Single();
            Assert.Equal("edited note", entry.Content);
            Assert.Equal("NOTE", entry.ContentKind);
            Assert.Equal("Notiz manuell bearbeitet.", entry.DetectionReason);
        }
        finally { DeleteDb(dbPath); }
    }

    [Fact]
    public void OlderDatabase_GetsMissingColumnsAndKeepsEntries()
    {
        var dbPath = TempDbPath();
        try
        {
            using (var conn = OpenRaw(dbPath))
            {
                Exec(conn, @"CREATE TABLE History (
                                 Id INTEGER PRIMARY KEY AUTOINCREMENT, Type INTEGER NOT NULL, Content TEXT,
                                 ImageData BLOB, OcrText TEXT, Language TEXT, HexColor TEXT,
                                 IsPinned INTEGER NOT NULL DEFAULT 0, Timestamp TEXT NOT NULL);
                             INSERT INTO History (Type, Content, Timestamp) VALUES (0, 'alt', '2026-01-01T10:00:00');");
            }

            using var db = new DatabaseService(dbPath);
            var entry = Assert.Single(db.LoadAllLite());
            Assert.Equal("alt", entry.Content);
            Assert.Equal(0, entry.UseCount);
        }
        finally { DeleteDb(dbPath); }
    }

    [Fact]
    public void DatabaseWithColumnsOfANewerVersion_StillOpensAndWorks()
    {
        var dbPath = TempDbPath();
        try
        {
            using (new DatabaseService(dbPath)) { }
            using (var conn = OpenRaw(dbPath))
                Exec(conn, "ALTER TABLE History ADD COLUMN FutureField TEXT NOT NULL DEFAULT ''");

            using var db = new DatabaseService(dbPath);
            db.Insert(new ClipboardEntry { Type = EntryType.Text, Content = "neu", Timestamp = DateTime.Now });
            Assert.Equal("neu", Assert.Single(db.LoadAll()).Content);
        }
        finally { DeleteDb(dbPath); }
    }

    private static Microsoft.Data.Sqlite.SqliteConnection OpenRaw(string path)
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection(
            EncryptedDatabase.ConnectionString(path, EncryptedDatabase.History, "Pooling=False"));
        conn.Open();
        return conn;
    }

    private static void Exec(Microsoft.Data.Sqlite.SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string TempDbPath()
        => Path.Combine(Path.GetTempPath(), $"merkautomat-test-{Guid.NewGuid():N}.db");

    private static void DeleteDb(string path)
    {
        DeleteIfExists(path);
        DeleteIfExists($"{path}-wal");
        DeleteIfExists($"{path}-shm");
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}

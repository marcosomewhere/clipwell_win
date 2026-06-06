using System.IO;
using ClipwellWin.Models;
using ClipwellWin.Services;
using Xunit;

namespace ClipwellWin.Tests;

public class DatabaseServiceTests
{
    [Fact]
    public void ExportAndImportJson_DeduplicatesImportedEntries()
    {
        var sourcePath = TempDbPath();
        var targetPath = TempDbPath();
        var exportPath = Path.Combine(Path.GetTempPath(), $"clipwell-export-{Guid.NewGuid():N}.json");

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
            Assert.Equal(1, target.ImportFromJson(exportPath));
            Assert.Equal(0, target.ImportFromJson(exportPath));
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

            db.SetType(id, EntryType.Code, language: "C#", kind: "SCRIPT", reason: "manual");

            var entry = db.LoadAll().Single();
            Assert.Equal(EntryType.Code, entry.Type);
            Assert.Equal("C#", entry.Language);
            Assert.Equal("SCRIPT", entry.ContentKind);
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

    private static string TempDbPath()
        => Path.Combine(Path.GetTempPath(), $"clipwell-test-{Guid.NewGuid():N}.db");

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

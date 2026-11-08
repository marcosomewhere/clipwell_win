using System.IO;
using System.Security.Cryptography;
using System.Text;
using ClipwellWin.Models;
using ClipwellWin.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ClipwellWin.Tests;

public sealed class EncryptionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "clipwell-crypto-" + Guid.NewGuid().ToString("N"));

    public EncryptionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string PathOf(string name) => Path.Combine(_dir, name);

    [Fact]
    public void RecoveryCode_RoundTripsAndToleratesTyping()
    {
        var code = RecoveryCode.Generate();
        var text = RecoveryCode.Format(code);
        Assert.Matches("^([0-9A-HJKMNP-TV-Z]{4}-){7}[0-9A-HJKMNP-TV-Z]{4}$", text);
        Assert.Equal(code, RecoveryCode.Parse(text));
        Assert.Equal(code, RecoveryCode.Parse(" " + text.ToLowerInvariant().Replace("-", " ") + " "));
        Assert.Equal(code, RecoveryCode.Parse(text.Replace('0', 'O').Replace('1', 'I')));
        Assert.Null(RecoveryCode.Parse(text[..^1]));
        Assert.Null(RecoveryCode.Parse(text[..^1] + "U"));
    }

    [Fact]
    public void RecoveryWrap_OpensOnlyWithItsCode()
    {
        var key = DataKey.Create();
        var reopened = DataProtection.TryUnwrap(key.RecoveryWrap, key.RecoveryKey!);
        Assert.NotNull(reopened);
        Assert.Equal(key.Id, reopened!.Id);
        Assert.Equal(key.DatabasePassword("history"), reopened.DatabasePassword("history"));
        Assert.Null(DataProtection.TryUnwrap(key.RecoveryWrap, RecoveryCode.Format(RecoveryCode.Generate())));
    }

    [Fact]
    public void DatabasePasswords_DifferPerDatabase()
    {
        var key = DataKey.Create();
        Assert.NotEqual(key.DatabasePassword("history"), key.DatabasePassword("semantic"));
        Assert.Matches("^[0-9a-f]{64}$", key.DatabasePassword("history"));
    }

    [Fact]
    public void SecureFile_RoundTripsWithoutPlainTextOnDisk()
    {
        var path = PathOf("note.md");
        SecureFile.WriteAllText(path, "Passwort: geheim123 äöü");
        Assert.True(SecureFile.IsEncryptedFile(path));
        Assert.DoesNotContain("geheim123", Encoding.Latin1.GetString(File.ReadAllBytes(path)));
        Assert.Equal("Passwort: geheim123 äöü", SecureFile.ReadAllText(path));
    }

    [Fact]
    public void SecureFile_ReadsPlainFilesFromBeforeTheEncryption()
    {
        var path = PathOf("alt.md");
        File.WriteAllText(path, "alter Stand", new UTF8Encoding(true));
        Assert.False(SecureFile.IsEncryptedFile(path));
        Assert.Equal("alter Stand", SecureFile.ReadAllText(path));
    }

    [Fact]
    public void SecureFile_DetectsTampering()
    {
        var path = PathOf("t.md");
        SecureFile.WriteAllText(path, "unveraendert");
        var bytes = File.ReadAllBytes(path);
        bytes[^20] ^= 1;
        File.WriteAllBytes(path, bytes);
        Assert.ThrowsAny<CryptographicException>(() => SecureFile.ReadAllText(path));
    }

    [Fact]
    public void SecureFile_RejectsRecordsMovedBetweenFiles()
    {
        var a = PathOf("a.md");
        var b = PathOf("b.md");
        var big = new string('x', SecureFile.RecordPlainMax) + "Ende";
        SecureFile.WriteAllText(a, big);
        SecureFile.WriteAllText(b, big);
        var bytesA = File.ReadAllBytes(a);
        // Same key and header, but records of a two-record file swapped: the record number is authenticated.
        var headerLength = bytesA.Length - (4 + 12 + SecureFile.RecordPlainMax + 16) - (4 + 12 + 4 + 16);
        var first = bytesA[headerLength..(headerLength + 4 + 12 + SecureFile.RecordPlainMax + 16)];
        var second = bytesA[(headerLength + first.Length)..];
        File.WriteAllBytes(a, [.. bytesA[..headerLength], .. second, .. first]);
        Assert.ThrowsAny<CryptographicException>(() => SecureFile.ReadAllText(a));
        Assert.Equal(big, SecureFile.ReadAllText(b));
    }

    [Fact]
    public void SecureFile_SplitsLargeContentIntoRecords()
    {
        var path = PathOf("gross.json");
        var text = string.Concat(Enumerable.Range(0, 30_000).Select(i => $"Zeile {i}\n"));
        SecureFile.WriteAllText(path, text);
        Assert.Equal(text, SecureFile.ReadAllText(path));
    }

    [Fact]
    public void OpenAppend_ContinuesARecordingAndDropsACutOffRecord()
    {
        var path = PathOf("aufnahme.md");
        using (var writer = new StreamWriter(SecureFile.OpenAppend(path), new UTF8Encoding(false)))
        {
            writer.Write("# Kopf\n");
            writer.Flush();
            writer.Write("Bild 1\n");
            writer.Flush();
            // The knowledge base reads the running recording.
            Assert.Equal("# Kopf\nBild 1\n", SecureFile.ReadAllText(path));
        }
        using (var fs = new FileStream(path, FileMode.Append)) fs.Write([10, 0, 0, 0, 1, 2, 3]);
        Assert.Equal("# Kopf\nBild 1\n", SecureFile.ReadAllText(path));

        using (var writer = new StreamWriter(SecureFile.OpenAppend(path), new UTF8Encoding(false)))
            writer.Write("Bild 2\n");
        Assert.Equal("# Kopf\nBild 1\nBild 2\n", SecureFile.ReadAllText(path));
    }

    [Fact]
    public void OpenAppend_EncryptsAPlainFileFirst()
    {
        var path = PathOf("alt-aufnahme.md");
        File.WriteAllText(path, "alt\n");
        using (var writer = new StreamWriter(SecureFile.OpenAppend(path), new UTF8Encoding(false))) writer.Write("neu\n");
        Assert.True(SecureFile.IsEncryptedFile(path));
        Assert.Equal("alt\nneu\n", SecureFile.ReadAllText(path));
    }

    [Fact]
    public void ForeignFile_OpensWithItsRecoveryKey()
    {
        var other = DataKey.Create();
        var path = PathOf("backup-vom-alten-pc.json");
        File.WriteAllBytes(path, SecureFile.Encrypt("[1,2,3]"u8, other));

        var foreign = Assert.Throws<ForeignKeyException>(() => SecureFile.ReadAllText(path));
        Assert.Null(DataProtection.TryUnwrap(foreign.RecoveryWrap, RecoveryCode.Format(RecoveryCode.Generate())));
        var opened = DataProtection.TryUnwrap(foreign.RecoveryWrap, other.RecoveryKey!);
        Assert.NotNull(opened);
        DataProtection.AddForeign(opened!);
        Assert.Equal("[1,2,3]", SecureFile.ReadAllText(path));
    }

    [Fact]
    public void EncryptInPlace_KeepsContentAndWriteTime()
    {
        var path = PathOf("2026-01-01_10-00-00.md");
        File.WriteAllText(path, "Notiz mit Inhalt");
        var written = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, written);

        Assert.True(SecureFile.EncryptInPlace(path));
        Assert.False(SecureFile.EncryptInPlace(path));
        Assert.True(SecureFile.IsEncryptedFile(path));
        Assert.Equal("Notiz mit Inhalt", SecureFile.ReadAllText(path));
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public void ResumeInterrupted_NeverLosesTheOnlyCopy()
    {
        // Crash after the original was moved aside, before the encrypted copy took its place.
        var path = PathOf("tasks.json");
        File.WriteAllText(path + SecureFile.PlainOldSuffix, "[\"original\"]");
        File.WriteAllBytes(path + SecureFile.EncryptingSuffix, SecureFile.Encrypt("[\"original\"]"u8));
        var result = DataMigration.EncryptExistingFiles(_dir);
        Assert.Empty(result.Failed);
        Assert.Equal("[\"original\"]", SecureFile.ReadAllText(path));
        Assert.True(SecureFile.IsEncryptedFile(path));
        Assert.Single(Directory.GetFiles(_dir));

        // Crash after the encrypted copy took its place: only the plain original is left to remove.
        File.WriteAllText(path + SecureFile.PlainOldSuffix, "[\"original\"]");
        DataMigration.EncryptExistingFiles(_dir);
        Assert.Single(Directory.GetFiles(_dir));
        Assert.Equal("[\"original\"]", SecureFile.ReadAllText(path));
    }

    [Fact]
    public void DataMigration_EncryptsNotesRecordingsAndJsonFiles()
    {
        Directory.CreateDirectory(PathOf("notes"));
        Directory.CreateDirectory(PathOf("recordings"));
        Directory.CreateDirectory(Path.Combine(_dir, "kb", "notes"));
        var files = new[]
        {
            PathOf("settings.json"), PathOf("tasks.json"), PathOf("background-discovery.json"),
            Path.Combine(_dir, "notes", "2026-01-01_10-00-00.md"), Path.Combine(_dir, "notes", "2026-01-01_10-00-00.md.bak"),
            Path.Combine(_dir, "kb", "notes", "2026-01-01_10-00-00.md"), Path.Combine(_dir, "recordings", "aufnahme.md"),
        };
        foreach (var file in files) File.WriteAllText(file, "Inhalt von " + Path.GetFileName(file));
        File.WriteAllText(PathOf("editor-styles.json"), "keine Inhalte");

        var result = DataMigration.EncryptExistingFiles(_dir);

        Assert.Equal(files.Length, result.Encrypted);
        foreach (var file in files)
        {
            Assert.True(SecureFile.IsEncryptedFile(file), file);
            Assert.Equal("Inhalt von " + Path.GetFileName(file), SecureFile.ReadAllText(file));
        }
        Assert.False(SecureFile.IsEncryptedFile(PathOf("editor-styles.json")));
        Assert.Equal(0, DataMigration.EncryptExistingFiles(_dir).Encrypted);
    }

    [Fact]
    public void DatabaseService_EncryptsAPlainHistory()
    {
        var path = PathOf("history.db");
        using (var plain = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            plain.Open();
            using var cmd = plain.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE History (Id INTEGER PRIMARY KEY AUTOINCREMENT, Type INTEGER NOT NULL, Content TEXT, " +
                              "ImageData BLOB, OcrText TEXT, Language TEXT, HexColor TEXT, IsPinned INTEGER NOT NULL DEFAULT 0, Timestamp TEXT NOT NULL);" +
                              "INSERT INTO History (Type, Content, Timestamp) VALUES (0, 'altes geheimes Passwort', '2026-01-01T10:00:00.0000000');";
            cmd.ExecuteNonQuery();
        }
        Assert.True(EncryptedDatabase.IsPlainFile(path));

        using (var db = new DatabaseService(path))
        {
            var entry = Assert.Single(db.LoadAll());
            Assert.Equal("altes geheimes Passwort", entry.Content);
            db.Insert(new ClipboardEntry { Type = EntryType.Text, Content = "neues geheimes Passwort", Timestamp = DateTime.Now });
            Assert.Equal(2, db.SearchFts("geheimes")?.Count ?? db.LoadAll().Count);
        }
        SqliteConnection.ClearAllPools();

        Assert.True(EncryptedDatabase.IsEncryptedFile(path));
        foreach (var file in Directory.GetFiles(_dir, "history.db*"))
            Assert.DoesNotContain("geheimes Passwort", Encoding.Latin1.GetString(SecureFile.ReadAllBytesShared(file)));
        Assert.Equal(["history.db"], Directory.GetFiles(_dir, "history.db*").Select(Path.GetFileName).Where(n => !n!.EndsWith("-wal") && !n.EndsWith("-shm")));

        using var withoutKey = new SqliteConnection($"Data Source={path};Pooling=False");
        var error = Assert.Throws<SqliteException>(() =>
        {
            withoutKey.Open();
            using var cmd = withoutKey.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM History";
            cmd.ExecuteScalar();
        });
        Assert.Equal(26, error.SqliteErrorCode);
    }

    [Fact]
    public void ExportAsSqliteCopy_IsAPlainDatabaseForOtherTools()
    {
        var path = PathOf("history.db");
        var copy = PathOf("export.db");
        using (var db = new DatabaseService(path))
        {
            db.Insert(new ClipboardEntry { Type = EntryType.Text, Content = "exportiert", Timestamp = DateTime.Now });
            db.ExportAsSqliteCopy(copy);
        }
        Assert.True(EncryptedDatabase.IsPlainFile(copy));
        using var plain = new SqliteConnection($"Data Source={copy};Pooling=False");
        plain.Open();
        using var cmd = plain.CreateCommand();
        cmd.CommandText = "SELECT Content FROM History";
        Assert.Equal("exportiert", cmd.ExecuteScalar());
    }

    [Fact]
    public void MailInbox_ReadsPlainHandoversFromOlderAddins()
        => Assert.Equal("{\"kind\":\"outlook-mail\"}", MailInbox.Decode("{\"kind\":\"outlook-mail\"}"u8.ToArray()));
}

/// <summary>Switches the process-wide key, so it runs alone.</summary>
[CollectionDefinition(nameof(DataKeyFileTests), DisableParallelization = true)]
public sealed class DataKeyFileCollection;

[Collection(nameof(DataKeyFileTests))]
public sealed class DataKeyFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "clipwell-keyfile-" + Guid.NewGuid().ToString("N"));
    private readonly DataKey _testKey = DataProtection.Current;

    public void Dispose()
    {
        DataProtection.Use(_testKey);
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Open_CreatesOnceAndReopensWithDpapi()
    {
        var created = DataProtection.Open(_dir);
        Assert.Equal(DataKeyState.Created, created.State);
        Assert.False(created.RecoveryKeyConfirmed);
        var id = DataProtection.Current.Id;
        var recovery = DataProtection.Current.RecoveryKey;
        Assert.DoesNotContain(recovery!, File.ReadAllText(DataProtection.KeyFile(_dir)));

        DataProtection.ConfirmRecoveryKey(_dir);
        DataProtection.Use(_testKey);
        var reopened = DataProtection.Open(_dir);
        Assert.Equal(DataKeyState.Ready, reopened.State);
        Assert.True(reopened.RecoveryKeyConfirmed);
        Assert.Equal(id, DataProtection.Current.Id);
        Assert.Equal(recovery, DataProtection.Current.RecoveryKey);
    }

    [Fact]
    public void Open_NeverReplacesTheKeyOfExistingEncryptedData()
    {
        DataProtection.Open(_dir);
        var original = DataProtection.Current;
        SecureFile.WriteAllText(Path.Combine(_dir, "settings.json"), "{}");
        File.Delete(DataProtection.KeyFile(_dir));
        DataProtection.Use(_testKey);

        var result = DataProtection.Open(_dir);

        Assert.Equal(DataKeyState.NeedsRecovery, result.State);
        Assert.False(File.Exists(DataProtection.KeyFile(_dir)));
        Assert.False(DataProtection.TryRecover(_dir, result.RecoveryWrap!, RecoveryCode.Format(RecoveryCode.Generate())));
        Assert.True(DataProtection.TryRecover(_dir, result.RecoveryWrap!, original.RecoveryKey!));
        Assert.Equal(original.Id, DataProtection.Current.Id);
        Assert.Equal("{}", SecureFile.ReadAllText(Path.Combine(_dir, "settings.json")));
        Assert.Equal(DataKeyState.Ready, DataProtection.Open(_dir).State);
    }

    [Fact]
    public void Open_AsksForTheRecoveryKeyWhenDpapiCannotOpenTheKey()
    {
        DataProtection.Open(_dir);
        var path = DataProtection.KeyFile(_dir);
        // What another Windows account sees: a DPAPI blob it cannot open.
        var json = File.ReadAllText(path);
        var start = json.IndexOf("\"Protected\": \"", StringComparison.Ordinal) + 14;
        File.WriteAllText(path, json[..start] + Convert.ToBase64String(new byte[64]) + json[json.IndexOf('"', start)..]);
        DataProtection.Use(_testKey);

        Assert.Equal(DataKeyState.NeedsRecovery, DataProtection.Open(_dir).State);
    }

    [Fact]
    public void Open_ReportsMissingWhenOnlyEncryptedDatabasesRemain()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(Path.Combine(_dir, "history.db"), RandomNumberGenerator.GetBytes(4096));
        Assert.Equal(DataKeyState.Missing, DataProtection.Open(_dir).State);
        Assert.False(File.Exists(DataProtection.KeyFile(_dir)));
    }
}

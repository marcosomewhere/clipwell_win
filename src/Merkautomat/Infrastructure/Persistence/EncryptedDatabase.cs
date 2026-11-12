using System.IO;
using Microsoft.Data.Sqlite;

namespace Merkautomat.Services;

/// <summary>
/// SQLite databases encrypted with SQLite3 Multiple Ciphers (ChaCha20-Poly1305 per page, also the WAL). Each
/// database has its own key derived from the data key.
/// </summary>
public static class EncryptedDatabase
{
    public const string History = "history";
    public const string Semantic = "semantic";
    public static readonly string[] Names = ["history.db", "semantic.db"];
    private static readonly byte[] PlainHeader = "SQLite format 3\0"u8.ToArray();

    public static string ConnectionString(string path, string name, string? extra = null)
    {
        var builder = new SqliteConnectionStringBuilder(extra ?? "")
        {
            DataSource = path,
            Password = DataProtection.Current.DatabasePassword(name),
        };
        return builder.ToString();
    }

    public static bool IsPlainFile(string path) => StartsWith(path, PlainHeader);

    /// <summary>Exists and is not a plain SQLite file.</summary>
    public static bool IsEncryptedFile(string path)
        => File.Exists(path) && new FileInfo(path).Length >= PlainHeader.Length && !IsPlainFile(path);

    private static bool StartsWith(string path, byte[] prefix)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var head = new byte[prefix.Length];
            return fs.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) == head.Length && head.AsSpan().SequenceEqual(prefix);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// Encrypts a plain database from before the encryption. Works on a copy: the original stays untouched until
    /// the encrypted copy has been opened with the key and checked, then it is overwritten with zeros and replaced.
    /// </summary>
    public static void EnsureEncrypted(string path, string name)
    {
        var copy = path + SecureFile.EncryptingSuffix;
        var old = path + SecureFile.PlainOldSuffix;
        ResumeInterrupted(path, name, copy, old);
        if (!IsPlainFile(path)) return;
        var password = DataProtection.Current.DatabasePassword(name);
        foreach (var leftover in new[] { copy, copy + "-journal" }) SecureFile.Shred(leftover);

        long expected;
        using (var plain = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            plain.Open();
            Exec(plain, "PRAGMA wal_checkpoint(TRUNCATE)");
            expected = Count(plain);
        }
        File.Copy(path, copy);
        using (var plain = new SqliteConnection($"Data Source={copy};Pooling=False"))
        {
            plain.Open();
            Exec(plain, "PRAGMA journal_mode=DELETE");
            Exec(plain, $"PRAGMA rekey='{password}'");
        }
        using (var check = new SqliteConnection(ConnectionString(copy, name, "Pooling=False")))
        {
            check.Open();
            using var cmd = check.CreateCommand();
            cmd.CommandText = "PRAGMA quick_check";
            if (!string.Equals(cmd.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase) || Count(check) != expected)
                throw new InvalidDataException($"Die verschlüsselte Kopie von {Path.GetFileName(path)} ist nicht vollständig; das Original bleibt unverändert.");
        }
        if (IsPlainFile(copy)) throw new InvalidDataException("Die Datenbank konnte nicht verschlüsselt werden.");
        SqliteConnection.ClearAllPools();
        foreach (var side in new[] { path + "-wal", path + "-shm", path + "-journal", copy + "-journal" }) SecureFile.Shred(side);
        // One complete copy under a known name at every moment; ResumeInterrupted finishes a crashed run.
        File.Move(path, old, overwrite: true);
        File.Move(copy, path);
        SecureFile.Shred(old);
    }

    private static void ResumeInterrupted(string path, string name, string copy, string old)
    {
        if (File.Exists(old))
        {
            if (File.Exists(path)) SecureFile.Shred(old);
            else File.Move(old, path);
        }
        if (!File.Exists(copy)) return;
        if (!File.Exists(path) && OpensWithKey(copy, name)) File.Move(copy, path);
        else SecureFile.Shred(copy);
    }

    private static bool OpensWithKey(string path, string name)
    {
        if (!IsEncryptedFile(path)) return false;
        try
        {
            using var check = new SqliteConnection(ConnectionString(path, name, "Pooling=False;Mode=ReadOnly"));
            check.Open();
            using var cmd = check.CreateCommand();
            cmd.CommandText = "PRAGMA quick_check";
            return string.Equals(cmd.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (SqliteException) { return false; }
    }

    /// <summary>A plain copy for the user's explicit "SQLite copy" export, which is meant for other tools.</summary>
    public static void WritePlainCopy(SqliteConnection source, string name, string target)
    {
        using (var dest = new SqliteConnection(ConnectionString(target, name, "Pooling=False")))
        {
            dest.Open();
            source.BackupDatabase(dest);
            Exec(dest, "PRAGMA journal_mode=DELETE");
            Exec(dest, "PRAGMA rekey=''");
        }
        if (!IsPlainFile(target)) throw new InvalidDataException("Die Kopie konnte nicht entschlüsselt werden.");
    }

    /// <summary>Rows of all tables except FTS shadow tables, to compare original and copy.</summary>
    private static long Count(SqliteConnection connection)
    {
        var tables = new List<string>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND sql NOT LIKE 'CREATE VIRTUAL%' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '%Fts_%'";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) tables.Add(reader.GetString(0));
        }
        long total = 0;
        foreach (var table in tables)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM \"{table.Replace("\"", "\"\"")}\"";
            total += (long)(cmd.ExecuteScalar() ?? 0L);
        }
        return total;
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}

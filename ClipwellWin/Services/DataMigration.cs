using System.IO;

namespace ClipwellWin.Services;

/// <summary>Encrypts plain data files from before the encryption (notes, KB copies, recordings, tasks, settings).</summary>
public static class DataMigration
{
    public sealed record Result(int Encrypted, IReadOnlyList<string> Failed);

    public static Result EncryptExistingFiles(string dataDir)
    {
        var encrypted = 0;
        var failed = new List<string>();
        foreach (var leftover in Leftovers(dataDir))
        {
            try { SecureFile.ResumeInterrupted(leftover); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.LogCrash(ex); failed.Add(leftover); }
        }
        foreach (var file in DataProtection.EncryptedFileCandidates(dataDir).ToList())
        {
            if (IsLeftover(file) || file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
            try { if (SecureFile.EncryptInPlace(file)) encrypted++; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                App.LogCrash(ex);
                failed.Add(file);
            }
        }
        // Temporary files of atomic writes from before the encryption; their target file is complete.
        foreach (var tmp in Directory.Exists(dataDir) ? Directory.GetFiles(dataDir, "*.json.tmp") : [])
        {
            try { SecureFile.Shred(tmp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.LogCrash(ex); }
        }
        return new(encrypted, failed);
    }

    private static bool IsLeftover(string file)
        => file.EndsWith(SecureFile.EncryptingSuffix, StringComparison.OrdinalIgnoreCase)
           || file.EndsWith(SecureFile.PlainOldSuffix, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> Leftovers(string dataDir)
    {
        var dirs = DataProtection.ContentDirectories(dataDir).Append(dataDir).Where(Directory.Exists);
        foreach (var dir in dirs)
            foreach (var file in Directory.GetFiles(dir).Where(IsLeftover).OrderBy(f => f.EndsWith(SecureFile.PlainOldSuffix) ? 0 : 1))
                // Databases resume in EncryptedDatabase.EnsureEncrypted.
                if (!EncryptedDatabase.Names.Any(n => Path.GetFileName(file).StartsWith(n, StringComparison.OrdinalIgnoreCase)))
                    yield return file;
    }
}

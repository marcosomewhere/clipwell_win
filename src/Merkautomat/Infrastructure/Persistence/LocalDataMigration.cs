using System.IO;

namespace Merkautomat.Services;

/// <summary>Copies the complete encrypted profile before its new location is opened.</summary>
public static class LocalDataMigration
{
    public static bool ImportLegacyDirectory(string sourceDirectory, string destinationDirectory)
    {
        var source = Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var destination = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) return false;
        if (source.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || Path.GetDirectoryName(destination) == null)
            throw new ArgumentException("Die Datenordner dürfen nicht ineinander liegen.");
        if (Directory.Exists(destination))
        {
            RejectLink(destination);
            if (Directory.EnumerateFileSystemEntries(destination).Any())
            {
                if (File.Exists(Path.Combine(destination, DataProtection.KeyFileName))
                    || File.Exists(Path.Combine(destination, "history.db")) || File.Exists(Path.Combine(destination, "settings.json")))
                    return false;
                if (!Directory.Exists(source)) return false;
                throw new IOException("Der neue Datenordner ist bereits belegt. Die bisherigen Daten wurden nicht mit ihm vermischt.");
            }
        }
        if (!Directory.Exists(source)) return false;
        RejectLink(source);
        var staging = destination + ".migration-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        Directory.CreateDirectory(staging);
        try
        {
            CopyDirectory(source, staging);
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: false);
            CompleteMove(staging, destination);
            return true;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static void CompleteMove(string staging, string destination)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { Directory.Move(staging, destination); return; }
            // Scanners can briefly hold the freshly copied model runtimes open.
            catch (IOException ex) when (attempt < 30 && (ex.HResult & 0xffff) is 5 or 32 or 33)
            { Thread.Sleep(100); }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            RejectLink(entry);
            var target = Path.Combine(destination, Path.GetFileName(entry));
            if (Directory.Exists(entry))
            {
                Directory.CreateDirectory(target);
                CopyDirectory(entry, target);
            }
            else
            {
                var modified = File.GetLastWriteTimeUtc(entry);
                // Byte copying preserves ciphertext and key wraps; opening the stores here would need the key first.
                using (var input = new FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    input.CopyTo(output);
                    output.Flush(flushToDisk: true);
                }
                File.SetLastWriteTimeUtc(target, modified);
            }
        }
    }

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Die Datenübernahme folgt keinen Verknüpfungen oder eingebundenen Ordnern.");
    }
}

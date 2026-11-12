using System.IO;
using Merkautomat.Models;
using Merkautomat.Services;
using Xunit;

namespace Merkautomat.Tests;

public sealed class LocalDataMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "merkautomat-local-data-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_root, "legacy");
    private string Target => Path.Combine(_root, "tool", "data");
    public LocalDataMigrationTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void DevelopmentBuildUsesRepositoryRootButPublishedCopyUsesItsOwnFolder()
    {
        var project = Path.Combine(_root, "src", "Merkautomat");
        Directory.CreateDirectory(project);
        File.WriteAllBytes(Path.Combine(_root, "Merkautomat.slnx"), []);
        File.WriteAllBytes(Path.Combine(project, "Merkautomat.csproj"), []);
        var build = Path.Combine(project, "bin", "Release", "net10.0-windows");
        Directory.CreateDirectory(build);
        Assert.Equal(Path.Combine(_root, "data"), AppPaths.ResolveDataDir(build));
        Assert.Equal(Path.Combine(_root, "data"), ToolDataPaths.ResolveDataDir(build));
        var published = Path.Combine(_root, "dist", "Merkautomat");
        Directory.CreateDirectory(published);
        Assert.Equal(Path.Combine(published, "data"), AppPaths.ResolveDataDir(published));
    }

    [Fact]
    public void CompleteProfileIsCopiedWithCiphertextKeyModelsAndTimestampsPreserved()
    {
        Directory.CreateDirectory(Path.Combine(Source, "notes"));
        Directory.CreateDirectory(Path.Combine(Source, "models", "example"));
        var key = new byte[] { 11, 22, 33, 44 };
        File.WriteAllBytes(Path.Combine(Source, DataProtection.KeyFileName), key);
        SecureFile.WriteAllText(Path.Combine(Source, "settings.json"), "settings fixture");
        SecureFile.WriteAllText(Path.Combine(Source, "notes", "example.md"), "private fixture");
        var modified = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(Source, "notes", "example.md"), modified);
        File.WriteAllBytes(Path.Combine(Source, "models", "example", "model.onnx"), [1, 2, 3]);
        using (var database = new DatabaseService(Path.Combine(Source, "history.db")))
            database.Insert(new ClipboardEntry { Type = EntryType.Text, Content = "history fixture", Timestamp = DateTime.Now });
        var encryptedSettings = File.ReadAllBytes(Path.Combine(Source, "settings.json"));

        Assert.True(LocalDataMigration.ImportLegacyDirectory(Source, Target));
        Assert.Equal(key, File.ReadAllBytes(Path.Combine(Target, DataProtection.KeyFileName)));
        Assert.Equal(encryptedSettings, File.ReadAllBytes(Path.Combine(Target, "settings.json")));
        Assert.Equal("private fixture", SecureFile.ReadAllText(Path.Combine(Target, "notes", "example.md")));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(Path.Combine(Target, "notes", "example.md")));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(Target, "models", "example", "model.onnx")));
        using var migrated = new DatabaseService(Path.Combine(Target, "history.db"));
        Assert.Equal("history fixture", Assert.Single(migrated.LoadAll()).Content);
        Assert.True(File.Exists(Path.Combine(Source, "history.db")));
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(Target)!, "data.migration-*"));
    }

    [Fact]
    public void InterruptedCopyExposesNoPartialProfileAndCanBeRetried()
    {
        Directory.CreateDirectory(Source);
        SecureFile.WriteAllText(Path.Combine(Source, "settings.json"), "preserved");
        var lockedPath = Path.Combine(Source, "locked.bin");
        File.WriteAllBytes(lockedPath, [1, 2]);
        using (var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Throws<IOException>(() => LocalDataMigration.ImportLegacyDirectory(Source, Target));
            Assert.False(Directory.Exists(Target));
            Assert.Equal("preserved", SecureFile.ReadAllText(Path.Combine(Source, "settings.json")));
        }
        Assert.True(LocalDataMigration.ImportLegacyDirectory(Source, Target));
        Assert.Equal("preserved", SecureFile.ReadAllText(Path.Combine(Target, "settings.json")));
    }

    [Fact]
    public void ExistingProfileIsAuthoritativeAndIsNeverMixedWithLegacyData()
    {
        Directory.CreateDirectory(Source);
        Directory.CreateDirectory(Target);
        SecureFile.WriteAllText(Path.Combine(Source, "settings.json"), "old");
        SecureFile.WriteAllText(Path.Combine(Target, "settings.json"), "current");
        File.WriteAllBytes(Path.Combine(Source, "old-only.bin"), [9]);
        Assert.False(LocalDataMigration.ImportLegacyDirectory(Source, Target));
        Assert.Equal("current", SecureFile.ReadAllText(Path.Combine(Target, "settings.json")));
        Assert.False(File.Exists(Path.Combine(Target, "old-only.bin")));
    }

    [Fact]
    public void OccupiedDestinationWithoutAProfileIsNotOverwrittenOrMerged()
    {
        Directory.CreateDirectory(Source);
        Directory.CreateDirectory(Target);
        SecureFile.WriteAllText(Path.Combine(Source, "settings.json"), "old");
        File.WriteAllBytes(Path.Combine(Target, "existing.bin"), [8]);
        Assert.Throws<IOException>(() => LocalDataMigration.ImportLegacyDirectory(Source, Target));
        Assert.False(File.Exists(Path.Combine(Target, "settings.json")));
        Assert.Equal(new byte[] { 8 }, File.ReadAllBytes(Path.Combine(Target, "existing.bin")));
    }

    [Fact]
    public void NestedDirectoriesAreRejectedAndSameDirectoryIsNotCopied()
    {
        Directory.CreateDirectory(Source);
        Assert.Throws<ArgumentException>(() => LocalDataMigration.ImportLegacyDirectory(Source, Path.Combine(Source, "data")));
        Assert.Throws<ArgumentException>(() => LocalDataMigration.ImportLegacyDirectory(Source, _root));
        Assert.False(LocalDataMigration.ImportLegacyDirectory(Source, Source));
    }

    [Fact]
    public void MissingLegacyFolderCreatesNoFiles()
    {
        Assert.False(LocalDataMigration.ImportLegacyDirectory(Source, Target));
        Assert.False(Directory.Exists(Target));
    }
}

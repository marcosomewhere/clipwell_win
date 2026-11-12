using System.IO;
using System.Text;
using Merkautomat.Services;
using Xunit;

namespace Merkautomat.Tests;

public sealed class KnowledgeFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "merkautomat-folder-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_root, "source");
    private string Cache => Path.Combine(_root, "internal", "folder.json");

    private string Write(string relative, string text)
    {
        var path = Path.Combine(Source, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
        return path;
    }

    private bool Scan(KnowledgeFolder folder, string extensions = "txt,md", int maxKb = 1024)
        => folder.Scan(Source, true, extensions, maxKb, CancellationToken.None);

    [Fact]
    public void RecursiveScan_RespectsFormatsBinaryAndPerFileLimit()
    {
        Write("a.txt", "Ein lesbarer Text aus dem Knowledgeordner.");
        Write(@"themen\b.md", "# Zweiter Text\n\nWissen im Unterordner.");
        Write("ignored.pdf", "Ein Text mit nicht freigegebener Endung.");
        Write("binary.txt", "Binaerer Inhalt\0mit NUL.");
        Write("large.txt", new string('x', 17 * 1024));
        Write("first.md", new string('a', 10 * 1024));
        Write("second.md", new string('b', 10 * 1024));
        var folder = new KnowledgeFolder(Cache);
        Assert.True(Scan(folder, maxKb: 16));
        Assert.Equal(4, folder.Snapshot.Files.Length);
        Assert.Contains(folder.Snapshot.Files, f => f.RelativePath == @"themen\b.md");
        Assert.DoesNotContain(folder.Snapshot.Files, f => f.RelativePath is "ignored.pdf" or "binary.txt" or "large.txt");
        Assert.False(Scan(folder, maxKb: 16));
    }

    [Fact]
    public void ChangesMovesAndDeletion_UpdateSnapshotAndKeepIdentity()
    {
        var path = Write("a.txt", "Erste Fassung mit ausreichend lesbarem Inhalt.");
        var folder = new KnowledgeFolder(Cache);
        Scan(folder);
        var id = Assert.Single(folder.Snapshot.Files).Id;
        Write("a.txt", "Zweite Fassung mit geaendertem lesbarem Inhalt.");
        Assert.True(Scan(folder));
        Assert.Equal(id, Assert.Single(folder.Snapshot.Files).Id);
        var moved = Path.Combine(Source, "neu.md");
        File.Move(path, moved);
        Assert.True(Scan(folder));
        Assert.Equal(id, Assert.Single(folder.Snapshot.Files).Id);
        File.Delete(moved);
        Assert.True(Scan(folder));
        Assert.Empty(folder.Snapshot.Files);
    }

    [Fact]
    public void MissingRootAndLockedFile_KeepLastReadableState()
    {
        var path = Write("a.txt", "Der letzte lesbare Stand bleibt erhalten.");
        var folder = new KnowledgeFolder(Cache);
        Scan(folder);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.False(Scan(folder));
            Assert.Single(folder.Snapshot.Files);
            Assert.Contains("1 Lesefehler", folder.Status);
        }
        Directory.Move(Source, Source + "-offline");
        Assert.False(Scan(folder));
        Assert.Single(folder.Snapshot.Files);
        Assert.Contains("1 Lesefehler", folder.Status);
        // Revoked types are excluded even when the drive is unavailable.
        Assert.True(Scan(folder, "md"));
        Assert.Empty(folder.Snapshot.Files);
    }

    [Fact]
    public void PauseReloadAndRemove_PreserveOriginalAndEncryptCache()
    {
        var original = Write("a.txt", "Vertrauliches Wissen aus einer externen Datei.");
        var folder = new KnowledgeFolder(Cache);
        Scan(folder);
        var id = Assert.Single(folder.Snapshot.Files).Id;
        Assert.True(SecureFile.IsEncryptedFile(Cache));
        Assert.Contains(Path.Combine(_root, KnowledgeFolder.FileName), DataProtection.EncryptedFileCandidates(_root));
        File.Copy(Cache, Path.Combine(_root, KnowledgeFolder.FileName));
        Assert.True(DataProtection.EncryptedDataExists(_root));
        Assert.DoesNotContain("Vertrauliches", Encoding.UTF8.GetString(File.ReadAllBytes(Cache)));
        Write("a.txt", "Eine spaetere Fassung, waehrend pausiert wird.");
        var reloaded = new KnowledgeFolder(Cache);
        Assert.True(reloaded.Scan(Source, false, "txt", 1024, CancellationToken.None));
        Assert.Equal(id, Assert.Single(reloaded.Snapshot.Files).Id);
        Assert.StartsWith("Vertrauliches", reloaded.Snapshot.Files[0].Text);
        Assert.True(reloaded.Scan("", false, "txt", 1024, CancellationToken.None));
        Assert.Empty(reloaded.Snapshot.Files);
        Assert.True(File.Exists(original));
    }

    [Fact]
    public void ExcludedSubtreeAndChangedRoot_DoNotMixSources()
    {
        Write("a.txt", "Ein Artikel im regulaeren Quellordner.");
        var excluded = Path.GetDirectoryName(Write(@"internal\secret.txt", "Interne Inhalte nicht erneut erfassen."))!;
        var folder = new KnowledgeFolder(Cache, excluded);
        Scan(folder);
        Assert.Single(folder.Snapshot.Files);
        var other = Path.Combine(_root, "other");
        Directory.CreateDirectory(other);
        Assert.True(folder.Scan(other, true, "txt", 1024, CancellationToken.None));
        Assert.Empty(folder.Snapshot.Files);
        Assert.True(KnowledgeFolder.IsWithin(@"C:\Wissen\a.txt", @"C:\"));
        Assert.False(KnowledgeFolder.IsWithin(@"C:\Wissen2\a.txt", @"C:\Wissen"));
    }

    [Fact]
    public void IdenticalCopies_AreSeparateAndDoNotTransferAmbiguousAnnotations()
    {
        var first = Write("a.txt", "Ein identischer Text in mehreren eigenen Dateien.");
        Write("b.txt", "Ein identischer Text in mehreren eigenen Dateien.");
        var folder = new KnowledgeFolder(Cache);
        Scan(folder);
        var ids = folder.Snapshot.Files.ToDictionary(f => f.RelativePath, f => f.Id);
        Assert.NotEqual(ids["a.txt"], ids["b.txt"]);
        File.Move(first, Path.Combine(Source, "c.txt"));
        Scan(folder);
        Assert.Equal(ids["b.txt"], folder.Snapshot.Files.Single(f => f.RelativePath == "b.txt").Id);
        Assert.DoesNotContain(folder.Snapshot.Files, f => f.Id == ids["a.txt"]);
    }

    [Fact]
    public void Chunks_KeepSourceContextAndFullLongDocument()
    {
        var longText = string.Join("\n\n", Enumerable.Repeat("Ein langer Absatz mit lesbarem Wissen und semantischem Zusammenhang.", 700));
        Write("long.md", longText);
        Write("short.txt", "kurz");
        var folder = new KnowledgeFolder(Cache);
        Scan(folder);
        var chunks = folder.Chunks();
        Assert.True(chunks.Count > 32);
        Assert.All(chunks, c =>
        {
            Assert.Equal(SemanticSource.KnowledgeFolder, c.Source);
            Assert.Equal(Path.Combine(Source, "long.md"), c.Metadata!.SourcePath);
            Assert.StartsWith("f:", c.ItemKey);
        });
        Assert.Equal(longText.Replace("\n", ""), string.Concat(chunks.Select(c => c.Text)).Replace("\n", ""));
        Assert.Equal(2, folder.Snapshot.Files.Length);
    }

    [Fact]
    public void DeletionPlan_NeverDeletesExternalOriginals()
    {
        var original = Write("a.md", "Ein wichtiger Artikel im Knowledgeordner.");
        var folder = new KnowledgeFolder(Cache);
        Scan(folder);
        var plan = SemanticDeletion.Plan(folder.Chunks(), Path.Combine(_root, "notes"), Path.Combine(_root, "kb"), Path.Combine(_root, "recordings"), null);
        Assert.Empty(plan.Targets);
        Assert.True(File.Exists(original));
    }

    private sealed class Embedder : ITextEmbedder
    {
        public int Embedded;
        public float[] EmbedQuery(string query) => [1, 0];
        public float[][] EmbedPassages(IReadOnlyList<string> texts)
        {
            Embedded += texts.Count;
            return texts.Select(_ => new float[] { 1, 0 }).ToArray();
        }
    }

    [Fact]
    public async Task SharedIndex_RefreshesQueuedFolderSnapshotAndReusesVectors()
    {
        Write("a.txt", "Ein Artikel zum Aufbau eines lokalen Wissensnetzes.");
        var folder = new KnowledgeFolder(Cache);
        Scan(folder);
        var embedder = new Embedder();
        using var index = new SemanticIndex(Path.Combine(_root, "semantic.db"), "test-folder", () => embedder)
        {
            AdditionalSources = folder.Chunks,
        };
        var stale = index.Collect([], Path.Combine(_root, "notes"), Path.Combine(_root, "recordings"));
        Assert.Single(stale);
        await index.SyncAsync(stale, TestContext.Current.CancellationToken);
        Assert.Equal(1, embedder.Embedded);
        await index.SyncAsync([], TestContext.Current.CancellationToken);
        Assert.Equal(1, embedder.Embedded);
        Write("a.txt", "Aktualisierter Inhalt zum Verknuepfen von lokalem Wissen.");
        Scan(folder);
        await index.SyncAsync(stale, TestContext.Current.CancellationToken);
        Assert.StartsWith("Aktualisierter", Assert.Single(index.Snapshot()).Chunk.Text);
        Assert.Equal(2, embedder.Embedded);
        folder.Scan("", false, "txt", 1024, CancellationToken.None);
        await index.SyncAsync(stale, TestContext.Current.CancellationToken);
        Assert.Empty(index.Snapshot());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var path = Path.GetFullPath(_root);
        if (!KnowledgeFolder.IsWithin(path, Path.GetFullPath(Path.GetTempPath()))) throw new InvalidOperationException("Ungültiger Testpfad.");
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }
}

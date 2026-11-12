using System.IO;
using Merkautomat.Models;
using Merkautomat.Services;
using Xunit;

namespace Merkautomat.Tests;

public sealed class SemanticSearchTests : IDisposable
{
    private static readonly DateTime Day = new(2026, 11, 5, 9, 0, 0);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "merkautomat-semantic-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>Bag of words hashed into a small vector: shared words mean high cosine similarity.</summary>
    private sealed class WordEmbedder : ITextEmbedder
    {
        public int PassageCalls;
        public int PassagesEmbedded;

        public float[] EmbedQuery(string query) => Vectorize(query);

        public float[][] EmbedPassages(IReadOnlyList<string> texts)
        {
            PassageCalls++;
            PassagesEmbedded += texts.Count;
            return texts.Select(Vectorize).ToArray();
        }

        public static float[] Vectorize(string text)
        {
            var v = new float[64];
            foreach (var term in SemanticRanking.Terms(text)) v[(term.GetHashCode() & 0x7fffffff) % 64] += 1;
            if (v.All(x => x == 0)) v[0] = 1;
            SemanticRanking.Normalize(v);
            return v;
        }
    }

    private static SemanticChunk Chunk(string item, string text, SemanticSource source = SemanticSource.Note, int minutes = 0)
        => new($"{item}:0", item, source, item, Day.AddMinutes(minutes), text);

    private static IndexedChunk Indexed(SemanticChunk c, float[]? vector = null) => new(c, vector ?? WordEmbedder.Vectorize(c.Text));

    private static float[] Unit(params float[] v)
    {
        SemanticRanking.Normalize(v);
        return v;
    }

    [Fact]
    public void Split_KeepsChunksBelowLimitAndCutsLongLinesAtSpaces()
    {
        var text = "Erster Absatz mit Text.\n\nZweiter Absatz.\n" + string.Join(" ", Enumerable.Repeat("wort", 80));
        var parts = SemanticChunker.Split(text, 120);

        Assert.All(parts, p => Assert.True(p.Length <= 120, p));
        Assert.StartsWith("Erster Absatz mit Text.", parts[0]);
        Assert.DoesNotContain(parts, p => p.StartsWith(' ') || p.EndsWith(' '));
        Assert.Equal(text.Count(c => c == 'w'), string.Concat(parts).Count(c => c == 'w'));
    }

    [Fact]
    public void FromClipboard_SkipsShortSingleTokensAndColors_UsesOcrAndUrlTitle()
    {
        ClipboardEntry[] entries =
        [
            new() { Id = 1, Type = EntryType.Text, Content = "Xy7$kLm9!qZ2", Timestamp = Day },
            new() { Id = 2, Type = EntryType.Text, Content = "Zertifikat im Portal erneuern", Timestamp = Day },
            new() { Id = 3, Type = EntryType.Color, Content = "#FF00FF00 und mehr Text", Timestamp = Day },
            new() { Id = 4, Type = EntryType.Image, OcrText = "Rechnung Nummer 4711 vom Oktober", Timestamp = Day },
            new() { Id = 5, Type = EntryType.Url, Content = "https://example.org/vpn", UrlTitle = "VPN Anleitung", Timestamp = Day },
            new() { Id = 6, Type = EntryType.Text, Content = "kurz", Timestamp = Day },
        ];

        var chunks = SemanticChunker.FromClipboard(entries).ToList();

        Assert.Equal(["c:2", "c:4", "c:5"], chunks.Select(c => c.ItemKey));
        Assert.Equal("VPN Anleitung", chunks[2].Title);
        Assert.Contains("https://example.org/vpn", chunks[2].Text);
        Assert.Equal(4, chunks[1].EntryId);
    }

    [Fact]
    public void SplitSnapshots_KeepsCapturedTextOnly()
    {
        var md = RecordingMarkdown.SessionHeader(new DateTime(2026, 11, 5, 23, 59, 0))
            + RecordingMarkdown.FormatSnapshot(new DateTime(2026, 11, 5, 23, 59, 30), "Edge", "VPN Hilfe",
                ["Zertifikat erneuern", "Portal öffnen"], null, null)
            + RecordingMarkdown.FormatSnapshot(new DateTime(2026, 11, 6, 0, 0, 10), "Edge", "VPN Hilfe",
                ["Zertifikat erneuern", "Portal öffnen", "Import im Client"], ["Zertifikat erneuern", "Portal öffnen"], null)
            + RecordingMarkdown.SessionFooter(new DateTime(2026, 11, 5, 23, 59, 0), new DateTime(2026, 11, 6, 0, 1, 0), 2);

        var snaps = RecordingMarkdown.SplitSnapshots(md);

        Assert.Equal(2, snaps.Count);
        Assert.Equal("Zertifikat erneuern\nPortal öffnen", snaps[0].Body);
        Assert.Equal("Import im Client", snaps[1].Body);
        Assert.Equal(new DateTime(2026, 11, 6, 0, 0, 10), snaps[1].Time);
        Assert.Equal("Edge", snaps[1].App);
    }

    [Fact]
    public void FromRecording_PrefixesWindowLabelAndKeysBySnapshot()
    {
        var md = RecordingMarkdown.SessionHeader(Day)
            + RecordingMarkdown.FormatSnapshot(Day.AddSeconds(5), "Teams", "Chat", ["Kein lesbarer Inhalt hier drin"], null, null);
        var chunk = Assert.Single(SemanticChunker.FromRecording(@"C:\rec\2026-11-05_09-00-00.md", md));

        Assert.Equal("r:2026-11-05_09-00-00.md#0", chunk.ItemKey);
        Assert.Equal("Teams: Chat", chunk.Title);
        Assert.StartsWith("Teams: Chat\n", chunk.Text);
    }

    [Fact]
    public void KbBody_StripsExportHeader()
    {
        var note = new Note { Id = "2026-11-05_09-00-00", Path = @"C:\n\x.md", Created = Day, Modified = Day, Text = "Titel\nInhalt der Notiz" };
        var md = NoteStore.BuildKbMarkdown(note, Day);

        Assert.Equal("Titel\nInhalt der Notiz", NoteStore.KbBody(md));
    }

    [Fact]
    public void Search_FusesSemanticAndWordMatches_AndDropsUnrelated()
    {
        var query = Unit(1, 0, 0);
        IndexedChunk[] chunks =
        [
            Indexed(Chunk("n:semantic", "Zertifikat im Portal anfordern"), Unit(0.95f, 0.31f, 0)),
            Indexed(Chunk("n:lexical", "Notiz zum VPN Client"), Unit(0, 1, 0)),
            Indexed(Chunk("n:unrelated", "Apfelkuchen mit Zimt"), Unit(0, 0, 1)),
        ];

        var hits = SemanticRanking.Search(query, "VPN", chunks, 10);

        Assert.Equal(["n:lexical", "n:semantic"], hits.Select(h => h.Chunk.ItemKey).Order());
        Assert.True(hits.Single(h => h.Chunk.ItemKey == "n:lexical").LexicalMatch);
        Assert.False(hits.Single(h => h.Chunk.ItemKey == "n:semantic").LexicalMatch);
    }

    [Fact]
    public void Search_IgnoresFunctionWordsAndNeedsMostWordsWithoutSemanticSupport()
    {
        Assert.Equal(["vpn", "zertifikat", "erneuert"], SemanticRanking.Terms("Wie habe ich das VPN-Zertifikat erneuert?"));

        var far = Unit(0, 0, 1);
        IndexedChunk[] chunks =
        [
            Indexed(Chunk("n:one", "Das ist nur ein VPN Hinweis"), far),
            Indexed(Chunk("n:two", "VPN Zertifikat wurde erneuert"), far),
            Indexed(Chunk("n:filler", "Ich habe das wie immer gemacht"), far),
        ];

        var hits = SemanticRanking.Search(Unit(1, 0, 0), "Wie habe ich das VPN-Zertifikat erneuert?", chunks, 10);

        Assert.Equal("n:two", Assert.Single(hits).Chunk.ItemKey);
    }

    [Fact]
    public void Search_GroupsChunksOfOneItemAndFoldsNearDuplicates()
    {
        var v = Unit(1, 0.01f, 0);
        IndexedChunk[] chunks =
        [
            Indexed(new SemanticChunk("n:a:0", "n:a", SemanticSource.Note, "a", Day, "eins"), v),
            Indexed(new SemanticChunk("n:a:1", "n:a", SemanticSource.Note, "a", Day, "zwei"), v),
            Indexed(Chunk("r:x#0", "drei", SemanticSource.Recording, minutes: 1), Unit(1, 0.02f, 0)),
            Indexed(Chunk("r:x#1", "vier", SemanticSource.Recording, minutes: 2), Unit(0.9f, 0.43f, 0)),
        ];

        var hits = SemanticRanking.Search(Unit(1, 0, 0), "nichts", chunks, 10);

        Assert.Equal(2, hits.Count);
        var first = hits[0];
        Assert.Equal("n:a", first.Chunk.ItemKey);
        Assert.Equal("r:x#0", Assert.Single(first.Duplicates).ItemKey);
        Assert.Equal("r:x#1", hits[1].Chunk.ItemKey);
    }

    [Fact]
    public void Browse_ListsEachItemOnceNewestFirst()
    {
        IndexedChunk[] chunks =
        [
            Indexed(new SemanticChunk("n:a:0", "n:a", SemanticSource.Note, "a", Day, "eins")),
            Indexed(new SemanticChunk("n:a:1", "n:a", SemanticSource.Note, "a", Day, "zwei")),
            Indexed(Chunk("r:x#0", "drei", SemanticSource.Recording, minutes: 5)),
        ];

        var items = SemanticRanking.Browse(chunks);

        Assert.Equal(["r:x#0", "n:a"], items.Select(h => h.Chunk.ItemKey));
        Assert.Equal("n:a:0", items[1].Chunk.Key);
    }

    [Fact]
    public void Similar_ExcludesOriginItem()
    {
        IndexedChunk[] chunks =
        [
            Indexed(new SemanticChunk("n:a:0", "n:a", SemanticSource.Note, "a", Day, "x"), Unit(1, 0, 0)),
            Indexed(new SemanticChunk("n:a:1", "n:a", SemanticSource.Note, "a", Day, "y"), Unit(1, 0.05f, 0)),
            Indexed(Chunk("n:b", "z"), Unit(0.9f, 0.2f, 0)),
            Indexed(Chunk("n:c", "w"), Unit(0, 1, 0)),
        ];

        var hits = SemanticRanking.Similar(chunks[0], chunks, 10);

        Assert.Equal("n:b", Assert.Single(hits).Chunk.ItemKey);
    }

    [Fact]
    public void DuplicateGroups_ReturnsOnlyRepeatedItemsNewestFirst()
    {
        IndexedChunk[] chunks =
        [
            Indexed(Chunk("r:1", "a", SemanticSource.Recording, 1), Unit(1, 0, 0)),
            Indexed(Chunk("r:2", "b", SemanticSource.Recording, 2), Unit(1, 0.01f, 0)),
            Indexed(Chunk("r:3", "c", SemanticSource.Recording, 3), Unit(1, 0.02f, 0)),
            Indexed(Chunk("r:4", "d", SemanticSource.Recording, 4), Unit(0, 1, 0)),
        ];

        var group = Assert.Single(SemanticRanking.DuplicateGroups(chunks));

        Assert.Equal("r:3", group.Chunk.ItemKey);
        Assert.Equal(["r:2", "r:1"], group.Duplicates.Select(d => d.ItemKey));
    }

    [Fact]
    public async Task Sync_EmbedsOnlyNewOrChangedChunks_AndPersistsVectors()
    {
        var db = Path.Combine(_root, SemanticIndex.FileName);
        var embedder = new WordEmbedder();
        List<SemanticChunk> chunks =
        [
            Chunk("n:a", "Zertifikat im Portal erneuern"),
            Chunk("n:b", "Apfelkuchen mit Zimt backen"),
        ];

        using (var index = new SemanticIndex(db, "test-model", () => embedder))
        {
            await index.SyncAsync(chunks, TestContext.Current.CancellationToken);
            Assert.Equal(2, index.Count);
            Assert.Equal(2, embedder.PassagesEmbedded);

            await index.SyncAsync(chunks, TestContext.Current.CancellationToken);
            Assert.Equal(2, embedder.PassagesEmbedded);

            chunks[1] = Chunk("n:b", "Apfelkuchen mit Sahne");
            await index.SyncAsync(chunks, TestContext.Current.CancellationToken);
            Assert.Equal(3, embedder.PassagesEmbedded);
        }

        var reopened = new WordEmbedder();
        using (var index = new SemanticIndex(db, "test-model", () => reopened))
        {
            await index.SyncAsync(chunks.Take(1).ToList(), TestContext.Current.CancellationToken);
            Assert.Equal(0, reopened.PassagesEmbedded);
            Assert.Equal(1, index.Count);

            var hit = Assert.Single(index.Search("Zertifikat erneuern", null));
            Assert.Equal("n:a", hit.Chunk.ItemKey);
        }

        var otherModel = new WordEmbedder();
        using (var index = new SemanticIndex(db, "other-model", () => otherModel))
        {
            await index.SyncAsync(chunks, TestContext.Current.CancellationToken);
            Assert.Equal(2, otherModel.PassagesEmbedded);
        }
    }

    [Fact]
    public async Task PruneClipboard_RemovesVectorsOfDeletedEntries()
    {
        var db = Path.Combine(_root, SemanticIndex.FileName);
        var embedder = new WordEmbedder();
        var chunks = SemanticChunker.FromClipboard(
        [
            new ClipboardEntry { Id = 7, Type = EntryType.Text, Content = "Erster langer Eintrag mit Text", Timestamp = Day },
            new ClipboardEntry { Id = 8, Type = EntryType.Text, Content = "Zweiter langer Eintrag mit Text", Timestamp = Day },
        ]).ToList();

        using (var index = new SemanticIndex(db, "test-model", () => embedder))
        {
            await index.SyncAsync(chunks, TestContext.Current.CancellationToken);
            index.PruneClipboard(new HashSet<long> { 8 });
            Assert.Equal(["c:8"], index.Snapshot().Select(c => c.Chunk.ItemKey));
        }

        using (var index = new SemanticIndex(db, "test-model", () => embedder))
        {
            await index.SyncAsync(chunks, TestContext.Current.CancellationToken);
            Assert.Equal(3, embedder.PassagesEmbedded);
        }
    }

    [Fact]
    public async Task Collect_ReadsKbNotesAndRecordings()
    {
        var kbDir = Path.Combine(_root, "kb", "notes");
        var recDir = Path.Combine(_root, "recordings");
        Directory.CreateDirectory(kbDir);
        Directory.CreateDirectory(recDir);
        var note = new Note
        {
            Id = "2026-11-05_09-00-00", Path = Path.Combine(_root, "n.md"), Created = Day, Modified = Day,
            Text = "VPN\nZertifikat jährlich erneuern",
        };
        File.WriteAllText(Path.Combine(kbDir, note.Id + ".md"), NoteStore.BuildKbMarkdown(note, Day));
        File.WriteAllText(Path.Combine(recDir, RecordingMarkdown.FileNameFor(Day)),
            RecordingMarkdown.SessionHeader(Day)
            + RecordingMarkdown.FormatSnapshot(Day.AddSeconds(3), "Edge", "Portal", ["Neues Zertifikat anfordern"], null, null));

        using var index = new SemanticIndex(Path.Combine(_root, SemanticIndex.FileName), "test-model", () => new WordEmbedder());
        var chunks = index.Collect([], kbDir, recDir);

        Assert.Equal([SemanticSource.Note, SemanticSource.Recording], chunks.Select(c => c.Source));
        Assert.Equal("VPN\nZertifikat jährlich erneuern", chunks[0].Text);
        await index.SyncAsync(chunks, TestContext.Current.CancellationToken);
        Assert.Equal(2, index.Count);
    }

    [Fact]
    public void Collect_AddsNotesNotExportedToKb()
    {
        var notesDir = Path.Combine(_root, "notes");
        var kbDir = Path.Combine(_root, "kb", "notes");
        Directory.CreateDirectory(notesDir);
        Directory.CreateDirectory(kbDir);
        var exported = new Note
        {
            Id = "2026-11-05_09-00-00", Path = Path.Combine(notesDir, "2026-11-05_09-00-00.md"), Created = Day, Modified = Day,
            Text = "VPN\nZertifikat jährlich erneuern",
        };
        File.WriteAllText(exported.Path, exported.Text + "\nspäter ergänzt");
        File.WriteAllText(Path.Combine(kbDir, exported.Id + ".md"), NoteStore.BuildKbMarkdown(exported, Day));
        File.WriteAllText(Path.Combine(notesDir, "2026-11-05_10-00-00.md"), "Einkauf\nDrucker-Toner bestellen");
        File.WriteAllText(Path.Combine(notesDir, "readme.md"), "Kein Notizname, wird ignoriert");

        using var index = new SemanticIndex(Path.Combine(_root, SemanticIndex.FileName), "test-model", () => new WordEmbedder());
        var chunks = index.Collect([], kbDir, Path.Combine(_root, "recordings"), notesDir);

        Assert.All(chunks, c => Assert.Equal(SemanticSource.Note, c.Source));
        Assert.Equal(["n:2026-11-05_09-00-00.md", "n:2026-11-05_10-00-00.md"], chunks.Select(c => c.ItemKey).Order());
        Assert.Equal("VPN\nZertifikat jährlich erneuern", chunks.Single(c => c.FilePath!.StartsWith(kbDir)).Text);
        Assert.Equal("Einkauf", chunks.Single(c => c.FilePath!.StartsWith(notesDir)).Title);
    }

    /// <summary>Network test, runs only when MERKAUTOMAT_E5_DOWNLOAD=1.</summary>
    [Fact]
    public async Task Download_FetchesPinnedFilesAndVerifiesHashes()
    {
        if (Environment.GetEnvironmentVariable("MERKAUTOMAT_E5_DOWNLOAD") != "1") Assert.Skip("MERKAUTOMAT_E5_DOWNLOAD not set");

        await EmbeddingModel.DownloadAsync(_root, null, TestContext.Current.CancellationToken);

        Assert.True(EmbeddingModel.IsInstalled(_root));
        Assert.Empty(Directory.GetFiles(EmbeddingModel.DirectoryFor(_root), "*.part"));
        using var model = new EmbeddingModel(_root);
        Assert.Equal(384, model.EmbedQuery("test").Length);
    }

    [Fact]
    public void ResolveModelsDir_PrefersCompleteBundledCopy()
    {
        var appDir = Path.Combine(_root, "app");
        var userDir = Path.Combine(_root, "user");
        Assert.Equal(userDir, EmbeddingModel.ResolveModelsDir(appDir, userDir));

        var bundled = EmbeddingModel.DirectoryFor(Path.Combine(appDir, "models"));
        Directory.CreateDirectory(bundled);
        foreach (var file in EmbeddingModel.Files)
        {
            using var fs = File.Create(Path.Combine(bundled, file.Name));
            fs.SetLength(file.Name.EndsWith(".onnx") ? 10 : file.Size);
        }
        Assert.Equal(userDir, EmbeddingModel.ResolveModelsDir(appDir, userDir));

        using (var fs = File.OpenWrite(Path.Combine(bundled, EmbeddingModel.Files[0].Name))) fs.SetLength(EmbeddingModel.Files[0].Size);
        Assert.Equal(Path.Combine(appDir, "models"), EmbeddingModel.ResolveModelsDir(appDir, userDir));
    }

    /// <summary>Uses the model the build ships next to the binaries (models\e5-small).</summary>
    [Fact]
    public void BundledModel_RanksRelevantPassagesFirst()
    {
        var modelsDir = Path.Combine(AppContext.BaseDirectory, "models");
        Assert.True(EmbeddingModel.IsInstalled(modelsDir), "models\\e5-small fehlt in der Build-Ausgabe");

        using var model = new EmbeddingModel(modelsDir);
        string[] passages =
        [
            "Das OpenVPN-Cert läuft ab. Neues Zertifikat über das Firmenportal anfordern und im Client importieren.",
            "Rezept für Apfelkuchen mit Zimt und Streuseln.",
            "Quarterly revenue grew 12 percent compared to last year.",
        ];
        var vectors = model.EmbedPassages(passages);
        var query = model.EmbedQuery("Wie erneuere ich das VPN-Zertifikat?");
        var chunks = passages.Select((p, i) => new IndexedChunk(Chunk($"n:{i}", p), vectors[i])).ToList();

        var hits = SemanticRanking.Search(query, "Wie erneuere ich das VPN-Zertifikat?", chunks, 10);

        Assert.Equal("n:0", hits[0].Chunk.ItemKey);
        Assert.DoesNotContain(hits, h => h.Chunk.ItemKey == "n:2");
        Assert.Equal(384, query.Length);
        Assert.Equal(1f, SemanticRanking.Dot(query, query), 3);
    }
}

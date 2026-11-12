using System.IO;
using System.Text.Json;
using Merkautomat.Models;
using Merkautomat.Services;
using Merkautomat.ViewModels;
using Xunit;

namespace Merkautomat.Tests;

public sealed class ContentMetadataTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "merkautomat-metadata-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Now = new(2026, 11, 8, 10, 20, 30);
    private string FileAt(string name) => Path.Combine(_root, name);
    private static ContentMetadata Metadata => new()
    {
        CapturedAt = Now, CaptureMethod = "Zwischenablage", ContentType = "Text", Application = "browser",
        WindowTitle = "Projektplanung", SourceUrl = "https://example.org/project",
    };

    public ContentMetadataTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Database_PreservesMetadataInLiteFullExportImportAndBackup()
    {
        using var db = new DatabaseService(FileAt("history.db"));
        db.Insert(new ClipboardEntry { Type = EntryType.Text, Content = "Planung mit Quellenkontext", Timestamp = Now, Metadata = Metadata });
        Assert.Equal(Metadata, Assert.Single(db.LoadAll()).Metadata);
        Assert.Equal(Metadata, Assert.Single(db.LoadAllLite()).Metadata);
        db.ExportAsJson(FileAt("export.json"));
        using var imported = new DatabaseService(FileAt("import.db"));
        Assert.Equal(1, imported.ImportFromJsonDetailed(FileAt("export.json")).imported);
        Assert.Equal(Metadata, Assert.Single(imported.LoadAllLite()).Metadata);
        db.AutoBackupIfNeeded(FileAt("backups"));
        var backup = Assert.Single(Directory.GetFiles(FileAt("backups"), "*.json"));
        using var restored = new DatabaseService(FileAt("restored.db"));
        Assert.Equal(1, restored.ImportFromJsonDetailed(backup).imported);
        Assert.Equal(Metadata, Assert.Single(restored.LoadAll()).Metadata);
    }

    [Fact]
    public void Notes_PreserveProvenanceThroughSaveSnapshotAndSummaryUpdate()
    {
        var metadata = Metadata with { CaptureMethod = "Diktat", SpeechModel = "Whisper base", SpeechLanguage = "de" };
        var (note, path) = NoteStore.CreateInKb(FileAt("notes"), FileAt("kb"), "# Planung\n\nErste Aussage", Now, metadata);
        var loaded = Assert.Single(NoteStore.Load(FileAt("notes"), FileAt("kb")));
        Assert.Equal(metadata, loaded.Metadata);
        Assert.Equal("# Planung\n\nErste Aussage", loaded.Text);
        BrainKnowledge.Save(FileAt("notes"), FileAt("kb"), note.Id, "# Neue Fassung", Now.AddHours(2));
        Assert.Equal(metadata, KnowledgeMetadata.ReadMarkdown(SecureFile.ReadAllText(path)));
        Assert.Equal(metadata, KnowledgeMetadata.ReadMarkdown(SecureFile.ReadAllText(note.Path)));
        Assert.Equal("# Neue Fassung", NoteStore.KbBody(SecureFile.ReadAllText(path)));
        Assert.Equal(metadata, KnowledgeMetadata.ReadMarkdown(SecureFile.ReadAllText(path + ".bak")));
    }

    [Fact]
    public void Notes_IndexOriginalTimeAndDomainWithoutEmbeddingMetadata()
    {
        var (_, path) = NoteStore.CreateInKb(FileAt("notes"), FileAt("kb"), "Eine lange Aussage zur Projektplanung", Now, Metadata);
        var chunk = Assert.Single(SemanticChunker.FromNote(path, SecureFile.ReadAllText(path), "Planung", Now.AddDays(4)));
        Assert.Equal(Now, chunk.Date);
        Assert.Equal("example.org", chunk.Domain);
        Assert.Equal(Metadata, chunk.Metadata);
        Assert.Equal("Eine lange Aussage zur Projektplanung", chunk.Text);
        Assert.DoesNotContain("merkautomat-metadata", chunk.Text);
    }

    [Fact]
    public void LegacyNote_UsesKnownCreationTimeAndDoesNotInventApplication()
    {
        const string body = "Eine alte Aussage ohne bekannte Herkunft";
        var chunk = Assert.Single(SemanticChunker.FromNote("2026-11-08_10-20-30.md", body, "Alt", Now.AddDays(3)));
        Assert.Equal(Now, chunk.Date);
        Assert.Null(chunk.Metadata!.Application);
        Assert.Null(chunk.Metadata.CaptureMethod);
        Assert.Equal(body, chunk.Text);
    }

    [Fact]
    public void Recording_DiffRetainsUrlEvenWhenUrlIsNotInNewText()
    {
        string[] before = ["URL: https://example.org/project", "Projektplanung im Team", "Nächste Schritte festlegen"];
        var first = RecordingMarkdown.FormatSnapshot(Now, "Browser", "Projekt", before, null, null);
        var next = RecordingMarkdown.FormatSnapshot(Now.AddMinutes(1), "Browser", "Projekt", [.. before, "Ergebnis morgen prüfen"], before, null);
        var markdown = "# Desktop-Aufnahme 2026-11-08 10:20:30\n\n" + first + next;
        var snapshots = RecordingMarkdown.SplitSnapshots(markdown);
        Assert.Equal(2, snapshots.Count);
        Assert.DoesNotContain("URL:", snapshots[1].Body);
        Assert.Equal("https://example.org/project", snapshots[1].Metadata!.SourceUrl);
        var chunks = SemanticChunker.FromRecording("session.md", markdown).ToList();
        Assert.Equal(2, chunks.Count);
        Assert.All(chunks, c => Assert.Equal("example.org", c.Domain));
        Assert.All(chunks, c => Assert.DoesNotContain("merkautomat-metadata", c.Text));
    }

    [Fact]
    public void ClipboardImage_KeepsHtmlSourceAndDimensions()
    {
        StaTestRunner.Run(() =>
        {
            var data = new System.Windows.DataObject();
            data.SetData("PNG", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII="));
            data.SetData(System.Windows.DataFormats.Html, ClipboardProcessor.BuildClipboardHtml("<img src='image.png'>", "https://example.org/project"));
            var entry = Assert.IsType<ClipboardEntry>(ClipboardProcessor.BuildEntry(data));
            Assert.Equal(EntryType.Image, entry.Type);
            var metadata = KnowledgeMetadata.ForClipboard(entry);
            Assert.Equal("https://example.org/project", metadata.SourceUrl);
            Assert.Equal(1, metadata.ImageWidth);
            Assert.Equal(1, metadata.ImageHeight);
            Assert.Equal(metadata, KnowledgeMetadata.ReadMarkdown(KnowledgeExport.ClipboardMarkdown(entry)));
            data.SetData(ClipboardProcessor.ExcludeFromMonitorFormat, new byte[] { 1 });
            Assert.Null(ClipboardProcessor.BuildEntry(data));
        });
    }

    [Fact]
    public void DuplicateCapture_SeparatesKnownSourcesAndPreservesFirstCaptureTime()
    {
        using var db = new DatabaseService(FileAt("history.db"));
        var vm = new PopupViewModel(db, runDatabase: PopupViewModel.InlineDatabase);
        ClipboardEntry Entry(ContentMetadata metadata, DateTime time) => new()
        {
            Type = EntryType.Text, Content = "Dieselbe Aussage mit unterschiedlicher Herkunft", Timestamp = time, Metadata = metadata,
        };
        vm.AddNow(Entry(Metadata, Now));
        vm.AddNow(Entry(Metadata with { SourceUrl = "https://other.example/project" }, Now.AddMinutes(1)));
        vm.AddNow(Entry(Metadata with { CapturedAt = Now.AddMinutes(2) }, Now.AddMinutes(2)));
        Assert.Equal(2, vm.Entries.Count);
        var latest = db.LoadAll()[0];
        Assert.Equal(Now.AddMinutes(2), latest.Timestamp);
        Assert.Equal(Now, latest.Metadata!.CapturedAt);
        Assert.Equal("https://example.org/project", latest.Metadata.SourceUrl);
    }

    [Fact]
    public void Brain_ConnectsNoteAndClipboardByPreservedDomain()
    {
        var note = Assert.Single(SemanticChunker.FromNote("note.md", KnowledgeMetadata.WithMetadata("Eine Aussage zur Projektplanung", Metadata), "Projekt", Now));
        var clipboard = Assert.Single(SemanticChunker.FromClipboard([new ClipboardEntry
        {
            Id = 1, Type = EntryType.Text, Content = "Eine andere Aussage zur Zusammenarbeit", Timestamp = Now.AddDays(1), Metadata = Metadata,
        }]));
        var graph = KnowledgeGraph.Build([new(note, [1f, 0f]), new(clipboard, [0f, 1f])], new BrainOptions());
        Assert.Contains(graph.Edges, e => e.Kind == GraphEdgeKind.Domain);
    }

    [Fact]
    public void QuestionInput_ContainsMetadataAsSourceData()
    {
        var chunk = new SemanticChunk("c:1:0", "c:1", SemanticSource.Clipboard, "Projekt", Now, "Inhalt", Metadata: Metadata);
        using var json = JsonDocument.Parse(KnowledgeAnswer.BuildInput("Woher stammt das?", [new AnswerSource(1, chunk, "Inhalt")]));
        var source = json.RootElement.GetProperty("sources")[0];
        Assert.Contains("https://example.org/project", source.GetProperty("metadata").GetString());
        Assert.Equal("Inhalt", source.GetProperty("text").GetString());
    }

    [Fact]
    public void JsonImport_DoesNotCollapseDifferentSourcesWithSameTimestampAndText()
    {
        using var db = new DatabaseService(FileAt("source.db"));
        foreach (var url in new[] { "https://first.example/page", "https://second.example/page" })
            db.Insert(new ClipboardEntry { Type = EntryType.Text, Content = "Gleicher Text", Timestamp = Now, Metadata = Metadata with { SourceUrl = url } });
        db.ExportAsJson(FileAt("export.json"));
        using var imported = new DatabaseService(FileAt("target.db"));
        Assert.Equal(2, imported.ImportFromJsonDetailed(FileAt("export.json")).imported);
        Assert.Equal(2, imported.ImportFromJsonDetailed(FileAt("export.json")).skipped);
        Assert.Equal(2, imported.LoadAll().Count);
    }

    [Fact]
    public void CombinedMarkdownImport_PreservesAllSourcesWithoutTurningThemIntoContent()
    {
        var entries = new[] { "https://first.example/page", "https://second.example/page" }.Select((url, i) => new ClipboardEntry
        {
            Id = i + 1, Type = EntryType.Text, Content = "Ein ausreichend langer Inhalt " + i, Timestamp = Now,
            Metadata = Metadata with { SourceUrl = url },
        }).ToList();
        var articles = entries.Select(e => new KbArticle(KbArticleKind.Clipboard, "Quelle", Now, $"{e.Id}.md", KnowledgeExport.ClipboardMarkdown(e))).ToList();
        var exported = KnowledgeExport.BuildCombined(articles, Now);
        var (note, path) = NoteStore.CreateInKb(FileAt("notes"), FileAt("kb"), NoteStore.ImportText("export.md", exported), Now.AddDays(1));
        var metadata = KnowledgeMetadata.ReadMarkdown(SecureFile.ReadAllText(path));
        Assert.Equal(["first.example", "second.example"], KnowledgeMetadata.Domains(metadata));
        Assert.DoesNotContain("merkautomat-metadata", note.Text);
        Assert.Contains("Ein ausreichend langer Inhalt 1", note.Text);
        Assert.Equal(Now, metadata!.CapturedAt);
    }

    [Fact]
    public void MixedNote_UsesAdditionalSourceDomainForBrainLinksAfterReload()
    {
        var mixed = KnowledgeMetadata.Combine(new ContentMetadata
        {
            CapturedAt = Now, CaptureMethod = "Diktat", SpeechLanguage = "de", SpeechModel = "Whisper base",
        }, Metadata);
        var (_, path) = NoteStore.CreateInKb(FileAt("notes"), FileAt("kb"), "Gesprochene und eingefügte Aussagen", Now, mixed);
        var note = Assert.Single(SemanticChunker.FromNote(path, SecureFile.ReadAllText(path), "Gemischt", Now));
        Assert.Null(note.Domain);
        var other = new SemanticChunk("c:1:0", "c:1", SemanticSource.Clipboard, "Quelle", Now.AddDays(1), "Anderer Inhalt", Domain: "example.org");
        var graph = KnowledgeGraph.Build([new(note, [1f, 0f]), new(other, [0f, 1f])], new BrainOptions());
        Assert.Contains(graph.Edges, edge => edge.Kind == GraphEdgeKind.Domain);
        Assert.Contains("Whisper base", KnowledgeMetadata.Describe(note.Metadata));
        Assert.Contains("example.org/project", KnowledgeMetadata.Describe(note.Metadata));
    }

    [Fact]
    public void Markdown_PreservesUnknownFieldsAndEscapesCommentTerminators()
    {
        var metadata = KnowledgeMetadata.Deserialize("{\"Application\":\"a --> b\",\"FutureField\":\"kept\"}")!;
        var markdown = KnowledgeMetadata.WithMetadata("# Titel\n\nInhalt", metadata);
        Assert.Equal(1, markdown.Split(" -->").Length - 1);
        Assert.Equal("kept", KnowledgeMetadata.ReadMarkdown(markdown)!.AdditionalFields!["FutureField"].GetString());
        Assert.Contains("Anwendung: a --> b", KnowledgeMetadata.ForDisplay(markdown));
        Assert.DoesNotContain("merkautomat-metadata", KnowledgeMetadata.ForDisplay(markdown));
        const string broken = "<!-- merkautomat-metadata: not-json -->";
        Assert.Null(KnowledgeMetadata.ReadMarkdown(broken));
        Assert.Equal(broken, KnowledgeMetadata.WithoutMetadata(broken));
    }
}

using System.IO;
using System.Text.Json;
using ClipwellWin.Models;
using ClipwellWin.Services;
using Xunit;
using ClipwellWin.Views;

namespace ClipwellWin.Tests;

public sealed class BrainKnowledgeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clipwell-brain-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0);
    private string Notes => Path.Combine(_root, "notes");
    private string Kb => Path.Combine(_root, "kb");
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public void Summary_DeduplicatesStatementsAndKeepsBothSources()
    {
        const string statement = "Das Zertifikat wird im Firmenportal erneuert.";
        BrainSource[] sources = [new("c:1", "Portal", statement, "https://example.org", Now), new("n:a.md", "Gespräch", statement, "notes/a.md", Now)];
        var text = BrainKnowledge.Summarize("VPN", "Arbeit", sources);
        Assert.Equal(1, text.Split(statement).Length - 1);
        Assert.Contains("Kategorie: Arbeit", text);
        Assert.Contains("[1]", text);
        Assert.Contains("Eintrag: c:1", text);
        Assert.Contains("Eintrag: n:a.md", text);
        Assert.Contains("https://example.org", text);
    }

    [Fact]
    public void Summary_DoesNotInventStatementsForEmptySources()
    {
        var text = BrainKnowledge.Summarize("Leer", "", []);
        Assert.Contains("Keine ausreichend langen Originalaussagen", text);
    }

    [Fact]
    public void Save_UpdatesSamePairAndBacksUpPreviousVersionWithoutChangingSource()
    {
        var source = NoteStore.CreateInKb(Notes, Kb, "Quelle unveraendert", Now.AddDays(-1));
        var first = BrainKnowledge.Save(Notes, Kb, null, "# Erste Fassung", Now);
        var updated = BrainKnowledge.Save(Notes, Kb, first.NoteId, "# Zweite Fassung", Now.AddMinutes(1));
        Assert.Equal(first, updated);
        Assert.Equal(2, Directory.GetFiles(Notes, "*.md").Length);
        Assert.Equal(2, Directory.GetFiles(Kb, "*.md").Length);
        Assert.Equal("# Zweite Fassung", File.ReadAllText(Path.Combine(Notes, first.NoteId + ".md")));
        Assert.Contains("# Zweite Fassung", File.ReadAllText(first.KbPath));
        Assert.Contains("# Erste Fassung", File.ReadAllText(first.KbPath + ".bak"));
        Assert.Equal("Quelle unveraendert", File.ReadAllText(source.Note.Path));
    }

    [Fact]
    public void Save_RejectsPathTraversalBeforeWriting()
    {
        Assert.Throws<ArgumentException>(() => BrainKnowledge.Save(Notes, Kb, "../2026-11-06_12-00-00", "text", Now));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void Save_RejectsEmptyDraft()
        => Assert.Throws<ArgumentException>(() => BrainKnowledge.Save(Notes, Kb, null, "  ", Now));

    [Fact]
    public void Build_RestoresAnnotationsByStableItemKeyAfterReordering()
    {
        var settings = new AppSettings
        {
            BrainAnnotations = new() { ["n:a.md"] = new("VPN-Zugang", "Arbeit") },
            BrainSummaries = [new() { NoteId = "2026-11-06_12-00-00", Title = "VPN", SourceKeys = ["n:a.md"] }],
        };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        IndexedChunk[] chunks =
        [
            new(new("n:b.md:0", "n:b.md", SemanticSource.Note, "b", Now, "Zertifikat Firmenportal"), [1f, 0f]),
            new(new("n:a.md:0", "n:a.md", SemanticSource.Note, "a", Now.AddMinutes(-1), "Zertifikat erneuern"), [1f, 0f]),
        ];
        var graph = KnowledgeGraph.Build(chunks, new() { Annotations = restored.BrainAnnotations });
        var topic = Assert.Single(graph.Clusters);
        Assert.Equal("VPN-Zugang", topic.Label);
        Assert.Equal("Arbeit", topic.Category);
        Assert.Equal("n:a.md", Assert.Single(Assert.Single(restored.BrainSummaries).SourceKeys));
    }

    [Fact]
    public void Build_FiltersInterfaceWordsFromTopicLabels()
    {
        IndexedChunk[] chunks =
        [
            new(new("c:1:0", "c:1", SemanticSource.Clipboard, "Strg Speichern", Now, "Strg Speichern Auswählen Zertifikat Firmenportal"), [1f, 0f]),
            new(new("c:2:0", "c:2", SemanticSource.Clipboard, "Strg Speichern", Now, "Strg Speichern Auswählen Zertifikat erneuern"), [1f, 0f]),
        ];
        var topic = Assert.Single(KnowledgeGraph.Build(chunks, new()).Clusters);
        Assert.Contains("zertifikat", topic.Keywords);
        Assert.DoesNotContain("strg", topic.Keywords);
        Assert.DoesNotContain("speichern", topic.Keywords);
        Assert.DoesNotContain("auswählen", topic.Keywords);
    }

    [Fact]
    public void Build_LinksSummaryToItsSourcesEvenWithoutSemanticSimilarity()
    {
        IndexedChunk[] chunks =
        [
            new(new("c:1:0", "c:1", SemanticSource.Clipboard, "Quelle", Now, "Zertifikat"), [1f, 0f]),
            new(new("n:a.md:0", "n:a.md", SemanticSource.Note, "Zusammenfassung", Now.AddHours(1), "Portal"), [0f, 1f]),
        ];
        var graph = KnowledgeGraph.Build(chunks, new()
        {
            SummarySources = new Dictionary<string, IReadOnlyList<string>> { ["n:a.md"] = ["c:1", "missing", "c:1"] },
        });
        var edge = Assert.Single(graph.Edges);
        Assert.Equal(GraphEdgeKind.Summary, edge.Kind);
        Assert.Contains("Quelle", edge.Detail);
    }

    [Fact]
    public void Dialog_RestoresExistingDraftAndItsSourceSelection()
    {
        StaTestRunner.Run(() =>
        {
            var a = new BrainSource("c:1", "Quelle A", "Text", "Zwischenablage", Now);
            var b = new BrainSource("c:2", "Quelle B", "Text", "Zwischenablage", Now);
            var dialog = new BrainKnowledgeDialog("Thema", "Arbeit", ["Arbeit"],
                [new() { Source = a, Selected = true }, new() { Source = b, Selected = true }],
                [new("Neu", null, ""), new("Vorhandene Fassung", "2026-11-06_12-00-00", "Eigene redigierte Fassung", ["c:2"])]);
            Assert.Equal("Eigene redigierte Fassung", dialog.Draft);
            Assert.Equal("2026-11-06_12-00-00", dialog.SummaryId);
            Assert.Equal("c:2", Assert.Single(dialog.SelectedSources).Key);
            dialog.Close();
        });
    }

    [Fact]
    public void Dialog_PreservesEditedDraftAndSelectionWhenSwitchingTargets()
        => StaTestRunner.Run(() =>
        {
            var a = new BrainSource("c:1", "Quelle A", "Text A", "Zwischenablage", Now);
            var b = new BrainSource("c:2", "Quelle B", "Text B", "Zwischenablage", Now);
            var sources = new List<BrainKnowledgeDialog.SourceChoice>
                { new() { Source = a, Selected = true }, new() { Source = b, Selected = true } };
            var dialog = new BrainKnowledgeDialog("Thema", "", [], sources,
                [new("Neu", null, ""), new("Bestehend", "saved", "Bisheriger Text", ["c:2"])], summarize: false);
            Assert.Equal(2, dialog.SelectedSources.Count);
            var editor = (System.Windows.Controls.TextBox)dialog.FindName("SummaryText");
            var target = (System.Windows.Controls.ComboBox)dialog.FindName("SummaryTarget");
            editor.Text = "Mein neuer Entwurf";
            target.SelectedIndex = 1;
            Assert.Single(dialog.SelectedSources);
            editor.Text = "Meine überarbeitete Fassung";
            target.SelectedIndex = 0;
            Assert.Equal("Mein neuer Entwurf", dialog.Draft);
            Assert.Equal(2, dialog.SelectedSources.Count);
            target.SelectedIndex = 1;
            Assert.Equal("Meine überarbeitete Fassung", dialog.Draft);
            Assert.Equal("c:2", Assert.Single(dialog.SelectedSources).Key);
            dialog.Close();
        });

    [Fact]
    public void Dialog_FilterKeepsHiddenSourceSelectionAndPreviewUsesCurrentTarget()
        => StaTestRunner.Run(() =>
        {
            var a = new BrainSource("c:1", "Quelle A", "Inhalt A", "Zwischenablage", Now);
            var b = new BrainSource("c:2", "Quelle B", "Inhalt B", "Zwischenablage", Now);
            var dialog = new BrainKnowledgeDialog("Thema", "", [],
                [new() { Source = a, Selected = true }, new() { Source = b, Selected = true }],
                [new("Neu", null, "# Neu"), new("Bestehend", "saved", "# Bestehend", ["c:2"])], summarize: false);
            ((System.Windows.Controls.TextBox)dialog.FindName("SourceFilter")).Text = "Quelle A";
            Assert.Single(((System.Windows.Controls.ListBox)dialog.FindName("SourceList")).Items.Cast<object>());
            Assert.Equal(2, dialog.SelectedSources.Count);
            var previewToggle = (System.Windows.Controls.CheckBox)dialog.FindName("PreviewToggle");
            previewToggle.IsChecked = true;
            previewToggle.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.CheckBox.ClickEvent));
            ((System.Windows.Controls.ComboBox)dialog.FindName("SummaryTarget")).SelectedIndex = 1;
            var preview = (System.Windows.Controls.RichTextBox)dialog.FindName("SummaryPreview");
            Assert.Contains("Bestehend", new System.Windows.Documents.TextRange(preview.Document.ContentStart, preview.Document.ContentEnd).Text);
            dialog.Close();
        });
}

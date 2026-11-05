using System.IO;
using ClipwellWin.Services;
using Xunit;

namespace ClipwellWin.Tests;

public sealed class NoteStoreTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 5, 9, 15, 30);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clipwell-notes-" + Guid.NewGuid().ToString("N"));
    private string NotesDir => Path.Combine(_root, "notes");
    private string KbDir => Path.Combine(_root, "kb", "notes");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void TitleOf_UsesFirstNonEmptyLineWithoutHeadingMarks()
    {
        Assert.Equal("Einkauf", NoteStore.TitleOf("\n\n  ## Einkauf  \nMilch"));
        Assert.Equal("(leere Notiz)", NoteStore.TitleOf("  \n "));
        Assert.EndsWith("…", NoteStore.TitleOf(new string('x', 200)));
    }

    [Fact]
    public void Create_WritesMarkdownFileAndAvoidsIdCollisions()
    {
        var first = NoteStore.Create(NotesDir, "Erste\r\nZeile", Now);
        var second = NoteStore.Create(NotesDir, "Zweite", Now);

        Assert.Equal("2026-11-05_09-15-30", first.Id);
        Assert.Equal("2026-11-05_09-15-30-2", second.Id);
        Assert.Equal("Erste\nZeile", File.ReadAllText(first.Path));
        Assert.True(NoteStore.TryParseId(second.Id, out var created));
        Assert.Equal(Now, created);
    }

    [Fact]
    public void Load_ReturnsNewestFirstAndIgnoresForeignFiles()
    {
        NoteStore.Create(NotesDir, "alt", Now.AddDays(-1));
        NoteStore.Create(NotesDir, "neu", Now);
        File.WriteAllText(Path.Combine(NotesDir, "readme.md"), "kein Notiz-Name");

        var notes = NoteStore.Load(NotesDir, KbDir);

        Assert.Equal(["neu", "alt"], notes.Select(n => n.Text));
        Assert.All(notes, n => Assert.False(n.IsInKb));
    }

    [Fact]
    public void Load_HandlesMissingDirectory()
        => Assert.Empty(NoteStore.Load(NotesDir, KbDir));

    [Fact]
    public void ExportToKb_WritesSnapshotThatSurvivesEditsAndDeletion()
    {
        var note = NoteStore.Create(NotesDir, "# Meeting\n\nPunkt 1", Now);
        var path = NoteStore.ExportToKb(note, KbDir, Now.AddMinutes(5));

        Assert.Equal(
            "# Notiz: Meeting\n\n- Erstellt: 2026-11-05 09:15:30\n- Exportiert: 2026-11-05 09:20:30\n\n# Meeting\n\nPunkt 1\n",
            File.ReadAllText(path));

        var loaded = NoteStore.Load(NotesDir, KbDir).Single();
        Assert.True(loaded.IsInKb);
        Assert.Equal(Now.AddMinutes(5), loaded.KbExportedAt);

        NoteStore.Save(loaded, "# Meeting\n\nPunkt 1\nPunkt 2");
        NoteStore.Delete(loaded);

        var kb = NoteStore.LoadKbNotes(KbDir).Single();
        Assert.Equal("Meeting", kb.Title);
        Assert.Equal(Now, kb.Created);
        Assert.Equal(Now.AddMinutes(5), kb.Exported);
        Assert.DoesNotContain("Punkt 2", File.ReadAllText(kb.Path));
    }

    [Fact]
    public void CreateInKb_SavesNoteAndKbSnapshotInOneStep()
    {
        var (note, kbPath) = NoteStore.CreateInKb(NotesDir, KbDir, "Diktat aus dem Popup", Now);

        Assert.True(File.Exists(note.Path));
        Assert.Equal(NoteStore.KbPathFor(KbDir, note.Id), kbPath);
        Assert.True(note.IsInKb);
        Assert.False(note.KbOutdated);
        var kb = Assert.Single(NoteStore.LoadKbNotes(KbDir));
        Assert.Equal("Diktat aus dem Popup", kb.Title);
        Assert.True(Assert.Single(NoteStore.Load(NotesDir, KbDir)).IsInKb);
    }

    [Fact]
    public void KbOutdated_WhenModifiedAfterExport()
    {
        var note = new Note { Modified = Now.AddMinutes(10), KbExportedAt = Now };
        Assert.True(note.KbOutdated);
        note.KbExportedAt = Now.AddMinutes(10);
        Assert.False(note.KbOutdated);
    }

    [Fact]
    public void KnowledgeSummary_ListsExportedNotes()
    {
        var notes = new List<KbNote> { new("a", "a.md", "Idee Projekt X", Now, Now) };

        var md = KnowledgeSummaryBuilder.Build([], [], Now, notes);

        Assert.Contains("## Notizen\n\n- Exportierte Notizen: 1\n\n- 05.11.2026 09:15 · Idee Projekt X\n", md);
    }

    [Theory]
    [InlineData("[Musik]", true)]
    [InlineData("(Stille)", true)]
    [InlineData("*Applaus*", true)]
    [InlineData("", true)]
    [InlineData("Termin am Montag", false)]
    public void Dictation_FiltersNonSpeechSegments(string text, bool noise)
        => Assert.Equal(noise, DictationService.IsNoise(text));

    [Fact]
    public void Dictation_PrefersBundledModelOverDownloadedCopy()
    {
        var appDir = Path.Combine(_root, "app");
        var modelsDir = Path.Combine(_root, "models");

        Assert.Equal(Path.Combine(modelsDir, "ggml-base.bin"), DictationService.ResolveModelPath(appDir, modelsDir, "base"));

        Directory.CreateDirectory(Path.Combine(appDir, "models"));
        File.WriteAllText(Path.Combine(appDir, "models", "ggml-base.bin"), "x");

        Assert.Equal(Path.Combine(appDir, "models", "ggml-base.bin"), DictationService.ResolveModelPath(appDir, modelsDir, "base"));
        Assert.Equal(Path.Combine(modelsDir, "ggml-small.bin"), DictationService.ResolveModelPath(appDir, modelsDir, "small"));
    }

    [Theory]
    [InlineData(null, "base")]
    [InlineData("SMALL", "small")]
    [InlineData("large", "base")]
    public void Dictation_NormalizesModelName(string? input, string expected)
        => Assert.Equal(expected, DictationService.NormalizeModel(input));
}

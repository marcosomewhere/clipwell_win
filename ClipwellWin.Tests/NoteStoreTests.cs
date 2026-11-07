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
        Assert.All(notes, n => Assert.Null(n.KbExportedAt));
    }

    [Fact]
    public void Load_HandlesMissingDirectory()
        => Assert.Empty(NoteStore.Load(NotesDir, KbDir));

    [Fact]
    public void LoadKnowledgeNotes_IncludesLegacyNotesAndKeepsExportedSnapshots()
    {
        var legacy = NoteStore.Create(NotesDir, "Alte Sprachnotiz", Now.AddDays(-1));
        var exported = NoteStore.Create(NotesDir, "Exportierter Stand", Now);
        var kbPath = NoteStore.ExportToKb(exported, KbDir, Now);
        NoteStore.Save(exported, "Spaetere Aenderung");

        var notes = NoteStore.LoadKnowledgeNotes(NotesDir, KbDir);

        Assert.Equal(2, notes.Count);
        Assert.Equal(kbPath, notes[0].Path);
        Assert.Equal("Exportierter Stand", notes[0].Title);
        Assert.Equal(legacy.Path, notes[1].Path);
        Assert.Equal("Alte Sprachnotiz", notes[1].Title);
        Assert.Equal("Spaetere Aenderung", File.ReadAllText(exported.Path));
        Assert.False(File.Exists(NoteStore.KbPathFor(KbDir, legacy.Id)));
    }

    [Fact]
    public void LoadKnowledgeNotes_KeepsSnapshotWithoutOriginalNote()
    {
        var note = NoteStore.Create(NotesDir, "Nur im Ideenkombinat", Now);
        var kbPath = NoteStore.ExportToKb(note, KbDir, Now);
        NoteStore.Delete(note);

        var loaded = Assert.Single(NoteStore.LoadKnowledgeNotes(NotesDir, KbDir));

        Assert.Equal(kbPath, loaded.Path);
    }

    [Fact]
    public void ExportToKb_WritesSnapshotThatSurvivesEditsAndDeletion()
    {
        var note = NoteStore.Create(NotesDir, "# Meeting\n\nPunkt 1", Now);
        var path = NoteStore.ExportToKb(note, KbDir, Now.AddMinutes(5));

        Assert.Equal(
            "# Notiz: Meeting\n\n- Erstellt: 2026-11-05 09:15:30\n- Exportiert: 2026-11-05 09:20:30\n\n# Meeting\n\nPunkt 1\n",
            File.ReadAllText(path));

        var loaded = NoteStore.Load(NotesDir, KbDir).Single();
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
        Assert.NotNull(note.KbExportedAt);
        var kb = Assert.Single(NoteStore.LoadKbNotes(KbDir));
        Assert.Equal("Diktat aus dem Popup", kb.Title);
        Assert.NotNull(Assert.Single(NoteStore.Load(NotesDir, KbDir)).KbExportedAt);
    }

    [Fact]
    public void KnowledgeSummary_ListsExportedNotes()
    {
        var notes = new List<KbNote> { new("a", "a.md", "Idee Projekt X", Now, Now) };

        var md = KnowledgeSummaryBuilder.Build([], [], Now, notes);

        Assert.Contains("## Sprechwerk\n\n- Exportierte Notizen: 1\n\n- 05.11.2026 09:15 · Idee Projekt X\n", md);
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

    [Fact]
    public void DecodeImport_HandlesBomUtf8AndLegacyAnsi()
    {
        Assert.Equal("Grüße", NoteStore.DecodeImport([0xEF, 0xBB, 0xBF, .. System.Text.Encoding.UTF8.GetBytes("Grüße")]));
        Assert.Equal("Grüße", NoteStore.DecodeImport(System.Text.Encoding.UTF8.GetBytes("Grüße")));
        Assert.Equal("Grüße", NoteStore.DecodeImport(System.Text.Encoding.Unicode.GetPreamble().Concat(System.Text.Encoding.Unicode.GetBytes("Grüße")).ToArray()));
        Assert.Equal("Grüße", NoteStore.DecodeImport([0x47, 0x72, 0xFC, 0xDF, 0x65]));
    }

    [Theory]
    [InlineData("Projekt.txt", "Erste Zeile\r\nzweite", "# Projekt\n\nErste Zeile\nzweite")]
    [InlineData("Projekt.md", "# Eigener Titel\n\nText", "# Eigener Titel\n\nText")]
    [InlineData("leer.md", "  \r\n ", "")]
    public void ImportText_UsesFileNameAsHeadingWhenMissing(string name, string content, string expected)
        => Assert.Equal(expected, NoteStore.ImportText(name, content));

    [Theory]
    [InlineData("a.md", true)]
    [InlineData("b.TXT", true)]
    [InlineData("c.markdown", true)]
    [InlineData("d.docx", false)]
    public void CanImport_AcceptsMarkdownAndText(string path, bool expected)
        => Assert.Equal(expected, NoteStore.CanImport(path));
}

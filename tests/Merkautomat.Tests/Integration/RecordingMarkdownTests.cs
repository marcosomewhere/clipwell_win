using System.IO;
using Merkautomat.Models;
using Merkautomat.Services;
using Xunit;

namespace Merkautomat.Tests;

public class RecordingMarkdownTests
{
    private static readonly DateTime Start = new(2026, 11, 5, 14, 32, 1);

    [Fact]
    public void NormalizeLines_CollapsesWhitespaceAndDropsEmptiesAndDuplicates()
    {
        var lines = RecordingMarkdown.NormalizeLines(["  Kunde:   Muster GmbH ", "", null, "Vertrag\nAktiv", "Kunde: Muster GmbH"]);

        Assert.Equal(["Kunde: Muster GmbH", "Vertrag", "Aktiv"], lines);
    }

    [Fact]
    public void NormalizeLines_CapsLineCount()
    {
        var raw = Enumerable.Range(0, RecordingMarkdown.MaxLinesPerSnapshot + 50).Select(i => $"Zeile {i}");

        Assert.Equal(RecordingMarkdown.MaxLinesPerSnapshot, RecordingMarkdown.NormalizeLines(raw).Count);
    }

    [Fact]
    public void FormatSnapshot_FirstSnapshotWritesFullContent()
    {
        var md = RecordingMarkdown.FormatSnapshot(Start.AddSeconds(7), "Google Chrome", "Kundenportal",
            ["Kunde: Muster GmbH", "Vertragsstatus: Aktiv"], null, null);

        Assert.Equal(
            "## 14:32:08 - Google Chrome: Kundenportal\n\n### Sichtbarer Inhalt\n\n- Kunde: Muster GmbH\n- Vertragsstatus: Aktiv\n\n",
            KnowledgeMetadata.WithoutMetadata(md));
    }

    [Fact]
    public void FormatSnapshot_SameWindowWritesOnlyNewLines()
    {
        var md = RecordingMarkdown.FormatSnapshot(Start, "Editor", "notiz.txt",
            ["a", "b", "c", "neu"], ["a", "b", "c", "alt"], null);

        Assert.Contains("### Neu sichtbar\n\n- neu\n", md);
        Assert.DoesNotContain("- a\n", md);
        Assert.Contains("### Änderung\n\n1 neue Zeile.", md);
    }

    [Fact]
    public void NormalizeLines_KeepsRepeatedTableSeparators()
    {
        var lines = RecordingMarkdown.NormalizeLines(["| a | b |", "| --- | --- |", "| c | d |", "| --- | --- |"]);

        Assert.Equal(["| a | b |", "| --- | --- |", "| c | d |", "| --- | --- |"], lines);
    }

    [Fact]
    public void FormatSnapshot_WritesTableRowsWithoutBullets()
    {
        var md = RecordingMarkdown.FormatSnapshot(Start, "Opera", "Seite",
            ["Intro", "| a | b |", "| --- | --- |", "| c | d |", "Schluss"], null, null);

        Assert.Contains("- Intro\n\n| a | b |\n| --- | --- |\n| c | d |\n\n- Schluss\n\n", md);
    }

    [Fact]
    public void NormalizeLines_DropsObjectPlaceholdersAndSymbolOnlyLines()
    {
        var lines = RecordingMarkdown.NormalizeLines(["￼", " - | ", "￼ Chat", "11:45:28"]);

        Assert.Equal(["Chat", "11:45:28"], lines);
    }

    [Theory]
    [InlineData("Minimieren", true)]
    [InlineData("close", true)]
    [InlineData("Zurück um kleine Menge", true)]
    [InlineData("\"Rechner\" minimieren", true)]
    [InlineData("Speichern", false)]
    public void IsWindowChromeName_FiltersCaptionAndScrollButtons(string name, bool expected)
        => Assert.Equal(expected, RecordingMarkdown.IsWindowChromeName(name));

    [Fact]
    public void FormatSnapshot_WindowSwitchIsNamedInChange()
    {
        var md = RecordingMarkdown.FormatSnapshot(Start, "Explorer", "Downloads", ["Datei.pdf"], null, "Editor: notiz.txt");

        Assert.Contains("### Änderung\n\nFensterwechsel von \"Editor: notiz.txt\".", md);
    }

    [Fact]
    public void FormatSnapshot_ColonInAppNameDoesNotBreakHeading()
    {
        var md = RecordingMarkdown.FormatSnapshot(Start, "App: Pro", "Titel: mit Doppelpunkt", ["x"], null, null);

        Assert.StartsWith("## 14:32:01 - App Pro: Titel: mit Doppelpunkt\n", md);
    }

    [Fact]
    public void ParseSession_RoundTripsHeaderSnapshotsAndFooter()
    {
        var text = RecordingMarkdown.SessionHeader(Start)
            + RecordingMarkdown.FormatSnapshot(Start.AddSeconds(7), "Chrome", "Portal - Seite", ["x"], null, null)
            + RecordingMarkdown.FormatSnapshot(Start.AddMinutes(2), "Editor", "a: b", ["y"], null, "Chrome: Portal - Seite")
            + RecordingMarkdown.SessionFooter(Start, Start.AddMinutes(3), 2);

        var session = RecordingMarkdown.ParseSession("x.md", text)!;

        Assert.Equal(Start, session.Start);
        Assert.Equal(Start.AddMinutes(3), session.End);
        Assert.False(session.IsOpen);
        Assert.Equal(TimeSpan.FromMinutes(3), session.Duration);
        Assert.Equal(2, session.Snapshots.Count);
        Assert.Equal(new RecordingSnapshot(Start.AddSeconds(7), "Chrome", "Portal - Seite"), session.Snapshots[0]);
        Assert.Equal("a: b", session.Snapshots[1].Title);
    }

    [Fact]
    public void ParseSession_ContentLinesNeverCountAsSnapshots()
    {
        var text = RecordingMarkdown.SessionHeader(Start)
            + RecordingMarkdown.FormatSnapshot(Start, "Editor", "md", RecordingMarkdown.NormalizeLines(["## 10:00:00 - Fake: Heading", "# Desktop-Aufnahme 2020-01-01 00:00:00"]), null, null);

        var session = RecordingMarkdown.ParseSession("x.md", text)!;

        Assert.Single(session.Snapshots);
        Assert.Equal(Start, session.Start);
        Assert.True(session.IsOpen);
    }

    [Fact]
    public void ParseSession_SnapshotsAfterMidnightMoveToNextDay()
    {
        var lateStart = new DateTime(2026, 11, 5, 23, 59, 50);
        var text = RecordingMarkdown.SessionHeader(lateStart)
            + RecordingMarkdown.FormatSnapshot(lateStart.AddSeconds(20), "Editor", "t", ["x"], null, null);

        var session = RecordingMarkdown.ParseSession("x.md", text)!;

        Assert.Equal(new DateTime(2026, 11, 6, 0, 0, 10), session.Snapshots[0].Time);
    }

    [Fact]
    public void ParseSession_ReturnsNullWithoutHeader()
        => Assert.Null(RecordingMarkdown.ParseSession("x.md", "# Irgendwas\n"));

    [Fact]
    public void LoadSessions_ReadsDirectoryNewestFirst()
    {
        var dir = Path.Combine(Path.GetTempPath(), "merkautomat-rec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, RecordingMarkdown.FileNameFor(Start)), RecordingMarkdown.SessionHeader(Start));
            var later = Start.AddHours(1);
            File.WriteAllText(Path.Combine(dir, RecordingMarkdown.FileNameFor(later)), RecordingMarkdown.SessionHeader(later));
            File.WriteAllText(Path.Combine(dir, "fremd.md"), "keine Aufnahme");

            var sessions = RecordingMarkdown.LoadSessions(dir);

            Assert.Equal([later, Start], sessions.Select(s => s.Start));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void KnowledgeSummary_CombinesClipboardAndRecordings()
    {
        var now = new DateTime(2026, 11, 5, 18, 0, 0);
        var entries = new List<ClipboardEntry>
        {
            new() { Type = EntryType.Url, Content = "https://www.example.com/a", ContentKind = "example.com", Timestamp = now.AddHours(-1) },
            new() { Type = EntryType.Url, Content = "https://example.com/b", Timestamp = now.AddHours(-2), UseCount = 3 },
            new() { Type = EntryType.Text, Content = "Notiz", SourceUrl = "https://docs.test.org/x", Timestamp = now.AddDays(-1), IsPinned = true },
            new() { Type = EntryType.Code, Content = "var x = 1;", ContentKind = "CODE", Timestamp = now.AddDays(-2) },
        };
        var session = new RecordingSession { Path = "a.md", Start = now.AddHours(-3), End = now.AddHours(-2) };
        session.Snapshots.Add(new RecordingSnapshot(now.AddHours(-3), "Chrome", "Portal"));
        session.Snapshots.Add(new RecordingSnapshot(now.AddHours(-2.5), "Chrome", "Portal"));
        session.Snapshots.Add(new RecordingSnapshot(now.AddHours(-2.2), "Editor", "notiz.txt"));

        var md = KnowledgeSummaryBuilder.Build(entries, [session], now);

        Assert.Contains("- Einträge: 4 (gepinnt: 1)", md);
        Assert.Contains("- example.com (2)", md);
        Assert.Contains("- docs.test.org (1)", md);
        Assert.Contains("### Inhaltsarten\n\n- CODE (1)\n\n", md);
        Assert.Contains("### Meistgenutzt\n\n- https://example.com/b (3x)", md);
        Assert.Contains("### Gepinnt\n\n- Notiz", md);
        Assert.Contains("- Erfasste Bildwechsel: 3", md);
        Assert.Contains("- Gesamtdauer: 01:00:00", md);
        Assert.Contains("- Chrome (2)", md);
        Assert.Contains("- Chrome: Portal\n- Editor: notiz.txt\n", md);
    }

    [Fact]
    public void KnowledgeSummary_HandlesEmptyState()
    {
        var md = KnowledgeSummaryBuilder.Build([], [], new DateTime(2026, 11, 5));

        Assert.Contains("Keine Einträge in der History.", md);
        Assert.Contains("Noch keine Aufnahmen.", md);
    }
}

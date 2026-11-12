using Merkautomat.Models;
using Merkautomat.Services;
using Xunit;

namespace Merkautomat.Tests;

public class MyKnowledgeTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0);

    private static MyKnowledgeContext Context(Dictionary<string, BrainAnnotation>? annotations = null, string[]? collection = null,
        int retention = 60, int halfLife = 7, bool condense = false, int maturityHours = 0)
        => new(annotations ?? [], (collection ?? []).ToHashSet(StringComparer.Ordinal), retention, halfLife, condense, maturityHours);

    private static MyKnowledgeCandidate Candidate(string key, SemanticSource source, double daysAgo, string? text = null,
        bool pinned = false, int useCount = 0)
        => new(key, source, "Titel " + key, text ?? "Inhalt von " + key, Now.AddDays(-daysAgo), Now.AddDays(-daysAgo), pinned, useCount);

    private static MyKnowledgeEntry Entry(SemanticSource source, double daysAgo, string key = "k")
        => new() { Key = key, Source = source, Date = Now.AddDays(-daysAgo) };

    // ---- Weight ----------------------------------------------------------------------------------------------

    [Fact]
    public void FreshItemsAreWeightedBySource()
    {
        var c = Context();
        var folder = MyKnowledge.Weight(Entry(SemanticSource.KnowledgeFolder, 0), Now, c);
        var note = MyKnowledge.Weight(Entry(SemanticSource.Note, 0), Now, c);
        var recording = MyKnowledge.Weight(Entry(SemanticSource.Recording, 0), Now, c);
        var clip = MyKnowledge.Weight(Entry(SemanticSource.Clipboard, 0), Now, c);
        Assert.True(folder > note && note > recording && recording > clip);
    }

    [Fact]
    public void ClipboardHalvesAfterItsHalfLifeWhileNotesKeepTheirWeight()
    {
        var c = Context(halfLife: 7);
        var fresh = MyKnowledge.Weight(Entry(SemanticSource.Clipboard, 0), Now, c);
        Assert.Equal(fresh / 2, MyKnowledge.Weight(Entry(SemanticSource.Clipboard, 7), Now, c), 6);
        Assert.Equal(MyKnowledge.Weight(Entry(SemanticSource.Note, 0), Now, c), MyKnowledge.Weight(Entry(SemanticSource.Note, 400), Now, c), 6);
    }

    [Fact]
    public void CategoryReuseAndKeepRaiseTheWeight()
    {
        var plain = MyKnowledge.Weight(Entry(SemanticSource.Clipboard, 3), Now, Context());
        var categorized = MyKnowledge.Weight(Entry(SemanticSource.Clipboard, 3), Now,
            Context(new() { ["k"] = new BrainAnnotation(Category: "Arbeit") }));
        var reused = Entry(SemanticSource.Clipboard, 3);
        reused.UseCount = 3;
        var kept = Entry(SemanticSource.Clipboard, 30);
        kept.Kept = true;
        Assert.True(categorized > plain);
        Assert.True(MyKnowledge.Weight(reused, Now, Context()) > plain);
        // Kept items do not decay.
        Assert.True(MyKnowledge.Weight(kept, Now, Context()) > MyKnowledge.Weight(Entry(SemanticSource.Clipboard, 0), Now, Context()));
    }

    // ---- Daily run -------------------------------------------------------------------------------------------

    [Fact]
    public void RunTakesInAllSourcesHeaviestFirst()
    {
        var state = new MyKnowledgeState();
        var run = MyKnowledge.Update(state, [
            Candidate("c:1", SemanticSource.Clipboard, 0),
            Candidate("n:a.md", SemanticSource.Note, 1),
            Candidate("f:x", SemanticSource.KnowledgeFolder, 100),
            Candidate("r:s.md#0", SemanticSource.Recording, 0),
        ], Context(), Now);

        Assert.Equal(4, run.Added);
        Assert.Equal(["f:x", "n:a.md", "r:s.md#0", "c:1"], state.Entries.Select(e => e.Key));
        Assert.Equal(Now, state.LastRun);
        Assert.All(state.Entries, e => Assert.Equal(Now, e.Added));
    }

    [Fact]
    public void CopyOutlivesAVanishedClipboardEntryButFolderFilesFollowTheFolder()
    {
        var state = new MyKnowledgeState();
        MyKnowledge.Update(state, [Candidate("c:1", SemanticSource.Clipboard, 0, "Wichtiger Gedanke"),
            Candidate("f:x", SemanticSource.KnowledgeFolder, 0)], Context(), Now);

        var run = MyKnowledge.Update(state, [], Context(), Now.AddDays(1));

        Assert.Equal("Wichtiger Gedanke", Assert.Single(state.Entries).Text);
        Assert.Equal(1, run.Expired);
    }

    [Fact]
    public void UnprotectedClipboardEntriesExpireAfterTheRetention()
    {
        var state = new MyKnowledgeState();
        MyKnowledge.Update(state, [Candidate("c:1", SemanticSource.Clipboard, 0)], Context(retention: 60), Now);

        MyKnowledge.Update(state, [], Context(retention: 60), Now.AddDays(59));
        Assert.Single(state.Entries);
        var run = MyKnowledge.Update(state, [], Context(retention: 60), Now.AddDays(61));
        Assert.Empty(state.Entries);
        Assert.Equal(1, run.Expired);
    }

    [Fact]
    public void RetentionZeroKeepsEverything()
    {
        var state = new MyKnowledgeState();
        MyKnowledge.Update(state, [Candidate("c:1", SemanticSource.Clipboard, 0)], Context(retention: 0), Now);
        MyKnowledge.Update(state, [], Context(retention: 0), Now.AddDays(400));
        Assert.Single(state.Entries);
    }

    [Fact]
    public void CategoryCollectionAndKeepProtectFromExpiry()
    {
        var state = new MyKnowledgeState();
        MyKnowledge.Update(state, [Candidate("c:1", SemanticSource.Clipboard, 0), Candidate("c:2", SemanticSource.Clipboard, 0),
            Candidate("c:3", SemanticSource.Clipboard, 0), Candidate("c:4", SemanticSource.Clipboard, 0)], Context(), Now);
        state.Entries.Single(e => e.Key == "c:3").Kept = true;

        MyKnowledge.Update(state, [], Context(new() { ["c:1"] = new BrainAnnotation(Category: "Arbeit") }, ["c:2"]), Now.AddDays(90));

        Assert.Equal(["c:1", "c:2", "c:3"], state.Entries.Select(e => e.Key).Order());
    }

    [Fact]
    public void OldStaleItemsAreNotTakenInAgain()
    {
        var state = new MyKnowledgeState();
        var old = Candidate("c:1", SemanticSource.Clipboard, 90);
        MyKnowledge.Update(state, [old], Context(), Now);
        MyKnowledge.Update(state, [old], Context(), Now.AddDays(1));
        Assert.Empty(state.Entries);
    }

    [Fact]
    public void LongRelevantItemsAreConsolidated()
    {
        var state = new MyKnowledgeState();
        MyKnowledge.Update(state, [Candidate("n:a.md", SemanticSource.Note, 0), Candidate("c:1", SemanticSource.Clipboard, 0)], Context(), Now);
        Assert.DoesNotContain(state.Entries, e => e.Consolidated);

        var run = MyKnowledge.Update(state, [Candidate("n:a.md", SemanticSource.Note, 0), Candidate("c:1", SemanticSource.Clipboard, 0)],
            Context(), Now.AddDays(MyKnowledge.ConsolidateDays));

        Assert.True(state.Entries.Single(e => e.Key == "n:a.md").Consolidated);
        // A plain clipboard entry has lost too much weight by then.
        Assert.False(state.Entries.Single(e => e.Key == "c:1").Consolidated);
        Assert.Equal(1, run.Consolidated);
    }

    [Fact]
    public void AnOldNoteCountsAsLongAroundOnTheFirstRun()
    {
        var state = new MyKnowledgeState();
        MyKnowledge.Update(state, [Candidate("n:alt.md", SemanticSource.Note, 200)], Context(), Now);
        Assert.True(Assert.Single(state.Entries).Consolidated);
    }

    [Fact]
    public void CopyingAgainKeepsAClipboardEntryRelevantUntilItIsConsolidated()
    {
        var state = new MyKnowledgeState();
        MyKnowledge.Update(state, [Candidate("c:1", SemanticSource.Clipboard, 0)], Context(), Now);
        var later = Now.AddDays(MyKnowledge.ConsolidateDays);
        MyKnowledge.Update(state, [new MyKnowledgeCandidate("c:1", SemanticSource.Clipboard, "Titel", "Inhalt", later, Now, UseCount: 3)],
            Context(), later);
        Assert.True(Assert.Single(state.Entries).Consolidated);
    }

    [Fact]
    public void IdenticalTextsCountOnceNewestWins()
    {
        var state = new MyKnowledgeState();
        MyKnowledge.Update(state, [
            Candidate("r:s.md#0", SemanticSource.Recording, 2, "Gleiche   Seite"),
            Candidate("r:s.md#5", SemanticSource.Recording, 1, "Gleiche Seite"),
        ], Context(), Now);
        Assert.Equal("r:s.md#5", Assert.Single(state.Entries).Key);
    }

    [Fact]
    public void TextIsStoredWithALimit()
    {
        var state = new MyKnowledgeState();
        MyKnowledge.Update(state, [Candidate("n:a.md", SemanticSource.Note, 0, new string('x', MyKnowledge.MaxTextChars + 50))], Context(), Now);
        Assert.Equal(MyKnowledge.MaxTextChars, state.Entries[0].Text.Length);
    }

    // ---- Waiting time and ramp -------------------------------------------------------------------------------

    [Fact]
    public void YoungItemsWaitUntilTheyHaveBeenInTheKnowledgeBaseLongEnough()
    {
        var state = new MyKnowledgeState();
        var context = Context(maturityHours: 48);
        var note = Candidate("n:a.md", SemanticSource.Note, 1);

        var run = MyKnowledge.Update(state, [note], context, Now);
        Assert.Empty(state.Entries);
        Assert.Equal(1, run.Waiting);
        Assert.True(state.Pending.ContainsKey("n:a.md"));

        MyKnowledge.Update(state, [note], context, Now.AddHours(25));
        Assert.Equal("n:a.md", Assert.Single(state.Entries).Key);
        Assert.Empty(state.Pending);
    }

    [Fact]
    public void AnOftenEditedFolderFileStillArrivesAfterTheWaitingTime()
    {
        var state = new MyKnowledgeState();
        var context = Context(maturityHours: 48);
        MyKnowledgeCandidate Edited(DateTime at) => new("f:x", SemanticSource.KnowledgeFolder, "Datei", "Inhalt", at, at);

        MyKnowledge.Update(state, [Edited(Now)], context, Now);
        MyKnowledge.Update(state, [Edited(Now.AddDays(1))], context, Now.AddDays(1));
        Assert.Empty(state.Entries);
        MyKnowledge.Update(state, [Edited(Now.AddDays(2.1))], context, Now.AddDays(2.1));

        Assert.Equal("f:x", Assert.Single(state.Entries).Key);
    }

    [Fact]
    public void WeightStartsLowAfterTheWaitingTimeAndRisesOverTwoWeeks()
    {
        var context = Context(maturityHours: 48);
        var entry = new MyKnowledgeEntry { Key = "n:a.md", Source = SemanticSource.Note, Date = Now, FirstCaptured = Now.AddDays(-2), Added = Now };

        Assert.Equal(MyKnowledge.YoungFactor, MyKnowledge.Maturity(entry, Now, context), 6);
        Assert.Equal(0.65, MyKnowledge.Maturity(entry, Now.AddDays(7), context), 6);
        Assert.Equal(1, MyKnowledge.Maturity(entry, Now.AddDays(14), context), 6);
        Assert.Equal(0.8 * MyKnowledge.YoungFactor, MyKnowledge.Weight(entry, Now, context), 6);

        entry.Kept = true;
        Assert.Equal(1, MyKnowledge.Weight(entry, Now, context), 6);
    }

    [Fact]
    public void EntriesTakenInBeforeTheWaitingTimeGoBackToWaiting()
    {
        var state = new MyKnowledgeState();
        var note = Candidate("n:a.md", SemanticSource.Note, 0);
        var kept = Candidate("n:b.md", SemanticSource.Note, 0);
        MyKnowledge.Update(state, [note, kept], Context(), Now);
        state.Entries.Single(e => e.Key == "n:b.md").Kept = true;

        MyKnowledge.Update(state, [note, kept], Context(maturityHours: 48), Now.AddHours(1));

        Assert.Equal("n:b.md", Assert.Single(state.Entries).Key);
        Assert.True(state.Pending.ContainsKey("n:a.md"));
    }

    [Fact]
    public void WaitingSourcesThatDisappearAreForgotten()
    {
        var state = new MyKnowledgeState();
        var context = Context(maturityHours: 48);
        MyKnowledge.Update(state, [Candidate("c:1", SemanticSource.Clipboard, 0)], context, Now);
        MyKnowledge.Update(state, [], context, Now.AddHours(1));
        Assert.Empty(state.Pending);
        Assert.Empty(state.Entries);
    }

    // ---- Condensing ------------------------------------------------------------------------------------------

    private const string RouterText = "Der Router im Büro braucht ein neues Zertifikat für das Gäste-WLAN. Die Firmware ist veraltet und muss aktualisiert werden.";
    private const string PrinterText = "Der Drucker im zweiten Stock meldet ständig einen Papierstau im Fach zwei. Ersatzteil ist bestellt und kommt am Freitag.";

    [Fact]
    public void ExpiringEntriesAreCondensedIntoOneDigestPerMonthAndSource()
    {
        var state = new MyKnowledgeState();
        var context = Context(condense: true);
        MyKnowledge.Update(state, [Candidate("c:1", SemanticSource.Clipboard, 0, RouterText), Candidate("c:2", SemanticSource.Clipboard, 0, PrinterText),
            Candidate("r:s.md#0", SemanticSource.Recording, 0, "Bildschirm: Ticketsystem mit offener Störung im Netzwerk des Standorts.")], context, Now);

        var run = MyKnowledge.Update(state, [], context, Now.AddDays(100));

        Assert.Equal(3, run.Condensed);
        Assert.Equal(0, run.Expired);
        var clip = Assert.Single(state.Entries, e => e.Key == "d:2026-11:s:Clipboard");
        Assert.True(clip.Digest);
        Assert.Equal(2, clip.Members);
        Assert.Equal("Zusammenfassung Zwischenablage · November 2026", clip.Title);
        Assert.Contains(clip.Statements, s => s.Key == "c:1" && s.Text.Contains("Router"));
        Assert.Contains("Verdichtet aus 2 Einträgen", clip.Text);
        Assert.Contains("## Enthaltene Einträge", clip.Text);
        Assert.Single(state.Entries, e => e.Key == "d:2026-11:s:Recording");
        Assert.DoesNotContain(state.Entries, e => !e.Digest);
    }

    [Fact]
    public void DigestsNeverExpireAndKeepASteadyWeight()
    {
        var state = new MyKnowledgeState();
        var context = Context(condense: true);
        MyKnowledge.Update(state, [Candidate("c:1", SemanticSource.Clipboard, 0, RouterText)], context, Now);
        MyKnowledge.Update(state, [], context, Now.AddDays(100));
        var weight = state.Entries[0].Weight;

        MyKnowledge.Update(state, [], context, Now.AddDays(1000));

        var digest = Assert.Single(state.Entries);
        Assert.True(MyKnowledge.IsProtected(digest, context));
        Assert.Equal(weight, digest.Weight, 6);
        Assert.InRange(digest.Weight, 0.3, 0.6);
    }

    [Fact]
    public void LaterEntriesOfTheSameMonthMergeIntoTheExistingDigest()
    {
        var state = new MyKnowledgeState();
        var context = Context(condense: true);
        // A category protects c:2 at first; once it is removed, c:2 follows into the same digest.
        var categorized = Context(new() { ["c:2"] = new BrainAnnotation(Category: "Technik") }, condense: true);
        MyKnowledge.Update(state, [Candidate("c:1", SemanticSource.Clipboard, 0, RouterText), Candidate("c:2", SemanticSource.Clipboard, 0, PrinterText)], context, Now);
        MyKnowledge.Update(state, [], categorized, Now.AddDays(100));
        Assert.Equal(2, state.Entries.Count);

        MyKnowledge.Update(state, [], context, Now.AddDays(101));

        var digest = Assert.Single(state.Entries);
        Assert.Equal(2, digest.Members);
        Assert.Equal(["c:1", "c:2"], digest.Statements.Select(s => s.Key).Distinct().Order());
    }

    [Fact]
    public void KnowledgeFolderHasNoRetentionAndIsNeverCondensed()
    {
        var state = new MyKnowledgeState();
        var context = Context(retention: 1, condense: true);
        var file = Candidate("f:x", SemanticSource.KnowledgeFolder, 3000, RouterText);
        MyKnowledge.Update(state, [file], context, Now);
        MyKnowledge.Update(state, [file], context, Now.AddDays(500));

        var entry = Assert.Single(state.Entries);
        Assert.Equal("f:x", entry.Key);
        Assert.True(MyKnowledge.IsProtected(entry, context));
        Assert.Null(MyKnowledge.DaysLeft(entry, Now, context));
        Assert.Null(MyKnowledge.Summarize(state, ["f:x"], context, Now));
    }

    [Fact]
    public void SummarizeCondensesTheSelectionButLeavesFolderFiles()
    {
        var state = new MyKnowledgeState();
        var context = Context(new() { ["c:1"] = new BrainAnnotation(Category: "Technik"), ["n:a.md"] = new BrainAnnotation(Category: "Technik") });
        MyKnowledge.Update(state, [Candidate("c:1", SemanticSource.Clipboard, 0, RouterText), Candidate("n:a.md", SemanticSource.Note, 0, PrinterText),
            Candidate("f:x", SemanticSource.KnowledgeFolder, 0)], context, Now);

        var digest = MyKnowledge.Summarize(state, ["c:1", "n:a.md", "f:x"], context, Now);

        Assert.NotNull(digest);
        Assert.Equal("Zusammenfassung Technik", digest.Title);
        Assert.Equal("Technik", context.CategoryOf(digest));
        Assert.Equal(2, digest.Members);
        Assert.Equal([digest.Key, "f:x"], state.Entries.Select(e => e.Key).Order(StringComparer.Ordinal));
        Assert.Equal(["c:1", "n:a.md"], state.Dismissed.Order(StringComparer.Ordinal));

        // The sources still exist, but the condensed entries do not come back.
        MyKnowledge.Update(state, [Candidate("c:1", SemanticSource.Clipboard, 0, RouterText), Candidate("n:a.md", SemanticSource.Note, 0, PrinterText),
            Candidate("f:x", SemanticSource.KnowledgeFolder, 0)], context, Now.AddDays(1));
        Assert.Equal(2, state.Entries.Count);
        Assert.Contains("Technik", MyKnowledge.Profile(state.Entries, context).Select(r => r.Category));
    }

    [Fact]
    public void DigestsCanBeMergedIntoALargerOne()
    {
        var state = new MyKnowledgeState();
        var context = Context();
        MyKnowledge.Update(state, [Candidate("c:1", SemanticSource.Clipboard, 0, RouterText), Candidate("c:2", SemanticSource.Clipboard, 0, PrinterText),
            Candidate("c:3", SemanticSource.Clipboard, 0, "Ein dritter Gedanke über die Planung des nächsten Quartals und die Ziele.")], context, Now);
        var first = MyKnowledge.Summarize(state, ["c:1", "c:2"], context, Now)!;

        var merged = MyKnowledge.Summarize(state, [first.Key, "c:3"], context, Now)!;

        Assert.Equal(3, merged.Members);
        Assert.Equal(["c:1", "c:2", "c:3"], merged.Sources.Select(s => s.Key).Order(StringComparer.Ordinal));
        Assert.Same(merged, Assert.Single(state.Entries));
    }

    [Fact]
    public void DeletingASourceRemovesItsStatementsFromDigests()
    {
        var state = new MyKnowledgeState();
        var context = Context(condense: true);
        MyKnowledge.Update(state, [Candidate("c:1", SemanticSource.Clipboard, 0, RouterText), Candidate("c:2", SemanticSource.Clipboard, 0, PrinterText)], context, Now);
        MyKnowledge.Update(state, [], context, Now.AddDays(100));

        MyKnowledge.Forget(state, ["c:1"]);
        var digest = Assert.Single(state.Entries);
        Assert.DoesNotContain(digest.Statements, s => s.Key == "c:1");
        Assert.DoesNotContain("Router", digest.Text);

        MyKnowledge.Forget(state, ["c:2"]);
        Assert.Empty(state.Entries);
    }

    [Fact]
    public void KeyStatementsAreLimitedInTotalAndPerText()
    {
        var long1 = "Das Projekt Alpha startet im Januar mit drei Leuten. Das Budget von Alpha reicht bis zum Sommer. "
            + "Für Alpha fehlt noch ein Testserver im Rechenzentrum. Die Kunden von Alpha wünschen eine mobile Ansicht. "
            + "Alpha wird zuerst in Hamburg eingeführt.";
        var long2 = "Projekt Beta hat einen neuen Ansprechpartner beim Lieferanten. Beta braucht eine Schnittstelle zur Buchhaltung. "
            + "Die Dokumentation von Beta liegt im Wiki unter Technik. Beta wird im Herbst abgeschlossen.";

        var picked = BrainKnowledge.KeyStatements([long1, long2, "zu kurz"], 5, 3);

        Assert.Equal(5, picked.Count);
        Assert.All(picked.GroupBy(p => p.Source), g => Assert.True(g.Count() <= 3));
        Assert.DoesNotContain(picked, p => p.Source == 2);
    }

    // ---- Shared building blocks -------------------------------------------------------------------------------

    [Fact]
    public void KnowledgeTextOfClipboardEntries()
    {
        Assert.Equal("Seite\nhttps://x.de", new ClipboardEntry { Type = EntryType.Url, UrlTitle = "Seite", Content = "https://x.de" }.KnowledgeText());
        Assert.Equal("erkannt", new ClipboardEntry { Type = EntryType.Image, OcrText = " erkannt " }.KnowledgeText());
        Assert.Equal("", new ClipboardEntry { Type = EntryType.Image }.KnowledgeText());
        // Colours are never indexed as knowledge.
        Assert.Empty(SemanticChunker.FromClipboard([new ClipboardEntry { Id = 1, Type = EntryType.Color, Content = "#ff8800 ist eine schöne Farbe" }]));
    }

    [Fact]
    public void CandidateFromAClipboardEntryUsesItsLatestUseAndFirstCapture()
    {
        var entry = new ClipboardEntry
        {
            Id = 5, Type = EntryType.Text, Content = "Kurz", Timestamp = Now, LastUsedAt = Now.AddDays(1), IsPinned = true, UseCount = 2,
            Metadata = new ContentMetadata { CapturedAt = Now.AddDays(-3) },
        };
        var candidate = MyKnowledge.Candidate(entry)!;
        Assert.Equal(("c:5", "Kurz", Now.AddDays(1), Now.AddDays(-3)), (candidate.Key, candidate.Text, candidate.Date, candidate.FirstCaptured));
        Assert.True(candidate.Pinned);
        Assert.Null(MyKnowledge.Candidate(new ClipboardEntry { Id = 6, Type = EntryType.Image }));
    }

    [Fact]
    public void CloneIsIndependentOfTheOriginal()
    {
        var entry = new MyKnowledgeEntry { Key = "d:x", Digest = true, Statements = [new("Aussage", "c:1")], SourceCounts = { ["Zwischenablage"] = 1 } };
        var copy = entry.Clone();
        entry.Statements.Clear();
        entry.SourceCounts.Clear();
        entry.Weight = 0.9;
        Assert.Single(copy.Statements);
        Assert.Single(copy.SourceCounts);
        Assert.Equal(0, copy.Weight);
    }

    // ---- User actions ----------------------------------------------------------------------------------------

    [Fact]
    public void DismissedItemsStayOutWhileTheirSourceExists()
    {
        var state = new MyKnowledgeState();
        var note = Candidate("n:a.md", SemanticSource.Note, 0);
        MyKnowledge.Update(state, [note], Context(), Now);
        Assert.Equal(1, MyKnowledge.Dismiss(state, ["n:a.md"]));

        MyKnowledge.Update(state, [note], Context(), Now.AddDays(1));
        Assert.Empty(state.Entries);
        Assert.Contains("n:a.md", state.Dismissed);

        MyKnowledge.Update(state, [], Context(), Now.AddDays(2));
        Assert.Empty(state.Dismissed);
    }

    [Fact]
    public void FixTakesInAWaitingItemAtOnceWithFullWeight()
    {
        var state = new MyKnowledgeState();
        var context = Context(maturityHours: 48);
        var clip = Candidate("c:1", SemanticSource.Clipboard, 0, RouterText);
        MyKnowledge.Update(state, [clip], context, Now);
        Assert.Empty(state.Entries);

        Assert.Equal(1, MyKnowledge.Fix(state, ["c:1"], null, [clip], Now));

        var entry = Assert.Single(state.Entries);
        Assert.True(entry.Kept);
        Assert.Equal(1, entry.Weight);
        Assert.Empty(state.Pending);
        // Fixed items stay at 100 %: no waiting time, no decay, no condensing.
        MyKnowledge.Update(state, [clip], Context(maturityHours: 48, condense: true), Now.AddDays(400));
        Assert.Equal(1, Assert.Single(state.Entries).Weight);
    }

    [Fact]
    public void FixBringsBackADismissedItemAndCoversAllSnapshotsOfARecording()
    {
        var state = new MyKnowledgeState();
        var snapshots = new[]
        {
            Candidate("r:s.md#0", SemanticSource.Recording, 0, "Erster Bildwechsel mit Inhalt"),
            Candidate("r:s.md#1", SemanticSource.Recording, 0, "Zweiter Bildwechsel mit Inhalt"),
        };
        MyKnowledge.Update(state, snapshots, Context(), Now);
        MyKnowledge.Dismiss(state, ["r:s.md#0"]);

        Assert.Equal(2, MyKnowledge.Fix(state, [], ["r:s.md#"], snapshots, Now));

        Assert.All(state.Entries, e => Assert.True(e.Kept));
        Assert.Equal(2, state.Entries.Count);
        Assert.Empty(state.Dismissed);
    }

    [Fact]
    public void FixAlsoWorksForCopiesWhoseSourceIsGone()
    {
        var state = new MyKnowledgeState();
        MyKnowledge.Update(state, [Candidate("c:1", SemanticSource.Clipboard, 0, RouterText)], Context(), Now);

        Assert.Equal(1, MyKnowledge.Fix(state, ["c:1"], null, [], Now));
        Assert.True(Assert.Single(state.Entries).Kept);
        Assert.Equal(0, MyKnowledge.Fix(state, ["c:99"], null, [], Now));
    }

    [Fact]
    public void DismissalSurvivesWhileTheSourceIsSkippedAsRepeatedText()
    {
        var state = new MyKnowledgeState();
        var older = Candidate("c:1", SemanticSource.Clipboard, 1, "Gleicher Text");
        var newer = Candidate("c:2", SemanticSource.Clipboard, 0, "Gleicher Text");
        MyKnowledge.Update(state, [older], Context(), Now);
        MyKnowledge.Dismiss(state, ["c:1"]);

        // c:2 wins the duplicate check, c:1 still exists as a source.
        MyKnowledge.Update(state, [older, newer], Context(), Now.AddHours(1));
        MyKnowledge.Dismiss(state, ["c:2"]);
        MyKnowledge.Update(state, [older, newer], Context(), Now.AddHours(2));

        Assert.Empty(state.Entries);
        Assert.Equal(["c:1", "c:2"], state.Dismissed.Order());
    }

    [Fact]
    public void ForgetRemovesKeysAndAllSnapshotsOfARecording()
    {
        var state = new MyKnowledgeState();
        MyKnowledge.Update(state, [
            Candidate("r:s.md#0", SemanticSource.Recording, 0, "eins"), Candidate("r:s.md#1", SemanticSource.Recording, 0, "zwei"),
            Candidate("r:t.md#0", SemanticSource.Recording, 0, "drei"), Candidate("c:1", SemanticSource.Clipboard, 0),
        ], Context(), Now);

        Assert.Equal(3, MyKnowledge.Forget(state, ["c:1"], ["r:s.md#"]));
        Assert.Equal("r:t.md#0", Assert.Single(state.Entries).Key);
    }

    // ---- Reading ---------------------------------------------------------------------------------------------

    [Fact]
    public void ProfileSumsWeightPerMainCategory()
    {
        var context = Context(new()
        {
            ["f:a"] = new BrainAnnotation(Category: "Technik", Subcategory: "Netz"),
            ["f:b"] = new BrainAnnotation(Category: "Technik"),
        });
        var entries = new List<MyKnowledgeEntry>
        {
            new() { Key = "f:a", Weight = 1, Date = Now }, new() { Key = "f:b", Weight = 0.5, Date = Now },
            new() { Key = "c:1", Weight = 0.5, Date = Now.AddDays(-1) },
        };

        var profile = MyKnowledge.Profile(entries, context);

        Assert.Equal("Technik", profile[0].Category);
        Assert.Equal(2, profile[0].Count);
        Assert.Equal(0.75, profile[0].Share, 6);
        Assert.Equal("Ohne Kategorie", profile[1].Category);
    }

    [Fact]
    public void SearchNeedsAllWordsAndRanksTitleHitsFirst()
    {
        var entries = new List<MyKnowledgeEntry>
        {
            new() { Key = "a", Title = "Notiz", Text = "Router Konfiguration im Büro", Weight = 0.9 },
            new() { Key = "b", Title = "Router Handbuch", Text = "Konfiguration Schritt für Schritt", Weight = 0.2 },
            new() { Key = "c", Title = "Router", Text = "nur ein Wort", Weight = 1 },
        };
        Assert.Equal(["b", "a"], MyKnowledge.Search(entries, "router konfiguration").Select(e => e.Key));
    }

    [Fact]
    public void CandidatesJoinChunksAndTakeUseOfTheHistoryEntry()
    {
        var chunks = new[]
        {
            new SemanticChunk("c:7:0", "c:7", SemanticSource.Clipboard, "T", Now.AddDays(-2), "Teil eins", EntryId: 7),
            new SemanticChunk("c:7:1", "c:7", SemanticSource.Clipboard, "T", Now.AddDays(-2), "Teil zwei", EntryId: 7),
        };
        var entry = new ClipboardEntry { Id = 7, IsPinned = true, UseCount = 2, LastUsedAt = Now.AddDays(-1) };

        var candidate = Assert.Single(MyKnowledge.Candidates(chunks, new Dictionary<long, ClipboardEntry> { [7] = entry }));

        Assert.Equal("Teil eins\n\nTeil zwei", candidate.Text);
        Assert.True(candidate.Pinned);
        Assert.Equal(2, candidate.UseCount);
        Assert.Equal(Now.AddDays(-1), candidate.Date);
        Assert.Equal(Now.AddDays(-2), candidate.FirstCaptured);
    }

    // ---- Brain -----------------------------------------------------------------------------------------------

    [Fact]
    public void BrainWithWeightsShowsOnlyThoseItemsHeaviestFirst()
    {
        var chunks = new[] { "c:1", "c:2", "c:3" }.Select((k, i) =>
            new IndexedChunk(new SemanticChunk(k + ":0", k, SemanticSource.Clipboard, k, Now.AddMinutes(i * 30), "text " + k), [1f, i])).ToArray();

        var graph = KnowledgeGraph.Build(chunks, new BrainOptions
        {
            MaxNodes = 1, Weights = new Dictionary<string, double> { ["c:1"] = 0.9, ["c:2"] = 0.1 },
        });

        Assert.Equal("c:1", Assert.Single(graph.Nodes).ItemKey);
    }
}

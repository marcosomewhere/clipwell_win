using System.IO;
using ClipwellWin.Services;
using Xunit;

namespace ClipwellWin.Tests;

public sealed class SemanticDeletionTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "clipwell-delete-plan");
    private static readonly string Notes = Path.Combine(Root, "notes");
    private static readonly string Kb = Path.Combine(Root, "kb");
    private static readonly string Recordings = Path.Combine(Root, "recordings");
    private static SemanticChunk Chunk(string key, SemanticSource source, long id = 0, string? path = null)
        => new(key + ":0", key, source, "Titel", DateTime.Now, "Text", EntryId: id, FilePath: path);
    private static SemanticDeletePlan Plan(IEnumerable<SemanticChunk> chunks, string? active = null)
        => SemanticDeletion.Plan(chunks, Notes, Kb, Recordings, active);

    [Fact]
    public void Plan_DeduplicatesRepeatedClipboardTargets()
    {
        var plan = Plan([Chunk("c:1", SemanticSource.Clipboard, 1), Chunk("c:1", SemanticSource.Clipboard, 1), Chunk("c:2", SemanticSource.Clipboard, 2)]);
        Assert.Equal([1L, 2L], plan.Targets.Select(t => t.EntryId));
        Assert.All(plan.Targets, target => Assert.Empty(target.Paths));
    }

    [Fact]
    public void Plan_NoteIncludesOriginalExportAndOnlyTheirBackups()
    {
        const string name = "2026-11-06_12-00-00.md";
        var target = Assert.Single(Plan([Chunk("n:" + name, SemanticSource.Note, path: Path.Combine(Kb, name))]).Targets);
        Assert.Equal([Path.Combine(Notes, name), Path.Combine(Kb, name), Path.Combine(Notes, name + ".bak"), Path.Combine(Kb, name + ".bak")], target.Paths);
    }

    [Fact]
    public void Plan_RejectsPathsOutsideOwnedDirectories()
        => Assert.Throws<ArgumentException>(() => Plan([Chunk("n:outside", SemanticSource.Note,
            path: Path.Combine(Root, "elsewhere", "2026-11-06_12-00-00.md"))]));

    [Fact]
    public void Plan_SkipsActiveRecording()
    {
        var path = Path.Combine(Recordings, "recording.md");
        var plan = Plan([Chunk("r:a#0", SemanticSource.Recording, path: path), Chunk("r:a#1", SemanticSource.Recording, path: path)], path);
        Assert.Empty(plan.Targets);
        Assert.Equal(1, plan.SkippedRecordings);
    }

    [Fact]
    public void Plan_SeveralSnapshotsResolveToOneCompleteRecording()
    {
        var path = Path.Combine(Recordings, "recording.md");
        var target = Assert.Single(Plan([Chunk("r:a#0", SemanticSource.Recording, path: path), Chunk("r:a#1", SemanticSource.Recording, path: path)]).Targets);
        Assert.Equal(path, Assert.Single(target.Paths));
    }

    [Fact]
    public void Plan_KeepsTheRetainedCopyOfADuplicateGroup()
    {
        var keep = Chunk("c:3", SemanticSource.Clipboard, 3);
        var plan = SemanticDeletion.Plan([Chunk("c:2", SemanticSource.Clipboard, 2), keep], Notes, Kb, Recordings, null, [keep]);
        Assert.Equal([2L], plan.Targets.Select(t => t.EntryId));
    }

    [Fact]
    public void Plan_DoesNotDeleteARecordingThatHoldsTheRetainedCopy()
    {
        var path = Path.Combine(Recordings, "recording.md");
        var plan = SemanticDeletion.Plan([Chunk("r:a#1", SemanticSource.Recording, path: path)], Notes, Kb, Recordings, null,
            [Chunk("r:a#0", SemanticSource.Recording, path: path)]);
        Assert.Empty(plan.Targets);
    }
}

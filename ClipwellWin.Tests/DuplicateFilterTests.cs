using ClipwellWin.Services;
using Xunit;

namespace ClipwellWin.Tests;

public sealed class DuplicateFilterTests
{
    private static IndexedChunk Item(string key, string text, int minute, float[]? vector = null, string? title = null, int part = 0)
        => new(new SemanticChunk(key + ":" + part, key, SemanticSource.Note, title ?? text,
            new DateTime(2026, 11, 6, 12, 0, 0).AddMinutes(minute), text), vector ?? [1f, 0f]);

    [Fact]
    public void ExactMode_DoesNotConfuseEqualVectorsWithEqualText()
    {
        var groups = SemanticRanking.DuplicateGroups([Item("a", "Erster Text", 0), Item("b", "Anderer Text", 1)], exactOnly: true);
        Assert.Empty(groups);
    }

    [Fact]
    public void ExactMode_UsesAllChunksInsteadOfSharedOpening()
    {
        IndexedChunk[] chunks = [Item("a", "Gleicher Anfang", 0), Item("a", "anderes Ende", 0, part: 1),
            Item("b", "Gleicher Anfang", 1), Item("b", "eigenes Ende", 1, part: 1)];
        Assert.Empty(SemanticRanking.DuplicateGroups(chunks, exactOnly: true));
    }

    [Fact]
    public void AllDuplicates_ShowsEveryRepeatedItemButNotUniqueItems()
    {
        var hits = SemanticRanking.FilterDuplicates([Item("a", "Gleich", 0), Item("b", "Gleich", 1), Item("c", "Einzel", 2)],
            DuplicateView.AllDuplicates, exactOnly: true);
        Assert.Equal(["b", "a"], hits.Select(h => h.Chunk.ItemKey));
        Assert.All(hits, h => Assert.Empty(h.Duplicates));
    }

    [Fact]
    public void OnePerGroup_KeepsNewestCopyAndUniqueItems()
    {
        var hits = SemanticRanking.FilterDuplicates([Item("a", "Gleich", 0), Item("b", "Gleich", 1), Item("c", "Einzel", 2)],
            DuplicateView.OnePerGroup, exactOnly: true);
        Assert.Equal(["c", "b"], hits.Select(h => h.Chunk.ItemKey));
    }

    [Fact]
    public void TextFilter_FindsGroupThroughAnyMember()
    {
        var hits = SemanticRanking.FilterDuplicates([Item("a", "Inhalt", 0, title: "Gesuchter Begriff"), Item("b", "Inhalt", 1, title: "Neueste Kopie")],
            DuplicateView.Groups, exactOnly: true, query: "Gesuchter");
        Assert.Equal("b", Assert.Single(hits).Chunk.ItemKey);
    }

    [Fact]
    public void Threshold_ChangesNearDuplicateResults()
    {
        IndexedChunk[] chunks = [Item("a", "Ein Text", 0), Item("b", "Anderer Text", 1, [0.95f, 0.31225f])];
        Assert.Empty(SemanticRanking.DuplicateGroups(chunks, threshold: 0.97f));
        Assert.Single(SemanticRanking.DuplicateGroups(chunks, threshold: 0.94f));
    }
}

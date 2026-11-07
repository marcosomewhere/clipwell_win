using System.Text.Json;
using ClipwellWin.Models;
using ClipwellWin.Services;
using Xunit;

namespace ClipwellWin.Tests;

/// <summary>Rocchio beyond categorization: relevance feedback, category matching, E5-before-Granite cascade, lexical and URL keys.</summary>
public sealed class RocchioFeedbackTests
{
    private static readonly DateTime Day = new(2026, 11, 7, 9, 0, 0);

    private static float[] Unit(params float[] v)
    {
        SemanticRanking.Normalize(v);
        return v;
    }

    private static IndexedChunk Chunk(string item, float[] vector, string text = "Beispieltext für den Index", int minutes = 0)
        => new(new SemanticChunk($"{item}:0", item, SemanticSource.Note, item, Day.AddMinutes(minutes), text), vector);

    // ---- Relevance feedback ------------------------------------------------------------------------------------

    [Fact]
    public void RocchioQuery_WithoutQueryIsMeanOfMarked_WithQueryMixesBoth()
    {
        var mean = SemanticRanking.RocchioQuery(null, [Unit(1, 0, 0), Unit(0, 1, 0)])!;
        Assert.Equal(mean[0], mean[1], 5);
        Assert.Equal(0, mean[2], 5);

        var mixed = SemanticRanking.RocchioQuery(Unit(0, 0, 1), [Unit(1, 0, 0)])!;
        Assert.True(mixed[2] > mixed[0] && mixed[0] > 0, "query weight 1 stays above marked weight 0.75");
        Assert.Equal(1, SemanticRanking.Dot(mixed, mixed), 4);

        Assert.Null(SemanticRanking.RocchioQuery(null, []));
    }

    [Fact]
    public void SimilarToMarked_KeepsMarkedAndFindsItemsCloseToTheirCentroid()
    {
        var chunks = new List<IndexedChunk>
        {
            Chunk("n:a", Unit(1, 0.4f, 0, 0)),
            Chunk("n:b", Unit(0.6f, 1, 0, 0)),
            Chunk("n:near", Unit(1, 0.9f, 0.3f, 0)),
            Chunk("n:far", Unit(0, 0, 1, 0)),
        };
        var hits = SemanticRanking.SimilarToMarked(chunks, chunks, ["n:a", "n:b"], null, null, 10);
        var keys = hits.SelectMany(h => h.Duplicates.Prepend(h.Chunk)).Select(c => c.ItemKey).ToList();
        Assert.Equal(3, hits.Count);
        Assert.Contains("n:a", keys);
        Assert.Contains("n:b", keys);
        Assert.Contains("n:near", keys);
        Assert.DoesNotContain("n:far", keys);
    }

    // ---- Category matching -------------------------------------------------------------------------------------

    [Fact]
    public void CategoryMatching_RanksUncategorizedItemsThatBeatAllOtherCategories()
    {
        var chunks = new List<IndexedChunk>
        {
            Chunk("n:a1", Unit(1, 0.05f, 0, 0.1f)), Chunk("n:a2", Unit(1, 0, 0.05f, 0.1f)),
            Chunk("n:b1", Unit(0.05f, 1, 0, 0.1f)), Chunk("n:b2", Unit(0, 1, 0.05f, 0.1f)),
            Chunk("n:c1", Unit(0, 0.05f, 1, 0.1f)), Chunk("n:c2", Unit(0.05f, 0, 1, 0.1f)),
            Chunk("n:new-a", Unit(1, 0.1f, 0.1f, 0.1f)),
            Chunk("n:new-b", Unit(0.1f, 1, 0.1f, 0.1f)),
        };
        var annotations = new Dictionary<string, BrainAnnotation>
        {
            ["n:a1"] = new(Category: "Arbeit"), ["n:a2"] = new(Category: "Arbeit"),
            ["n:b1"] = new(Category: "Garten"), ["n:b2"] = new(Category: "Garten"),
            ["n:c1"] = new(Category: "Reisen"), ["n:c2"] = new(Category: "Reisen"),
        };
        var result = CategoryMatching.Rank(chunks, annotations, new CategoryPath("Arbeit", ""));
        Assert.Equal("", result.Message);
        Assert.Equal(["n:new-a"], result.Matches.Select(m => m.Key));
        Assert.Equal(2, result.Examples);
        Assert.Equal(3, result.Competitors);
    }

    [Fact]
    public void CategoryMatching_NeedsThreeLearnedCategories()
    {
        var chunks = new List<IndexedChunk>
        {
            Chunk("n:a1", Unit(1, 0, 0)), Chunk("n:a2", Unit(1, 0.1f, 0)),
            Chunk("n:b1", Unit(0, 1, 0)), Chunk("n:b2", Unit(0.1f, 1, 0)),
            Chunk("n:x", Unit(1, 0, 0.1f)),
        };
        var annotations = new Dictionary<string, BrainAnnotation>
        {
            ["n:a1"] = new(Category: "Arbeit"), ["n:a2"] = new(Category: "Arbeit"),
            ["n:b1"] = new(Category: "Garten"), ["n:b2"] = new(Category: "Garten"),
        };
        var result = CategoryMatching.Rank(chunks, annotations, new CategoryPath("Arbeit", ""));
        Assert.Empty(result.Matches);
        Assert.Contains("drei Kategorien", result.Message);
        Assert.Contains("zwei Einträge", CategoryMatching.Rank(chunks, annotations, new CategoryPath("Unbekannt", "")).Message);
    }

    // ---- Cascade ---------------------------------------------------------------------------------------------

    [Fact]
    public void Cascade_OnlyConfidentLearnedE5AnswersReplaceGranite()
    {
        var candidates = new List<CategoryCandidate> { new("Arbeit", 0.3), new("Garten", 0.1), new("Reisen", 0.05), new("Haus", 0) };
        var learned = new LocalAiSuggestion("", "Arbeit", "Grund", false, candidates, ClassifierMethods.Rocchio) { Learned = true };
        var namesOnly = learned with { Learned = false, Method = ClassifierMethods.Names };
        var unsure = learned with { Uncertain = true, Category = "" };

        Assert.True(CategorizationCascade.Settles(learned));
        Assert.False(CategorizationCascade.Settles(namesOnly));
        Assert.False(CategorizationCascade.Settles(unsure));
        Assert.False(CategorizationCascade.Settles(null));
        Assert.Equal(["Arbeit", "Garten", "Reisen"], CategorizationCascade.Hints(unsure));
        Assert.Empty(CategorizationCascade.Hints(namesOnly));
        Assert.StartsWith("Granite nicht gestartet", CategorizationCascade.Shortcut(learned).Reason);
    }

    [Fact]
    public void GraniteInputCarriesOnlyExistingHints()
    {
        var source = new BrainSource("c:1", "Titel", "Ein Text über das Projekt", "", Day);
        using var withHints = JsonDocument.Parse(LocalAiCategorization.BuildInput([source], ["Arbeit", "Garten"], ["garten", "Erfunden"]));
        Assert.Equal(["Garten"], withHints.RootElement.GetProperty("likely_categories").EnumerateArray().Select(e => e.GetString()));
        using var plain = JsonDocument.Parse(LocalAiCategorization.BuildInput([source], ["Arbeit"]));
        Assert.False(plain.RootElement.TryGetProperty("likely_categories", out _));
    }

    [Fact]
    public void E5RankMarksLearnedResults()
    {
        string[] labels = ["Arbeit", "Garten", "Reisen"];
        float[][] names = [Unit(1, 0, 0), Unit(0, 1, 0), Unit(0, 0, 1)];
        var learned = E5Categorization.Rank(labels, names, [Unit(1, 0.05f, 0)], names, exampleCount: 6);
        Assert.True(learned.Learned);
        var plain = E5Categorization.Rank(labels, names, [Unit(1, 0.05f, 0)]);
        Assert.False(plain.Learned);
    }

    // ---- Lexical and duplicate keys -------------------------------------------------------------------------

    [Fact]
    public void ShortTermsMatchAtWordStartLongTermsInsideCompounds()
    {
        Assert.Equal(0, SemanticRanking.Occurrences("art", "Die Wartezeit"));
        Assert.Equal(1, SemanticRanking.Occurrences("art", "Eine Art Notiz"));
        Assert.Equal(1, SemanticRanking.Occurrences("rechnung", "Die Stromrechnung kam"));
    }

    [Fact]
    public void SearchPrefersRareWordsOverCommonOnes()
    {
        var chunks = Enumerable.Range(0, 20).Select(i => Chunk($"n:{i}", Unit(1, 0), "Projekt Notiz Nummer " + i))
            .Append(Chunk("n:rare", Unit(1, 0), "Zertifikat erneuert"))
            .Append(Chunk("n:common", Unit(1, 0), "Projekt Projekt Projekt")).ToList();
        // A query vector unrelated to all chunks: only the word matches count.
        var hits = SemanticRanking.Search(Unit(0, 1), "Projekt Zertifikat", chunks, 5);
        Assert.Equal("n:rare", hits[0].Chunk.ItemKey);
    }

    [Fact]
    public void DuplicateKeyIgnoresTrackingParametersAndHostCase()
    {
        Assert.Equal("https://example.com/artikel?id=7#teil",
            SemanticRanking.CanonicalUrl("https://Example.com/artikel/?utm_source=news&id=7&fbclid=abc#teil"));
        Assert.Equal(SemanticRanking.DuplicateKey("Lesen: https://example.com/a?utm_medium=mail ."),
            SemanticRanking.DuplicateKey("Lesen:   https://EXAMPLE.com/a ."));
        Assert.Equal("kein link", SemanticRanking.DuplicateKey("  kein \n link "));

        var chunks = new List<IndexedChunk>
        {
            Chunk("c:1", Unit(1, 0), "Seite https://example.com/a?utm_source=x"),
            Chunk("c:2", Unit(0, 1), "Seite https://example.com/a", minutes: 1),
        };
        var groups = SemanticRanking.DuplicateGroups(chunks, exactOnly: true);
        Assert.Single(groups);
        Assert.Single(groups[0].Duplicates);
    }
}

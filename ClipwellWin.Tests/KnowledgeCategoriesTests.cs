using System.Text.Json;
using ClipwellWin.Models;
using ClipwellWin.Services;
using Xunit;

namespace ClipwellWin.Tests;

public sealed class KnowledgeCategoriesTests
{
    private static AppSettings Settings(params (string Key, string Main, string Sub)[] items)
    {
        var s = new AppSettings();
        foreach (var (key, main, sub) in items) s.BrainAnnotations[key] = new BrainAnnotation("", main, sub);
        return s;
    }

    [Theory]
    [InlineData("Reisen › Strand", "Reisen", "Strand")]
    [InlineData("  Reisen  >  Strand  ", "Reisen", "Strand")]
    [InlineData("Reisen", "Reisen", "")]
    [InlineData(" › Strand", "", "")]
    [InlineData("", "", "")]
    public void ParseSplitsMainAndSubcategory(string text, string main, string sub)
    {
        var path = KnowledgeCategories.Parse(text);
        Assert.Equal(main, path.Main);
        Assert.Equal(sub, path.Sub);
    }

    [Fact]
    public void PathRoundTripsAndOldAnnotationsStayReadable()
    {
        Assert.Equal("Reisen › Strand", KnowledgeCategories.Combine("Reisen", "Strand"));
        Assert.Equal("Reisen", KnowledgeCategories.Combine("Reisen", " "));
        Assert.Equal("", KnowledgeCategories.Combine("", "Strand"));
        var old = JsonSerializer.Deserialize<AppSettings>("{\"BrainAnnotations\":{\"c:1\":{\"Name\":\"T\",\"Category\":\"Reisen\"}}}")!;
        Assert.Equal(new CategoryPath("Reisen", ""), KnowledgeCategories.Of(old.BrainAnnotations["c:1"]));
        Assert.Empty(old.KnowledgeCategories);
    }

    [Fact]
    public void AssignKeepsTitlesDefinesCategoriesAndRemovesEmptyAnnotations()
    {
        var s = new AppSettings();
        s.BrainAnnotations["c:1"] = new BrainAnnotation("Mein Titel", "Alt", "");
        var changed = KnowledgeCategories.Assign(s, ["c:1", "c:2", "c:2"], new CategoryPath("Reisen", "Strand"));
        Assert.Equal(["c:1", "c:2"], changed);
        Assert.Equal(new BrainAnnotation("Mein Titel", "Reisen", "Strand"), s.BrainAnnotations["c:1"]);
        Assert.Equal(["Reisen"], s.KnowledgeCategories.Select(c => c.Name));
        Assert.Equal(["Strand"], KnowledgeCategories.Subs(s, "reisen"));
        Assert.Empty(KnowledgeCategories.Assign(s, ["c:2"], new CategoryPath("Reisen", "Strand")));

        KnowledgeCategories.Assign(s, ["c:1", "c:2"], default);
        Assert.Equal(new BrainAnnotation("Mein Titel", "", ""), s.BrainAnnotations["c:1"]);
        Assert.False(s.BrainAnnotations.ContainsKey("c:2"));
    }

    [Fact]
    public void RecordingSnapshotsFallBackToTheRecordingCategory()
    {
        var s = Settings(("r:a.md", "Arbeit", "Projekt X"), ("r:a.md#3", "Privat", ""));
        Assert.Equal(new CategoryPath("Arbeit", "Projekt X"), KnowledgeCategories.Effective(s.BrainAnnotations, "r:a.md#1"));
        Assert.Equal(new CategoryPath("Privat", ""), KnowledgeCategories.Effective(s.BrainAnnotations, "r:a.md#3"));
        Assert.True(KnowledgeCategories.Effective(s.BrainAnnotations, "r:b.md#1").IsEmpty);
        // The recording key is neither an example nor counted twice.
        Assert.Equal(["r:a.md#3"], KnowledgeCategories.Assignments(s.BrainAnnotations).Keys);
        Assert.Equal(1, KnowledgeCategories.Counts(s).GetValueOrDefault("Privat"));
        Assert.Equal(0, KnowledgeCategories.Counts(s).GetValueOrDefault("Arbeit"));
    }

    [Fact]
    public void MainOnlyDialogsKeepTheSubcategory()
    {
        var old = new BrainAnnotation("T", "Reisen", "Strand");
        Assert.Equal(new BrainAnnotation("Neu", "Reisen", "Strand"), KnowledgeCategories.Annotation(old, "Neu", "reisen"));
        Assert.Equal(new BrainAnnotation("Neu", "Technik", ""), KnowledgeCategories.Annotation(old, "Neu", "Technik"));
        Assert.Equal(new BrainAnnotation("Neu", "Reisen", "Berg"), KnowledgeCategories.Annotation(old, "Neu", "Reisen › Berg"));
    }

    [Fact]
    public void RenamingMainOntoExistingMergesEntriesSubcategoriesAndDescriptions()
    {
        var s = Settings(("c:1", "Urlaub", "Strand"), ("c:2", "Reisen", "Berg"), ("c:3", "Technik", ""));
        KnowledgeCategories.SetDescription(s, new CategoryPath("Urlaub", ""), "Freizeit und Reisen");
        KnowledgeCategories.Define(s, new CategoryPath("Reisen", ""));
        var moved = KnowledgeCategories.Rename(s, new CategoryPath("Urlaub", ""), new CategoryPath("Reisen", ""));
        Assert.Equal(1, moved);
        Assert.Equal(new BrainAnnotation("", "Reisen", "Strand"), s.BrainAnnotations["c:1"]);
        Assert.Equal(["Reisen", "Technik"], KnowledgeCategories.Mains(s));
        Assert.Equal(["Berg", "Strand"], KnowledgeCategories.Subs(s, "Reisen"));
        Assert.Equal("Freizeit und Reisen", KnowledgeCategories.Description(s, new CategoryPath("Reisen", "")));
    }

    [Fact]
    public void SubcategoryCanMoveToAnotherMainCategory()
    {
        var s = Settings(("c:1", "Reisen", "Python"), ("c:2", "Reisen", "Strand"));
        KnowledgeCategories.SetDescription(s, new CategoryPath("Reisen", "Python"), "Skripte");
        KnowledgeCategories.Rename(s, new CategoryPath("Reisen", "Python"), new CategoryPath("Technik", "Python"));
        Assert.Equal(new BrainAnnotation("", "Technik", "Python"), s.BrainAnnotations["c:1"]);
        Assert.Equal(["Strand"], KnowledgeCategories.Subs(s, "Reisen"));
        Assert.Equal("Skripte", KnowledgeCategories.Description(s, new CategoryPath("Technik", "Python")));
        Assert.Equal("Skripte", KnowledgeCategories.Descriptions(s)["Technik › Python"]);
    }

    [Fact]
    public void DeletingSubKeepsMainAndDeletingMainClearsEntries()
    {
        var s = Settings(("c:1", "Reisen", "Strand"), ("c:2", "Reisen", ""), ("c:3", "Technik", ""));
        s.BrainAnnotations["c:4"] = new BrainAnnotation("Titel", "Reisen", "Strand");
        Assert.Equal(2, KnowledgeCategories.Delete(s, new CategoryPath("Reisen", "Strand")));
        Assert.Equal(new BrainAnnotation("", "Reisen", ""), s.BrainAnnotations["c:1"]);
        Assert.Equal(3, KnowledgeCategories.Delete(s, new CategoryPath("Reisen", "")));
        Assert.False(s.BrainAnnotations.ContainsKey("c:1"));
        Assert.Equal(new BrainAnnotation("Titel", "", ""), s.BrainAnnotations["c:4"]);
        Assert.Equal(["Technik"], KnowledgeCategories.Mains(s));
    }

    [Fact]
    public void PathsListMainsBeforeTheirSubcategories()
    {
        var s = Settings(("c:1", "Reisen", "Strand"), ("c:2", "Technik", ""));
        KnowledgeCategories.Define(s, new CategoryPath("Reisen", "Berg"));
        Assert.Equal(["Reisen", "Reisen › Berg", "Reisen › Strand", "Technik"], KnowledgeCategories.Paths(s));
    }

    [Fact]
    public void QueueKeepsPathsAndOffersSubcategoriesPerMain()
    {
        var queue = new CategorizationQueue([], new Dictionary<string, string> { ["c:1"] = "Reisen › Strand", ["c:2"] = "Technik" }, ["Reisen › Berg"]);
        Assert.Equal(["Reisen", "Technik"], queue.Mains);
        Assert.Equal(["Berg", "Strand"], queue.SubsOf("reisen").Order());
        Assert.NotNull(CategorizationQueue.Validate(" › Strand"));
        Assert.NotNull(CategorizationQueue.Validate("Reisen › " + new string('x', 81)));
        Assert.Null(CategorizationQueue.Validate("Reisen › Strand"));
    }

    [Fact]
    public void PracticeFeedbackComparesMainCategories()
    {
        var suggestion = new LocalAiSuggestion("", "Reisen › Strand", "Grund", false, Method: ClassifierMethods.Rocchio);
        Assert.Equal(SuggestionOutcome.Accepted, CategorizationReporting.Feedback(suggestion, "Reisen › Berg", DateTime.Now)!.Outcome);
        Assert.Equal(SuggestionOutcome.Corrected, CategorizationReporting.Feedback(suggestion, "Technik", DateTime.Now)!.Outcome);
    }

    [Fact]
    public void GraniteSeesSubcategoriesAndMayReturnOne()
    {
        var input = LocalAiCategorization.BuildInput([new BrainSource("c:1", "T", "Ein Text über einen Strandurlaub.", "", DateTime.MinValue)],
            ["Reisen", "Reisen › Strand", "Technik"]);
        using var data = JsonDocument.Parse(input);
        Assert.Equal(["Reisen", "Technik"], data.RootElement.GetProperty("existing_categories").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("Strand", data.RootElement.GetProperty("existing_subcategories").GetProperty("Reisen")[0].GetString());

        var content = JsonSerializer.Serialize(new { title = "Urlaub", category = "Reisen", subcategory = "Strand", reason = "Strandurlaub", uncertain = false });
        var envelope = JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = "stop", message = new { content } } } });
        Assert.Equal("Reisen › Strand", LocalAiCategorization.Parse(envelope).Category);
    }
}

public sealed class HierarchicalCategorizationTests
{
    // Keyword embedding: every text shares a base direction, keywords add their own axis.
    private static readonly string[] Keywords = ["reise", "technik", "strand", "berg", "stadt"];

    private static float[] Embed(string text)
    {
        var v = new float[Keywords.Length + 1];
        var lower = text.ToLowerInvariant();
        for (var i = 0; i < Keywords.Length; i++) if (lower.Contains(Keywords[i])) v[i] = 1;
        v[^1] = 2;
        return RocchioModel.Normalized(v);
    }

    private static E5Categorization Service(Dictionary<string, (string Text, DateTime Date)>? texts = null)
        => new((batch, _) => Task.FromResult(batch.Select(Embed).ToArray()), () => true,
            keys => (texts ?? []).Where(t => keys.Contains(t.Key)).ToDictionary());

    private static BrainSource Source(string text) => new("c:new", "Titel", text, "", DateTime.MinValue);

    [Fact]
    public async Task MainCategoryFirstThenItsSubcategory()
    {
        var result = await Service().SuggestAsync([Source("Unsere Reise an den Strand im Sommer, mit viel Sonne.")],
            ["Reisen › Strand", "Reisen › Berg", "Reisen › Stadt", "Technik"]);
        Assert.False(result.Uncertain);
        Assert.Equal("Reisen › Strand", result.Category);
        Assert.Equal("Strand", result.SubCandidates![0].Category);
        Assert.Contains("Unterkategorie: Strand", result.Reason);
    }

    [Fact]
    public async Task OtherMainCategoryIsNotOfferedSubcategoriesOfReisen()
    {
        var result = await Service().SuggestAsync([Source("Neue Technik für den Rechner, Strand als Bildschirmhintergrund.")],
            ["Reisen › Strand", "Reisen › Berg", "Technik"]);
        Assert.Equal("Technik", result.Category);
        Assert.Null(result.SubCandidates);
    }

    [Fact]
    public async Task SingleSubcategoryIsOnlyOfferedNeverChosen()
    {
        var sub = await Service().SuggestSubcategoryAsync([Source("Eine Reise an den Strand mit genug Text.")], "Reisen", ["Reisen › Strand"]);
        Assert.NotNull(sub);
        Assert.True(sub!.Uncertain);
        Assert.Equal(["Strand"], sub.SubCandidates!.Select(c => c.Category));
    }

    [Fact]
    public void TwoSubcategoriesNeedAgreementOfTheNearestOwnExamples()
    {
        float[][] names = [[1, 0, 0], [1, 0.01f, 0]];
        float[][][] examples = [[[0.9f, 1, 0], [0.9f, 0.9f, 0.1f]], [[0.9f, 0, 1], [0.9f, 0.1f, 0.9f]]];
        var clear = E5Categorization.RankSubcategories("Reisen", ["Strand", "Berg"], names, [RocchioModel.Normalized([0.9f, 1, 0.05f])], examples);
        Assert.False(clear.Uncertain);
        Assert.Equal("Reisen › Strand", clear.Category);
        var between = E5Categorization.RankSubcategories("Reisen", ["Strand", "Berg"], names, [RocchioModel.Normalized([0.9f, 0.5f, 0.5f])], examples);
        Assert.True(between.Uncertain);
    }

    [Fact]
    public async Task SubcategoryExamplesComeOnlyFromTheMainCategory()
    {
        var requested = new List<string>();
        var texts = new Dictionary<string, (string Text, DateTime Date)>
        {
            ["c:1"] = ("Erstes Beispiel einer Reise in die Berge mit Text.", DateTime.Now),
            ["c:2"] = ("Zweites Beispiel einer Reise in die Berge mit Text.", DateTime.Now),
            ["c:3"] = ("Technik mit Berg im Namen, gehört nicht zu Reisen.", DateTime.Now),
        };
        var service = new E5Categorization((batch, _) => Task.FromResult(batch.Select(Embed).ToArray()), () => true,
            keys => { requested.AddRange(keys); return texts.Where(t => keys.Contains(t.Key)).ToDictionary(); });
        await service.SuggestSubcategoryAsync([Source("Eine Reise in die Berge mit genug Text.")], "Reisen", ["Reisen › Strand"],
            assignments: new Dictionary<string, string> { ["c:1"] = "Reisen › Berg", ["c:2"] = "Reisen › Berg", ["c:3"] = "Technik › Berg" });
        Assert.Contains("c:1", requested);
        Assert.DoesNotContain("c:3", requested);
    }
}

public sealed class BrainCategoryViewTests
{
    private static readonly DateTime Day = new(2026, 11, 7, 9, 0, 0);

    private static IndexedChunk Item(string key, SemanticSource source, int minutes, float x, float y)
    {
        var v = new[] { x, y, 0.1f };
        SemanticRanking.Normalize(v);
        return new(new SemanticChunk(key + ":0", key, source, key, Day.AddMinutes(minutes * 30), "text", FilePath: source == SemanticSource.Recording ? "a.md" : null), v);
    }

    [Fact]
    public void CategoryViewGroupsByMainCategoryWithSubgroupsAndLinks()
    {
        IndexedChunk[] chunks =
        [
            Item("c:1", SemanticSource.Clipboard, 0, 1, 0), Item("c:2", SemanticSource.Clipboard, 1, 0, 1),
            Item("n:x.md", SemanticSource.Note, 2, 1, 1), Item("r:a.md#1", SemanticSource.Recording, 3, 0.5f, 1),
            Item("c:9", SemanticSource.Clipboard, 4, 1, 0.2f),
        ];
        var annotations = new Dictionary<string, BrainAnnotation>
        {
            ["c:1"] = new("", "Reisen", "Strand"), ["c:2"] = new("", "Reisen", "Strand"), ["n:x.md"] = new("", "reisen", ""),
            ["r:a.md"] = new("", "Technik", ""),
        };
        var graph = KnowledgeGraph.Build(chunks, new BrainOptions { Annotations = annotations, GroupByCategory = true });
        var reisen = Assert.Single(graph.Clusters, c => c.Label == "Reisen");
        Assert.Equal(3, reisen.Members.Count);
        Assert.Equal("Strand", Assert.Single(reisen.Subgroups).Name);
        Assert.Contains(graph.Clusters, c => c.Label == "Technik" && c.Members.Count == 1);
        Assert.Equal(-1, graph.Nodes.Single(n => n.ItemKey == "c:9").Cluster);
        var categoryEdges = graph.Edges.Where(e => e.Kind == GraphEdgeKind.Category).ToList();
        Assert.Equal(2, categoryEdges.Count);
        Assert.Contains(categoryEdges, e => e.Detail == "gleiche Kategorie Reisen › Strand");

        var topics = KnowledgeGraph.Build(chunks, new BrainOptions { Annotations = annotations });
        Assert.DoesNotContain(topics.Edges, e => e.Kind == GraphEdgeKind.Category);
    }
}

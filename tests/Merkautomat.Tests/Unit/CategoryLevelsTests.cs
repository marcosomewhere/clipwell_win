using System.Text.Json;
using Merkautomat.Models;
using Merkautomat.Services;
using Xunit;

namespace Merkautomat.Tests;

/// <summary>Three category levels: main › sub › fine.</summary>
public class CategoryLevelsTests
{
    private static AppSettings Settings(params (string Key, string Path)[] assignments)
    {
        var s = new AppSettings();
        foreach (var (key, path) in assignments) KnowledgeCategories.Assign(s, [key], KnowledgeCategories.Parse(path));
        return s;
    }

    [Theory]
    [InlineData("Technik › Netzwerk › WLAN", "Technik", "Netzwerk", "WLAN")]
    [InlineData("Technik > Netzwerk > WLAN > Gast", "Technik", "Netzwerk", "WLAN Gast")]
    [InlineData("Technik › Technik › WLAN", "Technik", "", "")]
    [InlineData("Technik › Netzwerk › Netzwerk", "Technik", "Netzwerk", "")]
    [InlineData(" › Netzwerk › WLAN", "", "", "")]
    public void ParseReadsUpToThreeLevels(string text, string main, string sub, string fine)
    {
        var p = KnowledgeCategories.Parse(text);
        Assert.Equal((main, sub, fine), (p.Main, p.Sub, p.Fine));
    }

    [Fact]
    public void PathKnowsDepthParentAndLeaf()
    {
        var p = new CategoryPath("Technik", "Netzwerk", "WLAN");
        Assert.Equal(3, p.Depth);
        Assert.Equal("Technik › Netzwerk", p.Parent.ToString());
        Assert.Equal("WLAN", p.Leaf);
        Assert.Equal("Technik › Netzwerk › WLAN", KnowledgeCategories.Combine("Technik", "Netzwerk › WLAN"));
        Assert.True(KnowledgeCategories.IsWithin(p, KnowledgeCategories.Parse("technik › netzwerk")));
        Assert.False(KnowledgeCategories.IsWithin(KnowledgeCategories.Parse("Technik"), p));
        Assert.Equal("Technik › Netzwerk › WLAN", KnowledgeCategories.Build(["Technik", "Netzwerk", "WLAN", "Gast"]).ToString());
    }

    [Fact]
    public void AssignStoresTheFineCategoryAndShorterDialogPathsKeepIt()
    {
        var s = Settings(("c:1", "Technik › Netzwerk › WLAN"));
        Assert.Equal("WLAN", s.BrainAnnotations["c:1"].Finecategory);
        Assert.Equal(["Technik", "Technik › Netzwerk", "Technik › Netzwerk › WLAN"], KnowledgeCategories.Paths(s));
        Assert.Equal(["Netzwerk", "Netzwerk › WLAN"], KnowledgeCategories.SubPaths(s, "Technik"));
        Assert.Equal("WLAN", KnowledgeCategories.Annotation(s.BrainAnnotations["c:1"], "", "Technik › Netzwerk").Finecategory);
        Assert.Equal("", KnowledgeCategories.Annotation(s.BrainAnnotations["c:1"], "", "Technik › Server").Finecategory);
    }

    [Fact]
    public void CountsIncludeEveryLevel()
    {
        var counts = KnowledgeCategories.Counts(Settings(("c:1", "Technik › Netzwerk › WLAN"), ("c:2", "Technik › Netzwerk"), ("c:3", "Technik")));
        Assert.Equal(3, counts["Technik"]);
        Assert.Equal(2, counts["Technik › Netzwerk"]);
        Assert.Equal(1, counts["Technik › Netzwerk › WLAN"]);
    }

    [Fact]
    public void RenamingASubcategoryMovesItsFineCategoriesAndDescriptions()
    {
        var s = Settings(("c:1", "Technik › Netzwerk › WLAN"), ("c:2", "Technik › Netzwerk"));
        KnowledgeCategories.SetDescription(s, KnowledgeCategories.Parse("Technik › Netzwerk › WLAN"), "Funknetze");

        var moved = KnowledgeCategories.Rename(s, KnowledgeCategories.Parse("Technik › Netzwerk"), KnowledgeCategories.Parse("IT › Netze"));

        Assert.Equal(2, moved);
        Assert.Equal("IT › Netze › WLAN", KnowledgeCategories.Of(s.BrainAnnotations["c:1"]).ToString());
        Assert.Equal("IT › Netze", KnowledgeCategories.Of(s.BrainAnnotations["c:2"]).ToString());
        Assert.Equal("Funknetze", KnowledgeCategories.Description(s, KnowledgeCategories.Parse("IT › Netze › WLAN")));
        Assert.Empty(KnowledgeCategories.Subs(s, "Technik"));
    }

    [Fact]
    public void RenamingOntoAnExistingFineCategoryMerges()
    {
        var s = Settings(("c:1", "Technik › Netzwerk › WLAN"), ("c:2", "Technik › Netzwerk › Funk"));
        KnowledgeCategories.Rename(s, KnowledgeCategories.Parse("Technik › Netzwerk › Funk"), KnowledgeCategories.Parse("Technik › Netzwerk › WLAN"));
        Assert.Equal(["WLAN"], KnowledgeCategories.Fines(s, "Technik", "Netzwerk"));
        Assert.Equal("Technik › Netzwerk › WLAN", KnowledgeCategories.Of(s.BrainAnnotations["c:2"]).ToString());
    }

    [Fact]
    public void DeletingALevelMovesItsEntriesOneLevelUp()
    {
        var s = Settings(("c:1", "Technik › Netzwerk › WLAN"), ("c:2", "Technik › Netzwerk"));
        Assert.Equal(1, KnowledgeCategories.Delete(s, KnowledgeCategories.Parse("Technik › Netzwerk › WLAN")));
        Assert.Equal("Technik › Netzwerk", KnowledgeCategories.Of(s.BrainAnnotations["c:1"]).ToString());

        Assert.Equal(2, KnowledgeCategories.Delete(s, KnowledgeCategories.Parse("Technik › Netzwerk")));
        Assert.Equal("Technik", KnowledgeCategories.Of(s.BrainAnnotations["c:1"]).ToString());
        Assert.Empty(KnowledgeCategories.Subs(s, "Technik"));
    }

    [Fact]
    public void OlderSettingsWithoutFineCategoriesStillLoad()
    {
        var old = JsonSerializer.Deserialize<BrainAnnotation>("{\"Name\":\"x\",\"Category\":\"Technik\",\"Subcategory\":\"Netzwerk\"}")!;
        Assert.Equal("Technik › Netzwerk", KnowledgeCategories.Of(old).ToString());
        var definitions = JsonSerializer.Deserialize<KnowledgeCategory>("{\"Name\":\"Technik\",\"Subcategories\":[{\"Name\":\"Netzwerk\"}]}")!;
        Assert.Empty(definitions.Subcategories[0].Subcategories);
    }

    [Fact]
    public void QueueOffersSubAndFinePaths()
    {
        var queue = new CategorizationQueue([], new Dictionary<string, string> { ["c:1"] = "Technik › Netzwerk › WLAN" }, []);
        Assert.Contains("Technik › Netzwerk", queue.Categories);
        Assert.Equal(["Netzwerk", "Netzwerk › WLAN"], queue.SubsOf("Technik").Order());
    }

    [Fact]
    public void LanguageModelSeesAndReturnsFineCategories()
    {
        var input = LocalAiCategorization.BuildInput([new BrainSource("c:1", "Titel", "Ein ausreichend langer Text über Netze.", "", DateTime.Now)],
            ["Technik › Netzwerk › WLAN", "Technik › Netzwerk"]);
        using var data = JsonDocument.Parse(input);
        Assert.Equal("WLAN", data.RootElement.GetProperty("existing_finecategories").GetProperty("Technik › Netzwerk")[0].GetString());

        var content = JsonSerializer.Serialize(new { title = "WLAN", category = "Technik", subcategory = "Netzwerk", finecategory = "WLAN", reason = "Grund", uncertain = false });
        var response = JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = "stop", message = new { content } } } });
        Assert.Equal("Technik › Netzwerk › WLAN", LocalAiCategorization.Parse(response).Category);
    }

    [Fact]
    public void E5RanksFineCategoriesBelowASubcategory()
    {
        float[] V(float a, float b) => [a, b];
        var result = E5Categorization.RankSubcategories("Technik › Netzwerk", ["WLAN", "Kabel"],
            [V(1, 0), V(0, 1)], [V(0.95f, 0.1f)], [[], []]);
        Assert.False(result.Uncertain);
        Assert.Equal("Technik › Netzwerk › WLAN", result.Category);
        Assert.StartsWith("Feinkategorie", result.Reason);
    }

    [Fact]
    public void ExportAndBrainShowTheFineLevel()
    {
        var article = new KbArticle(KbArticleKind.Note, "T", DateTime.Now, "t.md", "x", KnowledgeCategories.Parse("Technik › Netzwerk › WLAN"));
        Assert.StartsWith("Technik/Netzwerk/WLAN/", KnowledgeExport.ZipFolder(article, byCategory: true));

        var nodes = Enumerable.Range(0, 3).Select(i => new GraphNode
        {
            Index = i, Item = new SemanticChunk($"c:{i}:0", $"c:{i}", SemanticSource.Clipboard, "t", DateTime.Now, "t"), Text = "t", Vector = [1f],
        }).ToList();
        var paths = new[] { "Technik › Netzwerk › WLAN", "Technik › Netzwerk", "Technik" }.Select(KnowledgeCategories.Parse).ToList();
        var cluster = Assert.Single(KnowledgeGraph.CategoryClusters(nodes, paths));
        Assert.Contains(cluster.Subgroups, g => g.Name == "Netzwerk" && g.Members.Count == 2);
        Assert.Contains(cluster.Subgroups, g => g.Name == "Netzwerk › WLAN" && g.Members.Count == 1);
    }
}

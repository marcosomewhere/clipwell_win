using System.IO;
using System.Text.Json;
using Merkautomat.Models;
using Merkautomat.Services;
using Xunit;

namespace Merkautomat.Tests;

public class CategoryRefinementTests
{
    private static Dictionary<string, BrainAnnotation> Annotations(int technik, int netzwerk, int wlan)
    {
        var a = new Dictionary<string, BrainAnnotation>(StringComparer.Ordinal);
        for (var i = 0; i < technik; i++) a[$"t:{i}"] = new BrainAnnotation(Category: "Technik");
        for (var i = 0; i < netzwerk; i++) a[$"n:{i}"] = new BrainAnnotation(Category: "Technik", Subcategory: "Netzwerk");
        for (var i = 0; i < wlan; i++) a[$"w:{i}"] = new BrainAnnotation(Category: "Technik", Subcategory: "Netzwerk", Finecategory: "WLAN");
        return a;
    }

    private static string Envelope(object content)
        => JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = "stop", message = new { content = JsonSerializer.Serialize(content) } } } });

    [Fact]
    public void ParentsCountOnlyEntriesExactlyOnTheLevelAndNeverFineCategories()
    {
        var annotations = Annotations(technik: 5, netzwerk: 12, wlan: 30);
        var parents = CategoryRefinement.Parents(annotations.Keys, annotations);
        var parent = Assert.Single(parents);
        Assert.Equal("Technik › Netzwerk", parent.Parent.ToString());
        Assert.Equal(12, parent.Keys.Count);
    }

    [Fact]
    public void AnswerBecomesAProposalOneLevelDeeper()
    {
        var suggestion = CategoryRefinement.Parse(Envelope(new { name = "WLAN › Gast", description = "Funknetze", reason = "alle über WLAN", uncertain = false }));
        Assert.Equal("WLAN Gast", suggestion.Name);
        var members = new[] { new DiscoveryItem("n:1", "Router", "Text", DateTime.Now, [1f]) };

        var proposal = CategoryRefinement.ToProposal(KnowledgeCategories.Parse("Technik › Netzwerk"), members, suggestion, "granite");

        Assert.NotNull(proposal);
        Assert.Equal("Technik › Netzwerk › WLAN Gast", proposal.ProposedName);
        Assert.Contains("Funknetze", proposal.Reason);
        Assert.Equal("Funknetze", proposal.Description);
        var state = new CategoryRefinementState();
        state.Record(KnowledgeCategories.Parse("Technik › Netzwerk"), ["n:1"], [proposal]);
        Assert.Equal("Funknetze", state.Descriptions()["technik › netzwerk › wlan gast"]);
        Assert.Null(CategoryRefinement.ToProposal(KnowledgeCategories.Parse("Technik › Netzwerk › WLAN"), members, suggestion, "granite"));
    }

    [Fact]
    public void UnsureOrRepeatingAnswersGiveNoProposal()
    {
        var unsure = CategoryRefinement.Parse(Envelope(new { name = "Irgendwas", description = "", reason = "gemischt", uncertain = true }));
        var repeat = new RefinementSuggestion("Netzwerk", "", "", false);
        var parent = KnowledgeCategories.Parse("Technik › Netzwerk");
        var members = new[] { new DiscoveryItem("n:1", "Router", "Text", DateTime.Now, [1f]) };
        Assert.Null(CategoryRefinement.ToProposal(parent, members, unsure, "granite"));
        Assert.Null(CategoryRefinement.ToProposal(parent, members, repeat, "granite"));
        Assert.Throws<InvalidDataException>(() => CategoryRefinement.Parse("{\"choices\":[]}"));
    }

    [Fact]
    public void RequestCarriesParentExistingAndTheEditablePrompt()
    {
        var input = CategoryRefinement.BuildInput(KnowledgeCategories.Parse("Technik"), ["Netzwerk"],
            [new BrainSource("c:1", "Router", "Ein Text über Router.", "", DateTime.Now)]);
        using var request = JsonDocument.Parse(CategoryRefinement.BuildRequest(input, "Eigener Prompt"));
        Assert.Equal("Eigener Prompt", request.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
        using var data = JsonDocument.Parse(request.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
        Assert.Equal("Technik", data.RootElement.GetProperty("parent").GetString());
        Assert.Equal("Netzwerk", data.RootElement.GetProperty("existing")[0].GetString());
    }

    [Fact]
    public void StateLooksAtACategoryAgainOnlyWhenItsMembersChange()
    {
        var annotations = Annotations(technik: 0, netzwerk: 12, wlan: 0);
        var parents = CategoryRefinement.Parents(annotations.Keys, annotations);
        var state = new CategoryRefinementState();
        var next = state.Next(parents)!.Value;
        state.Record(next.Parent, next.Keys, []);
        Assert.Null(state.Next(parents));

        annotations["n:new"] = new BrainAnnotation(Category: "Technik", Subcategory: "Netzwerk");
        Assert.NotNull(state.Next(CategoryRefinement.Parents(annotations.Keys, annotations)));
    }

    [Fact]
    public void ReadyProposalsKeepOnlyMembersStillOnTheParentLevel()
    {
        var annotations = Annotations(technik: 0, netzwerk: 3, wlan: 0);
        var state = new CategoryRefinementState();
        state.Record(KnowledgeCategories.Parse("Technik › Netzwerk"), ["n:0", "n:1", "n:2"],
            [new StoredProposal(["n:0", "n:1", "n:2"], ["a", "b", "c"], "Technik › Netzwerk › WLAN", "Grund", false, "granite")]);
        annotations["n:2"] = new BrainAnnotation(Category: "Technik", Subcategory: "Netzwerk", Finecategory: "Kabel");

        var ready = Assert.Single(state.Ready(annotations, ["Technik", "Technik › Netzwerk"]));

        Assert.Equal(["n:0", "n:1"], ready.Keys);
        Assert.True(ready.IsNew);
        Assert.Equal("Technik › Netzwerk › WLAN", ready.Name);
        Assert.False(state.Seen);
    }

    [Fact]
    public void RefinePromptIsListedAndEditable()
    {
        var s = new AppSettings();
        Assert.Contains(AiPrompts.All, p => p.Id == AiPrompts.Refine && p.Default == CategoryRefinement.DefaultPrompt);
        AiPrompts.Set(s, AiPrompts.Refine, "Feiner unterteilen.");
        Assert.Equal("Feiner unterteilen.", AiPrompts.Effective(AiPrompts.Refine, AiPrompts.Custom(s, AiPrompts.Refine)));
    }
}

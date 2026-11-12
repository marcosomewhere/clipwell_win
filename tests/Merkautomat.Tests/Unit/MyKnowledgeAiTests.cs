using System.IO;
using System.Text.Json;
using Merkautomat.Models;
using Merkautomat.Services;
using Xunit;

namespace Merkautomat.Tests;

public class MyKnowledgeAiTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0);

    private static MyKnowledgeContext Context(Dictionary<string, BrainAnnotation>? annotations = null, IReadOnlyDictionary<string, double>? affinity = null)
        => new(annotations ?? [], new HashSet<string>(StringComparer.Ordinal), 60, 7, Affinity: affinity);

    private static IndexedChunk Chunk(string key, params float[] vector)
    {
        var norm = (float)Math.Sqrt(vector.Sum(x => x * x));
        return new IndexedChunk(new SemanticChunk(key + ":0", key, SemanticSource.Clipboard, key, Now, "text"), vector.Select(x => x / norm).ToArray());
    }

    private static string Envelope(object content, string finish = "stop")
        => JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = finish, message = new { content = JsonSerializer.Serialize(content) } } } });

    // ---- Closeness to the core knowledge -----------------------------------------------------------------------

    [Fact]
    public void AffinityMeasuresClosenessToTheNearestCoreItem()
    {
        var snapshot = new[] { Chunk("f:core", 1, 0), Chunk("c:near", 1, 0.05f), Chunk("c:mid", 1, 0.55f), Chunk("c:far", 0, 1) };

        var affinity = MyKnowledgeAi.Affinity(snapshot, new HashSet<string> { "f:core" }, new HashSet<string> { "f:core", "c:near", "c:mid", "c:far", "c:none" });

        Assert.Equal(1, affinity["c:near"], 3);
        Assert.InRange(affinity["c:mid"], 0.01, 0.99);
        Assert.Equal(0, affinity["c:far"], 3);
        Assert.False(affinity.ContainsKey("f:core"));
        Assert.False(affinity.ContainsKey("c:none"));
    }

    [Fact]
    public void AffinityRaisesTheWeightByAtMostThirtyPercent()
    {
        var entry = new MyKnowledgeEntry { Key = "c:1", Source = SemanticSource.Recording, Date = Now };
        var plain = MyKnowledge.Weight(entry, Now, Context());
        var near = MyKnowledge.Weight(entry, Now, Context(affinity: new Dictionary<string, double> { ["c:1"] = 1 }));
        Assert.Equal(plain * (1 + MyKnowledgeAi.AffinityBoost), near, 6);
    }

    [Fact]
    public void WithoutCoreItemsThereIsNoAffinity()
        => Assert.Empty(MyKnowledgeAi.Affinity([Chunk("c:1", 1, 0)], new HashSet<string>(), new HashSet<string> { "c:1" }));

    // ---- Proposals ----------------------------------------------------------------------------------------

    [Fact]
    public void E5ProposalIsSureOnlyWhenClearWithOwnExamples()
    {
        var sure = new MyKnowledgeEntry { Key = "c:1" };
        var unsure = new MyKnowledgeEntry { Key = "c:2" };

        MyKnowledgeAi.ApplyE5(sure, new LocalAiSuggestion("", "Technik", "passt", false) { Learned = true });
        MyKnowledgeAi.ApplyE5(unsure, new LocalAiSuggestion("", "Technik", "knapp", false));

        Assert.True(sure.SuggestionSure);
        Assert.False(unsure.SuggestionSure);
        Assert.Equal("Technik", unsure.SuggestedCategory);
        Assert.True(sure.SuggestionChecked && unsure.SuggestionChecked);
        Assert.Equal(["c:1"], MyKnowledgeAi.SureSuggestions([sure, unsure], Context()).Select(e => e.Key));
        // The real method is kept for the evaluation; the user sees which model made the proposal.
        Assert.Equal(ClassifierMethods.Rocchio, sure.SuggestionMethod);
        Assert.Equal("E5", MyKnowledgeAi.SuggestionSource(sure));
        var feedback = MyKnowledgeAi.SuggestionOf(sure);
        Assert.Equal(("Technik", ClassifierMethods.Rocchio, true), (feedback.Category, feedback.Method, feedback.Learned));
    }

    [Fact]
    public void AProposalNeverProtectsAndEndsWithTheUsersChoice()
    {
        var entry = new MyKnowledgeEntry { Key = "c:1", Source = SemanticSource.Clipboard, Date = Now };
        MyKnowledgeAi.ApplyE5(entry, new LocalAiSuggestion("", "Technik", "", false) { Learned = true });

        Assert.False(MyKnowledge.IsProtected(entry, Context()));
        Assert.True(MyKnowledgeAi.HasOpenSuggestion(entry, Context()));
        Assert.False(MyKnowledgeAi.HasOpenSuggestion(entry, Context(new() { ["c:1"] = new BrainAnnotation(Category: "Privat") })));
    }

    [Fact]
    public void LanguageModelGivesTitlesToCopiedTextButDoesNotOverrideSureProposals()
    {
        var clip = new MyKnowledgeEntry { Key = "c:1", Source = SemanticSource.Clipboard, Title = "https://beispiel.de/x?y" };
        var note = new MyKnowledgeEntry { Key = "n:a.md", Source = SemanticSource.Note, Title = "Meine Notiz" };
        MyKnowledgeAi.ApplyE5(clip, new LocalAiSuggestion("", "Technik", "", false) { Learned = true });

        MyKnowledgeAi.ApplyModel(clip, new LocalAiSuggestion("Anleitung WLAN-Zertifikat", "Netzwerk", "", false), "granite");
        MyKnowledgeAi.ApplyModel(note, new LocalAiSuggestion("Anderer Titel", "Privat", "Grund", false), "granite");

        Assert.Equal("Anleitung WLAN-Zertifikat", MyKnowledgeAi.DisplayTitle(clip));
        Assert.Equal("Technik", clip.SuggestedCategory);
        Assert.Equal("", note.AiTitle);
        Assert.Equal("Privat", note.SuggestedCategory);
        Assert.False(note.SuggestionSure);
        Assert.True(clip.ModelChecked && note.ModelChecked);
    }

    [Fact]
    public void UnsureModelAnswersLeaveTheCategoryOpen()
    {
        var entry = new MyKnowledgeEntry { Key = "c:1", Source = SemanticSource.Clipboard };
        MyKnowledgeAi.ApplyModel(entry, new LocalAiSuggestion("Titel", "Irgendwas", "", true), "qwen");
        Assert.Equal("", entry.SuggestedCategory);
        Assert.Equal("Titel", entry.AiTitle);
    }

    [Fact]
    public void ModelWorkIsNeededOnlyWhereItAddsSomething()
    {
        var context = Context();
        Assert.True(MyKnowledgeAi.NeedsModel(new MyKnowledgeEntry { Key = "c:1", Source = SemanticSource.Clipboard }, context));
        Assert.False(MyKnowledgeAi.NeedsModel(new MyKnowledgeEntry { Key = "n:a", Source = SemanticSource.Note, SuggestionSure = true }, context));
        Assert.False(MyKnowledgeAi.NeedsModel(new MyKnowledgeEntry { Key = "d:x", Digest = true }, context));
        Assert.False(MyKnowledgeAi.NeedsModel(new MyKnowledgeEntry { Key = "c:2", Source = SemanticSource.Clipboard, ModelChecked = true }, context));
        Assert.False(MyKnowledgeAi.NeedsSuggestion(new MyKnowledgeEntry { Key = "c:3" }, Context(new() { ["c:3"] = new BrainAnnotation(Category: "Technik") })));
    }

    [Fact]
    public void ChangedCategoriesMakeOpenProposalsAgain()
    {
        var categorized = new MyKnowledgeEntry { Key = "c:1", SuggestionChecked = true };
        var open = new MyKnowledgeEntry { Key = "c:2", SuggestionChecked = true, SuggestedCategory = "Alt", SuggestionSure = true, AiTitle = "Titel" };
        var state = new MyKnowledgeState { Entries = [categorized, open] };
        var context = Context(new() { ["c:1"] = new BrainAnnotation(Category: "Technik") });

        Assert.False(MyKnowledgeAi.ResetSuggestionsFor(state, ["Alt", "Technik"], context));
        Assert.False(MyKnowledgeAi.ResetSuggestionsFor(state, ["technik", "alt"], context));
        Assert.True(MyKnowledgeAi.ResetSuggestionsFor(state, ["Neu", "Technik"], context));

        Assert.False(open.SuggestionChecked);
        Assert.Equal("", open.SuggestedCategory);
        Assert.False(open.SuggestionSure);
        Assert.Equal("Titel", open.AiTitle);
        Assert.True(categorized.SuggestionChecked);
    }

    // ---- Summaries --------------------------------------------------------------------------------------------

    [Fact]
    public void SummaryKeepsOnlySentencesWithValidSources()
    {
        var json = Envelope(new
        {
            sentences = new object[]
            {
                new { text = "Der Router braucht ein neues Zertifikat [1].", sources = new[] { 1, 9 } },
                new { text = "Ohne Beleg erfunden.", sources = Array.Empty<int>() },
                new { text = "Nur ungültige Quelle.", sources = new[] { 7 } },
                new { text = "Drucker und Router wurden gewartet.", sources = new[] { 2, 1, 2 } },
            },
        });

        var sentences = MyKnowledgeAi.ParseSummary(json, 2);

        Assert.Equal(2, sentences.Count);
        Assert.Equal("Der Router braucht ein neues Zertifikat.", sentences[0].Text);
        Assert.Equal([1], sentences[0].Sources);
        Assert.Equal([1, 2], sentences[1].Sources);
    }

    [Fact]
    public void IncompleteOrBrokenSummariesAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => MyKnowledgeAi.ParseSummary(Envelope(new { sentences = Array.Empty<object>() }, "length"), 1));
        Assert.Throws<InvalidDataException>(() => MyKnowledgeAi.ParseSummary("{\"choices\":[]}", 1));
    }

    [Fact]
    public void SummaryRequestNumbersTheSourcesAndLimitsTheSentences()
    {
        using var request = JsonDocument.Parse(MyKnowledgeAi.BuildSummaryRequest(["Erste Aussage", "Zweite Aussage"], 3, "Fasse zusammen.", AiPrompts.DigestDefault));
        var input = JsonDocument.Parse(request.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!).RootElement;
        Assert.Equal(2, input.GetProperty("sources")[1].GetProperty("n").GetInt32());
        Assert.Equal("Fasse zusammen.", input.GetProperty("task").GetString());
        var schema = request.RootElement.GetProperty("response_format").GetProperty("json_schema");
        Assert.True(schema.GetProperty("strict").GetBoolean());
        Assert.Equal(3, schema.GetProperty("schema").GetProperty("properties").GetProperty("sentences").GetProperty("maxItems").GetInt32());
    }

    [Fact]
    public void DigestShowsTheModelSummaryOnlyWhileItFitsTheStatements()
    {
        var digest = new MyKnowledgeEntry { Key = "d:x", Digest = true, Members = 2, Statements = [new("Aussage eins ist lang genug.", "c:1"), new("Aussage zwei ist lang genug.", "c:2")] };
        digest.AiSummary = [new MyKnowledgeSummarySentence("Kurz gesagt: zwei Aussagen.", [1, 2])];
        digest.AiSummaryFor = MyKnowledgeAi.Fingerprint(digest.Statements);
        MyKnowledge.Render(digest);
        Assert.Contains("## Zusammenfassung", digest.Text);
        Assert.Contains("Kurz gesagt: zwei Aussagen. [1, 2]", digest.Text);
        Assert.Contains("1. Aussage eins", digest.Text);

        digest.Statements.RemoveAt(0);
        MyKnowledge.Render(digest);
        Assert.DoesNotContain("## Zusammenfassung", digest.Text);
    }

    [Fact]
    public void DeletingASourceAlsoDropsTheModelSummaryAndReview()
    {
        var digest = new MyKnowledgeEntry
        {
            Key = "d:x", Digest = true, Members = 2,
            Statements = [new("Aussage eins ist lang genug.", "c:1"), new("Aussage zwei ist lang genug.", "c:2")],
            AiSummary = [new MyKnowledgeSummarySentence("Enthält Inhalt aus c:1.", [1])],
        };
        digest.AiSummaryFor = MyKnowledgeAi.Fingerprint(digest.Statements);
        var state = new MyKnowledgeState { Entries = [digest] };
        state.ReviewSentences["abc"] = new MyKnowledgeSummarySentence("Satz", [1]);

        MyKnowledge.Forget(state, ["c:1"]);

        Assert.Empty(digest.AiSummary);
        Assert.Empty(state.ReviewSentences);
    }

    // ---- Weekly review ----------------------------------------------------------------------------------------

    [Fact]
    public void ReviewGroupsTheLastSevenDaysByCategory()
    {
        var context = Context(new() { ["c:1"] = new BrainAnnotation(Category: "Technik"), ["c:2"] = new BrainAnnotation(Category: "Technik") });
        var entries = new List<MyKnowledgeEntry>
        {
            new() { Key = "c:1", Added = Now.AddDays(-1), Weight = 0.5 },
            new() { Key = "c:2", Added = Now.AddDays(-6), Weight = 0.4 },
            new() { Key = "c:3", Added = Now.AddDays(-2), Weight = 0.3 },
            new() { Key = "c:4", Added = Now.AddDays(-9), Weight = 0.9 },
            new() { Key = "d:x", Digest = true, Added = Now },
        };

        var review = MyKnowledgeAi.Review(entries, context, Now);

        Assert.Equal(["Technik", "Ohne Kategorie"], review.Select(g => g.Name));
        Assert.Equal(["c:1", "c:2"], review[0].Entries.Select(e => e.Key));
        var again = MyKnowledgeAi.Review(entries, context, Now);
        Assert.Equal(review[0].Signature, again[0].Signature);
        entries.Add(new MyKnowledgeEntry { Key = "c:5", Added = Now });
        Assert.NotEqual(review[1].Signature, MyKnowledgeAi.Review(entries, context, Now)[1].Signature);
    }

    // ---- Questions ------------------------------------------------------------------------------------------

    [Fact]
    public void QuestionSourcesCombineMeaningWordsAndWeightIncludingDigests()
    {
        var entries = new List<MyKnowledgeEntry>
        {
            new() { Key = "c:1", Title = "Notiz", Text = "Der Router im Büro braucht ein Zertifikat", Weight = 0.3 },
            new() { Key = "d:x", Digest = true, Title = "Zusammenfassung", Text = "Router Zertifikat erneuert im Oktober", Weight = 0.5 },
            new() { Key = "f:x", Title = "Handbuch", Text = "Ganz anderes Thema", Weight = 1 },
            new() { Key = "n:a", Title = "Plan", Text = "Unpassend", Weight = 0.8 },
        };
        var semantic = new Dictionary<string, double> { ["f:x"] = 0.9, ["n:a"] = 0.1 };

        var ranked = MyKnowledgeAi.RankForQuestion(entries, "Wann braucht der Router ein Zertifikat?", semantic);

        Assert.Equal("f:x", ranked[0].Key);
        Assert.Contains(ranked, e => e.Key == "d:x");
        Assert.Contains(ranked, e => e.Key == "c:1");
        Assert.DoesNotContain(ranked, e => e.Key == "n:a");

        var sources = MyKnowledgeAi.AnswerSources(ranked);
        Assert.Equal(Enumerable.Range(1, ranked.Count), sources.Select(s => s.Number));
        Assert.Equal(ranked[0].Key, sources[0].Chunk.ItemKey);
    }
}

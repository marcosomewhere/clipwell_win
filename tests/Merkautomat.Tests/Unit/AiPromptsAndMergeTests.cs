using System.Text.Json;
using Merkautomat.Models;
using Merkautomat.Services;
using Xunit;

namespace Merkautomat.Tests;

public class AiPromptsTests
{
    [Fact]
    public void EveryPromptHasADefaultUseAndDataDescription()
    {
        Assert.Equal(5, AiPrompts.All.Count);
        Assert.All(AiPrompts.All, p =>
        {
            Assert.NotEmpty(p.Default);
            Assert.NotEmpty(p.UsedFor);
            Assert.NotEmpty(p.Data);
        });
        Assert.Equal(AiPrompts.All.Count, AiPrompts.All.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void DefaultOrEmptyTextIsNotStored()
    {
        var s = new AppSettings();
        AiPrompts.Set(s, AiPrompts.Answer, "Antworte knapp.");
        Assert.Equal("Antworte knapp.", AiPrompts.Custom(s, AiPrompts.Answer));
        AiPrompts.Set(s, AiPrompts.Answer, AiPrompts.AnswerDefault);
        Assert.Equal("", AiPrompts.Custom(s, AiPrompts.Answer));
        Assert.Equal(AiPrompts.AnswerDefault, AiPrompts.Effective(AiPrompts.Answer, ""));
        Assert.Equal(AiPrompts.MaxChars, AiPrompts.Effective(AiPrompts.Digest, new string('x', 5000)).Length);
    }

    [Fact]
    public void CategorizePromptStaysInItsOldSetting()
    {
        var s = new AppSettings { CategorizationPrompt = "Alt gespeichert" };
        Assert.Equal("Alt gespeichert", AiPrompts.Custom(s, AiPrompts.Categorize));
        AiPrompts.Set(s, AiPrompts.Categorize, "Neu");
        Assert.Equal("Neu", s.CategorizationPrompt);
        Assert.Empty(s.AiPrompts);
    }

    [Fact]
    public void AnswerAndSummaryRequestsUseTheGivenPrompt()
    {
        using var answer = JsonDocument.Parse(KnowledgeAnswer.BuildRequest("{}", "Nur Fakten."));
        Assert.Equal("Nur Fakten.", answer.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
        using var standard = JsonDocument.Parse(KnowledgeAnswer.BuildRequest("{}"));
        Assert.Equal(AiPrompts.AnswerDefault, standard.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
        using var summary = JsonDocument.Parse(MyKnowledgeAi.BuildSummaryRequest(["a"], 1, "t", "Ein Satz."));
        Assert.Equal("Ein Satz.", summary.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public void CategorizationPromptCanBeReplacedAndFallsBackToTheDefault()
    {
        Assert.Equal(LocalAiCategorization.DefaultPrompt, AiPrompts.Effective(AiPrompts.Categorize, "  "));
        Assert.Equal(AiPrompts.MaxChars, AiPrompts.Effective(AiPrompts.Categorize, new string('a', 5000)).Length);

        using var custom = JsonDocument.Parse(LocalAiCategorization.BuildRequest("{}", "Ordne nach Projekten."));
        Assert.Equal("Ordne nach Projekten.", custom.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
        // The answer format stays fixed by the schema.
        Assert.True(custom.RootElement.GetProperty("response_format").GetProperty("json_schema").GetProperty("strict").GetBoolean());

        using var standard = JsonDocument.Parse(LocalAiCategorization.BuildRequest("{}"));
        Assert.Equal(LocalAiCategorization.DefaultPrompt, standard.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
    }
}

public class BrainMergeTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 10, 0, 0);

    private static MergeSource Source(string key, string text, SemanticSource source = SemanticSource.Clipboard, bool removable = true)
        => new(key, source, "Titel " + key, Now, text, "Herkunft " + key, removable);

    [Fact]
    public void MergedNoteContainsEveryTextCompletelyWithItsOrigin()
    {
        var sources = new[]
        {
            Source("c:1", "Erster Text\nmit zwei Zeilen."),
            Source("n:a.md", "## Eigene Überschrift\n\nZweiter Text, lang und vollständig.", SemanticSource.Note),
            Source("c:2", "Erster  Text mit zwei Zeilen."),
        };

        var note = BrainMerge.Build(sources, "Zusammen", Now);

        Assert.StartsWith("# Zusammen", note);
        Assert.Empty(BrainMerge.Missing(note, sources));
        Assert.Contains("Eintrag c:1", note);
        Assert.Contains("Eintrag c:2", note);
        Assert.Contains("Herkunft n:a.md", note);
        // Identical text (apart from whitespace) is written once with both origins.
        Assert.Equal(2, note.Split("## ").Length - 1 - 1);
    }

    [Fact]
    public void MissingFindsTextsThatAreNotInTheSavedNote()
    {
        var sources = new[] { Source("c:1", "Alles da"), Source("c:2", "Fehlt leider") };
        Assert.Equal(["c:2"], BrainMerge.Missing("Hier steht: Alles   da.", sources));
    }

    [Fact]
    public void SharedCategoryIsTheDeepestCommonLevel()
    {
        var paths = new[] { "Technik › Netzwerk › WLAN", "Technik › Netzwerk", "" }.Select(KnowledgeCategories.Parse).ToList();
        Assert.Equal("Technik › Netzwerk", BrainMerge.SharedCategory(paths, default).ToString());
        Assert.Equal("Privat", BrainMerge.SharedCategory([KnowledgeCategories.Parse("Technik"), KnowledgeCategories.Parse("Reisen")],
            KnowledgeCategories.Parse("Privat")).ToString());
    }

    [Fact]
    public void TransferMovesCategoryCollectionsRelationsAndSummaries()
    {
        var s = new AppSettings
        {
            BrainCollections = [new BrainCollection { Id = "m", Name = "Mappe", Keys = ["c:1", "x"] }],
            BrainRelations =
            [
                new BrainRelation { FromKey = "c:1", ToKey = "x", Kind = "gehört zu" },
                new BrainRelation { FromKey = "c:2", ToKey = "x", Kind = "gehört zu" },
                new BrainRelation { FromKey = "c:1", ToKey = "c:2", Kind = "ergänzt" },
            ],
            BrainSummaries = [new BrainSavedSummary { NoteId = "s", SourceKeys = ["c:1", "c:2", "y"] }],
        };

        BrainMerge.Transfer(s, ["c:1", "c:2"], "n:neu.md", KnowledgeCategories.Parse("Technik › Netzwerk"));

        Assert.Equal("Technik › Netzwerk", KnowledgeCategories.Of(s.BrainAnnotations["n:neu.md"]).ToString());
        Assert.Contains("n:neu.md", s.BrainCollections[0].Keys);
        var relation = Assert.Single(s.BrainRelations);
        Assert.Equal(("n:neu.md", "x"), (relation.FromKey, relation.ToKey));
        Assert.Equal(["n:neu.md", "y"], s.BrainSummaries[0].SourceKeys);
    }
}

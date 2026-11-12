using System.IO;
using Merkautomat.Services;

namespace Merkautomat;

/// <summary>
/// Smart parts of "Mein Wissen": E5 category proposals after every daily run (cheap, CPU) and, as a step of the
/// background work, the language model for titles, unclear categories, digest summaries and the weekly review.
/// Nothing here assigns a category or changes a source; proposals wait for the user.
/// </summary>
public partial class App
{
    private const int MaxSuggestionsPerRun = 400;
    private const int MaxModelStepsPerJob = 40;

    /// <summary>E5 proposals for entries without a category; needs the search model and at least two categories.</summary>
    private async Task SuggestMyKnowledgeCategoriesAsync(CancellationToken ct)
    {
        if (!LocalAi.E5Ready || _myKnowledgeBroken) return;
        var settings = CurrentSettings;
        if (KnowledgeCategories.Mains(settings).Count < 2) return;
        var categories = KnowledgeCategories.Paths(settings);
        var assignments = KnowledgeCategories.Assignments(settings.BrainAnnotations);
        var context = MyKnowledgeContext();
        List<MyKnowledgeEntry> todo;
        lock (_myKnowledgeGate)
        {
            MyKnowledgeAi.ResetSuggestionsFor(_myKnowledge, categories, context);
            todo = _myKnowledge.Entries.Where(e => MyKnowledgeAi.NeedsSuggestion(e, context))
                .OrderByDescending(e => e.Weight).Take(MaxSuggestionsPerRun).Select(e => e.Clone()).ToList();
        }
        if (todo.Count == 0) return;

        var results = new Dictionary<string, LocalAiSuggestion?>(StringComparer.Ordinal);
        foreach (var entry in todo)
        {
            ct.ThrowIfCancellationRequested();
            try { results[entry.Key] = await LocalAi.SuggestWithE5Async([MyKnowledgeAi.SourceOf(entry)], categories, ct, assignments); }
            // Text E5 cannot use counts as looked at; it is not offered again.
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException) { results[entry.Key] = null; }
            // The daily run is unattended: a failing E5 run is logged and retried tomorrow, never shown as an error.
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException or Microsoft.ML.OnnxRuntime.OnnxRuntimeException)
            {
                LogError("Mein Wissen: Kategorien vorschlagen", ex);
                break;
            }
        }
        try
        {
            lock (_myKnowledgeGate)
            {
                foreach (var e in _myKnowledge.Entries)
                {
                    if (!results.TryGetValue(e.Key, out var suggestion)) continue;
                    if (suggestion == null) e.SuggestionChecked = true;
                    else MyKnowledgeAi.ApplyE5(e, suggestion);
                }
                MyKnowledge.Save(MyKnowledgeFile, _myKnowledge);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            LogError("Mein Wissen: Vorschläge speichern", ex);
        }
        MyKnowledgeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// One background job for "Mein Wissen" with the language model, step by step and giving way like the category
    /// analysis: titles and unclear categories first (heaviest entries first), then digest summaries, then one sentence
    /// per weekly review group. Returns true when work is left for the next job.
    /// </summary>
    private async Task<bool> RunMyKnowledgeModelStepsAsync(CancellationToken ct)
    {
        if (!LocalAi.Model.IsInstalled || _myKnowledgeBroken || !CurrentSettings.MyKnowledgeEnabled) return false;
        var done = 0;
        while (done < MaxModelStepsPerJob)
        {
            if (_backgroundStopAfterStep || _backgroundDecision?.Allowed != true) return true;
            ct.ThrowIfCancellationRequested();
            var context = MyKnowledgeContext();
            MyKnowledgeEntry? entry, digest;
            MyKnowledgeReviewGroup? group;
            lock (_myKnowledgeGate)
            {
                entry = _myKnowledge.Entries.Where(e => MyKnowledgeAi.NeedsModel(e, context)).OrderByDescending(e => e.Weight).Select(e => e.Clone()).FirstOrDefault();
                digest = _myKnowledge.Entries.Where(e => e.Digest && e.Statements.Count >= 2 && e.AiSummaryFor != MyKnowledgeAi.Fingerprint(e.Statements))
                    .Select(e => e.Clone()).FirstOrDefault();
                group = MyKnowledgeAi.Review(_myKnowledge.Entries, context, DateTime.Now)
                    .FirstOrDefault(g => g.Entries.Count >= 2 && !_myKnowledge.ReviewSentences.ContainsKey(g.Signature));
            }
            if (entry != null) await DescribeEntryAsync(entry, ct);
            else if (digest != null) await SummarizeDigestAsync(digest, ct);
            else if (group != null) await DescribeReviewGroupAsync(group, ct);
            else return false;
            done++;
            MyKnowledgeChanged?.Invoke(this, EventArgs.Empty);
        }
        return true;
    }

    private async Task DescribeEntryAsync(MyKnowledgeEntry entry, CancellationToken ct)
    {
        SetActivity($"{AppNames.MyKnowledge}: Titel und Kategorien vorschlagen");
        var settings = CurrentSettings;
        LocalAiSuggestion? suggestion = null;
        try
        {
            suggestion = await LocalAi.SuggestWithLanguageModelAsync([MyKnowledgeAi.SourceOf(entry)], KnowledgeCategories.Paths(settings), ct,
                KnowledgeCategories.Assignments(settings.BrainAnnotations), e5First: false, background: true);
        }
        // An unusable answer for this text is not retried; the entry simply keeps its title.
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException) { }
        ChangeModelResult("Mein Wissen: Titel speichern", state =>
        {
            if (state.Entries.FirstOrDefault(e => e.Key == entry.Key) is not { } live) return;
            if (suggestion == null) live.ModelChecked = true;
            else MyKnowledgeAi.ApplyModel(live, suggestion, LocalAi.Model.Method);
        });
    }

    private async Task SummarizeDigestAsync(MyKnowledgeEntry digest, CancellationToken ct)
    {
        SetActivity($"{AppNames.MyKnowledge}: Zusammenfassungen formulieren");
        var fingerprint = MyKnowledgeAi.Fingerprint(digest.Statements);
        List<MyKnowledgeSummarySentence> sentences;
        try
        {
            sentences = await LocalAi.SummarizeAsync(AiPrompts.Digest, MyKnowledgeAi.DigestSources(digest), MyKnowledgeAi.MaxSummarySentences,
                "Fasse diese Kernaussagen aus einem Monat lesbar zusammen.", background: true, ct);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException) { sentences = []; }
        ChangeModelResult("Mein Wissen: Zusammenfassung speichern", state =>
        {
            // The digest may have changed in the meantime; then the next job writes a summary for the new statements.
            if (state.Entries.FirstOrDefault(e => e.Key == digest.Key) is not { } live || MyKnowledgeAi.Fingerprint(live.Statements) != fingerprint) return;
            live.AiSummary = sentences;
            live.AiSummaryFor = fingerprint;
            MyKnowledge.Render(live);
        });
    }

    private async Task DescribeReviewGroupAsync(MyKnowledgeReviewGroup group, CancellationToken ct)
    {
        SetActivity($"{AppNames.MyKnowledge}: Wochenrückblick formulieren");
        MyKnowledgeSummarySentence sentence;
        try
        {
            var sentences = await LocalAi.SummarizeAsync(AiPrompts.Review, MyKnowledgeAi.ReviewSources(group), 1,
                "Beschreibe in einem Satz, worum es in diesen neuen Einträgen geht.", background: true, ct);
            sentence = sentences.FirstOrDefault() ?? new MyKnowledgeSummarySentence("", []);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException) { sentence = new MyKnowledgeSummarySentence("", []); }
        var context = MyKnowledgeContext();
        ChangeModelResult("Mein Wissen: Wochenrückblick speichern", state =>
        {
            state.ReviewSentences[group.Signature] = sentence;
            // Only the groups of the current week are kept.
            var current = MyKnowledgeAi.Review(state.Entries, context, DateTime.Now).Select(g => g.Signature).ToHashSet(StringComparer.Ordinal);
            foreach (var old in state.ReviewSentences.Keys.Where(k => !current.Contains(k)).ToList()) state.ReviewSentences.Remove(old);
        });
    }

    private void ChangeModelResult(string attempted, Action<MyKnowledgeState> change)
    {
        try
        {
            lock (_myKnowledgeGate)
            {
                change(_myKnowledge);
                MyKnowledge.Save(MyKnowledgeFile, _myKnowledge);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            LogError(attempted, ex);
        }
    }

    /// <summary>Weekly review groups with the sentence of the language model when one is ready.</summary>
    internal List<(MyKnowledgeReviewGroup Group, MyKnowledgeSummarySentence? Sentence)> MyKnowledgeReview()
    {
        var context = MyKnowledgeContext();
        lock (_myKnowledgeGate)
            return MyKnowledgeAi.Review(_myKnowledge.Entries.Select(e => e.Clone()).ToList(), context, DateTime.Now)
                .Select(g => (g, _myKnowledge.ReviewSentences.TryGetValue(g.Signature, out var s) && s.Text.Length > 0 ? s : null))
                .ToList();
    }

    /// <summary>
    /// Answers a question from "Mein Wissen": meaning hits from the index plus words in the stored copies, ranked with
    /// the weight; the language model answers only from these sources and cites them.
    /// </summary>
    internal async Task<(KnowledgeAnswerResult Answer, List<MyKnowledgeEntry> Sources)> AskMyKnowledgeAsync(string question, CancellationToken ct)
    {
        var entries = MyKnowledgeSnapshot();
        var semantic = new Dictionary<string, double>(StringComparer.Ordinal);
        if (EmbeddingModel.IsInstalled(EmbeddingModel.ResolveModelsDir(AppContext.BaseDirectory, AppPaths.ModelsDir)) && Semantic.Count > 0)
        {
            var keys = entries.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
            var hits = await Task.Run(() => Semantic.Search(question, null, 200), ct);
            var best = hits.Count == 0 ? 1 : hits.Max(h => h.Score);
            foreach (var hit in hits.Where(h => keys.Contains(h.Chunk.ItemKey)))
                semantic[hit.Chunk.ItemKey] = Math.Max(semantic.GetValueOrDefault(hit.Chunk.ItemKey), best > 0 ? hit.Score / best : 0);
        }
        var ranked = MyKnowledgeAi.RankForQuestion(entries, question, semantic);
        if (ranked.Count == 0) return (new KnowledgeAnswerResult("In Mein Wissen gibt es dazu keine passenden Einträge.", false, []), ranked);
        var answer = await LocalAi.AnswerAsync(question, MyKnowledgeAi.AnswerSources(ranked), ct);
        return (answer, ranked);
    }
}

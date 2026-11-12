using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Merkautomat.Services;

/// <summary>A group of the weekly review: entries new in Mein Wissen that share a main category.</summary>
public sealed record MyKnowledgeReviewGroup(string Name, IReadOnlyList<MyKnowledgeEntry> Entries, string Signature);

/// <summary>
/// The smart parts of "Mein Wissen", all local and none of them saving anything on their own: closeness to the core
/// knowledge (E5 vectors), category and title proposals, summaries by the language model that must cite their
/// sources, the weekly review and the selection of sources for questions.
/// </summary>
public static partial class MyKnowledgeAi
{
    /// <summary>Weight factor at full closeness to the core knowledge (1 + 0.3).</summary>
    public const double AffinityBoost = 0.3;
    /// <summary>E5 similarities are high in general; closeness counts from here and is full at <see cref="AffinityHigh"/>.</summary>
    public const float AffinityLow = 0.85f;
    public const float AffinityHigh = 0.95f;
    public const int ReviewDays = 7;
    public const int MaxReviewGroups = 6;
    public const int MaxSummarySentences = 5;
    public const int MaxSentenceChars = 300;
    public const int MaxSourceChars = 600;

    // ---- Closeness to the core knowledge -----------------------------------------------------------------------

    /// <summary>
    /// For every target: how close it is to the nearest core item (mean vector per item, cosine), mapped from
    /// <see cref="AffinityLow"/>..<see cref="AffinityHigh"/> to 0..1. Items without vectors are left out.
    /// </summary>
    public static Dictionary<string, double> Affinity(IReadOnlyList<IndexedChunk> snapshot, IReadOnlySet<string> core, IReadOnlySet<string> targets)
    {
        var vectors = ItemVectors(snapshot, k => core.Contains(k) || targets.Contains(k));
        var coreVectors = core.Where(vectors.ContainsKey).Select(k => (Key: k, Vector: vectors[k])).ToList();
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        if (coreVectors.Count == 0) return result;
        foreach (var key in targets)
        {
            if (core.Contains(key) || !vectors.TryGetValue(key, out var v)) continue;
            var best = coreVectors.Where(c => c.Key != key).Select(c => Dot(v, c.Vector)).DefaultIfEmpty(0).Max();
            result[key] = Math.Clamp((best - AffinityLow) / (AffinityHigh - AffinityLow), 0, 1);
        }
        return result;
    }

    private static Dictionary<string, float[]> ItemVectors(IReadOnlyList<IndexedChunk> snapshot, Func<string, bool> wanted)
    {
        var result = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (var group in snapshot.Where(c => wanted(c.Chunk.ItemKey)).GroupBy(c => c.Chunk.ItemKey))
        {
            var dims = group.First().Vector.Length;
            var mean = new float[dims];
            foreach (var chunk in group)
                for (var i = 0; i < dims && i < chunk.Vector.Length; i++) mean[i] += chunk.Vector[i];
            var norm = (float)Math.Sqrt(mean.Sum(x => (double)x * x));
            if (norm <= 0) continue;
            for (var i = 0; i < dims; i++) mean[i] /= norm;
            result[group.Key] = mean;
        }
        return result;
    }

    private static float Dot(float[] a, float[] b)
    {
        var sum = 0f;
        for (var i = 0; i < a.Length && i < b.Length; i++) sum += a[i] * b[i];
        return sum;
    }

    // ---- Category and title proposals ----------------------------------------------------------------------

    /// <summary>E5 has not looked at it yet and the user has not chosen a category.</summary>
    public static bool NeedsSuggestion(MyKnowledgeEntry e, MyKnowledgeContext context)
        => !e.Digest && !e.SuggestionChecked && !context.HasCategory(e.Key);

    /// <summary>The language model can still add something: a title for copied or recorded text, or a category E5 was not sure about.</summary>
    public static bool NeedsModel(MyKnowledgeEntry e, MyKnowledgeContext context)
        => !e.Digest && !e.ModelChecked
           && ((e.Source is SemanticSource.Clipboard or SemanticSource.Recording && e.AiTitle.Length == 0)
               || (!context.HasCategory(e.Key) && !e.SuggestionSure));

    /// <summary>A proposal that is still open: the user has not chosen a category since.</summary>
    public static bool HasOpenSuggestion(MyKnowledgeEntry e, MyKnowledgeContext context)
        => e.SuggestedCategory.Length > 0 && !context.HasCategory(e.Key);

    /// <summary>E5 proposals that were clear with the user's own examples.</summary>
    public static List<MyKnowledgeEntry> SureSuggestions(IEnumerable<MyKnowledgeEntry> entries, MyKnowledgeContext context)
        => entries.Where(e => e.SuggestionSure && HasOpenSuggestion(e, context)).ToList();

    /// <summary>
    /// After the category list changed (new, renamed or deleted categories) open proposals may point to categories
    /// that no longer exist and older entries never saw the new ones: they are made again. Titles stay.
    /// Returns true when something was reset.
    /// </summary>
    public static bool ResetSuggestionsFor(MyKnowledgeState state, IReadOnlyList<string> categories, MyKnowledgeContext context)
    {
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n", categories.Order(StringComparer.OrdinalIgnoreCase)).ToLowerInvariant())))[..16];
        if (state.SuggestionTaxonomy == fingerprint) return false;
        var first = state.SuggestionTaxonomy.Length == 0;
        state.SuggestionTaxonomy = fingerprint;
        // The very first run has nothing to reset.
        if (first) return false;
        foreach (var e in state.Entries.Where(e => !e.Digest && !context.HasCategory(e.Key)))
        {
            e.SuggestionChecked = false;
            e.SuggestedCategory = "";
            e.SuggestionSure = false;
            e.SuggestionMethod = "";
            e.SuggestionReason = "";
        }
        return true;
    }

    public static void ApplyE5(MyKnowledgeEntry e, LocalAiSuggestion suggestion)
    {
        e.SuggestionChecked = true;
        if (suggestion.Category.Length == 0) return;
        e.SuggestedCategory = suggestion.Category;
        e.SuggestionSure = CategorizationCascade.Settles(suggestion);
        e.SuggestionMethod = suggestion.Method.Length > 0 ? suggestion.Method : ClassifierMethods.Rocchio;
        e.SuggestionReason = suggestion.Reason;
    }

    /// <summary>
    /// Result of the language model: the title for copied and recorded text, the category only where E5 had no clear
    /// answer and the model itself is not unsure. A model proposal is never "sure".
    /// </summary>
    public static void ApplyModel(MyKnowledgeEntry e, LocalAiSuggestion suggestion, string method)
    {
        e.ModelChecked = true;
        var title = Regex.Replace(suggestion.Title ?? "", @"\s+", " ").Trim();
        if (e.Source is SemanticSource.Clipboard or SemanticSource.Recording && title.Length is > 0 and <= 120) e.AiTitle = title;
        if (e.SuggestionSure || suggestion.Uncertain || suggestion.Category.Length == 0) return;
        e.SuggestedCategory = suggestion.Category;
        e.SuggestionMethod = method;
        e.SuggestionReason = suggestion.Reason;
    }

    public static BrainSource SourceOf(MyKnowledgeEntry e) => new(e.Key, e.Title, e.Text, e.Origin, e.Date);

    /// <summary>Who made the proposal, for the user: the language model by name, every other method is E5.</summary>
    public static string SuggestionSource(MyKnowledgeEntry e) => e.SuggestionMethod switch
    {
        "" => "",
        ClassifierMethods.Granite => "Granite",
        ClassifierMethods.Qwen => "Qwen",
        _ => "E5",
    };

    /// <summary>The open proposal as it was made, so taking it over counts in the practice values of the evaluation.</summary>
    public static LocalAiSuggestion SuggestionOf(MyKnowledgeEntry e)
        => new("", e.SuggestedCategory, e.SuggestionReason, false, Method: e.SuggestionMethod) { Learned = e.SuggestionSure };

    // ---- Summaries by the language model -------------------------------------------------------------------

    /// <summary>Identifies the statements a digest summary was written for; a changed digest needs a new one.</summary>
    public static string Fingerprint(IReadOnlyList<MyKnowledgeStatement> statements)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", statements.Select(s => s.Text)))))[..16];

    /// <summary>Statements of a digest as numbered sources, in the order of its "Kernaussagen".</summary>
    public static List<string> DigestSources(MyKnowledgeEntry digest) => digest.Statements.Select(s => s.Text).ToList();

    /// <param name="prompt">Instruction from <see cref="AiPrompts"/> (digest or review); the number of sentences is fixed by the schema.</param>
    public static string BuildSummaryRequest(IReadOnlyList<string> sources, int maxSentences, string task, string prompt)
    {
        var input = JsonSerializer.Serialize(new
        {
            task,
            sources = sources.Select((s, i) => new { n = i + 1, text = Limit(Clean(s), MaxSourceChars) }),
        });
        return JsonSerializer.Serialize(new
        {
            messages = new[]
            {
                new { role = "system", content = prompt },
                new { role = "user", content = input }
            },
            temperature = 0.2, top_p = 0.9, seed = 42, max_tokens = 700, stream = false,
            chat_template_kwargs = new { enable_thinking = false },
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "summary", strict = true,
                    schema = new
                    {
                        type = "object",
                        properties = new
                        {
                            sentences = new
                            {
                                type = "array", maxItems = maxSentences,
                                items = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        text = new { type = "string", maxLength = MaxSentenceChars },
                                        sources = new { type = "array", items = new { type = "integer" }, maxItems = 8 },
                                    },
                                    required = new[] { "text", "sources" }, additionalProperties = false,
                                },
                            },
                        },
                        required = new[] { "sentences" }, additionalProperties = false,
                    },
                },
            },
        });
    }

    /// <summary>
    /// Keeps only sentences with at least one valid source number; numbers outside the given sources and citation
    /// marks inside the text are removed. An unusable answer throws <see cref="InvalidDataException"/>.
    /// </summary>
    public static List<MyKnowledgeSummarySentence> ParseSummary(string response, int sourceCount)
    {
        if (response.Length > 65536) throw new InvalidDataException("Die KI-Antwort ist zu lang.");
        try
        {
            using var envelope = JsonDocument.Parse(response);
            var choice = envelope.RootElement.GetProperty("choices")[0];
            if (choice.GetProperty("finish_reason").GetString() != "stop") throw new InvalidDataException("Die KI-Antwort ist unvollständig.");
            using var json = JsonDocument.Parse(choice.GetProperty("message").GetProperty("content").GetString()!);
            var result = new List<MyKnowledgeSummarySentence>();
            foreach (var item in json.RootElement.GetProperty("sentences").EnumerateArray().Take(MaxSummarySentences))
            {
                var text = Clean(Citation().Replace(item.GetProperty("text").GetString() ?? "", ""));
                var sources = item.GetProperty("sources").EnumerateArray()
                    .Where(n => n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out _)).Select(n => n.GetInt32())
                    .Where(n => n >= 1 && n <= sourceCount).Distinct().Order().ToList();
                if (text.Length is 0 or > MaxSentenceChars || sources.Count == 0) continue;
                result.Add(new MyKnowledgeSummarySentence(text, sources));
            }
            return result;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        { throw new InvalidDataException("Das Sprachmodell hat keine gültige Zusammenfassung geliefert.", ex); }
    }

    // ---- Weekly review --------------------------------------------------------------------------------------

    /// <summary>Entries that came into Mein Wissen in the last <see cref="ReviewDays"/> days, grouped by main category, largest first.</summary>
    public static List<MyKnowledgeReviewGroup> Review(IEnumerable<MyKnowledgeEntry> entries, MyKnowledgeContext context, DateTime now)
        => entries.Where(e => !e.Digest && e.Added >= now.AddDays(-ReviewDays))
            .GroupBy(e => context.MainOf(e) is { Length: > 0 } main ? main : "Ohne Kategorie", StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var members = g.OrderByDescending(e => e.Weight).ThenByDescending(e => e.Date).ToList();
                var signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                    g.Key + "\n" + string.Join("\n", members.Select(e => e.Key).Order(StringComparer.Ordinal)))))[..16];
                return new MyKnowledgeReviewGroup(g.Key, members, signature);
            })
            .OrderByDescending(g => g.Entries.Count).ThenByDescending(g => g.Entries.Sum(e => e.Weight))
            .Take(MaxReviewGroups).ToList();

    /// <summary>The heaviest entries of a review group as short numbered sources for one summary sentence.</summary>
    public static List<string> ReviewSources(MyKnowledgeReviewGroup group)
        => group.Entries.Take(8).Select(e => $"{DisplayTitle(e)}: {Limit(Clean(e.Text), 300)}").ToList();

    public static string DisplayTitle(MyKnowledgeEntry e) => e.AiTitle.Length > 0 ? e.AiTitle : e.Title;

    // ---- Questions ------------------------------------------------------------------------------------------

    /// <summary>
    /// Sources for a question: meaning hits from the index (only keys in Mein Wissen) and word hits in the stored copies,
    /// including digests and copies whose source is gone. Relevance first, weight decides between similar matches.
    /// </summary>
    public static List<MyKnowledgeEntry> RankForQuestion(IReadOnlyList<MyKnowledgeEntry> entries, string question,
        IReadOnlyDictionary<string, double> semantic, int max = KnowledgeAnswer.MaxSources)
    {
        var terms = SemanticRanking.Terms(question).Where(t => t.Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return entries.Select(e =>
            {
                var words = terms.Count == 0 ? 0
                    : (double)terms.Count(t => e.Title.Contains(t, StringComparison.OrdinalIgnoreCase) || e.Text.Contains(t, StringComparison.OrdinalIgnoreCase)) / terms.Count;
                var relevance = semantic.GetValueOrDefault(e.Key) + 0.5 * words;
                return (Entry: e, Score: relevance * (0.6 + 0.4 * e.Weight), Relevance: relevance);
            })
            .Where(x => x.Relevance > 0.25)
            .OrderByDescending(x => x.Score).ThenByDescending(x => x.Entry.Weight)
            .Take(max).Select(x => x.Entry).ToList();
    }

    /// <summary>Numbered answer sources built from the stored copies, so digests and vanished sources can be quoted too.</summary>
    public static List<AnswerSource> AnswerSources(IReadOnlyList<MyKnowledgeEntry> ranked)
        => ranked.Select((e, i) => new AnswerSource(i + 1,
                new SemanticChunk(e.Key + ":0", e.Key, e.Source, DisplayTitle(e), e.Date, e.Text),
                Limit(Clean(e.Text), KnowledgeAnswer.MaxTextPerSource)))
            .ToList();

    private static string Clean(string value) => Whitespace().Replace(value, " ").Trim();
    private static string Limit(string text, int count) => text.Length <= count ? text : text[..count] + "…";

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\s*\[\d+(?:\s*,\s*\d+)*\]")]
    private static partial Regex Citation();
}

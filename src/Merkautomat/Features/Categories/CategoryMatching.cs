using Merkautomat.Models;

namespace Merkautomat.Services;

public sealed record CategoryMatch(string Key, double Score, double Margin);

/// <param name="Message">Why nothing could be ranked; empty when <see cref="Matches"/> is meaningful.</param>
public sealed record CategoryMatchResult(IReadOnlyList<CategoryMatch> Matches, string Message, int Examples, int Competitors);

/// <summary>
/// The reverse direction of categorization: starting from one category, the uncategorized items that are closest to it.
/// Centered Rocchio prototypes from the mean index vectors of the saved examples (no names, so no new embedding is
/// needed); an item is a candidate only when the chosen category beats every other category for it. This is a ranking
/// for review, not an automatic assignment: nothing is saved without confirmation.
/// </summary>
public static class CategoryMatching
{
    public const int MaxExamplesPerCategory = 32;
    public const int MaxMatches = 100;
    /// <summary>Raw cosine to the target prototype; below this the item is unrelated even if it wins the comparison.</summary>
    public const double MinimumSimilarity = SemanticRanking.MinSemanticSimilarity;

    /// <param name="target">Main category, or a subcategory path; subcategories then compete as full paths.</param>
    public static CategoryMatchResult Rank(IReadOnlyList<IndexedChunk> chunks, IReadOnlyDictionary<string, BrainAnnotation>? annotations,
        CategoryPath target, int limit = MaxMatches)
    {
        if (target.IsEmpty) return new([], "Bitte eine Kategorie wählen.", 0, 0);
        string Label(CategoryPath p) => KnowledgeCategories.Build(p.Levels.Take(target.Depth)).ToString();
        var items = chunks.Select(c => c.Chunk).DistinctBy(c => c.ItemKey)
            .Select(c => (c.ItemKey, c.Date, Path: KnowledgeCategories.Effective(annotations, c.ItemKey))).ToList();
        var vectors = SemanticRanking.ItemVectors(chunks, items.Select(i => i.ItemKey).ToHashSet(StringComparer.Ordinal));

        var groups = items.Where(i => !i.Path.IsEmpty)
            .GroupBy(i => Label(i.Path), StringComparer.OrdinalIgnoreCase)
            .Select(g => (Label: g.Key, Vectors: g.OrderByDescending(i => i.Date).Take(MaxExamplesPerCategory)
                .Select(i => vectors[i.ItemKey]).ToArray()))
            .Where(g => g.Vectors.Length >= RocchioModel.MinimumExamples)
            .OrderBy(g => g.Label, StringComparer.OrdinalIgnoreCase).ToList();
        var targetLabel = Label(target);
        var targetIndex = groups.FindIndex(g => string.Equals(g.Label, targetLabel, StringComparison.OrdinalIgnoreCase));
        if (targetIndex < 0)
            return new([], $"„{targetLabel}“ hat noch keine zwei Einträge im Suchindex. Bitte zuerst einige Einträge manuell zuordnen.", 0, groups.Count);
        // Centering two prototypes mirrors them, so any item would look decisive.
        if (groups.Count < RocchioModel.MinimumLearnedCategories)
            return new([], "Für den Vergleich braucht es mindestens drei Kategorien mit je zwei zugeordneten Einträgen.",
                groups[targetIndex].Vectors.Length, groups.Count);

        var labels = groups.Select(g => g.Label).ToList();
        var examples = groups.Select(g => g.Vectors).ToArray();
        var means = examples.Select(e => RocchioModel.Mean(e)).ToArray();
        var model = RocchioModel.Build(labels, means, examples, multiPrototype: false, includeNames: false);
        var targetMean = means[targetIndex];
        var matches = new List<CategoryMatch>();
        foreach (var item in items.Where(i => i.Path.IsEmpty))
        {
            var v = vectors[item.ItemKey];
            var decision = model.Decide(v);
            if (decision.Category != targetIndex || decision.Candidates.Count < 2) continue;
            var raw = RocchioModel.Cosine(v, targetMean);
            if (raw < MinimumSimilarity) continue;
            matches.Add(new CategoryMatch(item.ItemKey, raw, decision.Candidates[0].Score - decision.Candidates[1].Score));
        }
        var ranked = matches.OrderByDescending(m => m.Margin).ThenByDescending(m => m.Score).Take(limit).ToList();
        return new(ranked, ranked.Count == 0 ? $"Kein Eintrag ohne Kategorie liegt näher an „{targetLabel}“ als an einer anderen Kategorie." : "",
            groups[targetIndex].Vectors.Length, groups.Count);
    }
}

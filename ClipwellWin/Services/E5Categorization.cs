using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ClipwellWin.Services;

/// <summary>
/// Suggests one of the existing categories with E5 query vectors and a classifier learned from saved manual
/// assignments. "Automatisch" uses the winner of <see cref="ClassifierBenchmark"/> on the user's own assignments,
/// otherwise the method chosen in the report window.
/// </summary>
public sealed class E5Categorization(
    Func<IReadOnlyList<string>, CancellationToken, Task<float[][]>> embed,
    Func<bool> isReady,
    Func<IReadOnlyCollection<string>, IReadOnlyDictionary<string, (string Text, DateTime Date)>>? exampleTexts = null,
    Func<string>? methodSetting = null,
    Func<IReadOnlyDictionary<string, string>>? descriptions = null)
{
    public const string Id = "e5-categories";
    public const string DisplayName = "E5 · bestehende Kategorien";
    // Heuristics for suggestions, not calibrated probabilities. Require agreement across excerpts.
    public const double MinimumSimilarity = RocchioModel.MinimumSimilarity;
    public const int MinimumExamples = RocchioModel.MinimumExamples;
    public const int MaxExamplesPerCategory = 16;
    public const int MaxExamples = 400;
    // Centering two prototypes mirrors them, so any text would look decisive.
    public const int MinimumLearnedCategories = RocchioModel.MinimumLearnedCategories;
    /// <summary>Up to this many examples the automatic method choice is computed before answering, beyond in the background.</summary>
    public const int InlineBenchmarkExamples = 150;
    private const int MaxCachedVectors = 4096;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, float[]> _vectors = [];
    private readonly Dictionary<string, Task<BenchmarkReport>> _benchmarks = [];
    public bool Ready => isReady();

    /// <summary>
    /// Main category first (method from the evaluation), then, local to that main category, its subcategory.
    /// Categories and assignments may be paths "Haupt › Unter".
    /// </summary>
    /// <param name="assignments">Saved manual assignments, item key to category path. Selected sources are left out.</param>
    public async Task<LocalAiSuggestion> SuggestAsync(IEnumerable<BrainSource> sources, IEnumerable<string> categories,
        CancellationToken ct = default, IReadOnlyDictionary<string, string>? assignments = null)
    {
        ct.ThrowIfCancellationRequested();
        var sourceList = sources.ToList();
        var paths = categories.ToList();
        var mainAssignments = assignments?.ToDictionary(a => a.Key, a => KnowledgeCategories.MainOf(a.Value), StringComparer.Ordinal);
        var main = await SuggestMainAsync(sourceList, paths.Select(KnowledgeCategories.MainOf), ct, mainAssignments).ConfigureAwait(false);
        if (main.Uncertain) return main;
        var sub = await SuggestSubcategoryAsync(sourceList, main.Category, paths, ct, assignments).ConfigureAwait(false);
        if (sub == null) return main;
        return main with
        {
            Category = sub.Uncertain ? main.Category : sub.Category,
            Reason = main.Reason + " " + sub.Reason,
            SubCandidates = sub.SubCandidates,
        };
    }

    /// <summary>
    /// Subcategory within a given main category: hierarchical, so only subcategories of that main category and only
    /// examples saved under it compete. Null when the main category has no subcategories.
    /// </summary>
    public async Task<LocalAiSuggestion?> SuggestSubcategoryAsync(IEnumerable<BrainSource> sources, string main,
        IEnumerable<string> categories, CancellationToken ct = default, IReadOnlyDictionary<string, string>? assignments = null)
    {
        var sourceList = sources.ToList();
        var mainName = KnowledgeCategories.MainOf(main);
        if (mainName.Length == 0) return null;
        bool Under(CategoryPath p) => p.HasSub && string.Equals(p.Main, mainName, StringComparison.OrdinalIgnoreCase);
        var subs = Labels(categories.Select(KnowledgeCategories.Parse).Concat((assignments ?? new Dictionary<string, string>()).Values.Select(KnowledgeCategories.Parse))
            .Where(Under).Select(p => p.Sub));
        if (subs.Length == 0) return null;
        var subCandidates = subs.Select(s => new CategoryCandidate(s, 0)).ToList();
        if (subs.Length == 1)
            return Unclear($"Unterkategorie: unter „{mainName}“ gibt es nur „{subs[0]}“; ob sie passt, bitte selbst entscheiden.") with { SubCandidates = subCandidates };
        var excerpts = sourceList.Select(s => Excerpt(s.Text)).Where(t => t.Length >= 20)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(LocalAiCategorization.MaxSources).ToArray();
        if (excerpts.Length == 0) return null;
        var subAssignments = (assignments ?? new Dictionary<string, string>())
            .Select(a => (a.Key, Path: KnowledgeCategories.Parse(a.Value))).Where(a => Under(a.Path))
            .ToDictionary(a => a.Key, a => a.Path.Sub, StringComparer.Ordinal);
        var examples = CollectExamples(subs, subAssignments, sourceList.Select(s => s.Key).ToHashSet());
        var names = subs.Select(s => NameText(KnowledgeCategories.Combine(mainName, s))).ToArray();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        Dictionary<string, float[]> vectors;
        try { vectors = await EmbedAsync(names.Concat(excerpts).Concat(examples.SelectMany(e => e)).ToArray(), ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
        return RankSubcategories(mainName, subs, names.Select(n => vectors[n]).ToArray(), excerpts.Select(e => vectors[e]).ToArray(),
            examples.Select(list => list.Select(e => vectors[e]).ToArray()).ToArray());
    }

    /// <summary>
    /// Three or more subcategories: centered Rocchio (name plus examples) as for main categories. Two: centering would
    /// mirror the prototypes, so the three nearest own examples must agree. Without examples: names only.
    /// </summary>
    public static LocalAiSuggestion RankSubcategories(string main, IReadOnlyList<string> subs, IReadOnlyList<float[]> names,
        IReadOnlyList<float[]> sources, IReadOnlyList<float[][]> examples)
    {
        var learnedCount = examples.Count(e => e.Length >= MinimumExamples);
        var count = examples.Where(e => e.Length >= MinimumExamples).Sum(e => e.Length);
        var twoWay = subs.Count == 2 && learnedCount == 2;
        var learned = subs.Count >= MinimumLearnedCategories && learnedCount > 0;
        string method;
        Func<float[], ClassifierDecision> decide;
        if (twoWay)
        {
            // With only two examples in a subcategory three neighbours could never agree.
            var k = examples.All(e => e.Length >= KnnClassifier.Neighbors) ? KnnClassifier.Neighbors : 2;
            method = $"nächste eigene Beispiele, {k} von {k} einig";
            decide = v =>
            {
                var ranked = examples.SelectMany((list, c) => list.Select(x => (Category: c, Score: RocchioModel.Cosine(x, v))))
                    .OrderByDescending(x => x.Score).ToList();
                var nearest = ranked.Take(k).ToList();
                var winner = nearest[0].Category;
                var confident = nearest.All(x => x.Category == winner) && nearest[0].Score >= RocchioModel.MinimumSimilarity;
                var candidates = ranked.GroupBy(x => x.Category).Select(g => new CategoryCandidate(subs[g.Key], g.Max(x => x.Score)))
                    .OrderByDescending(c => c.Score).ToList();
                return new ClassifierDecision(winner, confident, nearest[0].Score, candidates);
            };
        }
        else
        {
            var model = RocchioModel.Build(subs, names, learned ? examples : subs.Select(_ => Array.Empty<float[]>()).ToArray(), false);
            method = learned ? "Rocchio innerhalb der Hauptkategorie" : "nur Namen der Unterkategorien";
            decide = model.Decide;
        }
        var decisions = sources.Select(decide).ToList();
        var candidates = decisions.SelectMany(d => d.Candidates).GroupBy(c => c.Category)
            .Select(g => new CategoryCandidate(g.Key, g.Sum(c => c.Score) / decisions.Count))
            .OrderByDescending(c => c.Score).Take(3).ToList();
        var basis = (learned || twoWay) ? $"{method}, {count} eigene Beispiele" : method;
        if (decisions.Count == 0 || decisions.Any(d => !d.Confident || d.Category < 0) || decisions.Select(d => d.Category).Distinct().Count() > 1)
            return Unclear($"Unterkategorie: kein eindeutiger Vorschlag unter „{main}“ ({basis}).") with { SubCandidates = candidates };
        var sub = subs[decisions[0].Category];
        return new LocalAiSuggestion("", KnowledgeCategories.Combine(main, sub), $"Unterkategorie: {sub} ({basis}).", false)
            { SubCandidates = candidates };
    }

    private string NameText(string label)
    {
        var description = descriptions?.Invoke().GetValueOrDefault(label);
        return string.IsNullOrWhiteSpace(description) ? "Thema: " + label : $"Thema: {label}. {Excerpt(description)}";
    }

    private async Task<LocalAiSuggestion> SuggestMainAsync(List<BrainSource> sourceList, IEnumerable<string> categories,
        CancellationToken ct, IReadOnlyDictionary<string, string>? assignments)
    {
        var labels = Labels(categories);
        if (labels.Length == 0) return Unclear("Bitte zuerst eine Kategorie manuell zuordnen und speichern. E5 verwendet nur vorhandene Kategorien.");
        if (labels.Length == 1) return Unclear("Es ist erst eine Kategorie vorhanden. Für einen Vergleich bitte mindestens zwei Kategorien manuell zuordnen und speichern.");
        if (labels.Length > 200) return Unclear("Für diesen Vergleich sind höchstens 200 unterschiedliche Kategorien vorgesehen.");
        var excerpts = sourceList.Select(s => Excerpt(s.Text)).Where(t => t.Length >= 20)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(LocalAiCategorization.MaxSources).ToArray();
        if (excerpts.Length == 0) return Unclear("Die Auswahl enthält keinen ausreichend langen Text für den Kategorievergleich.");
        var learning = labels.Length >= MinimumLearnedCategories;
        var examples = learning ? CollectExamples(labels, assignments, sourceList.Select(s => s.Key).ToHashSet()) : Empty(labels);
        var setting = ClassifierMethods.Normalize(methodSetting?.Invoke());
        // Method choice and calibrated thresholds are properties of all saved assignments, not of the current selection.
        var all = learning ? CollectExamples(labels, assignments, []) : Empty(labels);
        var benchmark = all.Any(e => e.Length > 0) && ClassifierBenchmark.CanRun(labels, all.Select(e => new float[e.Length][]).ToArray());
        var names = labels.Select(NameText).ToArray();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var vectors = await EmbedAsync(names.Concat(excerpts).Concat(examples.SelectMany(e => e))
                .Concat(benchmark ? all.SelectMany(e => e) : []).ToArray(), ct).ConfigureAwait(false);
            var nameVectors = names.Select(n => vectors[n]).ToArray();
            var sourceVectors = excerpts.Select(e => vectors[e]).ToArray();
            var exampleVectors = examples.Select(list => list.Select(e => vectors[e]).ToArray()).ToArray();
            var learned = learning && exampleVectors.Any(e => e.Length >= MinimumExamples);
            var auto = setting == ClassifierMethods.Auto;
            var method = auto ? ClassifierMethods.Rocchio : setting;
            var note = auto ? "Standard" : "fest eingestellt";
            BenchmarkReport? report = null;
            if (learned && benchmark)
            {
                var task = Benchmark(labels, nameVectors, all.Select(list => list.Select(e => vectors[e]).ToArray()).ToArray(), all);
                if (!task.IsCompleted && all.Sum(e => e.Length) <= InlineBenchmarkExamples) await task.ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (task.IsCompletedSuccessfully) report = task.Result;
                else if (auto) note = "Auswertung läuft im Hintergrund, bis dahin Rocchio";
            }
            if (report != null && auto) (method, note) = (report.Recommended, "automatisch nach Auswertung gewählt");
            var threshold = report?.Get(method)?.CalibratedThreshold;
            if (threshold is { } t) note += $"; Schwelle aus deiner Auswertung {CategorizationReporting.Number(t)}";
            return Conclude(labels, nameVectors, sourceVectors, exampleVectors, examples, method, note, assignments, threshold: threshold);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Cross-validated comparison of all methods on the saved assignments (report window).</summary>
    public async Task<(BenchmarkReport? Report, string Message)> BenchmarkAsync(IReadOnlyDictionary<string, string> assignments,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        // The evaluation compares main categories.
        assignments = assignments.ToDictionary(a => a.Key, a => KnowledgeCategories.MainOf(a.Value), StringComparer.Ordinal);
        var labels = Labels(assignments.Values);
        if (labels.Length < MinimumLearnedCategories)
            return (null, "Für eine Auswertung sind mindestens drei Kategorien mit gespeicherten Zuordnungen nötig.");
        if (exampleTexts == null) return (null, "Die Beispieltexte sind nicht verfügbar.");
        var pairs = CollectExamplePairs(labels, assignments, []);
        var all = pairs.Select(list => list.Select(p => p.Text).ToArray()).ToArray();
        if (all.Count(e => e.Length >= MinimumExamples) < 2)
            return (null, "Für eine Auswertung brauchen mindestens zwei Kategorien je zwei Beispiele im Suchindex. Ist der Suchindex geladen? Dafür einmal Suche oder Brain öffnen.");
        var names = labels.Select(NameText).ToArray();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        Dictionary<string, float[]> vectors;
        var embedWatch = System.Diagnostics.Stopwatch.StartNew();
        try { vectors = await EmbedAsync(names.Concat(all.SelectMany(e => e)).ToArray(), ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
        var embeddingMs = embedWatch.Elapsed.TotalMilliseconds;
        var nameVectors = names.Select(n => vectors[n]).ToArray();
        var allVectors = all.Select(list => list.Select(e => vectors[e]).ToArray()).ToArray();
        var report = await Task.Run(() => ClassifierBenchmark.Run(labels, nameVectors, allVectors, progress: progress, ct: ct), ct).ConfigureAwait(false);
        // Case indices follow the category-then-example order of the benchmark input.
        var flat = pairs.SelectMany(list => list).ToArray();
        report = report with
        {
            EmbeddingMilliseconds = embeddingMs,
            Suspicious = report.Suspicious?.Select(s => s with { Key = flat[s.Case].Key, Excerpt = Shorten(flat[s.Case].Text) }).ToList(),
        };
        lock (_benchmarks) _benchmarks[BenchmarkKey(labels, all)] = Task.FromResult(report);
        return (report, "");
    }

    private Task<BenchmarkReport> Benchmark(string[] labels, float[][] names, float[][][] examples, string[][] texts)
    {
        var key = BenchmarkKey(labels, texts);
        lock (_benchmarks)
        {
            if (_benchmarks.TryGetValue(key, out var existing) && !existing.IsFaulted) return existing;
            if (_benchmarks.Count > 16) _benchmarks.Clear();
            var task = Task.Run(() => ClassifierBenchmark.Run(labels, names, examples));
            _benchmarks[key] = task;
            return task;
        }
    }

    private static string BenchmarkKey(string[] labels, string[][] texts)
        => Hash(string.Join("\n", labels) + "\u0001" + string.Join("\u0001", texts.Select(list => string.Join("\u0002", list))));

    private async Task<Dictionary<string, float[]>> EmbedAsync(string[] texts, CancellationToken ct)
    {
        var keys = texts.Select(Hash).ToArray();
        var missing = texts.Zip(keys).Where(x => !_vectors.ContainsKey(x.Second)).DistinctBy(x => x.Second).ToArray();
        if (missing.Length > 0)
        {
            if (!Ready) throw new InvalidOperationException("Das E5-Suchmodell fehlt. Bitte in der Kontrollstation herunterladen.");
            var generated = await embed(missing.Select(x => x.First).ToArray(), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (generated.Length != missing.Length) throw new InvalidOperationException("E5 hat unvollständige Vektoren geliefert.");
            if (_vectors.Count + generated.Length > MaxCachedVectors)
            {
                var keep = keys.Distinct().Where(_vectors.ContainsKey).ToDictionary(k => k, k => _vectors[k]);
                _vectors.Clear();
                foreach (var (k, v) in keep) _vectors[k] = v;
            }
            for (var i = 0; i < generated.Length; i++) _vectors[missing[i].Second] = generated[i];
        }
        var result = new Dictionary<string, float[]>(StringComparer.Ordinal);
        for (var i = 0; i < texts.Length; i++) result[texts[i]] = _vectors[keys[i]];
        return result;
    }

    private string[][] CollectExamples(string[] labels, IReadOnlyDictionary<string, string>? assignments, HashSet<string> exclude)
        => CollectExamplePairs(labels, assignments, exclude).Select(list => list.Select(p => p.Text).ToArray()).ToArray();

    private (string Key, string Text)[][] CollectExamplePairs(string[] labels, IReadOnlyDictionary<string, string>? assignments, HashSet<string> exclude)
    {
        var empty = labels.Select(_ => Array.Empty<(string Key, string Text)>()).ToArray();
        if (assignments == null || exampleTexts == null || assignments.Count == 0) return empty;
        var byLabel = labels.Select((l, i) => (l, i)).ToDictionary(x => x.l, x => x.i, StringComparer.OrdinalIgnoreCase);
        var candidates = assignments.Where(a => !exclude.Contains(a.Key) && byLabel.ContainsKey(a.Value.Trim())).ToList();
        if (candidates.Count == 0) return empty;
        var found = exampleTexts(candidates.Select(a => a.Key).ToArray());
        var result = candidates
            .Where(a => found.ContainsKey(a.Key))
            .GroupBy(a => byLabel[a.Value.Trim()])
            .ToDictionary(g => g.Key, g => g
                .OrderByDescending(a => found[a.Key].Date).ThenBy(a => a.Key, StringComparer.Ordinal)
                .Select(a => (a.Key, Text: Excerpt(found[a.Key].Text))).Where(t => t.Text.Length >= 20)
                .DistinctBy(t => t.Text, StringComparer.OrdinalIgnoreCase).Take(MaxExamplesPerCategory).ToArray());
        var total = 0;
        for (var c = 0; c < labels.Length; c++)
        {
            if (!result.TryGetValue(c, out var list) || list.Length < MinimumExamples || total + list.Length > MaxExamples) continue;
            empty[c] = list;
            total += list.Length;
        }
        return empty;
    }

    /// <summary>Single centroid per category (Rocchio with names), for direct use and tests.</summary>
    public static LocalAiSuggestion Rank(IReadOnlyList<string> labels, IReadOnlyList<float[]> categoryVectors,
        IReadOnlyList<float[]> sourceVectors, IReadOnlyList<float[]?>? exampleCentroids = null, int exampleCount = 0,
        string? nameOnlyHint = null)
    {
        if (exampleCentroids != null && exampleCentroids.Count != labels.Count) return Unclear("Keine vergleichbaren Kategorien oder Quellen vorhanden.");
        var examples = labels.Select((_, i) => exampleCentroids?[i] is { } c ? new[] { c, c } : []).ToArray();
        return Conclude(labels, categoryVectors, sourceVectors, examples, null, ClassifierMethods.Rocchio, null, null, nameOnlyHint, exampleCount);
    }

    private static LocalAiSuggestion Conclude(IReadOnlyList<string> labels, IReadOnlyList<float[]> names, IReadOnlyList<float[]> sources,
        float[][][] examples, string[][]? exampleExcerpts, string method, string? note, IReadOnlyDictionary<string, string>? assignments,
        string? nameOnlyHint = null, int? exampleCount = null, double? threshold = null)
    {
        if (labels.Count < 2 || labels.Count != names.Count || sources.Count == 0 || examples.Length != labels.Count)
            return Unclear("Keine vergleichbaren Kategorien oder Quellen vorhanden.");
        var dimension = names[0].Length;
        if (dimension == 0 || names.Concat(sources).Concat(examples.SelectMany(e => e))
                .Any(v => v.Length != dimension || v.Any(f => !float.IsFinite(f)) || v.All(f => f == 0)))
            return Unclear("Die E5-Vektoren sind nicht vergleichbar. Bitte erneut versuchen.");
        var learned = labels.Count >= MinimumLearnedCategories && examples.Any(e => e.Length >= MinimumExamples);
        if (!learned) method = ClassifierMethods.Rocchio;
        var classifier = ClassifierMethods.Train(method, labels, names, examples, learned ? threshold : null);
        var hint = nameOnlyHint ?? (assignments is not { Count: > 0 } ? null
            : labels.Count < MinimumLearnedCategories ? "Gespeicherte Zuordnungen werden erst ab drei Kategorien als Beispiele genutzt."
            : "Noch keine Kategorie hat mindestens zwei gespeicherte Beispiele außerhalb der Auswahl; verglichen wurden nur die Namen.");
        var suffix = !learned && hint != null ? " " + hint : "";
        var methodText = learned ? $" Verfahren: {ClassifierMethods.DisplayName(method)}" + (note != null ? $" ({note})." : ".") : "";
        var decisions = sources.Select(classifier.Decide).ToList();
        var candidates = decisions.SelectMany(d => d.Candidates).GroupBy(c => c.Category)
            .Select(g => new CategoryCandidate(g.Key, g.Sum(c => c.Score) / decisions.Count))
            .OrderByDescending(c => c.Score).Take(3).ToList();
        if (decisions.Any(d => !d.Confident || d.Category < 0))
            return Unclear((learned
                ? "Die vorhandenen Kategorien passen auch mit den gespeicherten Zuordnungen nicht eindeutig genug. Bitte manuell wählen oder passendere Quellen markieren."
                : "Die vorhandenen Kategorien passen nicht eindeutig genug. Bitte manuell wählen oder passendere Quellen markieren.") + methodText + suffix, candidates, method) with { Learned = learned };
        if (decisions.Select(d => d.Category).Distinct().Count() > 1)
            return Unclear("Die ausgewählten Quellenauszüge passen zu unterschiedlichen Kategorien. Bitte die Auswahl eingrenzen." + methodText + suffix, candidates, method) with { Learned = learned };
        var winner = decisions[0].Category;
        if (!learned)
            return new("", labels[winner], $"Ähnlichkeitsvergleich: {sources.Count} Quellenauszüge, {labels.Count} vorhandene Kategorien. Titel bleibt erhalten; Vorschlag bitte prüfen.{suffix}", false, candidates, ClassifierMethods.Names);
        var count = exampleCount ?? examples.Where(e => e.Length >= MinimumExamples).Sum(e => e.Length);
        var reason = new StringBuilder($"Vergleich mit gespeicherten Zuordnungen: {sources.Count} Quellenauszüge, {labels.Count} Kategorien, davon {examples.Count(e => e.Length >= MinimumExamples)} mit {count} eigenen Beispielen.");
        reason.Append(methodText);
        if (classifier is RocchioModel { } rocchio && rocchio.SubPrototypeCounts[winner] > 1)
            reason.Append($" „{labels[winner]}“ ist breit und wird über {rocchio.SubPrototypeCounts[winner]} Schwerpunkte verglichen.");
        if (decisions[0].Probability is { } p)
            reason.Append($" Modellwahrscheinlichkeit {p:P0} (in der Auswertung auf Kalibrierung geprüft).");
        if (exampleExcerpts != null && examples[winner].Length > 0)
        {
            var center = RocchioModel.Mean(sources.Select(s => (float[])s.Clone()).ToArray());
            var nearest = examples[winner].Select((v, i) => (i, RocchioModel.Cosine(v, center))).MaxBy(x => x.Item2).i;
            reason.Append($" Ähnlichstes eigenes Beispiel: „{Shorten(exampleExcerpts[winner][nearest])}“.");
        }
        reason.Append(" Titel bleibt erhalten; Vorschlag bitte prüfen.");
        return new("", labels[winner], reason.ToString(), false, candidates, method) { Learned = true };
    }

    private static string[] Labels(IEnumerable<string> categories) => categories.Select(c => c.Trim()).Where(c => c.Length is > 0 and <= 80)
        .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string[][] Empty(string[] labels) => labels.Select(_ => Array.Empty<string>()).ToArray();

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string Excerpt(string text)
    {
        var t = Regex.Replace(text, @"\s+", " ").Trim();
        return t[..Math.Min(t.Length, LocalAiCategorization.MaxTextPerSource)];
    }

    private static string Shorten(string text) => text.Length <= 70 ? text : text[..67].TrimEnd() + "...";

    private static LocalAiSuggestion Unclear(string reason, IReadOnlyList<CategoryCandidate>? candidates = null, string method = "")
        => new("", "", reason, true, candidates, method);
}

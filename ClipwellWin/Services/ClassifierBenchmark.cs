using System.Diagnostics;

namespace ClipwellWin.Services;

public sealed record CategoryReport(string Category, int Examples, double Precision, double Recall, double F1, string? MostConfusedWith);

public sealed record MethodReport(
    string Method, double MacroF1, double MacroF1Spread, double Coverage, double SelectiveAccuracy, double TopThree,
    double CoverageAtTenPercentError, double? CalibrationError, double MicrosecondsPerDecision,
    IReadOnlyList<CategoryReport> PerCategory, int[][] Confusion,
    double TopOne = 0, double SelectiveLow = 0, double SelectiveHigh = 0, double? UnknownRejection = null,
    double? CalibratedThreshold = null, double CalibratedCoverage = 0, double TrainingMilliseconds = 0);

/// <summary>A saved assignment that the baseline confidently put into another category in every repetition.</summary>
public sealed record SuspiciousAssignment(int Case, string Category, string Suggested, string Key = "", string Excerpt = "");

/// <param name="Confusion">Rows: true category; columns: predicted category plus a last column for "no suggestion".</param>
public sealed record BenchmarkReport(DateTime Date, int Examples, int Categories, int Folds, int Repeats, IReadOnlyList<string> Labels,
    IReadOnlyList<MethodReport> Methods, string Recommended, IReadOnlyList<SuspiciousAssignment>? Suspicious = null, double EmbeddingMilliseconds = 0)
{
    public MethodReport? Get(string method) => Methods.FirstOrDefault(m => m.Method == method);
}

/// <summary>
/// Compares the classifiers on the saved assignments with grouped, stratified k-fold cross-validation: near copies
/// (cosine >= 0.98) stay in the same fold, every repetition reshuffles with a fixed seed. Reports macro-F1 with spread,
/// coverage, selective accuracy, top-3 accuracy, coverage at 10 % error, calibration error (probabilistic methods only)
/// and time per decision.
/// </summary>
public static class ClassifierBenchmark
{
    public const int DefaultFolds = 5;
    public const int DefaultRepeats = 3;
    public const double DuplicateSimilarity = 0.98;
    public const double ErrorLimit = 0.10;
    /// <summary>A more complex method replaces Rocchio only with at least this macro-F1 gain.</summary>
    public const double RequiredGain = 0.02;
    /// <summary>Calibration needs this many examples; below, the fixed thresholds stay.</summary>
    public const int MinimumCalibrationExamples = 40;
    /// <summary>Lower 95 % confidence bound for the share of correct suggestions that the calibrated threshold must reach.</summary>
    public const double TargetPrecision = 0.90;
    public const int MaxUnknownCategories = 10;

    public static bool CanRun(IReadOnlyList<string> labels, IReadOnlyList<float[][]> examples)
        => labels.Count >= RocchioModel.MinimumLearnedCategories && examples.Count(e => e.Length >= RocchioModel.MinimumExamples) >= 2;

    public static BenchmarkReport Run(IReadOnlyList<string> labels, IReadOnlyList<float[]> names, IReadOnlyList<float[][]> examples,
        IReadOnlyList<string>? methods = null, int folds = DefaultFolds, int repeats = DefaultRepeats,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        methods ??= ClassifierMethods.All;
        var cases = examples.SelectMany((list, c) => list.Select((v, i) => (Category: c, Index: i, Vector: v))).ToArray();
        var group = Groups(cases.Select(c => c.Vector).ToArray());
        var groupCount = group.Distinct().Count();
        folds = Math.Clamp(Math.Min(folds, groupCount), 2, DefaultFolds);
        var truth = cases.Select(c => c.Category).ToArray();
        var outcomes = methods.Select(_ => new List<Outcome[]>()).ToArray();
        var steps = repeats * folds;
        var trainingTime = new double[methods.Count];
        for (var r = 0; r < repeats; r++)
        {
            var fold = AssignFolds(cases.Select(c => c.Category).ToArray(), group, folds, seed: 17 + r);
            var run = methods.Select(_ => new Outcome[cases.Length]).ToArray();
            for (var f = 0; f < folds; f++)
            {
                ct.ThrowIfCancellationRequested();
                var training = examples.Select(_ => new List<float[]>()).ToArray();
                for (var n = 0; n < cases.Length; n++) if (fold[n] != f) training[cases[n].Category].Add(cases[n].Vector);
                var trainingArrays = training.Select(t => t.ToArray()).ToArray();
                for (var m = 0; m < methods.Count; m++)
                {
                    var trainWatch = Stopwatch.StartNew();
                    var classifier = ClassifierMethods.Train(methods[m], labels, names, trainingArrays);
                    trainingTime[m] += trainWatch.Elapsed.TotalMilliseconds;
                    for (var n = 0; n < cases.Length; n++)
                    {
                        if (fold[n] != f) continue;
                        var watch = Stopwatch.StartNew();
                        var d = classifier.Decide(cases[n].Vector);
                        watch.Stop();
                        run[m][n] = new Outcome(d.Confident ? d.Category : -1, d.Category, d.Confidence, d.Probability,
                            d.Candidates.Any(c => c.Category == labels[truth[n]]), watch.Elapsed.TotalMicroseconds);
                    }
                }
                progress?.Report((double)(r * folds + f + 1) / steps);
            }
            for (var m = 0; m < methods.Count; m++) outcomes[m].Add(run[m]);
        }
        var unknown = UnknownRejection(labels, names, examples, methods, ct);
        var reports = methods.Select((method, m) => Summarize(method, labels, truth, outcomes[m], unknown?[m], cases.Length)
            with { TrainingMilliseconds = trainingTime[m] / steps }).ToList();
        var baseline = methods.ToList().IndexOf(ClassifierMethods.Rocchio);
        var suspicious = baseline < 0 ? [] : Enumerable.Range(0, cases.Length)
            .Where(n => outcomes[baseline].All(run => run[n].Predicted >= 0 && run[n].Predicted != truth[n]))
            .Select(n => new SuspiciousAssignment(n, labels[truth[n]], labels[outcomes[baseline]
                .GroupBy(run => run[n].Predicted).OrderByDescending(g => g.Count()).First().Key]))
            .ToList();
        return new BenchmarkReport(DateTime.Now, cases.Length, examples.Count(e => e.Length > 0), folds, repeats, labels, reports, Recommend(reports), suspicious);
    }

    /// <summary>The simplest learned method wins unless a more complex one is clearly better without being less precise.</summary>
    public static string Recommend(IReadOnlyList<MethodReport> reports)
    {
        var baseline = reports.FirstOrDefault(r => r.Method == ClassifierMethods.Rocchio);
        if (baseline == null) return reports.FirstOrDefault()?.Method ?? ClassifierMethods.Rocchio;
        var best = baseline;
        foreach (var r in reports.Where(r => r.Method is not ClassifierMethods.Names and not ClassifierMethods.Rocchio))
            if (r.MacroF1 >= best.MacroF1 + RequiredGain && r.SelectiveAccuracy >= baseline.SelectiveAccuracy - RequiredGain) best = r;
        return best.Method;
    }

    private sealed record Outcome(int Predicted, int Top, double Confidence, double? Probability, bool InTopThree, double Microseconds);

    /// <summary>
    /// Open-set check: each of the largest categories is removed completely (label, name and examples); its texts
    /// then play unknown content. Returns per method the share that got no suggestion. Needs four or more categories.
    /// </summary>
    private static double?[]? UnknownRejection(IReadOnlyList<string> labels, IReadOnlyList<float[]> names, IReadOnlyList<float[][]> examples,
        IReadOnlyList<string> methods, CancellationToken ct)
    {
        var learned = Enumerable.Range(0, labels.Count).Where(c => examples[c].Length >= RocchioModel.MinimumExamples).ToList();
        if (labels.Count < 4 || learned.Count < 3) return null;
        var rejected = new int[methods.Count];
        var total = 0;
        foreach (var hidden in learned.OrderByDescending(c => examples[c].Length).Take(MaxUnknownCategories))
        {
            ct.ThrowIfCancellationRequested();
            var keep = Enumerable.Range(0, labels.Count).Where(c => c != hidden).ToArray();
            var keptLabels = keep.Select(c => labels[c]).ToArray();
            var keptNames = keep.Select(c => names[c]).ToArray();
            var keptExamples = keep.Select(c => examples[c]).ToArray();
            total += examples[hidden].Length;
            for (var m = 0; m < methods.Count; m++)
            {
                var classifier = ClassifierMethods.Train(methods[m], keptLabels, keptNames, keptExamples);
                rejected[m] += examples[hidden].Count(v => !classifier.Decide(v).Confident);
            }
        }
        return total == 0 ? null : rejected.Select(r => (double?)r / total).ToArray();
    }

    /// <summary>Wilson score interval (95 %) for k successes out of n.</summary>
    public static (double Low, double High) Wilson(int k, int n, double z = 1.96)
    {
        if (n == 0) return (0, 0);
        var p = (double)k / n;
        var denominator = 1 + z * z / n;
        var center = (p + z * z / (2 * n)) / denominator;
        var half = z * Math.Sqrt(p * (1 - p) / n + z * z / (4.0 * n * n)) / denominator;
        return (Math.Max(0, center - half), Math.Min(1, center + half));
    }

    /// <summary>
    /// Smallest threshold (largest coverage) whose accepted cases reach <see cref="TargetPrecision"/> at the lower
    /// Wilson bound, within method-specific limits. Null when no threshold qualifies or data are too few.
    /// </summary>
    public static (double? Threshold, double Coverage) Calibrate(string method, (double Confidence, bool Correct)[] decisions, int examples)
    {
        if (!ClassifierMethods.Calibratable(method) || examples < MinimumCalibrationExamples || decisions.Length == 0) return (null, 0);
        var (min, max) = method == ClassifierMethods.LogisticRegression ? (0.35, 0.95) : (0.04, 0.25);
        foreach (var t in decisions.Select(d => d.Confidence).Where(c => c >= min && c <= max).Append(min).Distinct().Order())
        {
            var accepted = decisions.Where(d => d.Confidence >= t).ToArray();
            if (accepted.Length < 10) break;
            if (Wilson(accepted.Count(d => d.Correct), accepted.Length).Low >= TargetPrecision)
                return (t, (double)accepted.Length / decisions.Length);
        }
        return (null, 0);
    }

    private static MethodReport Summarize(string method, IReadOnlyList<string> labels, int[] truth, List<Outcome[]> runs, double? unknown, int examples)
    {
        var f1 = runs.Select(run => MacroF1(truth, run.Select(o => o.Predicted).ToArray(), labels.Count)).ToArray();
        var mean = f1.Average();
        var spread = f1.Length > 1 ? Math.Sqrt(f1.Sum(v => (v - mean) * (v - mean)) / (f1.Length - 1)) : 0;
        var all = runs.SelectMany(run => run.Select((o, n) => (Outcome: o, Truth: truth[n]))).ToArray();
        var answered = all.Where(a => a.Outcome.Predicted >= 0).ToArray();
        var probabilistic = all.All(a => a.Outcome.Probability != null);
        var first = runs[0];
        var confusion = labels.Select(_ => new int[labels.Count + 1]).ToArray();
        for (var n = 0; n < truth.Length; n++) confusion[truth[n]][first[n].Predicted >= 0 ? first[n].Predicted : labels.Count]++;
        var perCategory = new List<CategoryReport>();
        for (var c = 0; c < labels.Count; c++)
        {
            var actual = truth.Count(t => t == c);
            if (actual == 0) continue;
            var predicted = first.Count(o => o.Predicted == c);
            var hits = confusion[c][c];
            var precision = predicted == 0 ? 0 : (double)hits / predicted;
            var recall = (double)hits / actual;
            var confused = Enumerable.Range(0, labels.Count).Where(o => o != c && confusion[c][o] > 0)
                .OrderByDescending(o => confusion[c][o]).Select(o => $"{labels[o]} ({confusion[c][o]})").FirstOrDefault();
            perCategory.Add(new CategoryReport(labels[c], actual, precision, recall,
                precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall), confused));
        }
        var firstAnswered = first.Where(o => o.Predicted >= 0).ToArray();
        var (low, high) = Wilson(first.Where((o, n) => o.Predicted >= 0 && o.Predicted == truth[n]).Count(), firstAnswered.Length);
        var (threshold, calibratedCoverage) = Calibrate(method, all.Select(a => (a.Outcome.Confidence, a.Outcome.Top == a.Truth)).ToArray(), examples);
        return new MethodReport(method, mean, spread,
            (double)answered.Length / all.Length,
            answered.Length == 0 ? 0 : (double)answered.Count(a => a.Outcome.Predicted == a.Truth) / answered.Length,
            (double)all.Count(a => a.Outcome.InTopThree) / all.Length,
            CoverageAtError(all.Select(a => (a.Outcome.Confidence, a.Outcome.Top == a.Truth)).ToArray(), ErrorLimit),
            probabilistic ? CalibrationError(all.Select(a => (a.Outcome.Probability!.Value, a.Outcome.Top == a.Truth)).ToArray()) : null,
            all.Average(a => a.Outcome.Microseconds), perCategory, confusion,
            (double)all.Count(a => a.Outcome.Top == a.Truth) / all.Length, low, high, unknown, threshold, calibratedCoverage);
    }

    public static double MacroF1(int[] truth, int[] predicted, int categories)
    {
        var f1 = new List<double>();
        for (var c = 0; c < categories; c++)
        {
            var actual = truth.Count(t => t == c);
            if (actual == 0) continue;
            var predictedCount = predicted.Count(p => p == c);
            var hits = truth.Zip(predicted).Count(x => x.First == c && x.Second == c);
            var precision = predictedCount == 0 ? 0 : (double)hits / predictedCount;
            var recall = (double)hits / actual;
            f1.Add(precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall));
        }
        return f1.Count == 0 ? 0 : f1.Average();
    }

    /// <summary>Largest share of cases, taken by descending confidence, whose top-1 error rate stays within the limit.</summary>
    public static double CoverageAtError((double Confidence, bool Correct)[] decisions, double limit)
    {
        if (decisions.Length == 0) return 0;
        var ordered = decisions.OrderByDescending(d => d.Confidence).ToArray();
        int errors = 0, best = 0;
        for (var k = 0; k < ordered.Length; k++)
        {
            if (!ordered[k].Correct) errors++;
            if (errors <= limit * (k + 1)) best = k + 1;
        }
        return (double)best / ordered.Length;
    }

    /// <summary>Expected calibration error over ten equal-width probability bins.</summary>
    public static double CalibrationError((double Probability, bool Correct)[] decisions)
    {
        if (decisions.Length == 0) return 0;
        return decisions.GroupBy(d => Math.Min(9, (int)(d.Probability * 10)))
            .Sum(g => (double)g.Count() / decisions.Length * Math.Abs(g.Average(d => d.Probability) - g.Average(d => d.Correct ? 1.0 : 0.0)));
    }

    /// <summary>Connected components of near copies; such copies must never be split between training and test.</summary>
    public static int[] Groups(float[][] vectors)
    {
        var parent = Enumerable.Range(0, vectors.Length).ToArray();
        int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        for (var i = 0; i < vectors.Length; i++)
            for (var j = i + 1; j < vectors.Length; j++)
                if (RocchioModel.Cosine(vectors[i], vectors[j]) >= DuplicateSimilarity) parent[Find(i)] = Find(j);
        return Enumerable.Range(0, vectors.Length).Select(Find).ToArray();
    }

    /// <summary>Groups are shuffled per category and dealt round-robin, so every fold sees every category where possible.</summary>
    public static int[] AssignFolds(int[] categories, int[] groups, int folds, int seed)
    {
        var random = new Random(seed);
        var fold = new int[categories.Length];
        var groupFold = new Dictionary<int, int>();
        foreach (var byCategory in Enumerable.Range(0, categories.Length).GroupBy(n => categories[n]))
        {
            var categoryGroups = byCategory.Select(n => groups[n]).Distinct().Where(g => !groupFold.ContainsKey(g))
                .OrderBy(_ => random.Next()).ToList();
            var start = random.Next(folds);
            for (var i = 0; i < categoryGroups.Count; i++) groupFold[categoryGroups[i]] = (start + i) % folds;
        }
        for (var n = 0; n < categories.Length; n++) fold[n] = groupFold[groups[n]];
        return fold;
    }
}

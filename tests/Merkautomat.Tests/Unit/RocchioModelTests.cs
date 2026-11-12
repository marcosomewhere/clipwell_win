using System.Text.Json;
using Merkautomat.Models;
using Merkautomat.Services;
using Merkautomat.Views;
using Xunit;

namespace Merkautomat.Tests;

public sealed class RocchioModelTests
{
    private const int Dimensions = 32;
    internal static readonly string[] Labels = ["A", "B", "C"];
    // Names are nearly identical, so only the saved examples separate the categories.
    internal static readonly float[][] Names = [Vec((0, 1f), (31, 0.01f)), Vec((0, 1f), (31, -0.01f)), Vec((0, 1f), (30, 0.01f))];

    internal static float[] Vec(params (int Dim, float Value)[] parts)
    {
        var v = new float[Dimensions];
        foreach (var (dim, value) in parts) v[dim] += value;
        return RocchioModel.Normalized(v);
    }

    /// <summary>A is broad (two themes), B sits between them, C is separate.</summary>
    internal static float[][][] Broad() =>
    [
        [.. Enumerable.Range(0, 4).Select(i => Vec((0, 1), (1, 1), (5 + i, 0.3f))), .. Enumerable.Range(0, 4).Select(i => Vec((0, 1), (2, 1), (5 + i, 0.3f)))],
        [.. Enumerable.Range(0, 4).Select(i => Vec((0, 1), (1, 0.85f), (2, 0.85f), (5 + i, 0.3f)))],
        [.. Enumerable.Range(0, 4).Select(i => Vec((0, 1), (4, 1), (5 + i, 0.3f)))],
    ];

    /// <summary>Three compact, well separated categories.</summary>
    internal static float[][][] Compact(int perCategory = 6) =>
    [
        [.. Enumerable.Range(0, perCategory).Select(i => Vec((0, 1), (1, 1), (6 + i, 0.3f)))],
        [.. Enumerable.Range(0, perCategory).Select(i => Vec((0, 1), (2, 1), (6 + i, 0.3f)))],
        [.. Enumerable.Range(0, perCategory).Select(i => Vec((0, 1), (3, 1), (6 + i, 0.3f)))],
    ];

    [Fact]
    public void HardNegativesMovePrototypesAwayFromForeignExamples()
    {
        var source = Vec((0, 1), (1, 1));
        var plain = RocchioModel.Build(Labels, Names, Compact(), false).Decide(source);
        var negative = RocchioModel.Build(Labels, Names, Compact(), false, negativeWeight: 0.2).Decide(source);
        Assert.Equal(0, negative.Category);
        Assert.True(negative.Confident);
        Assert.NotEqual(Math.Round(plain.Confidence, 6), Math.Round(negative.Confidence, 6));
        Assert.Equal(Math.Round(plain.Confidence, 6), Math.Round(RocchioModel.Build(Labels, Names, Compact(), false, negativeWeight: 0).Decide(source).Confidence, 6));
    }

    [Fact]
    public async Task CalibratedThresholdIsUsedOnceEnoughExamplesExist()
    {
        var (service, assignments) = Service(Compact(14));
        var result = await service.SuggestAsync([new BrainSource("c:1", "Titel", SourceText, "", DateTime.MinValue)], Labels,
            TestContext.Current.CancellationToken, assignments);
        Assert.Equal("A", result.Category);
        Assert.Contains("Schwelle aus deiner Auswertung", result.Reason);
    }

    [Fact]
    public void BroadCategoryNeedsSubPrototypes()
    {
        var source = Vec((0, 1), (1, 1));
        var single = RocchioModel.Build(Labels, Names, Broad(), multiPrototype: false).Decide(source);
        var multi = RocchioModel.Build(Labels, Names, Broad(), multiPrototype: true);
        Assert.False(single.Confident && single.Category == 0);
        var decision = multi.Decide(source);
        Assert.Equal(2, multi.SubPrototypeCounts[0]);
        Assert.True(decision.Confident);
        Assert.Equal("A", Labels[decision.Category]);
        Assert.Equal(3, decision.Candidates.Count);
        Assert.Equal("A", decision.Candidates[0].Category);
    }

    [Fact]
    public void ExamplesOnlyPrototypeIgnoresTheName()
    {
        // A misleading name for A (pointing at C) changes the name-based model but not the examples-only one.
        float[][] misleading = [Vec((0, 1), (3, 1)), Names[1], Names[2]];
        var source = Vec((0, 1), (3, 0.6f), (1, 1));
        var a = RocchioModel.Build(Labels, Names, Compact(), false, includeNames: false).Decide(source);
        var b = RocchioModel.Build(Labels, misleading, Compact(), false, includeNames: false).Decide(source);
        Assert.Equal(a.Candidates.Select(c => (c.Category, Math.Round(c.Score, 6))), b.Candidates.Select(c => (c.Category, Math.Round(c.Score, 6))));
        var withNames = RocchioModel.Build(Labels, misleading, Compact(), false).Decide(source);
        Assert.NotEqual(Math.Round(a.Confidence, 6), Math.Round(withNames.Confidence, 6));
    }

    [Fact]
    public void SmallCategoriesKeepOnePrototype()
        => Assert.Single(RocchioModel.SubClusters([.. Enumerable.Range(0, 5).Select(i => Vec((0, 1), (1 + i % 2, 1), (5 + i, 0.3f)))]));

    [Fact]
    public void AbstentionStillOffersCandidates()
    {
        var result = E5Categorization.Rank(Labels, Names, [Vec((0, 1))]);
        Assert.True(result.Uncertain);
        Assert.Equal(3, result.Candidates!.Count);
    }

    [Fact]
    public void KnnNeedsTwoOfThreeVotes()
    {
        var knn = new KnnClassifier(Labels, Compact());
        var clear = knn.Decide(Vec((0, 1), (1, 1)));
        Assert.True(clear.Confident);
        Assert.Equal(0, clear.Category);
        Assert.False(new KnnClassifier(Labels, [[], [], []]).Decide(Vec((0, 1))).Confident);
    }

    [Fact]
    public void LogisticRegressionSeparatesCompactCategoriesAndAbstainsInBetween()
    {
        var model = LogisticClassifier.Train(Labels, Compact());
        var clear = model.Decide(Vec((0, 1), (2, 1)));
        Assert.True(clear.Confident);
        Assert.Equal(1, clear.Category);
        Assert.NotNull(clear.Probability);
        Assert.False(model.Decide(Vec((0, 1), (1, 1), (2, 1), (3, 1))).Confident);
        Assert.False(LogisticClassifier.Train(Labels, [Compact()[0], [], []]).Decide(Vec((0, 1), (1, 1))).Confident);
    }

    [Fact]
    public void MethodSettingIsNormalizedAndRoundTrips()
    {
        Assert.Equal(ClassifierMethods.Auto, ClassifierMethods.Normalize("unbekannt"));
        Assert.Equal(ClassifierMethods.Knn, ClassifierMethods.Normalize("knn"));
        var settings = new AppSettings { CategorizationMethod = ClassifierMethods.LogisticRegression };
        settings.CategorizationFeedback.Add(new CategorizationFeedback(DateTime.Now, "rocchio", SuggestionOutcome.Corrected, true));
        settings.CategorizationRuns.Add(new CategorizationRun(DateTime.Now, 10, 3, "rocchio", [new("rocchio", 0.9, 0.8, 0.95)]));
        var copy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(ClassifierMethods.LogisticRegression, copy.CategorizationMethod);
        Assert.Equal(SuggestionOutcome.Corrected, copy.CategorizationFeedback[0].Outcome);
        Assert.Equal(0.9, copy.CategorizationRuns[0].Results[0].MacroF1);
    }

    [Fact]
    public async Task AutomaticChoiceFollowsTheBenchmarkAndFixedSettingIsRespected()
    {
        var setting = ClassifierMethods.Auto;
        var (service, assignments) = Service(Broad(), () => setting);
        var ct = TestContext.Current.CancellationToken;
        var source = new BrainSource("c:1", "Titel", SourceText, "", DateTime.MinValue);
        var auto = await service.SuggestAsync([source], Labels, ct, assignments);
        var (report, _) = await service.BenchmarkAsync(assignments, null, ct);
        Assert.NotEqual(ClassifierMethods.Rocchio, report!.Recommended);
        Assert.Equal(report.Recommended, auto.Method);
        Assert.Equal("A", auto.Category);
        Assert.Contains("automatisch nach Auswertung gewählt", auto.Reason);
        setting = ClassifierMethods.RocchioMulti;
        var fixedMulti = await service.SuggestAsync([source], Labels, ct, assignments);
        Assert.Equal(ClassifierMethods.RocchioMulti, fixedMulti.Method);
        Assert.Contains("fest eingestellt", fixedMulti.Reason);
        Assert.Contains("2 Schwerpunkte", fixedMulti.Reason);
    }

    private const string SourceText = "Ein neuer Text zum ersten Thema von A.";

    internal static (E5Categorization Service, Dictionary<string, string> Assignments) Service(float[][][] examples, Func<string>? method = null)
    {
        var texts = new Dictionary<string, (string Text, DateTime Date)>();
        var assignments = new Dictionary<string, string>();
        var vectors = new Dictionary<string, float[]> { ["Thema: A"] = Names[0], ["Thema: B"] = Names[1], ["Thema: C"] = Names[2], [SourceText] = Vec((0, 1), (1, 1)) };
        for (var c = 0; c < Labels.Length; c++)
            for (var i = 0; i < examples[c].Length; i++)
            {
                var text = $"Gespeichertes Beispiel {Labels[c]} Nummer {i} mit Text.";
                texts[$"n:{c}{i}"] = (text, DateTime.Now.AddMinutes(-i));
                assignments[$"n:{c}{i}"] = Labels[c];
                vectors[text] = examples[c][i];
            }
        var service = new E5Categorization((batch, _) => Task.FromResult(batch.Select(t => vectors[t]).ToArray()), () => true,
            keys => texts.Where(t => keys.Contains(t.Key)).ToDictionary(), method);
        return (service, assignments);
    }
}

public sealed class ClassifierBenchmarkTests
{
    [Fact]
    public void BroadDataRecommendsSubPrototypesCompactDataKeepsRocchio()
    {
        var broad = ClassifierBenchmark.Run(RocchioModelTests.Labels, RocchioModelTests.Names, RocchioModelTests.Broad());
        Assert.Equal(ClassifierMethods.All.Length, broad.Methods.Count);
        Assert.Equal(16, broad.Examples);
        Assert.Equal(0, broad.Get(ClassifierMethods.Names)!.Coverage);
        Assert.True(broad.Get(ClassifierMethods.RocchioMulti)!.MacroF1 > broad.Get(ClassifierMethods.Rocchio)!.MacroF1);
        // k-NN models the sub-groups directly and wins clearly here; the plain centroid must not be recommended.
        Assert.NotEqual(ClassifierMethods.Rocchio, broad.Recommended);
        Assert.True(broad.Get(broad.Recommended)!.MacroF1 >= broad.Get(ClassifierMethods.Rocchio)!.MacroF1 + ClassifierBenchmark.RequiredGain);

        var compact = ClassifierBenchmark.Run(RocchioModelTests.Labels, RocchioModelTests.Names, RocchioModelTests.Compact());
        Assert.Equal(1.0, compact.Get(ClassifierMethods.Rocchio)!.MacroF1, 3);
        Assert.Equal(ClassifierMethods.Rocchio, compact.Recommended);
        Assert.NotNull(compact.Get(ClassifierMethods.LogisticRegression)!.CalibrationError);
        Assert.Null(compact.Get(ClassifierMethods.Rocchio)!.CalibrationError);
        var confusion = compact.Get(ClassifierMethods.Rocchio)!.Confusion;
        Assert.Equal(6, confusion[0][0]);
        Assert.Equal(4, confusion[0].Length);
    }

    [Fact]
    public void NearCopiesStayInOneFoldAndEveryFoldSeesEachCategory()
    {
        var copy = RocchioModelTests.Vec((0, 1), (1, 1));
        float[][] vectors = [copy, (float[])copy.Clone(), RocchioModelTests.Vec((0, 1), (2, 1)), RocchioModelTests.Vec((0, 1), (3, 1))];
        var groups = ClassifierBenchmark.Groups(vectors);
        Assert.Equal(groups[0], groups[1]);
        Assert.NotEqual(groups[0], groups[2]);
        var categories = Enumerable.Range(0, 20).Select(i => i % 2).ToArray();
        var folds = ClassifierBenchmark.AssignFolds(categories, Enumerable.Range(0, 20).ToArray(), 5, seed: 3);
        foreach (var f in Enumerable.Range(0, 5))
            Assert.Equal([0, 1], categories.Where((_, n) => folds[n] == f).Distinct().Order());
        var grouped = ClassifierBenchmark.AssignFolds([0, 0, 1], [7, 7, 8], 2, seed: 1);
        Assert.Equal(grouped[0], grouped[1]);
    }

    [Fact]
    public void WilsonIntervalAndCalibrationFollowTheTargetPrecision()
    {
        var (low, high) = ClassifierBenchmark.Wilson(9, 10);
        Assert.Equal(0.596, low, 3);
        Assert.Equal(0.982, high, 3);
        var allRight = Enumerable.Repeat((0.1, true), 50).ToArray();
        Assert.Equal((0.04, 1.0), ClassifierBenchmark.Calibrate(ClassifierMethods.Rocchio, allRight, 50));
        Assert.Null(ClassifierBenchmark.Calibrate(ClassifierMethods.Rocchio, allRight, 39).Threshold);
        Assert.Null(ClassifierBenchmark.Calibrate(ClassifierMethods.Knn, allRight, 50).Threshold);
        var mixed = Enumerable.Repeat((0.2, true), 40).Concat(Enumerable.Repeat((0.05, false), 20)).ToArray();
        var (threshold, coverage) = ClassifierBenchmark.Calibrate(ClassifierMethods.Rocchio, mixed, 60);
        Assert.Equal(0.2, threshold);
        Assert.Equal(40.0 / 60, coverage, 3);
        Assert.Null(ClassifierBenchmark.Calibrate(ClassifierMethods.Rocchio, Enumerable.Repeat((0.2, true), 30).Concat(Enumerable.Repeat((0.05, false), 20)).ToArray(), 50).Threshold);
    }

    [Fact]
    public void UnknownCategoriesAreCountedFromFourCategoriesOn()
    {
        string[] labels = ["A", "B", "C", "D"];
        float[][] names = [.. RocchioModelTests.Names, RocchioModelTests.Vec((0, 1f), (29, 0.01f))];
        var examples = RocchioModelTests.Compact().Append([.. Enumerable.Range(0, 6).Select(i => RocchioModelTests.Vec((0, 1), (4, 1), (6 + i, 0.3f)))]).ToArray();
        var report = ClassifierBenchmark.Run(labels, names, examples);
        Assert.All(report.Methods, m => Assert.InRange(m.UnknownRejection!.Value, 0, 1));
        Assert.Null(ClassifierBenchmark.Run(RocchioModelTests.Labels, RocchioModelTests.Names, RocchioModelTests.Compact()).Methods[0].UnknownRejection);
        Assert.True(report.Get(ClassifierMethods.Rocchio)!.SelectiveLow <= report.Get(ClassifierMethods.Rocchio)!.SelectiveAccuracy);
        Assert.Contains("Unbekanntes abgewiesen", CategorizationReporting.ToMarkdown(report, ClassifierMethods.Rocchio, [], []));
    }

    [Fact]
    public void MetricsMatchHandComputedValues()
    {
        // Class 0: precision 1, recall 0.5, F1 2/3; class 1: F1 0.
        Assert.Equal(1.0 / 3, ClassifierBenchmark.MacroF1([0, 0, 1, 1], [0, -1, -1, -1], 2), 3);
        Assert.Equal(0.75, ClassifierBenchmark.CoverageAtError([(0.9, true), (0.8, true), (0.7, true), (0.6, false)], 0.10));
        Assert.Equal(0.0, ClassifierBenchmark.CoverageAtError([(0.9, false)], 0.10));
        Assert.Equal(0.1, ClassifierBenchmark.CalibrationError([(0.9, true), (0.9, true), (0.9, true), (0.9, true), (0.9, true),
            (0.9, true), (0.9, true), (0.9, true), (0.9, true), (0.9, true)]), 3);
    }

    [Fact]
    public void RecommendationNeedsAClearGainWithoutLosingPrecision()
    {
        MethodReport R(string m, double f1, double sel) => new(m, f1, 0, 1, sel, 1, 1, null, 1, [], []);
        Assert.Equal(ClassifierMethods.Rocchio, ClassifierBenchmark.Recommend([R("rocchio", 0.80, 0.9), R("knn", 0.81, 0.9)]));
        Assert.Equal(ClassifierMethods.Knn, ClassifierBenchmark.Recommend([R("rocchio", 0.80, 0.9), R("knn", 0.85, 0.9)]));
        Assert.Equal(ClassifierMethods.Rocchio, ClassifierBenchmark.Recommend([R("rocchio", 0.80, 0.9), R("logreg", 0.90, 0.7)]));
        Assert.Equal(ClassifierMethods.Rocchio, ClassifierBenchmark.Recommend([R("rocchio", 0.80, 0.9), R("names", 0.95, 1)]));
    }
}

public sealed class CategorizationReportingTests
{
    private static LocalAiSuggestion Suggestion(string category, bool uncertain = false, params string[] candidates)
        => new("", category, "", uncertain, candidates.Select(c => new CategoryCandidate(c, 0.5)).ToList(), ClassifierMethods.Rocchio);

    [Fact]
    public void FeedbackClassifiesAcceptedCorrectedAndAbstained()
    {
        var now = DateTime.Now;
        Assert.Equal(SuggestionOutcome.Accepted, CategorizationReporting.Feedback(Suggestion("Reisen"), " reisen ", now)!.Outcome);
        var corrected = CategorizationReporting.Feedback(Suggestion("Reisen", false, "Reisen", "Arbeit"), "Arbeit", now)!;
        Assert.Equal(SuggestionOutcome.Corrected, corrected.Outcome);
        Assert.True(corrected.InTopThree);
        var abstained = CategorizationReporting.Feedback(Suggestion("", true, "Technik"), "Kochen", now)!;
        Assert.Equal(SuggestionOutcome.NoSuggestion, abstained.Outcome);
        Assert.False(abstained.InTopThree);
        Assert.Null(CategorizationReporting.Feedback(null, "Reisen", now));
        Assert.Null(CategorizationReporting.Feedback(new LocalAiSuggestion("", "Reisen", "", false), "Reisen", now));
    }

    [Fact]
    public void SummaryAndMarkdownReportTheNumbers()
    {
        var now = DateTime.Now;
        var feedback = new List<CategorizationFeedback>
        {
            new(now, "knn", SuggestionOutcome.Accepted, true), new(now, "rocchio", SuggestionOutcome.Accepted, true),
            new(now, "rocchio", SuggestionOutcome.Corrected, true), new(now, "rocchio", SuggestionOutcome.NoSuggestion, false),
        };
        var summary = CategorizationReporting.Summarize(feedback);
        Assert.Equal(["rocchio", "knn"], summary.Select(s => s.Method));
        Assert.Equal(0.5, summary[0].Precision, 3);
        var list = Enumerable.Range(0, 5).ToList();
        CategorizationReporting.Append(list, 5, 3);
        Assert.Equal([3, 4, 5], list);
        var report = ClassifierBenchmark.Run(RocchioModelTests.Labels, RocchioModelTests.Names, RocchioModelTests.Compact());
        var markdown = CategorizationReporting.ToMarkdown(report, ClassifierMethods.Rocchio, summary, [CategorizationReporting.ToRun(report)]);
        Assert.Contains("| Rocchio (Name + Beispiele) |", markdown);
        Assert.Contains("## Kategorien (Rocchio (Name + Beispiele))", markdown);
        Assert.Contains("## Praxis", markdown);
        Assert.Contains("## Verlauf", markdown);
    }

    [Fact]
    public void ReportWindowShowsDataAndStoresTheChosenMethod()
        => StaTestRunner.Run(() =>
        {
            var saves = 0;
            var settings = new AppSettings();
            settings.CategorizationFeedback.Add(new CategorizationFeedback(DateTime.Now, "rocchio", SuggestionOutcome.Accepted, true));
            var report = ClassifierBenchmark.Run(RocchioModelTests.Labels, RocchioModelTests.Names, RocchioModelTests.Broad());
            settings.CategorizationRuns.Add(CategorizationReporting.ToRun(report));
            var window = new ClassifierReportWindow(null, settings, () => saves++, runOnOpen: false);
            window.Show();
            Assert.True(((System.Windows.Controls.Grid)window.FindName("FeedbackTable")).Children.Count > 7);
            Assert.True(((System.Windows.Controls.Grid)window.FindName("HistoryTable")).Children.Count > 6);
            var box = (System.Windows.Controls.ComboBox)window.FindName("MethodBox");
            box.SelectedItem = box.Items.OfType<System.Windows.Controls.ComboBoxItem>().First(i => (string)i.Tag == ClassifierMethods.Knn);
            Assert.Equal(ClassifierMethods.Knn, settings.CategorizationMethod);
            Assert.Equal(1, saves);
            window.Close();
        });

    [Fact]
    public void CategorizeDialogReportsWhatHappenedToTheSuggestion()
        => StaTestRunner.Run(() =>
        {
            var recorded = new List<(LocalAiSuggestion? Suggestion, string Category)>();
            var item = new CategorizationItem { Source = new BrainSource("c:1", "T", "Ein ausreichend langer Text für den Vergleich.", "", DateTime.MinValue), SourceName = "Zwischenablage" };
            var queue = new CategorizationQueue([item], new Dictionary<string, string>());
            var dialog = new CategorizeDialog("Test", "", queue, null, (_, _) => { }, false, false, (s, c) => recorded.Add((s, c)));
            dialog.Show();
            item.Suggestion = new LocalAiSuggestion("", "Reisen", "", false, null, ClassifierMethods.Rocchio);
            ((System.Windows.Controls.ComboBox)dialog.FindName("ItemCategory")).Text = "Arbeit";
            ((System.Windows.Controls.Button)dialog.FindName("ApplyButton")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Assert.Single(recorded);
            Assert.Equal("Reisen", recorded[0].Suggestion!.Category);
            Assert.Equal("Arbeit", recorded[0].Category);
            dialog.Close();
        });
}

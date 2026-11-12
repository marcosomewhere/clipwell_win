using Merkautomat.Services;
using Xunit;
using static Merkautomat.Tests.RocchioModelTests;

namespace Merkautomat.Tests;

public sealed class RocchioVariantTests
{
    /// <summary>Compact data where B holds one text that clearly belongs to A.</summary>
    private static float[][][] WithMislabel()
    {
        var data = Compact(8);
        data[1] = [.. data[1], Vec((0, 1), (1, 1), (20, 0.25f))];
        return data;
    }

    [Fact]
    public void EuclideanVariantFindsTheNearestCentroidButDiffersFromCosine()
    {
        var euclid = new EuclideanRocchio(Labels, Names, Compact());
        var decision = euclid.Decide(Vec((0, 1), (2, 1)));
        Assert.Equal(1, decision.Category);
        Assert.True(decision.Confident);
        Assert.True(decision.Candidates[0].Score > decision.Candidates[1].Score);
        var cosine = RocchioModel.Build(Labels, Names, Compact(), false).Decide(Vec((0, 1), (2, 1)));
        Assert.NotEqual(Math.Round(cosine.Confidence, 6), Math.Round(decision.Confidence, 6));
    }

    [Fact]
    public void TrimmedMeanDropsTheOutlier()
    {
        float[][] examples = [.. Enumerable.Range(0, 5).Select(i => Vec((0, 1), (1, 1), (6 + i, 0.3f))), Vec((0, 1), (3, 2))];
        var plain = RocchioModel.Mean(examples);
        var trimmed = RocchioModel.TrimmedMean(examples);
        var outlier = examples[^1];
        Assert.True(RocchioModel.Cosine(trimmed, outlier) < RocchioModel.Cosine(plain, outlier));
        Assert.Equal(RocchioModel.Mean(examples[..4]), RocchioModel.TrimmedMean(examples[..4]));
    }

    [Fact]
    public void RobustVariantIsLessPulledByAWrongExample()
    {
        var source = Vec((0, 1), (2, 1));
        var plain = RocchioModel.Build(Labels, Names, WithMislabel(), false).Decide(source);
        var robust = RocchioModel.Build(Labels, Names, WithMislabel(), false, robust: true).Decide(source);
        Assert.Equal(1, robust.Category);
        Assert.True(robust.Confidence >= plain.Confidence);
    }

    [Fact]
    public void BenchmarkFlagsTheMislabeledAssignmentAndReportsTimes()
    {
        var report = ClassifierBenchmark.Run(Labels, Names, WithMislabel());
        var flagged = Assert.Single(report.Suspicious!);
        Assert.Equal("B", flagged.Category);
        Assert.Equal("A", flagged.Suggested);
        Assert.Equal(8 + 8, flagged.Case);
        Assert.Contains(report.Methods, m => m.Method == ClassifierMethods.RocchioEuclidean);
        Assert.Contains(report.Methods, m => m.Method == ClassifierMethods.RocchioRobust);
        Assert.All(report.Methods, m => Assert.True(m.TrainingMilliseconds >= 0));
        var markdown = CategorizationReporting.ToMarkdown(report, ClassifierMethods.Rocchio, [], []);
        Assert.Contains("## Zuordnungen zur Prüfung", markdown);
        Assert.Contains(" ms / ", markdown);
    }

    [Fact]
    public async Task EvaluationNamesTheFlaggedEntryByKey()
    {
        var (service, assignments) = Service(WithMislabel());
        var (report, _) = await service.BenchmarkAsync(assignments, null, TestContext.Current.CancellationToken);
        var flagged = Assert.Single(report!.Suspicious!);
        Assert.Equal("B", assignments[flagged.Key]);
        Assert.Contains("Gespeichertes Beispiel B", flagged.Excerpt);
        Assert.True(report.EmbeddingMilliseconds >= 0);
    }
}

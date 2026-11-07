using System.IO;
using System.Text.Json;
using ClipwellWin.Models;
using ClipwellWin.Services;
using ClipwellWin.Views;
using Xunit;

namespace ClipwellWin.Tests;

public sealed class E5CategorizationTests
{
    private static readonly string[] Categories = ["Reisen", "Technik"];
    private static readonly float[][] CategoryVectors = [[1, 0], [0, 1]];
    private static BrainSource Source(string text = "Eine ausreichend lange Reisebeschreibung nach Rom.") => new("c:1", "Eigener Titel", text, "", DateTime.MinValue);
    private static float[] At(double degrees) => [(float)Math.Cos(degrees * Math.PI / 180), (float)Math.Sin(degrees * Math.PI / 180)];

    [Fact]
    public void ModelChoiceRoundTripsWithoutEnablingUnknownValues()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings { LocalAiModel = E5Categorization.Id }))!;
        Assert.Equal(E5Categorization.Id, LocalAiModel.Normalize(settings.LocalAiModel));
        Assert.Equal("off", LocalAiModel.Normalize("e5-unknown"));
    }

    [Fact]
    public void ClearAgreementReturnsOnlyExistingCategoryAndNoTitle()
    {
        var result = E5Categorization.Rank(Categories, CategoryVectors, [At(5), At(15)]);
        Assert.Equal("Reisen", result.Category);
        Assert.Empty(result.Title);
        Assert.False(result.Uncertain);
    }

    [Theory]
    [InlineData(44, 44)]
    [InlineData(180, 180)]
    [InlineData(0, 90)]
    public void AmbiguousUnrelatedAndMixedSelectionsAbstain(double first, double second)
    {
        var result = E5Categorization.Rank(Categories, CategoryVectors, [At(first), At(second)]);
        Assert.True(result.Uncertain);
        Assert.Empty(result.Category);
    }

    [Fact]
    public void CloseScoresAbstainEvenWhenAbsoluteSimilarityIsHigh()
        => Assert.True(E5Categorization.Rank(Categories, [At(0), At(5)], [At(0)]).Uncertain);

    [Fact]
    public void InvalidVectorsNeverProduceCategory()
    {
        Assert.True(E5Categorization.Rank(Categories, CategoryVectors, [[float.NaN, 0]]).Uncertain);
        Assert.True(E5Categorization.Rank(Categories, CategoryVectors, [[0, 0]]).Uncertain);
        Assert.True(E5Categorization.Rank(Categories, CategoryVectors, [[1, 0, 0]]).Uncertain);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task InsufficientCategoryVocabularyDoesNotLoadModel(int count)
    {
        var service = new E5Categorization((_, _) => throw new Exception("Must not embed"), () => true);
        Assert.True((await service.SuggestAsync([Source()], Categories.Take(count), TestContext.Current.CancellationToken)).Uncertain);
    }

    [Fact]
    public async Task EmptyTextDoesNotLoadModel()
    {
        var service = new E5Categorization((_, _) => throw new Exception("Must not embed"), () => true);
        Assert.True((await service.SuggestAsync([Source("abc")], Categories, TestContext.Current.CancellationToken)).Uncertain);
    }

    [Fact]
    public async Task CacheReusesEmbeddingsButRemovedCategoriesCannotWin()
    {
        var embedded = new List<string>();
        var service = new E5Categorization((texts, _) =>
        {
            embedded.AddRange(texts);
            return Task.FromResult(texts.Select(t => t.StartsWith("Thema:") && t != "Thema: Reisen" ? new float[] { 0, 1 } : [1f, 0f]).ToArray());
        }, () => true);
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal("Reisen", (await service.SuggestAsync([Source()], Categories, ct)).Category);
        Assert.Equal(3, embedded.Count);
        await service.SuggestAsync([Source()], Categories, ct);
        Assert.Equal(3, embedded.Count);
        var changed = await service.SuggestAsync([Source()], ["Technik", "Arbeit"], ct);
        Assert.True(changed.Uncertain);
        Assert.Empty(changed.Category);
        Assert.Equal(4, embedded.Count);
    }

    [Fact]
    public async Task ExcerptsAreDeduplicatedAndBounded()
    {
        var embedded = new List<string>();
        var service = new E5Categorization((texts, _) =>
        {
            embedded.AddRange(texts);
            return Task.FromResult(texts.Select(_ => new float[] { 1, 0 }).ToArray());
        }, () => true);
        var sources = Enumerable.Range(0, 20).Select(i => Source(i + new string('x', 1000))).ToList();
        sources.Insert(1, sources[0]);
        await service.SuggestAsync(sources, [" Reisen ", "REISEN", "Technik"], TestContext.Current.CancellationToken);
        Assert.Equal(10, embedded.Count);
        Assert.All(embedded.Skip(2), t => Assert.Equal(700, t.Length));
    }

    [Fact]
    public async Task CancellationDoesNotPublishGeneratedVectors()
    {
        var calls = 0;
        using var cancel = new CancellationTokenSource();
        var service = new E5Categorization((texts, _) =>
        {
            if (++calls == 1) cancel.Cancel();
            return Task.FromResult(texts.Select(_ => new float[] { 1, 0 }).ToArray());
        }, () => true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SuggestAsync([Source()], Categories, cancel.Token));
        await service.SuggestAsync([Source()], Categories, TestContext.Current.CancellationToken);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task E5RouteWorksWithoutGraniteFilesAndDoesNotCreateThem()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var classifier = new E5Categorization((texts, _) => Task.FromResult(texts.Select(t => t == "Thema: Technik" ? new float[] { 0, 1 } : [1f, 0f]).ToArray()), () => true);
        using var service = new LocalAiService(root, () => E5Categorization.Id, classifier);
        Assert.True(service.Ready);
        var result = await service.SuggestAsync([Source()], Categories, TestContext.Current.CancellationToken);
        Assert.Equal("Reisen", result.Category);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task SharedIndexUsesOneModelAndSerializesQueryEmbeddings()
    {
        var fake = new CountingEmbedder();
        var created = 0;
        using var index = new SemanticIndex(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db"), "test", () => { created++; return fake; });
        var ct = TestContext.Current.CancellationToken;
        await Task.WhenAll(index.EmbedForCategorizationAsync(["Thema: Reisen", "Reisetext"], ct), index.EmbedForCategorizationAsync(["Thema: Technik"], ct));
        Assert.Equal(1, created);
        Assert.Equal(1, fake.Peak);
        Assert.Equal(3, fake.Queries.Count);
        Assert.Contains("Thema: Reisen", fake.Queries);
    }

    [Fact]
    public void E5ApplyPreservesEditedTitle()
        => StaTestRunner.Run(() =>
        {
            var dialog = new BrainKnowledgeDialog("Mein eigener Titel", "Arbeit", Categories,
                [new() { Source = Source(), Selected = true }], [new("Neu", null, "")], false);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(BrainKnowledgeDialog).GetField("_aiSuggestion", flags)!.SetValue(dialog, new LocalAiSuggestion("", "Reisen", "Vergleich", false));
            typeof(BrainKnowledgeDialog).GetField("_aiKeys", flags)!.SetValue(dialog, new HashSet<string> { "c:1" });
            ((System.Windows.Controls.Button)dialog.FindName("AiApplyButton")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Assert.Equal("Mein eigener Titel", dialog.TopicTitle);
            Assert.Equal("Reisen", dialog.TopicCategory);
            dialog.Close();
        });

    private sealed class CountingEmbedder : ITextEmbedder
    {
        private int _active;
        public int Peak;
        public readonly List<string> Queries = [];
        public float[] EmbedQuery(string query)
        {
            Peak = Math.Max(Peak, Interlocked.Increment(ref _active));
            lock (Queries) Queries.Add(query);
            Thread.Sleep(10);
            Interlocked.Decrement(ref _active);
            return [1, 0];
        }
        public float[][] EmbedPassages(IReadOnlyList<string> texts) => throw new Exception("Classification must use query prefixes on both sides");
    }
}

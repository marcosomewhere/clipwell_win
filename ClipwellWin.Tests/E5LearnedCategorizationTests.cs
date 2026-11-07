using System.IO;
using ClipwellWin.Services;
using Xunit;

namespace ClipwellWin.Tests;

public sealed class E5LearnedCategorizationTests
{
    private static readonly string[] Labels = ["A", "B", "C"];
    // Category names that E5 can barely tell apart; the saved examples carry the distinction.
    private static readonly float[][] Names = [[1, 0.01f, 0, 0], [1, 0, 0.01f, 0], [1, 0, 0, 0.01f]];
    private static readonly float[]?[] Centroids = [[1, 0.6f, 0, 0], [1, 0, 0.6f, 0], [1, 0, 0, 0.6f]];
    private static readonly float[] NearA = [1, 0.5f, 0, 0];

    [Fact]
    public void SavedExamplesSeparateCategoriesThatNamesCannot()
    {
        Assert.True(E5Categorization.Rank(Labels, Names, [NearA]).Uncertain);
        var learned = E5Categorization.Rank(Labels, Names, [NearA], Centroids, 6);
        Assert.False(learned.Uncertain);
        Assert.Equal("A", learned.Category);
        Assert.Empty(learned.Title);
        Assert.Contains("gespeicherten Zuordnungen", learned.Reason);
    }

    [Fact]
    public void TextBetweenAllLearnedCategoriesAbstains()
        => Assert.True(E5Categorization.Rank(Labels, Names, [[1, 0, 0, 0]], Centroids, 6).Uncertain);

    [Fact]
    public void MixedSourcesAbstainInLearnedMode()
        => Assert.True(E5Categorization.Rank(Labels, Names, [NearA, [1, 0, 0.5f, 0]], Centroids, 6).Uncertain);

    [Fact]
    public void TwoCategoriesStayOnTheNameComparison()
        => Assert.True(E5Categorization.Rank(Labels[..2], Names[..2], [NearA], Centroids[..2], 4).Uncertain);

    [Fact]
    public void InvalidCentroidNeverProducesCategory()
        => Assert.True(E5Categorization.Rank(Labels, Names, [NearA], [[float.NaN, 0, 0, 0], null, null], 2).Uncertain);

    [Fact]
    public async Task SelectedSourcesAreNotTheirOwnExamplesAndSingleExamplesAreIgnored()
    {
        var requested = new List<string>();
        var embedded = new List<string>();
        var texts = new Dictionary<string, (string Text, DateTime Date)>
        {
            ["c:1"] = ("Der ausgewählte Eintrag selbst mit genug Text.", DateTime.Now),
            ["c:2"] = ("Erstes gespeichertes Beispiel für A mit Text.", DateTime.Now),
            ["c:3"] = ("Zweites gespeichertes Beispiel für A mit Text.", DateTime.Now),
            ["c:4"] = ("Einziges gespeichertes Beispiel für B mit Text.", DateTime.Now),
        };
        var service = new E5Categorization((batch, _) =>
        {
            embedded.AddRange(batch);
            return Task.FromResult(batch.Select(Vector).ToArray());
        }, () => true, keys => { requested.AddRange(keys); return texts.Where(t => keys.Contains(t.Key)).ToDictionary(); });
        var assignments = new Dictionary<string, string> { ["c:1"] = "A", ["c:2"] = "a ", ["c:3"] = "A", ["c:4"] = "B", ["c:5"] = "Gelöscht" };
        var result = await service.SuggestAsync([new BrainSource("c:1", "Titel", "Ein ausgewählter Text, der zu A gehört.", "", DateTime.MinValue)],
            Labels, TestContext.Current.CancellationToken, assignments);
        // The selected source is looked up for the global sub-prototype check, but never used as its own example.
        Assert.DoesNotContain(texts["c:1"].Text, embedded);
        Assert.DoesNotContain("c:5", requested);
        Assert.Contains(texts["c:2"].Text, embedded);
        Assert.DoesNotContain(texts["c:4"].Text, embedded);
        Assert.Equal("A", result.Category);
        Assert.Contains("davon 1 mit 2 eigenen Beispielen", result.Reason);
    }

    [Theory]
    [InlineData(2, "erst ab drei Kategorien")]
    [InlineData(3, "verglichen wurden nur die Namen")]
    public async Task ReasonExplainsWhySavedAssignmentsWereNotUsed(int categories, string expected)
    {
        var service = new E5Categorization((batch, _) => Task.FromResult(batch.Select(Vector).ToArray()), () => true,
            keys => keys.ToDictionary(k => k, _ => ("Einziges gespeichertes Beispiel für A mit Text.", DateTime.Now)));
        var result = await service.SuggestAsync([new BrainSource("c:1", "Titel", "Ein ausgewählter Text, der zu A gehört.", "", DateTime.MinValue)],
            Labels.Take(categories), TestContext.Current.CancellationToken, new Dictionary<string, string> { ["c:2"] = "A" });
        Assert.Contains(expected, result.Reason);
    }

    [Fact]
    public async Task ExamplesAreBoundedPerCategoryNewestFirst()
    {
        var embedded = new List<string>();
        var start = new DateTime(2026, 1, 1);
        var texts = Enumerable.Range(0, 20).ToDictionary(i => $"n:{i}", i => (Text: $"Gespeichertes Beispiel Nummer {i:00} für A.", Date: start.AddDays(i)));
        var service = new E5Categorization((batch, _) =>
        {
            embedded.AddRange(batch);
            return Task.FromResult(batch.Select(Vector).ToArray());
        }, () => true, keys => texts.Where(t => keys.Contains(t.Key)).ToDictionary());
        await service.SuggestAsync([new BrainSource("c:1", "Titel", "Ein ausgewählter Text, der zu A gehört.", "", DateTime.MinValue)],
            Labels, TestContext.Current.CancellationToken, texts.Keys.ToDictionary(k => k, _ => "A"));
        var examples = embedded.Where(t => t.StartsWith("Gespeichertes")).ToList();
        Assert.Equal(E5Categorization.MaxExamplesPerCategory, examples.Count);
        Assert.Contains(examples, t => t.Contains("Nummer 19"));
        Assert.DoesNotContain(examples, t => t.Contains("Nummer 03"));
    }

    /// <summary>Uses the bundled model; guards the measured margins of the learned comparison.</summary>
    [Fact]
    public async Task BundledModelLearnsFromSavedAssignments()
    {
        using var model = new EmbeddingModel(Path.Combine(AppContext.BaseDirectory, "models"));
        Task<float[][]> Embed(IReadOnlyList<string> batch, CancellationToken _) => Task.FromResult(batch.Select(model.EmbedQuery).ToArray());
        string[] categories = ["Reisen", "Arbeit", "Technik", "Kochen"];
        string[][] examples =
        [
            ["Flug nach Lissabon gebucht, Hotel in der Altstadt für drei Nächte.", "Zugverbindung München Wien, Sitzplatzreservierung nicht vergessen.", "Packliste für den Strandurlaub: Sonnencreme, Reisepass, Adapter."],
            ["Meeting mit dem Vertrieb am Montag, Quartalszahlen vorbereiten.", "Urlaubsantrag bei der Personalabteilung einreichen und Vertretung klären.", "Protokoll der Teamrunde an alle Projektbeteiligten schicken."],
            ["OpenVPN Zertifikat erneuern und im Client importieren.", "Docker Container startet nicht, Port 8080 bereits belegt.", "Git Rebase auf main, Konflikte in der Konfiguration lösen."],
            ["Rezept Apfelkuchen mit Zimt und Streuseln.", "Risotto mit Pilzen, Brühe langsam zugeben.", "Brot backen mit Sauerteig, Teig über Nacht gehen lassen."],
        ];
        var texts = new Dictionary<string, (string Text, DateTime Date)>();
        var assignments = new Dictionary<string, string>();
        for (var c = 0; c < categories.Length; c++)
            for (var i = 0; i < examples[c].Length; i++)
            {
                texts[$"n:{c}{i}"] = (examples[c][i], DateTime.Now);
                assignments[$"n:{c}{i}"] = categories[c];
            }
        var service = new E5Categorization(Embed, () => true, keys => texts.Where(t => keys.Contains(t.Key)).ToDictionary());
        var ct = TestContext.Current.CancellationToken;
        Task<LocalAiSuggestion> Suggest(string text, IReadOnlyDictionary<string, string>? saved)
            => service.SuggestAsync([new BrainSource("c:1", "Titel", text, "", DateTime.MinValue)], categories, ct, saved);

        const string python = "Python Skript wirft ImportError nach Update der Bibliothek.";
        Assert.True((await Suggest(python, null)).Uncertain);
        Assert.Equal("Technik", (await Suggest(python, assignments)).Category);
        Assert.Equal("Arbeit", (await Suggest("Budgetplanung für das nächste Quartal mit der Abteilungsleitung abstimmen.", assignments)).Category);
        Assert.Equal("Reisen", (await Suggest("Wir planen eine Reise nach Italien mit Hotel in Rom und Stadtführung.", assignments)).Category);
        Assert.True((await Suggest("Geburtstagsgeschenk für Oma aussuchen und Karte schreiben.", assignments)).Uncertain);
        Assert.True((await Suggest("Fußballtraining am Dienstag fällt wegen Regen aus.", assignments)).Uncertain);
    }

    private static float[] Vector(string text) => text.StartsWith("Thema: ")
        ? Names[Array.IndexOf(Labels, text[7..])]
        : text.Contains("für A") || text.Contains("zu A") ? [1, 0.6f, 0, 0]
        : text.Contains("für B") ? [1, 0, 0.6f, 0] : [1, 0, 0, 0.6f];
}

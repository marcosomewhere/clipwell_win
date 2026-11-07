using System.IO;
using ClipwellWin.Services;
using ClipwellWin.Views;
using Xunit;

namespace ClipwellWin.Tests;

public sealed class CategorizationQueueTests
{
    private static CategorizationItem Item(string key, string text = "Ein ausreichend langer Text für den Vergleich.")
        => new() { Source = new BrainSource(key, "Titel " + key, text, "", DateTime.MinValue), SourceName = "Zwischenablage" };

    [Fact]
    public void SavedCategoriesAreLoadedAndOfferedOnce()
    {
        var queue = new CategorizationQueue([Item("c:1"), Item("c:2")],
            new Dictionary<string, string> { ["c:1"] = " Reisen ", ["c:9"] = "reisen", ["c:8"] = "Arbeit", ["c:7"] = "  " });
        Assert.Equal("Reisen", queue.Items[0].Category);
        Assert.Equal("", queue.Items[1].Category);
        Assert.Equal(["Arbeit", "Reisen"], queue.Categories);
        Assert.DoesNotContain("c:7", queue.Assignments.Keys);
    }

    [Fact]
    public void ApplyRecordsAssignmentsAndBumpsVersionOnlyOnChange()
    {
        var item = Item("c:1");
        var queue = new CategorizationQueue([item], new Dictionary<string, string>());
        Assert.Equal(["c:1"], queue.Apply([item], " Technik "));
        Assert.Equal("Technik", queue.Assignments["c:1"]);
        Assert.Equal(CategorizationState.Applied, item.State);
        Assert.Equal(1, queue.Version);
        Assert.Empty(queue.Apply([item], "Technik"));
        Assert.Equal(1, queue.Version);
        Assert.StartsWith("Zugeordnet: Technik", item.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrTooLongCategoryIsRejected(string category)
    {
        var item = Item("c:1");
        var queue = new CategorizationQueue([item], new Dictionary<string, string>());
        Assert.NotNull(CategorizationQueue.Validate(category));
        Assert.NotNull(CategorizationQueue.Validate(new string('x', 81)));
        Assert.Throws<ArgumentException>(() => queue.Apply([item], category));
        Assert.Empty(queue.Assignments);
    }

    [Fact]
    public void NextOpenSkipsDoneItemsWrapsAndHonorsTheFilter()
    {
        var items = new[] { Item("c:1"), Item("c:2"), Item("c:3"), Item("c:4") };
        var queue = new CategorizationQueue(items, new Dictionary<string, string> { ["c:3"] = "Reisen" });
        Assert.Equal(0, queue.NextOpen(-1, onlyUncategorized: true));
        queue.Apply([items[0]], "Arbeit");
        Assert.Equal(1, queue.NextOpen(0, onlyUncategorized: true));
        CategorizationQueue.Skip(items[1]);
        Assert.Equal(3, queue.NextOpen(1, onlyUncategorized: true));
        Assert.Equal(2, queue.NextOpen(1, onlyUncategorized: false));
        Assert.Equal(2, queue.NextOpen(3, onlyUncategorized: false));
        queue.Apply([items[3]], "Arbeit");
        Assert.Equal(-1, queue.NextOpen(3, onlyUncategorized: true));
    }

    [Fact]
    public void LearningModelsRecomputeAfterNewAssignmentsOthersKeepTheirSuggestion()
    {
        var items = new[] { Item("c:1"), Item("c:2") };
        var queue = new CategorizationQueue(items, new Dictionary<string, string>());
        Assert.True(queue.NeedsSuggestion(items[1], learns: true));
        items[1].Suggestion = new LocalAiSuggestion("", "Reisen", "", false);
        items[1].SuggestionVersion = queue.Version;
        Assert.False(queue.NeedsSuggestion(items[1], learns: true));
        queue.Apply([items[0]], "Arbeit");
        Assert.True(queue.NeedsSuggestion(items[1], learns: true));
        Assert.False(queue.NeedsSuggestion(items[1], learns: false));
    }

    [Fact]
    public void StatusShowsSuggestionOnlyWhenItDiffers()
    {
        var item = Item("c:1");
        _ = new CategorizationQueue([item], new Dictionary<string, string> { ["c:1"] = "Reisen" });
        item.Suggestion = new LocalAiSuggestion("", "reisen", "", false);
        Assert.Equal("Kategorie: Reisen", item.Status);
        item.Suggestion = new LocalAiSuggestion("", "Arbeit", "", false);
        Assert.Equal("Kategorie: Reisen · Vorschlag: Arbeit", item.Status);
    }

    [Fact]
    public void DialogSavesEachConfirmationAndLaterE5SuggestionsLearnFromIt()
        => StaTestRunner.Run(() =>
        {
            var seen = new List<IReadOnlyDictionary<string, string>?>();
            var saved = new List<(string Key, string Category)>();
            var e5 = new E5Categorization((batch, _) => Task.FromResult(batch.Select(_ => new float[] { 1, 0 }).ToArray()), () => true);
            using var ai = new LocalAiService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), () => E5Categorization.Id, e5);
            var items = new[] { Item("c:1"), Item("c:2") };
            var queue = new CategorizationQueue(items, new Dictionary<string, string>());
            var dialog = new CategorizeDialog("Test", "", queue, ai, (keys, category) => saved.AddRange(keys.Select(k => (k, category))),
                onlyUncategorized: true, groupFirst: false);
            dialog.Show();
            var box = (System.Windows.Controls.ComboBox)dialog.FindName("ItemCategory");
            box.Text = "Reisen";
            Click(dialog, "ApplyButton");
            Assert.Equal([("c:1", "Reisen")], saved);
            Assert.True(dialog.Changed);
            Assert.Same(items[1], ((System.Windows.Controls.ListBox)dialog.FindName("ItemList")).SelectedItem);
            Assert.True(queue.NeedsSuggestion(items[1], learns: true) || items[1].SuggestionVersion == queue.Version);
            box.Text = "";
            Click(dialog, "ApplyButton");
            Assert.Single(saved);
            Click(dialog, "SkipButton");
            Assert.Equal(CategorizationState.Skipped, items[1].State);
            dialog.Close();
        });

    [Fact]
    public void DialogAppliesOneCategoryToAllMarkedEntries()
        => StaTestRunner.Run(() =>
        {
            var saved = new List<(string Key, string Category)>();
            var items = new[] { Item("c:1"), Item("c:2"), Item("c:3") };
            items[0].Marked = true;
            var queue = new CategorizationQueue(items, new Dictionary<string, string>());
            var dialog = new CategorizeDialog("Test", "", queue, null, (keys, category) => saved.AddRange(keys.Select(k => (k, category))),
                onlyUncategorized: false, groupFirst: true);
            dialog.Show();
            ((System.Windows.Controls.ComboBox)dialog.FindName("GroupCategory")).Text = "Technik";
            Click(dialog, "GroupApplyButton");
            Assert.Equal([("c:1", "Technik")], saved);
            Assert.False(items[0].Marked);
            Assert.Equal(CategorizationState.Open, items[1].State);
            dialog.Close();
        });

    [Fact]
    public void FailedSaveLeavesTheEntryOpen()
        => StaTestRunner.Run(() =>
        {
            var item = Item("c:1");
            var queue = new CategorizationQueue([item], new Dictionary<string, string>());
            var dialog = new CategorizeDialog("Test", "", queue, null, (_, _) => throw new IOException("gesperrt"),
                onlyUncategorized: false, groupFirst: false);
            dialog.Show();
            ((System.Windows.Controls.ComboBox)dialog.FindName("ItemCategory")).Text = "Reisen";
            Click(dialog, "ApplyButton");
            Assert.Equal(CategorizationState.Open, item.State);
            Assert.Empty(queue.Assignments);
            Assert.False(dialog.Changed);
            Assert.Contains("gesperrt", ((System.Windows.Controls.TextBlock)dialog.FindName("Feedback")).Text);
            dialog.Close();
        });

    private static void Click(System.Windows.Window dialog, string name)
        => ((System.Windows.Controls.Button)dialog.FindName(name)).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
}

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ClipwellWin.Services;
using ClipwellWin.Views;
using Xunit;

namespace ClipwellWin.Tests;

public sealed class CategorizeSourceTests
{
    private static CategorizationItem Item(string key)
        => new() { Source = new BrainSource(key, "Titel " + key, "Ein ausreichend langer Text für den Vergleich.", "", DateTime.MinValue), SourceName = "Zwischenablage" };

    private static LocalAiService Service(string selection)
        => new(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), () => selection,
            new E5Categorization((batch, _) => Task.FromResult(batch.Select(_ => new float[] { 1, 0 }).ToArray()), () => true));

    [Fact]
    public void E5IsPreselectedWhenTheKontrollstationIsOffAndMissingGraniteIsDisabled()
        => StaTestRunner.Run(() =>
        {
            using var ai = Service("off");
            var dialog = new CategorizeDialog("Test", "", new CategorizationQueue([Item("c:1")], new Dictionary<string, string>()), ai,
                (_, _) => { }, false, false);
            dialog.Show();
            var box = (System.Windows.Controls.ComboBox)dialog.FindName("SourceBox");
            var items = box.Items.OfType<ComboBoxItem>().ToList();
            Assert.Same(items[0], box.SelectedItem);
            Assert.False(items[1].IsEnabled);
            Assert.Contains("nicht installiert", (string)items[1].Content);
            var resuggest = (System.Windows.Controls.Button)dialog.FindName("ResuggestButton");
            Assert.Equal(Visibility.Visible, resuggest.Visibility);
            box.SelectedItem = items[2];
            Assert.Equal(Visibility.Collapsed, resuggest.Visibility);
            dialog.Close();
        });

    [Fact]
    public void AnalysisResultsAreShownAsAssigned()
        => StaTestRunner.Run(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var items = new[] { Item("c:1"), Item("c:2"), Item("c:3"), Item("c:4") };
            foreach (var i in items) i.Marked = true;
            var queue = new CategorizationQueue(items, new Dictionary<string, string>());
            IReadOnlyList<string>? requested = null;
            var dialog = new CategorizeDialog("Test", "", queue, null, (_, _) => { }, false, false, analyze: (keys, owner) =>
            {
                requested = keys;
                return Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string> { ["c:1"] = "Recherche", ["c:2"] = "Recherche", ["c:3"] = "Technik" });
            });
            dialog.Show();
            var analyze = (System.Windows.Controls.Button)dialog.FindName("GroupAnalyzeButton");
            Assert.Equal(Visibility.Visible, analyze.Visibility);
            analyze.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            var deadline = DateTime.Now.AddSeconds(5);
            while (items[2].State != CategorizationState.Applied && DateTime.Now < deadline)
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
            Assert.Equal(4, requested!.Count);
            Assert.Equal("Recherche", items[0].Category);
            Assert.Equal(CategorizationState.Applied, items[2].State);
            Assert.Equal(CategorizationState.Open, items[3].State);
            Assert.True(items[3].Marked);
            Assert.False(items[0].Marked);
            Assert.True(dialog.Changed);
            dialog.Close();
        });
}

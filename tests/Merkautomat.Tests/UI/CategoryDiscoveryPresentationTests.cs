using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Merkautomat.Services;
using Merkautomat.Views;
using Xunit;

namespace Merkautomat.Tests;

public class CategoryDiscoveryPresentationTests
{
    [Fact]
    public void TallGroupsScrollInSmallPixelStepsInsteadOfJumpingToTheNextGroup()
        => StaTestRunner.Run(() =>
        {
            TestApplication.Ensure();
            var items = Enumerable.Range(1, 8).Select(i => new DiscoveryItem($"c:{i}",
                $"Eintrag {i}", new string('a', 430), DateTime.Now, [1f, 0f])).ToList();
            var window = new CategoryDiscoveryWindow(null, items, [], (_, _) => { },
                prepared: [Proposal(items.Take(4).ToList()), Proposal(items.Skip(4).ToList())]);
            window.Width = 760;
            window.Height = 660;
            try
            {
                window.Show();
                window.UpdateLayout();
                var list = (ListBox)window.FindName("ProposalList");
                var scroll = Descendants<ScrollViewer>(list).First();
                Assert.True(scroll.ScrollableHeight > 100);

                scroll.LineDown();
                window.UpdateLayout();
                Assert.InRange(scroll.VerticalOffset, 1, 99);
                var firstOffset = scroll.VerticalOffset;
                scroll.LineDown();
                window.UpdateLayout();
                Assert.InRange(scroll.VerticalOffset - firstOffset, 1, 99);

                scroll.LineUp();
                window.UpdateLayout();
                Assert.Equal(firstOffset, scroll.VerticalOffset);
            }
            finally { window.Close(); }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreparedGroupsShowReadableContentsAndExpandWithoutRepeatingEntries(bool light)
        => StaTestRunner.Run(() =>
        {
            TestApplication.Ensure();
            var items = Enumerable.Range(1, 5).Select(i => new DiscoveryItem($"c:{i}",
                $"Konkreter Eintrag {i}: Symbole für digitale Sprachnotizen und Einstellungen",
                i == 5 ? "Kurzer vollständiger Inhalt." : $"Inhalt {i}: " + new string('a', 430), DateTime.Now, [1f, 0f])).ToList();
            var proposal = Proposal(items);
            var window = new CategoryDiscoveryWindow(null, items, ["Engineering"], (_, _) => { },
                prepared: [proposal]);
            var foreground = light ? Colors.Black : Colors.White;
            window.Resources["EditorTextBrush"] = new SolidColorBrush(foreground);
            window.Resources["EditorPanelBrush"] = new SolidColorBrush(light ? Colors.White : Colors.Black);
            window.Resources["EditorPanelAltBrush"] = new SolidColorBrush(light ? Colors.White : Colors.Black);
            window.Width = 720;
            try
            {
                window.Show();
                window.UpdateLayout();
                var list = (ListBox)window.FindName("ProposalList");
                var more = Descendants<Expander>(list).Single(e => Equals(e.Header, "Weitere 2 Einträge anzeigen"));
                more.IsExpanded = true;
                window.UpdateLayout();
                foreach (var item in items)
                {
                    var title = Assert.Single(Descendants<TextBlock>(list), t => t.Text == item.Title);
                    Assert.Equal(TextWrapping.Wrap, title.TextWrapping);
                    Assert.Equal(TextTrimming.None, title.TextTrimming);
                    Assert.Equal(foreground, Assert.IsType<SolidColorBrush>(title.Foreground).Color);
                }
                var full = Descendants<Expander>(list).First(e => Equals(e.Header, "Vollständigen Text lesen"));
                var shortText = Descendants<Expander>(list).Single(e => e.DataContext is DiscoveryEntryPreview preview && preview.Text == items[4].Text);
                Assert.Equal(Visibility.Collapsed, shortText.Visibility);
                Assert.Equal(Visibility.Visible, full.Visibility);
                var excerpt = Assert.Single(Descendants<TextBlock>(list), t => t.DataContext == full.DataContext && t.Text == ((DiscoveryEntryPreview)full.DataContext).Excerpt);
                full.IsExpanded = true;
                window.UpdateLayout();
                Assert.Equal(Visibility.Collapsed, excerpt.Visibility);
                Assert.Equal("Text einklappen", full.Header);
                var text = Assert.Single(Descendants<TextBox>(full));
                Assert.Equal(items[0].Text, text.Text);
                Assert.True(text.IsReadOnly);
                Assert.Equal(foreground, Assert.IsType<SolidColorBrush>(text.Foreground).Color);
                full.IsExpanded = false;
                window.UpdateLayout();
                Assert.Equal(Visibility.Visible, excerpt.Visibility);
                Assert.Contains(Descendants<TextBlock>(list), t => t.Text == items[0].Title + " · " + items[1].Title + " · " + items[2].Title);
            }
            finally { window.Close(); }
        });

    [Fact]
    public void MissingEntryKeepsItsOwnTitleAndDoesNotShowAnotherEntriesText()
    {
        var items = new[] { new DiscoveryItem("a", "Titel A", "Text A", DateTime.Now, [1f]) };
        var proposal = Proposal(items);
        proposal.Keys = ["missing", "a"];
        proposal.Titles = ["Alter Titel", "Titel A"];
        proposal.LoadPreviews(items.ToDictionary(i => i.Key));
        Assert.Equal("Alter Titel", proposal.Entries[0].Title);
        Assert.Contains("nicht mehr verfügbar", proposal.Entries[0].Text);
        Assert.Equal("Text A", proposal.Entries[1].Text);
    }

    [Theory]
    [InlineData(359, false)]
    [InlineData(360, false)]
    [InlineData(361, true)]
    public void FullTextIsOfferedOnlyWhenTheExcerptOmitsContent(int length, bool expected)
    {
        var preview = new DiscoveryEntryPreview("Titel", new string('a', length));
        Assert.Equal(expected, preview.HasMoreText);
        if (expected) Assert.EndsWith("…", preview.Excerpt);
        else Assert.Equal(preview.Text, preview.Excerpt);
    }

    [Fact]
    public void ExtraWhitespaceAloneDoesNotOfferTheSameTextAgain()
    {
        var preview = new DiscoveryEntryPreview("Titel", "Eintrag" + new string(' ', 400) + " mit Inhalt");
        Assert.False(preview.HasMoreText);
        Assert.Equal("Eintrag mit Inhalt", preview.Excerpt);
    }

    private static CategoryProposal Proposal(IReadOnlyList<DiscoveryItem> items) => new()
    {
        Keys = items.Select(i => i.Key).ToList(), Titles = items.Select(i => i.Title).ToList(),
        ProposedName = "Engineering", Name = "Engineering", Reason = "Die Einträge behandeln Symbole für digitale Anwendungen.",
        Uncertain = false, IsNew = false, Accepted = true,
    };

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}

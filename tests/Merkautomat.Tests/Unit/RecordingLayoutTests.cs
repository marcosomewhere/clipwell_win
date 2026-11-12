using System.Windows;
using System.Windows.Automation;
using Merkautomat.Services;
using Xunit;

namespace Merkautomat.Tests;

public class RecordingLayoutTests
{
    private static UiNode Node(ControlType type, string name = "", Rect? bounds = null, UiLandmark landmark = UiLandmark.None,
        string? value = null, int row = -1, int col = -1, params UiNode[] children)
    {
        var n = new UiNode
        {
            Type = type, Name = name, Bounds = bounds ?? Rect.Empty, Landmark = landmark, Value = value,
            GridRow = row, GridColumn = col,
        };
        n.Children.AddRange(children);
        return n;
    }

    private static UiNode Text(string name, double x, double y, double w, double h = 18)
        => Node(ControlType.Text, name, new Rect(x, y, w, h));

    private static UiNode Group(Rect bounds, params UiNode[] children)
        => Node(ControlType.Group, "", bounds, children: children);

    private static UiNode Window(params UiNode[] children)
        => Node(ControlType.Window, "Fenster", new Rect(0, 0, 1000, 800), children: children);

    [Fact]
    public void Render_JoinsFragmentsOnTheSameRow()
    {
        var root = Window(Group(new Rect(0, 0, 600, 20),
            Text("Siehe auch", 0, 0, 80),
            Node(ControlType.Hyperlink, "René Kassnell", new Rect(84, 0, 100, 18)),
            Text(", liegt bei IT", 184, 0, 90)));

        Assert.Equal(["Siehe auch René Kassnell, liegt bei IT"], RecordingLayout.Render(root, false, false));
    }

    [Fact]
    public void Render_JoinsWrappedLinesAndKeepsParagraphsApart()
    {
        var root = Window(
            Group(new Rect(0, 0, 400, 44),
                Text("Im April bestellt aber voraussichtlich erst", 0, 0, 380),
                Text("Ende September so weit.", 0, 22, 180)),
            Group(new Rect(0, 80, 400, 20),
                Text("Neuer Absatz", 0, 80, 100)));

        Assert.Equal(
            ["Im April bestellt aber voraussichtlich erst Ende September so weit.", "Neuer Absatz"],
            RecordingLayout.Render(root, false, false));
    }

    [Fact]
    public void Render_JoinsHyphenatedWrapWithoutSpace()
    {
        var root = Window(Group(new Rect(0, 0, 60, 44),
            Text("Kunden-", 0, 0, 58),
            Text("feedback", 0, 22, 58)));

        Assert.Equal(["Kunden-feedback"], RecordingLayout.Render(root, false, false));
    }

    [Fact]
    public void Render_KeepsShortStackedItemsApart()
    {
        var root = Window(Group(new Rect(0, 0, 300, 60),
            Node(ControlType.ListItem, "Product Vision", new Rect(0, 0, 100, 18)),
            Node(ControlType.ListItem, "Roadmaps", new Rect(0, 20, 80, 18))));

        Assert.Equal(["Product Vision", "Roadmaps"], RecordingLayout.Render(root, false, false));
    }

    [Fact]
    public void Render_ContainerNamedAfterItsContentIsWrittenOnce()
    {
        var item = Node(ControlType.ListItem, "• Kunde hatte viele Fragen", new Rect(0, 0, 300, 18),
            children: Text("Kunde hatte viele Fragen", 12, 0, 280));

        Assert.Equal(["Kunde hatte viele Fragen"], RecordingLayout.Render(Window(item), false, false));
    }

    [Fact]
    public void Render_GridCellsBecomeMarkdownTable()
    {
        var table = Node(ControlType.Table, "", new Rect(0, 0, 400, 100), children:
        [
            Node(ControlType.DataItem, "", new Rect(0, 0, 200, 20), row: 0, col: 0, children: Text("Kunde", 0, 0, 50)),
            Node(ControlType.DataItem, "", new Rect(200, 0, 200, 20), row: 0, col: 1, children: Text("Status", 200, 0, 50)),
            Node(ControlType.DataItem, "", new Rect(0, 20, 200, 20), row: 1, col: 0, children: Text("Styria", 0, 20, 50)),
            Node(ControlType.DataItem, "", new Rect(200, 20, 200, 20), row: 1, col: 1, children: Text("A|B", 200, 20, 50)),
        ]);

        Assert.Equal(
            ["| Kunde | Status |", "| --- | --- |", "| Styria | A\\|B |"],
            RecordingLayout.Render(Window(table), false, false));
    }

    [Fact]
    public void Render_BrowserContentOnlyKeepsUrlAndMainLandmark()
    {
        var root = Window(
            Node(ControlType.TabItem, "Anderer Tab", new Rect(0, 0, 100, 20)),
            Node(ControlType.Edit, "Adressleiste", new Rect(100, 30, 500, 20), value: "https://example.org/seite"),
            Node(ControlType.Document, "Seite", new Rect(0, 60, 1000, 740), children:
            [
                Node(ControlType.Group, "Sidebar", new Rect(0, 60, 200, 740), UiLandmark.Navigation, children: Text("Menue", 0, 60, 50)),
                Node(ControlType.Group, "", new Rect(200, 60, 800, 740), UiLandmark.Main, children: Text("Inhalt", 200, 60, 50)),
            ]));

        Assert.Equal(["URL: https://example.org/seite", "Inhalt"], RecordingLayout.Render(root, true, true));
    }

    [Fact]
    public void Render_BrowserWithoutMainSkipsNavigationAndMovesComplementaryToEnd()
    {
        var root = Window(Node(ControlType.Document, "Seite", new Rect(0, 0, 1000, 800), children:
        [
            Node(ControlType.Group, "", new Rect(0, 0, 1000, 40), UiLandmark.Banner, children: Text("Logo", 0, 0, 40)),
            Node(ControlType.Group, "", new Rect(0, 40, 200, 700), UiLandmark.Complementary, children: Text("Werbung", 0, 40, 60)),
            Group(new Rect(200, 40, 800, 700), Text("Artikel", 200, 40, 60)),
            Node(ControlType.Group, "", new Rect(0, 760, 1000, 40), UiLandmark.Navigation, children: Text("Links", 0, 760, 40)),
        ]));

        Assert.Equal(["Artikel", "Werbung"], RecordingLayout.Render(root, true, true));
    }

    [Fact]
    public void Render_AllModeKeepsBrowserChrome()
    {
        var root = Window(
            Node(ControlType.TabItem, "Anderer Tab", new Rect(0, 0, 100, 20)),
            Node(ControlType.Document, "Seite", new Rect(0, 60, 1000, 740), children: Text("Inhalt", 0, 60, 50)));

        Assert.Equal(["Anderer Tab", "Inhalt"], RecordingLayout.Render(root, false, true));
    }

    [Fact]
    public void Render_ContentOnlyDoesNotAffectNonBrowserApps()
    {
        var root = Window(
            Node(ControlType.TabItem, "Posteingang", new Rect(0, 0, 100, 20)),
            Node(ControlType.Document, "Mail", new Rect(0, 60, 1000, 740), children: Text("Hallo", 0, 60, 50)));

        Assert.Equal(["Posteingang", "Hallo"], RecordingLayout.Render(root, true, false));
    }

    [Fact]
    public void Render_SkipsOffscreenAndOutsideWindowText()
    {
        var hidden = new UiNode { Type = ControlType.Text, Name = "Versteckt", Bounds = new Rect(0, 0, 50, 18), IsOffscreen = true };
        var root = Window(hidden, Text("Weit weg", 5000, 5000, 50), Text("Sichtbar", 0, 100, 50));

        Assert.Equal(["Sichtbar"], RecordingLayout.Render(root, false, false));
    }

    [Theory]
    [InlineData("Navigation", UiLandmark.Navigation)]
    [InlineData("Primär", UiLandmark.Main)]
    [InlineData("main", UiLandmark.Main)]
    [InlineData("Überschrift", UiLandmark.Banner)]
    [InlineData("Suchtaste", UiLandmark.Search)]
    [InlineData("Gruppe", UiLandmark.None)]
    public void ClassifyLandmark_ReadsLocalizedChromiumNames(string localized, UiLandmark expected)
        => Assert.Equal(expected, RecordingLayout.ClassifyLandmark(ControlType.Group, localized));

    [Fact]
    public void ClassifyLandmark_IgnoresHeadingText()
        => Assert.Equal(UiLandmark.None, RecordingLayout.ClassifyLandmark(ControlType.Text, "Überschrift"));

    [Fact]
    public void IsBrowserProcess_KnowsCommonBrowsers()
    {
        Assert.True(RecordingLayout.IsBrowserProcess("opera"));
        Assert.True(RecordingLayout.IsBrowserProcess("MSEDGE"));
        Assert.False(RecordingLayout.IsBrowserProcess("OUTLOOK"));
    }
}

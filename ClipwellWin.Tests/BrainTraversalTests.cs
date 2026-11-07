using System.Text.Json;
using ClipwellWin.Models;
using ClipwellWin.Services;
using ClipwellWin.Views;
using Xunit;

namespace ClipwellWin.Tests;

public sealed class BrainTraversalTests
{
    private static BrainGraph Graph(params GraphEdge[] edges) => new(
        Enumerable.Range(0, 5).Select(i => new GraphNode
        {
            Index = i, Item = new SemanticChunk($"c:{i}:0", $"c:{i}", SemanticSource.Clipboard,
                $"Eintrag {i}", DateTime.Today, "Inhalt"), Text = "Inhalt", Vector = [1f, 0f],
        }).ToArray(), edges, []);
    private static GraphEdge Manual(int a, int b) => new(a, b, GraphEdgeKind.Manual, 1, "benötigt") { Directed = true };
    private static readonly HashSet<GraphEdgeKind> ManualOnly = [GraphEdgeKind.Manual];

    [Fact]
    public void Find_RespectsDirectionAndHandlesCycles()
    {
        var graph = Graph(Manual(0, 1), Manual(1, 2), Manual(2, 1));
        Assert.Equal(new[] { 0, 1, 2 }, BrainTraversal.Find(graph, 0, 2, ManualOnly)!.Nodes);
        Assert.Null(BrainTraversal.Find(graph, 2, 0, ManualOnly));
        Assert.Null(BrainTraversal.Find(graph, 0, 4, ManualOnly));
    }

    [Fact]
    public void Find_ChoosesFewestHopsAndExcludesHintsUnlessAllowed()
    {
        var graph = Graph(Manual(0, 1), Manual(1, 2), Manual(2, 3),
            new GraphEdge(0, 3, GraphEdgeKind.Semantic, .99f, "ähnlich"));
        Assert.Equal(3, BrainTraversal.Find(graph, 0, 3, ManualOnly)!.Steps.Count);
        Assert.Single(BrainTraversal.Find(graph, 0, 3, new HashSet<GraphEdgeKind> { GraphEdgeKind.Manual, GraphEdgeKind.Semantic })!.Steps);
    }

    [Fact]
    public void Find_HandlesSameNodeInvalidNodesAndUndirectedSummary()
    {
        var graph = Graph(new GraphEdge(0, 1, GraphEdgeKind.Summary, 1, "Quelle"));
        Assert.Empty(BrainTraversal.Find(graph, 0, 0, ManualOnly)!.Steps);
        Assert.Null(BrainTraversal.Find(graph, -1, 0, ManualOnly));
        Assert.Null(BrainTraversal.Find(graph, 0, 8, ManualOnly));
        Assert.Single(BrainTraversal.Find(graph, 1, 0, new HashSet<GraphEdgeKind> { GraphEdgeKind.Summary })!.Steps);
    }

    [Fact]
    public void Relations_RoundTripAndBuildByStableKeysSkippingMissingEndpoints()
    {
        var relation = new BrainRelation { FromKey = "c:1", ToKey = "c:0", Kind = "benötigt", Evidence = "Absatz 2", Task = "Prüfen", Steps = "Lesen\nTesten" };
        var settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings
        { BrainRelations = [relation, relation with { Id = "missing", ToKey = "c:missing" }] }))!;
        var chunks = Graph().Nodes.Take(2).Select(n => new IndexedChunk(n.Item, n.Vector)).ToArray();
        var graph = KnowledgeGraph.Build(chunks, new BrainOptions { Relations = settings.BrainRelations });
        var edge = Assert.Single(graph.Edges, e => e.Kind == GraphEdgeKind.Manual);
        Assert.Equal("c:1", graph.Nodes[edge.A].ItemKey);
        Assert.Equal("c:0", graph.Nodes[edge.B].ItemKey);
        Assert.True(edge.Directed);
        Assert.Equal(relation, settings.BrainRelations[0]);
    }

    [Fact]
    public void Validate_RequiresDistinctExistingEndpointsAndEvidence()
    {
        HashSet<string> keys = ["a", "b"];
        var valid = new BrainRelation { FromKey = "a", ToKey = "b", Evidence = "Quelle" };
        Assert.Null(BrainTraversal.Validate(valid, keys));
        Assert.NotNull(BrainTraversal.Validate(valid with { ToKey = "a" }, keys));
        Assert.NotNull(BrainTraversal.Validate(valid with { ToKey = "missing" }, keys));
        Assert.NotNull(BrainTraversal.Validate(valid with { Kind = " " }, keys));
        Assert.NotNull(BrainTraversal.Validate(valid with { Evidence = " " }, keys));
    }

    [Fact]
    public void Dialog_CreatesRelationAndFindsOnlyItsForwardPath()
        => StaTestRunner.Run(() =>
        {
            var dialog = new BrainRelationsDialog(Graph(), [], "c:0", ManualOnly);
            var target = (System.Windows.Controls.ComboBox)dialog.FindName("RelationTo");
            target.SelectedIndex = 1;
            ((System.Windows.Controls.TextBox)dialog.FindName("Evidence")).Text = "Absatz 2";
            ((System.Windows.Controls.Button)dialog.FindName("ApplyRelationButton")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Assert.Single(dialog.Relations);
            var pathFrom = (System.Windows.Controls.ComboBox)dialog.FindName("PathFrom");
            var pathTo = (System.Windows.Controls.ComboBox)dialog.FindName("PathTo");
            pathTo.SelectedIndex = 1;
            var find = (System.Windows.Controls.Button)dialog.FindName("FindPathButton");
            find.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            var result = (System.Windows.Controls.TextBox)dialog.FindName("PathResult");
            Assert.Contains("1 Verbindung(en)", result.Text);
            Assert.Contains("Absatz 2", result.Text);
            pathFrom.SelectedIndex = 1;
            pathTo.SelectedIndex = 0;
            find.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Assert.Contains("Kein gerichteter Weg", result.Text);
            dialog.Close();
        });

    [Fact]
    public void Dialog_CanLoadMissingEndpointsWithoutMutatingSettings()
        => StaTestRunner.Run(() =>
        {
            var original = new List<BrainRelation> { new() { FromKey = "c:0", ToKey = "missing", Evidence = "Alt" } };
            var dialog = new BrainRelationsDialog(Graph(), original, "c:0", ManualOnly);
            dialog.Relations.Clear();
            Assert.Single(original);
            dialog.Close();
        });

    [Fact]
    public void Dialog_EditingMissingEndpointPreservesItsKeyAndOriginalRelation()
        => StaTestRunner.Run(() =>
        {
            var original = new BrainRelation { FromKey = "c:0", ToKey = "missing", Evidence = "Alt" };
            var dialog = new BrainRelationsDialog(Graph(), [original], "c:0", ManualOnly, editRelations: true);
            ((System.Windows.Controls.ListBox)dialog.FindName("RelationsList")).SelectedIndex = 0;
            Assert.False(((System.Windows.Controls.Button)dialog.FindName("SaveRelationsButton")).IsEnabled);
            ((System.Windows.Controls.TextBox)dialog.FindName("Evidence")).Text = "Neue Begründung";
            Assert.True(((System.Windows.Controls.Button)dialog.FindName("SaveRelationsButton")).IsEnabled);
            ((System.Windows.Controls.Button)dialog.FindName("ApplyRelationButton")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            var updated = Assert.Single(dialog.Relations);
            Assert.Equal("missing", updated.ToKey);
            Assert.Equal("Neue Begründung", updated.Evidence);
            Assert.Equal("Alt", original.Evidence);
            dialog.Close();
        });

    [Fact]
    public void Dialog_InvalidEditKeepsSelectedRelationWhenAnotherIsClicked()
        => StaTestRunner.Run(() =>
        {
            var first = new BrainRelation { FromKey = "c:0", ToKey = "c:1", Evidence = "Erste" };
            var second = new BrainRelation { FromKey = "c:1", ToKey = "c:2", Evidence = "Zweite" };
            var dialog = new BrainRelationsDialog(Graph(), [first, second], "c:0", ManualOnly);
            var list = (System.Windows.Controls.ListBox)dialog.FindName("RelationsList");
            list.SelectedIndex = 0;
            ((System.Windows.Controls.TextBox)dialog.FindName("Evidence")).Text = "";
            list.SelectedIndex = 1;
            Assert.Equal(first.Id, ((BrainRelationsDialog.RelationChoice)list.SelectedItem).Relation.Id);
            Assert.Equal("", ((System.Windows.Controls.TextBox)dialog.FindName("Evidence")).Text);
            Assert.Equal(first, dialog.Relations[0]);
            dialog.Close();
        });
}

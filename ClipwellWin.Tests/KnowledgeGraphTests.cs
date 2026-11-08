using System.Windows;
using System.Windows.Media.Imaging;
using ClipwellWin.Models;
using ClipwellWin.Services;
using ClipwellWin.Views;
using Xunit;

namespace ClipwellWin.Tests;

public class KnowledgeGraphTests
{
    private static readonly DateTime Day = new(2026, 11, 5, 9, 0, 0);

    private static float[] Unit(params float[] v)
    {
        SemanticRanking.Normalize(v);
        return v;
    }

    private static IndexedChunk Item(string itemKey, SemanticSource source, int minutes, float[] vector,
        string text = "text", string? file = null, string? domain = null)
        => new(new SemanticChunk(itemKey + ":0", itemKey, source, itemKey, Day.AddMinutes(minutes), text, FilePath: file, Domain: domain), vector);

    private static BrainOptions Options => new() { SemanticThreshold = 0.86f, TimeWindow = TimeSpan.FromMinutes(5) };

    private static GraphEdge? EdgeBetween(BrainGraph g, string a, string b, GraphEdgeKind kind)
    {
        var ia = g.Nodes.Single(n => n.ItemKey == a).Index;
        var ib = g.Nodes.Single(n => n.ItemKey == b).Index;
        return g.Edges.FirstOrDefault(e => e.Kind == kind && ((e.A == ia && e.B == ib) || (e.A == ib && e.B == ia)));
    }

    [Fact]
    public void Build_AveragesChunksPerItemAndLinksSimilarItems()
    {
        IndexedChunk[] chunks =
        [
            new(new SemanticChunk("n:a:0", "n:a", SemanticSource.Note, "a", Day, "eins"), Unit(1, 0.1f, 0)),
            new(new SemanticChunk("n:a:1", "n:a", SemanticSource.Note, "a", Day, "zwei"), Unit(1, -0.1f, 0)),
            Item("n:b", SemanticSource.Note, 600, Unit(0.95f, 0.05f, 0)),
            Item("n:c", SemanticSource.Note, 1200, Unit(0, 0, 1)),
        ];

        var g = KnowledgeGraph.Build(chunks, Options);

        Assert.Equal(3, g.Nodes.Count);
        Assert.Equal("eins\nzwei", g.Nodes.Single(n => n.ItemKey == "n:a").Text);
        var semantic = EdgeBetween(g, "n:a", "n:b", GraphEdgeKind.Semantic);
        Assert.NotNull(semantic);
        Assert.StartsWith("inhaltlich ", semantic!.Detail);
        Assert.Null(EdgeBetween(g, "n:a", "n:c", GraphEdgeKind.Semantic));
        Assert.Empty(g.EdgesOf(g.Nodes.Single(n => n.ItemKey == "n:c").Index));
    }

    [Fact]
    public void Build_ChainsRecordingSnapshotsAndLinksCloseItemsInTime()
    {
        var far = Unit(0, 0, 1);
        IndexedChunk[] chunks =
        [
            Item("r:s.md#0", SemanticSource.Recording, 0, Unit(1, 0, 0), file: @"C:\rec\s.md"),
            Item("r:s.md#1", SemanticSource.Recording, 1, Unit(0, 1, 0), file: @"C:\rec\s.md"),
            Item("r:s.md#2", SemanticSource.Recording, 2, Unit(0.5f, 0.5f, 0.7f), file: @"C:\rec\s.md"),
            Item("c:1", SemanticSource.Clipboard, 3, far),
            Item("c:2", SemanticSource.Clipboard, 30, Unit(0, 0.7f, -0.7f)),
        ];

        var g = KnowledgeGraph.Build(chunks, Options);

        Assert.NotNull(EdgeBetween(g, "r:s.md#0", "r:s.md#1", GraphEdgeKind.Session));
        Assert.NotNull(EdgeBetween(g, "r:s.md#1", "r:s.md#2", GraphEdgeKind.Session));
        Assert.Null(EdgeBetween(g, "r:s.md#0", "r:s.md#2", GraphEdgeKind.Session));
        Assert.Null(EdgeBetween(g, "r:s.md#0", "r:s.md#1", GraphEdgeKind.Time));
        var time = EdgeBetween(g, "r:s.md#2", "c:1", GraphEdgeKind.Time);
        Assert.NotNull(time);
        Assert.Equal("zeitlich nah (1 min)", time!.Detail);
        Assert.Empty(g.EdgesOf(g.Nodes.Single(n => n.ItemKey == "c:2").Index));
    }

    [Fact]
    public void Build_ChainsItemsOfTheSameWebsite()
    {
        IndexedChunk[] chunks =
        [
            Item("c:1", SemanticSource.Clipboard, 0, Unit(1, 0, 0), domain: "example.org"),
            Item("c:2", SemanticSource.Clipboard, 100, Unit(0, 1, 0), domain: "example.org"),
            Item("c:3", SemanticSource.Clipboard, 200, Unit(0, 0, 1), domain: "example.org"),
        ];

        var g = KnowledgeGraph.Build(chunks, Options);

        Assert.Equal("gleiche Webseite example.org", EdgeBetween(g, "c:1", "c:2", GraphEdgeKind.Domain)!.Detail);
        Assert.NotNull(EdgeBetween(g, "c:2", "c:3", GraphEdgeKind.Domain));
        Assert.Null(EdgeBetween(g, "c:1", "c:3", GraphEdgeKind.Domain));
    }

    [Fact]
    public void Build_RespectsSourceFilterAndNodeLimitNewestFirst()
    {
        IndexedChunk[] chunks =
        [
            Item("c:1", SemanticSource.Clipboard, 0, Unit(1, 0, 0)),
            Item("n:1", SemanticSource.Note, 10, Unit(1, 0, 0)),
            Item("n:2", SemanticSource.Note, 20, Unit(1, 0, 0)),
            Item("n:3", SemanticSource.Note, 30, Unit(1, 0, 0)),
        ];

        var g = KnowledgeGraph.Build(chunks, Options with { Sources = new HashSet<SemanticSource> { SemanticSource.Note }, MaxNodes = 2 });

        Assert.Equal(["n:3", "n:2"], g.Nodes.Select(n => n.ItemKey));
    }

    [Fact]
    public void Cluster_GroupsByContentAndNamesTopicsByDistinctiveWords()
    {
        var vpn = Unit(1, 0.05f, 0);
        var cake = Unit(0, 0.05f, 1);
        IndexedChunk[] chunks =
        [
            Item("n:1", SemanticSource.Note, 0, vpn, "Zertifikat für das Firmen VPN erneuern"),
            Item("n:2", SemanticSource.Note, 100, Unit(1, 0.1f, 0), "VPN Zertifikat im Portal anfordern"),
            Item("n:3", SemanticSource.Note, 200, Unit(1, 0, 0.05f), "Neues Zertifikat in den VPN Client importieren"),
            Item("n:4", SemanticSource.Note, 300, cake, "Apfelkuchen Rezept mit Zimt"),
            Item("n:5", SemanticSource.Note, 400, Unit(0.05f, 0, 1), "Zimt und Apfelkuchen für Sonntag"),
            Item("n:6", SemanticSource.Note, 500, Unit(0, 1, 0), "Einzelner Gedanke ohne Bezug"),
        ];

        var g = KnowledgeGraph.Build(chunks, Options);

        Assert.Equal(2, g.Clusters.Count);
        var vpnTopic = g.Clusters[0];
        Assert.Equal(3, vpnTopic.Members.Count);
        Assert.Contains("zertifikat", vpnTopic.Keywords);
        Assert.Contains("Zertifikat", vpnTopic.Label);
        Assert.Contains("apfelkuchen", g.Clusters[1].Keywords);
        Assert.Equal(-1, g.Nodes.Single(n => n.ItemKey == "n:6").Cluster);
        Assert.Equal(g.Nodes.Single(n => n.ItemKey == "n:1").Cluster, g.Nodes.Single(n => n.ItemKey == "n:3").Cluster);
    }

    [Fact]
    public void Build_RanksNeighborsOnCenteredVectorsSoACommonDirectionDoesNotMergeTopics()
    {
        // Every pair is above the raw threshold, and strong-common items of different topics are rawly closer
        // than weak-common items of the same topic; only after removing the shared direction do topics separate.
        var chunks = new List<IndexedChunk>();
        for (var i = 0; i < 8; i++)
        {
            var common = i % 2 == 0 ? 3f : 1f;
            chunks.Add(Item($"n:a{i}", SemanticSource.Note, i * 100, Unit(common, 0.35f, 0), "Zertifikat VPN"));
            chunks.Add(Item($"n:b{i}", SemanticSource.Note, 5000 + i * 100, Unit(common, 0, 0.35f), "Apfelkuchen Zimt"));
        }

        var g = KnowledgeGraph.Build(chunks, Options);

        var semantic = g.Edges.Where(e => e.Kind == GraphEdgeKind.Semantic).ToList();
        Assert.NotEmpty(semantic);
        Assert.All(semantic, e => Assert.Equal(g.Nodes[e.A].ItemKey[..3], g.Nodes[e.B].ItemKey[..3]));
        Assert.Equal(2, g.Clusters.Count);
    }

    [Fact]
    public void Neighbors_MergesReasonsAndNeighborhoodFollowsHops()
    {
        var v = Unit(1, 0, 0);
        IndexedChunk[] chunks =
        [
            Item("c:1", SemanticSource.Clipboard, 0, v, domain: "x.org"),
            Item("c:2", SemanticSource.Clipboard, 1, v, domain: "x.org"),
            Item("c:3", SemanticSource.Clipboard, 300, Unit(0, 1, 0), domain: "x.org"),
        ];

        var g = KnowledgeGraph.Build(chunks, Options);
        var first = g.Nodes.Single(n => n.ItemKey == "c:1").Index;
        var (node, edges) = g.Neighbors(first)[0];

        Assert.Equal("c:2", g.Nodes[node].ItemKey);
        Assert.Equal([GraphEdgeKind.Semantic, GraphEdgeKind.Time, GraphEdgeKind.Domain], edges.Select(e => e.Kind).Order());
        Assert.Equal(2, g.Neighborhood(first, 1).Count);
        Assert.Equal(3, g.Neighborhood(first, 2).Count);
    }

    [Fact]
    public void ForceLayout_IsDeterministicAndPullsLinkedNodesTogether()
    {
        var chunks = new List<IndexedChunk>();
        for (var i = 0; i < 12; i++)
            chunks.Add(Item($"n:a{i}", SemanticSource.Note, i * 100, Unit(1, 0.01f * i, 0)));
        for (var i = 0; i < 12; i++)
            chunks.Add(Item($"n:b{i}", SemanticSource.Note, 5000 + i * 100, Unit(0, 0.01f * i, 1)));
        var g = KnowledgeGraph.Build(chunks, Options);

        var first = new ForceLayout(g);
        first.Run();
        var second = new ForceLayout(g);
        second.Run();

        Assert.Equal(first.X, second.X);
        Assert.All(first.X.Concat(first.Y), v => Assert.True(double.IsFinite(v)));
        double Dist(int a, int b) => Math.Sqrt(Math.Pow(first.X[a] - first.X[b], 2) + Math.Pow(first.Y[a] - first.Y[b], 2));
        var a = g.Nodes.Where(n => n.ItemKey.StartsWith("n:a")).Select(n => n.Index).ToList();
        var b = g.Nodes.Where(n => n.ItemKey.StartsWith("n:b")).Select(n => n.Index).ToList();
        var within = a.SelectMany(x => a.Where(y => y != x).Select(y => Dist(x, y))).Average();
        var between = a.SelectMany(x => b.Select(y => Dist(x, y))).Average();
        Assert.True(within * 1.5 < between, $"within {within:0}, between {between:0}");
    }

    [Fact]
    public void ForceLayout_KeepsKnownPositions()
    {
        IndexedChunk[] chunks = [Item("n:1", SemanticSource.Note, 0, Unit(1, 0, 0)), Item("n:2", SemanticSource.Note, 1, Unit(0, 1, 0))];
        var g = KnowledgeGraph.Build(chunks, Options);
        var layout = new ForceLayout(g, new Dictionary<string, (double, double)> { ["n:1"] = (500, -200), ["n:2"] = (520, -180) });

        Assert.Equal(500, layout.X[g.Nodes.Single(n => n.ItemKey == "n:1").Index]);
    }

    [Theory]
    [InlineData("https://www.example.org/a?b=c", "example.org")]
    [InlineData("http://Docs.Microsoft.com", "docs.microsoft.com")]
    [InlineData("file:///C:/x.txt", null)]
    [InlineData("kein link", null)]
    public void DomainOf_ReturnsHostWithoutWww(string url, string? expected)
        => Assert.Equal(expected, SemanticChunk.DomainOf(url));

    [Fact]
    public void Chunker_TakesDomainsFromUrlEntriesSourceUrlAndRecordingAddress()
    {
        var clips = SemanticChunker.FromClipboard(
        [
            new ClipboardEntry { Id = 1, Type = EntryType.Url, Content = "https://www.heise.de/news", Timestamp = Day },
            new ClipboardEntry { Id = 2, Type = EntryType.Text, Content = "Text aus einer Webseite", SourceUrl = "https://golem.de/x", Timestamp = Day },
        ]).ToList();
        Assert.Equal(["heise.de", "golem.de"], clips.Select(c => c.Domain));

        var md = RecordingMarkdown.SessionHeader(Day)
            + RecordingMarkdown.FormatSnapshot(Day.AddSeconds(2), "Edge", "Artikel", ["URL: https://www.msn.com/de-de/x", "Hunderasse passt zu dir"], null, null);
        Assert.Equal("msn.com", Assert.Single(SemanticChunker.FromRecording(@"C:\r\a.md", md)).Domain);
    }

    [Fact]
    public void BrainCanvas_RendersGraphWithSelectionAndHighlight()
    {
        StaTestRunner.Run(() =>
        {
            if (Application.Current == null) _ = new Application();
            var chunks = new List<IndexedChunk>();
            for (var i = 0; i < 30; i++)
            {
                var source = (SemanticSource)(i % 3);
                chunks.Add(Item($"x:{i}", source, i, Unit(1, 0.02f * (i % 5), i % 2), $"Eintrag {i} Thema", file: source == SemanticSource.Recording ? "r.md" : null));
            }
            var g = KnowledgeGraph.Build(chunks, Options);
            var layout = new ForceLayout(g);
            layout.Run();

            var canvas = new BrainCanvas { Width = 800, Height = 600 };
            canvas.SetGraph(g, layout.X, layout.Y, keepView: false);
            canvas.Measure(new Size(800, 600));
            canvas.Arrange(new Rect(0, 0, 800, 600));
            canvas.Select(0);
            canvas.SetHighlight(g.Neighborhood(0, 1));
            canvas.VisibleEdgeKinds.Remove(GraphEdgeKind.Time);

            var bitmap = new RenderTargetBitmap(800, 600, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(canvas);
            Assert.Equal(0, canvas.SelectedNode);
            Assert.True(g.Edges.Count > 0);
        });
    }
}

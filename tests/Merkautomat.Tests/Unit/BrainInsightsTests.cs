using System.Text.Json;
using Merkautomat.Models;
using Merkautomat.Services;
using Xunit;

namespace Merkautomat.Tests;

public class BrainInsightsTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 12, 0, 0);

    private static GraphNode Node(int index, string key, double daysAgo, SemanticSource source = SemanticSource.Clipboard, long entryId = 0, string? file = null)
        => new()
        {
            Index = index,
            Item = new SemanticChunk(key + ":0", key, source, "Titel " + key, Now.AddDays(-daysAgo), "text", entryId, file),
            Text = "text",
            Vector = [1f, 0f],
        };

    private static GraphEdge Semantic(int a, int b, float weight = 0.9f) => new(a, b, GraphEdgeKind.Semantic, weight, "inhaltlich");

    /// <summary>Groups are given as collections, which also sets the node clusters.</summary>
    private static BrainGraph Graph(List<GraphNode> nodes, List<GraphEdge> edges, params string[][] groups)
    {
        var collections = groups.Select((keys, i) => new BrainCollection { Id = "c" + i, Name = "Gruppe " + i, Keys = [.. keys] }).ToList();
        return new BrainGraph(nodes, edges, BrainCollections.Clusters(nodes, collections));
    }

    // ---- Collections -----------------------------------------------------------------------------------------

    [Fact]
    public void Collections_SuggestWellLinkedItemsButNotRejectedOrAlreadyFiled()
    {
        var nodes = Enumerable.Range(0, 6).Select(i => Node(i, "c:" + i, i)).ToList();
        var edges = new List<GraphEdge>
        {
            Semantic(0, 1), Semantic(1, 2),
            Semantic(3, 0), Semantic(3, 1),          // two links into the collection
            Semantic(4, 2), new(4, 0, GraphEdgeKind.Time, 1, "zeitlich"), // its only content link goes there
            Semantic(5, 2), Semantic(5, 3),          // one of two content links: no
        };
        var collection = new BrainCollection { Id = "m", Name = "Mappe", Keys = ["c:0", "c:1", "c:2"] };
        var graph = new BrainGraph(nodes, edges, []);

        var suggested = BrainCollections.Suggest(graph, [collection]).Select(s => graph.Nodes[s.Node].ItemKey).ToList();
        Assert.Equal(["c:3", "c:4"], suggested);

        collection.Rejected.Add("c:3");
        Assert.Equal(["c:4"], BrainCollections.Suggest(graph, [collection]).Select(s => graph.Nodes[s.Node].ItemKey));

        var other = new BrainCollection { Id = "x", Name = "Andere", Keys = ["c:4"] };
        Assert.DoesNotContain(BrainCollections.Suggest(graph, [collection, other]), s => s.CollectionId == "m");
    }

    [Fact]
    public void Collections_UpdateRejectsRemovedMembersAndKeepsUncheckedSuggestionsOpen()
    {
        var c = BrainCollections.Create("  Outlook   Add-in ", ["a", "b", "outside"], Now);
        Assert.Equal("Outlook Add-in", c.Name);

        BrainCollections.Update(c, "Outlook", removed: ["b"], added: ["d"]);

        Assert.Equal(["a", "outside", "d"], c.Keys);
        Assert.Equal(["b"], c.Rejected);
        Assert.Throws<ArgumentException>(() => BrainCollections.Update(c, "   ", [], []));
        Assert.Throws<ArgumentException>(() => BrainCollections.Create("", ["a"], Now));
    }

    [Fact]
    public void Collections_ViewKeepsEmptyCollectionsAndColorsSharedItemsByFirst()
    {
        var nodes = Enumerable.Range(0, 3).Select(i => Node(i, "c:" + i, i)).ToList();
        var clusters = BrainCollections.Clusters(nodes, [
            new BrainCollection { Id = "a", Name = "A", Keys = ["c:0", "c:1"] },
            new BrainCollection { Id = "b", Name = "B", Keys = ["c:1", "c:2"] },
            new BrainCollection { Id = "e", Name = "Leer", Keys = ["c:99"] },
        ]);

        Assert.Equal(3, clusters.Count);
        Assert.Equal([1, 2], clusters[1].Members);
        Assert.Empty(clusters[2].Members);
        Assert.Equal("e", clusters[2].CollectionId);
        Assert.Equal([0, 0, 1], nodes.Select(n => n.Cluster));
    }

    [Fact]
    public void Collections_GroupingOptionUsesCollectionsInsteadOfTopics()
    {
        var chunks = new[]
        {
            new IndexedChunk(new SemanticChunk("c:1:0", "c:1", SemanticSource.Clipboard, "eins", Now, "eins"), [1f, 0f]),
            new IndexedChunk(new SemanticChunk("c:2:0", "c:2", SemanticSource.Clipboard, "zwei", Now.AddHours(-2), "zwei"), [1f, 0f]),
        };
        var graph = KnowledgeGraph.Build(chunks, new BrainOptions
        {
            GroupByCollection = true, GroupByCategory = true,
            Collections = [new BrainCollection { Id = "m", Name = "Meine Mappe", Keys = ["c:2"] }],
        });

        var cluster = Assert.Single(graph.Clusters);
        Assert.Equal("Meine Mappe", cluster.Label);
        Assert.DoesNotContain(graph.Edges, e => e.Kind == GraphEdgeKind.Category);
        Assert.Equal(-1, graph.Nodes.Single(n => n.ItemKey == "c:1").Cluster);
    }

    [Fact]
    public void Collections_SurviveSettingsRoundTripAndOldFilesHaveNone()
    {
        var settings = new AppSettings { BrainGroupByCollection = true };
        settings.BrainCollections.Add(new BrainCollection { Name = "M", Keys = ["c:1"], Rejected = ["c:2"], Created = Now });

        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.True(back.BrainGroupByCollection);
        Assert.Equal(["c:2"], Assert.Single(back.BrainCollections).Rejected);

        var old = JsonSerializer.Deserialize<AppSettings>("""{ "BrainGroupByCategory": true }""")!;
        Assert.Empty(old.BrainCollections);
        Assert.False(old.BrainGroupByCollection);
    }

    [Fact]
    public void Collections_ForgetDeletedItems()
    {
        var c = new BrainCollection { Keys = ["a", "b"], Rejected = ["b", "c"] };
        BrainCollections.Forget([c], new HashSet<string> { "b" });
        Assert.Equal(["a"], c.Keys);
        Assert.Equal(["c"], c.Rejected);
    }

    // ---- Review ----------------------------------------------------------------------------------------------

    [Fact]
    public void Review_ClassifiesNewGrowingActiveAndRestingWithOpenTasks()
    {
        var nodes = new List<GraphNode>
        {
            Node(0, "c:0", 1), Node(1, "c:1", 3),                                         // new
            Node(2, "c:2", 1), Node(3, "c:3", 2), Node(4, "c:4", 10), Node(5, "c:5", 40), // growing
            Node(6, "c:6", 2), Node(7, "c:7", 9), Node(8, "c:8", 10),                     // active
            Node(9, "n:a.md", 30, SemanticSource.Note, file: @"C:\notes\a.md"), Node(10, "c:10", 35, entryId: 10), // resting with task
            Node(11, "c:11", 12), Node(12, "c:12", 13),                                   // quiet: not listed
        };
        var graph = Graph(nodes, [],
            ["c:0", "c:1"], ["c:2", "c:3", "c:4", "c:5"], ["c:6", "c:7", "c:8"], ["n:a.md", "c:10"], ["c:11", "c:12"]);
        var tasks = new[]
        {
            new KbTask { Title = "offen", SourceKey = "clip:10" },
            new KbTask { Title = "auch offen", SourceKey = @"c:\NOTES\a.md" },
            new KbTask { Title = "erledigt", SourceKey = "clip:10", Done = true },
        };

        var review = BrainInsights.Review(graph, tasks, Now);

        Assert.Equal([ReviewKind.New, ReviewKind.Growing, ReviewKind.Active, ReviewKind.Resting], review.Topics.Select(t => t.Kind));
        Assert.Equal([0, 1, 2, 3], review.Topics.Select(t => t.Cluster));
        Assert.Equal(2, review.Topics[3].OpenTasks);
        Assert.Contains("2 offene Aufgaben", review.Topics[3].Text);
        Assert.Equal(5, review.RecentItems);
        Assert.Equal(3, review.ActiveTopics);
        Assert.StartsWith("In den letzten 7 Tagen: 5 neue Einträge in 3 Gruppen", review.Headline);
    }

    [Fact]
    public void Review_ListsRestingGroupsWithoutTasksOnlyFromThreeItems()
    {
        var nodes = new List<GraphNode> { Node(0, "a", 30), Node(1, "b", 31), Node(2, "c", 40), Node(3, "d", 50), Node(4, "e", 60) };
        var graph = Graph(nodes, [], ["a", "b", "c"], ["d", "e"]);

        var review = BrainInsights.Review(graph, [], Now);

        var resting = Assert.Single(review.Topics);
        Assert.Equal(0, resting.Cluster);
        Assert.Equal(ReviewKind.Resting, resting.Kind);
        Assert.Equal("In den letzten 7 Tagen ist nichts Neues im Netz dazugekommen.", review.Headline);
    }

    // ---- Discoveries -----------------------------------------------------------------------------------------

    [Fact]
    public void Discover_FindsForgottenBridgesNearDuplicatesAndIslands()
    {
        var nodes = new List<GraphNode>
        {
            Node(0, "neu", 0.5), Node(1, "alt", 60),             // forgotten: old item linked to today
            Node(2, "a1", 20), Node(3, "a2", 20),                // group A
            Node(4, "b1", 20), Node(5, "b2", 20),                // group B
            Node(6, "bruecke", 20),                               // links A and B
            Node(7, "kopie1", 18), Node(8, "kopie2", 18),        // near duplicates
            Node(9, "insel", 25),                                // only a time link
        };
        var edges = new List<GraphEdge>
        {
            Semantic(0, 1, 0.91f),
            Semantic(2, 3), Semantic(4, 5), Semantic(6, 2), Semantic(6, 4),
            Semantic(7, 8, 0.985f),
            new(9, 2, GraphEdgeKind.Time, 1, "zeitlich"),
        };
        var graph = Graph(nodes, edges, ["a1", "a2"], ["b1", "b2"]);

        var found = BrainInsights.Discover(graph, Now);

        var forgotten = Assert.Single(found, d => d.Kind == DiscoveryKind.Forgotten);
        Assert.Equal([1, 0], forgotten.Nodes);
        Assert.Contains("91 %", forgotten.Detail);
        var bridge = Assert.Single(found, d => d.Kind == DiscoveryKind.Bridge && d.Nodes[0] == 6);
        Assert.Equal("verbindet Gruppe 0 und Gruppe 1", bridge.Detail);
        Assert.Equal([7, 8], Assert.Single(found, d => d.Kind == DiscoveryKind.NearDuplicates).Nodes);
        var islands = Assert.Single(found, d => d.Kind == DiscoveryKind.Islands);
        Assert.Equal([9], islands.Nodes);
        Assert.Equal([DiscoveryKind.Forgotten, DiscoveryKind.Bridge, DiscoveryKind.NearDuplicates, DiscoveryKind.Islands],
            found.Select(d => d.Kind).Distinct());
    }

    [Fact]
    public void Discover_AnchorsOnNewestItemAfterAPause()
    {
        var nodes = new List<GraphNode> { Node(0, "zuletzt", 100), Node(1, "frueher", 200) };
        var graph = new BrainGraph(nodes, [Semantic(0, 1)], []);

        var forgotten = Assert.Single(BrainInsights.Discover(graph, Now), d => d.Kind == DiscoveryKind.Forgotten);
        Assert.Equal([1, 0], forgotten.Nodes);
    }

    [Fact]
    public void Discover_EmptyGraphFindsNothing()
        => Assert.Empty(BrainInsights.Discover(new BrainGraph([], [], []), Now));

    [Fact]
    public void TaskKey_MatchesKeysOfTasksCreatedFromEntriesAndFiles()
    {
        Assert.Equal("clip:42", BrainInsights.TaskKey(new SemanticChunk("c:42:0", "c:42", SemanticSource.Clipboard, "", Now, "", 42)));
        Assert.Equal(@"C:\n.md", BrainInsights.TaskKey(new SemanticChunk("n:n.md:0", "n:n.md", SemanticSource.Note, "", Now, "", 0, @"C:\n.md")));
    }

    // ---- Open points -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("- [ ] Angebot an Kunde schicken", "Angebot an Kunde schicken")]
    [InlineData("1. [ ] Zertifikat erneuern", "Zertifikat erneuern")]
    [InlineData("TODO: Tests ergänzen", "Tests ergänzen")]
    [InlineData("// todo Logging prüfen", null)]
    [InlineData("TODO Logging prüfen", "Logging prüfen")]
    [InlineData("> FIXME - Timeout zu kurz", "Timeout zu kurz")]
    [InlineData("Offen: Lizenz klären", "Lizenz klären")]
    [InlineData("Nächster Schritt: Termin mit IT", "Termin mit IT")]
    [InlineData("Naechster Schritt - Termin mit IT", "Termin mit IT")]
    [InlineData("- [x] erledigt", null)]
    [InlineData("Offene Fragen zum Projekt", null)]
    [InlineData("Aufgabenliste für morgen", null)]
    [InlineData("TODO:", null)]
    public void OpenPoints_MatchOnlyExplicitMarkers(string line, string? expected)
        => Assert.Equal(expected, BrainOpenPoints.Match(line));

    [Fact]
    public void OpenPoints_SkipExistingTasksAndDuplicatesAndKeepContext()
    {
        var sources = new[]
        {
            new BrainSource("c:1", "Notiz eins", "Einleitung\n- [ ] Server neu starten\n  danach Log prüfen\n\nAnderes", "", Now),
            new BrainSource("c:2", "Notiz zwei", "TODO: server neu starten.\nTODO: Bericht schreiben", "", Now),
        };

        var points = BrainOpenPoints.Find(sources, ["Bericht schreiben"]);

        var point = Assert.Single(points);
        Assert.Equal("Server neu starten", point.Title);
        Assert.Equal("c:1", point.ItemKey);
        Assert.Equal("- [ ] Server neu starten\n  danach Log prüfen", point.Context);

        var task = BrainOpenPoints.ToTask(point, "clip:1", Now);
        Assert.Equal("Server neu starten", task.Title);
        Assert.Equal("clip:1", task.SourceKey);
        Assert.Equal("Notiz eins", task.SourceTitle);
        Assert.Equal(Now, task.Created);
    }
}

using System.Globalization;
using ClipwellWin.Models;

namespace ClipwellWin.Services;

/// <summary>Why two knowledge items are linked.</summary>
public enum GraphEdgeKind { Semantic, Time, Session, Domain, Summary, Manual, Category }

public sealed class GraphNode
{
    public required int Index { get; init; }
    /// <summary>First chunk of the item; carries key, source, title, date, file and entry id.</summary>
    public required SemanticChunk Item { get; init; }
    /// <summary>All chunk texts of the item, joined.</summary>
    public required string Text { get; init; }
    /// <summary>Normalized mean of the chunk vectors.</summary>
    public required float[] Vector { get; init; }
    public int Cluster { get; internal set; } = -1;
    public int Degree { get; internal set; }

    public string ItemKey => Item.ItemKey;
    public SemanticSource Source => Item.Source;
}

public sealed record GraphEdge(int A, int B, GraphEdgeKind Kind, float Weight, string Detail)
{
    public bool Directed { get; init; }
    public int Other(int node) => node == A ? B : A;
}

public sealed record GraphCluster(int Id, string Label, IReadOnlyList<string> Keywords, IReadOnlyList<int> Members)
{
    public string Category { get; init; } = "";
    /// <summary>Category view only: the subcategories of this main category with their members.</summary>
    public IReadOnlyList<GraphSubgroup> Subgroups { get; init; } = [];
}

public sealed record GraphSubgroup(string Name, IReadOnlyList<int> Members);

public sealed record BrainOptions
{
    public float SemanticThreshold { get; init; } = 0.86f;
    public int SemanticNeighbors { get; init; } = 5;
    public TimeSpan TimeWindow { get; init; } = TimeSpan.FromMinutes(5);
    public int TimeNeighbors { get; init; } = 3;
    public int MaxNodes { get; init; } = 1000;
    public IReadOnlySet<SemanticSource>? Sources { get; init; }
    public IReadOnlyDictionary<string, BrainAnnotation>? Annotations { get; init; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? SummarySources { get; init; }
    public IReadOnlyList<BrainRelation>? Relations { get; init; }
    /// <summary>Clusters are the saved main categories instead of automatic topics.</summary>
    public bool GroupByCategory { get; init; }
}

/// <summary>The knowledge network: items as nodes, typed links as edges, topics as clusters.</summary>
public sealed class BrainGraph
{
    private readonly List<GraphEdge>[] _adjacency;

    public BrainGraph(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphEdge> edges, IReadOnlyList<GraphCluster> clusters)
    {
        Nodes = nodes;
        Edges = edges;
        Clusters = clusters;
        _adjacency = new List<GraphEdge>[nodes.Count];
        for (var i = 0; i < nodes.Count; i++) _adjacency[i] = [];
        foreach (var e in edges)
        {
            _adjacency[e.A].Add(e);
            _adjacency[e.B].Add(e);
        }
    }

    public IReadOnlyList<GraphNode> Nodes { get; }
    public IReadOnlyList<GraphEdge> Edges { get; }
    public IReadOnlyList<GraphCluster> Clusters { get; }

    public IReadOnlyList<GraphEdge> EdgesOf(int node) => _adjacency[node];

    public GraphCluster? ClusterOf(int node)
    {
        var id = Nodes[node].Cluster;
        return id >= 0 && id < Clusters.Count ? Clusters[id] : null;
    }

    /// <summary>Neighbors with all reasons that link them, strongest first.</summary>
    public List<(int Node, List<GraphEdge> Edges)> Neighbors(int node)
        => _adjacency[node]
            .GroupBy(e => e.Other(node))
            .Select(g => (g.Key, g.OrderByDescending(e => e.Weight).ToList()))
            .OrderByDescending(x => x.Item2.Max(e => Strength(e)))
            .ToList();

    /// <summary>Nodes reachable within <paramref name="hops"/> links, including the start node.</summary>
    public HashSet<int> Neighborhood(int node, int hops)
    {
        var seen = new HashSet<int> { node };
        var frontier = new List<int> { node };
        for (var h = 0; h < hops && frontier.Count > 0; h++)
        {
            var next = new List<int>();
            foreach (var n in frontier)
                foreach (var e in _adjacency[n])
                    if (seen.Add(e.Other(n))) next.Add(e.Other(n));
            frontier = next;
        }
        return seen;
    }

    /// <summary>Comparable strength across link kinds, 0..1.</summary>
    public static float Strength(GraphEdge e) => e.Kind switch
    {
        GraphEdgeKind.Semantic => e.Weight,
        GraphEdgeKind.Session => 0.9f,
        GraphEdgeKind.Time => 0.7f + 0.2f * e.Weight,
        GraphEdgeKind.Summary => 1f,
        GraphEdgeKind.Manual => 1f,
        GraphEdgeKind.Category => 0.8f,
        _ => 0.75f,
    };
}

/// <summary>Builds the knowledge network from the semantic index.</summary>
public static class KnowledgeGraph
{
    private const int MaxTextChars = 4000;
    private const int LabelPropagationRounds = 20;

    public static string KindName(GraphEdgeKind kind) => kind switch
    {
        GraphEdgeKind.Semantic => "inhaltlich",
        GraphEdgeKind.Time => "zeitlich",
        GraphEdgeKind.Session => "gleiche Aufnahme",
        GraphEdgeKind.Summary => "Zusammenfassung",
        GraphEdgeKind.Manual => "manuell gepflegt",
        GraphEdgeKind.Category => "gleiche Kategorie",
        _ => "gleiche Webseite",
    };

    public static BrainGraph Build(IReadOnlyList<IndexedChunk> chunks, BrainOptions options)
    {
        var nodes = BuildNodes(chunks, options);
        var edges = new List<GraphEdge>();
        var seen = new HashSet<(int, int, GraphEdgeKind)>();

        void Add(int a, int b, GraphEdgeKind kind, float weight, string detail)
        {
            if (a == b) return;
            var key = (Math.Min(a, b), Math.Max(a, b), kind);
            if (seen.Add(key)) edges.Add(new GraphEdge(key.Item1, key.Item2, kind, weight, detail));
        }

        AddSemanticEdges(nodes, options, Add);
        AddSessionEdges(nodes, Add);
        AddTimeEdges(nodes, options, Add);
        AddDomainEdges(nodes, Add);
        var paths = nodes.Select(n => KnowledgeCategories.Effective(options.Annotations, n.ItemKey)).ToArray();
        // Only in the category view: these links pull categories together, which would distort the topic layout.
        if (options.GroupByCategory) AddCategoryEdges(nodes, paths, Add);
        if (options.SummarySources != null)
        {
            var byKey = nodes.ToDictionary(n => n.ItemKey, n => n.Index);
            foreach (var (summary, sources) in options.SummarySources)
                if (byKey.TryGetValue(summary, out var target))
                    foreach (var key in sources)
                        if (byKey.TryGetValue(key, out var source))
                            Add(target, source, GraphEdgeKind.Summary, 1f, "Zusammenfassung dieser Quelle");
        }

        if (options.Relations != null)
        {
            var byKey = nodes.ToDictionary(n => n.ItemKey, n => n.Index);
            foreach (var relation in options.Relations)
                if (byKey.TryGetValue(relation.FromKey, out var from) && byKey.TryGetValue(relation.ToKey, out var to)
                    && from != to && !string.IsNullOrWhiteSpace(relation.Kind))
                    edges.Add(new GraphEdge(from, to, GraphEdgeKind.Manual, 1f,
                        $"manuell: {nodes[from].Item.Title} → {relation.Kind} → {nodes[to].Item.Title}") { Directed = true });
        }

        foreach (var e in edges)
        {
            nodes[e.A].Degree++;
            nodes[e.B].Degree++;
        }
        if (options.GroupByCategory) return new BrainGraph(nodes, edges, CategoryClusters(nodes, paths));
        var clusters = Cluster(nodes, edges);
        if (options.Annotations is { } annotations)
        {
            for (var i = 0; i < clusters.Count; i++)
            {
                var c = clusters[i];
                var saved = c.Members.Select(m => annotations.GetValueOrDefault(nodes[m].ItemKey))
                    .Where(a => a != null).Cast<BrainAnnotation>().ToList();
                string MostCommon(IEnumerable<string> values) => values.Where(v => !string.IsNullOrWhiteSpace(v))
                    .GroupBy(v => v, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key).FirstOrDefault() ?? "";
                var name = MostCommon(saved.Select(a => a.Name));
                clusters[i] = c with { Label = name.Length > 0 ? name : c.Label, Category = MostCommon(c.Members.Select(m => paths[m].Main)) };
            }
        }
        return new BrainGraph(nodes, edges, clusters);
    }

    private static List<GraphNode> BuildNodes(IReadOnlyList<IndexedChunk> chunks, BrainOptions options)
    {
        var groups = chunks
            .Where(c => options.Sources == null || options.Sources.Contains(c.Chunk.Source))
            .GroupBy(c => c.Chunk.ItemKey)
            .Select(g => g.OrderBy(c => ChunkOrdinal(c.Chunk.Key)).ToList())
            .OrderByDescending(g => g[0].Chunk.Date)
            .ThenBy(g => g[0].Chunk.ItemKey, StringComparer.Ordinal)
            .Take(options.MaxNodes)
            .ToList();

        var nodes = new List<GraphNode>(groups.Count);
        foreach (var g in groups)
        {
            var dims = g[0].Vector.Length;
            var mean = new float[dims];
            foreach (var c in g)
                for (var k = 0; k < dims && k < c.Vector.Length; k++) mean[k] += c.Vector[k];
            SemanticRanking.Normalize(mean);
            var text = string.Join("\n", g.Select(c => c.Chunk.Text));
            if (text.Length > MaxTextChars) text = text[..MaxTextChars];
            nodes.Add(new GraphNode { Index = nodes.Count, Item = g[0].Chunk, Text = text, Vector = mean });
        }
        return nodes;
    }

    private static int ChunkOrdinal(string key)
    {
        var colon = key.LastIndexOf(':');
        return colon >= 0 && int.TryParse(key.AsSpan(colon + 1), out var n) ? n : 0;
    }

    /// <summary>
    /// The raw cosine must reach the threshold; among those candidates neighbors are ranked on centered vectors and
    /// only (loosely) mutual k-nearest neighbors are linked, plus each node's single best neighbor so nobody is orphaned.
    /// E5 places almost all texts at a raw cosine of 0.8 to 0.9, so without this everything chains into one topic.
    /// </summary>
    private static void AddSemanticEdges(List<GraphNode> nodes, BrainOptions options, Action<int, int, GraphEdgeKind, float, string> add)
    {
        var n = nodes.Count;
        var centered = CenteredVectors(nodes);
        var candidates = new List<(int Node, float Score, float Sim)>[n];
        for (var i = 0; i < n; i++) candidates[i] = [];
        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                var sim = SemanticRanking.Dot(nodes[i].Vector, nodes[j].Vector);
                if (sim < options.SemanticThreshold) continue;
                var score = SemanticRanking.Dot(centered[i], centered[j]);
                candidates[i].Add((j, score, sim));
                candidates[j].Add((i, score, sim));
            }
        }
        var ranked = candidates.Select(c => c.OrderByDescending(x => x.Score).ThenBy(x => x.Node).ToList()).ToArray();
        // Mutual within 2k on the other side: a hub is in many top-k lists but keeps only its own 2k, while a dense
        // group of near-copies stays well connected instead of thinning out to a chain.
        var wide = ranked.Select(r => r.Take(2 * options.SemanticNeighbors).Select(x => x.Node).ToHashSet()).ToArray();
        for (var i = 0; i < n; i++)
        {
            for (var r = 0; r < Math.Min(ranked[i].Count, options.SemanticNeighbors); r++)
            {
                var (j, _, sim) = ranked[i][r];
                if (r == 0 || wide[j].Contains(i))
                    add(i, j, GraphEdgeKind.Semantic, sim, $"inhaltlich {sim * 100:0} %");
            }
        }
    }

    private const int MinNodesForCentering = 8;

    /// <summary>Node vectors minus their mean, normalized; with few nodes the mean says nothing, so raw vectors stay.</summary>
    private static float[][] CenteredVectors(List<GraphNode> nodes)
    {
        if (nodes.Count < MinNodesForCentering) return nodes.Select(x => x.Vector).ToArray();
        var dims = nodes[0].Vector.Length;
        var mean = new float[dims];
        foreach (var node in nodes)
            for (var k = 0; k < dims && k < node.Vector.Length; k++) mean[k] += node.Vector[k];
        for (var k = 0; k < dims; k++) mean[k] /= nodes.Count;
        return nodes.Select(node =>
        {
            var v = new float[dims];
            for (var k = 0; k < dims && k < node.Vector.Length; k++) v[k] = node.Vector[k] - mean[k];
            if (SemanticRanking.Dot(v, v) < 1e-8f) return node.Vector;
            SemanticRanking.Normalize(v);
            return v;
        }).ToArray();
    }

    /// <summary>Consecutive snapshots of one recording form a chain.</summary>
    private static void AddSessionEdges(List<GraphNode> nodes, Action<int, int, GraphEdgeKind, float, string> add)
    {
        foreach (var session in nodes.Where(x => x.Source == SemanticSource.Recording && x.Item.FilePath != null)
                     .GroupBy(x => x.Item.FilePath!, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = session.OrderBy(x => x.Item.Date).ToList();
            for (var i = 1; i < ordered.Count; i++)
                add(ordered[i - 1].Index, ordered[i].Index, GraphEdgeKind.Session, 1f, "gleiche Bildschirmaufnahme");
        }
    }

    /// <summary>Items created close together in time, except snapshots of the same recording.</summary>
    private static void AddTimeEdges(List<GraphNode> nodes, BrainOptions options, Action<int, int, GraphEdgeKind, float, string> add)
    {
        var ordered = nodes.OrderBy(x => x.Item.Date).ToList();
        var candidates = new List<(int Node, TimeSpan Gap)>[nodes.Count];
        for (var i = 0; i < nodes.Count; i++) candidates[i] = [];
        for (var i = 0; i < ordered.Count; i++)
        {
            for (var j = i + 1; j < ordered.Count; j++)
            {
                var gap = ordered[j].Item.Date - ordered[i].Item.Date;
                if (gap > options.TimeWindow) break;
                if (SameRecording(ordered[i], ordered[j])) continue;
                candidates[ordered[i].Index].Add((ordered[j].Index, gap));
                candidates[ordered[j].Index].Add((ordered[i].Index, gap));
            }
        }
        var window = Math.Max(1, options.TimeWindow.TotalSeconds);
        for (var i = 0; i < nodes.Count; i++)
        {
            foreach (var (j, gap) in candidates[i].OrderBy(x => x.Gap).Take(options.TimeNeighbors))
            {
                var weight = (float)Math.Clamp(1 - gap.TotalSeconds / window, 0.1, 1);
                add(i, j, GraphEdgeKind.Time, weight, "zeitlich nah (" + FormatGap(gap) + ")");
            }
        }
    }

    private static bool SameRecording(GraphNode a, GraphNode b)
        => a.Source == SemanticSource.Recording && b.Source == SemanticSource.Recording
           && string.Equals(a.Item.FilePath, b.Item.FilePath, StringComparison.OrdinalIgnoreCase);

    private static string FormatGap(TimeSpan gap)
        => gap.TotalSeconds < 60 ? $"{Math.Max(1, (int)gap.TotalSeconds)} s" : $"{(int)Math.Round(gap.TotalMinutes)} min";

    /// <summary>Items from the same website, chained in time order so a busy domain does not become a clique.</summary>
    private static void AddDomainEdges(List<GraphNode> nodes, Action<int, int, GraphEdgeKind, float, string> add)
    {
        foreach (var site in nodes.Where(x => x.Item.Domain != null).GroupBy(x => x.Item.Domain!, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = site.OrderBy(x => x.Item.Date).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                if (SameRecording(ordered[i - 1], ordered[i])) continue;
                add(ordered[i - 1].Index, ordered[i].Index, GraphEdgeKind.Domain, 1f, "gleiche Webseite " + site.Key);
            }
        }
    }

    /// <summary>
    /// Items of one category path are chained in time order (no cliques); the first item of every subcategory is
    /// linked to the first item of the main category, so subcategories sit inside their main category.
    /// </summary>
    private static void AddCategoryEdges(List<GraphNode> nodes, CategoryPath[] paths, Action<int, int, GraphEdgeKind, float, string> add)
    {
        foreach (var main in Enumerable.Range(0, nodes.Count).Where(i => !paths[i].IsEmpty)
                     .GroupBy(i => paths[i].Main, StringComparer.OrdinalIgnoreCase))
        {
            var heads = new List<int>();
            foreach (var group in main.GroupBy(i => paths[i].Sub, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key.Length > 0))
            {
                var ordered = group.OrderBy(i => nodes[i].Item.Date).ThenBy(i => i).ToList();
                var label = paths[ordered[0]].ToString();
                for (var k = 1; k < ordered.Count; k++)
                    add(ordered[k - 1], ordered[k], GraphEdgeKind.Category, 1f, "gleiche Kategorie " + label);
                heads.Add(ordered[0]);
            }
            for (var k = 1; k < heads.Count; k++)
                add(heads[0], heads[k], GraphEdgeKind.Category, 0.6f, "gleiche Hauptkategorie " + main.Key);
        }
    }

    /// <summary>Category view: one cluster per saved main category (largest first); items without category stay unassigned.</summary>
    public static List<GraphCluster> CategoryClusters(IReadOnlyList<GraphNode> nodes, IReadOnlyList<CategoryPath> paths)
    {
        foreach (var node in nodes) node.Cluster = -1;
        var groups = Enumerable.Range(0, nodes.Count).Where(i => !paths[i].IsEmpty)
            .GroupBy(i => paths[i].Main, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var clusters = new List<GraphCluster>();
        foreach (var g in groups)
        {
            var id = clusters.Count;
            var members = g.ToList();
            foreach (var m in members) nodes[m].Cluster = id;
            var subgroups = members.Where(m => paths[m].HasSub).GroupBy(m => paths[m].Sub, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(s => s.Count()).ThenBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
                .Select(s => new GraphSubgroup(Spelling(s.Select(m => paths[m].Sub)), s.ToList())).ToList();
            var name = Spelling(members.Select(m => paths[m].Main));
            clusters.Add(new GraphCluster(id, name, subgroups.Select(s => s.Name).ToList(), members) { Category = name, Subgroups = subgroups });
        }
        return clusters;
    }

    /// <summary>Most frequent spelling of a name that is compared case-insensitively.</summary>
    private static string Spelling(IEnumerable<string> names) => names.GroupBy(n => n, StringComparer.Ordinal)
        .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).First().Key;

    // ---- Topics ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Label propagation over content links (and recording chains, weaker), so topics follow meaning rather than
    /// time. Clusters of a single node stay unassigned.
    /// </summary>
    public static List<GraphCluster> Cluster(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphEdge> edges)
    {
        var n = nodes.Count;
        var adjacency = new List<(int Node, float Weight)>[n];
        for (var i = 0; i < n; i++) adjacency[i] = [];
        foreach (var e in edges)
        {
            var w = e.Kind switch
            {
                GraphEdgeKind.Semantic => e.Weight,
                GraphEdgeKind.Session => 0.5f,
                GraphEdgeKind.Summary => 0.9f,
                _ => 0f,
            };
            if (w <= 0) continue;
            adjacency[e.A].Add((e.B, w));
            adjacency[e.B].Add((e.A, w));
        }

        var labels = Enumerable.Range(0, n).ToArray();
        var order = Enumerable.Range(0, n).OrderByDescending(i => adjacency[i].Count).ThenBy(i => i).ToArray();
        for (var round = 0; round < LabelPropagationRounds; round++)
        {
            var changed = false;
            foreach (var i in order)
            {
                if (adjacency[i].Count == 0) continue;
                var scores = new Dictionary<int, float>();
                foreach (var (j, w) in adjacency[i]) scores[labels[j]] = scores.GetValueOrDefault(labels[j]) + w;
                var best = scores.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
                if (best == labels[i]) continue;
                labels[i] = best;
                changed = true;
            }
            if (!changed) break;
        }

        var groups = Enumerable.Range(0, n)
            .GroupBy(i => labels[i])
            .Where(g => g.Count() >= 2)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Min())
            .Select(g => g.ToList())
            .ToList();

        var documentTerms = nodes.Select(x => TopicTerms(x.Source == SemanticSource.Recording
            ? string.Join('\n', x.Text.Split('\n').Where(line => line.Trim() != x.Item.Title.Trim()))
            : x.Item.Title + "\n" + x.Text)).ToList();
        // Copies of the same content must not outvote independent sources when naming a topic.
        var uniqueTexts = nodes.Select(x => string.Join(' ', SemanticRanking.Terms(x.Text))).ToArray();
        foreach (var group in Enumerable.Range(0, n).GroupBy(i => uniqueTexts[i]).Where(g => g.Count() > 1))
            foreach (var duplicate in group.Skip(1)) documentTerms[duplicate] = [];
        var df = new Dictionary<string, int>();
        foreach (var terms in documentTerms)
            foreach (var t in terms) df[t] = df.GetValueOrDefault(t) + 1;

        foreach (var node in nodes) node.Cluster = -1;
        var clusters = new List<GraphCluster>();
        foreach (var members in groups)
        {
            var id = clusters.Count;
            foreach (var m in members) nodes[m].Cluster = id;
            var keywords = Keywords(members, documentTerms, df, n);
            var label = keywords.Count == 0 ? $"Thema {id + 1}" : string.Join(" · ", keywords.Select(Capitalize));
            clusters.Add(new GraphCluster(id, label, keywords, members));
        }
        return clusters;
    }

    /// <summary>Terms that are frequent in the cluster and rare elsewhere (document frequency based TF-IDF).</summary>
    private static List<string> Keywords(List<int> members, List<HashSet<string>> documentTerms, Dictionary<string, int> df, int total)
    {
        var inCluster = new Dictionary<string, int>();
        foreach (var m in members)
            foreach (var t in documentTerms[m]) inCluster[t] = inCluster.GetValueOrDefault(t) + 1;
        var documents = members.Count(m => documentTerms[m].Count > 0);
        var minCount = documents >= 3 ? 2 : 1;
        return inCluster
            .Where(kv => kv.Value >= minCount)
            .Select(kv => (Term: kv.Key, Score: (double)kv.Value / members.Count * Math.Log((total + 1.0) / (df[kv.Key] + 0.5))))
            .OrderByDescending(x => x.Score).ThenBy(x => x.Term, StringComparer.Ordinal)
            .Take(3)
            .Select(x => x.Term)
            .ToList();
    }

    private static readonly HashSet<string> TopicNoise = new(StringComparer.OrdinalIgnoreCase)
    {
        "strg", "ctrl", "shift", "alt", "enter", "esc", "escape", "click", "klicken", "öffnen", "oeffnen",
        "schließen", "schliessen", "auswählen", "auswaehlen", "speichern", "kopieren", "einfügen", "einfuegen",
        "neueste", "gepinnt", "dashboard", "gesamtübersicht", "zwischenablage", "sprechwerk", "ideenkombinat",
        "merkautomat", "clipwell", "datei", "bearbeiten", "ansicht", "menü", "menu", "fenster", "text", "bild",
        "eigentlich", "einfach", "gerade", "irgendwie", "natürlich", "vielleicht", "schon", "noch", "dann",
        "quelle", "quellen", "kernaussagen", "kategorie", "originalaussagen", "ausgewählte", "markierten",
        "zusammenfassung", "gesamtaussage", "eintrag", "einträge", "eintraege", "formulierte",
        "keine", "keinen", "löschen", "loeschen", "zurücksetzen", "zuruecksetzen", "abbrechen",
    };

    private static HashSet<string> TopicTerms(string text)
        => SemanticRanking.Terms(text)
            .Where(t => t.Length >= 4 && !TopicNoise.Contains(t) && !t.All(char.IsDigit) && !t.StartsWith("http", StringComparison.Ordinal))
            .Take(400)
            .ToHashSet();

    private static string Capitalize(string term)
        => term.Length == 0 ? term : char.ToUpper(term[0], CultureInfo.InvariantCulture) + term[1..];
}

/// <summary>
/// Force-directed layout (Fruchterman-Reingold): nodes repel each other, links pull, a weak gravity keeps the
/// picture together. Deterministic for a given graph and start positions.
/// </summary>
public sealed class ForceLayout
{
    private readonly BrainGraph _graph;
    private readonly double _k;
    private readonly int _maxIterations;
    private double _temperature;
    private readonly double _cooling;

    public ForceLayout(BrainGraph graph, IReadOnlyDictionary<string, (double X, double Y)>? previous = null, int seed = 42)
    {
        _graph = graph;
        var n = Math.Max(1, graph.Nodes.Count);
        Size = Math.Sqrt(n) * 110;
        _k = Size / Math.Sqrt(n) * 0.9;
        X = new double[graph.Nodes.Count];
        Y = new double[graph.Nodes.Count];

        var random = new Random(seed);
        var clusterCount = Math.Max(1, graph.Clusters.Count);
        var reused = 0;
        for (var i = 0; i < graph.Nodes.Count; i++)
        {
            if (previous != null && previous.TryGetValue(graph.Nodes[i].ItemKey, out var p))
            {
                X[i] = p.X;
                Y[i] = p.Y;
                reused++;
                continue;
            }
            var cluster = graph.Nodes[i].Cluster;
            double cx = 0, cy = 0;
            if (cluster >= 0)
            {
                var angle = 2 * Math.PI * cluster / clusterCount;
                cx = Math.Cos(angle) * Size * 0.3;
                cy = Math.Sin(angle) * Size * 0.3;
            }
            X[i] = cx + (random.NextDouble() - 0.5) * Size * (cluster >= 0 ? 0.15 : 0.8);
            Y[i] = cy + (random.NextDouble() - 0.5) * Size * (cluster >= 0 ? 0.15 : 0.8);
        }

        // Mostly known positions: only settle the newcomers and the changed links.
        var warm = graph.Nodes.Count > 0 && reused >= graph.Nodes.Count * 0.7;
        _maxIterations = warm ? 120 : 320;
        _temperature = warm ? Size / 40 : Size / 8;
        _cooling = _temperature / (_maxIterations + 1);
    }

    public double[] X { get; }
    public double[] Y { get; }
    public double Size { get; }
    public int Iteration { get; private set; }
    public bool IsDone => Iteration >= _maxIterations || _graph.Nodes.Count < 2;

    public void Run(CancellationToken ct = default)
    {
        while (!IsDone)
        {
            ct.ThrowIfCancellationRequested();
            Step();
        }
    }

    public void Step()
    {
        if (IsDone) return;
        var n = _graph.Nodes.Count;
        var dx = new double[n];
        var dy = new double[n];
        var k2 = _k * _k;
        var cutoff2 = (3 * _k) * (3 * _k);

        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                var vx = X[i] - X[j];
                var vy = Y[i] - Y[j];
                var d2 = vx * vx + vy * vy;
                if (d2 > cutoff2) continue;
                if (d2 < 0.01)
                {
                    vx = 0.1 * ((i * 7919 + j) % 3 - 1) + 0.05;
                    vy = 0.1 * ((i * 104729 + j) % 3 - 1) + 0.05;
                    d2 = vx * vx + vy * vy;
                }
                var f = k2 / d2;
                dx[i] += vx * f; dy[i] += vy * f;
                dx[j] -= vx * f; dy[j] -= vy * f;
            }
        }

        foreach (var e in _graph.Edges)
        {
            var pull = e.Kind switch
            {
                GraphEdgeKind.Semantic => 0.4 + e.Weight,
                GraphEdgeKind.Session => 0.8,
                GraphEdgeKind.Time => 0.25,
                GraphEdgeKind.Category => 0.5,
                _ => 0.2,
            };
            var vx = X[e.A] - X[e.B];
            var vy = Y[e.A] - Y[e.B];
            var d = Math.Sqrt(vx * vx + vy * vy) + 0.01;
            var f = d / _k * pull;
            dx[e.A] -= vx * f; dy[e.A] -= vy * f;
            dx[e.B] += vx * f; dy[e.B] += vy * f;
        }

        for (var i = 0; i < n; i++)
        {
            dx[i] -= X[i] * 0.03;
            dy[i] -= Y[i] * 0.03;
            var len = Math.Sqrt(dx[i] * dx[i] + dy[i] * dy[i]);
            if (len < 1e-9) continue;
            var step = Math.Min(len, _temperature);
            X[i] += dx[i] / len * step;
            Y[i] += dy[i] / len * step;
        }

        _temperature = Math.Max(_k * 0.02, _temperature - _cooling);
        Iteration++;
    }
}

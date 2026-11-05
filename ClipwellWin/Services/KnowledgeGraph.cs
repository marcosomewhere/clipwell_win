using System.Globalization;

namespace ClipwellWin.Services;

/// <summary>Why two knowledge items are linked.</summary>
public enum GraphEdgeKind { Semantic, Time, Session, Domain }

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
    public int Other(int node) => node == A ? B : A;
}

public sealed record GraphCluster(int Id, string Label, IReadOnlyList<string> Keywords, IReadOnlyList<int> Members);

public sealed record BrainOptions
{
    public float SemanticThreshold { get; init; } = 0.86f;
    public int SemanticNeighbors { get; init; } = 5;
    public TimeSpan TimeWindow { get; init; } = TimeSpan.FromMinutes(5);
    public int TimeNeighbors { get; init; } = 3;
    public int MaxNodes { get; init; } = 1000;
    public IReadOnlySet<SemanticSource>? Sources { get; init; }
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

        foreach (var e in edges)
        {
            nodes[e.A].Degree++;
            nodes[e.B].Degree++;
        }
        var clusters = Cluster(nodes, edges);
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

    /// <summary>Each node links to its strongest neighbors above the threshold (union, so hubs can exceed k).</summary>
    private static void AddSemanticEdges(List<GraphNode> nodes, BrainOptions options, Action<int, int, GraphEdgeKind, float, string> add)
    {
        var n = nodes.Count;
        var best = new List<(int Node, float Sim)>[n];
        for (var i = 0; i < n; i++) best[i] = [];
        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                var sim = SemanticRanking.Dot(nodes[i].Vector, nodes[j].Vector);
                if (sim < options.SemanticThreshold) continue;
                best[i].Add((j, sim));
                best[j].Add((i, sim));
            }
        }
        for (var i = 0; i < n; i++)
            foreach (var (j, sim) in best[i].OrderByDescending(x => x.Sim).Take(options.SemanticNeighbors))
                add(i, j, GraphEdgeKind.Semantic, sim, $"inhaltlich {sim * 100:0} %");
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

        var documentTerms = nodes.Select(x => TopicTerms(x.Item.Title + "\n" + x.Text)).ToList();
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
        var minCount = members.Count >= 3 ? 2 : 1;
        return inCluster
            .Where(kv => kv.Value >= minCount)
            .Select(kv => (Term: kv.Key, Score: (double)kv.Value / members.Count * Math.Log((total + 1.0) / (df[kv.Key] + 0.5))))
            .OrderByDescending(x => x.Score).ThenBy(x => x.Term, StringComparer.Ordinal)
            .Take(3)
            .Select(x => x.Term)
            .ToList();
    }

    private static HashSet<string> TopicTerms(string text)
        => SemanticRanking.Terms(text)
            .Where(t => t.Length >= 4 && !t.All(char.IsDigit) && !t.StartsWith("http", StringComparison.Ordinal))
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

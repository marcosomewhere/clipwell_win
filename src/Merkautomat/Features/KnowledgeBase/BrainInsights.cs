using System.Globalization;

namespace Merkautomat.Services;

public enum ReviewKind { New, Growing, Active, Resting }

/// <summary>How one topic (or category or collection) developed recently.</summary>
public sealed record TopicReview(int Cluster, string Label, ReviewKind Kind, int Recent, int Previous, int Total,
    DateTime LastActivity, int OpenTasks, string Text);

public sealed record BrainReviewResult(int RecentItems, int ActiveTopics, IReadOnlyList<TopicReview> Topics)
{
    public string Headline => RecentItems == 0
        ? "In den letzten 7 Tagen ist nichts Neues im Netz dazugekommen."
        : $"In den letzten 7 Tagen: {RecentItems} {(RecentItems == 1 ? "neuer Eintrag" : "neue Einträge")}"
          + (ActiveTopics > 0 ? $" in {ActiveTopics} {(ActiveTopics == 1 ? "Gruppe" : "Gruppen")}" : "");
}

public enum DiscoveryKind { Forgotten, Bridge, NearDuplicates, Islands }

/// <summary>Something the net shows that a search does not: <see cref="Nodes"/> are highlighted together.</summary>
public sealed record BrainDiscovery(DiscoveryKind Kind, IReadOnlyList<int> Nodes, string Title, string Detail);

/// <summary>Answers from the knowledge net: what developed recently and what is worth a second look.</summary>
public static class BrainInsights
{
    public static readonly TimeSpan Week = TimeSpan.FromDays(7);
    public static readonly TimeSpan RestingAfter = TimeSpan.FromDays(21);
    public static readonly TimeSpan RecentWindow = TimeSpan.FromDays(3);
    public static readonly TimeSpan ForgottenAfter = TimeSpan.FromDays(30);
    public static readonly TimeSpan IslandAfter = TimeSpan.FromDays(14);
    public const float NearDuplicateSimilarity = 0.97f;
    public const int MaxRestingWithoutTasks = 5;

    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>The <see cref="KbTask.SourceKey"/> a task created from this item carries.</summary>
    public static string? TaskKey(SemanticChunk item)
        => item.Source == SemanticSource.Clipboard ? $"clip:{item.EntryId}" : item.FilePath;

    // ---- Review ----------------------------------------------------------------------------------------------

    /// <summary>
    /// New: at least two items this week and nothing older than two weeks. Growing: at least two this week and more
    /// than the week before. Active: something this week. Resting: nothing for three weeks, listed when tasks are
    /// still open or (at most <see cref="MaxRestingWithoutTasks"/>) when it has at least three items.
    /// </summary>
    public static BrainReviewResult Review(BrainGraph graph, IEnumerable<KbTask> tasks, DateTime now)
    {
        var openTasks = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var task in tasks.Where(t => !t.Done && !string.IsNullOrEmpty(t.SourceKey)))
        {
            if (!openTasks.TryGetValue(task.SourceKey!, out var list)) openTasks[task.SourceKey!] = list = [];
            list.Add(task.Id);
        }
        var weekStart = now - Week;
        var previousStart = now - 2 * Week;
        var reviews = new List<TopicReview>();
        foreach (var cluster in graph.Clusters.Where(c => c.Members.Count > 0))
        {
            var dates = cluster.Members.Select(m => graph.Nodes[m].Item.Date).ToList();
            var recent = dates.Count(d => d > weekStart && d <= now);
            var previous = dates.Count(d => d > previousStart && d <= weekStart);
            var last = dates.Max();
            var taskCount = cluster.Members.Select(m => TaskKey(graph.Nodes[m].Item)).Where(k => k != null)
                .SelectMany(k => openTasks.GetValueOrDefault(k!) ?? []).Distinct().Count();
            ReviewKind? kind = recent >= 2 && dates.Min() > previousStart ? ReviewKind.New
                : recent >= 2 && recent > previous ? ReviewKind.Growing
                : recent >= 1 ? ReviewKind.Active
                : last <= now - RestingAfter && (taskCount > 0 || dates.Count >= 3) ? ReviewKind.Resting
                : null;
            if (kind == null) continue;
            reviews.Add(new TopicReview(cluster.Id, cluster.Label, kind.Value, recent, previous, dates.Count, last, taskCount,
                Describe(kind.Value, recent, previous, dates.Min(), last, taskCount)));
        }
        var ordered = reviews.Where(r => r.Kind != ReviewKind.Resting)
            .OrderBy(r => r.Kind).ThenByDescending(r => r.Recent).ThenByDescending(r => r.LastActivity).ThenBy(r => r.Cluster)
            .Concat(reviews.Where(r => r.Kind == ReviewKind.Resting && r.OpenTasks > 0)
                .OrderByDescending(r => r.OpenTasks).ThenByDescending(r => r.LastActivity).ThenBy(r => r.Cluster))
            .Concat(reviews.Where(r => r.Kind == ReviewKind.Resting && r.OpenTasks == 0)
                .OrderByDescending(r => r.Total).ThenByDescending(r => r.LastActivity).ThenBy(r => r.Cluster).Take(MaxRestingWithoutTasks))
            .ToList();
        var recentItems = graph.Nodes.Count(n => n.Item.Date > weekStart && n.Item.Date <= now);
        return new BrainReviewResult(recentItems, reviews.Count(r => r.Recent > 0), ordered);
    }

    public static string KindName(ReviewKind kind) => kind switch
    {
        ReviewKind.New => "Neu",
        ReviewKind.Growing => "Wächst",
        ReviewKind.Active => "Aktiv",
        _ => "Ruht",
    };

    private static string Describe(ReviewKind kind, int recent, int previous, DateTime first, DateTime last, int tasks)
    {
        var text = kind switch
        {
            ReviewKind.New => $"Neu seit {first.ToString("dd.MM.", De)} · {recent} Einträge diese Woche",
            ReviewKind.Growing => $"Wächst · {recent} Einträge diese Woche, {previous} in der Woche davor",
            ReviewKind.Active => $"Aktiv · {Count(recent, "Eintrag", "Einträge")} diese Woche",
            _ => $"Ruht seit {last.ToString("dd.MM.yyyy", De)}",
        };
        return tasks > 0 ? text + $" · {Count(tasks, "offene Aufgabe", "offene Aufgaben")}" : text;
    }

    private static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    // ---- Discoveries -----------------------------------------------------------------------------------------

    /// <summary>
    /// Forgotten: old items with a content link to something from the last days. Bridges: items whose content links
    /// reach two or more groups. Near-duplicates: groups of almost identical items. Islands: older items without any
    /// link except time. "Now" is the newest item when nothing was added recently, so a pause does not empty the list.
    /// </summary>
    public static List<BrainDiscovery> Discover(BrainGraph graph, DateTime now, int perKind = 8)
    {
        var result = new List<BrainDiscovery>();
        if (graph.Nodes.Count == 0) return result;
        var newest = graph.Nodes.Max(n => n.Item.Date);
        var anchor = newest < now ? newest : now;
        var nodes = graph.Nodes;

        var forgotten = graph.Edges
            .Where(e => e.Kind == GraphEdgeKind.Semantic)
            .Select(e => nodes[e.A].Item.Date <= nodes[e.B].Item.Date ? (Old: e.A, New: e.B, e.Weight) : (Old: e.B, New: e.A, e.Weight))
            .Where(x => nodes[x.New].Item.Date > anchor - RecentWindow && nodes[x.Old].Item.Date < anchor - ForgottenAfter)
            .GroupBy(x => x.Old)
            .Select(g => g.OrderByDescending(x => x.Weight).ThenBy(x => x.New).First())
            .OrderByDescending(x => x.Weight).ThenBy(x => x.Old)
            .Take(perKind);
        foreach (var (old, recent, weight) in forgotten)
            result.Add(new BrainDiscovery(DiscoveryKind.Forgotten, [old, recent], nodes[old].Item.Title,
                $"vom {nodes[old].Item.Date.ToString("dd.MM.yyyy", De)} · passt zu \"{Short(nodes[recent].Item.Title)}\" "
                + $"vom {nodes[recent].Item.Date.ToString("dd.MM.", De)} ({weight * 100:0} %)"));

        var bridges = new List<(int Node, List<int> Clusters, List<int> Others)>();
        for (var i = 0; i < nodes.Count; i++)
        {
            var own = nodes[i].Cluster;
            var others = graph.EdgesOf(i).Where(e => e.Kind is GraphEdgeKind.Semantic or GraphEdgeKind.Manual)
                .Select(e => e.Other(i)).Where(o => nodes[o].Cluster >= 0 && nodes[o].Cluster != own).Distinct().ToList();
            var clusters = others.Select(o => nodes[o].Cluster).Distinct().ToList();
            if (own >= 0) clusters.Insert(0, own);
            if (others.Count > 0 && clusters.Count >= 2) bridges.Add((i, clusters, others));
        }
        foreach (var (node, clusters, others) in bridges.OrderByDescending(b => b.Clusters.Count).ThenByDescending(b => b.Others.Count)
                     .ThenByDescending(b => nodes[b.Node].Item.Date).ThenBy(b => b.Node).Take(perKind))
            result.Add(new BrainDiscovery(DiscoveryKind.Bridge, [node, .. others], nodes[node].Item.Title,
                "verbindet " + JoinAnd(clusters.Take(3).Select(c => c < graph.Clusters.Count ? Short(graph.Clusters[c].Label) : "?"))
                + (clusters.Count > 3 ? $" und {clusters.Count - 3} weitere" : "")));

        foreach (var group in NearDuplicateGroups(graph).Take(perKind))
        {
            var newestMember = group.OrderByDescending(m => nodes[m].Item.Date).ThenBy(m => m).First();
            result.Add(new BrainDiscovery(DiscoveryKind.NearDuplicates, group, nodes[newestMember].Item.Title,
                $"{group.Count} fast gleiche Einträge · " + string.Join(", ", group.GroupBy(m => nodes[m].Source)
                    .OrderByDescending(g => g.Count()).Select(g => $"{g.Count()} {SemanticChunk.SourceName(g.Key)}"))));
        }

        var islands = Enumerable.Range(0, nodes.Count)
            .Where(i => nodes[i].Item.Date < anchor - IslandAfter && graph.EdgesOf(i).All(e => e.Kind == GraphEdgeKind.Time))
            .OrderBy(i => nodes[i].Item.Date).ThenBy(i => i).ToList();
        if (islands.Count > 0)
            result.Add(new BrainDiscovery(DiscoveryKind.Islands, islands,
                $"{Count(islands.Count, "Eintrag", "Einträge")} ohne inhaltlichen Bezug",
                $"älter als {IslandAfter.TotalDays:0} Tage · zum Aufräumen oder Einordnen"));
        return result;
    }

    public static string KindName(DiscoveryKind kind) => kind switch
    {
        DiscoveryKind.Forgotten => "Wiederentdeckt",
        DiscoveryKind.Bridge => "Brücken zwischen Themen",
        DiscoveryKind.NearDuplicates => "Fast gleiche Einträge",
        _ => "Ohne Bezug",
    };

    /// <summary>Connected groups over content links of at least <see cref="NearDuplicateSimilarity"/>, largest first.</summary>
    private static List<List<int>> NearDuplicateGroups(BrainGraph graph)
    {
        var parent = Enumerable.Range(0, graph.Nodes.Count).ToArray();
        int Find(int x)
        {
            while (parent[x] != x) x = parent[x] = parent[parent[x]];
            return x;
        }
        foreach (var e in graph.Edges.Where(e => e.Kind == GraphEdgeKind.Semantic && e.Weight >= NearDuplicateSimilarity))
            parent[Find(e.A)] = Find(e.B);
        return Enumerable.Range(0, graph.Nodes.Count).GroupBy(Find).Where(g => g.Count() >= 2)
            .Select(g => g.OrderBy(i => i).ToList())
            .OrderByDescending(g => g.Count).ThenBy(g => g[0]).ToList();
    }

    private static string JoinAnd(IEnumerable<string> parts)
    {
        var list = parts.ToList();
        return list.Count <= 1 ? string.Concat(list) : string.Join(", ", list.Take(list.Count - 1)) + " und " + list[^1];
    }

    private static string Short(string text)
    {
        var one = text.ReplaceLineEndings(" ").Trim();
        return one.Length <= 40 ? one : one[..39].TrimEnd() + "…";
    }
}

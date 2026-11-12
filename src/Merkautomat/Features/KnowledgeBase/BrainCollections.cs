using System.Text.RegularExpressions;
using Merkautomat.Models;

namespace Merkautomat.Services;

/// <summary>An item outside a collection that is well linked to its members.</summary>
public sealed record CollectionSuggestion(int Node, string CollectionId, int Links);

/// <summary>Collections ("Mappen"): confirmed topics with stable members, and suggestions for new items.</summary>
public static partial class BrainCollections
{
    public const int MaxNameLength = 80;
    public const int MaxSuggestionsPerCollection = 20;

    public static string CleanName(string name)
    {
        var clean = Whitespace().Replace(name ?? "", " ").Trim();
        return clean.Length <= MaxNameLength ? clean : clean[..MaxNameLength].TrimEnd();
    }

    public static BrainCollection Create(string name, IEnumerable<string> keys, DateTime now)
    {
        var clean = CleanName(name);
        if (clean.Length == 0) throw new ArgumentException("Die Mappe braucht einen Namen.", nameof(name));
        return new BrainCollection { Name = clean, Created = now, Keys = keys.Distinct(StringComparer.Ordinal).ToList() };
    }

    /// <summary>
    /// Applies the edit dialog: removed members become rejected (so they are not suggested right back), accepted
    /// suggestions become members. Suggestions left unchecked stay suggestions, so renaming never discards them.
    /// Members outside the shown net are not touched.
    /// </summary>
    public static void Update(BrainCollection collection, string name, IEnumerable<string> removed, IEnumerable<string> added)
    {
        var clean = CleanName(name);
        if (clean.Length == 0) throw new ArgumentException("Die Mappe braucht einen Namen.", nameof(name));
        var removedSet = removed.ToHashSet(StringComparer.Ordinal);
        var addedSet = added.Where(k => !removedSet.Contains(k)).ToHashSet(StringComparer.Ordinal);
        collection.Name = clean;
        collection.Keys = collection.Keys.Where(k => !removedSet.Contains(k)).Concat(addedSet)
            .Distinct(StringComparer.Ordinal).ToList();
        collection.Rejected = collection.Rejected.Where(k => !addedSet.Contains(k)).Concat(removedSet)
            .Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Drops deleted items from every collection.</summary>
    public static void Forget(IEnumerable<BrainCollection> collections, IReadOnlySet<string> deletedKeys)
    {
        foreach (var c in collections)
        {
            c.Keys.RemoveAll(deletedKeys.Contains);
            c.Rejected.RemoveAll(deletedKeys.Contains);
        }
    }

    /// <summary>
    /// Collection view: one cluster per collection in the stored order, also when none of its members is in the
    /// shown net (so it stays editable). An item in several collections takes the color of the first one.
    /// </summary>
    public static List<GraphCluster> Clusters(IReadOnlyList<GraphNode> nodes, IReadOnlyList<BrainCollection> collections)
    {
        foreach (var node in nodes) node.Cluster = -1;
        var byKey = nodes.ToDictionary(n => n.ItemKey, n => n.Index, StringComparer.Ordinal);
        var clusters = new List<GraphCluster>();
        foreach (var c in collections)
        {
            var id = clusters.Count;
            var members = c.Keys.Where(byKey.ContainsKey).Select(k => byKey[k]).Distinct().ToList();
            foreach (var m in members.Where(m => nodes[m].Cluster < 0)) nodes[m].Cluster = id;
            clusters.Add(new GraphCluster(id, c.Name.Length > 0 ? c.Name : $"Mappe {id + 1}", [], members) { CollectionId = c.Id });
        }
        return clusters;
    }

    /// <summary>
    /// Items in no collection with at least two content links into one collection, or whose only content link goes
    /// there. The tuned net decides what is related, so no extra threshold is needed. Best collection per item.
    /// </summary>
    public static List<CollectionSuggestion> Suggest(BrainGraph graph, IReadOnlyList<BrainCollection> collections)
    {
        var byKey = graph.Nodes.ToDictionary(n => n.ItemKey, n => n.Index, StringComparer.Ordinal);
        var memberOf = new HashSet<int>();
        var members = new List<HashSet<int>>();
        foreach (var c in collections)
        {
            var set = c.Keys.Where(byKey.ContainsKey).Select(k => byKey[k]).ToHashSet();
            members.Add(set);
            memberOf.UnionWith(set);
        }
        var best = new Dictionary<int, CollectionSuggestion>();
        for (var i = 0; i < graph.Nodes.Count; i++)
        {
            if (memberOf.Contains(i)) continue;
            var content = graph.EdgesOf(i).Where(IsContentLink).Select(e => e.Other(i)).ToHashSet();
            if (content.Count == 0) continue;
            for (var c = 0; c < collections.Count; c++)
            {
                if (members[c].Count == 0 || collections[c].Rejected.Contains(graph.Nodes[i].ItemKey, StringComparer.Ordinal)) continue;
                var links = content.Count(members[c].Contains);
                if (links == 0 || (links < 2 && content.Count > 1)) continue;
                if (!best.TryGetValue(i, out var current) || links > current.Links)
                    best[i] = new CollectionSuggestion(i, collections[c].Id, links);
            }
        }
        return best.Values
            .GroupBy(s => s.CollectionId)
            .SelectMany(g => g.OrderByDescending(s => s.Links).ThenByDescending(s => graph.Nodes[s.Node].Item.Date)
                .ThenBy(s => s.Node).Take(MaxSuggestionsPerCollection))
            .ToList();
    }

    private static bool IsContentLink(GraphEdge e)
        => e.Kind is GraphEdgeKind.Semantic or GraphEdgeKind.Summary or GraphEdgeKind.Manual or GraphEdgeKind.Session;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

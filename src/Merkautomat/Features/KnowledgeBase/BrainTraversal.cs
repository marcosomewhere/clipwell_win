using Merkautomat.Models;

namespace Merkautomat.Services;

public sealed record BrainPathStep(int From, int To, GraphEdge Edge);
public sealed record BrainPath(int Start, IReadOnlyList<BrainPathStep> Steps)
{
    public IEnumerable<int> Nodes => Steps.Select(s => s.To).Prepend(Start);
}

public static class BrainTraversal
{
    // Breadth-first search minimizes graph hops, not time or factual uncertainty.
    public static BrainPath? Find(BrainGraph graph, int start, int target, IReadOnlySet<GraphEdgeKind> allowed)
    {
        if (start < 0 || target < 0 || start >= graph.Nodes.Count || target >= graph.Nodes.Count) return null;
        var seen = new HashSet<int> { start };
        var previous = new Dictionary<int, BrainPathStep>();
        var queue = new Queue<int>();
        queue.Enqueue(start);
        while (queue.TryDequeue(out var current))
        {
            if (current == target)
            {
                var steps = new List<BrainPathStep>();
                while (current != start)
                {
                    var step = previous[current];
                    steps.Add(step);
                    current = step.From;
                }
                steps.Reverse();
                return new BrainPath(start, steps);
            }
            foreach (var edge in graph.EdgesOf(current).Where(e => allowed.Contains(e.Kind))
                         .OrderBy(e => e.Kind == GraphEdgeKind.Manual ? 0 : 1).ThenBy(e => e.Other(current)))
            {
                if (edge.Directed && edge.A != current) continue;
                var next = edge.Other(current);
                if (!seen.Add(next)) continue;
                previous[next] = new BrainPathStep(current, next, edge);
                queue.Enqueue(next);
            }
        }
        return null;
    }

    public static string? Validate(BrainRelation relation, IReadOnlySet<string> keys)
    {
        if (!keys.Contains(relation.FromKey) || !keys.Contains(relation.ToKey)) return "Bitte zwei vorhandene Einträge auswählen.";
        if (relation.FromKey == relation.ToKey) return "Start und Ziel müssen verschiedene Einträge sein.";
        if (string.IsNullOrWhiteSpace(relation.Kind)) return "Bitte die Beziehung benennen.";
        if (string.IsNullOrWhiteSpace(relation.Evidence)) return "Bitte eine Begründung oder Quellenstelle angeben.";
        return null;
    }
}

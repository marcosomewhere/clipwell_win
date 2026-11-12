using Merkautomat.Models;

namespace Merkautomat.Services;

public sealed record DashboardTile(string Label, int Value);

/// <summary>Figures for the KB dashboard: article counters, the current or latest recording and library breakdowns.</summary>
public sealed class KnowledgeDashboard
{
    private const int MaxTiles = 8;

    public int ArticleCount => NoteCount + RecordingCount;
    public int NoteCount { get; init; }
    public int RecordingCount { get; init; }
    public int ClipboardCount { get; init; }
    public int TotalSnapshots { get; init; }

    /// <summary>The running recording, otherwise the most recent one; null when nothing was recorded yet.</summary>
    public RecordingSession? FocusSession { get; init; }
    public bool FocusIsRunning { get; init; }
    public int FocusSnapshots { get; init; }
    public int FocusWindows { get; init; }
    public int FocusApps { get; init; }
    public int FocusMinutes { get; init; }

    public IReadOnlyList<DashboardTile> Applications { get; init; } = [];
    public IReadOnlyList<DashboardTile> Domains { get; init; } = [];

    public static KnowledgeDashboard Build(
        IReadOnlyList<ClipboardEntry> entries,
        IReadOnlyList<RecordingSession> sessions,
        IReadOnlyList<KbNote> notes,
        string? currentSessionPath)
    {
        var running = currentSessionPath == null ? null
            : sessions.FirstOrDefault(s => string.Equals(s.Path, currentSessionPath, StringComparison.OrdinalIgnoreCase));
        var focus = running ?? sessions.OrderByDescending(s => s.Start).FirstOrDefault();

        return new KnowledgeDashboard
        {
            NoteCount = notes.Count,
            RecordingCount = sessions.Count,
            ClipboardCount = entries.Count,
            TotalSnapshots = sessions.Sum(s => s.Snapshots.Count),
            FocusSession = focus,
            FocusIsRunning = running != null,
            FocusSnapshots = focus?.Snapshots.Count ?? 0,
            FocusWindows = focus?.Snapshots.Select(s => $"{s.App}: {s.Title}").Distinct(StringComparer.OrdinalIgnoreCase).Count() ?? 0,
            FocusApps = focus?.Snapshots.Select(s => s.App).Distinct(StringComparer.OrdinalIgnoreCase).Count() ?? 0,
            FocusMinutes = focus == null ? 0 : (int)Math.Round(focus.Duration.TotalMinutes, MidpointRounding.AwayFromZero),
            Applications = sessions.SelectMany(s => s.Snapshots)
                .GroupBy(s => s.App, StringComparer.OrdinalIgnoreCase)
                .Select(g => new DashboardTile(g.Key, g.Count()))
                .OrderByDescending(t => t.Value).ThenBy(t => t.Label, StringComparer.OrdinalIgnoreCase)
                .Take(MaxTiles).ToList(),
            Domains = entries
                .Select(KnowledgeSummaryBuilder.DomainOf)
                .Where(d => d != null)
                .GroupBy(d => d!, StringComparer.OrdinalIgnoreCase)
                .Select(g => new DashboardTile(g.Key, g.Count()))
                .OrderByDescending(t => t.Value).ThenBy(t => t.Label, StringComparer.OrdinalIgnoreCase)
                .Take(MaxTiles).ToList(),
        };
    }
}

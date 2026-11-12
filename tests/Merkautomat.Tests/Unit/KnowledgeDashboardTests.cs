using Merkautomat.Models;
using Merkautomat.Services;
using Merkautomat.Views;
using Xunit;

namespace Merkautomat.Tests;

public class KnowledgeDashboardTests
{
    private static RecordingSession Session(string path, DateTime start, DateTime? end, params (int Minute, string App, string Title)[] snaps)
    {
        var s = new RecordingSession { Path = path, Start = start, End = end };
        foreach (var (minute, app, title) in snaps) s.Snapshots.Add(new RecordingSnapshot(start.AddMinutes(minute), app, title));
        return s;
    }

    [Fact]
    public void Build_CountsArticlesAndFocusesRunningSession()
    {
        var start = new DateTime(2026, 11, 5, 10, 0, 0);
        var older = Session("a.md", start, start.AddMinutes(5), (1, "Edge", "Jira"));
        var running = Session("b.md", start.AddHours(1), null,
            (1, "Edge", "Jira"), (2, "Edge", "Confluence"), (3, "Outlook", "Posteingang"), (12, "Edge", "Jira"));
        var notes = new List<KbNote> { new("n1", "n1.md", "Notiz", start, start) };

        var data = KnowledgeDashboard.Build([], [older, running], notes, "B.MD");

        Assert.Equal(3, data.ArticleCount);
        Assert.Same(running, data.FocusSession);
        Assert.True(data.FocusIsRunning);
        Assert.Equal(4, data.FocusSnapshots);
        Assert.Equal(3, data.FocusWindows);
        Assert.Equal(2, data.FocusApps);
        Assert.Equal(12, data.FocusMinutes);
        Assert.Equal(new DashboardTile("Edge", 4), data.Applications[0]);
        Assert.Equal(new DashboardTile("Outlook", 1), data.Applications[1]);
    }

    [Fact]
    public void Build_FallsBackToLatestSessionAndCountsDomains()
    {
        var start = new DateTime(2026, 11, 5, 10, 0, 0);
        var first = Session("a.md", start, start.AddMinutes(1));
        var latest = Session("b.md", start.AddDays(1), start.AddDays(1).AddMinutes(2));
        var entries = new List<ClipboardEntry>
        {
            new() { Type = EntryType.Url, Content = "https://www.example.com/a" },
            new() { Type = EntryType.Text, Content = "x", SourceUrl = "https://example.com/b" },
            new() { Type = EntryType.Text, Content = "y" },
        };

        var data = KnowledgeDashboard.Build(entries, [first, latest], [], null);

        Assert.Same(latest, data.FocusSession);
        Assert.False(data.FocusIsRunning);
        Assert.Equal(3, data.ClipboardCount);
        Assert.Equal([new DashboardTile("example.com", 2)], data.Domains);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(5, 5)]
    [InlineData(6, 10)]
    [InlineData(11, 25)]
    [InlineData(26, 50)]
    [InlineData(51, 100)]
    [InlineData(101, 250)]
    public void NiceMaximum_PicksReadableScale(int value, int expected)
        => Assert.Equal(expected, AnalogGauge.NiceMaximum(value));
}

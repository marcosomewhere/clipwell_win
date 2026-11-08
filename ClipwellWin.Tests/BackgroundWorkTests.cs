using System.Diagnostics;
using System.IO;
using System.Text.Json;
using ClipwellWin.Models;
using ClipwellWin.Services;
using Xunit;
using static ClipwellWin.Tests.RocchioModelTests;

namespace ClipwellWin.Tests;

public sealed class BackgroundWorkTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "merkautomat-bg-" + Guid.NewGuid().ToString("N"));
    public BackgroundWorkTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    private static SystemActivity Activity(double idleMinutes = 0, bool battery = false, UserPresence presence = UserPresence.Normal,
        double sinceDirectMinutes = 60)
        => new(TimeSpan.FromMinutes(idleMinutes), battery, presence, TimeSpan.FromMinutes(sinceDirectMinutes));

    [Fact]
    public void IdleModeRunsFastOnlyWhenIdleAndWaitsOtherwise()
    {
        Assert.Equal(BackgroundState.Fast, BackgroundWork.Decide("idle", Activity(idleMinutes: 6)).State);
        var busy = BackgroundWork.Decide("idle", Activity(idleMinutes: 1));
        Assert.Equal(BackgroundState.Paused, busy.State);
        Assert.False(busy.Hard);
        Assert.Contains("Leerlauf", busy.Reason);
    }

    [Fact]
    public void AlwaysModeRunsThrottledDuringWork()
    {
        Assert.Equal(BackgroundState.Eco, BackgroundWork.Decide("always", Activity(idleMinutes: 0)).State);
        Assert.Equal(BackgroundState.Fast, BackgroundWork.Decide("always", Activity(idleMinutes: 10)).State);
    }

    [Fact]
    public void FullScreenVideoRunsThrottledEvenWhenIdle()
    {
        Assert.Equal(BackgroundState.Eco, BackgroundWork.Decide("idle", Activity(idleMinutes: 30, presence: UserPresence.FullScreen)).State);
        Assert.Equal(BackgroundState.Eco, BackgroundWork.Decide("idle", Activity(idleMinutes: 0, presence: UserPresence.FullScreen)).State);
    }

    [Theory]
    [InlineData(true, UserPresence.Normal)]
    [InlineData(false, UserPresence.Game)]
    [InlineData(false, UserPresence.Presentation)]
    public void BatteryGamesAndPresentationsStopAtOnce(bool battery, UserPresence presence)
    {
        var decision = BackgroundWork.Decide("always", Activity(idleMinutes: 30, battery: battery, presence: presence));
        Assert.Equal(BackgroundState.Paused, decision.State);
        Assert.True(decision.Hard);
    }

    [Fact]
    public void OffAndRecentDirectUsePause()
    {
        Assert.True(BackgroundWork.Decide("off", Activity(idleMinutes: 30)).Hard);
        var direct = BackgroundWork.Decide("always", Activity(idleMinutes: 30, sinceDirectMinutes: 1));
        Assert.Equal(BackgroundState.Paused, direct.State);
        Assert.False(direct.Hard);
        Assert.Equal(BackgroundState.Fast, BackgroundWork.Decide("idle", Activity(idleMinutes: 30, sinceDirectMinutes: 4)).State);
    }

    [Fact]
    public void ModeNormalizesAndSettingsDefaultToIdle()
    {
        Assert.Equal("idle", BackgroundWork.Normalize(null));
        Assert.Equal("idle", BackgroundWork.Normalize("unknown"));
        Assert.Equal("always", BackgroundWork.Normalize("always"));
        Assert.Equal("off", BackgroundWork.Normalize("off"));
        var settings = JsonSerializer.Deserialize<AppSettings>("{}")!;
        Assert.Equal("idle", settings.BackgroundWorkMode);
        Assert.False(settings.BackgroundKeepAwake);
    }

    [Fact]
    public void ProbeReadsTheMachineWithoutFailing()
    {
        var activity = BackgroundWork.Probe(DateTime.MinValue);
        Assert.True(activity.IdleFor >= TimeSpan.Zero);
        Assert.True(activity.SinceDirectAiUse > TimeSpan.FromDays(1));
    }

    [Fact]
    public void ProcessLoadSwitchesPriorityOfARunningProcess()
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1 >nul") { CreateNoWindow = true, UseShellExecute = false })!;
        try
        {
            ProcessLoad.Apply(process, AiLoad.Eco);
            process.Refresh();
            Assert.Equal(ProcessPriorityClass.Idle, process.PriorityClass);
            ProcessLoad.Apply(process, AiLoad.Normal);
            process.Refresh();
            Assert.Equal(ProcessPriorityClass.BelowNormal, process.PriorityClass);
        }
        finally { process.Kill(entireProcessTree: true); }
    }

    private static List<DiscoveryItem> Items()
    {
        var items = new List<DiscoveryItem>();
        for (var topic = 0; topic < 3; topic++)
            for (var i = 0; i < 8; i++)
                items.Add(new DiscoveryItem($"c:{topic}{i}", $"Thema{topic} Eintrag {i}", $"Text zu Thema {topic} Nummer {i}.",
                    DateTime.Now, Vec((0, 1), (1 + topic, 1), (6 + i, 0.3f))));
        return items;
    }

    [Fact]
    public async Task InterruptedRunResumesWithoutNamingFinishedGroupsAgain()
    {
        var items = Items();
        var path = Path.Combine(_root, BackgroundDiscoveryState.FileName);
        var state = new BackgroundDiscoveryState { Keys = items.Select(i => i.Key).ToList(), Started = DateTime.Now };
        var calls = 0;
        using var stop = new CancellationTokenSource();
        Task<LocalAiSuggestion> First(IReadOnlyList<BrainSource> sources, IReadOnlyList<string> categories, CancellationToken ct)
        {
            if (++calls == 2) throw new OperationCanceledException(ct);
            return Task.FromResult(new LocalAiSuggestion("", "Thema " + sources[0].Title[5], "passt", false));
        }
        var progress = new SyncProgress(p => { if (p.Finished != null && !p.Resumed) { state.Groups.Add(StoredProposal.From(p.Finished)); state.Save(path); } });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CategoryDiscovery.RunAsync(items, [], First, progress, TestContext.Current.CancellationToken));
        Assert.Single(state.Groups);

        var loaded = BackgroundDiscoveryState.Load(path)!;
        Assert.True(loaded.CanResume(items));
        Assert.False(loaded.CanResume(items.Skip(1).ToList()));
        var named = new List<string>();
        Task<LocalAiSuggestion> Second(IReadOnlyList<BrainSource> sources, IReadOnlyList<string> categories, CancellationToken ct)
        {
            named.Add(sources[0].Title);
            Assert.Contains(loaded.Groups[0].ProposedName, categories);
            return Task.FromResult(new LocalAiSuggestion("", "Thema " + sources[0].Title[5], "passt", false));
        }
        var resumed = new List<DiscoveryProgress>();
        var proposals = await CategoryDiscovery.RunAsync(items, [], Second, new SyncProgress(resumed.Add), TestContext.Current.CancellationToken,
            loaded.Proposals([]));
        Assert.Single(resumed, p => p.Resumed);
        Assert.DoesNotContain(named, t => t[5] == loaded.Groups[0].Keys[0][2]);
        Assert.Equal(3, proposals.Count);
    }

    [Fact]
    public void ReadyProposalsDropCategorizedEntriesAndMergeByName()
    {
        var state = new BackgroundDiscoveryState
        {
            Keys = ["a", "b", "c", "d", "e"], Complete = true, Finished = DateTime.Now,
            Groups =
            [
                new(["a", "b"], ["A", "B"], "Reisen", "passt", false, ClassifierMethods.Granite),
                new(["c"], ["C"], "reisen", "auch", false, ClassifierMethods.Granite),
                new(["d"], ["D"], "", "unklar", true, ClassifierMethods.Granite),
                new(["e"], ["E"], "Arbeit", "passt", false, ClassifierMethods.Granite),
            ],
        };
        var ready = state.Ready(new HashSet<string> { "b", "c", "d" }, ["Arbeit"]);
        var reisen = Assert.Single(ready, p => p.Name == "Reisen");
        Assert.Equal(["b", "c"], reisen.Keys);
        Assert.Equal(["B", "C"], reisen.Titles);
        Assert.True(reisen.IsNew && reisen.Accepted);
        var open = Assert.Single(ready, p => p.Uncertain);
        Assert.False(open.Accepted);
        Assert.DoesNotContain(ready, p => p.Name == "Arbeit");
        Assert.Equal(1, state.NewItems(["a", "x"]));
        // Merging must not change the stored groups.
        Assert.Equal(["a", "b"], state.Groups[0].Keys);
    }

    [Fact]
    public void StateSurvivesASaveAndBrokenFilesAreIgnored()
    {
        var path = Path.Combine(_root, BackgroundDiscoveryState.FileName);
        new BackgroundDiscoveryState { Keys = ["a"], Complete = true, Seen = true, Groups = [new(["a"], ["A"], "X", "r", false, "granite")] }.Save(path);
        var loaded = BackgroundDiscoveryState.Load(path)!;
        Assert.True(loaded.Complete && loaded.Seen);
        Assert.Equal("X", loaded.Groups[0].ProposedName);
        Assert.False(File.Exists(path + ".tmp"));
        File.WriteAllText(path, "{ kaputt");
        Assert.Null(BackgroundDiscoveryState.Load(path));
        Assert.Null(BackgroundDiscoveryState.Load(Path.Combine(_root, "fehlt.json")));
    }

    [Fact]
    public void MergeLeavesItsInputUnchanged()
    {
        CategoryProposal P(string key, string name) => new()
        {
            Keys = [key], Titles = [key], ProposedName = name, Reason = "", Uncertain = false, IsNew = true, Name = name, Accepted = true,
        };
        var first = P("a", "Reisen");
        var merged = CategoryDiscovery.Merge([first, P("b", "Reisen")]);
        Assert.Equal(["a", "b"], Assert.Single(merged).Keys);
        Assert.Equal(["a"], first.Keys);
    }

    private sealed class SyncProgress(Action<DiscoveryProgress> report) : IProgress<DiscoveryProgress>
    {
        public void Report(DiscoveryProgress value) => report(value);
    }
}

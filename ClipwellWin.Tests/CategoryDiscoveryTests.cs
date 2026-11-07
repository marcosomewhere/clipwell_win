using System.IO;
using System.Windows.Threading;
using ClipwellWin.Services;
using ClipwellWin.Views;
using Xunit;
using static ClipwellWin.Tests.RocchioModelTests;

namespace ClipwellWin.Tests;

public sealed class CategoryDiscoveryTests
{
    /// <summary>Three topics with eight entries each, plus two loners.</summary>
    private static List<DiscoveryItem> Items()
    {
        var items = new List<DiscoveryItem>();
        for (var topic = 0; topic < 3; topic++)
            for (var i = 0; i < 8; i++)
                items.Add(new DiscoveryItem($"c:{topic}{i}", $"Thema{topic} Eintrag {i}", $"Text zu Thema {topic} Nummer {i} mit genug Inhalt.",
                    DateTime.Now, Vec((0, 1), (1 + topic, 1), (6 + i, 0.3f))));
        items.Add(new DiscoveryItem("c:x1", "Einzeln 1", "Ganz anderer Text eins.", DateTime.Now, Vec((0, 1), (20, 1))));
        return items;
    }

    [Fact]
    public void GroupsFollowTheTopicsAndDropTinyGroups()
    {
        var items = Items();
        var groups = CategoryDiscovery.Group(items.Select(i => i.Vector).ToArray());
        Assert.All(groups, g => Assert.True(g.Length >= CategoryDiscovery.MinGroupSize));
        foreach (var topic in Enumerable.Range(0, 3))
        {
            var members = Enumerable.Range(topic * 8, 8).ToHashSet();
            Assert.Contains(groups, g => members.IsSubsetOf(g));
        }
        Assert.Empty(CategoryDiscovery.Group([Vec((0, 1)), Vec((1, 1))]));
    }

    [Fact]
    public async Task GraniteNamesGroupsSameNamesMergeAndFailuresStayOpen()
    {
        var seenCategories = new List<IReadOnlyList<string>>();
        var calls = 0;
        Task<LocalAiSuggestion> Name(IReadOnlyList<BrainSource> sources, IReadOnlyList<string> categories, CancellationToken _)
        {
            seenCategories.Add(categories);
            calls++;
            Assert.True(sources.Count <= CategoryDiscovery.Representatives);
            var topic = sources[0].Title[5];
            return topic switch
            {
                '0' => Task.FromResult(new LocalAiSuggestion("T", "Reisen", "passt", false)),
                '1' => Task.FromResult(new LocalAiSuggestion("T", "reisen", "auch", false)),
                _ => throw new InvalidDataException("Antwort unvollständig"),
            };
        }
        var reports = new List<DiscoveryProgress>();
        var proposals = await CategoryDiscovery.RunAsync(Items(), ["Arbeit"], Name, new SyncProgress(reports.Add), TestContext.Current.CancellationToken);
        Assert.Equal(calls * 2, reports.Count);
        Assert.All(reports.Where(r => r.Finished == null), r => Assert.True(r.GroupSize >= CategoryDiscovery.MinGroupSize && r.Sample.Length > 0));
        Assert.Equal(calls, reports.Last().Done);
        Assert.Equal(calls, seenCategories.Count);
        Assert.Contains(seenCategories, c => c.Contains("Reisen"));
        var named = Assert.Single(proposals, p => !p.Uncertain);
        Assert.Equal("Reisen", named.Name);
        Assert.True(named.IsNew);
        Assert.True(named.Accepted);
        Assert.Equal(16, named.Count);
        var open = Assert.Single(proposals, p => p.Uncertain);
        Assert.False(open.Accepted);
        Assert.Equal("", open.Name);
        Assert.Contains("Antwort unvollständig", open.Reason);
        Assert.Contains("keinen eindeutigen Namen", open.Explanation);
        Assert.Equal("noch ohne Namen", open.Kind);
        Assert.Equal(4, named.Examples.Count);
        Assert.True(named.HasMore);
    }

    [Fact]
    public async Task StartErrorsAbortInsteadOfProducingUnnamedGroups()
        => await Assert.ThrowsAsync<IOException>(() => CategoryDiscovery.RunAsync(Items(), [],
            (_, _, _) => throw new IOException("ggml-base.dll gesperrt"), null, TestContext.Current.CancellationToken));

    [Fact]
    public async Task ExistingCategoryIsMarkedAsExisting()
    {
        var proposals = await CategoryDiscovery.RunAsync(Items(), ["Reisen"],
            (_, _, _) => Task.FromResult(new LocalAiSuggestion("T", "Reisen", "passt", false)), null, TestContext.Current.CancellationToken);
        Assert.False(Assert.Single(proposals).IsNew);
    }

    [Fact]
    public void WindowOffersDownloadWithoutGraniteAndShowsProposalsAfterAnalysis()
        => StaTestRunner.Run(() =>
        {
            using var ai = new LocalAiService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), () => "off");
            var missing = new CategoryDiscoveryWindow(ai, Items(), [], (_, _) => { });
            missing.Show();
            Assert.StartsWith("Granite herunterladen", (string)((System.Windows.Controls.Button)missing.FindName("StartButton")).Content);
            missing.Close();

            // Same context as in the app, where the click handler runs on the UI thread.
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var window = new CategoryDiscoveryWindow(null, Items(), [], (_, _) => { },
                name: (_, _, _) => Task.FromResult(new LocalAiSuggestion("T", "Technik", "passt", false)));
            window.Show();
            ((System.Windows.Controls.Button)window.FindName("StartButton")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            var list = (System.Windows.Controls.ListBox)window.FindName("ProposalList");
            var deadline = DateTime.Now.AddSeconds(10);
            while (list.Items.Count == 0 && DateTime.Now < deadline)
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
            var proposal = Assert.IsType<CategoryProposal>(Assert.Single(list.Items.Cast<object>()));
            Assert.Equal("Technik", proposal.Name);
            Assert.True(((System.Windows.Controls.Button)window.FindName("ApplyButton")).IsEnabled);
            window.Close();
        });

    private sealed class SyncProgress(Action<DiscoveryProgress> report) : IProgress<DiscoveryProgress>
    {
        public void Report(DiscoveryProgress value) => report(value);
    }
}
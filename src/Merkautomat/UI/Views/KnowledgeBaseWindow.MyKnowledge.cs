using System.IO;
using System.Windows;
using Merkautomat.Services;
using MessageBox = Merkautomat.Views.AppMessageBox;

namespace Merkautomat.Views;

/// <summary>Window side of "Mein Wissen": navigation, meaning search over the index, categories and Brain focus.</summary>
public partial class KnowledgeBaseWindow
{
    private void InitMyKnowledge()
    {
        MyKnowledgeView.StatusMessage += text => StatusLabel.Text = text;
        MyKnowledgeView.StateChanged += (_, _) => UpdateMyKnowledgeChrome();
        MyKnowledgeView.ShowInBrain += ShowMyKnowledgeInBrain;
        MyKnowledgeView.AssignCategory += AssignMyKnowledgeCategory;
        MyKnowledgeView.SemanticSearch = SearchMyKnowledgeAsync;
        MyKnowledgeView.SimilarGroups = SimilarMyKnowledgeAsync;
        MyKnowledgeView.AcceptSuggestions += AcceptMyKnowledgeSuggestions;
        MyKnowledgeView.Init(_app);
        _app.MyKnowledgeChanged += App_MyKnowledgeChanged;
        UpdateMyKnowledgeChrome();
    }

    private void CloseMyKnowledge()
    {
        _app.MyKnowledgeChanged -= App_MyKnowledgeChanged;
        MyKnowledgeView.Close();
    }

    private void EnterMyKnowledge()
    {
        _semanticModelInstalled = EmbeddingModel.IsInstalled(EmbeddingModel.ResolveModelsDir(AppContext.BaseDirectory, AppPaths.ModelsDir));
        // Meaning search needs a current index; words are found in the stored copies anyway.
        StartSemanticSync();
        MyKnowledgeView.Enter();
    }

    private void App_MyKnowledgeChanged(object? sender, EventArgs e)
    {
        UpdateMyKnowledgeChrome();
        // The Brain's "Mein Wissen" filter depends on the collection; background titles or proposals leave it alone.
        if (_view == KbView.Brain && _app.CurrentSettings.BrainOnlyMyKnowledge && BrainWeightsChanged()) RebuildBrain(keepLayout: true);
        if (_view == KbView.Dashboard) UpdateWeeklyReview();
    }

    private void UpdateMyKnowledgeChrome()
    {
        var count = _app.MyKnowledgeCount;
        MyKnowledgeNavCount.Text = count > 0 ? count.ToString("N0", De) : "";
        if (_view == KbView.MyKnowledge)
        {
            ContentSubtitle.Text = MyKnowledgeView.Subtitle;
            DeleteButton.IsEnabled = MyKnowledgeView.HasSelection;
        }
    }

    private async Task<IReadOnlyList<SemanticHit>> SearchMyKnowledgeAsync(string query, CancellationToken ct)
    {
        if (!_semanticModelInstalled || _app.Semantic.Count == 0) return [];
        var index = _app.Semantic;
        return await Task.Run(() => (IReadOnlyList<SemanticHit>)index.Search(query, null, 200), ct);
    }

    /// <summary>Groups of nearly equal items from the search index (same rule as the duplicate filter of the search).</summary>
    private async Task<List<List<string>>> SimilarMyKnowledgeAsync(CancellationToken ct)
    {
        if (!_semanticModelInstalled || _app.Semantic.Count == 0) return [];
        var index = _app.Semantic;
        return await Task.Run(() => index.Duplicates(null, DuplicateView.Groups)
            .Select(h => new[] { h.Chunk.ItemKey }.Concat(h.Duplicates.Select(d => d.ItemKey)).Distinct().ToList())
            .Where(g => g.Count >= 2).ToList(), ct);
    }

    /// <summary>The user agreed to category proposals; saved like a manual assignment.</summary>
    private void AcceptMyKnowledgeSuggestions(IReadOnlyDictionary<string, string> proposals)
    {
        var assignments = proposals.GroupBy(p => p.Value, StringComparer.OrdinalIgnoreCase)
            .Select(g => ((IReadOnlyCollection<string>)g.Select(p => p.Key).ToList(), KnowledgeCategories.Parse(g.Key)))
            .Where(a => KnowledgeCategories.Validate(a.Item2) == null).ToList();
        // Taken-over proposals count in the practice values of the evaluation, like in the categorization window.
        foreach (var entry in _app.MyKnowledgeSnapshot().Where(e => proposals.ContainsKey(e.Key) && e.SuggestedCategory.Length > 0))
            _app.RecordCategorizationFeedback(MyKnowledgeAi.SuggestionOf(entry), proposals[entry.Key]);
        if (SaveCategories(assignments, "Vorschläge aus Mein Wissen speichern") == null) return;
        StatusLabel.Text = $"{proposals.Count} {(proposals.Count == 1 ? "Vorschlag" : "Vorschläge")} übernommen.";
    }

    // ---- Dashboard: new in Mein Wissen ------------------------------------------------------------------------

    public sealed record WeeklyItem(string Title, string Meta, string Category);

    private List<BrainSource> _weeklySources = [];

    /// <summary>Groups of the last seven days with the sentence of the language model when it is ready, else the top titles.</summary>
    private void UpdateWeeklyReview()
    {
        var review = _app.MyKnowledgeReview();
        WeeklyList.ItemsSource = review.Select(r => new WeeklyItem(
            $"{r.Group.Name} · {r.Group.Entries.Count} neu",
            r.Sentence?.Text ?? string.Join(" · ", r.Group.Entries.Take(3).Select(MyKnowledgeAi.DisplayTitle)),
            r.Group.Name)).ToList();
        var total = review.Sum(r => r.Group.Entries.Count);
        var waiting = _app.CurrentSettings.MyKnowledgeMaturityHours;
        WeeklyHeadline.Text = total == 0
            ? $"In den letzten {MyKnowledgeAi.ReviewDays} Tagen ist nichts dazugekommen. Neues erscheint nach {waiting} Stunden Wartezeit."
            : $"{total} Einträge in den letzten {MyKnowledgeAi.ReviewDays} Tagen, nach Kategorie."
              + (review.Any(r => r.Sentence != null) ? " Sätze lokal vom Sprachmodell formuliert." : "");
        _weeklySources = review.SelectMany(r => r.Group.Entries).Select(MyKnowledgeAi.SourceOf).ToList();
        var points = BrainOpenPoints.Find(_weeklySources, TasksView.Tasks.Select(t => t.Title)).Count;
        WeeklyOpenPointsButton.IsEnabled = points > 0;
        WeeklyOpenPointsText.Text = points > 0 ? $"Offene Punkte ({points}) …" : "Keine offenen Punkte";
    }

    private void WeeklyOpenPoints_Click(object sender, RoutedEventArgs e)
        => CreateTasksFromOpenPoints(_weeklySources,
            key => key.StartsWith("c:", StringComparison.Ordinal) ? "clip:" + key[2..] : null,
            $"neuen Einträgen der letzten {MyKnowledgeAi.ReviewDays} Tage");

    private void WeeklyOpen_Click(object sender, RoutedEventArgs e) => MyKnowledgeNav.IsChecked = true;

    private void WeeklyItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not WeeklyItem item) return;
        MyKnowledgeNav.IsChecked = true;
        MyKnowledgeView.ShowCategory(item.Category);
    }

    private void ShowMyKnowledgeInBrain(IReadOnlyCollection<string> keys, string title)
    {
        _pendingBrainFocus = (keys.ToHashSet(StringComparer.Ordinal), title);
        OpenBrainSection();
    }

    private void AssignMyKnowledgeCategory(IReadOnlyCollection<string> keys)
    {
        var settings = _app.CurrentSettings;
        var current = keys.Select(k => KnowledgeCategories.Effective(settings.BrainAnnotations, k)).ToList();
        var distinct = current.Select(p => p.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var scope = $"{keys.Count} Einträge aus {AppNames.MyKnowledge}."
            + (distinct.Count > 1 ? " Sie haben unterschiedliche Kategorien; die gewählte gilt danach für alle." : "");
        var dialog = new AssignCategoryDialog(settings, scope, distinct.Count == 1 ? current[0] : default, current.Any(p => !p.IsEmpty)) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (SaveCategories([(keys, dialog.Result)], "Kategorie in Mein Wissen speichern") == null) return;
        StatusLabel.Text = dialog.Result.IsEmpty
            ? $"Kategorie bei {keys.Count} Einträgen entfernt."
            : $"{keys.Count} Einträge sind jetzt „{dialog.Result}“ zugeordnet.";
    }
}

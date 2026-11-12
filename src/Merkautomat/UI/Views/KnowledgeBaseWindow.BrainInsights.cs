using System.IO;
using System.Text;
using System.Windows;
using Merkautomat.Models;
using Merkautomat.Services;
using MessageBox = Merkautomat.Views.AppMessageBox;

namespace Merkautomat.Views;

/// <summary>Brain side panel "Rückblick" and "Entdecken", collections ("Mappen") and actions on the highlighted group.</summary>
public partial class KnowledgeBaseWindow
{
    public sealed record DashboardReviewItem(string Title, string Meta, IReadOnlyList<string> Keys);

    private sealed record BrainGroup(string Title, IReadOnlyList<int> Nodes, BrainCollection? Collection);

    private (HashSet<string> Keys, string Title)? _pendingBrainFocus;
    private int _dashboardReviewVersion;
    private string? _dashboardReviewKey;

    private void UpdateBrainBackButton()
        => BrainBackButton.Content = BrainPanelReview.IsChecked == true ? "Zurück zum Rückblick"
            : BrainPanelDiscover.IsChecked == true ? "Zurück zu Entdecken"
            : ByCollection ? "Zurück zu Mappen" : ByCategory ? "Zurück zu Kategorien" : "Zurück zu Themen";

    private void BrainPanel_Checked(object sender, RoutedEventArgs e)
    {
        // Fires while the window is still being built.
        if (BrainBackButton == null || BrainPanelReview == null || BrainPanelDiscover == null || BrainGroupCollections == null) return;
        UpdateBrainBackButton();
        if (_brain != null) ShowBrainOverview();
    }

    private static BrainListItem ListHeader(string text) => new()
    {
        Title = text.ToUpperInvariant(), Meta = "", Brush = System.Windows.Media.Brushes.Transparent, IsHeader = true,
    };

    // ---- Panels ----------------------------------------------------------------------------------------------

    private void ShowBrainReview()
    {
        var review = BrainInsights.Review(_brain!, TasksView.Tasks, DateTime.Now);
        BrainLinksHeader.Text = review.Headline + (review.Topics.Count == 0
            ? $" Keine {(ByCollection ? "Mappe" : ByCategory ? "Kategorie" : "Thema")} ist neu, gewachsen oder ruht mit offenen Aufgaben."
            : "");
        SetBrainList(review.Topics.Select(r => new BrainListItem
        {
            Cluster = r.Cluster,
            Title = r.Label,
            Meta = r.Text,
            Brush = BrushFor(r.Cluster),
        }).ToList());
        ApplyBrainHighlight();
    }

    private void ShowBrainDiscoveries()
    {
        var found = BrainInsights.Discover(_brain!, DateTime.Now);
        BrainLinksHeader.Text = found.Count == 0
            ? "Im angezeigten Netz fällt nichts auf: kein Vergessenes zu den letzten Tagen, keine Brücken, keine fast gleichen Einträge."
            : "Was eine Suche nicht zeigt. Eine Zeile wählen hebt die Einträge hervor; die Aktionen unten wirken auf sie.";
        var items = new List<BrainListItem>();
        foreach (var group in found.GroupBy(d => d.Kind))
        {
            var kind = BrainInsights.KindName(group.Key);
            items.Add(ListHeader(kind));
            items.AddRange(group.Select(d => new BrainListItem
            {
                Title = d.Title,
                Meta = d.Detail,
                Members = d.Nodes,
                GroupTitle = $"{kind}: {d.Title}",
                Brush = BrushFor(_brain!.Nodes[d.Nodes[0]].Cluster),
            }));
        }
        SetBrainList(items);
        ApplyBrainHighlight();
    }

    private void ShowBrainCollections(int unassigned)
    {
        var collections = _app.CurrentSettings.BrainCollections ?? [];
        if (_brain!.Clusters.Count == 0)
        {
            BrainLinksHeader.Text = "MAPPEN · noch keine. Unter „Themen“ ein Thema wählen und „Als Mappe speichern …“.";
            SetBrainList([]);
            ApplyBrainHighlight();
            return;
        }
        var suggestions = BrainCollections.Suggest(_brain, collections);
        BrainLinksHeader.Text = $"MAPPEN ({_brain.Clusters.Count})" + (unassigned > 0 ? $" · {unassigned} Einträge ohne Mappe (grau)" : "");
        SetBrainList(_brain.Clusters.Select(c =>
        {
            var stored = collections.FirstOrDefault(x => x.Id == c.CollectionId);
            var outside = Math.Max(0, (stored?.Keys.Count ?? c.Members.Count) - c.Members.Count);
            var suggested = suggestions.Count(s => s.CollectionId == c.CollectionId);
            return new BrainListItem
            {
                Cluster = c.Id,
                Title = c.Label,
                Meta = $"{c.Members.Count} im Netz" + (outside > 0 ? $" · {outside} außerhalb" : "")
                    + (suggested > 0 ? $" · {suggested} {(suggested == 1 ? "Vorschlag" : "Vorschläge")}" : "")
                    + (c.Members.Count > 0 ? " · " + SourceBreakdown(c.Members) : ""),
                Brush = BrushFor(c.Id),
            };
        }).ToList());
        ApplyBrainHighlight();
    }

    // ---- Group and actions -----------------------------------------------------------------------------------

    /// <summary>What the actions work on: the chosen group, the highlighted rows, or the selected item with its links.</summary>
    private BrainGroup? CurrentBrainGroup()
    {
        if (_brain == null) return null;
        if (_brainCluster >= 0 && _brainCluster < _brain.Clusters.Count)
        {
            var c = _brain.Clusters[_brainCluster];
            var collection = c.CollectionId.Length == 0 ? null
                : (_app.CurrentSettings.BrainCollections ?? []).FirstOrDefault(x => x.Id == c.CollectionId);
            return new BrainGroup(c.Label, c.Members, collection);
        }
        if (_brainSubMembers != null) return new BrainGroup(_brainGroupTitle, _brainSubMembers, null);
        if (SelectedBrainNode is { } node)
            return new BrainGroup(node.Item.Title, [node.Index, .. _brain.Neighbors(node.Index)
                .Where(x => x.Edges.Any(e => BrainCanvasView.VisibleEdgeKinds.Contains(e.Kind))).Select(x => x.Node)], null);
        return null;
    }

    private void UpdateBrainActions()
    {
        var group = CurrentBrainGroup();
        BrainCollectionButton.IsEnabled = group != null && (group.Collection != null || group.Nodes.Count > 0);
        BrainCollectionButton.Content = group?.Collection != null ? "Mappe bearbeiten …" : "Als Mappe speichern …";
        BrainActionsButton.IsEnabled = group is { Nodes.Count: > 0 };
    }

    private void BrainActions_Click(object sender, RoutedEventArgs e)
    {
        BrainActionsButton.ContextMenu.PlacementTarget = BrainActionsButton;
        BrainActionsButton.ContextMenu.IsOpen = true;
    }

    private BrainChoiceDialog.Choice ChoiceFor(GraphNode node, bool initial, string? reason = null) => new()
    {
        Key = node.ItemKey,
        Title = node.Item.Title.ReplaceLineEndings(" ").Trim() is { Length: > 0 } t ? t : "(ohne Titel)",
        Detail = $"{SemanticChunk.SourceName(node.Source)} · {node.Item.Date.ToString("dd.MM.yyyy HH:mm", De)}" + (reason != null ? " · " + reason : ""),
        Initial = initial,
    };

    private void BrainCollection_Click(object sender, RoutedEventArgs e)
    {
        if (_brain is not { } graph || CurrentBrainGroup() is not { } group) return;
        var settings = _app.CurrentSettings;
        settings.BrainCollections ??= [];
        if (group.Collection != null)
        {
            EditBrainCollection(graph, group.Collection);
            return;
        }
        var choices = group.Nodes.Select(i => ChoiceFor(graph.Nodes[i], initial: true)).ToList();
        var dialog = new BrainChoiceDialog("Als Mappe speichern",
            "Die markierten Einträge bilden die Mappe. Sie bleibt bestehen, auch wenn sich die automatischen Themen ändern. "
            + "Neue Einträge, die gut zu ihr passen, erscheinen unter „Mappen“ als Vorschläge.",
            BrainCollections.CleanName(group.Title), [new BrainChoiceDialog.Section($"EINTRÄGE ({choices.Count})", choices)],
            "Mappe speichern", nameLabel: "Name der Mappe") { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (dialog.CheckedKeys.Count == 0)
        {
            MessageBox.Show(this, "Keine Mappe gespeichert: Es war kein Eintrag markiert.", AppNames.KnowledgeBase, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        BrainCollection created;
        try { created = BrainCollections.Create(dialog.NameText, dialog.CheckedKeys, DateTime.Now); }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, AppNames.KnowledgeBase, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        settings.BrainCollections.Add(created);
        if (!SaveBrainSettingsChange())
        {
            settings.BrainCollections.Remove(created);
            return;
        }
        _pendingBrainFocus = (created.Keys.ToHashSet(StringComparer.Ordinal), $"Mappe „{created.Name}“ gespeichert; unter Gruppieren nach „Mappen“ zu finden");
        RebuildBrain(keepLayout: true);
    }

    private void EditBrainCollection(BrainGraph graph, BrainCollection collection)
    {
        var settings = _app.CurrentSettings;
        var byKey = graph.Nodes.ToDictionary(n => n.ItemKey, n => n.Index, StringComparer.Ordinal);
        var members = collection.Keys.Where(byKey.ContainsKey).Distinct()
            .Select(k => ChoiceFor(graph.Nodes[byKey[k]], initial: true)).ToList();
        var outside = collection.Keys.Count - members.Count;
        var suggestions = BrainCollections.Suggest(graph, settings.BrainCollections ?? [])
            .Where(s => s.CollectionId == collection.Id)
            .Select(s => ChoiceFor(graph.Nodes[s.Node], initial: false, $"{s.Links} {(s.Links == 1 ? "Bezug" : "Bezüge")} zur Mappe")).ToList();
        var dialog = new BrainChoiceDialog("Mappe bearbeiten",
            "Abgewählte Einträge verlassen die Mappe und werden ihr nicht wieder vorgeschlagen. Markierte Vorschläge kommen dazu."
            + (outside > 0 ? $" {outside} weitere Einträge liegen außerhalb des angezeigten Netzes und bleiben unverändert." : ""),
            collection.Name,
            [new BrainChoiceDialog.Section($"IN DER MAPPE ({members.Count})", members),
             new BrainChoiceDialog.Section($"VORSCHLÄGE ({suggestions.Count})", suggestions)],
            "Speichern", removeText: "Mappe auflösen", nameLabel: "Name der Mappe") { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var list = settings.BrainCollections ??= [];
        var position = list.IndexOf(collection);
        var backup = CopyOf(collection);
        if (dialog.RemoveRequested)
        {
            list.Remove(collection);
        }
        else
        {
            var chosen = dialog.CheckedKeys.ToHashSet(StringComparer.Ordinal);
            try
            {
                BrainCollections.Update(collection, dialog.NameText,
                    members.Where(c => !chosen.Contains(c.Key)).Select(c => c.Key),
                    suggestions.Where(c => chosen.Contains(c.Key)).Select(c => c.Key));
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(this, ex.Message, AppNames.KnowledgeBase, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }
        if (!SaveBrainSettingsChange())
        {
            if (dialog.RemoveRequested && position >= 0) list.Insert(position, collection);
            collection.Name = backup.Name;
            collection.Keys = backup.Keys;
            collection.Rejected = backup.Rejected;
            return;
        }
        RebuildBrain(keepLayout: true);
    }

    private bool SaveBrainSettingsChange()
    {
        try
        {
            _app.SaveSettings();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.LogCrash(ex);
            MessageBox.Show(this, $"Die Änderung konnte nicht gespeichert werden: {ex.Message}", AppNames.KnowledgeBase, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    /// <summary>Selects the group in the library lists, where export, delete and categories already work on selections.</summary>
    private void BrainSelectInLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (_brain is not { } graph || CurrentBrainGroup() is not { Nodes.Count: > 0 } group) return;
        var keys = group.Nodes.Select(i => graph.Nodes[i].Item)
            .Select(c => c.Source == SemanticSource.Clipboard ? ClipboardKey(c.EntryId) : c.FilePath)
            .Where(k => k != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = AllItems().Where(i => keys.Contains(i.FilePath)).ToList();
        if (items.Count == 0)
        {
            MessageBox.Show(this, "Keiner der hervorgehobenen Einträge ist noch in der Bibliothek.", AppNames.KnowledgeBase, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ClearSelection();
        _suppressSelection = true;
        foreach (var item in items) ListOf(item.ArticleKind).SelectedItems.Add(item);
        _suppressSelection = false;
        ArticleList_SelectionChanged(NotesList, null!);
        SetArea(AreaOf(items[0].ArticleKind));
        ListOf(items[0].ArticleKind).ScrollIntoView(items[0]);
        var selected = SelectedItems().Count();
        if (selected < items.Count)
            MessageBox.Show(this, $"{selected} von {items.Count} Artikeln ausgewählt; die übrigen blendet der Kategorienfilter der Bibliothek aus.",
                AppNames.KnowledgeBase, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BrainOpenPoints_Click(object sender, RoutedEventArgs e)
    {
        if (_brain is not { } graph || CurrentBrainGroup() is not { Nodes.Count: > 0 } group) return;
        var nodes = group.Nodes.Distinct().Select(i => graph.Nodes[i]).ToList();
        var sources = nodes.Select(n => new BrainSource(n.ItemKey, n.Item.Title, ReadBrainPreview(n), n.Item.FilePath ?? "", n.Item.Date)).ToList();
        var byKey = nodes.ToDictionary(n => n.ItemKey, StringComparer.Ordinal);
        CreateTasksFromOpenPoints(sources, key => BrainInsights.TaskKey(byKey[key].Item), $"{nodes.Count} hervorgehobenen Einträgen");
    }

    /// <summary>Finds marked open points in the sources, lets the user choose and creates the tasks.</summary>
    /// <param name="taskKeyOf">Source reference for the task (see <see cref="KbTask.SourceKey"/>) per item key.</param>
    private void CreateTasksFromOpenPoints(IReadOnlyList<BrainSource> sources, Func<string, string?> taskKeyOf, string scope)
    {
        var points = BrainOpenPoints.Find(sources, TasksView.Tasks.Select(t => t.Title));
        if (points.Count == 0)
        {
            MessageBox.Show(this, $"In den {scope} gibt es keine markierten offenen Punkte ohne passende Aufgabe.\n\n"
                + "Erkannt werden offene Kästchen (- [ ] …) und Zeilen, die mit TODO, FIXME, Offen:, Aufgabe: oder Nächster Schritt: beginnen.",
                AppNames.KnowledgeBase, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var choices = points.Select((p, k) => new BrainChoiceDialog.Choice
        {
            Key = k.ToString(System.Globalization.CultureInfo.InvariantCulture), Title = p.Title, Detail = "aus " + p.SourceTitle, Initial = true,
        }).ToList();
        var dialog = new BrainChoiceDialog("Offene Punkte als Aufgaben",
            $"Gefunden in {scope}. Markierte Punkte werden als Aufgaben angelegt; Herkunft und Umfeld bleiben bei der Aufgabe.",
            null, [new BrainChoiceDialog.Section($"OFFENE PUNKTE ({choices.Count})", choices)], "Aufgaben anlegen") { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var now = DateTime.Now;
        var created = dialog.CheckedKeys.Select(k => points[int.Parse(k, System.Globalization.CultureInfo.InvariantCulture)])
            .Select(p => BrainOpenPoints.ToTask(p, taskKeyOf(p.ItemKey), now)).ToList();
        if (created.Count == 0) return;
        try
        {
            var tasks = TaskStore.Load(AppPaths.TasksFile);
            tasks.AddRange(created);
            TaskStore.Save(AppPaths.TasksFile, tasks);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.LogCrash(ex);
            MessageBox.Show(this, $"Aufgaben konnten nicht gespeichert werden: {ex.Message}", AppNames.KnowledgeBase, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _app.NotifyTasksChanged();
        if (MessageBox.Show(this, $"{created.Count} {(created.Count == 1 ? "Aufgabe" : "Aufgaben")} angelegt. Zu den Aufgaben wechseln?",
                AppNames.KnowledgeBase, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
            OpenTasks(created[0].Id);
    }

    private void BrainCopyGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_brain is not { } graph || CurrentBrainGroup() is not { Nodes.Count: > 0 } group) return;
        var sb = new StringBuilder().Append("# ").Append(group.Title.ReplaceLineEndings(" ").Trim()).Append("\n\n");
        foreach (var node in group.Nodes.Distinct().Select(i => graph.Nodes[i]).OrderByDescending(n => n.Item.Date))
            sb.Append("- ").Append(node.Item.Title.ReplaceLineEndings(" ").Trim()).Append(" (").Append(SemanticChunk.SourceName(node.Source))
              .Append(", ").Append(node.Item.Date.ToString("dd.MM.yyyy HH:mm", De)).Append(")\n");
        BrainActionHint.Text = _app.CopyTextWithoutHistory(sb.ToString())
            ? $"{group.Nodes.Distinct().Count()} Einträge als Markdown kopiert."
            : "Zwischenablage ist gerade belegt.";
    }

    // ---- Focus from outside (dashboard, new collection) ------------------------------------------------------

    /// <summary>Highlights items by key once the net is built; a matching group of the current grouping is chosen as such.</summary>
    private void ApplyPendingBrainFocus()
    {
        if (_pendingBrainFocus is not { } focus || _brain == null || _brainBuilding) return;
        _pendingBrainFocus = null;
        var members = _brain.Nodes.Where(n => focus.Keys.Contains(n.ItemKey)).Select(n => n.Index).ToList();
        if (members.Count == 0) return;
        BrainCanvasView.Select(-1);
        var set = members.ToHashSet();
        var cluster = _brain.Clusters.FirstOrDefault(c => c.Members.Count == set.Count && c.Members.All(set.Contains));
        if (cluster != null)
        {
            _brainCluster = cluster.Id;
            _brainSubMembers = null;
            BrainManageButton.IsEnabled = true;
            _brainListUpdating = true;
            BrainList.SelectedItem = BrainList.Items.OfType<BrainListItem>().FirstOrDefault(i => i.Cluster == cluster.Id && i.Members == null && i.Node < 0);
            _brainListUpdating = false;
        }
        else
        {
            _brainCluster = -1;
            _brainSubMembers = members;
            _brainGroupTitle = focus.Title;
            BrainManageButton.IsEnabled = false;
        }
        BrainActionHint.Text = focus.Title;
        ApplyBrainHighlight();
        UpdateBrainActions();
        BrainCanvasView.CenterOn(members);
    }

    private void OpenBrainSection()
    {
        if (BrainNav.IsChecked == true) EnterBrain();
        else BrainNav.IsChecked = true;
        if (_brain != null && !_brainBuilding) ShowBrainOverview();
        ApplyPendingBrainFocus();
    }

    private void DashboardReview_Click(object sender, RoutedEventArgs e)
    {
        BrainPanelReview.IsChecked = true;
        OpenBrainSection();
    }

    private void DashboardReviewItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DashboardReviewItem item) return;
        _pendingBrainFocus = (item.Keys.ToHashSet(StringComparer.Ordinal), item.Title);
        BrainPanelReview.IsChecked = true;
        OpenBrainSection();
    }

    private void UpdateDashboardReview() => UpdateDashboardReviewAsync().Forget("Rückblick im Dashboard berechnen");

    /// <summary>Builds the net in the background with the Brain's grouping; skipped while nothing relevant changed.</summary>
    private async Task UpdateDashboardReviewAsync()
    {
        if (_view != KbView.Dashboard || _semanticClosed) return;
        var version = ++_dashboardReviewVersion;
        if (!EmbeddingModel.IsInstalled(EmbeddingModel.ResolveModelsDir(AppContext.BaseDirectory, AppPaths.ModelsDir)))
        {
            ShowDashboardReview("Der Rückblick braucht das Modell der semantischen Suche; es lässt sich im Abschnitt Suche laden.", []);
            return;
        }
        var snapshot = _app.Semantic.Snapshot();
        if (snapshot.Count == 0)
        {
            _dashboardReviewKey = null;
            ShowDashboardReview("Noch keine indizierten Einträge. Der Rückblick erscheint, sobald Suche oder Brain den Index aufgebaut haben.", []);
            return;
        }
        var options = CurrentBrainOptions();
        var tasks = TasksView.Tasks.ToList();
        var signature = SignatureOf(snapshot);
        var key = $"{signature.Count}|{signature.Hash}|{tasks.Count(t => !t.Done)}|{options.GroupByCollection}|{options.GroupByCategory}"
            + $"|{options.SemanticThreshold}|{options.MaxNodes}|{options.Sources?.Count}|{DateTime.Today:yyyyMMdd}|{options.Collections?.Sum(c => c.Keys.Count)}";
        if (key == _dashboardReviewKey) return;
        BrainReviewResult review;
        List<DashboardReviewItem> items;
        try
        {
            (review, items) = await Task.Run(() =>
            {
                var graph = KnowledgeGraph.Build(snapshot, options);
                var result = BrainInsights.Review(graph, tasks, DateTime.Now);
                return (result, result.Topics.Take(6).Select(t => new DashboardReviewItem(t.Label, t.Text,
                    graph.Clusters[t.Cluster].Members.Select(m => graph.Nodes[m].ItemKey).ToList())).ToList());
            });
        }
        catch (Exception ex)
        {
            App.LogCrash(ex);
            if (version == _dashboardReviewVersion) ShowDashboardReview("Der Rückblick konnte nicht berechnet werden.", []);
            return;
        }
        if (version != _dashboardReviewVersion || _semanticClosed) return;
        _dashboardReviewKey = key;
        ShowDashboardReview(review.Headline + (items.Count == 0 ? "" : " Ein Eintrag öffnet das Brain mit dieser Gruppe."), items);
    }

    private void ShowDashboardReview(string headline, List<DashboardReviewItem> items)
    {
        DashboardReviewHeadline.Text = headline;
        DashboardReviewList.ItemsSource = items;
    }
}

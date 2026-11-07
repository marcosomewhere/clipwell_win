using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ClipwellWin.Services;
using ClipwellWin.Models;
using Brush = System.Windows.Media.Brush;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ClipwellWin.Views;

/// <summary>KB section "Brain": the knowledge network built from the semantic index.</summary>
public partial class KnowledgeBaseWindow
{
    public sealed class BrainListItem
    {
        public required string Title { get; init; }
        public required string Meta { get; init; }
        public required Brush Brush { get; init; }
        public int Node { get; init; } = -1;
        public int Cluster { get; init; } = -1;
        /// <summary>Subcategory row in the category view: its members are highlighted.</summary>
        public IReadOnlyList<int>? Members { get; init; }
    }

    private IReadOnlyList<int>? _brainSubMembers;
    private bool _brainGroupingLoading;
    private bool ByCategory => BrainGroupCategories.IsChecked == true;

    private void BrainGrouping_Checked(object sender, RoutedEventArgs e)
    {
        if (_brainGroupingLoading || BrainGroupCategories == null || BrainBackButton == null) return;
        BrainBackButton.Content = ByCategory ? "Zurück zu Kategorien" : "Zurück zu Themen";
        if (!IsLoaded) return;
        SaveBrainSettings();
        // Topics and categories group differently, so the layout starts fresh.
        RebuildBrain(keepLayout: false);
    }

    private BrainGraph? _brain;
    private CancellationTokenSource? _brainLayoutCts;
    private DispatcherTimer? _brainSearchDebounce;
    private DispatcherTimer? _brainThresholdDebounce;
    private HashSet<int>? _brainSearchHits;
    private string? _brainSearchInfo;
    private int _brainVersion;
    private int _brainCluster = -1;
    private bool _brainLayoutRunning;
    private bool _brainListUpdating;
    private bool _brainBuilding;
    private bool _brainPreviewExpanded;
    private GridLength _libraryWidth = new(400);
    private bool _libraryCollapsed;

    private void UpdateBrainLibrary()
    {
        var collapsed = _view == KbView.Brain && BrainLibraryToggle.IsChecked != true;
        BrainLibraryToggle.Visibility = _view == KbView.Brain ? Visibility.Visible : Visibility.Collapsed;
        if (collapsed && !_libraryCollapsed) _libraryWidth = LibraryColumn.Width;
        LibraryColumn.MinWidth = collapsed ? 210 : 380;
        if (collapsed) LibraryColumn.Width = new GridLength(210);
        else if (_libraryCollapsed) LibraryColumn.Width = _libraryWidth;
        LibraryPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        _libraryCollapsed = collapsed;
    }

    private void BrainLibrary_Click(object sender, RoutedEventArgs e) => UpdateBrainLibrary();

    private void InitBrain()
    {
        // Before the debounce timers exist, so restoring the slider does not trigger a rebuild.
        LoadBrainSettings();
        _brainSearchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _brainSearchDebounce.Tick += (_, _) =>
        {
            _brainSearchDebounce.Stop();
            ApplyBrainWordSearch();
        };
        _brainThresholdDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _brainThresholdDebounce.Tick += (_, _) =>
        {
            _brainThresholdDebounce.Stop();
            SaveBrainSettings();
            RebuildBrain(keepLayout: true);
        };
        BrainCanvasView.NodeClicked += BrainCanvas_NodeClicked;
        BrainCanvasView.NodeDoubleClicked += node => { if (_brain != null) OpenChunkInKb(_brain.Nodes[node].Item); };
    }

    private void LoadBrainSettings()
    {
        var s = _app.CurrentSettings;
        s.NormalizeBrainSources();
        BrainThreshold.Value = s.EffectiveBrainThreshold();
        BrainThresholdLabel.Text = $"{BrainThreshold.Value * 100:0} %";
        BrainSrcClipboard.IsChecked = s.BrainShowClipboard;
        BrainSrcNotes.IsChecked = s.BrainShowNotes;
        BrainSrcRecordings.IsChecked = s.BrainShowRecordings;
        BrainEdgeSemantic.IsChecked = s.BrainEdgeSemantic;
        BrainEdgeTime.IsChecked = s.BrainEdgeTime;
        BrainEdgeSession.IsChecked = s.BrainEdgeSession;
        BrainEdgeDomain.IsChecked = s.BrainEdgeDomain;
        BrainEdgeSummary.IsChecked = s.BrainEdgeSummary;
        BrainEdgeManual.IsChecked = s.BrainEdgeManual;
        BrainEdgeCategory.IsChecked = s.BrainEdgeCategory;
        BrainFocus.IsChecked = s.BrainFocus;
        _brainGroupingLoading = true;
        (s.BrainGroupByCategory ? BrainGroupCategories : BrainGroupTopics).IsChecked = true;
        _brainGroupingLoading = false;
        BrainBackButton.Content = ByCategory ? "Zurück zu Kategorien" : "Zurück zu Themen";
        ApplyBrainEdgeFilter();
    }

    private void SaveBrainSettings()
    {
        var s = _app.CurrentSettings;
        s.BrainThreshold = Math.Round(BrainThreshold.Value, 2);
        s.BrainShowClipboard = BrainSrcClipboard.IsChecked == true;
        s.BrainShowNotes = BrainSrcNotes.IsChecked == true;
        s.BrainShowRecordings = BrainSrcRecordings.IsChecked == true;
        s.BrainEdgeSemantic = BrainEdgeSemantic.IsChecked == true;
        s.BrainEdgeTime = BrainEdgeTime.IsChecked == true;
        s.BrainEdgeSession = BrainEdgeSession.IsChecked == true;
        s.BrainEdgeDomain = BrainEdgeDomain.IsChecked == true;
        s.BrainEdgeSummary = BrainEdgeSummary.IsChecked == true;
        s.BrainEdgeManual = BrainEdgeManual.IsChecked == true;
        s.BrainEdgeCategory = BrainEdgeCategory.IsChecked == true;
        s.BrainGroupByCategory = BrainGroupCategories.IsChecked == true;
        s.BrainFocus = BrainFocus.IsChecked == true;
        try { _app.SaveSettings(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.LogCrash(ex); }
    }

    private void CloseBrain()
    {
        _brainSearchDebounce?.Stop();
        _brainThresholdDebounce?.Stop();
        _brainLayoutCts?.Cancel();
        ++_brainVersion;
    }

    private void EnterBrain()
    {
        _semanticModelInstalled = EmbeddingModel.IsInstalled(EmbeddingModel.ResolveModelsDir(AppContext.BaseDirectory, AppPaths.ModelsDir));
        if (!_semanticModelInstalled)
        {
            BrainEmpty.Text = "Das Brain braucht das Modell der semantischen Suche. Unter \"Semantische Suche\" kann es geladen werden.";
            BrainEmpty.Visibility = Visibility.Visible;
            UpdateBrainStatus();
            return;
        }
        StartSemanticSync();
        if (_brain == null || BrainIndexChanged()) RebuildBrain(keepLayout: _brain != null);
        else UpdateBrainStatus();
        Dispatcher.BeginInvoke(() => BrainCanvasView.Focus(), DispatcherPriority.Input);
    }

    private (int Count, int Hash) _brainIndexSignature;

    /// <summary>
    /// True when chunks were added, removed or re-embedded since the last build. Every sync publishes a new
    /// snapshot, so comparing references would relayout the net every second during a recording.
    /// </summary>
    private bool BrainIndexChanged() => SignatureOf(_app.Semantic.Snapshot()) != _brainIndexSignature;

    private static (int Count, int Hash) SignatureOf(IReadOnlyList<IndexedChunk> snapshot)
    {
        var hash = 0;
        foreach (var c in snapshot)
            hash = unchecked(hash + c.Chunk.Key.GetHashCode() * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(c.Vector));
        return (snapshot.Count, hash);
    }

    private BrainOptions CurrentBrainOptions()
    {
        var sources = new HashSet<SemanticSource>();
        if (BrainSrcClipboard.IsChecked == true) sources.Add(SemanticSource.Clipboard);
        if (BrainSrcNotes.IsChecked == true) sources.Add(SemanticSource.Note);
        if (BrainSrcRecordings.IsChecked == true) sources.Add(SemanticSource.Recording);
        return new BrainOptions
        {
            SemanticThreshold = (float)Math.Round(BrainThreshold.Value, 2), Sources = sources,
            Annotations = new Dictionary<string, BrainAnnotation>(_app.CurrentSettings.BrainAnnotations ?? []),
            Relations = (_app.CurrentSettings.BrainRelations ?? []).ToArray(),
            SummarySources = (_app.CurrentSettings.BrainSummaries ?? []).GroupBy(s => "n:" + s.NoteId + ".md")
                .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Last().SourceKeys.ToArray()),
            GroupByCategory = BrainGroupCategories.IsChecked == true,
        };
    }

    // ---- Build and layout ------------------------------------------------------------------------------------

    private async void RebuildBrain(bool keepLayout)
    {
        if (_semanticClosed || !_semanticModelInstalled) return;
        var version = ++_brainVersion;
        _brainLayoutCts?.Cancel();
        var snapshot = _app.Semantic.Snapshot();
        // Remember what this build is based on, so the next sync does not rebuild the same net again.
        _brainIndexSignature = SignatureOf(snapshot);
        var options = CurrentBrainOptions();
        var selectedKey = _brain != null && BrainCanvasView.SelectedNode >= 0 && BrainCanvasView.SelectedNode < _brain.Nodes.Count
            ? _brain.Nodes[BrainCanvasView.SelectedNode].ItemKey : null;

        Dictionary<string, (double X, double Y)>? previous = null;
        if (keepLayout && _brain != null)
        {
            var (px, py) = BrainCanvasView.Positions();
            previous = new Dictionary<string, (double, double)>();
            for (var i = 0; i < _brain.Nodes.Count && i < px.Length; i++) previous[_brain.Nodes[i].ItemKey] = (px[i], py[i]);
        }

        _brainBuilding = true;
        UpdateBrainStatus();
        BrainGraph graph;
        ForceLayout layout;
        try
        {
            (graph, layout) = await Task.Run(() =>
            {
                var g = KnowledgeGraph.Build(snapshot, options);
                return (g, new ForceLayout(g, previous));
            });
        }
        catch (Exception ex)
        {
            App.LogCrash(ex);
            if (version == _brainVersion) { _brainBuilding = false; StatusLabel.Text = $"Brain konnte nicht aufgebaut werden: {ex.Message}"; }
            return;
        }
        if (version != _brainVersion || _semanticClosed) return;
        _brainBuilding = false;

        _brain = graph;
        BrainRelationsButton.IsEnabled = graph.Nodes.Count > 0;
        _brainSearchHits = null;
        _brainSearchInfo = null;
        _brainCluster = -1;
        _brainSubMembers = null;
        var warm = previous != null && previous.Count > 0;
        BrainCanvasView.SetGraph(graph, layout.X, layout.Y, keepView: warm);
        BrainEmpty.Visibility = graph.Nodes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BrainEmpty.Text = snapshot.Count == 0
            ? "Der Index wird noch aufgebaut. Das Netz erscheint, sobald Einträge indiziert sind."
            : "Keine Einträge für die gewählten Quellen.";

        var selected = selectedKey == null ? -1 : graph.Nodes.FirstOrDefault(n => n.ItemKey == selectedKey)?.Index ?? -1;
        if (selected >= 0) SelectBrainNode(selected, center: false);
        else ShowBrainOverview();
        if (BrainQuery.Text.Trim().Length > 0) ApplyBrainWordSearch();

        _brainLayoutRunning = true;
        BrainCanvasView.AllowNodeDrag = false;
        UpdateBrainStatus();
        var cts = new CancellationTokenSource();
        _brainLayoutCts = cts;
        try
        {
            await Task.Run(() =>
            {
                var last = Environment.TickCount64;
                while (!layout.IsDone)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    layout.Step();
                    if (Environment.TickCount64 - last < 45) continue;
                    last = Environment.TickCount64;
                    var x = (double[])layout.X.Clone();
                    var y = (double[])layout.Y.Clone();
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (version != _brainVersion) return;
                        BrainCanvasView.UpdatePositions(x, y);
                        if (!warm && !BrainCanvasView.UserMovedView) BrainCanvasView.FitToView();
                    });
                }
            }, cts.Token);
        }
        catch (OperationCanceledException) { return; }
        if (version != _brainVersion || _semanticClosed) return;

        BrainCanvasView.UpdatePositions(layout.X, layout.Y);
        if (!warm && !BrainCanvasView.UserMovedView) BrainCanvasView.FitToView();
        _brainLayoutRunning = false;
        BrainCanvasView.AllowNodeDrag = true;
        UpdateBrainStatus();
    }

    private void UpdateBrainStatus()
    {
        var sourceCount = new[] { BrainSrcClipboard, BrainSrcNotes, BrainSrcRecordings }.Count(c => c.IsChecked == true);
        var edgeCount = new[] { BrainEdgeSemantic, BrainEdgeTime, BrainEdgeSession, BrainEdgeDomain, BrainEdgeSummary, BrainEdgeManual, BrainEdgeCategory }.Count(c => c.IsChecked == true);
        BrainFilters.Header = $"Filter · {sourceCount} Quellen · {edgeCount} Bezugsarten · ab {BrainThreshold.Value * 100:0} %";
        if (_view != KbView.Brain) return;
        if (!_semanticModelInstalled)
        {
            ContentSubtitle.Text = "Modell der semantischen Suche fehlt.";
            return;
        }
        var parts = new List<string>();
        if (_brain != null)
        {
            parts.Add($"{_brain.Nodes.Count:N0} Einträge");
            parts.Add($"{_brain.Edges.Count(e => BrainCanvasView.VisibleEdgeKinds.Contains(e.Kind)):N0} sichtbare Verknüpfungen");
            var manualCount = BrainCanvasView.VisibleEdgeKinds.Contains(GraphEdgeKind.Manual)
                ? _brain.Edges.Count(e => e.Kind == GraphEdgeKind.Manual) : 0;
            if (manualCount > 0) parts.Add($"davon {manualCount} manuell (gerichtet)");
            parts.Add(ByCategory ? $"{_brain.Clusters.Count:N0} Kategorien" : $"{_brain.Clusters.Count:N0} Themen");
        }
        if (_brainSearchInfo != null) parts.Add(_brainSearchInfo);
        if (_brainBuilding) parts.Add("baue Netz ...");
        else if (_brainLayoutRunning) parts.Add("ordne an ...");
        var index = _app.Semantic;
        if (index.IsSyncing && index.PendingTotal > 0) parts.Add($"indiziere {index.PendingDone:N0} von {index.PendingTotal:N0}");
        ContentSubtitle.Text = parts.Count == 0 ? "Wissensnetz" : string.Join(" · ", parts);
    }

    // ---- Selection and side panel ----------------------------------------------------------------------------

    private void BrainCanvas_NodeClicked(int node)
    {
        if (node < 0)
        {
            ShowBrainOverview();
            return;
        }
        SelectBrainNode(node, center: false);
    }

    private void SelectBrainNode(int node, bool center)
    {
        if (_brain == null || node < 0 || node >= _brain.Nodes.Count) return;
        _brainCluster = -1;
        _brainSubMembers = null;
        BrainCanvasView.Select(node);
        var n = _brain.Nodes[node];
        BrainDetail.Visibility = Visibility.Visible;
        BrainDetailRow.Height = new GridLength(2, GridUnitType.Star);
        BrainManageButton.IsEnabled = true;
        BrainDetailTitle.Text = n.Item.Title;
        BrainActionHint.Text = "Ausgewählter Eintrag und seine Verbindungen";
        BrainDetailMeta.Text = $"{SemanticChunk.SourceName(n.Source)} · {n.Item.Date.ToString("dd.MM.yyyy HH:mm", De)}"
            + (KnowledgeCategories.Effective(_app.CurrentSettings.BrainAnnotations, n.ItemKey) is { IsEmpty: false } path
                ? $" · Kategorie: {path}" : "")
            + $" · {n.Degree} Verknüpfungen insgesamt";
        _brainPreviewExpanded = false;
        BrainDetailText.MaxLines = 6;
        BrainDetailText.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        BrainDetailText.Text = ReadBrainPreview(n);
        BrainDetailText.ScrollToHome();
        Dispatcher.BeginInvoke(UpdateBrainPreviewButton, DispatcherPriority.Loaded);

        var neighbors = _brain.Neighbors(node)
            .Select(x => (x.Node, Edges: x.Edges.Where(e => BrainCanvasView.VisibleEdgeKinds.Contains(e.Kind)).ToList()))
            .Where(x => x.Edges.Count > 0).ToList();
        BrainLinksHeader.Text = $"SICHTBARE VERKNÜPFUNGEN ({neighbors.Count})";
        SetBrainList(neighbors.Select(x =>
        {
            var other = _brain.Nodes[x.Node];
            return new BrainListItem
            {
                Node = x.Node,
                Title = other.Item.Title,
                Meta = string.Join(" · ", x.Edges.Select(e => e.Detail))
                       + $" · {SemanticChunk.SourceName(other.Source)}, {other.Item.Date.ToString("dd.MM. HH:mm", De)}",
                Brush = BrushFor(other.Cluster),
            };
        }).ToList());

        ApplyBrainHighlight();
        if (center) BrainCanvasView.CenterOn([node]);
    }

    /// <summary>Every recording chunk repeats the window label; keep it once.</summary>
    private static string StripRecordingTitles(GraphNode n)
    {
        var label = n.Item.Title;
        var lines = n.Text.Split('\n').Where(l => l != label);
        return label + "\n\n" + string.Join("\n", lines);
    }

    private string ReadBrainPreview(GraphNode node)
    {
        try
        {
            if (node.Source == SemanticSource.Note && node.Item.FilePath is { } path && File.Exists(path))
                return NoteStore.KbBody(File.ReadAllText(path));
            if (node.Source == SemanticSource.Clipboard)
            {
                var entry = _app.SnapshotEntries().FirstOrDefault(e => e.Id == node.Item.EntryId);
                if (entry != null) return entry.Type == EntryType.Image ? entry.OcrText ?? "" : entry.Content ?? "";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.LogCrash(ex); }
        return node.Source == SemanticSource.Recording ? StripRecordingTitles(node) : node.Text;
    }

    private void BrainPreview_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (BrainPreviewMore != null) Dispatcher.BeginInvoke(UpdateBrainPreviewButton, DispatcherPriority.Loaded);
    }

    private void UpdateBrainPreviewButton()
    {
        BrainPreviewMore.Visibility = _brainPreviewExpanded || BrainDetailText.LineCount > 6 ? Visibility.Visible : Visibility.Collapsed;
        BrainPreviewMore.Content = _brainPreviewExpanded ? "Weniger anzeigen" : "Mehr anzeigen";
    }

    private void BrainPreviewMore_Click(object sender, RoutedEventArgs e)
    {
        _brainPreviewExpanded = !_brainPreviewExpanded;
        BrainDetailText.MaxLines = _brainPreviewExpanded ? 14 : 6;
        BrainDetailText.VerticalScrollBarVisibility = _brainPreviewExpanded ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        if (!_brainPreviewExpanded) BrainDetailText.ScrollToHome();
        UpdateBrainPreviewButton();
    }

    private void ShowBrainOverview()
    {
        _brainCluster = -1;
        _brainSubMembers = null;
        BrainManageButton.IsEnabled = false;
        BrainActionHint.Text = ByCategory ? "Eine Kategorie oder einen Eintrag auswählen." : "Ein Thema oder einen Eintrag auswählen.";
        BrainCanvasView.Select(-1);
        BrainDetail.Visibility = Visibility.Collapsed;
        BrainDetailRow.Height = new GridLength(0);
        if (_brain == null)
        {
            BrainLinksHeader.Text = "THEMEN";
            SetBrainList([]);
            return;
        }
        var unassigned = _brain.Nodes.Count(n => n.Cluster < 0);
        if (ByCategory)
        {
            ShowBrainCategories(unassigned);
            return;
        }
        BrainLinksHeader.Text = $"THEMEN ({_brain.Clusters.Count})" + (unassigned > 0 ? $" · {unassigned} Einträge ohne Thema" : "");
        SetBrainList(_brain.Clusters.OrderBy(c => c.Category).ThenByDescending(c => c.Members.Count).Select(c => new BrainListItem
        {
            Cluster = c.Id,
            Title = c.Label,
            Meta = (c.Category.Length > 0 ? $"{c.Category} · " : "") + $"{c.Members.Count} Einträge · " + string.Join(", ", c.Members
                .GroupBy(m => _brain.Nodes[m].Source)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Count()} {SourcePlural(g.Key)}")),
            Brush = BrushFor(c.Id),
        }).ToList());
        ApplyBrainHighlight();
    }

    /// <summary>Category view: main categories, each followed by its subcategories (which highlight their members).</summary>
    private void ShowBrainCategories(int unassigned)
    {
        BrainLinksHeader.Text = $"KATEGORIEN ({_brain!.Clusters.Count})" + (unassigned > 0 ? $" · {unassigned} Einträge ohne Kategorie (grau)" : "");
        var items = new List<BrainListItem>();
        foreach (var c in _brain.Clusters)
        {
            var withSub = c.Subgroups.Sum(s => s.Members.Count);
            items.Add(new BrainListItem
            {
                Cluster = c.Id,
                Title = c.Label,
                Meta = $"{c.Members.Count} Einträge" + (c.Subgroups.Count > 0 ? $" · {c.Subgroups.Count} Unterkategorien" : "")
                    + (withSub < c.Members.Count && c.Subgroups.Count > 0 ? $" · {c.Members.Count - withSub} ohne Unterkategorie" : "")
                    + " · " + SourceBreakdown(c.Members),
                Brush = BrushFor(c.Id),
            });
            items.AddRange(c.Subgroups.Select(s => new BrainListItem
            {
                Cluster = c.Id,
                Members = s.Members,
                Title = "    › " + s.Name,
                Meta = $"    {s.Members.Count} Einträge · " + SourceBreakdown(s.Members),
                Brush = BrushFor(c.Id),
            }));
        }
        if (items.Count == 0)
            BrainLinksHeader.Text = "KATEGORIEN · noch keine Einträge mit Kategorie. In der Bibliothek Einträge auswählen und Kategorie festlegen (Strg+Umschalt+K).";
        SetBrainList(items);
        ApplyBrainHighlight();
    }

    private string SourceBreakdown(IEnumerable<int> members) => string.Join(", ", members
        .GroupBy(m => _brain!.Nodes[m].Source).OrderByDescending(g => g.Count()).Select(g => $"{g.Count()} {SourcePlural(g.Key)}"));

    private static string SourcePlural(SemanticSource source) => source switch
    {
        SemanticSource.Clipboard => "Zwischenablage",
        SemanticSource.Note => "Sprachnotizen",
        _ => "Bildwechsel",
    };

    private static Brush BrushFor(int cluster)
    {
        var brush = new SolidColorBrush(BrainCanvas.ClusterColor(cluster));
        brush.Freeze();
        return brush;
    }

    private void SetBrainList(List<BrainListItem> items)
    {
        _brainListUpdating = true;
        BrainList.ItemsSource = items;
        BrainList.SelectedItem = null;
        _brainListUpdating = false;
    }

    private void BrainList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_brainListUpdating || _brain == null || BrainList.SelectedItem is not BrainListItem item) return;
        if (item.Node >= 0)
        {
            SelectBrainNode(item.Node, center: true);
            return;
        }
        if (item.Cluster < 0 || item.Cluster >= _brain.Clusters.Count) return;
        if (item.Members != null)
        {
            // A subcategory: highlighted only; actions work on topics and main categories.
            _brainCluster = -1;
            _brainSubMembers = item.Members;
            BrainManageButton.IsEnabled = false;
            BrainActionHint.Text = $"Unterkategorie: {_brain.Clusters[item.Cluster].Label} › {item.Title.Trim().TrimStart('›').Trim()}";
            ApplyBrainHighlight();
            BrainCanvasView.CenterOn(item.Members);
            return;
        }
        _brainCluster = item.Cluster;
        _brainSubMembers = null;
        BrainManageButton.IsEnabled = true;
        BrainActionHint.Text = (ByCategory ? "Kategorie: " : "Thema: ") + item.Title;
        ApplyBrainHighlight();
        BrainCanvasView.CenterOn(_brain.Clusters[item.Cluster].Members);
    }

    /// <summary>Priority: search hits, then focus on the selected node, then the chosen topic.</summary>
    private void ApplyBrainHighlight()
    {
        if (_brain == null) return;
        HashSet<int>? lit = null;
        var selected = BrainCanvasView.SelectedNode;
        if (_brainSearchHits != null)
        {
            lit = [.. _brainSearchHits];
            if (selected >= 0) lit.Add(selected);
        }
        else if (BrainFocus.IsChecked == true && selected >= 0)
        {
            lit = _brain.Neighborhood(selected, 2);
        }
        else if (_brainSubMembers != null)
        {
            lit = [.. _brainSubMembers];
        }
        else if (_brainCluster >= 0 && _brainCluster < _brain.Clusters.Count)
        {
            lit = [.. _brain.Clusters[_brainCluster].Members];
        }
        BrainCanvasView.SetHighlight(lit);
        UpdateBrainStatus();
    }

    // ---- Toolbar ---------------------------------------------------------------------------------------------

    private void BrainOverview_Click(object sender, RoutedEventArgs e) => ShowBrainOverview();

    private void BrainMeaning_Click(object sender, RoutedEventArgs e)
    {
        _brainSearchDebounce?.Stop();
        _ = ApplyBrainMeaningSearchAsync();
    }

    private void BrainClearSearch_Click(object sender, RoutedEventArgs e)
    {
        BrainQuery.Clear();
        ApplyBrainWordSearch();
        BrainQuery.Focus();
    }

    private void BrainQuery_TextChanged(object sender, TextChangedEventArgs e)
    {
        BrainQueryHint.Visibility = BrainQuery.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_brainSearchDebounce == null) return;
        _brainSearchDebounce.Stop();
        _brainSearchDebounce.Start();
    }

    private void BrainQuery_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        _brainSearchDebounce?.Stop();
        _ = ApplyBrainMeaningSearchAsync();
    }

    /// <summary>All query words must occur in title or text.</summary>
    private void ApplyBrainWordSearch()
    {
        if (_brain == null) return;
        var terms = SemanticRanking.Terms(BrainQuery.Text);
        if (BrainQuery.Text.Trim().Length == 0 || terms.Count == 0)
        {
            _brainSearchHits = null;
            _brainSearchInfo = null;
        }
        else
        {
            _brainSearchHits = _brain.Nodes
                .Where(n => terms.All(t => n.Item.Title.Contains(t, StringComparison.OrdinalIgnoreCase)
                                           || n.Text.Contains(t, StringComparison.OrdinalIgnoreCase)))
                .Select(n => n.Index)
                .ToHashSet();
            _brainSearchInfo = $"{_brainSearchHits.Count} Worttreffer";
        }
        ApplyBrainHighlight();
    }

    private async Task ApplyBrainMeaningSearchAsync()
    {
        var query = BrainQuery.Text.Trim();
        if (_brain == null) return;
        if (query.Length == 0)
        {
            _brainSearchHits = null;
            _brainSearchInfo = "Für die Suche nach Bedeutung zuerst einen Suchtext eingeben";
            ApplyBrainHighlight();
            BrainQuery.Focus();
            return;
        }
        var graph = _brain;
        var index = _app.Semantic;
        _brainSearchInfo = "suche nach Bedeutung …";
        UpdateBrainStatus();
        List<SemanticHit> hits;
        try
        {
            hits = await Task.Run(() => index.Search(query, null, 30));
        }
        catch (Exception ex)
        {
            App.LogCrash(ex);
            if (BrainQuery.Text.Trim() != query) return;
            _brainSearchHits = null;
            _brainSearchInfo = $"Suche nach Bedeutung fehlgeschlagen: {ex.Message}";
            ApplyBrainHighlight();
            return;
        }
        if (graph != _brain || BrainQuery.Text.Trim() != query) return;
        var byKey = graph.Nodes.ToDictionary(n => n.ItemKey, n => n.Index);
        var keys = hits.SelectMany(h => h.Duplicates.Prepend(h.Chunk)).Select(c => c.ItemKey).Distinct().ToList();
        _brainSearchHits = keys.Where(byKey.ContainsKey).Select(k => byKey[k]).ToHashSet();
        var outside = keys.Count - _brainSearchHits.Count;
        _brainSearchInfo = keys.Count == 0 ? "keine Treffer nach Bedeutung"
            : _brainSearchHits.Count == 0 ? $"{keys.Count} Treffer nach Bedeutung, aber keiner im angezeigten Netz (Quellenfilter oder älter als die neuesten 1000)"
            : outside > 0 ? $"{_brainSearchHits.Count} Treffer nach Bedeutung im Netz, {outside} außerhalb"
            : $"{_brainSearchHits.Count} Treffer nach Bedeutung";
        ApplyBrainHighlight();
        if (_brainSearchHits.Count > 0) BrainCanvasView.CenterOn(_brainSearchHits);
    }

    private void BrainSource_Click(object sender, RoutedEventArgs e)
    {
        if (BrainSrcClipboard.IsChecked != true && BrainSrcNotes.IsChecked != true && BrainSrcRecordings.IsChecked != true)
            ((System.Windows.Controls.CheckBox)sender).IsChecked = true;
        SaveBrainSettings();
        RebuildBrain(keepLayout: true);
    }

    private void BrainEdge_Click(object sender, RoutedEventArgs e)
    {
        ApplyBrainEdgeFilter();
        SaveBrainSettings();
        if (SelectedBrainNode is { } node) SelectBrainNode(node.Index, center: false);
        UpdateBrainStatus();
    }

    private void ApplyBrainEdgeFilter()
    {
        void Set(GraphEdgeKind kind, bool? on)
        {
            if (on == true) BrainCanvasView.VisibleEdgeKinds.Add(kind);
            else BrainCanvasView.VisibleEdgeKinds.Remove(kind);
        }
        Set(GraphEdgeKind.Semantic, BrainEdgeSemantic.IsChecked);
        Set(GraphEdgeKind.Time, BrainEdgeTime.IsChecked);
        Set(GraphEdgeKind.Session, BrainEdgeSession.IsChecked);
        Set(GraphEdgeKind.Domain, BrainEdgeDomain.IsChecked);
        Set(GraphEdgeKind.Summary, BrainEdgeSummary.IsChecked);
        Set(GraphEdgeKind.Manual, BrainEdgeManual.IsChecked);
        Set(GraphEdgeKind.Category, BrainEdgeCategory.IsChecked);
        BrainCanvasView.InvalidateVisual();
    }

    private void BrainThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BrainThresholdLabel != null) BrainThresholdLabel.Text = $"{e.NewValue * 100:0} %";
        if (_brainThresholdDebounce == null) return;
        _brainThresholdDebounce.Stop();
        _brainThresholdDebounce.Start();
    }

    private void BrainFocus_Click(object sender, RoutedEventArgs e)
    {
        ApplyBrainHighlight();
        SaveBrainSettings();
    }

    private void BrainRelayout_Click(object sender, RoutedEventArgs e) => RebuildBrain(keepLayout: false);

    private void BrainFit_Click(object sender, RoutedEventArgs e) => BrainCanvasView.FitToView();

    private GraphNode? SelectedBrainNode
        => _brain != null && BrainCanvasView.SelectedNode >= 0 && BrainCanvasView.SelectedNode < _brain.Nodes.Count
            ? _brain.Nodes[BrainCanvasView.SelectedNode] : null;

    private void BrainOpen_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedBrainNode is { } node) OpenChunkInKb(node.Item);
    }

    private void BrainSimilar_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedBrainNode is not { } node) return;
        SemanticNav.IsChecked = true;
        RunSimilar(node.Item);
    }

    /// <summary>Markdown for the copy button: the selected entry with its links, otherwise all topics.</summary>
    private string BrainCopyText()
    {
        if (_brain == null) return "";
        var sb = new StringBuilder();
        if (SelectedBrainNode is { } node)
        {
            sb.Append("# ").Append(node.Item.Title).Append("\n\n");
            sb.Append("- Quelle: ").Append(SemanticChunk.SourceName(node.Source)).Append('\n');
            sb.Append("- Datum: ").Append(node.Item.Date.ToString("dd.MM.yyyy HH:mm", De)).Append('\n');
            if (_brain.ClusterOf(node.Index) is { } c) sb.Append("- Thema: ").Append(c.Label).Append('\n');
            sb.Append("\n## Verknüpfungen\n\n");
            foreach (var (other, edges) in _brain.Neighbors(node.Index))
                sb.Append("- ").Append(_brain.Nodes[other].Item.Title).Append(" (")
                  .Append(string.Join(", ", edges.Select(e => e.Detail))).Append(")\n");
            return sb.ToString();
        }
        sb.Append("# Brain: Themen\n\n");
        foreach (var c in _brain.Clusters)
        {
            sb.Append("## ").Append(c.Label).Append(" (").Append(c.Members.Count).Append(")\n\n");
            foreach (var m in c.Members.OrderByDescending(m => _brain.Nodes[m].Item.Date))
            {
                var n = _brain.Nodes[m];
                sb.Append("- ").Append(n.Item.Title).Append(" (").Append(SemanticChunk.SourceName(n.Source))
                  .Append(", ").Append(n.Item.Date.ToString("dd.MM.yyyy HH:mm", De)).Append(")\n");
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }
}

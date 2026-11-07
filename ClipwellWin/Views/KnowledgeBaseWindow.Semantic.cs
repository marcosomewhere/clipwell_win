using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ClipwellWin.Models;
using ClipwellWin.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = ClipwellWin.Views.AppMessageBox;

namespace ClipwellWin.Views;

/// <summary>KB section "Semantische Suche": local embedding search, similar entries and duplicates.</summary>
public partial class KnowledgeBaseWindow
{
    private enum SemanticMode { Browse, Search, Similar, Duplicates }

    public sealed class SemanticResultItem : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _marked;
        public bool Marked
        {
            get => _marked;
            set { if (_marked == value) return; _marked = value; PropertyChanged?.Invoke(this, new(nameof(Marked))); }
        }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public required SemanticHit Hit { get; init; }
        public required string Title { get; init; }
        public required string Meta { get; init; }
        public required string Snippet { get; init; }
        public bool ShowTitle
        {
            get
            {
                var title = Regex.Replace(Title, @"\s+", " ").Trim().TrimStart('#', ' ').TrimEnd('…', '.');
                var snippet = Regex.Replace(Snippet, @"\s+", " ").Trim().TrimStart('#', ' ', '…');
                return title.Length > 0 && Title != "(ohne Titel)"
                    && !snippet.StartsWith(title, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private DispatcherTimer? _semanticDebounce;
    private CancellationTokenSource? _semanticSyncCts;
    private CancellationTokenSource? _semanticDownloadCts;
    private Task? _semanticSyncTask;
    private bool _semanticResync;
    private int _semanticVersion;
    private SemanticMode _semanticMode = SemanticMode.Browse;
    private SemanticChunk? _similarOrigin;
    // Rocchio feedback: marked items (and the query at that moment) that "Ähnliche zu Markierten" ranks by.
    private IReadOnlyList<string>? _similarMarked;
    private string _similarQuery = "";
    private string? _semanticError;
    private bool _semanticModelInstalled;
    private bool _semanticDownloading;
    private bool _semanticBusy;
    private bool _semanticClosed;

    private void InitSemantic()
    {
        _semanticDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _semanticDebounce.Tick += (_, _) =>
        {
            _semanticDebounce.Stop();
            if (_semanticMode == SemanticMode.Duplicates) RunDuplicates();
            else RunSemanticSearch(keepSelection: false);
        };
        _app.Semantic.Changed += Semantic_Changed;
    }

    private void CloseSemantic()
    {
        _semanticClosed = true;
        _semanticDebounce?.Stop();
        _app.Semantic.Changed -= Semantic_Changed;
        _semanticDownloadCts?.Cancel();
        _semanticSyncCts?.Cancel();
        var pending = _semanticSyncTask ?? Task.CompletedTask;
        var index = _app.Semantic;
        // The model needs a few hundred MB; free it once the running batch has stopped.
        _ = Task.Run(async () =>
        {
            await pending;
            index.ReleaseModel();
        });
    }

    private void Semantic_Changed(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(() =>
        {
            if (_semanticClosed) return;
            UpdateSemanticStatus();
            UpdateBrainStatus();
        });

    private void EnterSemantic()
    {
        _semanticModelInstalled = EmbeddingModel.IsInstalled(EmbeddingModel.ResolveModelsDir(AppContext.BaseDirectory, AppPaths.ModelsDir));
        UpdateSemanticModelPanel();
        UpdateSemanticStatus();
        if (_semanticModelInstalled) StartSemanticSync();
        if (!_semanticModelInstalled) ShowSemanticResults([], null, keepSelection: false, EmptySearchText());
        else if (_semanticMode is SemanticMode.Browse or SemanticMode.Search) RunSemanticSearch(keepSelection: true);
        else if (_semanticMode == SemanticMode.Duplicates) RunDuplicates(keepSelection: true);
        Dispatcher.BeginInvoke(FocusSemanticQuery, DispatcherPriority.Input);
    }

    private void FocusSemanticQuery()
    {
        if (!SemanticQuery.IsEnabled) return;
        SemanticQuery.Focus();
        SemanticQuery.SelectAll();
    }

    private SemanticSource? SelectedScope()
        => ScopeClipboard.IsChecked == true ? SemanticSource.Clipboard
            : ScopeNotes.IsChecked == true ? SemanticSource.Note
            : ScopeRecordings.IsChecked == true ? SemanticSource.Recording
            : null;

    // ---- Index sync ------------------------------------------------------------------------------------------

    private void StartSemanticSync()
    {
        if (!_semanticModelInstalled || _semanticClosed) return;
        if (_semanticSyncTask is { IsCompleted: false })
        {
            _semanticResync = true;
            return;
        }
        _semanticResync = false;
        _semanticError = null;
        _semanticSyncCts?.Dispose();
        _semanticSyncCts = new CancellationTokenSource();
        _semanticSyncTask = RunSemanticSyncAsync(_app.SnapshotEntries(), _app.Recorder.Directory, _semanticSyncCts.Token);
    }

    private async Task RunSemanticSyncAsync(IReadOnlyList<ClipboardEntry> entries, string recordingsDir, CancellationToken ct)
    {
        var index = _app.Semantic;
        try
        {
            var chunks = await Task.Run(() => index.Collect(entries, AppPaths.KbNotesDir, recordingsDir, AppPaths.NotesDir), ct);
            await index.SyncAsync(chunks, ct);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            App.LogCrash(ex);
            _semanticError = ex.Message;
        }
        if (_semanticClosed) return;
        UpdateSemanticStatus();
        if (_semanticResync)
        {
            // Run after this task has completed, otherwise StartSemanticSync would see it as still running.
            _ = Dispatcher.BeginInvoke(StartSemanticSync);
            return;
        }
        if (_view == KbView.Brain && BrainIndexChanged()) RebuildBrain(keepLayout: _brain != null);
        if (_semanticMode is SemanticMode.Search or SemanticMode.Browse) RunSemanticSearch(keepSelection: true);
        else if (_semanticMode == SemanticMode.Duplicates) RunDuplicates(keepSelection: true);
        else if (_semanticMode == SemanticMode.Similar && _similarMarked != null) RunSimilarToMarked(_similarMarked, _similarQuery);
        else if (_semanticMode == SemanticMode.Similar && _similarOrigin != null) RunSimilar(_similarOrigin);
    }

    private void UpdateSemanticStatus()
    {
        var index = _app.Semantic;
        var syncing = _semanticSyncTask is { IsCompleted: false };
        SemanticQuery.IsEnabled = _semanticModelInstalled;
        SemanticSearchButton.IsEnabled = _semanticModelInstalled;
        SemanticAskButton.IsEnabled = _semanticModelInstalled && !_askRunning;
        SemanticDuplicatesButton.IsEnabled = _semanticModelInstalled && !_semanticBusy;
        SemanticClearButton.IsEnabled = _semanticModelInstalled && !syncing;
        var duplicates = _semanticMode == SemanticMode.Duplicates;
        SemanticDuplicatePanel.Visibility = duplicates ? Visibility.Visible : Visibility.Collapsed;
        SemanticDuplicateThreshold.IsEnabled = SemanticDuplicateKind.SelectedIndex == 0;
        SemanticDuplicatePanel.Header = $"Dublettenfilter · {(SemanticDuplicateView.SelectedItem as ComboBoxItem)?.Content} · "
            + (SemanticDuplicateKind.SelectedIndex == 1 ? "gleicher Text" : $"ab {SemanticDuplicateThreshold.Value * 100:0} % ähnlich");
        SemanticQueryHint.Text = duplicates ? "Dubletten nach Text filtern" : "Frage oder Stichworte, z. B. wie habe ich das VPN-Zertifikat erneuert (Strg+Enter: Fragen)";
        if (duplicates)
            SemanticDuplicatesButton.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "OnboardingAccentBrush");
        else SemanticDuplicatesButton.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);
        UpdateSemanticMarks();
        if (_view != KbView.Semantic) return;

        string status;
        if (!_semanticModelInstalled) status = "Modell fehlt: einmalig laden, danach läuft alles lokal.";
        else if (_semanticError != null) status = $"Fehler: {_semanticError}";
        else if (index.IsSyncing && index.PendingTotal > 0)
            status = $"Indiziere {index.PendingDone:N0} von {index.PendingTotal:N0} neuen Abschnitten · {index.Count:N0} schon durchsuchbar";
        else if (syncing) status = "Prüfe Index ...";
        else if (_semanticBusy) status = "Suche ...";
        else
        {
            var counts = index.ItemCounts();
            status = $"Im Index: {counts.GetValueOrDefault(SemanticSource.Clipboard):N0} Zwischenablage-Einträge · " +
                     $"{counts.GetValueOrDefault(SemanticSource.Note):N0} Sprachnotizen · " +
                     $"{counts.GetValueOrDefault(SemanticSource.Recording):N0} Bildwechsel aus Bildschirmaufnahmen " +
                     $"({index.Count:N0} Abschnitte, lokal)";
        }

        var mode = _semanticMode switch
        {
            SemanticMode.Browse => "Alle indizierten Einträge, neueste zuerst · ",
            SemanticMode.Similar when _similarMarked != null => $"Ähnlich zu {_similarMarked.Count} markierten Einträgen"
                + (_similarQuery.Length > 0 ? $" und \"{_similarQuery}\"" : "") + " (Rocchio) · ",
            SemanticMode.Similar when _similarOrigin != null => $"Ähnlich zu \"{_similarOrigin.Title}\" · ",
            SemanticMode.Duplicates => $"Dublettenfilter · {SemanticResults.Items.Count} Ergebnisse · neueste {SemanticRanking.MaxDuplicateItems:N0} Einträge maximal · ",
            _ => "",
        };
        ContentSubtitle.Text = mode + status;
    }

    private void UpdateSemanticModelPanel()
    {
        SemanticModelPanel.Visibility = _semanticModelInstalled ? Visibility.Collapsed : Visibility.Visible;
        SemanticDownloadButton.IsEnabled = !_semanticDownloading;
        if (!_semanticDownloading)
            SemanticModelText.Text =
                $"Das mitgelieferte Modell multilingual-e5-small fehlt neben Merkautomat.exe (models\\e5-small) oder ist beschädigt. " +
                $"Es kann von Hugging Face neu geladen werden (ca. {EmbeddingModel.ApproxSizeMb} MB) und liegt dann unter {EmbeddingModel.DirectoryFor(AppPaths.ModelsDir)}. " +
                "Indizierung und Suche laufen danach komplett lokal.";
    }

    private async void SemanticDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_semanticDownloading) return;
        _semanticDownloading = true;
        UpdateSemanticModelPanel();
        _semanticDownloadCts?.Dispose();
        _semanticDownloadCts = new CancellationTokenSource();
        var totalMb = EmbeddingModel.Files.Sum(f => f.Size) >> 20;
        var progress = new Progress<long>(done => SemanticModelText.Text = $"Lade Modell ... {done >> 20} / {totalMb} MB");
        try
        {
            await EmbeddingModel.DownloadAsync(AppPaths.ModelsDir, progress, _semanticDownloadCts.Token);
        }
        catch (OperationCanceledException) when (_semanticClosed) { return; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException
                                       or UnauthorizedAccessException or OperationCanceledException)
        {
            App.LogCrash(ex);
            if (_semanticClosed) return;
            MessageBox.Show(this, $"Modell konnte nicht geladen werden: {ex.Message}", AppNames.App, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _semanticDownloading = false;
        }
        if (_semanticClosed) return;
        EnterSemantic();
    }

    private async void SemanticClear_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "Alle gespeicherten Vektoren der semantischen Suche löschen?\n\n" +
            "Zwischenablage, Sprachnotizen und Bildschirmaufnahmen bleiben unverändert. " +
            "Der Index wird bei der nächsten Suche oder beim nächsten Öffnen neu aufgebaut.",
            AppNames.App, MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        _semanticSyncCts?.Cancel();
        if (_semanticSyncTask != null) await _semanticSyncTask;
        try
        {
            await _app.Semantic.ClearAsync();
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            App.LogCrash(ex);
            _semanticError = ex.Message;
        }
        _semanticMode = SemanticMode.Browse;
        ShowSemanticResults([], null, keepSelection: false, "Index gelöscht. Mit Enter suchen baut ihn neu auf.");
    }

    // ---- Queries ---------------------------------------------------------------------------------------------

    private void SemanticQuery_TextChanged(object sender, TextChangedEventArgs e)
    {
        SemanticQueryHint.Visibility = SemanticQuery.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_semanticDebounce == null) return;
        _semanticDebounce.Stop();
        _semanticDebounce.Start();
    }

    private void SemanticQuery_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            _ = AskAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            SemanticSearchNow();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && SemanticResults.Items.Count > 0)
        {
            if (SemanticResults.SelectedIndex < 0) SemanticResults.SelectedIndex = 0;
            (SemanticResults.ItemContainerGenerator.ContainerFromIndex(SemanticResults.SelectedIndex) as ListBoxItem)?.Focus();
            e.Handled = true;
        }
    }

    private void SemanticSearch_Click(object sender, RoutedEventArgs e) => SemanticSearchNow();

    /// <summary>Explicit search: also picks up clipboard entries and files added since the last sync.</summary>
    private void SemanticSearchNow()
    {
        _semanticDebounce?.Stop();
        StartSemanticSync();
        if (_semanticMode == SemanticMode.Duplicates) RunDuplicates();
        else RunSemanticSearch(keepSelection: false);
    }

    private void SemanticScope_Checked(object sender, RoutedEventArgs e)
    {
        if (_semanticDebounce == null) return;
        switch (_semanticMode)
        {
            case SemanticMode.Similar when _similarMarked != null:
                RunSimilarToMarked(_similarMarked, _similarQuery);
                break;
            case SemanticMode.Similar when _similarOrigin != null:
                RunSimilar(_similarOrigin);
                break;
            case SemanticMode.Duplicates:
                RunDuplicates();
                break;
            default:
                RunSemanticSearch(keepSelection: true);
                break;
        }
    }

    private async void RunSemanticSearch(bool keepSelection)
    {
        if (!_semanticModelInstalled || _semanticClosed) return;
        _semanticMode = SemanticMode.Search;
        _similarOrigin = null;
        _similarMarked = null;
        var query = SemanticQuery.Text.Trim();
        var source = SelectedScope();
        var index = _app.Semantic;
        if (query.Length == 0)
        {
            // Without a query the list shows what is in the index, so it is clear what the search covers.
            _semanticMode = SemanticMode.Browse;
            await RunSemanticQuery(() => index.Browse(source), null, keepSelection,
                index.IsSyncing ? "Der Index wird aufgebaut, Einträge erscheinen nach und nach." : EmptySearchText());
            return;
        }
        await RunSemanticQuery(() => index.Search(query, source), query, keepSelection,
            index.Count == 0 ? "Der Index wird noch aufgebaut, Treffer erscheinen nach und nach." : "Keine passenden Einträge.");
    }

    private void SemanticSimilar_Click(object sender, RoutedEventArgs e)
    {
        if (SemanticResults.SelectedItem is SemanticResultItem item) RunSimilar(item.Hit.Chunk);
    }

    private async void RunSimilar(SemanticChunk origin)
    {
        _semanticMode = SemanticMode.Similar;
        _similarOrigin = origin;
        _similarMarked = null;
        var source = SelectedScope();
        var index = _app.Semantic;
        await RunSemanticQuery(() => index.Similar(origin.ItemKey, source), null, keepSelection: false,
            "Keine inhaltlich ähnlichen Einträge gefunden.");
    }

    private void SemanticSimilarMarked_Click(object sender, RoutedEventArgs e)
    {
        var marked = SemanticItems.Where(i => i.Marked).Select(i => i.Hit.Chunk.ItemKey).Distinct().ToList();
        if (marked.Count == 0 || _semanticBusy) return;
        // In the marked-similar view the query box keeps the text the search started with.
        var query = _semanticMode == SemanticMode.Similar && _similarMarked != null ? _similarQuery : SemanticQuery.Text.Trim();
        RunSimilarToMarked(marked, query);
    }

    private async void RunSimilarToMarked(IReadOnlyList<string> marked, string query)
    {
        if (!_semanticModelInstalled || _semanticClosed) return;
        _semanticMode = SemanticMode.Similar;
        _similarOrigin = null;
        _similarMarked = marked;
        _similarQuery = query;
        var source = SelectedScope();
        var index = _app.Semantic;
        await RunSemanticQuery(() => index.SimilarToMarked(marked, query, source), query.Length > 0 ? query : null, keepSelection: true,
            "Keine inhaltlich ähnlichen Einträge zu den Markierten gefunden.");
    }

    private void SemanticDuplicates_Click(object sender, RoutedEventArgs e)
    {
        _semanticDebounce?.Stop();
        if (_semanticMode == SemanticMode.Duplicates) { ExitDuplicateFilter(); return; }
        StartSemanticSync();
        RunDuplicates();
    }

    private void SemanticDuplicateExit_Click(object sender, RoutedEventArgs e) => ExitDuplicateFilter();

    private void ExitDuplicateFilter()
    {
        _semanticDebounce?.Stop();
        _semanticMode = SemanticMode.Browse;
        RunSemanticSearch(keepSelection: false);
    }

    private void SemanticDuplicateOptions_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_semanticDebounce == null || _semanticMode != SemanticMode.Duplicates) return;
        foreach (var item in SemanticItems) item.Marked = false;
        UpdateSemanticMarks();
        _semanticDebounce.Stop();
        _semanticDebounce.Start();
    }

    private void SemanticDuplicateThreshold_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SemanticDuplicateThresholdLabel != null) SemanticDuplicateThresholdLabel.Text = $"{e.NewValue * 100:0} %";
        if (_semanticDebounce == null || _semanticMode != SemanticMode.Duplicates) return;
        foreach (var item in SemanticItems) item.Marked = false;
        UpdateSemanticMarks();
        _semanticDebounce.Stop();
        _semanticDebounce.Start();
    }

    private async void RunDuplicates(bool keepSelection = false)
    {
        if (!_semanticModelInstalled) return;
        _semanticMode = SemanticMode.Duplicates;
        _similarOrigin = null;
        _similarMarked = null;
        var source = SelectedScope();
        var index = _app.Semantic;
        var view = (DuplicateView)Math.Max(0, SemanticDuplicateView.SelectedIndex);
        var threshold = (float)Math.Round(SemanticDuplicateThreshold.Value, 2);
        var exact = SemanticDuplicateKind.SelectedIndex == 1;
        var query = SemanticQuery.Text.Trim();
        SemanticDuplicateThresholdLabel.Text = $"{threshold * 100:0} %";
        await RunSemanticQuery(() => index.Duplicates(source, view, threshold, exact, query), query, keepSelection,
            "Keine Einträge für diesen Dublettenfilter. Quelle, Textfilter oder Ähnlichkeit anpassen.");
    }

    private async Task RunSemanticQuery(Func<List<SemanticHit>> query, string? terms, bool keepSelection, string emptyText)
    {
        var version = ++_semanticVersion;
        _semanticBusy = true;
        UpdateSemanticStatus();
        List<SemanticHit> hits;
        try
        {
            hits = await Task.Run(query);
        }
        catch (Exception ex)
        {
            App.LogCrash(ex);
            if (version == _semanticVersion) _semanticError = ex.Message;
            hits = [];
        }
        finally
        {
            if (version == _semanticVersion) _semanticBusy = false;
        }
        if (version != _semanticVersion || _semanticClosed) return;
        ShowSemanticResults(hits, terms, keepSelection, emptyText);
    }

    private string EmptySearchText()
        => _semanticModelInstalled
            ? $"Noch nichts indiziert. Die Suche umfasst Zwischenablage (Text, Code, URLs, OCR-Text von Bildern), Sprachnotizen im {AppNames.KnowledgeBase} und Bildschirmaufnahmen."
            : "Für die semantische Suche zuerst oben das Modell laden.";

    // ---- Results ---------------------------------------------------------------------------------------------

    private void ShowSemanticResults(List<SemanticHit> hits, string? terms, bool keepSelection, string emptyText)
    {
        var previous = keepSelection ? (SemanticResults.SelectedItem as SemanticResultItem)?.Hit.Chunk.ItemKey : null;
        var marked = keepSelection ? SemanticItems.Where(i => i.Marked).ToDictionary(i => i.Hit.Chunk.ItemKey,
            i => (SemanticMarksGroups ? new[] { i.Hit.Chunk }.Concat(i.Hit.Duplicates) : [i.Hit.Chunk]).Select(c => c.ItemKey).ToHashSet()) : [];
        var items = hits.Select(h => ToResultItem(h, terms)).ToList();
        foreach (var item in items)
            if (marked.TryGetValue(item.Hit.Chunk.ItemKey, out var keys)
                && keys.SetEquals((SemanticMarksGroups ? new[] { item.Hit.Chunk }.Concat(item.Hit.Duplicates) : [item.Hit.Chunk]).Select(c => c.ItemKey)))
                item.Marked = true;
        SemanticResults.ItemsSource = items;
        SemanticEmpty.Text = emptyText;
        SemanticEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var select = items.FirstOrDefault(i => i.Hit.Chunk.ItemKey == previous) ?? items.FirstOrDefault();
        SemanticResults.SelectedItem = select;
        if (select != null) SemanticResults.ScrollIntoView(select);
        UpdateSemanticStatus();
        UpdateSemanticPreview();
        UpdateSemanticMarks();
    }

    private SemanticResultItem ToResultItem(SemanticHit hit, string? terms)
    {
        var c = hit.Chunk;
        var meta = new List<string>();
        if (_semanticMode == SemanticMode.Duplicates && SemanticDuplicateView.SelectedIndex == 0)
            meta.Add($"{hit.Duplicates.Count + 1} Einträge in dieser Gruppe");
        meta.Add(SemanticChunk.SourceName(c.Source));
        meta.Add(FormatHitDate(c));
        if (_semanticMode is SemanticMode.Search or SemanticMode.Similar)
        {
            meta.Add($"{hit.Similarity * 100:0} % ähnlich");
            if (hit.LexicalMatch) meta.Add("Worttreffer");
            if (hit.Duplicates.Count > 0) meta.Add($"+{hit.Duplicates.Count} fast gleich");
        }
        return new SemanticResultItem
        {
            Hit = hit,
            Title = c.Title.Length == 0 ? "(ohne Titel)" : c.Title,
            Meta = string.Join(" · ", meta),
            Snippet = SnippetOf(BodyOf(c), terms),
        };
    }

    private static string FormatHitDate(SemanticChunk c)
        => c.Date.ToString(c.Source == SemanticSource.Recording ? "dd.MM.yyyy HH:mm:ss" : "dd.MM.yyyy HH:mm", De);

    /// <summary>Recording chunks start with the window label, which the title already shows.</summary>
    private static string BodyOf(SemanticChunk c)
    {
        if (c.Source != SemanticSource.Recording) return c.Text;
        var newline = c.Text.IndexOf('\n');
        return newline < 0 ? c.Text : c.Text[(newline + 1)..];
    }

    private static string SnippetOf(string text, string? terms)
    {
        var flat = Regex.Replace(text, @"\s+", " ").Trim();
        var start = 0;
        if (terms != null)
        {
            foreach (var term in SemanticRanking.Terms(terms))
            {
                var at = flat.IndexOf(term, StringComparison.OrdinalIgnoreCase);
                if (at < 0) continue;
                start = Math.Max(0, at - 60);
                break;
            }
        }
        var snippet = flat.Length - start > 220 ? flat.Substring(start, 220) + "…" : flat[start..];
        return start > 0 ? "…" + snippet : snippet;
    }

    private void SemanticResults_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSemanticPreview();

    private void UpdateSemanticPreview()
    {
        var item = SemanticResults.SelectedItem as SemanticResultItem;
        SemanticSimilarButton.IsEnabled = item != null;
        SemanticOpenButton.IsEnabled = item != null;
        if (item == null)
        {
            SemanticPreviewTitle.Text = "Kein Treffer ausgewählt";
            SemanticPreview.Text = "";
            return;
        }

        var c = item.Hit.Chunk;
        SemanticPreviewTitle.Text = $"{SemanticChunk.SourceName(c.Source)} · {FormatHitDate(c)} · {item.Title}";
        var sb = new StringBuilder();
        if (c.Source == SemanticSource.Recording) sb.Append("## ").Append(c.Title).Append("\n\n");
        sb.Append(FullTextOf(c).TrimEnd()).Append('\n');
        if (item.Hit.Duplicates.Count > 0)
        {
            sb.Append("\n---\n\nWeitere Einträge dieser Gruppe (").Append(item.Hit.Duplicates.Count).Append("):\n\n");
            foreach (var d in item.Hit.Duplicates)
                sb.Append("- ").Append(FormatHitDate(d)).Append(" · ").Append(SemanticChunk.SourceName(d.Source))
                  .Append(" · ").Append(d.Title).Append('\n');
        }
        SemanticPreview.Text = sb.ToString();
        SemanticPreview.ScrollToHome();
    }

    /// <summary>Clipboard hits show the whole entry; notes and recordings the matching passage.</summary>
    private string FullTextOf(SemanticChunk c)
    {
        if (c.Source != SemanticSource.Clipboard) return BodyOf(c);
        var entry = _app.SnapshotEntries().FirstOrDefault(x => x.Id == c.EntryId);
        var full = entry?.Type == EntryType.Image ? entry.OcrText : entry?.Content;
        return string.IsNullOrWhiteSpace(full) ? c.Text : full;
    }

    private void SemanticResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SemanticResults.SelectedItem is SemanticResultItem) SemanticOpen_Click(sender, e);
    }

    private void SemanticOpen_Click(object sender, RoutedEventArgs e)
    {
        if (SemanticResults.SelectedItem is SemanticResultItem selected) OpenChunkInKb(selected.Hit.Chunk);
    }

    /// <summary>Selects the article behind a chunk in its KB list; recordings scroll to the snapshot.</summary>
    private void OpenChunkInKb(SemanticChunk c)
    {
        var key =c.Source == SemanticSource.Clipboard ? ClipboardKey(c.EntryId) : c.FilePath;
        var item = AllItems().FirstOrDefault(i => string.Equals(i.FilePath, key, StringComparison.OrdinalIgnoreCase));
        if (item == null && c.Source == SemanticSource.Note && c.FilePath is { } notePath && File.Exists(notePath)
            && string.Equals(Path.GetDirectoryName(notePath), AppPaths.NotesDir, StringComparison.OrdinalIgnoreCase))
        {
            Reload();
            item = AllItems().FirstOrDefault(i => string.Equals(i.FilePath, key, StringComparison.OrdinalIgnoreCase));
        }
        if (item == null)
        {
            StatusLabel.Text = c.Source == SemanticSource.Clipboard
                ? "Der Eintrag ist nicht mehr in der Zwischenablage-History."
                : $"Der Artikel ist nicht mehr im {AppNames.KnowledgeBase}.";
            return;
        }
        if (!FilterItem(item))
        {
            SearchBox.Text = "";
            _searchDebounce.Stop();
            _notesView?.Refresh();
            _recordingsView?.Refresh();
            _clipboardView?.Refresh();
            UpdateListState();
        }

        ClearSelection();
        SetArea(AreaOf(item.ArticleKind));
        var list = ListOf(item.ArticleKind);
        list.SelectedItems.Add(item);
        list.ScrollIntoView(item);
        if (c.Source == SemanticSource.Recording) ScrollArticleTo(c.Date);
    }

    private void ScrollArticleTo(DateTime snapshotTime)
    {
        var marker = $"## {snapshotTime.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)} - ";
        var at = MarkdownView.Text.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (_formattedMarkdown)
            {
                var heading = FormattedMarkdownView.Document.Blocks.OfType<System.Windows.Documents.Paragraph>()
                    .FirstOrDefault(p => p.Tag is string source && source.StartsWith(marker, StringComparison.Ordinal));
                heading?.BringIntoView();
                return;
            }
            var line = MarkdownView.GetLineIndexFromCharacterIndex(at);
            if (line >= 0) MarkdownView.ScrollToLine(line);
            MarkdownView.Select(at, 0);
        }, DispatcherPriority.Loaded);
    }
}

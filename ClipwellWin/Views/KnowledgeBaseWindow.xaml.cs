using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ClipwellWin.Services;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ListBox = System.Windows.Controls.ListBox;
using MessageBox = System.Windows.MessageBox;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace ClipwellWin.Views;

public partial class KnowledgeBaseWindow : Window
{
    private enum KbView { Dashboard, Overview, Articles }

    /// <summary>The section whose creation panel is shown below the lists.</summary>
    private enum KbArea { None, Notes, Recordings }

    public sealed class KbItem
    {
        public required KbArticleKind ArticleKind { get; init; }
        public required string Title { get; init; }
        public required string Subtitle { get; init; }
        public required DateTime Date { get; init; }
        public required string FilePath { get; init; }
        public RecordingSession? Session { get; init; }
        public KbNote? Note { get; init; }

        public string KindName => KnowledgeExport.KindName(ArticleKind);
        public string DateLabel => Date.ToString("dd.MM.yyyy HH:mm", De);
    }

    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private static readonly SolidColorBrush RecordingRed = new(Color.FromRgb(0xE8, 0x3B, 0x3B));
    private static readonly SolidColorBrush IdleGray = new(Color.FromRgb(0x6B, 0x6B, 0x6B));

    private readonly App _app;
    private readonly DispatcherTimer _refreshDebounce;
    private readonly DispatcherTimer _searchDebounce;
    private readonly DispatcherTimer _recordTimer;
    private readonly InlineDictation _noteDictation;
    private List<KbItem> _noteItems = [];
    private List<KbItem> _recordingItems = [];
    private ICollectionView? _notesView;
    private ICollectionView? _recordingsView;
    private List<RecordingSession> _sessions = [];
    private List<KbNote> _notes = [];
    private KbView _view = KbView.Dashboard;
    private KbArea _area = KbArea.None;
    private bool _suppressSelection;
    private string? _pendingSelectPath;
    private bool _selectRecordingOnStart;

    public KnowledgeBaseWindow(App app)
    {
        _app = app;
        InitializeComponent();
        EditorChrome.ApplyTheme(this);
        App.ApplyAppIcon(this);
        var work = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(Width, work.Width - 40));
        Height = Math.Max(MinHeight, Math.Min(Height, work.Height - 40));

        _refreshDebounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshDebounce.Tick += (_, _) => { _refreshDebounce.Stop(); Reload(); };
        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            _notesView?.Refresh();
            _recordingsView?.Refresh();
            UpdateListState();
        };
        _recordTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _recordTimer.Tick += (_, _) => UpdateRecordStatus();

        _app.RecordingStateChanged += App_RecordingStateChanged;
        _app.Recorder.SnapshotWritten += Recorder_SnapshotWritten;
        _app.KnowledgeBaseChanged += App_KnowledgeBaseChanged;
        _noteDictation = new InlineDictation(_app, this, NoteInput);
        _noteDictation.Changed += UpdateNoteDictationUi;
        Closed += (_, _) =>
        {
            _noteDictation.Dispose();
            _app.RecordingStateChanged -= App_RecordingStateChanged;
            _app.Recorder.SnapshotWritten -= Recorder_SnapshotWritten;
            _app.KnowledgeBaseChanged -= App_KnowledgeBaseChanged;
            _refreshDebounce.Stop();
            _searchDebounce.Stop();
            _recordTimer.Stop();
        };

        UpdateNoteDictationUi();
        Reload();
        UpdateRecordButton();
        SetArea(KbArea.None);
        DashboardNav.IsChecked = true;
    }

    private void Window_SourceInitialized(object sender, EventArgs e) => EditorChrome.ApplyFrame(this);

    private void App_RecordingStateChanged(object? sender, EventArgs e)
    {
        if (_selectRecordingOnStart && _app.Recorder.IsRecording) _pendingSelectPath = _app.Recorder.CurrentSessionPath;
        _selectRecordingOnStart = false;
        Reload();
        UpdateRecordButton();
    }

    private void Recorder_SnapshotWritten(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(() => { if (!_refreshDebounce.IsEnabled) _refreshDebounce.Start(); });

    private void App_KnowledgeBaseChanged(object? sender, EventArgs e) => Reload();

    private void UpdateRecordButton()
    {
        var recording = _app.Recorder.IsRecording;
        RecordIcon.Text = recording ? "" : "";
        RecordIcon.Foreground = recording ? RecordingRed : (Brush)FindResource("OnboardingTextPrimaryBrush");
        RecordLabel.Text = recording ? "Aufnahme beenden" : "Aufnahme starten";
        HeaderStateDot.Fill = recording ? RecordingRed : IdleGray;
        HeaderStateLabel.Text = recording ? "Bildschirmaufnahme läuft" : "Keine Aufnahme";
        if (recording) _recordTimer.Start(); else _recordTimer.Stop();
        UpdateRecordStatus();
    }

    private void UpdateRecordStatus()
    {
        var path = _app.Recorder.CurrentSessionPath;
        var session = path == null ? null
            : _sessions.FirstOrDefault(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));
        if (!_app.Recorder.IsRecording)
            RecordStatus.Text = "Liest den sichtbaren Text des aktiven Fensters, keine Bildaufnahme.";
        else if (session == null)
            RecordStatus.Text = "Aufnahme läuft...";
        else
            RecordStatus.Text = $"Läuft seit {session.Start:HH:mm} · {RecordingMarkdown.FormatDuration(DateTime.Now - session.Start)} · {session.Snapshots.Count} Einträge";
    }

    // ---- Areas ---------------------------------------------------------------------------------------------

    private void SetArea(KbArea area)
    {
        _area = area;
        NotesHeader.Tag = area == KbArea.Notes ? "active" : null;
        RecordingsHeader.Tag = area == KbArea.Recordings ? "active" : null;
        NoteComposer.Visibility = area == KbArea.Notes ? Visibility.Visible : Visibility.Collapsed;
        RecordingComposer.Visibility = area == KbArea.Recordings ? Visibility.Visible : Visibility.Collapsed;
    }

    private KbArea AreaOf(ListBox list) => list == NotesList ? KbArea.Notes : KbArea.Recordings;

    private ListBox ListOf(KbArticleKind kind) => kind == KbArticleKind.Note ? NotesList : RecordingsList;

    private void NotesHeader_Click(object sender, MouseButtonEventArgs e) => SetArea(KbArea.Notes);

    private void RecordingsHeader_Click(object sender, MouseButtonEventArgs e) => SetArea(KbArea.Recordings);

    private void ArticleList_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is ListBox list) SetArea(AreaOf(list));
    }

    /// <summary>A plain click starts a new selection; Ctrl or Shift keeps what is selected in the other area.</summary>
    private void ArticleList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox list) return;
        SetArea(AreaOf(list));
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0) return;
        var other = list == NotesList ? RecordingsList : NotesList;
        if (other.SelectedItems.Count == 0) return;
        _suppressSelection = true;
        other.UnselectAll();
        _suppressSelection = false;
        // Clicking an item that is already selected raises no SelectionChanged, so refresh here.
        ArticleList_SelectionChanged(list, null!);
    }

    // ---- Loading -------------------------------------------------------------------------------------------

    private void Reload()
    {
        var selectedPaths = SelectedItems().Select(i => i.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pending = _pendingSelectPath;
        _pendingSelectPath = null;
        if (pending != null) selectedPaths = new HashSet<string>([pending], StringComparer.OrdinalIgnoreCase);

        try { _sessions = RecordingMarkdown.LoadSessions(_app.Recorder.Directory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.LogCrash(ex); _sessions = []; }
        try { _notes = NoteStore.LoadKbNotes(AppPaths.KbNotesDir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.LogCrash(ex); _notes = []; }

        _noteItems = BuildNoteItems(_notes);
        _recordingItems = BuildRecordingItems(_sessions, _app.Recorder.CurrentSessionPath);

        // Show a just-created item even if the search would hide it.
        var target = pending == null ? null
            : _noteItems.Concat(_recordingItems).FirstOrDefault(i => string.Equals(i.FilePath, pending, StringComparison.OrdinalIgnoreCase));
        if (target != null && !FilterItem(target)) SearchBox.Text = "";

        _suppressSelection = true;
        _notesView = Bind(NotesList, _noteItems);
        _recordingsView = Bind(RecordingsList, _recordingItems);
        foreach (var item in _noteItems.Concat(_recordingItems).Where(i => selectedPaths.Contains(i.FilePath)))
            ListOf(item.ArticleKind).SelectedItems.Add(item);
        _suppressSelection = false;

        if (target != null && SelectedItems().Contains(target))
        {
            _view = KbView.Articles;
            DashboardNav.IsChecked = false;
            OverviewNav.IsChecked = false;
            SetArea(target.ArticleKind == KbArticleKind.Note ? KbArea.Notes : KbArea.Recordings);
            ListOf(target.ArticleKind).ScrollIntoView(target);
        }

        UpdateDashboard();
        UpdateListState();
        if (_view == KbView.Articles && !SelectedItems().Any()) DashboardNav.IsChecked = true;
        else ShowCurrent(keepScroll: true);
        StatusLabel.Text = $"{_notes.Count} Sprachnotizen · {_sessions.Count} Bildschirmaufnahmen · Ordner: {AppPaths.DataDir}";
    }

    private ICollectionView Bind(ListBox list, List<KbItem> items)
    {
        var view = CollectionViewSource.GetDefaultView(items);
        view.Filter = FilterItem;
        list.ItemsSource = view;
        return view;
    }

    private static List<KbItem> BuildNoteItems(IEnumerable<KbNote> notes)
        => notes.Select(n => new KbItem
            {
                ArticleKind = KbArticleKind.Note,
                Title = n.Title,
                Subtitle = n.Exported.ToString("dd.MM.yyyy HH:mm", De),
                Date = n.Exported,
                FilePath = n.Path,
                Note = n,
            })
            .OrderByDescending(i => i.Date)
            .ToList();

    private static List<KbItem> BuildRecordingItems(IEnumerable<RecordingSession> sessions, string? currentSession)
        => sessions.Select(s =>
            {
                var running = string.Equals(s.Path, currentSession, StringComparison.OrdinalIgnoreCase);
                var apps = s.Snapshots.Select(x => x.App).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
                return new KbItem
                {
                    ArticleKind = KbArticleKind.Recording,
                    Title = apps.Count == 0 ? "Ohne erfasste Fenster" : string.Join(", ", apps),
                    Subtitle = $"{s.Start.ToString("dd.MM.yyyy HH:mm", De)} · {s.Snapshots.Count} Einträge · {RecordingMarkdown.FormatDuration(s.Duration)}"
                        + (running ? " · läuft" : s.IsOpen ? " · nicht abgeschlossen" : ""),
                    Date = s.Start,
                    FilePath = s.Path,
                    Session = s,
                };
            })
            .OrderByDescending(i => i.Date)
            .ToList();

    // ---- Search ----------------------------------------------------------------------------------------------

    private bool FilterItem(object o)
    {
        if (o is not KbItem item) return false;
        var q = SearchBox.Text.Trim();
        if (q.Length == 0) return true;
        if (item.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || item.Subtitle.Contains(q, StringComparison.OrdinalIgnoreCase))
            return true;
        if (item.Session?.Snapshots.Any(s => s.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                                           || s.App.Contains(q, StringComparison.OrdinalIgnoreCase)) == true)
            return true;
        return ReadArticle(item).Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private IEnumerable<KbItem> Visible(ICollectionView? view) => view?.Cast<KbItem>() ?? [];

    private void UpdateListState()
    {
        var notes = Visible(_notesView).Count();
        var recordings = Visible(_recordingsView).Count();
        var searching = SearchBox.Text.Trim().Length > 0;

        NotesCount.Text = searching ? $"{notes} von {_noteItems.Count}" : notes.ToString(De);
        RecordingsCount.Text = searching ? $"{recordings} von {_recordingItems.Count}" : recordings.ToString(De);
        NotesEmpty.Text = searching ? "Keine Treffer." : "Noch keine Sprachnotizen. Bereich anklicken und unten diktieren oder schreiben.";
        RecordingsEmpty.Text = searching ? "Keine Treffer." : "Noch keine Bildschirmaufnahmen. Bereich anklicken und unten starten.";
        NotesEmpty.Visibility = notes == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecordingsEmpty.Visibility = recordings == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Share the height by content, but keep each area usable.
        NotesRow.Height = new GridLength(Math.Clamp(notes, 2, 8), GridUnitType.Star);
        RecordingsRow.Height = new GridLength(Math.Clamp(recordings, 2, 8), GridUnitType.Star);

        var selected = SelectedItems().ToList();
        var selNotes = selected.Count(i => i.ArticleKind == KbArticleKind.Note);
        SelectionLabel.Text = selected.Count == 0
            ? $"{notes + recordings} Artikel"
            : $"{selected.Count} ausgewählt ({selNotes} Sprachn., {selected.Count - selNotes} Bildschirmaufn.)";
        DeleteButton.IsEnabled = selected.Any(CanDelete);
    }

    // ---- Selection and display ------------------------------------------------------------------------------

    /// <summary>Selection across both areas: notes first, then recordings, each in list order.</summary>
    private IEnumerable<KbItem> SelectedItems()
    {
        var notes = NotesList.SelectedItems.OfType<KbItem>().ToHashSet();
        var recordings = RecordingsList.SelectedItems.OfType<KbItem>().ToHashSet();
        return Visible(_notesView).Where(notes.Contains).Concat(Visible(_recordingsView).Where(recordings.Contains));
    }

    private void ClearSelection()
    {
        _suppressSelection = true;
        NotesList.UnselectAll();
        RecordingsList.UnselectAll();
        _suppressSelection = false;
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        _view = sender == OverviewNav ? KbView.Overview : KbView.Dashboard;
        SetArea(KbArea.None);
        if (SelectedItems().Any())
        {
            ClearSelection();
            UpdateListState();
        }
        ShowCurrent(keepScroll: false);
    }

    private void ArticleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection) return;
        UpdateListState();
        if (SelectedItems().Any())
        {
            _view = KbView.Articles;
            DashboardNav.IsChecked = false;
            OverviewNav.IsChecked = false;
            ShowCurrent(keepScroll: false);
        }
        else if (_view == KbView.Articles)
        {
            ShowCurrent(keepScroll: false);
        }
    }

    private void ShowCurrent(bool keepScroll)
    {
        var offset = MarkdownView.VerticalOffset;
        DashboardView.Visibility = _view == KbView.Dashboard ? Visibility.Visible : Visibility.Collapsed;
        MarkdownView.Visibility = _view == KbView.Dashboard ? Visibility.Collapsed : Visibility.Visible;
        ContentHeader.Visibility = _view == KbView.Dashboard ? Visibility.Collapsed : Visibility.Visible;

        switch (_view)
        {
            case KbView.Dashboard:
                MarkdownView.Text = "";
                return;
            case KbView.Overview:
                ContentTitle.Text = "Gesamtübersicht";
                ContentSubtitle.Text = "Zwischenablage, Sprachnotizen und Bildschirmaufnahmen als Markdown";
                MarkdownView.Text = BuildOverview();
                break;
            default:
                var selected = SelectedItems().ToList();
                if (selected.Count == 0)
                {
                    ContentTitle.Text = "Keine Auswahl";
                    ContentSubtitle.Text = "Links eine Sprachnotiz oder Bildschirmaufnahme wählen, mit Strg oder Umschalt mehrere.";
                    MarkdownView.Text = "";
                }
                else if (selected.Count == 1)
                {
                    var item = selected[0];
                    ContentTitle.Text = $"{item.KindName}: {item.Title}";
                    ContentSubtitle.Text = item.Subtitle;
                    MarkdownView.Text = ReadArticle(item);
                }
                else
                {
                    var notes = selected.Count(i => i.ArticleKind == KbArticleKind.Note);
                    ContentTitle.Text = $"{selected.Count} Artikel ausgewählt";
                    ContentSubtitle.Text = $"{notes} Sprachnotizen · {selected.Count - notes} Bildschirmaufnahmen · zusammengefasste Vorschau";
                    MarkdownView.Text = KnowledgeExport.BuildCombined(ToArticles(selected), DateTime.Now);
                }
                break;
        }
        if (keepScroll) MarkdownView.ScrollToVerticalOffset(offset);
        else MarkdownView.ScrollToHome();
    }

    private string BuildOverview()
        => KnowledgeSummaryBuilder.Build(_app.SnapshotEntries(), _sessions, DateTime.Now, _notes);

    private static string ReadArticle(KbItem item)
    {
        try { return RecordingMarkdown.ReadShared(item.FilePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return $"Datei konnte nicht gelesen werden: {ex.Message}"; }
    }

    private static List<KbArticle> ToArticles(IEnumerable<KbItem> items)
        => items.Select(i => new KbArticle(i.ArticleKind, i.Title, i.Date, Path.GetFileName(i.FilePath), ReadArticle(i))).ToList();

    private void SelectNotes_Click(object sender, RoutedEventArgs e)
    {
        SetArea(KbArea.Notes);
        NotesList.SelectAll();
    }

    private void SelectRecordings_Click(object sender, RoutedEventArgs e)
    {
        SetArea(KbArea.Recordings);
        RecordingsList.SelectAll();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        _suppressSelection = true;
        NotesList.SelectAll();
        _suppressSelection = false;
        RecordingsList.SelectAll();
        if (RecordingsList.Items.Count == 0) ArticleList_SelectionChanged(NotesList, null!);
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        ClearSelection();
        ArticleList_SelectionChanged(NotesList, null!);
    }

    // ---- Dashboard ---------------------------------------------------------------------------------------------

    private void UpdateDashboard()
    {
        var data = KnowledgeDashboard.Build(_app.SnapshotEntries(), _sessions, _notes, _app.Recorder.CurrentSessionPath);

        ArticleCounter.Value = data.ArticleCount;
        NoteCounter.Value = data.NoteCount;
        RecordingCounter.Value = data.RecordingCount;
        ClipboardCounter.Value = data.ClipboardCount;

        SnapshotGauge.Reading = data.FocusSnapshots;
        WindowGauge.Reading = data.FocusWindows;
        AppGauge.Reading = data.FocusApps;
        MinuteGauge.Reading = data.FocusMinutes;
        FocusLabel.Text = data.FocusSession switch
        {
            null => "Noch keine Bildschirmaufnahme",
            var s when data.FocusIsRunning => $"Läuft seit {s.Start.ToString("dd.MM.yyyy HH:mm", De)}",
            var s => $"Letzte Aufnahme {s.Start.ToString("dd.MM.yyyy HH:mm", De)} · {RecordingMarkdown.FormatDuration(s.Duration)}",
        };

        AppTiles.ItemsSource = data.Applications;
        AppTilesEmpty.Visibility = data.Applications.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DomainTiles.ItemsSource = data.Domains;
        DomainTilesEmpty.Visibility = data.Domains.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- Actions ----------------------------------------------------------------------------------------------

    private void Record_Click(object sender, RoutedEventArgs e)
    {
        // A recording started here is selected right away, so its entries can be watched while it runs.
        _selectRecordingOnStart = !_app.Recorder.IsRecording;
        _app.ToggleRecording();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var text = _view == KbView.Dashboard ? BuildOverview() : MarkdownView.Text;
        if (string.IsNullOrEmpty(text)) return;
        StatusLabel.Text = _app.CopyTextWithoutHistory(text)
            ? "In die Zwischenablage kopiert."
            : "Zwischenablage ist gerade belegt.";
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedItems().ToList();
        var fromSelection = selected.Count > 0;
        var scope = fromSelection ? selected : Visible(_notesView).Concat(Visible(_recordingsView)).ToList();
        var scopeText = fromSelection
            ? $"{selected.Count} ausgewählte Artikel"
            : scope.Count > 0
                ? $"Keine Auswahl: alle {scope.Count} angezeigten Artikel"
                : "Keine Artikel vorhanden, nur die Gesamtübersicht";

        var dialog = new KbExportDialog(scopeText, includeOverview: !fromSelection, canExportArticles: scope.Count > 0) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm", CultureInfo.InvariantCulture);
        var save = dialog.AsZip
            ? new SaveFileDialog { Filter = "ZIP-Archiv (*.zip)|*.zip", FileName = $"clipwell-kb-{stamp}.zip" }
            : new SaveFileDialog
            {
                Filter = "Markdown (*.md)|*.md",
                FileName = scope.Count == 1 && !dialog.IncludeOverview ? Path.GetFileName(scope[0].FilePath) : $"clipwell-kb-{stamp}.md",
            };
        if (save.ShowDialog(this) != true) return;

        try
        {
            var articles = ToArticles(scope);
            var overview = dialog.IncludeOverview ? BuildOverview() : null;
            if (dialog.AsZip)
            {
                KnowledgeExport.WriteZip(save.FileName, articles, overview);
            }
            else
            {
                var text = articles.Count == 0 ? overview ?? ""
                    : articles.Count == 1 && overview == null ? articles[0].Content
                    : KnowledgeExport.BuildCombined(articles, DateTime.Now, overview);
                File.WriteAllText(save.FileName, text, new System.Text.UTF8Encoding(false));
            }
            StatusLabel.Text = $"{articles.Count} Artikel exportiert: {save.FileName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, $"Export fehlgeschlagen: {ex.Message}", "Clipwell", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_app.Recorder.Directory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.DataDir}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            App.LogCrash(ex);
        }
    }

    private bool CanDelete(KbItem item)
        => item.Note != null || !string.Equals(item.FilePath, _app.Recorder.CurrentSessionPath, StringComparison.OrdinalIgnoreCase);

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var targets = SelectedItems().Where(CanDelete).ToList();
        if (targets.Count == 0) return;

        string question;
        if (targets.Count == 1)
        {
            var item = targets[0];
            question = item.Note != null
                ? $"Sprachnotiz \"{item.Title}\" aus der KB entfernen?\n\nDie Originalnotiz im Notizfenster bleibt erhalten."
                : $"Bildschirmaufnahme vom {item.DateLabel} endgültig löschen?";
        }
        else
        {
            var notes = targets.Count(i => i.Note != null);
            question = $"{targets.Count} Artikel löschen ({notes} Sprachnotizen, {targets.Count - notes} Bildschirmaufnahmen)?\n\n"
                + "Sprachnotizen werden nur aus der KB entfernt, die Originale im Notizfenster bleiben erhalten. "
                + "Bildschirmaufnahmen werden endgültig gelöscht.";
        }
        if (MessageBox.Show(this, question, "Clipwell", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        var failed = new List<string>();
        foreach (var item in targets)
        {
            try { File.Delete(item.FilePath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed.Add($"{item.Title}: {ex.Message}"); }
        }
        ClearSelection();
        Reload();
        if (failed.Count > 0)
            MessageBox.Show(this, "Löschen fehlgeschlagen:\n" + string.Join("\n", failed), "Clipwell", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (e.Key == Key.Escape)
        {
            if (_noteDictation.IsActive) _noteDictation.Cancel();
            else if (SearchBox.IsKeyboardFocusWithin && SearchBox.Text.Length > 0) SearchBox.Text = "";
            else if (NoteInput.IsKeyboardFocusWithin && NoteInput.Text.Length > 0) NotesList.Focus();
            else Close();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.E)
        {
            Export_Click(this, e);
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            Reload();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && (NotesList.IsKeyboardFocusWithin || RecordingsList.IsKeyboardFocusWithin))
        {
            Delete_Click(this, e);
            e.Handled = true;
        }
    }

    // ---- New voice note ----------------------------------------------------------------------------------------

    private void NoteInput_TextChanged(object sender, TextChangedEventArgs e)
        => NoteInputHint.Visibility = NoteInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void NoteInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            _ = SaveNoteAsync();
            e.Handled = true;
        }
    }

    private void NoteDictate_Click(object sender, RoutedEventArgs e)
    {
        NoteInput.Focus();
        _ = _noteDictation.ToggleAsync();
    }

    private void NoteSave_Click(object sender, RoutedEventArgs e) => _ = SaveNoteAsync();

    private async Task SaveNoteAsync()
    {
        if (_noteDictation.IsWorking) return;
        await _noteDictation.FinishAsync();
        var text = NoteInput.Text.Trim();
        if (text.Length == 0)
        {
            NoteStatus.Text = "Erst diktieren oder Text eingeben.";
            return;
        }
        try
        {
            _pendingSelectPath = _app.SaveQuickNote(text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _pendingSelectPath = null;
            App.LogCrash(ex);
            MessageBox.Show(this, $"Sprachnotiz konnte nicht gespeichert werden: {ex.Message}", "Clipwell", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        NoteInput.Text = "";
        _noteDictation.ClearStatus();
        NoteStatus.Text = "Gespeichert und in die KB übernommen.";
    }

    private void UpdateNoteDictationUi()
    {
        var recording = _noteDictation.IsRecording;
        NoteDictateIcon.Text = recording ? "" : "";
        NoteDictateIcon.Foreground = recording ? RecordingRed : (Brush)FindResource("OnboardingTextPrimaryBrush");
        NoteDictateLabel.Text = recording ? "Beenden" : _noteDictation.IsWorking ? "Bitte warten" : "Diktieren";
        NoteDictateButton.IsEnabled = !_noteDictation.IsBlocked && !_noteDictation.IsWorking;
        NoteDictateButton.ToolTip = _noteDictation.IsBlocked
            ? "Ein anderes Fenster diktiert gerade"
            : "Diktieren (lokal per Whisper), erneut klicken zum Beenden";
        NoteSaveButton.IsEnabled = !_noteDictation.IsWorking;
        NoteStatus.Text = _noteDictation.StatusText;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

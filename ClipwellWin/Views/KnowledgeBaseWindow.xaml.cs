using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ClipwellWin.Services;
using MessageBox = System.Windows.MessageBox;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace ClipwellWin.Views;

public partial class KnowledgeBaseWindow : Window
{
    private enum KbItemKind { Dashboard, Overview, Note, Recording }

    private sealed record KbItem(KbItemKind ItemKind, string Title, string Subtitle, RecordingSession? Session = null, KbNote? Note = null)
    {
        public string? FilePath => Session?.Path ?? Note?.Path;
        public string Kind => ItemKind == KbItemKind.Note ? "NOTIZ" : "AUFNAHME";
    }

    private readonly App _app;
    private readonly DispatcherTimer _refreshDebounce;
    private bool _suppressSelection;

    public KnowledgeBaseWindow(App app)
    {
        _app = app;
        InitializeComponent();
        App.ApplyAppIcon(this);

        _refreshDebounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshDebounce.Tick += (_, _) => { _refreshDebounce.Stop(); Reload(keepSelection: true); };

        _app.RecordingStateChanged += App_RecordingStateChanged;
        _app.Recorder.SnapshotWritten += Recorder_SnapshotWritten;
        _app.KnowledgeBaseChanged += App_KnowledgeBaseChanged;
        Closed += (_, _) =>
        {
            _app.RecordingStateChanged -= App_RecordingStateChanged;
            _app.Recorder.SnapshotWritten -= Recorder_SnapshotWritten;
            _app.KnowledgeBaseChanged -= App_KnowledgeBaseChanged;
            _refreshDebounce.Stop();
        };

        UpdateRecordButton();
        Reload(keepSelection: false);
    }

    private void App_RecordingStateChanged(object? sender, EventArgs e)
    {
        UpdateRecordButton();
        Reload(keepSelection: true);
    }

    private void Recorder_SnapshotWritten(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(() => { if (!_refreshDebounce.IsEnabled) _refreshDebounce.Start(); });

    private void UpdateRecordButton()
    {
        var recording = _app.Recorder.IsRecording;
        RecordIcon.Text = recording ? "" : "";
        RecordIcon.Foreground = recording
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE8, 0x3B, 0x3B))
            : (System.Windows.Media.Brush)FindResource("PopupTextPrimaryBrush");
        RecordLabel.Text = recording ? "Aufnahme beenden" : "Aufnahme starten";
    }

    private void App_KnowledgeBaseChanged(object? sender, EventArgs e) => Reload(keepSelection: true);

    private void Reload(bool keepSelection)
    {
        var selected = keepSelection ? SessionList.SelectedItem as KbItem : null;
        var selectedPath = selected?.FilePath;

        List<RecordingSession> sessions;
        List<KbNote> notes;
        try
        {
            sessions = RecordingMarkdown.LoadSessions(_app.Recorder.Directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.LogCrash(ex);
            sessions = [];
        }
        try
        {
            notes = NoteStore.LoadKbNotes(AppPaths.KbNotesDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.LogCrash(ex);
            notes = [];
        }

        var de = CultureInfo.GetCultureInfo("de-DE");
        var items = new List<KbItem>
        {
            new(KbItemKind.Dashboard, "Dashboard", "Instrumente und gespeicherte Artikel"),
            new(KbItemKind.Overview, "Gesamtübersicht", "Zwischenablage, Notizen und Aufnahmen als Markdown"),
        };
        foreach (var n in notes)
            items.Add(new KbItem(KbItemKind.Note, "Notiz: " + n.Title, $"Exportiert {n.Exported.ToString("dd.MM.yyyy HH:mm", de)}", Note: n));

        var current = _app.Recorder.CurrentSessionPath;
        foreach (var s in sessions)
        {
            var running = string.Equals(s.Path, current, StringComparison.OrdinalIgnoreCase);
            var subtitle = $"{s.Snapshots.Count} Einträge · {RecordingMarkdown.FormatDuration(s.Duration)}"
                + (running ? " · läuft" : s.IsOpen ? " · nicht abgeschlossen" : "");
            items.Add(new KbItem(KbItemKind.Recording, "Aufnahme " + s.Start.ToString("dd.MM.yyyy HH:mm:ss", de), subtitle, Session: s));
        }

        _suppressSelection = true;
        SessionList.ItemsSource = items;
        var index = 0;
        if (selectedPath != null)
        {
            var found = items.FindIndex(i => string.Equals(i.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase));
            if (found >= 0) index = found;
        }
        else if (selected?.ItemKind == KbItemKind.Overview)
        {
            index = 1;
        }
        SessionList.SelectedIndex = index;
        _suppressSelection = false;

        UpdateDashboard(items, sessions, notes);
        ShowSelected(items[index], sessions, notes);
        StatusLabel.Text = $"{notes.Count} Notizen · {sessions.Count} Aufnahmen · Ordner: {AppPaths.DataDir}";
    }

    private void ShowSelected(KbItem item, IReadOnlyList<RecordingSession>? sessions = null, IReadOnlyList<KbNote>? notes = null)
    {
        var offset = MarkdownView.VerticalOffset;
        string text;
        if (item.FilePath == null)
        {
            sessions ??= RecordingMarkdown.LoadSessions(_app.Recorder.Directory);
            notes ??= NoteStore.LoadKbNotes(AppPaths.KbNotesDir);
            text = KnowledgeSummaryBuilder.Build(_app.SnapshotEntries(), sessions, DateTime.Now, notes);
        }
        else
        {
            try { text = RecordingMarkdown.ReadShared(item.FilePath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { text = $"Datei konnte nicht gelesen werden: {ex.Message}"; }
        }

        var key = item.FilePath ?? item.ItemKind.ToString();
        var sameItem = Equals(MarkdownView.Tag, key);
        MarkdownView.Tag = key;
        MarkdownView.Text = text;
        if (sameItem) MarkdownView.ScrollToVerticalOffset(offset);

        var dashboard = item.ItemKind == KbItemKind.Dashboard;
        DashboardView.Visibility = dashboard ? Visibility.Visible : Visibility.Collapsed;
        MarkdownView.Visibility = dashboard ? Visibility.Collapsed : Visibility.Visible;
        DeleteButton.IsEnabled = item.Note != null || (item.Session != null
            && !string.Equals(item.Session.Path, _app.Recorder.CurrentSessionPath, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateDashboard(List<KbItem> items, IReadOnlyList<RecordingSession> sessions, IReadOnlyList<KbNote> notes)
    {
        var data = KnowledgeDashboard.Build(_app.SnapshotEntries(), sessions, notes, _app.Recorder.CurrentSessionPath);
        var de = CultureInfo.GetCultureInfo("de-DE");

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
            null => "Noch keine Aufnahme",
            var s when data.FocusIsRunning => $"Läuft seit {s.Start.ToString("dd.MM.yyyy HH:mm", de)}",
            var s => $"Letzte Aufnahme {s.Start.ToString("dd.MM.yyyy HH:mm", de)} · {RecordingMarkdown.FormatDuration(s.Duration)}",
        };

        var recording = _app.Recorder.IsRecording;
        StateDot.Fill = recording
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE8, 0x3B, 0x3B))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6B, 0x6B, 0x6B));
        StateLabel.Text = recording ? "AUFNAHME LÄUFT" : "INAKTIV";

        AppTiles.ItemsSource = data.Applications;
        AppTilesEmpty.Visibility = data.Applications.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DomainTiles.ItemsSource = data.Domains;
        DomainTilesEmpty.Visibility = data.Domains.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var articles = items.Where(i => i.ItemKind is KbItemKind.Note or KbItemKind.Recording).ToList();
        ArticleList.ItemsSource = articles;
        ArticleListEmpty.Visibility = articles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Article_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: KbItem item })
        {
            SessionList.SelectedItem = item;
            SessionList.ScrollIntoView(item);
        }
    }

    private void SessionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection) return;
        if (SessionList.SelectedItem is KbItem item) ShowSelected(item);
    }

    private void Record_Click(object sender, RoutedEventArgs e) => _app.ToggleRecording();

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload(keepSelection: true);

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(MarkdownView.Text)) return;
        StatusLabel.Text = _app.CopyTextWithoutHistory(MarkdownView.Text)
            ? "In die Zwischenablage kopiert."
            : "Zwischenablage ist gerade belegt.";
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var item = SessionList.SelectedItem as KbItem;
        var dialog = new SaveFileDialog
        {
            Filter = "Markdown (*.md)|*.md",
            FileName = item?.FilePath == null
                ? $"clipwell-uebersicht-{DateTime.Now:yyyy-MM-dd}.md"
                : Path.GetFileName(item.FilePath),
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, MarkdownView.Text, new System.Text.UTF8Encoding(false));
            StatusLabel.Text = $"Exportiert: {dialog.FileName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (SessionList.SelectedItem is not KbItem { FilePath: { } path } item) return;
        string question;
        if (item.Note != null)
        {
            question = $"Notiz \"{item.Note.Title}\" aus der KB entfernen?\n\nDie Originalnotiz im Notizfenster bleibt erhalten.";
        }
        else
        {
            if (string.Equals(path, _app.Recorder.CurrentSessionPath, StringComparison.OrdinalIgnoreCase)) return;
            question = $"Aufnahme vom {item.Session!.Start:dd.MM.yyyy HH:mm} endgültig löschen?";
        }
        var answer = MessageBox.Show(this, question, "Clipwell", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Löschen fehlgeschlagen: {ex.Message}", "Clipwell", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Reload(keepSelection: false);
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        if (e.LeftButton == MouseButtonState.Pressed)
            try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

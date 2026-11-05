using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ClipwellWin.Services;
using MessageBox = System.Windows.MessageBox;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace ClipwellWin.Views;

public enum NotesStartAction { None, NewNote, NewDictatedNote }

public partial class NotesWindow : Window
{
    private sealed record NoteItem(Note Note, string Title, string Subtitle);

    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private static readonly SolidColorBrush RecordingBrush = new(Color.FromRgb(0xE8, 0x3B, 0x3B));

    private readonly App _app;
    private readonly DispatcherTimer _saveDebounce;
    private readonly DispatcherTimer _levelTimer;
    private List<Note> _notes = [];
    private Note? _current;
    private bool _dirty;
    private bool _suppressEvents;

    public NotesWindow(App app)
    {
        _app = app;
        InitializeComponent();
        App.ApplyAppIcon(this);

        _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _saveDebounce.Tick += (_, _) => { _saveDebounce.Stop(); SaveIfDirty(); };
        _levelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _levelTimer.Tick += (_, _) => UpdateLevel();

        _app.Dictation.StateChanged += Dictation_StateChanged;
        Closing += (_, _) => { SaveIfDirty(); DropIfEmpty(); };
        Closed += (_, _) =>
        {
            _app.Dictation.StateChanged -= Dictation_StateChanged;
            if (_app.Dictation.Owner == this) _app.Dictation.Cancel();
            _saveDebounce.Stop();
            _levelTimer.Stop();
        };

        LoadNotes(selectId: null);
        UpdateDictationUi();
    }

    /// <summary>Applies a start action from the tray or popup after the window is shown.</summary>
    internal void Run(NotesStartAction action, string? initialText)
    {
        switch (action)
        {
            case NotesStartAction.NewNote:
                StartNewNote(initialText);
                break;
            case NotesStartAction.NewDictatedNote:
                StartNewNote(initialText);
                if (_app.Dictation.State == DictationState.Idle) _ = ToggleDictationAsync();
                break;
        }
        Editor.Focus();
    }

    private void LoadNotes(string? selectId)
    {
        try
        {
            _notes = NoteStore.Load(AppPaths.NotesDir, AppPaths.KbNotesDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.LogCrash(ex);
            _notes = [];
        }

        var target = selectId != null ? _notes.FirstOrDefault(n => n.Id == selectId) : _notes.FirstOrDefault();
        RefreshList(target);
        ShowNote(target);
    }

    private void RefreshList(Note? select)
    {
        var query = SearchBox.Text.Trim();
        var items = _notes
            .Where(n => query.Length == 0 || n.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .Select(n => new NoteItem(n, n.Title, Subtitle(n)))
            .ToList();

        _suppressEvents = true;
        NoteList.ItemsSource = items;
        NoteList.SelectedItem = select == null ? null : items.FirstOrDefault(i => i.Note == select);
        _suppressEvents = false;
        StatusLabel.Text = $"{_notes.Count} Notizen · Ordner: {AppPaths.NotesDir}";
    }

    private static string Subtitle(Note n)
    {
        var text = n.Modified.ToString("dd.MM.yyyy HH:mm", De);
        if (n.IsInKb) text += n.KbOutdated ? " · KB veraltet" : " · in KB";
        return text;
    }

    private void ShowNote(Note? note)
    {
        _current = note;
        _dirty = false;
        _suppressEvents = true;
        Editor.Text = note?.Text ?? "";
        Editor.CaretIndex = Editor.Text.Length;
        _suppressEvents = false;
        UpdateEditorState();
    }

    private void StartNewNote(string? initialText = null)
    {
        SaveIfDirty();
        DropIfEmpty();
        _suppressEvents = true;
        NoteList.SelectedItem = null;
        _suppressEvents = false;
        ShowNote(null);
        if (!string.IsNullOrEmpty(initialText))
        {
            Editor.Text = initialText;
            Editor.CaretIndex = initialText.Length;
            SaveIfDirty();
        }
        Editor.Focus();
    }

    private void UpdateEditorState()
    {
        EditorPlaceholder.Visibility = Editor.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        DeleteButton.IsEnabled = _current != null;
        KbButton.IsEnabled = _current != null || Editor.Text.Trim().Length > 0;
        KbLabel.Text = _current switch
        {
            { IsInKb: true } when _dirty || _current.KbOutdated => "KB aktualisieren",
            { IsInKb: true } => "In KB",
            _ => "In KB exportieren",
        };
    }

    private void SaveIfDirty()
    {
        if (!_dirty) return;
        _dirty = false;
        var text = Editor.Text;
        try
        {
            if (_current == null)
            {
                if (text.Trim().Length == 0) return;
                _current = NoteStore.Create(AppPaths.NotesDir, text, DateTime.Now);
                _notes.Insert(0, _current);
            }
            else
            {
                NoteStore.Save(_current, text);
                _notes.Remove(_current);
                _notes.Insert(0, _current);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.LogCrash(ex);
            StatusLabel.Text = $"Speichern fehlgeschlagen: {ex.Message}";
            _dirty = true;
            return;
        }
        RefreshList(_current);
        UpdateEditorState();
    }

    /// <summary>Removes a note that was emptied, so clearing the editor does not leave blank files behind.</summary>
    private void DropIfEmpty()
    {
        if (_current == null || _current.Text.Trim().Length > 0) return;
        try { NoteStore.Delete(_current); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.LogCrash(ex); return; }
        _notes.Remove(_current);
        _current = null;
        RefreshList(null);
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents) return;
        _dirty = true;
        _saveDebounce.Stop();
        _saveDebounce.Start();
        UpdateEditorState();
    }

    private void NoteList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || NoteList.SelectedItem is not NoteItem item) return;
        if (item.Note == _current) return;
        SaveIfDirty();
        DropIfEmpty();
        ShowNote(item.Note);
        RefreshList(item.Note);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        SaveIfDirty();
        RefreshList(_current);
    }

    private void New_Click(object sender, RoutedEventArgs e) => StartNewNote();

    private void Dictate_Click(object sender, RoutedEventArgs e) => _ = ToggleDictationAsync();

    private async Task ToggleDictationAsync()
    {
        var text = await _app.ToggleDictationAsync(this, this);
        if (string.IsNullOrWhiteSpace(text))
        {
            if (_app.Dictation.State == DictationState.Idle && text != null)
                StatusLabel.Text = "Keine Sprache erkannt.";
            return;
        }
        InsertAtCaret(text);
    }

    private void InsertAtCaret(string text)
    {
        var caret = Math.Clamp(Editor.CaretIndex, 0, Editor.Text.Length);
        var before = caret > 0 ? Editor.Text[caret - 1] : '\n';
        var after = caret < Editor.Text.Length ? Editor.Text[caret] : '\n';
        var insert = (char.IsWhiteSpace(before) ? "" : " ") + text + (char.IsWhiteSpace(after) ? "" : " ");
        Editor.Text = Editor.Text.Insert(caret, insert);
        Editor.CaretIndex = caret + insert.Length;
        Editor.Focus();
        SaveIfDirty();
        StatusLabel.Text = $"Diktat eingefügt ({text.Length} Zeichen).";
    }

    private void Dictation_StateChanged(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(UpdateDictationUi);

    private void UpdateDictationUi()
    {
        var d = _app.Dictation;
        var mine = d.Owner == this;
        var busyElsewhere = d.IsBusyFor(this);
        var recording = mine && d.State == DictationState.Recording;

        DictateButton.IsEnabled = !busyElsewhere && !(mine && d.State is DictationState.Downloading or DictationState.Transcribing);
        DictateIcon.Text = recording ? "" : "";
        DictateIcon.Foreground = recording ? RecordingBrush : (Brush)FindResource("PopupTextPrimaryBrush");
        DictateLabel.Text = recording ? "Diktat beenden" : "Diktieren";
        DictateButton.Background = recording
            ? new SolidColorBrush(Color.FromArgb(0x40, 0xE8, 0x3B, 0x3B))
            : (Brush)FindResource("PopupBackgroundBrush");

        DictationPanel.Visibility = mine && d.State != DictationState.Idle ? Visibility.Visible : Visibility.Collapsed;
        LevelBar.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
        DictationLabel.Text = d.StatusText;
        if (recording) _levelTimer.Start(); else _levelTimer.Stop();
    }

    private void UpdateLevel()
    {
        var mic = _app.Dictation.Microphone;
        var peak = Math.Min(1f, mic.TakePeak() * 1.5f);
        LevelBar.Width = 80 * peak;
        var elapsed = mic.Elapsed;
        DictationLabel.Text = $"Aufnahme {(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
    }

    private void ExportKb_Click(object sender, RoutedEventArgs e)
    {
        SaveIfDirty();
        if (_current == null) return;
        try
        {
            NoteStore.ExportToKb(_current, AppPaths.KbNotesDir, DateTime.Now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Export in die KB fehlgeschlagen: {ex.Message}", "Clipwell",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        RefreshList(_current);
        UpdateEditorState();
        _app.NotifyKnowledgeBaseChanged();
        StatusLabel.Text = "In die KB exportiert.";
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (Editor.Text.Length == 0) return;
        StatusLabel.Text = _app.CopyTextWithoutHistory(Editor.Text)
            ? "In die Zwischenablage kopiert."
            : "Zwischenablage ist gerade belegt.";
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.NotesDir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.NotesDir}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            App.LogCrash(ex);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        var hint = _current.IsInKb ? "\n\nDie in die KB exportierte Fassung bleibt erhalten." : "";
        var answer = MessageBox.Show(this, $"Notiz \"{_current.Title}\" endgültig löschen?{hint}",
            "Clipwell", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            NoteStore.Delete(_current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Löschen fehlgeschlagen: {ex.Message}", "Clipwell", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _notes.Remove(_current);
        _dirty = false;
        var next = _notes.FirstOrDefault();
        RefreshList(next);
        ShowNote(next);
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        switch (e.Key)
        {
            case Key.N: StartNewNote(); e.Handled = true; break;
            case Key.D: _ = ToggleDictationAsync(); e.Handled = true; break;
            case Key.E: ExportKb_Click(sender, e); e.Handled = true; break;
            case Key.S: SaveIfDirty(); e.Handled = true; break;
        }
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

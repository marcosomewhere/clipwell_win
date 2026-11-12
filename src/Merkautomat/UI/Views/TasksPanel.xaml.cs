using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using Merkautomat.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = Merkautomat.Views.AppMessageBox;
using UserControl = System.Windows.Controls.UserControl;

namespace Merkautomat.Views;

/// <summary>
/// Ideenkombinat section "Aufgaben": tasks from clipboard entries (popup context menu), Outlook mails and manually
/// added ones. The window owns navigation, the header buttons and the status line; it reads
/// <see cref="OpenCount"/>, <see cref="DueTodayCount"/>, <see cref="HasSelection"/> and <see cref="Subtitle"/>
/// whenever <see cref="StateChanged"/> fires.
/// </summary>
public partial class TasksPanel : UserControl
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public sealed class TaskRow(KbTask task) : INotifyPropertyChanged
    {
        public KbTask Task { get; } = task;
        public string Title => Task.Title;
        public bool Done => Task.Done;
        public bool Overdue => TaskStore.IsOverdue(Task, DateTime.Today);
        public string Meta => TaskMeta(Task);
        public event PropertyChangedEventHandler? PropertyChanged;

        public void Refresh()
        {
            foreach (var name in new[] { nameof(Title), nameof(Done), nameof(Overdue), nameof(Meta) })
                PropertyChanged?.Invoke(this, new(name));
        }
    }

    private App? _app;
    private string _tasksFile = AppPaths.TasksFile;
    private List<KbTask> _tasks = [];
    private KbTask? _editingTask;
    private bool _loadingTaskEditor;
    private bool _taskDirty;
    private string? _pendingTaskSelect;
    private DispatcherTimer? _taskSaveDebounce;

    public TasksPanel()
    {
        InitializeComponent();
    }

    /// <summary>Counts, selection or subtitle changed; the window refreshes navigation and header from the properties.</summary>
    public event EventHandler? StateChanged;

    /// <summary>A message for the window's status line.</summary>
    public event Action<string>? StatusMessage;

    public int OpenCount => _tasks.Count(t => !t.Done);
    public int DueTodayCount => TaskStore.OpenDueOn(_tasks, DateTime.Today).Count;
    public bool HasSelection => TasksList.SelectedItem != null;

    /// <summary>Delete in the window removes the task only while the list, not an editor field, has the focus.</summary>
    public bool ListHasKeyboardFocus => TasksList.IsKeyboardFocusWithin;
    public IReadOnlyList<KbTask> Tasks => _tasks;

    public string Subtitle
    {
        get
        {
            var open = OpenCount;
            var overdue = _tasks.Count(t => TaskStore.IsOverdue(t, DateTime.Today));
            return $"{open} offen" + (overdue > 0 ? $" · {overdue} überfällig" : "")
                + $" · {_tasks.Count - open} erledigt · Rechtsklick im Popup: \"Zu Aufgaben hinzufügen\"";
        }
    }

    /// <param name="app">Null in tests: then no other window announces changes to the task file.</param>
    public void Init(App? app, string? tasksFile = null)
    {
        _app = app;
        _tasksFile = tasksFile ?? AppPaths.TasksFile;
        _taskSaveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _taskSaveDebounce.Tick += (_, _) => FlushTaskEdits();
        if (_app != null) _app.TasksChanged += App_TasksChanged;
        LoadTasks();
        RaiseStateChanged();
    }

    public void Close()
    {
        if (_app != null) _app.TasksChanged -= App_TasksChanged;
        FlushTaskEdits();
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private void ShowStatus(string text) => StatusMessage?.Invoke(text);

    private Window? OwnerWindow => Window.GetWindow(this);

    /// <summary>Another window wrote tasks.json: reload it, keeping edits that were not saved yet.</summary>
    private void App_TasksChanged(object? sender, EventArgs e)
    {
        _taskSaveDebounce?.Stop();
        var edited = _taskDirty ? _editingTask : null;
        LoadTasks();
        if (edited != null)
        {
            var index = _tasks.FindIndex(t => t.Id == edited.Id);
            if (index >= 0) _tasks[index] = edited;
            _taskDirty = true;
            FlushTaskEdits();
        }
        if (IsVisible) RefreshTaskList((TasksList.SelectedItem as TaskRow)?.Task.Id);
        else RaiseStateChanged();
    }

    /// <summary>Before the window switches to this section: a new task must not be hidden by a filter.</summary>
    public void PrepareOpen(string? selectId)
    {
        if (selectId != null)
        {
            // A new task has no due date yet, so a calendar filter would hide it.
            _dueFrom = null;
            _dueTo = null;
            if (TaskFilterDone.IsChecked == true) TaskFilterOpen.IsChecked = true;
        }
        _pendingTaskSelect = selectId;
    }

    /// <summary>After the switch: puts the cursor into the title of the new task. False when it is not selected.</summary>
    public bool FocusNewTask(string selectId)
    {
        if (TasksList.SelectedItem is not TaskRow row || row.Task.Id != selectId) return false;
        Dispatcher.BeginInvoke(() =>
        {
            TaskTitleBox.Focus();
            TaskTitleBox.SelectAll();
        }, DispatcherPriority.Input);
        return true;
    }

    public void Enter()
    {
        var select = _pendingTaskSelect ?? (TasksList.SelectedItem as TaskRow)?.Task.Id;
        _pendingTaskSelect = null;
        RefreshTaskList(select);
        if (_tasks.Count == 0) Dispatcher.BeginInvoke(() => NewTaskInput.Focus(), DispatcherPriority.Input);
    }

    private void LoadTasks()
    {
        try { _tasks = TaskStore.Load(_tasksFile); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.LogCrash(ex);
            _tasks = [];
            ShowStatus($"Aufgaben konnten nicht geladen werden: {ex.Message}");
        }
        _editingTask = _editingTask == null ? null : _tasks.FirstOrDefault(t => t.Id == _editingTask.Id);
        _taskDirty = false;
    }

    private bool SaveTasks()
    {
        try
        {
            TaskStore.Save(_tasksFile, _tasks);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.LogCrash(ex);
            MessageBox.Show(OwnerWindow, $"Aufgaben konnten nicht gespeichert werden: {ex.Message}", AppNames.App, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void FlushTaskEdits()
    {
        _taskSaveDebounce?.Stop();
        if (!_taskDirty) return;
        _taskDirty = false;
        SaveTasks();
    }

    // ---- List ------------------------------------------------------------------------------------------------

    private IEnumerable<KbTask> FilteredTasks()
        => (TaskFilterDone.IsChecked == true ? _tasks.Where(t => t.Done)
            : TaskFilterAll.IsChecked == true ? _tasks
            : _tasks.Where(t => !t.Done)).Where(MatchesDueFilter);

    private void RefreshTaskList(string? selectId)
    {
        var rows = TaskStore.Sorted(FilteredTasks()).Select(t => new TaskRow(t)).ToList();
        TasksList.ItemsSource = rows;
        TasksEmpty.Text = _tasks.Count == 0
            ? "Noch keine Aufgaben. Oben eintippen und Enter drücken, oder im Popup per Rechtsklick auf einen Eintrag \"Zu Aufgaben hinzufügen\"."
            : DueFilterActive ? (DueFilterIsDay ? "An diesem Tag ist nichts fällig." : "In dieser Woche ist nichts fällig.")
            : TaskFilterDone.IsChecked == true ? "Noch nichts erledigt."
            : TaskFilterAll.IsChecked == true ? "" : "Alles erledigt.";
        TasksEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var select = rows.FirstOrDefault(r => r.Task.Id == selectId) ?? rows.FirstOrDefault();
        TasksList.SelectedItem = select;
        if (select != null) TasksList.ScrollIntoView(select);
        else LoadTaskEditor(null);
        BuildTaskCalendar();
        UpdateDueFilterBar();
        RaiseStateChanged();
    }

    /// <summary>"Heute erledigt" in the window's header: all open tasks due today, after asking.</summary>
    public void CompleteDueToday()
    {
        FlushTaskEdits();
        var due = TaskStore.OpenDueOn(_tasks, DateTime.Today);
        if (due.Count == 0) return;
        var list = string.Join("\n", due.Take(10).Select(t => "- " + t.Title)) + (due.Count > 10 ? $"\n- … und {due.Count - 10} weitere" : "");
        var overdue = _tasks.Count(t => TaskStore.IsOverdue(t, DateTime.Today));
        if (MessageBox.Show(OwnerWindow,
                (due.Count == 1 ? "Die heute fällige Aufgabe als erledigt markieren?" : $"Alle {due.Count} heute fälligen Aufgaben als erledigt markieren?")
                + "\n\n" + list + (overdue > 0 ? $"\n\n{overdue} überfällige Aufgaben von früheren Tagen bleiben offen." : ""),
                AppNames.App, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes) != MessageBoxResult.Yes) return;

        var done = TaskStore.CompleteDueOn(_tasks, DateTime.Today, DateTime.Now);
        if (!SaveTasks())
        {
            foreach (var task in done) TaskStore.SetDone(task, false, DateTime.Now);
            return;
        }
        RefreshTaskList((TasksList.SelectedItem as TaskRow)?.Task.Id);
        ShowStatus(done.Count == 1 ? "1 Aufgabe erledigt." : $"{done.Count} Aufgaben erledigt.");
    }

    private static string TaskMeta(KbTask task)
    {
        var parts = new List<string>();
        if (task.Done && task.Completed is { } completed) parts.Add("erledigt " + completed.ToString("dd.MM.yyyy", De));
        else if (task.Due is { } due)
            parts.Add((TaskStore.IsOverdue(task, DateTime.Today) ? "überfällig seit " : due.Date == DateTime.Today ? "fällig heute, " : "fällig ")
                      + due.ToString("dd.MM.yyyy", De));
        if (task.SourceKey != null) parts.Add("aus " + TaskSourceName(task));
        parts.Add("angelegt " + task.Created.ToString("dd.MM.yyyy HH:mm", De));
        return string.Join(" · ", parts);
    }

    private static string TaskSourceName(KbTask task)
        => task.SourceKey?.StartsWith("clip:", StringComparison.Ordinal) == true ? "Zwischenablage"
            : task.SourceKey?.StartsWith("mail:", StringComparison.Ordinal) == true ? "Outlook"
            : "Eintrag";

    private void TaskFilter_Checked(object sender, RoutedEventArgs e)
    {
        if (_taskSaveDebounce == null) return;
        FlushTaskEdits();
        RefreshTaskList((TasksList.SelectedItem as TaskRow)?.Task.Id);
    }

    private void TasksList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        FlushTaskEdits();
        LoadTaskEditor((TasksList.SelectedItem as TaskRow)?.Task);
        RaiseStateChanged();
    }

    // ---- New task --------------------------------------------------------------------------------------------

    private void NewTaskInput_TextChanged(object sender, TextChangedEventArgs e)
        => NewTaskHint.Visibility = NewTaskInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void NewTaskInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        AddNewTask(NewTaskInput.Text);
        e.Handled = true;
    }

    /// <summary>Adds a task as if typed into the input; a day filter becomes its due date. Returns it, or null.</summary>
    public KbTask? AddNewTask(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            NewTaskInput.Focus();
            return null;
        }
        FlushTaskEdits();
        var task = TaskStore.Create(text, DateTime.Now);
        if (DueFilterIsDay) task.Due = _dueFrom;
        _tasks.Add(task);
        if (!SaveTasks())
        {
            _tasks.Remove(task);
            return null;
        }
        NewTaskInput.Text = "";
        if (TaskFilterDone.IsChecked == true) TaskFilterOpen.IsChecked = true;
        RefreshTaskList(task.Id);
        NewTaskInput.Focus();
        return task;
    }

    // ---- Editor ----------------------------------------------------------------------------------------------

    private void LoadTaskEditor(KbTask? task)
    {
        _editingTask = task;
        _loadingTaskEditor = true;
        try
        {
            TaskEditor.IsEnabled = task != null;
            TaskTitleBox.Text = task?.Title ?? "";
            TaskDueBox.Text = task?.Due?.ToString("dd.MM.yyyy", De) ?? "";
            TaskDueError.Visibility = Visibility.Collapsed;
            TaskDoneBox.IsChecked = task?.Done == true;
            TaskNotesBox.Text = task?.Notes ?? "";
            var hasSource = task?.SourceKey != null;
            TaskSourcePanel.Visibility = hasSource ? Visibility.Visible : Visibility.Collapsed;
            TaskSplitter.Visibility = TaskSplitterLine.Visibility = TaskSourcePanel.Visibility;
            // Without a source the notes take the whole height; a split the user dragged stays for the next source.
            if (!hasSource) TaskSourceRow.Height = new GridLength(0);
            else if (TaskSourceRow.Height.Value == 0)
            {
                TaskNotesRow.Height = new GridLength(1, GridUnitType.Star);
                TaskSourceRow.Height = new GridLength(3, GridUnitType.Star);
            }
            TaskSourceLabel.Text = hasSource ? $"Quelle: {TaskSourceName(task!)} · {task!.SourceTitle}" : "";
            TaskSourceText.Document = SourceDocument(task);
            TaskCreatedLabel.Text = task == null ? ""
                : "Angelegt " + task.Created.ToString("dd.MM.yyyy HH:mm", De)
                  + (task.Completed is { } done ? " · erledigt " + done.ToString("dd.MM.yyyy HH:mm", De) : "");
        }
        finally { _loadingTaskEditor = false; }
    }

    /// <summary>
    /// Source text with clickable, shortened web links. Mail sources are cleaned on display as well, so tasks
    /// created before the cleanup look the same.
    /// </summary>
    private FlowDocument SourceDocument(KbTask? task)
    {
        var doc = new FlowDocument { PagePadding = new Thickness(4, 2, 4, 2), FontFamily = new System.Windows.Media.FontFamily("Segoe UI"), FontSize = 12.5 };
        var text = task?.SourceText ?? "";
        var isMail = task?.SourceKey?.StartsWith("mail:", StringComparison.Ordinal) == true;
        if (isMail) text = MailInbox.CleanBody(text);
        var accent = (System.Windows.Media.Brush)FindResource("OnboardingAccentBrush");
        var muted = (System.Windows.Media.Brush)FindResource("EditorMutedBrush");
        var blocks = text.Split("\n\n");
        for (var b = 0; b < blocks.Length; b++)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
            var lines = blocks[b].Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0) paragraph.Inlines.Add(new LineBreak());
                var line = lines[i];
                var header = isMail && b == 0 ? MailHeaderLabel(line) : null;
                if (header != null)
                {
                    paragraph.Inlines.Add(new Run(header) { Foreground = muted });
                    line = line[header.Length..];
                }
                foreach (var segment in LinkText.Split(line))
                {
                    if (segment.Link is not { } uri)
                    {
                        paragraph.Inlines.Add(new Run(segment.Text));
                        continue;
                    }
                    var link = new Hyperlink(new Run(segment.Text)) { NavigateUri = uri, Foreground = accent, ToolTip = uri.AbsoluteUri };
                    link.RequestNavigate += SourceLink_RequestNavigate;
                    paragraph.Inlines.Add(link);
                }
            }
            doc.Blocks.Add(paragraph);
        }
        return doc;
    }

    private static string? MailHeaderLabel(string line)
        => new[] { "Von: ", "Empfangen: ", "Betreff: " }.FirstOrDefault(l => line.StartsWith(l, StringComparison.Ordinal));

    private void SourceLink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        e.Handled = true;
        WebLink.TryOpen(e.Uri);
    }

    private TaskRow? EditingRow()
        => _editingTask == null ? null : TasksList.Items.OfType<TaskRow>().FirstOrDefault(r => r.Task == _editingTask);

    private void TaskField_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loadingTaskEditor || _editingTask is not { } task) return;
        if (sender == TaskTitleBox)
        {
            var title = TaskTitleBox.Text.Trim();
            if (title.Length == 0) return;
            task.Title = title;
        }
        else if (sender == TaskNotesBox) task.Notes = TaskNotesBox.Text;
        else if (sender == TaskDueBox)
        {
            var valid = TaskStore.TryParseDue(TaskDueBox.Text, DateTime.Today, out var due);
            TaskDueError.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
            if (!valid) return;
            task.Due = due;
            BuildTaskCalendar();
        }
        EditingRow()?.Refresh();
        RaiseStateChanged();
        _taskDirty = true;
        _taskSaveDebounce!.Stop();
        _taskSaveDebounce.Start();
    }

    private void TaskDone_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TaskRow row) return;
        SetTaskDone(row.Task, !row.Task.Done);
    }

    private void TaskDoneBox_Click(object sender, RoutedEventArgs e)
    {
        if (_editingTask != null) SetTaskDone(_editingTask, TaskDoneBox.IsChecked == true);
    }

    private void SetTaskDone(KbTask task, bool done)
    {
        FlushTaskEdits();
        var neighbour = NeighbourOf(task);
        TaskStore.SetDone(task, done, DateTime.Now);
        SaveTasks();
        // A task that leaves the current filter hands the selection to its neighbour.
        var keep = TasksList.SelectedItem is TaskRow selected && selected.Task != task ? selected.Task.Id : task.Id;
        if (keep == task.Id && FilteredTasks().All(t => t != task)) keep = neighbour;
        RefreshTaskList(keep);
        ShowStatus(done ? $"Erledigt: {task.Title}" : $"Wieder offen: {task.Title}");
    }

    /// <summary>Deletes the selected task after asking; the entry it came from stays.</summary>
    /// <param name="confirm">False only in tests.</param>
    public void DeleteSelected(bool confirm = true)
    {
        if (TasksList.SelectedItem is not TaskRow row) return;
        if (confirm && MessageBox.Show(OwnerWindow, $"Aufgabe \"{row.Task.Title}\" löschen?\n\nDer Eintrag, aus dem sie entstanden ist, bleibt erhalten.",
                AppNames.App, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        // Unsaved edits of other tasks are kept; those of the deleted task do not matter.
        FlushTaskEdits();
        var next = NeighbourOf(row.Task);
        var index = _tasks.IndexOf(row.Task);
        _tasks.Remove(row.Task);
        if (!SaveTasks())
        {
            _tasks.Insert(Math.Max(0, index), row.Task);
            return;
        }
        RefreshTaskList(next);
        TasksList.Focus();
    }

    /// <summary>Escape in the window: closes the due date picker first. False when it was not open.</summary>
    public bool CloseDuePicker()
    {
        if (!TaskDuePopup.IsOpen) return false;
        TaskDuePopup.IsOpen = false;
        return true;
    }

    /// <summary>Escape in the window: empties the new-task input while it has the focus. False when there was nothing to do.</summary>
    public bool ClearNewTaskInput()
    {
        if (!NewTaskInput.IsKeyboardFocusWithin || NewTaskInput.Text.Length == 0) return false;
        NewTaskInput.Text = "";
        return true;
    }

    public void FocusNewTaskInput() => NewTaskInput.Focus();

    /// <summary>Selected task in the list, or null.</summary>
    public KbTask? SelectedTask => (TasksList.SelectedItem as TaskRow)?.Task;

    /// <summary>Tasks in list order under the current filters.</summary>
    public IReadOnlyList<KbTask> VisibleTasks => TasksList.Items.OfType<TaskRow>().Select(r => r.Task).ToList();

    /// <summary>Row that takes the place of <paramref name="task"/> in the list: the next one, or the previous at the end.</summary>
    private string? NeighbourOf(KbTask task)
    {
        var rows = TasksList.Items.OfType<TaskRow>().ToList();
        var position = rows.FindIndex(r => r.Task == task);
        var rest = rows.Where(r => r.Task != task).ToList();
        if (rest.Count == 0) return null;
        return rest[Math.Clamp(position < 0 ? 0 : position, 0, rest.Count - 1)].Task.Id;
    }

    /// <summary>The visible tasks as a Markdown checklist, for "Kopieren" in the window's header.</summary>
    public string CopyText()
    {
        var sb = new StringBuilder("# Aufgaben\n\n");
        foreach (var task in TaskStore.Sorted(FilteredTasks()))
        {
            sb.Append(task.Done ? "- [x] " : "- [ ] ").Append(task.Title);
            if (task.Due is { } due && !task.Done) sb.Append(" (fällig ").Append(due.ToString("dd.MM.yyyy", De)).Append(')');
            sb.Append('\n');
            foreach (var line in task.Notes.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0))
                sb.Append("  ").Append(line).Append('\n');
        }
        return sb.ToString();
    }
}

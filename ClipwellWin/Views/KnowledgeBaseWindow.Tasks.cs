using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using ClipwellWin.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = ClipwellWin.Views.AppMessageBox;

namespace ClipwellWin.Views;

/// <summary>KB section "Aufgaben": tasks from clipboard entries (popup context menu) and manually added ones.</summary>
public partial class KnowledgeBaseWindow
{
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

    private List<KbTask> _tasks = [];
    private KbTask? _editingTask;
    private bool _loadingTaskEditor;
    private bool _taskDirty;
    private string? _pendingTaskSelect;
    private DispatcherTimer? _taskSaveDebounce;

    private void InitTasks()
    {
        _taskSaveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _taskSaveDebounce.Tick += (_, _) => FlushTaskEdits();
        _app.TasksChanged += App_TasksChanged;
        LoadTasks();
        UpdateTasksNavCount();
    }

    private void CloseTasks()
    {
        _app.TasksChanged -= App_TasksChanged;
        FlushTaskEdits();
    }

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
        if (_view == KbView.Tasks) RefreshTaskList((TasksList.SelectedItem as TaskRow)?.Task.Id);
        else UpdateTasksNavCount();
    }

    internal void OpenTasks(string? selectId)
    {
        if (selectId != null)
        {
            // A new task has no due date yet, so a calendar filter would hide it.
            _dueFrom = null;
            _dueTo = null;
            if (TaskFilterDone.IsChecked == true) TaskFilterOpen.IsChecked = true;
        }
        _pendingTaskSelect = selectId;
        if (TasksNav.IsChecked == true) EnterTasks();
        else TasksNav.IsChecked = true;
        if (selectId != null && TasksList.SelectedItem is TaskRow row && row.Task.Id == selectId)
        {
            Dispatcher.BeginInvoke(() =>
            {
                TaskTitleBox.Focus();
                TaskTitleBox.SelectAll();
            }, DispatcherPriority.Input);
            StatusLabel.Text = "Aufgabe angelegt. Titel, Fälligkeit und Notizen lassen sich rechts anpassen.";
        }
    }

    private void EnterTasks()
    {
        var select = _pendingTaskSelect ?? (TasksList.SelectedItem as TaskRow)?.Task.Id;
        _pendingTaskSelect = null;
        RefreshTaskList(select);
        if (_tasks.Count == 0) Dispatcher.BeginInvoke(() => NewTaskInput.Focus(), DispatcherPriority.Input);
    }

    private void LoadTasks()
    {
        try { _tasks = TaskStore.Load(AppPaths.TasksFile); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.LogCrash(ex);
            _tasks = [];
            StatusLabel.Text = $"Aufgaben konnten nicht geladen werden: {ex.Message}";
        }
        _editingTask = _editingTask == null ? null : _tasks.FirstOrDefault(t => t.Id == _editingTask.Id);
        _taskDirty = false;
    }

    private bool SaveTasks()
    {
        try
        {
            TaskStore.Save(AppPaths.TasksFile, _tasks);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.LogCrash(ex);
            MessageBox.Show(this, $"Aufgaben konnten nicht gespeichert werden: {ex.Message}", AppNames.App, MessageBoxButton.OK, MessageBoxImage.Warning);
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
        UpdateTasksNavCount();
        UpdateTasksSubtitle();
    }

    private void UpdateTasksNavCount()
    {
        var open = _tasks.Count(t => !t.Done);
        TasksNavCount.Text = open > 0 ? open.ToString(De) : "";
        var today = TaskStore.OpenDueOn(_tasks, DateTime.Today).Count;
        TaskTodayDoneButton.IsEnabled = today > 0;
        TaskTodayDoneButton.ToolTip = today switch
        {
            0 => "Heute ist keine offene Aufgabe fällig",
            1 => "Die heute fällige Aufgabe als erledigt markieren",
            _ => $"Alle {today} heute fälligen Aufgaben als erledigt markieren",
        };
    }

    private void TaskTodayDone_Click(object sender, RoutedEventArgs e)
    {
        FlushTaskEdits();
        var due = TaskStore.OpenDueOn(_tasks, DateTime.Today);
        if (due.Count == 0) return;
        var list = string.Join("\n", due.Take(10).Select(t => "- " + t.Title)) + (due.Count > 10 ? $"\n- … und {due.Count - 10} weitere" : "");
        var overdue = _tasks.Count(t => TaskStore.IsOverdue(t, DateTime.Today));
        if (MessageBox.Show(this,
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
        StatusLabel.Text = done.Count == 1 ? "1 Aufgabe erledigt." : $"{done.Count} Aufgaben erledigt.";
    }

    private void UpdateTasksSubtitle()
    {
        if (_view != KbView.Tasks) return;
        var open = _tasks.Count(t => !t.Done);
        var overdue = _tasks.Count(t => TaskStore.IsOverdue(t, DateTime.Today));
        ContentSubtitle.Text = $"{open} offen" + (overdue > 0 ? $" · {overdue} überfällig" : "")
            + $" · {_tasks.Count - open} erledigt · Rechtsklick im Popup: \"Zu Aufgaben hinzufügen\"";
        DeleteButton.IsEnabled = TasksList.SelectedItem != null;
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
        UpdateTasksSubtitle();
    }

    // ---- New task --------------------------------------------------------------------------------------------

    private void NewTaskInput_TextChanged(object sender, TextChangedEventArgs e)
        => NewTaskHint.Visibility = NewTaskInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void NewTaskInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        AddNewTask();
        e.Handled = true;
    }

    private void AddNewTask()
    {
        var text = NewTaskInput.Text.Trim();
        if (text.Length == 0)
        {
            NewTaskInput.Focus();
            return;
        }
        FlushTaskEdits();
        var task = TaskStore.Create(text, DateTime.Now);
        if (DueFilterIsDay) task.Due = _dueFrom;
        _tasks.Add(task);
        if (!SaveTasks())
        {
            _tasks.Remove(task);
            return;
        }
        NewTaskInput.Text = "";
        if (TaskFilterDone.IsChecked == true) TaskFilterOpen.IsChecked = true;
        RefreshTaskList(task.Id);
        NewTaskInput.Focus();
    }

    // ---- Editor ----------------------------------------------------------------------------------------------

    private void LoadTaskEditor(KbTask? task)
    {
        _editingTask = task;
        _loadingTaskEditor = true;
        try
        {
            TaskEditor.IsEnabled = task != null;
            TaskDeleteButton.IsEnabled = task != null;
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
        if (e.Uri.Scheme is not ("http" or "https")) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { App.LogCrash(ex); }
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
        UpdateTasksNavCount();
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
        StatusLabel.Text = done ? $"Erledigt: {task.Title}" : $"Wieder offen: {task.Title}";
    }

    private void TaskDelete_Click(object sender, RoutedEventArgs e)
    {
        if (TasksList.SelectedItem is not TaskRow row) return;
        if (MessageBox.Show(this, $"Aufgabe \"{row.Task.Title}\" löschen?\n\nDer Eintrag, aus dem sie entstanden ist, bleibt erhalten.",
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

    /// <summary>Row that takes the place of <paramref name="task"/> in the list: the next one, or the previous at the end.</summary>
    private string? NeighbourOf(KbTask task)
    {
        var rows = TasksList.Items.OfType<TaskRow>().ToList();
        var position = rows.FindIndex(r => r.Task == task);
        var rest = rows.Where(r => r.Task != task).ToList();
        if (rest.Count == 0) return null;
        return rest[Math.Clamp(position < 0 ? 0 : position, 0, rest.Count - 1)].Task.Id;
    }

    private string TasksCopyText()
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

using System.Windows;

namespace Merkautomat.Views;

/// <summary>Window side of the "Aufgaben" section: navigation, header buttons and status line around <see cref="TasksPanel"/>.</summary>
public partial class KnowledgeBaseWindow
{
    private void InitTasks()
    {
        TasksView.StateChanged += (_, _) => UpdateTasksChrome();
        TasksView.StatusMessage += text => StatusLabel.Text = text;
        TasksView.Init(_app);
    }

    private void CloseTasks() => TasksView.Close();

    private void EnterTasks() => TasksView.Enter();

    internal void OpenTasks(string? selectId)
    {
        TasksView.PrepareOpen(selectId);
        if (TasksNav.IsChecked == true) EnterTasks();
        else TasksNav.IsChecked = true;
        if (selectId != null && TasksView.FocusNewTask(selectId))
            StatusLabel.Text = "Aufgabe angelegt. Titel, Fälligkeit und Notizen lassen sich rechts anpassen.";
    }

    private void UpdateTasksChrome()
    {
        var open = TasksView.OpenCount;
        TasksNavCount.Text = open > 0 ? open.ToString(De) : "";
        var today = TasksView.DueTodayCount;
        TaskTodayDoneButton.IsEnabled = today > 0;
        TaskTodayDoneButton.ToolTip = today switch
        {
            0 => "Heute ist keine offene Aufgabe fällig",
            1 => "Die heute fällige Aufgabe als erledigt markieren",
            _ => $"Alle {today} heute fälligen Aufgaben als erledigt markieren",
        };
        TaskDeleteButton.IsEnabled = TasksView.HasSelection;
        UpdateTasksSubtitle();
    }

    private void UpdateTasksSubtitle()
    {
        if (_view != KbView.Tasks) return;
        ContentSubtitle.Text = TasksView.Subtitle;
        DeleteButton.IsEnabled = TasksView.HasSelection;
    }

    private void TaskTodayDone_Click(object sender, RoutedEventArgs e) => TasksView.CompleteDueToday();

    private void TaskDelete_Click(object sender, RoutedEventArgs e) => TasksView.DeleteSelected();

    private string TasksCopyText() => TasksView.CopyText();
}

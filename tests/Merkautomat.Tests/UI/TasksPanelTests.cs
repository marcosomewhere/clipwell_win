using System.IO;
using System.Windows;
using System.Windows.Media;
using Merkautomat.Services;
using Merkautomat.Views;
using Xunit;

namespace Merkautomat.Tests;

public class TasksPanelTests
{
    [Fact]
    public void Panel_LoadsItsXamlAndStartsEmpty()
    {
        WithPanel([], (panel, _) =>
        {
            Assert.Equal(0, panel.OpenCount);
            Assert.False(panel.HasSelection);
            Assert.Empty(panel.VisibleTasks);
        });
    }

    [Fact]
    public void AddNewTask_SavesSelectsAndCountsIt()
    {
        WithPanel([], (panel, file) =>
        {
            var states = 0;
            panel.StateChanged += (_, _) => states++;

            var task = panel.AddNewTask("  Zertifikat erneuern  ");

            Assert.NotNull(task);
            Assert.Equal("Zertifikat erneuern", task.Title);
            Assert.Same(task, panel.SelectedTask);
            Assert.Equal(1, panel.OpenCount);
            Assert.True(states > 0);
            Assert.Equal(["Zertifikat erneuern"], TaskStore.Load(file).Select(t => t.Title));
            Assert.Contains("- [ ] Zertifikat erneuern", panel.CopyText());
        });
    }

    [Fact]
    public void AddNewTask_IgnoresBlankInput()
    {
        WithPanel([], (panel, file) =>
        {
            Assert.Null(panel.AddNewTask("   "));
            Assert.Empty(TaskStore.Load(file));
        });
    }

    [Fact]
    public void DeleteSelected_RemovesOnlyThatTaskAndSelectsTheNeighbour()
    {
        var first = TaskStore.Create("Erste", new DateTime(2026, 11, 1, 9, 0, 0));
        var second = TaskStore.Create("Zweite", new DateTime(2026, 11, 2, 9, 0, 0));
        WithPanel([first, second], (panel, file) =>
        {
            var deleted = panel.SelectedTask!;
            panel.DeleteSelected(confirm: false);

            var remaining = Assert.Single(TaskStore.Load(file));
            Assert.NotEqual(deleted.Id, remaining.Id);
            Assert.Equal(remaining.Id, panel.SelectedTask?.Id);
        });
    }

    [Fact]
    public void MailTask_ShowsItsSourceWithoutFailing()
    {
        var task = TaskStore.Create("Rechnung prüfen", new DateTime(2026, 11, 3, 9, 0, 0));
        task.SourceKey = "mail:abc";
        task.SourceTitle = "Mail von Anna: Rechnung";
        task.SourceText = "Von: Anna\nBetreff: Rechnung\n\nBitte bis Freitag prüfen: https://example.com/rechnung";
        WithPanel([task], (panel, _) =>
        {
            Assert.Equal(task.Id, panel.SelectedTask?.Id);
            Assert.Contains("- [ ] Rechnung prüfen", panel.CopyText());
        });
    }

    private static void WithPanel(List<KbTask> tasks, Action<TasksPanel, string> test)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"merkautomat-tasks-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "tasks.json");
        try
        {
            if (tasks.Count > 0) TaskStore.Save(file, tasks);
            StaTestRunner.Run(() =>
            {
                var app = TestApplication.Ensure();
                // App.xaml is not loaded in tests; the source view looks these two up directly.
                foreach (var key in new[] { "OnboardingAccentBrush", "EditorMutedBrush" })
                    if (!app.Resources.Contains(key)) app.Resources[key] = Brushes.Gray;

                var panel = new TasksPanel();
                var window = new Window { Content = panel, Width = 1000, Height = 700, ShowActivated = false };
                window.Show();
                panel.Init(null, file);
                panel.Enter();
                window.UpdateLayout();
                try { test(panel, file); }
                finally
                {
                    panel.Close();
                    window.Close();
                }
            });
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

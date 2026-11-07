using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ClipwellWin.Services;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;
using HAlign = System.Windows.HorizontalAlignment;
using VAlign = System.Windows.VerticalAlignment;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ClipwellWin.Views;

/// <summary>Month calendar with calendar weeks above the task list; a day or week filters the list by due date.</summary>
public partial class KnowledgeBaseWindow
{
    private const double CalendarCell = 34;
    private static readonly SolidColorBrush OverdueRed = new(Color.FromRgb(0xE8, 0x60, 0x3B));
    private DateTime _calendarMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime? _dueFrom;
    private DateTime? _dueTo;
    private Style? _calendarCellStyle;

    private bool DueFilterActive => _dueFrom.HasValue;
    private bool DueFilterIsDay => _dueFrom.HasValue && _dueFrom == _dueTo;

    private bool MatchesDueFilter(KbTask task)
        => _dueFrom is not { } from || TaskCalendar.IsDueIn(task, from, _dueTo ?? from);

    private void BuildTaskCalendar()
    {
        TaskCalendarTitle.Text = _calendarMonth.ToString("MMMM yyyy", De);
        RenderCalendar(TaskCalendarGrid, _calendarMonth, _dueFrom, _dueTo,
            day => SetDueFilter(day, day), monday => SetDueFilter(monday, monday.AddDays(6)));
    }

    /// <summary>
    /// Month grid with calendar weeks; <paramref name="from"/>/<paramref name="to"/> mark the selection (one day when
    /// equal). Without <paramref name="weekClick"/> the week numbers are labels only.
    /// </summary>
    private void RenderCalendar(Grid grid, DateTime month, DateTime? from, DateTime? to, Action<DateTime> dayClick, Action<DateTime>? weekClick)
    {
        grid.Children.Clear();
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();
        for (var c = 0; c < 8; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CalendarCell + 2) });

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string[] names = ["KW", "Mo", "Di", "Mi", "Do", "Fr", "Sa", "So"];
        for (var c = 0; c < names.Length; c++)
        {
            var header = new TextBlock
            {
                Text = names[c], FontSize = 10.5, HorizontalAlignment = HAlign.Center, Margin = new Thickness(0, 0, 0, 3),
                FontWeight = c == 0 ? FontWeights.SemiBold : FontWeights.Normal,
            };
            header.SetResourceReference(TextBlock.ForegroundProperty, "EditorMutedBrush");
            Place(grid, header, 0, c);
        }

        var today = DateTime.Today;
        var singleDay = from.HasValue && from == to;
        var counts = TaskCalendar.DayCounts(_tasks, today);
        var weeks = TaskCalendar.MonthWeeks(month.Year, month.Month);
        for (var w = 0; w < weeks.Count; w++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var monday = weeks[w];
            var weekSelected = weekClick != null && from == monday && to == monday.AddDays(6);
            var weekCell = CalendarCellBorder(weekSelected ? CellState.Selected : CellState.Normal, clickable: weekClick != null);
            var weekOpen = Enumerable.Range(0, 7).Sum(d => counts.GetValueOrDefault(monday.AddDays(d)).Open);
            weekCell.Child = new TextBlock
            {
                Text = TaskCalendar.Week(monday).ToString(De), FontSize = 11, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HAlign.Center, VerticalAlignment = VAlign.Center,
                Foreground = weekSelected ? Brushes.Black : (Brush)FindResource("InstrumentOrange"),
            };
            weekCell.ToolTip = $"KW {TaskCalendar.Week(monday)}: {monday.ToString("dd.MM.", De)} bis {monday.AddDays(6).ToString("dd.MM.yyyy", De)}"
                               + (weekOpen > 0 ? $" · {weekOpen} offen" : "") + (weekClick != null ? "\nKlicken: Aufgaben dieser Woche" : "");
            if (weekClick != null) weekCell.MouseLeftButtonUp += (_, _) => weekClick(monday);
            Place(grid, weekCell, w + 1, 0);

            for (var d = 0; d < 7; d++)
            {
                var day = monday.AddDays(d);
                var inMonth = day.Month == month.Month;
                var count = counts.GetValueOrDefault(day);
                var selected = from.HasValue && day >= from && day <= to;
                var cell = CalendarCellBorder(selected ? (singleDay ? CellState.Selected : CellState.InWeek) : CellState.Normal);
                if (day == today && !(selected && singleDay)) cell.SetResourceReference(Border.BorderBrushProperty, "OnboardingAccentBrush");

                var number = new TextBlock
                {
                    Text = day.Day.ToString(De), FontSize = 11.5, HorizontalAlignment = HAlign.Center,
                    FontWeight = day == today ? FontWeights.Bold : FontWeights.Normal,
                };
                if (selected && singleDay) number.Foreground = Brushes.Black;
                else if (!inMonth) number.SetResourceReference(TextBlock.ForegroundProperty, "EditorMutedBrush");
                else number.SetResourceReference(TextBlock.ForegroundProperty, "OnboardingTextPrimaryBrush");
                if (!inMonth) number.Opacity = 0.55;

                var stack = new StackPanel { VerticalAlignment = VAlign.Center };
                stack.Children.Add(number);
                stack.Children.Add(new Ellipse
                {
                    Width = 5, Height = 5, Margin = new Thickness(0, 2, 0, 0), HorizontalAlignment = HAlign.Center,
                    Fill = count.Overdue > 0 ? OverdueRed : count.Open > 0 ? (Brush)FindResource("InstrumentOrange") : (Brush)FindResource("EditorMutedBrush"),
                    Visibility = count.Open + count.Done > 0 ? Visibility.Visible : Visibility.Hidden,
                });
                cell.Child = stack;
                cell.ToolTip = day.ToString("dddd, dd.MM.yyyy", De) + DayCountText(count);
                cell.MouseLeftButtonUp += (_, _) => dayClick(day);
                Place(grid, cell, w + 1, d + 1);
            }
        }
    }

    private static string DayCountText(TaskDayCount count)
    {
        var parts = new List<string>();
        if (count.Open > 0) parts.Add($"{count.Open} offen");
        if (count.Overdue > 0) parts.Add($"{count.Overdue} überfällig");
        if (count.Done > 0) parts.Add($"{count.Done} erledigt");
        return parts.Count == 0 ? "\nKeine Aufgaben fällig" : "\n" + string.Join(", ", parts);
    }

    private enum CellState { Normal, InWeek, Selected }

    private Border CalendarCellBorder(CellState state, bool clickable = true)
    {
        _calendarCellStyle ??= CreateCalendarCellStyle();
        var border = new Border
        {
            Width = CalendarCell, Height = CalendarCell, Margin = new Thickness(1), CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, Cursor = clickable ? Cursors.Hand : null,
            Style = clickable ? _calendarCellStyle : null,
        };
        if (state == CellState.Selected) border.SetResourceReference(Border.BackgroundProperty, "OnboardingAccentBrush");
        else if (state == CellState.InWeek) border.SetResourceReference(Border.BackgroundProperty, "EditorSelectedBrush");
        return border;
    }

    private Style CreateCalendarCellStyle()
    {
        var style = new Style(typeof(Border));
        style.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.Transparent));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, FindResource("EditorHoverBrush")));
        style.Triggers.Add(hover);
        return style;
    }

    private static void Place(Grid grid, UIElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    /// <summary>Clicking the active day or week again clears the filter.</summary>
    private void SetDueFilter(DateTime from, DateTime to)
    {
        FlushTaskEdits();
        if (_dueFrom == from && _dueTo == to) { _dueFrom = null; _dueTo = null; }
        else { _dueFrom = from; _dueTo = to; }
        // Done tasks would stay hidden under "Offen", although the day is asked for as a whole.
        if (_dueFrom.HasValue && TaskFilterOpen.IsChecked == true) TaskFilterAll.IsChecked = true;
        RefreshTaskList((TasksList.SelectedItem as TaskRow)?.Task.Id);
    }

    private void UpdateDueFilterBar()
    {
        TaskDateFilterBar.Visibility = DueFilterActive ? Visibility.Visible : Visibility.Collapsed;
        NewTaskHint.Text = DueFilterIsDay
            ? $"Neue Aufgabe für {_dueFrom!.Value.ToString("dd.MM.yyyy", De)}, mit Enter hinzufügen"
            : "Neue Aufgabe, mit Enter hinzufügen";
        if (_dueFrom is not { } from) return;
        TaskDateFilterLabel.Text = DueFilterIsDay
            ? "Fällig am " + from.ToString("dddd, dd.MM.yyyy", De)
            : $"Fällig in KW {TaskCalendar.Week(from)} ({from.ToString("dd.MM.", De)} bis {_dueTo!.Value.ToString("dd.MM.yyyy", De)})";
    }

    // ---- Date picker for "Fällig am" -------------------------------------------------------------------------

    private DateTime _pickerMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    private void TaskDueBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!TaskDuePopup.IsOpen) OpenDuePicker();
    }

    private void TaskDueBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((e.KeyboardDevice.Modifiers == ModifierKeys.Alt && e.SystemKey == Key.Down) || e.Key == Key.F4)
        {
            OpenDuePicker();
            e.Handled = true;
        }
    }

    private void OpenDuePicker()
    {
        if (_editingTask == null) return;
        var shown = _editingTask.Due ?? DateTime.Today;
        _pickerMonth = new DateTime(shown.Year, shown.Month, 1);
        BuildDuePicker();
        TaskDuePopup.IsOpen = true;
    }

    private void BuildDuePicker()
    {
        TaskDuePickerTitle.Text = _pickerMonth.ToString("MMMM yyyy", De);
        var due = _editingTask?.Due?.Date;
        RenderCalendar(TaskDuePickerGrid, _pickerMonth, due, due, day => PickDue(day), weekClick: null);
    }

    private void PickDue(DateTime? day)
    {
        TaskDuePopup.IsOpen = false;
        // TextChanged validates, saves and refreshes the list and the calendar.
        TaskDueBox.Text = day?.ToString("dd.MM.yyyy", De) ?? "";
        TaskDueBox.Focus();
        TaskDueBox.CaretIndex = TaskDueBox.Text.Length;
    }

    private void TaskDuePickerPrev_Click(object sender, RoutedEventArgs e)
    {
        _pickerMonth = _pickerMonth.AddMonths(-1);
        BuildDuePicker();
    }

    private void TaskDuePickerNext_Click(object sender, RoutedEventArgs e)
    {
        _pickerMonth = _pickerMonth.AddMonths(1);
        BuildDuePicker();
    }

    private void TaskDueQuick_Click(object sender, RoutedEventArgs e)
    {
        var today = DateTime.Today;
        PickDue((sender as FrameworkElement)?.Tag switch
        {
            "0" => today,
            "1" => today.AddDays(1),
            "monday" => TaskCalendar.StartOfWeek(today).AddDays(7),
            _ => null,
        });
    }

    private void TaskDateFilterClear_Click(object sender, RoutedEventArgs e)
    {
        _dueFrom = null;
        _dueTo = null;
        RefreshTaskList((TasksList.SelectedItem as TaskRow)?.Task.Id);
    }

    private void TaskCalendarPrev_Click(object sender, RoutedEventArgs e)
    {
        _calendarMonth = _calendarMonth.AddMonths(-1);
        BuildTaskCalendar();
    }

    private void TaskCalendarNext_Click(object sender, RoutedEventArgs e)
    {
        _calendarMonth = _calendarMonth.AddMonths(1);
        BuildTaskCalendar();
    }

    private void TaskCalendarToday_Click(object sender, RoutedEventArgs e)
    {
        var today = DateTime.Today;
        _calendarMonth = new DateTime(today.Year, today.Month, 1);
        if (_dueFrom == today && _dueTo == today) BuildTaskCalendar();
        else SetDueFilter(today, today);
    }
}

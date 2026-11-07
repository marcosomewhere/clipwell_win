using System.Globalization;

namespace ClipwellWin.Services;

/// <summary>Tasks due on one day of the calendar: open, overdue (open and in the past) and done.</summary>
public readonly record struct TaskDayCount(int Open, int Overdue, int Done);

/// <summary>Month grid with ISO calendar weeks (weeks start on Monday) and due-date filtering for the Aufgaben section.</summary>
public static class TaskCalendar
{
    public static DateTime StartOfWeek(DateTime date)
        => date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    public static int Week(DateTime date) => ISOWeek.GetWeekOfYear(date);

    /// <summary>Mondays of all weeks that contain a day of the month, in order (four to six weeks).</summary>
    public static List<DateTime> MonthWeeks(int year, int month)
    {
        var first = new DateTime(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var weeks = new List<DateTime>();
        for (var monday = StartOfWeek(first); monday <= last; monday = monday.AddDays(7)) weeks.Add(monday);
        return weeks;
    }

    /// <summary>Due date within <paramref name="from"/> to <paramref name="to"/>, both days inclusive.</summary>
    public static bool IsDueIn(KbTask task, DateTime from, DateTime to)
        => task.Due is { } due && due.Date >= from.Date && due.Date <= to.Date;

    public static Dictionary<DateTime, TaskDayCount> DayCounts(IEnumerable<KbTask> tasks, DateTime today)
        => tasks.Where(t => t.Due.HasValue).GroupBy(t => t.Due!.Value.Date).ToDictionary(g => g.Key, g => new TaskDayCount(
            g.Count(t => !t.Done), g.Count(t => TaskStore.IsOverdue(t, today)), g.Count(t => t.Done)));
}

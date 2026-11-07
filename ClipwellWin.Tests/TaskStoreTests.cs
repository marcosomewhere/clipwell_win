using System.IO;
using ClipwellWin.Models;
using ClipwellWin.Services;
using Xunit;

namespace ClipwellWin.Tests;

public sealed class TaskStoreTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 7, 10, 0, 0);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clipwell-tasks-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_root, "tasks.json");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsAllFields()
    {
        var task = TaskStore.Create("Rechnung prüfen", Now);
        task.Notes = "bis Freitag\nmit Beleg";
        task.Due = new DateTime(2026, 11, 14);
        task.SourceKey = "clip:42";
        task.SourceTitle = "Rechnung";
        task.SourceText = "Rechnung Nr. 7";
        TaskStore.SetDone(task, true, Now.AddHours(1));

        TaskStore.Save(FilePath, [task]);
        var loaded = Assert.Single(TaskStore.Load(FilePath));

        Assert.Equal(task.Id, loaded.Id);
        Assert.Equal("Rechnung prüfen", loaded.Title);
        Assert.Equal("bis Freitag\nmit Beleg", loaded.Notes);
        Assert.Equal(new DateTime(2026, 11, 14), loaded.Due);
        Assert.True(loaded.Done);
        Assert.Equal(Now.AddHours(1), loaded.Completed);
        Assert.Equal("clip:42", loaded.SourceKey);
        Assert.Equal("Rechnung Nr. 7", loaded.SourceText);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void Load_MissingFileIsEmpty()
        => Assert.Empty(TaskStore.Load(FilePath));

    [Fact]
    public void Load_KeepsUnreadableFileAsideInsteadOfOverwritingIt()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(FilePath, "{ kaputt");

        Assert.Empty(TaskStore.Load(FilePath));

        Assert.False(File.Exists(FilePath));
        var kept = Assert.Single(Directory.GetFiles(_root, "tasks.json.defekt-*"));
        Assert.Equal("{ kaputt", File.ReadAllText(kept));
    }

    [Fact]
    public void FromEntry_CopiesTitleAndTextOfTheEntry()
    {
        var entry = new ClipboardEntry { Id = 5, Type = EntryType.Text, Content = "## Angebot anfordern\nbei Firma X", Timestamp = Now };

        var task = TaskStore.FromEntry(entry, "clip:5", Now);

        Assert.Equal("Angebot anfordern", task.Title);
        Assert.Equal("clip:5", task.SourceKey);
        Assert.Equal("## Angebot anfordern\nbei Firma X", task.SourceText);
        Assert.False(task.Done);
        Assert.Equal(Now, task.Created);
    }

    [Fact]
    public void FromEntry_ImageUsesOcrTextAndLongTextIsShortened()
    {
        var image = new ClipboardEntry { Id = 6, Type = EntryType.Image, OcrText = "Termin 12.11.", Timestamp = Now };
        Assert.Equal("Termin 12.11.", TaskStore.FromEntry(image, "clip:6", Now).SourceText);

        var longText = new ClipboardEntry { Id = 7, Type = EntryType.Text, Content = new string('a', 10_000), Timestamp = Now };
        var task = TaskStore.FromEntry(longText, "clip:7", Now);
        Assert.Equal(TaskStore.MaxSourceText + 1, task.SourceText!.Length);
        Assert.True(task.Title.Length <= TaskStore.MaxTitle);
    }

    [Theory]
    [InlineData("- [ ] Milch kaufen", "Milch kaufen")]
    [InlineData("1. Server neu starten", "Server neu starten")]
    [InlineData("   \n  # Bericht  \nText", "Bericht")]
    [InlineData("  ", "Neue Aufgabe")]
    public void TitleFrom_StripsMarkdownMarkers(string text, string expected)
        => Assert.Equal(expected, TaskStore.TitleFrom(text));

    [Fact]
    public void Sorted_OpenByDueDateThenNewestThenDone()
    {
        KbTask Make(string title, DateTime created, DateTime? due = null, bool done = false)
        {
            var t = TaskStore.Create(title, created);
            t.Due = due;
            if (done) TaskStore.SetDone(t, true, created.AddDays(1));
            return t;
        }
        var tasks = new[]
        {
            Make("ohne Datum alt", Now.AddDays(-3)),
            Make("erledigt", Now, done: true),
            Make("fällig später", Now, new DateTime(2026, 12, 1)),
            Make("ohne Datum neu", Now.AddDays(-1)),
            Make("fällig bald", Now, new DateTime(2026, 11, 9)),
        };

        Assert.Equal(["fällig bald", "fällig später", "ohne Datum neu", "ohne Datum alt", "erledigt"],
            TaskStore.Sorted(tasks).Select(t => t.Title));
    }

    [Fact]
    public void CompleteDueOn_MarksOnlyOpenTasksOfThatDay()
    {
        KbTask Due(string title, DateTime? due, bool done = false)
        {
            var t = TaskStore.Create(title, Now.AddDays(-5));
            t.Due = due;
            if (done) TaskStore.SetDone(t, true, Now.AddDays(-1));
            return t;
        }
        var tasks = new[]
        {
            Due("heute", new DateTime(2026, 11, 7)),
            Due("heute auch", new DateTime(2026, 11, 7)),
            Due("schon erledigt", new DateTime(2026, 11, 7), done: true),
            Due("überfällig", new DateTime(2026, 11, 6)),
            Due("morgen", new DateTime(2026, 11, 8)),
            Due("ohne Datum", null),
        };

        var done = TaskStore.CompleteDueOn(tasks, Now, Now);

        Assert.Equal(["heute", "heute auch"], done.Select(t => t.Title));
        Assert.All(done, t => Assert.Equal(Now, t.Completed));
        Assert.Equal(Now.AddDays(-1), tasks[2].Completed);
        Assert.Equal(["überfällig", "morgen", "ohne Datum"], tasks.Where(t => !t.Done).Select(t => t.Title));
        Assert.Empty(TaskStore.OpenDueOn(tasks, Now));
    }

    [Fact]
    public void SetDone_AndOverdue()
    {
        var task = TaskStore.Create("x", Now);
        task.Due = new DateTime(2026, 11, 6);
        Assert.True(TaskStore.IsOverdue(task, Now));
        Assert.False(TaskStore.IsOverdue(task, new DateTime(2026, 11, 6, 23, 0, 0)));

        TaskStore.SetDone(task, true, Now);
        Assert.Equal(Now, task.Completed);
        Assert.False(TaskStore.IsOverdue(task, Now));

        TaskStore.SetDone(task, false, Now);
        Assert.Null(task.Completed);
    }

    [Theory]
    [InlineData("", true, null)]
    [InlineData("14.11.2026", true, "2026-11-14")]
    [InlineData("1.2.27", true, "2027-02-01")]
    [InlineData("24.12.", true, "2026-12-24")]
    [InlineData("2026-11-30", true, "2026-11-30")]
    [InlineData("31.02.2026", false, null)]
    [InlineData("morgen", false, null)]
    public void TryParseDue_AcceptsGermanDates(string text, bool valid, string? expected)
    {
        Assert.Equal(valid, TaskStore.TryParseDue(text, Now, out var due));
        Assert.Equal(expected == null ? null : DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), due);
    }
}

public sealed class TaskCalendarTests
{
    [Fact]
    public void MonthWeeks_StartOnMondayAndCoverTheMonth()
    {
        var weeks = TaskCalendar.MonthWeeks(2026, 11);

        Assert.Equal(new DateTime(2026, 10, 26), weeks[0]);
        Assert.Equal(new DateTime(2026, 11, 30), weeks[^1]);
        Assert.Equal(6, weeks.Count);
        Assert.All(weeks, w => Assert.Equal(DayOfWeek.Monday, w.DayOfWeek));
        Assert.Equal(4, TaskCalendar.MonthWeeks(2027, 2).Count);
    }

    [Theory]
    [InlineData("2026-11-07", 45)]
    [InlineData("2026-12-31", 53)]
    [InlineData("2027-01-01", 53)]
    [InlineData("2027-01-04", 1)]
    public void Week_IsIsoCalendarWeek(string date, int week)
        => Assert.Equal(week, TaskCalendar.Week(DateTime.Parse(date, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void StartOfWeek_SundayBelongsToThePreviousMonday()
        => Assert.Equal(new DateTime(2026, 11, 2), TaskCalendar.StartOfWeek(new DateTime(2026, 11, 8, 18, 0, 0)));

    [Fact]
    public void IsDueIn_IncludesBothDaysAndSkipsTasksWithoutDate()
    {
        var task = TaskStore.Create("a", new DateTime(2026, 11, 1));
        Assert.False(TaskCalendar.IsDueIn(task, new DateTime(2026, 11, 1), new DateTime(2026, 12, 1)));
        task.Due = new DateTime(2026, 11, 8);
        Assert.True(TaskCalendar.IsDueIn(task, new DateTime(2026, 11, 2), new DateTime(2026, 11, 8)));
        Assert.True(TaskCalendar.IsDueIn(task, new DateTime(2026, 11, 8), new DateTime(2026, 11, 8)));
        Assert.False(TaskCalendar.IsDueIn(task, new DateTime(2026, 11, 9), new DateTime(2026, 11, 15)));
    }

    [Fact]
    public void DayCounts_SplitsOpenOverdueAndDone()
    {
        var today = new DateTime(2026, 11, 7);
        KbTask Due(DateTime due, bool done = false)
        {
            var t = TaskStore.Create("x", today);
            t.Due = due;
            if (done) TaskStore.SetDone(t, true, today);
            return t;
        }
        var counts = TaskCalendar.DayCounts(
            [Due(new DateTime(2026, 11, 5)), Due(new DateTime(2026, 11, 5), done: true), Due(new DateTime(2026, 11, 9)), TaskStore.Create("ohne", today)],
            today);

        Assert.Equal(new TaskDayCount(1, 1, 1), counts[new DateTime(2026, 11, 5)]);
        Assert.Equal(new TaskDayCount(1, 0, 0), counts[new DateTime(2026, 11, 9)]);
        Assert.Equal(2, counts.Count);
    }
}

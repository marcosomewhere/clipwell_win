using ClipwellWin.Models;
using ClipwellWin.ViewModels;
using Xunit;

namespace ClipwellWin.Tests;

public class EntryBadgeTests
{
    [Theory]
    [InlineData(EntryType.Url, "example.com", "URL", "example.com")]
    [InlineData(EntryType.Code, "CODE", "CODE", null)]
    [InlineData(EntryType.Image, "PNG", "IMG", "PNG")]
    [InlineData(EntryType.Text, null, "TEXT", null)]
    public void EntryViewModel_ExposesBadgesForAllEntryTypes(
        EntryType type,
        string? kind,
        string expectedBadge,
        string? expectedDetail)
    {
        var vm = new EntryViewModel(new ClipboardEntry
        {
            Type = type,
            ContentKind = kind,
            Timestamp = DateTime.Now,
        });

        Assert.Equal(expectedBadge, vm.BadgeText);
        Assert.Equal(expectedDetail, vm.DetailBadgeText);
    }

    [Fact]
    public void RefreshTimestamp_NotifiesRelativeTimeAndGroupLabel()
    {
        var vm = new EntryViewModel(new ClipboardEntry
        {
            Type = EntryType.Text,
            Content = "note",
            Timestamp = DateTime.Now,
        });
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.RefreshTimestamp();

        Assert.Contains(nameof(EntryViewModel.RelativeTime), changed);
        Assert.Contains(nameof(EntryViewModel.GroupLabel), changed);
    }

    [Fact]
    public void MarkUsed_IncrementsUseCountAndNotifies()
    {
        var usedAt = new DateTime(2026, 6, 27, 12, 0, 0, DateTimeKind.Local);
        var vm = new EntryViewModel(new ClipboardEntry
        {
            Type = EntryType.Text,
            Content = "note",
            Timestamp = DateTime.Now,
            UseCount = 2,
        });
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.MarkUsed(usedAt);

        Assert.Equal(3, vm.UseCount);
        Assert.Equal(usedAt, vm.LastUsedAt);
        Assert.Contains(nameof(EntryViewModel.UseCount), changed);
        Assert.Contains(nameof(EntryViewModel.LastUsedAt), changed);
    }
}

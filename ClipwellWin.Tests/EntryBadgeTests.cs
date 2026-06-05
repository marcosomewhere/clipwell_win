using ClipwellWin.Models;
using ClipwellWin.ViewModels;
using Xunit;

namespace ClipwellWin.Tests;

public class EntryBadgeTests
{
    [Theory]
    [InlineData(EntryType.Url, "example.com", "URL", "example.com")]
    [InlineData(EntryType.Code, "PS1", "CODE", "PS1")]
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
}

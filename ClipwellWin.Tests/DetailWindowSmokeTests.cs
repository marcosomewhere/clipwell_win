using System.Windows;
using ClipwellWin.Models;
using ClipwellWin.ViewModels;
using ClipwellWin.Views;
using Xunit;

namespace ClipwellWin.Tests;

public class DetailWindowSmokeTests
{
    [Theory]
    [MemberData(nameof(Entries))]
    public void DetailWindow_CanRenderSupportedEntryTypes(ClipboardEntry entry)
    {
        StaTestRunner.Run(() =>
        {
            if (Application.Current == null)
                _ = new Application();

            var window = new DetailWindow(new EntryViewModel(entry));
            window.Show();
            window.UpdateLayout();
            Assert.NotNull(window.Content);
            window.Close();
        });
    }

    public static IEnumerable<object[]> Entries()
    {
        yield return
        [
            new ClipboardEntry
            {
                Type = EntryType.Text,
                Content = "plain text details",
                Timestamp = DateTime.Now,
                DetectionReason = "test",
            }
        ];
        yield return
        [
            new ClipboardEntry
            {
                Type = EntryType.Code,
                Content = "SELECT id FROM users WHERE active = 1;",
                Language = "SQL",
                ContentKind = "SQL",
                Timestamp = DateTime.Now,
                DetectionReason = "test",
            }
        ];
        yield return
        [
            new ClipboardEntry
            {
                Type = EntryType.Color,
                Content = "#14B8A6",
                HexColor = "#14B8A6",
                ContentKind = "COLOR",
                Timestamp = DateTime.Now,
                DetectionReason = "test",
            }
        ];
        yield return
        [
            new ClipboardEntry
            {
                Type = EntryType.Image,
                ImageData = Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAFgwJ/lx0OUwAAAABJRU5ErkJggg=="),
                ContentKind = "PNG",
                Timestamp = DateTime.Now,
                DetectionReason = "test",
            }
        ];
    }
}

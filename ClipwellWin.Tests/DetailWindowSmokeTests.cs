using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
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

    [Fact]
    public void DetailWindow_CodeEditor_PopulatesHighlightOverlay()
    {
        StaTestRunner.Run(() =>
        {
            if (Application.Current == null)
                _ = new Application();

            var entry = new ClipboardEntry
            {
                Type = EntryType.Code,
                Content = "function sayHello(name) {\n  const message = \"Hallo\";\n  return message;\n}",
                Language = "JavaScript",
                ContentKind = "JS",
                Timestamp = DateTime.Now,
                DetectionReason = "test",
            };

            var window = new DetailWindow(new EntryViewModel(entry));
            window.Show();
            window.UpdateLayout();

            var overlay = Assert.IsType<TextBlock>(window.FindName("CodeHighlightOverlay"));
            Assert.Equal(Visibility.Visible, overlay.Visibility);
            Assert.NotEmpty(overlay.Inlines);
            Assert.Contains(overlay.Inlines.OfType<Run>(), run => run.Foreground is SolidColorBrush);

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

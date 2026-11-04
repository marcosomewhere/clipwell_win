using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

    [Fact]
    public void DetailWindow_CodeEditor_RehighlightsWhenLanguageChanges()
    {
        StaTestRunner.Run(() =>
        {
            if (Application.Current == null)
                _ = new Application();

            var entry = new ClipboardEntry
            {
                Type = EntryType.Code,
                Content = "var value = 1;\nconst other = value;",
                Language = "JavaScript",
                ContentKind = "CODE",
                Timestamp = DateTime.Now,
                DetectionReason = "test",
            };

            var window = new DetailWindow(new EntryViewModel(entry));
            window.Show();
            window.UpdateLayout();

            var overlay = Assert.IsType<TextBlock>(window.FindName("CodeHighlightOverlay"));
            Assert.Contains(overlay.Inlines.OfType<Run>(), run => run.Text == "var" && IsKeywordBrush(run.Foreground));

            var box = Assert.IsType<ComboBox>(window.FindName("EditorLanguageBox"));
            box.SelectedItem = "C++";
            window.UpdateLayout();

            Assert.DoesNotContain(overlay.Inlines.OfType<Run>(), run => run.Text == "var" && IsKeywordBrush(run.Foreground));

            window.Close();
        });
    }

    [Fact]
    public void DetailWindow_ImageEditor_EnablesOcrToggleWhenOcrArrives()
    {
        StaTestRunner.Run(() =>
        {
            if (Application.Current == null)
                _ = new Application();

            var vm = new EntryViewModel(new ClipboardEntry
            {
                Type = EntryType.Image,
                ImageData = Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAFgwJ/lx0OUwAAAABJRU5ErkJggg=="),
                ContentKind = "PNG",
                Timestamp = DateTime.Now,
                DetectionReason = "test",
            });

            var window = new DetailWindow(vm);
            window.Show();
            window.UpdateLayout();

            var button = Assert.IsType<Button>(window.FindName("ShowOcrBtn"));
            var label = Assert.IsType<TextBlock>(window.FindName("ShowOcrBtnLabel"));
            Assert.Equal(Visibility.Visible, button.Visibility);
            Assert.True(button.IsEnabled);
            Assert.Equal("OCR-Text anzeigen", label.Text);

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();

            var textEditor = Assert.IsType<Border>(window.FindName("TextEditorPanel"));
            Assert.Equal(Visibility.Visible, textEditor.Visibility);
            Assert.Equal("Bild anzeigen", label.Text);
            var textPreview = Assert.IsType<TextBox>(window.FindName("TextPreview"));
            textPreview.Focus();
            window.UpdateLayout();
            Assert.NotEqual(Colors.White, Assert.IsType<SolidColorBrush>(textPreview.Background).Color);

            vm.Entry.OcrText = "Erkannter Text";
            vm.RefreshPreview();
            window.UpdateLayout();

            Assert.True(button.IsEnabled);
            Assert.Equal("Erkannter Text", textPreview.Text);

            window.Close();
        });
    }

    [Fact]
    public void DetailWindow_ImageEditor_RenderEditedImage_PreservesFullyTransparentColorData()
    {
        StaTestRunner.Run(() =>
        {
            if (Application.Current == null)
                _ = new Application();

            var window = new DetailWindow(new EntryViewModel(new ClipboardEntry
            {
                Type = EntryType.Image,
                ImageData = Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAFgwJ/lx0OUwAAAABJRU5ErkJggg=="),
                ContentKind = "PNG",
                Timestamp = DateTime.Now,
                DetectionReason = "test",
            }));
            window.Show();
            window.UpdateLayout();

            var source = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null,
                new byte[] { 255, 0, 0, 0, 0, 0, 255, 0 }, 8);
            typeof(DetailWindow)
                .GetField("_baseImage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(window, source);

            var canvas = Assert.IsType<Canvas>(window.FindName("EditorCanvas"));
            var image = Assert.IsType<Image>(window.FindName("ImagePreview"));
            canvas.Width = 2;
            canvas.Height = 1;
            image.Source = source;
            image.Width = 2;
            image.Height = 1;
            Canvas.SetLeft(image, 0);
            Canvas.SetTop(image, 0);
            window.UpdateLayout();

            var rendered = Assert.IsType<RenderTargetBitmap>(typeof(DetailWindow)
                .GetMethod("RenderEditedImage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(window, null));
            var pixels = new byte[8];
            new FormatConvertedBitmap(rendered, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, 8, 0);

            Assert.Equal(255, pixels[0]);
            Assert.Equal(0, pixels[1]);
            Assert.Equal(0, pixels[2]);
            Assert.Equal(255, pixels[3]);
            Assert.Equal(0, pixels[4]);
            Assert.Equal(0, pixels[5]);
            Assert.Equal(255, pixels[6]);
            Assert.Equal(255, pixels[7]);

            window.Close();
        });
    }

    private static bool IsKeywordBrush(Brush? brush)
        => brush is SolidColorBrush solid
           && solid.Color.R == 197
           && solid.Color.G == 134
           && solid.Color.B == 232;

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

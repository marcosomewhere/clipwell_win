using System.Reflection;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ClipwellWin.Views;
using Xunit;

namespace ClipwellWin.Tests;

public sealed class StoryViewerTests
{
    private static T Field<T>(StoryViewerWindow window, string name)
        => (T)typeof(StoryViewerWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    [Fact]
    public void AllStoryPagesAreEmbedded()
        => StaTestRunner.Run(() =>
        {
            for (var page = 0; page < StoryViewerWindow.PageCount; page++)
            {
                var image = BitmapFrame.Create(System.Windows.Application.GetResourceStream(StoryViewerWindow.PageUri(page))!.Stream);
                Assert.True(image.PixelWidth > 0 && image.PixelHeight > image.PixelWidth);
            }
        });

    [Fact]
    public void PagingStaysInRangeAndUpdatesButtons()
        => StaTestRunner.Run(() =>
        {
            var window = new StoryViewerWindow();
            var prev = Field<Button>(window, "PrevButton");
            var next = Field<Button>(window, "NextButton");
            var label = Field<TextBlock>(window, "PageLabel");

            Assert.False(prev.IsEnabled);
            Assert.Equal($"Seite 1 von {StoryViewerWindow.PageCount}", label.Text);

            var show = typeof(StoryViewerWindow).GetMethod("ShowPage", BindingFlags.Instance | BindingFlags.NonPublic)!;
            show.Invoke(window, [99]);
            Assert.Equal($"Seite {StoryViewerWindow.PageCount} von {StoryViewerWindow.PageCount}", label.Text);
            Assert.Equal("Schließen", next.Content);
            Assert.True(prev.IsEnabled);

            show.Invoke(window, [-3]);
            Assert.Equal(0, Field<int>(window, "_page"));
            window.Close();
        });
}

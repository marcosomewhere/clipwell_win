using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipwellWin.Models;
using ClipwellWin.Services;
using ClipwellWin.ViewModels;
using ClipwellWin.Views;
using Xunit;

namespace ClipwellWin.Tests;

public class PopupPresentationTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 0, 40, 0);

    [Theory]
    [InlineData(2026, 11, 6, 0, 39, 30, "gerade eben")]
    [InlineData(2026, 11, 6, 0, 34, 0, "vor 6 Min.")]
    [InlineData(2026, 11, 6, 0, 0, 0, "vor 40 Min.")]
    [InlineData(2026, 11, 5, 23, 37, 0, "Gestern, 23:37")]
    [InlineData(2026, 11, 3, 14, 5, 0, "Di, 14:05")]
    [InlineData(2026, 10, 20, 9, 0, 0, "20.10.2026")]
    public void FormatTime_FollowsTheListGroups(int y, int mo, int d, int h, int mi, int s, string expected)
        => Assert.Equal(expected, EntryViewModel.FormatTime(new DateTime(y, mo, d, h, mi, s), Now));

    [Fact]
    public void FormatTime_TodayInHours_AndFutureAsJustNow()
    {
        var noon = new DateTime(2026, 11, 6, 15, 0, 0);
        Assert.Equal("vor 3 Std.", EntryViewModel.FormatTime(noon.AddHours(-3).AddMinutes(-10), noon));
        Assert.Equal("gerade eben", EntryViewModel.FormatTime(noon.AddMinutes(2), noon));
    }

    [Fact]
    public void ImageEntries_ShowSizeAsTitleAndQuotedOcrBelow()
    {
        var vm = new EntryViewModel(new ClipboardEntry
        {
            Type = EntryType.Image,
            OcrText = "a Q\n»urchsuchen   Text",
            ImageSize = (1280, 720),
            Timestamp = Now,
        });

        Assert.Equal("Bild 1280×720", vm.HeadlineText);
        Assert.Equal("„a Q »urchsuchen Text“", vm.SublineText);
        Assert.Equal("Kein Text erkannt", EntryViewModel.OcrSubline(" "));
        Assert.Equal("Bild", EntryViewModel.ImageHeadline((0, 0)));
    }

    [Fact]
    public void HeadlineText_IsUrlTitleForUrlsAndEmptyForText()
    {
        var url = new EntryViewModel(new ClipboardEntry { Type = EntryType.Url, Content = "https://x.org", UrlTitle = "X", Timestamp = Now });
        var text = new EntryViewModel(new ClipboardEntry { Type = EntryType.Text, Content = "Hallo", Timestamp = Now });

        Assert.Equal("X", url.HeadlineText);
        Assert.Equal("https://x.org", url.SublineText);
        Assert.Null(text.HeadlineText);
        Assert.Equal("Hallo", text.SublineText);
    }

    private static byte[] Png(int width, int height)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, new byte[width * height * 4], width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    [Fact]
    public void TryReadImageSize_ReadsPngGifAndBmpHeaders()
    {
        var png = Png(37, 21);
        Assert.True(ImageUtils.TryReadImageSize(png.AsSpan(0, ImageUtils.ImageHeaderBytes), out var w, out var h));
        Assert.Equal((37, 21), (w, h));

        byte[] gif = [(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 10, 0, 5, 0];
        Assert.True(ImageUtils.TryReadImageSize(gif, out w, out h));
        Assert.Equal((10, 5), (w, h));

        var bmp = new byte[26];
        bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
        BitConverter.GetBytes(64).CopyTo(bmp, 18);
        BitConverter.GetBytes(-48).CopyTo(bmp, 22);
        Assert.True(ImageUtils.TryReadImageSize(bmp, out w, out h));
        Assert.Equal((64, 48), (w, h));

        Assert.False(ImageUtils.TryReadImageSize([1, 2, 3], out _, out _));
    }

    [Fact]
    public void LoadAllLite_KnowsImageSizeWithoutLoadingImageData()
    {
        var path = Path.Combine(Path.GetTempPath(), $"clipwell-test-{Guid.NewGuid():N}.db");
        try
        {
            using (var db = new DatabaseService(path))
                db.Insert(new ClipboardEntry { Type = EntryType.Image, ImageData = Png(120, 45), Timestamp = Now, DetectionReason = "test" });

            using var reopened = new DatabaseService(path);
            var entry = Assert.Single(reopened.LoadAllLite());
            Assert.Null(entry.ImageData);
            Assert.Equal((120, 45), entry.ImageSize);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var f in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(f)) File.Delete(f);
        }
    }

    [Fact]
    public void ShortcutText_UsesSettingsNotation()
    {
        Assert.Equal("Ctrl+N", PopupWindow.ShortcutText("N", ctrl: true, alt: false, shift: false, win: false));
        Assert.Equal("Ctrl+Alt+Shift+Win+F2", PopupWindow.ShortcutText("F2", true, true, true, true));
        Assert.Equal("", PopupWindow.ShortcutText(" ", true, false, false, false));
    }

    [Fact]
    public void KbArtwork_IsEmbeddedAsResource()
    {
        StaTestRunner.Run(() =>
        {
            if (Application.Current == null) _ = new Application();
            foreach (var name in new[] { "kb-icon-16.png", "kb-rec-logo.png" })
            {
                var info = Application.GetResourceStream(new Uri($"pack://application:,,,/Merkautomat;component/Assets/{name}"));
                Assert.NotNull(info);
                using var stream = info!.Stream;
                Assert.True(stream.Length > 0, name);
            }
        });
    }
}

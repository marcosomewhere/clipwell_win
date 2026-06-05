using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipwellWin.Models;
using ClipwellWin.Services;
using Xunit;

namespace ClipwellWin.Tests;

public class ClipboardProcessorIntegrationTests
{
    [Fact]
    public void BuildEntry_CapturesPlainText()
    {
        StaTestRunner.Run(() =>
        {
            var data = new DataObject();
            data.SetText("A normal clipboard sentence.");

            var entry = ClipboardProcessor.BuildEntry(data);

            Assert.NotNull(entry);
            Assert.Equal(EntryType.Text, entry!.Type);
            Assert.Equal("A normal clipboard sentence.", entry.Content);
        });
    }

    [Fact]
    public void BuildEntry_CapturesUrl()
    {
        StaTestRunner.Run(() =>
        {
            var data = new DataObject();
            data.SetText("https://example.com/path");

            var entry = ClipboardProcessor.BuildEntry(data);

            Assert.NotNull(entry);
            Assert.Equal(EntryType.Url, entry!.Type);
            Assert.Contains("URL erkannt", entry.DetectionReason);
            Assert.Equal("example.com", entry.ContentKind);
        });
    }

    [Fact]
    public void BuildEntry_CapturesImage()
    {
        StaTestRunner.Run(() =>
        {
            var bitmap = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
            bitmap.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[] { 0, 0, 255, 255 }, 4, 0);
            var data = new DataObject();
            data.SetImage(bitmap);

            var entry = ClipboardProcessor.BuildEntry(data);

            Assert.NotNull(entry);
            Assert.Equal(EntryType.Image, entry!.Type);
            Assert.NotNull(entry.ImageData);
            Assert.NotEmpty(entry.ImageData!);
            Assert.Equal("PNG", entry.ContentKind);
        });
    }

    [Theory]
    [InlineData("$env:CLIPWELL='1'\nGet-ChildItem | Select-Object Name", "PS1")]
    [InlineData("<configuration><appSettings><add key=\"mode\" value=\"test\" /></appSettings></configuration>", "XML")]
    [InlineData("server.port=8080\napp.name=Clipwell", "PROPS")]
    [InlineData("API_KEY=test\nFEATURE_FLAG=true", "ENV")]
    public void BuildEntry_AddsDetailedBadgesForKnownTextKinds(string text, string expectedKind)
    {
        StaTestRunner.Run(() =>
        {
            var data = new DataObject();
            data.SetText(text);

            var entry = ClipboardProcessor.BuildEntry(data, CodeDetectionMode.Aggressive);

            Assert.NotNull(entry);
            Assert.Equal(expectedKind, entry!.ContentKind);
        });
    }
}

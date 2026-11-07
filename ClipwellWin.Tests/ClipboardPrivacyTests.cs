using System.IO;
using System.Windows;
using ClipwellWin.Services;
using Xunit;

namespace ClipwellWin.Tests;

public class ClipboardPrivacyTests
{
    private static DataObject Secret(Action<DataObject> mark)
    {
        var data = new DataObject();
        data.SetText("hunter2-Geheim!");
        mark(data);
        return data;
    }

    [Fact]
    public void BuildEntry_SkipsExcludeFromMonitorFormat()
        => StaTestRunner.Run(() =>
        {
            var data = Secret(d => d.SetData(ClipboardProcessor.ExcludeFromMonitorFormat, new MemoryStream([0])));
            Assert.True(ClipboardProcessor.IsExcludedFromHistory(data));
            Assert.Null(ClipboardProcessor.BuildEntry(data));
        });

    [Fact]
    public void BuildEntry_SkipsClipboardViewerIgnore()
        => StaTestRunner.Run(() =>
        {
            var data = Secret(d => d.SetData(ClipboardProcessor.ViewerIgnoreFormat, new MemoryStream([0])));
            Assert.Null(ClipboardProcessor.BuildEntry(data));
        });

    [Fact]
    public void BuildEntry_SkipsCanIncludeInHistoryZero()
        => StaTestRunner.Run(() =>
        {
            var data = Secret(d => d.SetData(ClipboardProcessor.CanIncludeInHistoryFormat,
                new MemoryStream(BitConverter.GetBytes(0u))));
            Assert.Null(ClipboardProcessor.BuildEntry(data));
        });

    [Fact]
    public void BuildEntry_KeepsCanIncludeInHistoryOne()
        => StaTestRunner.Run(() =>
        {
            var data = Secret(d => d.SetData(ClipboardProcessor.CanIncludeInHistoryFormat,
                new MemoryStream(BitConverter.GetBytes(1u))));
            Assert.False(ClipboardProcessor.IsExcludedFromHistory(data));
            Assert.Equal("hunter2-Geheim!", ClipboardProcessor.BuildEntry(data)?.Content);
        });

    [Fact]
    public void BuildEntry_KeepsUnmarkedText()
        => StaTestRunner.Run(() =>
        {
            var data = Secret(_ => { });
            Assert.False(ClipboardProcessor.IsExcludedFromHistory(data));
            Assert.NotNull(ClipboardProcessor.BuildEntry(data));
        });
}

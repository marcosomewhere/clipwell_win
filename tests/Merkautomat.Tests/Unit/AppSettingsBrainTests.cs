using System.Text.Json;
using Merkautomat.Models;
using Xunit;

namespace Merkautomat.Tests;

public class AppSettingsBrainTests
{
    [Fact]
    public void OldSettingsFileWithoutBrainFields_GetsDefaults()
    {
        var s = JsonSerializer.Deserialize<AppSettings>("""{ "MaxHistoryItems": 300 }""")!;

        Assert.Equal(300, s.MaxHistoryItems);
        Assert.Equal(AppSettings.BrainThresholdDefault, s.BrainThreshold);
        Assert.True(s.BrainShowClipboard && s.BrainShowNotes && s.BrainShowRecordings);
        Assert.True(s.BrainEdgeSemantic && s.BrainEdgeTime && s.BrainEdgeSession && s.BrainEdgeDomain);
        Assert.True(s.BrainEdgeManual);
        Assert.False(s.BrainFocus);
    }

    [Fact]
    public void BrainSettings_SurviveJsonRoundTrip()
    {
        var s = new AppSettings
        {
            BrainThreshold = 0.91,
            BrainShowRecordings = false,
            BrainEdgeTime = false,
            BrainEdgeManual = false,
            BrainFocus = true,
        };

        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s))!;

        Assert.Equal(0.91, back.BrainThreshold);
        Assert.False(back.BrainShowRecordings);
        Assert.False(back.BrainEdgeTime);
        Assert.False(back.BrainEdgeManual);
        Assert.True(back.BrainFocus);
        Assert.True(back.BrainShowClipboard);
    }

    [Theory]
    [InlineData(0.5, 0.80)]
    [InlineData(1.2, 0.95)]
    [InlineData(0.8749, 0.87)]
    [InlineData(double.NaN, 0.86)]
    public void EffectiveBrainThreshold_StaysWithinSliderRange(double stored, double expected)
        => Assert.Equal(expected, new AppSettings { BrainThreshold = stored }.EffectiveBrainThreshold());

    [Fact]
    public void NormalizeBrainSources_ShowsAllWhenEverySourceIsOff()
    {
        var s = new AppSettings { BrainShowClipboard = false, BrainShowNotes = false, BrainShowRecordings = false, BrainShowKnowledgeFolder = false };
        s.NormalizeBrainSources();
        Assert.True(s.BrainShowClipboard && s.BrainShowNotes && s.BrainShowRecordings);

        var partial = new AppSettings { BrainShowClipboard = false };
        partial.NormalizeBrainSources();
        Assert.False(partial.BrainShowClipboard);
    }

    [Theory]
    [InlineData(1, 100)]
    [InlineData(1000, 1000)]
    [InlineData(2500, 2500)]
    [InlineData(100000, 10000)]
    public void BrainLimit_IsConfigurableWithinRange(int stored, int expected)
        => Assert.Equal(expected, new AppSettings { BrainMaxNodes = stored }.EffectiveBrainMaxNodes());

    [Fact]
    public void FolderAndLimit_PersistWithoutChangingLegacyDefaults()
    {
        var defaults = JsonSerializer.Deserialize<AppSettings>("{}")!;
        Assert.Equal(1000, defaults.EffectiveBrainMaxNodes());
        Assert.False(defaults.KnowledgeFolderEnabled);
        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings
        {
            KnowledgeFolderPath = @"D:\Wissen", KnowledgeFolderEnabled = true, BrainMaxNodes = 3500,
            BrainShowKnowledgeFolder = false,
        }))!;
        Assert.Equal(@"D:\Wissen", back.KnowledgeFolderPath);
        Assert.True(back.KnowledgeFolderEnabled);
        Assert.False(back.BrainShowKnowledgeFolder);
        Assert.Equal(3500, back.EffectiveBrainMaxNodes());
    }
}

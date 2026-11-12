using Merkautomat.Models;
using Merkautomat.Services;
using Xunit;

namespace Merkautomat.Tests;

public sealed class AiExecutionTests
{
    private static AiResources Resources => new(DateTime.UtcNow, 32768, 16000, 10, 0.9, 16000, false,
        new("t600", "T600", "test", 4096, 3938, 0, 60, false));
    private static LocalAiModel Granite => new("models");

    [Fact]
    public void FullGpuFitsFourGbWithDesktopReserveButNotAnOccupiedGpu()
    {
        var full = new AiExecutionPlan("vulkan", -1, 2);
        Assert.True(AiExecutionPolicy.FitsGpu(Resources, Granite, full));
        var busy = Resources with { Gpu = Resources.Gpu! with { FreeMiB = 3000 } };
        Assert.False(AiExecutionPolicy.FitsGpu(busy, Granite, full));
        Assert.True(AiExecutionPolicy.FitsGpu(busy, Granite, full with { GpuLayers = 20 }));
    }

    [Fact]
    public void ModelStartReservesRamAndCommitForWindows()
    {
        Assert.Null(AiExecutionPolicy.StartBlockReason(Resources, Granite));
        Assert.NotNull(AiExecutionPolicy.StartBlockReason(Resources with { FreeRamMiB = 7000 }, Granite));
        Assert.NotNull(AiExecutionPolicy.StartBlockReason(Resources with { FreeCommitMiB = 1000 }, Granite));
        Assert.NotNull(AiExecutionPolicy.StartBlockReason(AiResources.Unknown, Granite));
    }

    [Fact]
    public void HotGpuStopsGenerationButAllowsCpuFallback()
    {
        var hot = Resources with { Gpu = Resources.Gpu! with { Temperature = 86 } };
        Assert.NotNull(AiExecutionPolicy.CriticalReason(hot, true));
        Assert.Null(AiExecutionPolicy.CriticalReason(hot, false));
        Assert.False(AiExecutionPolicy.FitsGpu(hot, Granite, new("vulkan", -1, 2)));
        var throttled = Resources with { Gpu = Resources.Gpu! with { ThermalThrottle = true } };
        Assert.NotNull(AiExecutionPolicy.CriticalReason(throttled, true));
    }

    [Fact]
    public void BackgroundIgnoresOwnGpuUtilizationButYieldsToExternalLoad()
    {
        var busy = Resources with { Gpu = Resources.Gpu! with { Utilization = 99 } };
        Assert.Null(AiExecutionPolicy.BackgroundBlockReason(busy, true));
        Assert.NotNull(AiExecutionPolicy.BackgroundBlockReason(busy, false));
        Assert.NotNull(AiExecutionPolicy.BackgroundBlockReason(Resources with { CpuUtilization = 90 }, true));
        Assert.NotNull(AiExecutionPolicy.BackgroundBlockReason(Resources with { CpuUtilization = 65, CpuClockRatio = 0.4 }, true));
    }

    [Fact]
    public void StaleOrUnknownGpuSensorsCannotAuthorizeGpuAllocation()
    {
        var plan = new AiExecutionPlan("vulkan", -1, 2);
        Assert.False(AiExecutionPolicy.FitsGpu(Resources with { SampledUtc = DateTime.UtcNow.AddMinutes(-1) }, Granite, plan));
        Assert.False(AiExecutionPolicy.FitsGpu(Resources with { Gpu = null }, Granite, plan));
        Assert.False(AiExecutionPolicy.FitsGpu(Resources with { Gpu = Resources.Gpu! with { Utilization = 95 } }, Granite, plan));
        Assert.False(AiExecutionPolicy.FitsGpu(Resources with { Gpu = Resources.Gpu! with { Temperature = null } }, Granite, plan));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(20)]
    public void CpuCandidatesLeaveLogicalProcessorsAvailable(int logical)
    {
        var plans = AiExecutionPolicy.Candidates(Granite, logical, true);
        Assert.All(plans, p => Assert.True(AiExecutionPolicy.ValidPlan(p)));
        Assert.All(plans.Where(p => !p.UsesGpu), p => Assert.InRange(p.Threads, 1, Math.Max(1, logical / 2)));
        Assert.Contains(plans, p => p.UsesGpu && p.GpuLayers == -1);
        Assert.Contains(plans, p => p.GpuLayers == 20);
    }

    [Fact]
    public void DriverXmlHandlesUnsupportedSensorsAndNeverTreatsThemAsZero()
    {
        const string xml = "<!DOCTYPE nvidia_smi_log SYSTEM 'file:///missing'><nvidia_smi_log><driver_version>596.86</driver_version><gpu><uuid>unit</uuid><fb_memory_usage><total>4096 MiB</total><free>3500 MiB</free></fb_memory_usage><temperature><gpu_temp>N/A</gpu_temp></temperature><utilization><gpu_util></gpu_util></utilization></gpu></nvidia_smi_log>";
        var gpu = AiResourceMonitor.ParseGpu(xml)!;
        Assert.Equal(3500, gpu.FreeMiB);
        Assert.Null(gpu.Temperature);
        Assert.Null(gpu.Utilization);
        Assert.Null(gpu.ThermalThrottle);
        Assert.Null(AiResourceMonitor.ParseGpu("<nvidia_smi_log/>"));
    }

    [Theory]
    [InlineData("Active", true)]
    [InlineData("Not Active", false)]
    public void DriverThermalFlagIsRespected(string flag, bool expected)
    {
        var gpu = AiResourceMonitor.ParseGpu($"<nvidia_smi_log><gpu><fb_memory_usage><total>4096 MiB</total><free>2000 MiB</free></fb_memory_usage><clocks_event_reasons><clocks_event_reason_hw_thermal_slowdown>{flag}</clocks_event_reason_hw_thermal_slowdown></clocks_event_reasons></gpu></nvidia_smi_log>");
        Assert.Equal(expected, gpu!.ThermalThrottle);
    }

    [Fact]
    public void NewSettingsRemainCompatibleWithOlderProfiles()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{}")!;
        Assert.Equal("auto", settings.LocalAiComputeMode);
        Assert.Equal("auto", AiExecutionPolicy.Normalize("unknown"));
        Assert.False(AiExecutionPolicy.ValidPlan(new("cuda", 10, 4)));
        Assert.False(AiExecutionPolicy.ValidPlan(new("cpu", 10, 4)));
        Assert.False(AiExecutionPolicy.ValidPlan(new("vulkan", int.MaxValue, 4)));
    }
}

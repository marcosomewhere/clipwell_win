namespace Merkautomat.Services;

public sealed record AiExecutionPlan(string Backend, int GpuLayers, int Threads)
{
    public bool UsesGpu => Backend == "vulkan" && GpuLayers != 0;
    public string Key => $"{Backend}:{GpuLayers}:{Threads}";
    public string Name => UsesGpu ? GpuLayers < 0 ? $"GPU vollständig · {Threads} CPU-Threads" : $"CPU/GPU · {GpuLayers} GPU-Schichten · {Threads} Threads" : $"CPU · {Threads} Threads";
}

public static class AiExecutionPolicy
{
    public const long VramReserveMiB = 768;
    public const double GpuStopTemperature = 85;
    public const double GpuResumeTemperature = 78;
    public static string Normalize(string? value) => value is "cpu" or "gpu" ? value : "auto";
    public static bool ValidPlan(AiExecutionPlan p) => p.Threads is > 0 and <= 8
        && (p.Backend == "cpu" && p.GpuLayers == 0 || p.Backend == "vulkan" && (p.GpuLayers == -1 || p.GpuLayers is > 0 and <= 40));
    public static int LayerCount(LocalAiModel model) => model.ModelId == LocalAiModel.QwenId ? 24 : 40;
    public static long EstimatedGpuMiB(LocalAiModel model, AiExecutionPlan plan)
        => plan.UsesGpu ? (long)Math.Ceiling(model.ModelDownload.Bytes / 1048576.0
            * (plan.GpuLayers < 0 ? 1 : (double)plan.GpuLayers / LayerCount(model))) + 768 : 0;
    public static long RamReserveMiB(AiResources r) => Math.Max(4096, (long)(r.TotalRamMiB * 0.15));

    public static string? StartBlockReason(AiResources r, LocalAiModel model)
    {
        if (!r.Fresh || r.TotalRamMiB == 0) return "Ressourcenmessung fehlt oder ist veraltet.";
        if (r.FreeRamMiB < RamReserveMiB(r) + model.ModelDownload.Bytes / 1048576 + 1024)
            return "Zu wenig freier RAM mit Reserve für Windows und die Suche.";
        if (r.FreeCommitMiB < 2048) return "Zu wenig freier Commit-Speicher; Modellstart pausiert.";
        return null;
    }

    public static string? CriticalReason(AiResources r, bool gpuActive)
    {
        if (!r.Fresh || r.TotalRamMiB == 0) return "Ressourcenmessung fehlt oder ist veraltet.";
        if (gpuActive && r.Gpu == null) return "GPU-Ressourcenmessung ist nicht verfügbar.";
        if (r.TotalRamMiB > 0 && r.FreeRamMiB < Math.Max(1024, r.TotalRamMiB / 12) || r.FreeCommitMiB is > 0 and < 1024)
            return "Speicherdruck: lokales Sprachmodell wird entladen.";
        if (gpuActive && r.Gpu is { } gpu)
        {
            if (gpu.ThermalThrottle == true || gpu.Temperature >= GpuStopTemperature) return "GPU zu warm oder thermisch gedrosselt.";
            if (gpu.FreeMiB < 192) return "Zu wenig freier Grafikspeicher für einen stabilen Betrieb.";
        }
        return null;
    }

    public static string? BackgroundBlockReason(AiResources r, bool ownGpuWork)
    {
        if (!r.Fresh) return "pausiert bis zur aktuellen Ressourcenmessung";
        if (r.TotalRamMiB == 0 || r.FreeRamMiB < RamReserveMiB(r) || r.FreeCommitMiB < 2048) return "pausiert wegen Speicherdruck";
        if (r.CpuUtilization >= 85) return "pausiert wegen hoher CPU-Auslastung";
        if (r.CpuUtilization >= 60 && r.CpuClockRatio < 0.55) return "pausiert wegen hoher CPU-Last bei reduziertem Takt";
        if (r.Gpu is { } gpu)
        {
            if (gpu.ThermalThrottle == true || gpu.Temperature >= 80) return "pausiert wegen GPU-Temperatur/Drosselung";
            if (!ownGpuWork && gpu.Utilization >= 85) return "pausiert, die GPU wird anderweitig gebraucht";
        }
        return null;
    }

    public static bool FitsGpu(AiResources r, LocalAiModel model, AiExecutionPlan plan)
        => r.Fresh && r.Gpu is { } gpu && gpu.ThermalThrottle != true
            && gpu.Utilization is < 85 && gpu.Temperature is < GpuStopTemperature
            && gpu.FreeMiB >= EstimatedGpuMiB(model, plan) + VramReserveMiB;

    public static AiExecutionPlan CpuDefault(int logicalProcessors) => new("cpu", 0, Math.Clamp(logicalProcessors / 4, 1, 4));

    public static IReadOnlyList<AiExecutionPlan> Candidates(LocalAiModel model, int logicalProcessors, bool gpuAvailable)
    {
        var plans = new List<AiExecutionPlan>();
        foreach (var threads in new[] { 2, 4, 8 }.Select(t => Math.Clamp(t, 1, Math.Max(1, logicalProcessors / 2))).Distinct())
            plans.Add(new("cpu", 0, threads));
        if (gpuAvailable)
        {
            plans.Add(new("vulkan", -1, Math.Min(2, logicalProcessors)));
            plans.Add(new("vulkan", -1, Math.Min(4, logicalProcessors)));
            plans.Add(new("vulkan", LayerCount(model) / 2, Math.Min(4, logicalProcessors)));
        }
        return plans;
    }
}

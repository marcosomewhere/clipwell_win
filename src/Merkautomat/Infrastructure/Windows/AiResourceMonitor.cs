using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Linq;

namespace Merkautomat.Services;

public sealed record GpuResources(string Id, string Name, string Driver, long TotalMiB, long FreeMiB,
    double? Utilization, double? Temperature, bool? ThermalThrottle);
public sealed record AiResources(DateTime SampledUtc, long TotalRamMiB, long FreeRamMiB, double? CpuUtilization,
    double? CpuClockRatio, long FreeCommitMiB, bool OnBattery, GpuResources? Gpu)
{
    public static AiResources Unknown { get; } = new(DateTime.MinValue, 0, 0, null, null, 0, false, null);
    public bool Fresh => DateTime.UtcNow - SampledUtc < TimeSpan.FromSeconds(15);
}

/// <summary>Windows memory/CPU counters and the NVIDIA driver's installed diagnostic tool; no sensor driver.</summary>
public sealed class AiResourceMonitor : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _probe = new(1, 1);
    private AiResources _latest = AiResources.Unknown;
    private ulong _idle, _kernel, _user;
    private Task? _loop;
    public AiResources Latest => Volatile.Read(ref _latest);
    public event Action? Changed;

    public void Start() => _loop ??= Task.Run(LoopAsync);

    private async Task LoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await RefreshAsync(_stop.Token).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(3), _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    public async Task<AiResources> RefreshAsync(CancellationToken ct)
    {
        await _probe.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var memory = new MEMORYSTATUSEX { Length = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            var hasMemory = GlobalMemoryStatusEx(ref memory);
            double? cpu = null;
            if (GetSystemTimes(out var idle, out var kernel, out var user))
            {
                var total = kernel + user;
                var previous = _kernel + _user;
                if (previous > 0 && total > previous && idle >= _idle)
                    cpu = Math.Clamp(100.0 * (1 - (double)(idle - _idle) / (total - previous)), 0, 100);
                (_idle, _kernel, _user) = (idle, kernel, user);
            }
            var battery = BackgroundWork.Probe(DateTime.MinValue).OnBattery;
            var snapshot = new AiResources(DateTime.UtcNow, hasMemory ? (long)(memory.TotalPhys / 1048576) : 0,
                hasMemory ? (long)(memory.AvailPhys / 1048576) : 0, cpu, ReadClockRatio(),
                hasMemory ? (long)(memory.AvailPageFile / 1048576) : 0, battery, await ReadGpuAsync(ct).ConfigureAwait(false));
            Volatile.Write(ref _latest, snapshot);
            Changed?.Invoke();
            return snapshot;
        }
        finally { _probe.Release(); }
    }

    private static double? ReadClockRatio()
    {
        var values = new PROCESSOR_POWER_INFORMATION[Environment.ProcessorCount];
        var size = Marshal.SizeOf<PROCESSOR_POWER_INFORMATION>() * values.Length;
        if (CallNtPowerInformation(11, IntPtr.Zero, 0, values, (uint)size) != 0) return null;
        var ratios = values.Where(v => v.MaxMhz > 0).Select(v => (double)v.CurrentMhz / v.MaxMhz).ToArray();
        return ratios.Length > 0 ? ratios.Average() : null;
    }

    private static async Task<GpuResources?> ReadGpuAsync(CancellationToken ct)
    {
        var tool = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
        if (!File.Exists(tool)) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        using var process = new Process
        {
            StartInfo = new(tool) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true },
        };
        process.StartInfo.ArgumentList.Add("-q");
        process.StartInfo.ArgumentList.Add("-x");
        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var xml = await output.ConfigureAwait(false);
            await error.ConfigureAwait(false);
            return process.ExitCode == 0 && xml.Length <= 262144 ? ParseGpu(xml) : null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        catch (Exception ex) when (ex is Win32Exception or IOException or XmlException)
        { App.LogError("GPU-Ressourcen abfragen", ex); return null; }
        finally
        {
            try { if (process.Id != 0 && !process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (Win32Exception ex) { App.LogError("GPU-Abfrage beenden", ex); }
        }
    }

    public static GpuResources? ParseGpu(string xml)
    {
        using var text = new StringReader(xml.TrimStart('\uFEFF'));
        using var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = 262144 });
        var document = XDocument.Load(reader);
        var gpu = document.Root?.Elements("gpu").FirstOrDefault();
        if (gpu == null) return null;
        var memory = gpu.Element("fb_memory_usage");
        var total = Number(memory?.Element("total")?.Value);
        var free = Number(memory?.Element("free")?.Value);
        if (total is not > 0 || free == null || free < 0 || free > total) return null;
        var thermal = gpu.Element("clocks_event_reasons") ?? gpu.Element("clocks_throttle_reasons");
        var flags = thermal?.Elements().Where(e => e.Name.LocalName.Contains("thermal_slowdown", StringComparison.Ordinal)).ToArray();
        bool? throttled = flags is { Length: > 0 } && flags.All(e => e.Value is "Active" or "Not Active")
            ? flags.Any(e => e.Value == "Active") : null;
        return new(gpu.Element("uuid")?.Value ?? "", gpu.Element("product_name")?.Value ?? "NVIDIA GPU",
            document.Root?.Element("driver_version")?.Value ?? "", (long)total, (long)free,
            Number(gpu.Element("utilization")?.Element("gpu_util")?.Value),
            Number(gpu.Element("temperature")?.Element("gpu_temp")?.Value), throttled);
    }

    private static double? Number(string? value) => value != null
        && double.TryParse(value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
        && double.IsFinite(number) ? number : null;

    public void Dispose() => _stop.Cancel();

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint Length, Load;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESSOR_POWER_INFORMATION { public uint Number, MaxMhz, CurrentMhz, MhzLimit, MaxIdleState, CurrentIdleState; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX memory);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("powrprof.dll")] private static extern uint CallNtPowerInformation(int level, IntPtr input, uint inputSize,
        [Out] PROCESSOR_POWER_INFORMATION[] output, uint outputSize);
}

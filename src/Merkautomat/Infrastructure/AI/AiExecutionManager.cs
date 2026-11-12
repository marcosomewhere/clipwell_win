using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Merkautomat.Services;

public sealed record AiExecutionMeasurement(string Signature, string ModelId, AiExecutionPlan Plan, double Milliseconds,
    double LoadMilliseconds, bool Success, DateTime MeasuredUtc);

public sealed class AiExecutionManager : IDisposable
{
    public const string FileName = "ai-execution-benchmarks.json";
    public AiResourceMonitor Resources { get; }
    public GpuRuntime Runtime { get; }
    private readonly Func<string> _mode;
    private readonly string _file;
    private readonly object _gate = new();
    private List<AiExecutionMeasurement> _measurements = [];
    private DateTime _gpuBlockedUntil;
    private bool _gpuHot;
    private string _status = "Noch kein Ausführungsvergleich für dieses Modell.";
    public string Status { get { lock (_gate) return _status; } }
    public event Action? Changed;
    public string Mode => AiExecutionPolicy.Normalize(_mode());

    public AiExecutionManager(string modelsDirectory, AiResourceMonitor resources, Func<string> mode)
    {
        Resources = resources;
        Runtime = new(modelsDirectory);
        _mode = mode;
        _file = Path.Combine(Path.GetDirectoryName(modelsDirectory)!, FileName);
        if (File.Exists(_file))
        {
            try
            {
                _measurements = (JsonSerializer.Deserialize<List<AiExecutionMeasurement>>(SecureFile.ReadAllText(_file)) ?? [])
                    .Where(m => m != null && m.Plan != null && AiExecutionPolicy.ValidPlan(m.Plan)
                        && double.IsFinite(m.Milliseconds) && m.Milliseconds >= 0).Take(48).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or System.Security.Cryptography.CryptographicException)
            { App.LogError("KI-Ausführungsvergleiche laden", ex); }
        }
        Resources.Changed += ResourceChanged;
        if (_measurements.Count > 0) _status = $"{_measurements.Count} gespeicherte Messwerte; Auswahl beim nächsten Modellaufruf.";
    }

    private void ResourceChanged()
    {
        var gpu = Resources.Latest.Gpu;
        lock (_gate)
        {
            if (gpu?.ThermalThrottle == true || gpu?.Temperature >= AiExecutionPolicy.GpuStopTemperature) _gpuHot = true;
            else if (gpu?.Temperature < AiExecutionPolicy.GpuResumeTemperature && gpu.ThermalThrottle == false) _gpuHot = false;
        }
        Changed?.Invoke();
    }

    public string Signature(LocalAiModel model)
    {
        var gpu = Resources.Latest.Gpu;
        var text = $"{model.ModelDownload.Sha256}|{GpuRuntime.Archive.Sha256}|{LocalAiModel.Runtime.Sha256}|{gpu?.Id}|{gpu?.Driver}|{Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")}|{Environment.ProcessorCount}|ctx4096-b128-ub128-json64";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    public bool NeedsBenchmark(LocalAiModel model)
    {
        if (Mode != "auto" || !Runtime.IsInstalled || Resources.Latest.Gpu == null) return false;
        var signature = Signature(model);
        lock (_gate) return !_measurements.Any(m => m.Signature == signature && m.Success && m.Milliseconds > 0);
    }

    public bool CanBenchmark(LocalAiModel model)
    {
        var r = Resources.Latest;
        return AiExecutionPolicy.StartBlockReason(r, model) == null && !r.OnBattery && r.CpuUtilization is < 65
            && (r.Gpu == null || r.Gpu.Utilization is < 35 && r.Gpu.Temperature is < 78 && r.Gpu.ThermalThrottle != true);
    }

    public AiExecutionPlan Select(LocalAiModel model, AiExecutionPlan? active = null)
    {
        var resources = Resources.Latest;
        var block = active == null ? AiExecutionPolicy.StartBlockReason(resources, model) : AiExecutionPolicy.CriticalReason(resources, false);
        if (block != null) throw new InvalidOperationException(block);
        var signature = Signature(model);
        AiExecutionMeasurement[] measurements;
        bool gpuAllowed;
        lock (_gate)
        {
            measurements = _measurements.Where(m => m.Signature == signature && m.Success && double.IsFinite(m.Milliseconds) && m.Milliseconds > 0).ToArray();
            gpuAllowed = Mode != "cpu" && Runtime.IsInstalled && !_gpuHot && DateTime.UtcNow >= _gpuBlockedUntil;
        }
        bool Usable(AiExecutionPlan p) => p.Backend == "cpu" && p.GpuLayers == 0 && p.Threads is > 0 and <= 8
            || gpuAllowed && p.UsesGpu && p.Threads is > 0 and <= 8
                && (active?.Key == p.Key && resources.Gpu?.FreeMiB >= 256 || AiExecutionPolicy.FitsGpu(resources, model, p));
        var ranked = measurements.Where(m => Usable(m.Plan)).OrderBy(m => m.Milliseconds).ToArray();
        var preferred = Mode == "gpu" ? ranked.FirstOrDefault(m => m.Plan.UsesGpu) ?? ranked.FirstOrDefault() : ranked.FirstOrDefault();
        if (preferred != null)
        {
            SetStatus($"{model.Name}: {preferred.Plan.Name} · gemessen {preferred.Milliseconds / 1000:0.00} s je Testanfrage.");
            return preferred.Plan;
        }
        if (gpuAllowed)
        {
            var full = new AiExecutionPlan("vulkan", -1, Math.Min(2, Environment.ProcessorCount));
            if (active?.Key == full.Key && resources.Gpu?.FreeMiB >= 256 || AiExecutionPolicy.FitsGpu(resources, model, full)) return full;
            var partial = full with { GpuLayers = AiExecutionPolicy.LayerCount(model) / 2, Threads = Math.Min(4, Environment.ProcessorCount) };
            if (AiExecutionPolicy.FitsGpu(resources, model, partial)) return partial;
        }
        return AiExecutionPolicy.CpuDefault(Environment.ProcessorCount);
    }

    public void SetStatus(string status)
    {
        lock (_gate) _status = status;
        Changed?.Invoke();
    }

    public void BlockGpu(string reason)
    {
        lock (_gate) _gpuBlockedUntil = DateTime.UtcNow.AddMinutes(5);
        SetStatus(reason + " CPU-Ausweichbetrieb für fünf Minuten.");
    }

    public void Store(LocalAiModel model, IReadOnlyList<AiExecutionMeasurement> measurements)
    {
        var signature = Signature(model);
        List<AiExecutionMeasurement> copy;
        lock (_gate)
        {
            _measurements.RemoveAll(m => m.Signature == signature);
            _measurements.AddRange(measurements);
            _measurements = _measurements.OrderByDescending(m => m.MeasuredUtc).Take(48).ToList();
            copy = _measurements.ToList();
        }
        SecureFile.WriteAllTextAtomic(_file, JsonSerializer.Serialize(copy));
        var best = measurements.Where(m => m.Success).MinBy(m => m.Milliseconds);
        SetStatus(best == null ? "Kein erfolgreicher Ausführungsvergleich." : $"Gemessen für {model.Name}: {best.Plan.Name}, {best.Milliseconds / 1000:0.00} s je Testanfrage.");
    }

    public IReadOnlyList<AiExecutionMeasurement> Measurements { get { lock (_gate) return _measurements.ToArray(); } }
    public void Dispose() => Resources.Changed -= ResourceChanged;
}

namespace Merkautomat.Services;

/// <summary>One ONNX inference at a time on the CPU, independently of GPU generation.</summary>
public static class AiCpuWork
{
    private static readonly SemaphoreSlim Inference = new(1, 1);
    public static Func<AiResources>? Resources { get; set; }
    public static int Threads => Resources?.Invoke().CpuUtilization >= 70 ? 2 : Math.Clamp(Environment.ProcessorCount / 4, 1, 4);

    public static IDisposable Enter(CancellationToken ct = default)
    {
        var resources = Resources?.Invoke();
        if (resources is { Fresh: true, TotalRamMiB: > 0 }
            && (resources.FreeRamMiB < Math.Max(768, resources.TotalRamMiB / 20) || resources.FreeCommitMiB < 1024))
            throw new InvalidOperationException("CPU-KI pausiert wegen Speicherdruck. Bitte zunächst Speicher freigeben.");
        Inference.Wait(ct);
        return new Lease();
    }

    private sealed class Lease : IDisposable
    {
        public void Dispose() => Inference.Release();
    }
}

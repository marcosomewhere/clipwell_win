using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace Merkautomat.Services;

public sealed partial class LocalAiService
{
    public Task<IReadOnlyList<AiExecutionMeasurement>> BenchmarkAsync(CancellationToken ct = default)
        => ExecuteAsync(token => CompareAsync(Model, token, automatic: false), "Bitte den Vergleich später wiederholen.", ct, benchmark: true);

    private async Task<IReadOnlyList<AiExecutionMeasurement>> CompareAsync(LocalAiModel model, CancellationToken ct, bool automatic)
    {
        var execution = Execution ?? throw new InvalidOperationException("Ressourcensteuerung ist nicht verfügbar.");
        lock (_sync) StopProcessCore();
        await execution.Resources.RefreshAsync(ct).ConfigureAwait(false);
        if (!execution.CanBenchmark(model))
        {
            if (automatic) return [];
            throw new InvalidOperationException("Vergleich benötigt Netzbetrieb, freien Speicher und geringe CPU-/GPU-Last ohne Überhitzung.");
        }
        Benchmarking = true;
        var results = new List<AiExecutionMeasurement>();
        var signature = execution.Signature(model);
        var candidates = AiExecutionPolicy.Candidates(model, Environment.ProcessorCount, execution.Runtime.IsInstalled);
        if (automatic) candidates = candidates.Where(p => p.Backend == "cpu" && p.Threads == 4 || p.UsesGpu && p.Threads == 2 || p.GpuLayers > 0).ToArray();
        try
        {
            foreach (var plan in candidates)
            {
                ct.ThrowIfCancellationRequested();
                lock (_sync) StopProcessCore();
                // Driver utilization is averaged over a preceding interval, including our just-finished test.
                await Task.Delay(1500, ct).ConfigureAwait(false);
                var before = await execution.Resources.RefreshAsync(ct).ConfigureAwait(false);
                for (var retry = 0; retry < 3 && !execution.CanBenchmark(model); retry++)
                {
                    await Task.Delay(1000, ct).ConfigureAwait(false);
                    before = await execution.Resources.RefreshAsync(ct).ConfigureAwait(false);
                }
                if (!execution.CanBenchmark(model)) break;
                if (plan.UsesGpu && !AiExecutionPolicy.FitsGpu(before, model, plan)) continue;
                execution.SetStatus($"{model.Name} vergleichen: {plan.Name} …");
                var timer = Stopwatch.StartNew();
                try
                {
                    using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    limit.CancelAfter(TimeSpan.FromSeconds(60));
                    await StartAsync(model, limit.Token, plan).ConfigureAwait(false);
                    var load = timer.Elapsed.TotalMilliseconds;
                    if (plan.UsesGpu)
                    {
                        var resident = await execution.Resources.RefreshAsync(limit.Token).ConfigureAwait(false);
                        if (resident.Gpu == null || before.Gpu!.FreeMiB - resident.Gpu.FreeMiB < 64)
                            throw new InvalidOperationException("GPU-Speicherbelegung konnte nicht bestätigt werden.");
                    }
                    await SendChatAsync(model, BenchmarkRequest(0), "den Vergleich", limit.Token, plan).ConfigureAwait(false);
                    var timings = new List<double>();
                    for (var sample = 1; sample <= 2; sample++)
                    {
                        timer.Restart();
                        await SendChatAsync(model, BenchmarkRequest(sample), "den Vergleich", limit.Token, plan).ConfigureAwait(false);
                        timings.Add(timer.Elapsed.TotalMilliseconds);
                    }
                    var after = await execution.Resources.RefreshAsync(limit.Token).ConfigureAwait(false);
                    if (AiExecutionPolicy.CriticalReason(after, plan.UsesGpu) != null)
                        throw new InvalidOperationException("Ressourcenlimit im Vergleich erreicht.");
                    results.Add(new(signature, model.ModelId, plan, timings.Average(), load, true, DateTime.UtcNow));
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && ex is InvalidOperationException or HttpRequestException or OperationCanceledException or ObjectDisposedException)
                {
                    App.LogError("Lokalen Ausführungsvergleich durchführen", ex);
                    results.Add(new(signature, model.ModelId, plan, 0, 0, false, DateTime.UtcNow));
                    execution.SetStatus($"{plan.Name}: Vergleich nicht erfolgreich; nächste sichere Variante wird geprüft.");
                }
            }
            if (results.Any(r => r.Success)) execution.Store(model, results);
            else execution.SetStatus("Vergleich zurückgestellt: keine erfolgreiche Messung unter geeigneten Bedingungen.");
            return results;
        }
        finally
        {
            lock (_sync) StopProcessCore();
            Benchmarking = false;
        }
    }

    private static string BenchmarkRequest(int sample) => JsonSerializer.Serialize(new
    {
        messages = new[] { new { role = "user", content = $"Synthetic local benchmark {sample}. Return a JSON object with topic Travel and a description of planning a train journey, hotel and museum visits in about 100 words." } },
        temperature = 0, max_tokens = 64, stream = false, cache_prompt = false,
        response_format = new { type = "json_object" },
    });

    private void ResourcesChanged()
    {
        lock (_sync)
        {
            if (_disposed || _process == null) return;
            var reason = AiExecutionPolicy.CriticalReason(Execution!.Resources.Latest, _activePlan?.UsesGpu == true);
            if (reason != null)
            {
                if (_activePlan?.UsesGpu == true) Execution.BlockGpu(reason);
                else Execution.SetStatus(reason);
                // Let an interrupted GPU request retry on CPU if memory still permits it.
                StopProcessCore();
            }
            else if (_requests.CurrentCount == 1 && DateTime.UtcNow - _lastGeneration >= TimeSpan.FromMinutes(3))
            {
                StopProcessCore();
                Execution.SetStatus("Sprachmodell nach drei Minuten Leerlauf entladen.");
            }
        }
    }
}

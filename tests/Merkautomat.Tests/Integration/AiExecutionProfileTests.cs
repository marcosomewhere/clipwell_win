using System.IO;
using System.Text;
using System.Text.Json;
using Merkautomat.Services;
using Xunit;

namespace Merkautomat.Tests;

public sealed class AiExecutionProfileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "merkautomat-ai-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void BenchmarkProfilesAreEncryptedAndSeparateModels()
    {
        Directory.CreateDirectory(_root);
        var models = Path.Combine(_root, "models");
        var granite = new LocalAiModel(models);
        var qwen = new LocalAiModel(models, LocalAiModel.QwenId);
        using var monitor = new AiResourceMonitor();
        using (var manager = new AiExecutionManager(models, monitor, () => "auto"))
        {
            Assert.NotEqual(manager.Signature(granite), manager.Signature(qwen));
            manager.Store(granite, [new(manager.Signature(granite), granite.ModelId, new("cpu", 0, 4), 200, 500, true, DateTime.UtcNow)]);
            manager.Store(qwen, [new(manager.Signature(qwen), qwen.ModelId, new("cpu", 0, 2), 100, 400, true, DateTime.UtcNow)]);
        }
        var file = Path.Combine(_root, AiExecutionManager.FileName);
        Assert.Equal("MKE1", Encoding.ASCII.GetString(File.ReadAllBytes(file), 0, 4));
        Assert.Contains("granite", SecureFile.ReadAllText(file));
        using var reloaded = new AiExecutionManager(models, monitor, () => "cpu");
        Assert.Equal(2, reloaded.Measurements.Count);
        Assert.Equal("cpu", reloaded.Mode);
        Assert.False(reloaded.CanBenchmark(granite));
    }

    [Fact]
    public void InvalidCachedPlansAreIgnored()
    {
        Directory.CreateDirectory(_root);
        var measurements = new AiExecutionMeasurement?[]
        {
            null,
            new("signature", "model", null!, 100, 100, true, DateTime.UtcNow),
            new("signature", "model", new("vulkan", int.MaxValue, 4), 100, 100, true, DateTime.UtcNow),
            new("signature", "model", new("cpu", 0, 4), 100, 100, true, DateTime.UtcNow),
        };
        SecureFile.WriteAllTextAtomic(Path.Combine(_root, AiExecutionManager.FileName), JsonSerializer.Serialize(measurements));
        using var monitor = new AiResourceMonitor();
        using var manager = new AiExecutionManager(Path.Combine(_root, "models"), monitor, () => "auto");
        Assert.Single(manager.Measurements);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}

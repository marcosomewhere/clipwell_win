using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace Merkautomat.Services;

public sealed class GpuRuntime(string modelsDirectory)
{
    public const string Id = "llama-b12110-vulkan";
    public static readonly LocalAiDownload Archive = new("llama-b12110-bin-win-vulkan-x64.zip",
        "https://github.com/ggml-org/llama.cpp/releases/download/b12110/llama-b12110-bin-win-vulkan-x64.zip",
        37661146, "9732a5e48887ca54d0bfe4af4324dc85fd56cd8cda19b52df3542fa6ebe6874f");
    private readonly SemaphoreSlim _install = new(1, 1);
    public string DirectoryPath => Path.Combine(modelsDirectory, "runtimes", Id);
    public string ArchivePath => Path.Combine(DirectoryPath, Archive.Name);
    public string ServerPath => Path.Combine(DirectoryPath, "bin", "llama-server.exe");
    public bool IsInstalled
    {
        get
        {
            try { return new FileInfo(ArchivePath) is { Exists: true } file && file.Length == Archive.Bytes; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }

    public async Task InstallAsync(IProgress<LocalAiProgress>? progress, CancellationToken ct)
    {
        await _install.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            if (!await LocalAiModel.VerifyAsync(ArchivePath, Archive, ct).ConfigureAwait(false))
            {
                using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
                await LocalAiModel.DownloadAsync(client, Archive, ArchivePath, progress, ct).ConfigureAwait(false);
            }
            progress?.Report(new("GPU-Laufzeit ist bereit.", 100));
        }
        finally { _install.Release(); }
    }

    public async Task PrepareAsync(LocalAiModel model, CancellationToken ct)
    {
        if (!await LocalAiModel.VerifyAsync(model.ModelPath, model.ModelDownload, ct).ConfigureAwait(false)
            || !await LocalAiModel.VerifyAsync(ArchivePath, Archive, ct).ConfigureAwait(false))
            throw new InvalidDataException("GPU-Laufzeit oder Modelldatei fehlt bzw. ist beschädigt. Bitte prüfen / reparieren.");
        ZipFile.ExtractToDirectory(ArchivePath, Path.GetDirectoryName(ServerPath)!, overwriteFiles: true);
        ct.ThrowIfCancellationRequested();
    }
}

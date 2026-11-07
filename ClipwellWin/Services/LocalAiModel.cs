using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace ClipwellWin.Services;

public sealed record LocalAiDownload(string Name, string Url, long Bytes, string Sha256);
public sealed record LocalAiProgress(string Stage, double Percent);

public sealed class LocalAiModel
{
    public const string Id = "granite-4.2-3b";
    public const string DisplayName = "Granite 4.2 3B · lokal (CPU)";
    public static readonly LocalAiDownload Model = new("granite-4.2-3b-Q4_K_M.gguf",
        "https://huggingface.co/ibm-granite/granite-4.2-3b-GGUF/resolve/c40945d71cd90f249a56985e8155551a9188dc30/granite-4.2-3b-Q4_K_M.gguf",
        2244011552, "e0406663965846ae22a403456eb826ccce5f450840491f71952f18a7cb78e7d5");
    public static readonly LocalAiDownload Runtime = new("llama-b12110-bin-win-cpu-x64.zip",
        "https://github.com/ggml-org/llama.cpp/releases/download/b12110/llama-b12110-bin-win-cpu-x64.zip",
        24708445, "98901e327c252875accaf2d1bf364e10e868843b0b63468baad1aa945cdc5da2");
    private readonly SemaphoreSlim _install = new(1, 1);
    public string DirectoryPath { get; }
    public string ModelPath => Path.Combine(DirectoryPath, Model.Name);
    public string ServerPath => Path.Combine(DirectoryPath, "runtime-b12110", "llama-server.exe");
    public bool IsInstalled => HasSize(Model) && HasSize(Runtime);
    public LocalAiModel(string modelsDirectory) => DirectoryPath = Path.Combine(modelsDirectory, Id);
    public static string Normalize(string? value) => value is Id or E5Categorization.Id ? value : "off";
    private bool HasSize(LocalAiDownload file)
    {
        try { return new FileInfo(Path.Combine(DirectoryPath, file.Name)) is { Exists: true } info && info.Length == file.Bytes; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public async Task InstallAsync(IProgress<LocalAiProgress>? progress, CancellationToken cancellationToken)
    {
        await _install.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Environment.Is64BitProcess || !System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                throw new NotSupportedException("Granite benötigt Windows x64.");
            Directory.CreateDirectory(DirectoryPath);
            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Merkautomat/1.2");
            foreach (var file in new[] { Runtime, Model })
            {
                var destination = Path.Combine(DirectoryPath, file.Name);
                if (HasSize(file) && await VerifyAsync(destination, file, cancellationToken).ConfigureAwait(false)) continue;
                await DownloadAsync(client, file, destination, progress, cancellationToken).ConfigureAwait(false);
            }
            progress?.Report(new("Granite ist bereit.", 100));
        }
        finally { _install.Release(); }
    }

    public static async Task<bool> VerifyAsync(string path, LocalAiDownload file, CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != file.Bytes) return false;
        await using var input = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task DownloadAsync(HttpClient client, LocalAiDownload file, string destination,
        IProgress<LocalAiProgress>? progress, CancellationToken cancellationToken)
    {
        var temporary = destination + ".part";
        try
        {
            using var response = await client.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long size && size != file.Bytes)
                throw new InvalidDataException("Die Downloadgröße stimmt nicht mit der geprüften Version überein.");
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = File.Create(temporary))
            {
                var buffer = new byte[131072];
                long total = 0;
                var lastProgress = -1;
                int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
                {
                    total += count;
                    if (total > file.Bytes) throw new InvalidDataException("Der Download ist größer als erwartet.");
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    var percent = (int)(100 * total / file.Bytes);
                    if (percent != lastProgress)
                    {
                        lastProgress = percent;
                        progress?.Report(new($"{file.Name}: {total / 1_000_000} / {file.Bytes / 1_000_000} MB", percent));
                    }
                }
            }
            progress?.Report(new("SHA256 wird geprüft …", 100));
            if (!await VerifyAsync(temporary, file, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("Die SHA256-Prüfung ist fehlgeschlagen. Bitte erneut herunterladen.");
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task PrepareRuntimeAsync(CancellationToken cancellationToken)
    {
        if (!IsInstalled) throw new InvalidOperationException("Bitte Granite zuerst in der Kontrollstation herunterladen.");
        foreach (var file in new[] { Runtime, Model })
            if (!await VerifyAsync(Path.Combine(DirectoryPath, file.Name), file, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("Die lokalen KI-Dateien sind beschädigt. Bitte in der Kontrollstation erneut herunterladen.");
        // Restore executable files from the verified archive before each process start.
        ZipFile.ExtractToDirectory(Path.Combine(DirectoryPath, Runtime.Name), Path.GetDirectoryName(ServerPath)!, true);
        cancellationToken.ThrowIfCancellationRequested();
    }
}

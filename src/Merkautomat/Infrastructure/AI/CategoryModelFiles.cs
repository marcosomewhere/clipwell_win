using System.IO;
using System.Net.Http;

namespace Merkautomat.Services;

public sealed class CategoryModelFiles(string modelsDirectory, string id)
{
    private readonly SemaphoreSlim _install = new(1, 1);
    public const string MiniLmId = "minilm-l6";
    public const string HorizonId = "horizon-zeroshot-small";
    public static bool IsCategoryModel(string? id) => id is MiniLmId or HorizonId;
    public string Id { get; } = IsCategoryModel(id) ? id : throw new ArgumentOutOfRangeException(nameof(id));
    public string Name => Id == MiniLmId ? "MiniLM L6" : "Horizon ZeroShot Small";
    public string DirectoryPath => Path.Combine(modelsDirectory, Id);
    public IReadOnlyList<LocalAiDownload> Files => Id == MiniLmId ? MiniLmFiles : HorizonFiles;
    public string DownloadSize => $"{Files.Sum(f => f.Bytes) / 1_000_000.0:0.0} MB";
    public bool IsInstalled => Files.All(f =>
    {
        try { return new FileInfo(Path.Combine(DirectoryPath, f.Name)) is { Exists: true } info && info.Length == f.Bytes; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    });

    private const string MiniLmUrl = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/1110a243fdf4706b3f48f1d95db1a4f5529b4d41/";
    private const string HorizonUrl = "https://huggingface.co/Horizon-Labs/multilingual-zeroshot-small/resolve/17af5f8a728d101ce33a9bffad227b0cd005e95e/";
    private static readonly LocalAiDownload[] MiniLmFiles =
    [
        new("model.onnx", MiniLmUrl + "onnx/model_quint8_avx2.onnx", 23046789,
            "b941bf19f1f1283680f449fa6a7336bb5600bdcd5f84d10ddc5cd72218a0fd21"),
        new("vocab.txt", MiniLmUrl + "vocab.txt", 231508,
            "07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3"),
    ];
    private static readonly LocalAiDownload[] HorizonFiles =
    [
        new("model.onnx", HorizonUrl + "onnx/model_quantized.onnx", 268409234,
            "0ac394942a086fb1c02beb0fde0357bf7f3bd739ca07af7fa6c9fa11c9c05512"),
        new("tokenizer.json", HorizonUrl + "tokenizer.json", 34363287,
            "aebee76d0312011b0d73b08a255d4d11d25c83e9639324870c7b916ea102e13e"),
    ];

    public async Task InstallAsync(IProgress<LocalAiProgress>? progress, CancellationToken ct)
    {
        await _install.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Merkautomat/1.2");
            foreach (var file in Files)
            {
                var target = Path.Combine(DirectoryPath, file.Name);
                if (await LocalAiModel.VerifyAsync(target, file, ct).ConfigureAwait(false)) continue;
                await LocalAiModel.DownloadAsync(client, file, target, progress, ct).ConfigureAwait(false);
            }
            progress?.Report(new($"{Name} ist bereit.", 100));
        }
        finally { _install.Release(); }
    }

    public async Task VerifyAsync(CancellationToken ct)
    {
        foreach (var file in Files)
            if (!await LocalAiModel.VerifyAsync(Path.Combine(DirectoryPath, file.Name), file, ct).ConfigureAwait(false))
                throw new InvalidDataException($"Die Dateien für {Name} fehlen oder sind beschädigt. Bitte in der Kontrollstation herunterladen / reparieren.");
    }
}

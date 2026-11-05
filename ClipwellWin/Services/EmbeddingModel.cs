using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace ClipwellWin.Services;

public interface ITextEmbedder
{
    /// <summary>L2-normalized vector for a search query.</summary>
    float[] EmbedQuery(string query);

    /// <summary>One L2-normalized vector per stored text, in input order.</summary>
    float[][] EmbedPassages(IReadOnlyList<string> texts);
}

public sealed record EmbeddingModelFile(string Name, string Url, string Sha256, long Size);

/// <summary>
/// multilingual-e5-small (MIT) as int8 ONNX model plus its SentencePiece tokenizer, run locally with ONNX Runtime.
/// Adds the e5 prefixes <see cref="QueryPrefix"/> and <see cref="PassagePrefix"/> itself.
/// </summary>
public sealed class EmbeddingModel : ITextEmbedder, IDisposable
{
    public const string Id = "multilingual-e5-small-int8";
    public const string FolderName = "e5-small";
    public const string QueryPrefix = "query: ";
    public const string PassagePrefix = "passage: ";
    public const int ApproxSizeMb = 123;
    private const int MaxTokens = 512;
    private const int MaxInputChars = 4000;

    // Pinned revisions, so the SHA256 values stay valid.
    public static readonly EmbeddingModelFile[] Files =
    [
        new("model_quantized.onnx",
            "https://huggingface.co/Xenova/multilingual-e5-small/resolve/761b726dd34fb83930e26aab4e9ac3899aa1fa78/onnx/model_quantized.onnx",
            "f80102d3f2a1229f387d3c81909990d8945513e347b0eab049f7de3c6f98c193", 118308185),
        new("sentencepiece.bpe.model",
            "https://huggingface.co/intfloat/multilingual-e5-small/resolve/614241f622f53c4eeff9890bdc4f31cfecc418b3/sentencepiece.bpe.model",
            "cfc8146abe2a0488e9e2a0c56de7952f7c11ab059eca145a0a727afce0db2865", 5069051),
    ];

    private readonly SentencePieceTokenizer _tokenizer;
    private readonly InferenceSession _session;
    private readonly bool _needsTokenTypes;

    public EmbeddingModel(string modelsDir)
    {
        var dir = DirectoryFor(modelsDir);
        using (var stream = File.OpenRead(Path.Combine(dir, Files[1].Name)))
            _tokenizer = SentencePieceTokenizer.Create(stream, addBeginningOfSentence: false, addEndOfSentence: false);

        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
            InterOpNumThreads = 1,
        };
        _session = new InferenceSession(Path.Combine(dir, Files[0].Name), options);
        _needsTokenTypes = _session.InputMetadata.ContainsKey("token_type_ids");
    }

    public static string DirectoryFor(string modelsDir) => Path.Combine(modelsDir, FolderName);

    /// <summary>
    /// The copy shipped next to the EXE (models\e5-small), otherwise the user models folder, where a missing or
    /// damaged bundled copy can be downloaded again.
    /// </summary>
    public static string ResolveModelsDir(string appDir, string userModelsDir)
    {
        var bundled = Path.Combine(appDir, "models");
        return IsInstalled(bundled) ? bundled : userModelsDir;
    }

    public static bool IsInstalled(string modelsDir)
        => Files.All(f =>
        {
            var info = new FileInfo(Path.Combine(DirectoryFor(modelsDir), f.Name));
            return info.Exists && info.Length == f.Size;
        });

    public static async Task DownloadAsync(string modelsDir, IProgress<long>? progressBytes, CancellationToken ct)
    {
        var dir = DirectoryFor(modelsDir);
        Directory.CreateDirectory(dir);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        long done = 0;
        foreach (var file in Files)
        {
            var target = Path.Combine(dir, file.Name);
            var existing = new FileInfo(target);
            if (existing.Exists && existing.Length == file.Size)
            {
                done += file.Size;
                progressBytes?.Report(done);
                continue;
            }

            var tmp = target + ".part";
            try
            {
                using (var response = await http.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    response.EnsureSuccessStatusCode();
                    await using var source = await response.Content.ReadAsStreamAsync(ct);
                    await using var output = File.Create(tmp);
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[1 << 16];
                    int read;
                    long lastReport = 0;
                    while ((read = await source.ReadAsync(buffer, ct)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), ct);
                        hash.AppendData(buffer, 0, read);
                        done += read;
                        if (done - lastReport >= 1 << 20)
                        {
                            lastReport = done;
                            progressBytes?.Report(done);
                        }
                    }
                    var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
                    if (actual != file.Sha256)
                        throw new InvalidDataException($"{file.Name}: Prüfsumme stimmt nicht (SHA256 {actual}).");
                }
                File.Move(tmp, target, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { }
            }
        }
        progressBytes?.Report(done);
    }

    public float[] EmbedQuery(string query) => Embed([QueryPrefix + query])[0];

    public float[][] EmbedPassages(IReadOnlyList<string> texts) => Embed(texts.Select(t => PassagePrefix + t).ToList());

    private float[][] Embed(IReadOnlyList<string> texts)
    {
        if (texts.Count == 0) return [];
        var tokenized = texts.Select(Tokenize).ToList();
        var batch = tokenized.Count;
        var length = tokenized.Max(t => t.Length);

        var ids = new long[batch * length];
        var mask = new long[batch * length];
        for (var b = 0; b < batch; b++)
        {
            var row = tokenized[b];
            for (var t = 0; t < row.Length; t++)
            {
                ids[b * length + t] = row[t];
                mask[b * length + t] = 1;
            }
            for (var t = row.Length; t < length; t++) ids[b * length + t] = PadId;
        }

        int[] shape = [batch, length];
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(ids, shape)),
            NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(mask, shape)),
        };
        if (_needsTokenTypes)
            inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(new long[batch * length], shape)));

        using var results = _session.Run(inputs);
        var hidden = (DenseTensor<float>)results.First(r => r.Name == "last_hidden_state").AsTensor<float>();
        var dims = hidden.Dimensions[2];
        var data = hidden.Buffer.Span;

        // Mean pooling over real tokens, then L2 normalization (as the e5 reference implementation does).
        var vectors = new float[batch][];
        for (var b = 0; b < batch; b++)
        {
            var v = new float[dims];
            var count = tokenized[b].Length;
            for (var t = 0; t < count; t++)
            {
                var offset = (b * length + t) * dims;
                for (var k = 0; k < dims; k++) v[k] += data[offset + k];
            }
            SemanticRanking.Normalize(v);
            vectors[b] = v;
        }
        return vectors;
    }

    // XLM-R vocabulary layout: <s>=0, <pad>=1, </s>=2, <unk>=3, SentencePiece ids shifted by one (fairseq offset).
    private const long BosId = 0;
    private const long PadId = 1;
    private const long EosId = 2;
    private const long UnkId = 3;

    private long[] Tokenize(string text)
    {
        if (text.Length > MaxInputChars) text = text[..MaxInputChars];
        var pieces = _tokenizer.EncodeToIds(text, MaxTokens - 2, out _, out _);
        var result = new long[pieces.Count + 2];
        result[0] = BosId;
        for (var i = 0; i < pieces.Count; i++) result[i + 1] = pieces[i] == 0 ? UnkId : pieces[i] + 1;
        result[^1] = EosId;
        return result;
    }

    public void Dispose() => _session.Dispose();
}

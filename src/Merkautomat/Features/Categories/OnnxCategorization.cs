using System.IO;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace Merkautomat.Services;

/// <summary>Optional category-only encoders. Their vectors never enter the E5 search index.</summary>
public sealed class OnnxCategorization : IDisposable
{
    private readonly CategoryModelFiles _model;
    private readonly InferenceSession _session;
    private readonly BertTokenizer? _miniLm;
    private readonly HorizonTokenizer? _horizon;
    private const int BatchSize = 8;

    public OnnxCategorization(CategoryModelFiles model)
    {
        _model = model;
        if (model.Id == CategoryModelFiles.MiniLmId)
            _miniLm = BertTokenizer.Create(Path.Combine(model.DirectoryPath, "vocab.txt"), new BertOptions
            {
                LowerCaseBeforeTokenization = true, RemoveNonSpacingMarks = true,
            });
        else
        {
            using var tokenizer = File.OpenRead(Path.Combine(model.DirectoryPath, "tokenizer.json"));
            _horizon = new HorizonTokenizer(tokenizer);
        }
        using var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            IntraOpNumThreads = AiCpuWork.Threads, InterOpNumThreads = 1,
        };
        _session = new InferenceSession(Path.Combine(model.DirectoryPath, "model.onnx"), options);
    }

    public LocalAiSuggestion Suggest(IEnumerable<BrainSource> sources, IEnumerable<string> categories, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var texts = sources.Where(s => !string.IsNullOrWhiteSpace(s.Text)).Take(LocalAiCategorization.MaxSources)
            .Select(s => Regex.Replace(s.Text, @"\s+", " ").Trim())
            .Select(s => s[..Math.Min(s.Length, LocalAiCategorization.MaxTextPerSource)]).Distinct().ToArray();
        if (texts.Length == 0) throw new ArgumentException("Die Auswahl enthält keinen auswertbaren Text.");
        var labels = categories.Select(KnowledgeCategories.Parse).Select(p => p.ToString()).Where(s => s.Length is > 0 and <= 163)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(200).ToArray();
        if (labels.Length < 2)
            return new("", "", "Bitte mindestens zwei vorhandene Kategorien anlegen. Dieses Modell erfindet keine Kategorien oder Titel.", true, Method: _model.Id);
        double[] scores;
        bool supported;
        if (_miniLm != null)
        {
            var vectors = Embed([.. texts, .. labels], ct);
            scores = labels.Select((_, c) => texts.Select((_, t) => (double)RocchioModel.Cosine(vectors[t], vectors[texts.Length + c])).Average()).ToArray();
            supported = scores.Max() >= 0.25;
        }
        else
        {
            var premise = string.Join("\n", texts);
            var pairs = labels.Select(label => _horizon!.EncodePair(premise, $"This text is about {label}.")).ToArray();
            var logits = new List<(double No, double Yes)>();
            foreach (var batch in pairs.Chunk(BatchSize))
            {
                using var output = Run(batch, ct);
                var values = output.First(r => r.Name == "logits").AsTensor<float>();
                if (values.Rank != 2 || values.Dimensions[1] != 2)
                    throw new InvalidDataException("Das Horizon-Modell liefert ein unerwartetes Ausgabeformat.");
                for (var i = 0; i < batch.Length; i++) logits.Add((values[i, 0], values[i, 1]));
            }
            scores = Softmax(logits.Select(l => l.Yes).ToArray());
            var best = Array.IndexOf(scores, scores.Max());
            supported = logits[best].Yes > logits[best].No;
        }
        if (scores.Any(s => !double.IsFinite(s))) throw new InvalidDataException("Das Kategoriemodell liefert ungültige Werte.");
        var ranked = labels.Select((label, i) => new CategoryCandidate(label, scores[i])).OrderByDescending(c => c.Score).ToArray();
        var gap = ranked[0].Score - ranked[1].Score;
        var uncertain = !supported || gap < (_miniLm != null ? 0.04 : 0.08);
        var reason = uncertain ? $"{_model.Name}: keine eindeutige vorhandene Kategorie. Kandidaten bitte prüfen."
            : $"{_model.Name} vergleicht den Inhalt mit vorhandenen Kategorien. Titel bleibt erhalten; Vorschlag bitte prüfen.";
        return new("", uncertain ? "" : ranked[0].Category, reason, uncertain, ranked.Take(3).ToList(), _model.Id);
    }

    private float[][] Embed(IReadOnlyList<string> texts, CancellationToken ct)
    {
        var vectors = new List<float[]>();
        foreach (var batch in texts.Chunk(BatchSize))
        {
            var ids = batch.Select(t => _miniLm!.EncodeToIds(t, 256, out _, out _).ToArray()).ToArray();
            using var output = Run(ids, ct);
            var hidden = output.First(r => r.Name == "last_hidden_state").AsTensor<float>();
            for (var b = 0; b < ids.Length; b++)
            {
                var vector = new float[hidden.Dimensions[2]];
                for (var t = 0; t < ids[b].Length; t++)
                    for (var d = 0; d < vector.Length; d++) vector[d] += hidden[b, t, d];
                SemanticRanking.Normalize(vector);
                vectors.Add(vector);
            }
        }
        return vectors.ToArray();
    }

    private IDisposableReadOnlyCollection<DisposableNamedOnnxValue> Run(int[][] tokens, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var length = tokens.Max(t => t.Length);
        var ids = new long[tokens.Length * length];
        var mask = new long[ids.Length];
        for (var b = 0; b < tokens.Length; b++)
            for (var t = 0; t < tokens[b].Length; t++)
            {
                ids[b * length + t] = tokens[b][t];
                mask[b * length + t] = 1;
            }
        int[] shape = [tokens.Length, length];
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(ids, shape)),
            NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(mask, shape)),
        };
        if (_session.InputMetadata.ContainsKey("token_type_ids"))
            inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(new long[ids.Length], shape)));
        using var options = new RunOptions();
        using var cancel = ct.Register(() => options.Terminate = true);
        using var budget = AiCpuWork.Enter(ct);
        try { return _session.Run(inputs, _session.OutputMetadata.Keys.ToArray(), options); }
        catch (OnnxRuntimeException) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
    }

    private static double[] Softmax(double[] values)
    {
        var max = values.Max();
        var exps = values.Select(v => Math.Exp(v - max)).ToArray();
        var sum = exps.Sum();
        return exps.Select(v => v / sum).ToArray();
    }

    public void Dispose() => _session.Dispose();
}

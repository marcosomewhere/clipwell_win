namespace ClipwellWin.Services;

public sealed record CategoryCandidate(string Category, double Score);

/// <param name="Category">Best category (also when not confident), -1 when the classifier cannot rank anything.</param>
/// <param name="Confidence">Method-specific ranking value for risk/coverage analysis; not a probability.</param>
/// <param name="Probability">Only for probabilistic classifiers (logistic regression).</param>
public sealed record ClassifierDecision(int Category, bool Confident, double Confidence, IReadOnlyList<CategoryCandidate> Candidates,
    double? Probability = null);

public interface ICategoryClassifier
{
    ClassifierDecision Decide(float[] source);
}

/// <summary>
/// Rocchio prototypes on E5 query vectors. A category prototype is the normalized sum of its name vector and the mean
/// of its saved examples (or the example mean alone); broad categories may get up to three sub-prototypes (spherical
/// k-means) and are scored by the best one. Prototypes and inputs are compared after subtracting the mean category
/// prototype, because raw E5 cosines sit in a narrow band. Scores are similarities, not probabilities.
/// </summary>
public sealed class RocchioModel : ICategoryClassifier
{
    public const double MinimumSimilarity = 0.80;
    public const double NameMargin = 0.025;
    // Centered scores spread much wider; measured gaps: unrelated texts <= 0.042, matching texts >= 0.105.
    public const double LearnedMargin = 0.08;
    public const int MinimumExamples = 2;
    public const int MinimumLearnedCategories = 3;
    public const int MinimumSubClusterSize = 3;
    public const int MaxSubPrototypes = 3;
    /// <summary>Hard negatives per category: the most similar examples of other categories.</summary>
    public const int HardNegatives = 5;
    /// <summary>Robust variant: from this many examples on, the least similar share is dropped before averaging.</summary>
    public const int RobustMinimumExamples = 5;
    public const double RobustTrimShare = 0.2;
    private readonly double _learnedMargin;

    private readonly float[][][] _prototypes;
    private readonly float[][][] _centered;
    private readonly float[] _center;

    public IReadOnlyList<string> Labels { get; }
    public bool Learned { get; }
    public int[] SubPrototypeCounts => _prototypes.Select(p => p.Length).ToArray();

    private RocchioModel(IReadOnlyList<string> labels, float[][][] prototypes, float[] center, bool learned, double learnedMargin)
    {
        _learnedMargin = learnedMargin;
        Labels = labels;
        _prototypes = prototypes;
        _center = center;
        Learned = learned;
        _centered = learned ? prototypes.Select(ps => ps.Select(p => Normalized(Subtract(p, center))).ToArray()).ToArray() : prototypes;
    }

    /// <param name="examples">Example vectors per category; categories with fewer than two stay name-only.</param>
    /// <param name="includeNames">False: learned categories use their example mean alone (ablation of the name prior).</param>
    /// <param name="negativeWeight">Gamma for hard negatives: p = norm(mean+ - gamma * mean of the closest foreign examples); 0 disables.</param>
    /// <param name="learnedMargin">Abstention margin for learned models; null keeps <see cref="LearnedMargin"/>.</param>
    public static RocchioModel Build(IReadOnlyList<string> labels, IReadOnlyList<float[]> names, IReadOnlyList<float[][]> examples,
        bool multiPrototype, bool includeNames = true, double negativeWeight = 0, double? learnedMargin = null, bool robust = false)
    {
        var learned = labels.Count >= MinimumLearnedCategories && examples.Any(e => e.Length >= MinimumExamples);
        var single = new float[labels.Count][];
        var prototypes = new float[labels.Count][][];
        for (var c = 0; c < labels.Count; c++)
        {
            if (!learned || examples[c].Length < MinimumExamples)
            {
                single[c] = Normalized((float[])names[c].Clone());
                prototypes[c] = [single[c]];
                continue;
            }
            var name = names[c];
            float[] Prototype(float[] mean) => includeNames ? Normalized(Add(name, mean)) : (float[])mean.Clone();
            var positive = robust ? TrimmedMean(examples[c]) : Mean(examples[c]);
            if (negativeWeight > 0)
            {
                var foreign = examples.Where((_, o) => o != c).SelectMany(e => e)
                    .OrderByDescending(v => Cosine(v, positive)).Take(HardNegatives).ToArray();
                if (foreign.Length > 0)
                {
                    var negative = Mean(foreign);
                    positive = Normalized(positive.Zip(negative, (p, n) => (float)(p - negativeWeight * n)).ToArray());
                }
            }
            single[c] = Prototype(positive);
            prototypes[c] = multiPrototype ? SubClusters(examples[c]).Select(Prototype).ToArray() : [single[c]];
        }
        // The center uses one prototype per category so that categories with sub-prototypes do not pull it.
        return new RocchioModel(labels, prototypes, Mean(single), learned, learnedMargin ?? LearnedMargin);
    }

    public ClassifierDecision Decide(float[] source)
    {
        var shifted = Learned ? Normalized(Subtract(source, _center)) : source;
        var scores = new (int Category, double Score, int Prototype)[Labels.Count];
        for (var c = 0; c < Labels.Count; c++)
        {
            var best = (Score: double.NegativeInfinity, Index: 0);
            for (var j = 0; j < _centered[c].Length; j++)
            {
                var s = Cosine(shifted, _centered[c][j]);
                if (s > best.Score) best = (s, j);
            }
            scores[c] = (c, best.Score, best.Index);
        }
        var ranked = scores.OrderByDescending(s => s.Score).ToArray();
        var margin = ranked[0].Score - ranked[1].Score;
        var raw = Cosine(source, _prototypes[ranked[0].Category][ranked[0].Prototype]);
        var confident = raw >= MinimumSimilarity && margin >= (Learned ? _learnedMargin : NameMargin);
        return new ClassifierDecision(ranked[0].Category, confident, margin,
            ranked.Take(3).Select(r => new CategoryCandidate(Labels[r.Category], r.Score)).ToList());
    }

    /// <summary>Spherical k-means with deterministic farthest-point seeding; falls back to fewer clusters when one gets too small.</summary>
    public static float[][] SubClusters(float[][] examples)
    {
        var maxK = Math.Min(MaxSubPrototypes, examples.Length / MinimumSubClusterSize);
        for (var k = maxK; k >= 2; k--)
        {
            var seeds = new List<float[]> { examples[0] };
            while (seeds.Count < k)
                seeds.Add(examples.OrderBy(e => seeds.Max(s => Cosine(e, s))).First());
            var centers = seeds.Select(s => (float[])s.Clone()).ToArray();
            var assignment = new int[examples.Length];
            for (var iteration = 0; iteration < 10; iteration++)
            {
                for (var i = 0; i < examples.Length; i++)
                {
                    var best = 0;
                    for (var j = 1; j < k; j++) if (Cosine(examples[i], centers[j]) > Cosine(examples[i], centers[best])) best = j;
                    assignment[i] = best;
                }
                for (var j = 0; j < k; j++)
                {
                    var members = examples.Where((_, i) => assignment[i] == j).ToArray();
                    if (members.Length > 0) centers[j] = Mean(members);
                }
            }
            if (Enumerable.Range(0, k).All(j => assignment.Count(a => a == j) >= MinimumSubClusterSize)) return centers;
        }
        return [Mean(examples)];
    }

    /// <summary>Mean without the least typical examples (lowest cosine to the plain mean), against outliers and wrong labels.</summary>
    public static float[] TrimmedMean(float[][] examples)
    {
        var mean = Mean(examples);
        if (examples.Length < RobustMinimumExamples) return mean;
        var drop = Math.Max(1, (int)(examples.Length * RobustTrimShare));
        return Mean(examples.OrderByDescending(e => Cosine(e, mean)).Take(examples.Length - drop).ToArray());
    }

    // ---- Vector helpers ---------------------------------------------------------------------------------------

    public static float[] Mean(IReadOnlyList<float[]> vectors)
    {
        var sum = new float[vectors[0].Length];
        foreach (var v in vectors)
            for (var i = 0; i < sum.Length; i++) sum[i] += v[i];
        return Normalized(sum);
    }

    public static float[] Normalized(float[] v)
    {
        double norm = 0;
        foreach (var f in v) norm += (double)f * f;
        if (norm <= 0 || !double.IsFinite(norm)) return v;
        var scale = (float)(1 / Math.Sqrt(norm));
        for (var i = 0; i < v.Length; i++) v[i] *= scale;
        return v;
    }

    public static double Cosine(float[] a, float[] b)
    {
        double dot = 0, aa = 0, bb = 0;
        for (var i = 0; i < a.Length; i++) { dot += (double)a[i] * b[i]; aa += (double)a[i] * a[i]; bb += (double)b[i] * b[i]; }
        return aa == 0 || bb == 0 ? 0 : dot / Math.Sqrt(aa * bb);
    }

    public static float[] Add(float[] a, float[] b) => a.Zip(b, (x, y) => x + y).ToArray();
    public static float[] Subtract(float[] a, float[] b) => a.Zip(b, (x, y) => x - y).ToArray();
}

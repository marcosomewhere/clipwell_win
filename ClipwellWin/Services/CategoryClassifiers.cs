namespace ClipwellWin.Services;

/// <summary>Classifier choices for E5 suggestions, ordered from simplest to most complex.</summary>
public static class ClassifierMethods
{
    public const string Auto = "auto";
    public const string Names = "names";
    public const string Rocchio = "rocchio";
    public const string RocchioExamples = "rocchio-examples";
    public const string RocchioEuclidean = "rocchio-euclid";
    public const string RocchioRobust = "rocchio-robust";
    public const string RocchioNegative10 = "rocchio-neg-0.1";
    public const string RocchioNegative20 = "rocchio-neg-0.2";
    public const string RocchioMulti = "rocchio-multi";
    public const string Knn = "knn";
    public const string LogisticRegression = "logreg";
    public const string Granite = "granite";

    /// <summary>Benchmarked methods; the order is the complexity order used for automatic selection.</summary>
    public static readonly string[] All = [Names, Rocchio, RocchioExamples, RocchioEuclidean, RocchioRobust, RocchioNegative10, RocchioNegative20, RocchioMulti, Knn, LogisticRegression];

    /// <summary>Methods whose abstention threshold can be calibrated on the user's data (margin or probability).</summary>
    public static bool Calibratable(string id) => id is Rocchio or RocchioExamples or RocchioEuclidean or RocchioRobust or RocchioNegative10 or RocchioNegative20 or RocchioMulti or LogisticRegression;

    public static string DisplayName(string id) => id switch
    {
        Auto => "Automatisch",
        Names => "Nur Kategorienamen",
        Rocchio => "Rocchio (Name + Beispiele)",
        RocchioExamples => "Rocchio (nur Beispiele)",
        RocchioEuclidean => "Rocchio klassisch (euklidisch)",
        RocchioRobust => "Rocchio robust (Ausreißer gekürzt)",
        RocchioNegative10 => "Rocchio mit Gegenbeispielen (γ 0,1)",
        RocchioNegative20 => "Rocchio mit Gegenbeispielen (γ 0,2)",
        RocchioMulti => "Rocchio, mehrere Prototypen",
        Knn => "k-NN (3 Nachbarn)",
        LogisticRegression => "Logistische Regression",
        Granite => "Granite (Sprachmodell)",
        _ => id,
    };

    public static string Normalize(string? id) => id != null && (id == Auto || All.Contains(id)) ? id : Auto;

    /// <param name="threshold">Calibrated abstention threshold (margin for Rocchio variants, probability for logistic regression).</param>
    public static ICategoryClassifier Train(string method, IReadOnlyList<string> labels, IReadOnlyList<float[]> names, IReadOnlyList<float[][]> examples,
        double? threshold = null)
        => method switch
        {
            Names => RocchioModel.Build(labels, names, labels.Select(_ => Array.Empty<float[]>()).ToArray(), false),
            RocchioExamples => RocchioModel.Build(labels, names, examples, false, includeNames: false, learnedMargin: threshold),
            RocchioEuclidean => new EuclideanRocchio(labels, names, examples, threshold),
            RocchioRobust => RocchioModel.Build(labels, names, examples, false, learnedMargin: threshold, robust: true),
            RocchioNegative10 => RocchioModel.Build(labels, names, examples, false, negativeWeight: 0.1, learnedMargin: threshold),
            RocchioNegative20 => RocchioModel.Build(labels, names, examples, false, negativeWeight: 0.2, learnedMargin: threshold),
            RocchioMulti => RocchioModel.Build(labels, names, examples, true, learnedMargin: threshold),
            Knn => new KnnClassifier(labels, examples),
            LogisticRegression => LogisticClassifier.Train(labels, examples, threshold),
            _ => RocchioModel.Build(labels, names, examples, false, learnedMargin: threshold),
        };
}

/// <summary>
/// Textbook Rocchio: nearest unnormalized centroid by squared Euclidean distance, no name prior and no centering.
/// Categories without two examples fall back to their name vector. Kept as an ablation against the cosine variant.
/// </summary>
public sealed class EuclideanRocchio : ICategoryClassifier
{
    public const double DefaultMargin = 0.05;
    private readonly IReadOnlyList<string> _labels;
    private readonly float[][] _centroids;
    private readonly double _margin;

    public EuclideanRocchio(IReadOnlyList<string> labels, IReadOnlyList<float[]> names, IReadOnlyList<float[][]> examples, double? margin = null)
    {
        _labels = labels;
        _margin = margin ?? DefaultMargin;
        _centroids = labels.Select((_, c) =>
        {
            if (examples[c].Length < RocchioModel.MinimumExamples) return RocchioModel.Normalized((float[])names[c].Clone());
            var sum = new float[names[c].Length];
            foreach (var v in examples[c]) for (var i = 0; i < sum.Length; i++) sum[i] += v[i] / examples[c].Length;
            return sum;
        }).ToArray();
    }

    public ClassifierDecision Decide(float[] source)
    {
        var z = RocchioModel.Normalized((float[])source.Clone());
        var ranked = _centroids.Select((mu, c) => (Category: c, Distance: z.Zip(mu, (a, b) => (double)(a - b) * (a - b)).Sum()))
            .OrderBy(r => r.Distance).ToArray();
        var margin = ranked[1].Distance - ranked[0].Distance;
        return new ClassifierDecision(ranked[0].Category, margin >= _margin, margin,
            ranked.Take(3).Select(r => new CategoryCandidate(_labels[r.Category], -r.Distance)).ToList());
    }
}

/// <summary>k nearest saved examples (cosine on query vectors); a category needs two of three votes.</summary>
public sealed class KnnClassifier(IReadOnlyList<string> labels, IReadOnlyList<float[][]> examples) : ICategoryClassifier
{
    public const int Neighbors = 3;
    private readonly (int Category, float[] Vector)[] _examples = examples.SelectMany((list, c) => list.Select(v => (c, v))).ToArray();

    public ClassifierDecision Decide(float[] source)
    {
        if (_examples.Length == 0) return new ClassifierDecision(-1, false, 0, []);
        var scored = _examples.Select(e => (e.Category, Score: RocchioModel.Cosine(e.Vector, source))).OrderByDescending(e => e.Score).ToList();
        var nearest = scored.Take(Neighbors).ToList();
        var vote = nearest.GroupBy(n => n.Category).Select(g => (Category: g.Key, Votes: g.Count(), Best: g.Max(n => n.Score)))
            .OrderByDescending(g => g.Votes).ThenByDescending(g => g.Best).First();
        var candidates = scored.GroupBy(s => s.Category).Select(g => new CategoryCandidate(labels[g.Key], g.Max(s => s.Score)))
            .OrderByDescending(c => c.Score).Take(3).ToList();
        var confident = nearest.Count == Neighbors && vote.Votes >= 2 && nearest[0].Score >= RocchioModel.MinimumSimilarity;
        return new ClassifierDecision(vote.Category, confident, vote.Votes + vote.Best, candidates);
    }
}

/// <summary>
/// Multinomial logistic regression on centered, normalized E5 vectors with L2 regularization and class-balanced
/// weights, trained by full-batch gradient descent. Only categories with saved examples can be predicted.
/// </summary>
public sealed class LogisticClassifier : ICategoryClassifier
{
    public const double MinimumProbability = 0.5;
    public const double MinimumProbabilityMargin = 0.2;
    private const int Iterations = 400;
    private const double LearningRate = 4.0;
    private const double Regularization = 0.001;

    private readonly IReadOnlyList<string> _labels;
    private readonly int[] _classes;
    private readonly double[][] _weights;
    private readonly double[] _bias;
    private readonly float[] _center;
    private readonly double _minimumProbability;

    private LogisticClassifier(IReadOnlyList<string> labels, int[] classes, double[][] weights, double[] bias, float[] center, double minimumProbability)
    {
        _minimumProbability = minimumProbability;
        _labels = labels;
        _classes = classes;
        _weights = weights;
        _bias = bias;
        _center = center;
    }

    public static LogisticClassifier Train(IReadOnlyList<string> labels, IReadOnlyList<float[][]> examples, double? minimumProbability = null)
    {
        var classes = Enumerable.Range(0, labels.Count).Where(c => examples[c].Length > 0).ToArray();
        var all = examples.SelectMany(e => e).ToArray();
        var dimension = all.Length > 0 ? all[0].Length : 0;
        var center = all.Length > 0 ? RocchioModel.Mean(all) : new float[dimension];
        var x = new List<double[]>();
        var y = new List<int>();
        for (var k = 0; k < classes.Length; k++)
            foreach (var v in examples[classes[k]]) { x.Add(Feature(v, center)); y.Add(k); }
        var weights = classes.Select(_ => new double[dimension]).ToArray();
        var bias = new double[classes.Length];
        if (classes.Length >= 2)
        {
            var counts = classes.Select((_, k) => y.Count(t => t == k)).ToArray();
            var sampleWeight = y.Select(t => (double)x.Count / (classes.Length * counts[t])).ToArray();
            var total = sampleWeight.Sum();
            var gradW = classes.Select(_ => new double[dimension]).ToArray();
            var gradB = new double[classes.Length];
            for (var iteration = 0; iteration < Iterations; iteration++)
            {
                foreach (var g in gradW) Array.Clear(g);
                Array.Clear(gradB);
                for (var n = 0; n < x.Count; n++)
                {
                    var p = Softmax(weights, bias, x[n]);
                    for (var k = 0; k < classes.Length; k++)
                    {
                        var error = (p[k] - (y[n] == k ? 1 : 0)) * sampleWeight[n] / total;
                        gradB[k] += error;
                        var gk = gradW[k];
                        var xn = x[n];
                        for (var d = 0; d < dimension; d++) gk[d] += error * xn[d];
                    }
                }
                for (var k = 0; k < classes.Length; k++)
                {
                    for (var d = 0; d < dimension; d++) weights[k][d] -= LearningRate * (gradW[k][d] + Regularization * weights[k][d]);
                    bias[k] -= LearningRate * gradB[k];
                }
            }
        }
        return new LogisticClassifier(labels, classes, weights, bias, center, minimumProbability ?? MinimumProbability);
    }

    public ClassifierDecision Decide(float[] source)
    {
        if (_classes.Length < 2) return new ClassifierDecision(-1, false, 0, []);
        var p = Softmax(_weights, _bias, Feature(source, _center));
        var ranked = p.Select((value, k) => (Category: _classes[k], Probability: value)).OrderByDescending(r => r.Probability).ToArray();
        var confident = ranked[0].Probability >= _minimumProbability && ranked[0].Probability - ranked[1].Probability >= MinimumProbabilityMargin;
        return new ClassifierDecision(ranked[0].Category, confident, ranked[0].Probability,
            ranked.Take(3).Select(r => new CategoryCandidate(_labels[r.Category], r.Probability)).ToList(), ranked[0].Probability);
    }

    private static double[] Feature(float[] v, float[] center)
    {
        var shifted = RocchioModel.Normalized(RocchioModel.Subtract(v, center));
        return shifted.Select(f => (double)f).ToArray();
    }

    private static double[] Softmax(double[][] weights, double[] bias, double[] x)
    {
        var z = new double[weights.Length];
        for (var k = 0; k < weights.Length; k++)
        {
            var sum = bias[k];
            var w = weights[k];
            for (var d = 0; d < x.Length; d++) sum += w[d] * x[d];
            z[k] = sum;
        }
        var max = z.Max();
        var total = 0.0;
        for (var k = 0; k < z.Length; k++) { z[k] = Math.Exp(z[k] - max); total += z[k]; }
        for (var k = 0; k < z.Length; k++) z[k] /= total;
        return z;
    }
}

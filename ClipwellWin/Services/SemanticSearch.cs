using System.Globalization;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using ClipwellWin.Models;

namespace ClipwellWin.Services;

public enum SemanticSource { Clipboard, Note, Recording }
public enum DuplicateView { Groups, AllDuplicates, OnePerGroup }

/// <summary>
/// One embedded piece of text. <see cref="ItemKey"/> groups the chunks of one hit: a clipboard entry, a KB note or
/// a single recording snapshot.
/// </summary>
public sealed record SemanticChunk(
    string Key,
    string ItemKey,
    SemanticSource Source,
    string Title,
    DateTime Date,
    string Text,
    long EntryId = 0,
    string? FilePath = null,
    string? Domain = null)
{
    /// <summary>Host without "www.", or null for anything that is not an absolute http(s) URL.</summary>
    public static string? DomainOf(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        var host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    public static string SourceName(SemanticSource source) => source switch
    {
        SemanticSource.Clipboard => "Zwischenablage",
        SemanticSource.Note => "Sprachnotiz",
        _ => "Bildschirmaufnahme",
    };
}

public sealed record IndexedChunk(SemanticChunk Chunk, float[] Vector);

public sealed class SemanticHit
{
    public required SemanticChunk Chunk { get; init; }
    public required float Similarity { get; init; }
    public bool LexicalMatch { get; init; }
    public double Score { get; init; }
    /// <summary>Nearly identical hits folded into this one, newest first.</summary>
    public List<SemanticChunk> Duplicates { get; } = [];
}

/// <summary>Turns clipboard entries, KB notes and recordings into chunks of embeddable size.</summary>
public static partial class SemanticChunker
{
    public const int MaxChunkChars = 1000;

    // Desktop recordings in content-only mode start with the page address.
    [GeneratedRegex(@"(?m)^URL: (\S+)")]
    private static partial Regex UrlLineRegex();

    private const int MinChars = 12;
    private const int MaxClipboardChunks = 8;
    private const int MaxNoteChunks = 32;
    private const int MaxSnapshotChunks = 8;

    public static IEnumerable<SemanticChunk> FromClipboard(IEnumerable<ClipboardEntry> entries)
    {
        foreach (var e in entries)
        {
            var text = e.Type switch
            {
                EntryType.Text or EntryType.Code => e.Content,
                EntryType.Url => string.IsNullOrWhiteSpace(e.UrlTitle) ? e.Content : $"{e.UrlTitle}\n{e.Content}",
                EntryType.Image => e.OcrText,
                _ => null,
            };
            text = text?.Trim();
            if (text == null || !IsWorthIndexing(text, e.Type == EntryType.Url)) continue;

            var itemKey = $"c:{e.Id}";
            var domain = SemanticChunk.DomainOf(e.Type == EntryType.Url ? e.Content : e.SourceUrl);
            var parts = Split(text, MaxChunkChars).Take(MaxClipboardChunks).ToList();
            for (var i = 0; i < parts.Count; i++)
                yield return new SemanticChunk($"{itemKey}:{i}", itemKey, SemanticSource.Clipboard,
                    TitleOf(e.Type == EntryType.Url && !string.IsNullOrWhiteSpace(e.UrlTitle) ? e.UrlTitle! : text),
                    e.Timestamp, parts[i], EntryId: e.Id, Domain: domain);
        }
    }

    public static IEnumerable<SemanticChunk> FromNote(string path, string markdown, string title, DateTime date)
    {
        var body = NoteStore.KbBody(markdown);
        if (!IsWorthIndexing(body, allowSingleWord: true)) yield break;
        var itemKey = "n:" + Path.GetFileName(path);
        var parts = Split(body, MaxChunkChars).Take(MaxNoteChunks).ToList();
        for (var i = 0; i < parts.Count; i++)
            yield return new SemanticChunk($"{itemKey}:{i}", itemKey, SemanticSource.Note, title, date, parts[i], FilePath: path);
    }

    public static IEnumerable<SemanticChunk> FromRecording(string path, string markdown)
    {
        var name = Path.GetFileName(path);
        var snapshots = RecordingMarkdown.SplitSnapshots(markdown);
        for (var s = 0; s < snapshots.Count; s++)
        {
            var snap = snapshots[s];
            if (!IsWorthIndexing(snap.Body, allowSingleWord: true)) continue;
            var title = $"{snap.App}: {snap.Title}";
            var itemKey = $"r:{name}#{s}";
            var domain = SemanticChunk.DomainOf(UrlLineRegex().Match(snap.Body) is { Success: true } m ? m.Groups[1].Value : null);
            var parts = Split(snap.Body, MaxChunkChars).Take(MaxSnapshotChunks).ToList();
            for (var i = 0; i < parts.Count; i++)
                yield return new SemanticChunk($"{itemKey}:{i}", itemKey, SemanticSource.Recording, title, snap.Time,
                    $"{title}\n{parts[i]}", FilePath: path, Domain: domain);
        }
    }

    /// <summary>
    /// Too short or a single token without spaces (identifiers, codes, passwords) is left out: semantic search
    /// adds nothing there, and secrets should not end up in the index.
    /// </summary>
    public static bool IsWorthIndexing(string text, bool allowSingleWord)
    {
        if (text.Length < MinChars) return false;
        if (allowSingleWord) return true;
        return text.AsSpan().IndexOfAny(" \t\n") >= 0 || text.Length >= 80;
    }

    /// <summary>Splits at paragraph and line boundaries; overlong lines are cut at the last space.</summary>
    public static List<string> Split(string text, int maxChars)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
            {
                if (current.Length > 0 && current[^1] != '\n') current.Append('\n');
                continue;
            }
            while (line.Length > maxChars)
            {
                var cut = line.LastIndexOf(' ', maxChars - 1);
                if (cut < maxChars / 2) cut = maxChars;
                Add(line[..cut]);
                line = line[cut..].TrimStart();
            }
            Add(line);
        }
        Flush();
        return result;

        void Add(string line)
        {
            if (current.Length > 0 && current.Length + 1 + line.Length > maxChars) Flush();
            if (current.Length > 0 && current[^1] != '\n') current.Append('\n');
            current.Append(line);
        }

        void Flush()
        {
            var chunk = current.ToString().Trim();
            if (chunk.Length > 0) result.Add(chunk);
            current.Clear();
        }
    }

    public static string TitleOf(string text)
    {
        var line = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        return line.Length > 90 ? line[..90] + "…" : line;
    }
}

/// <summary>Hybrid ranking: cosine similarity plus word matches, fused by reciprocal rank.</summary>
public static class SemanticRanking
{
    /// <summary>e5 scores sit in a narrow band; unrelated texts still reach about 0.75.</summary>
    public const float MinSemanticSimilarity = 0.80f;
    public const float MinSimilarSimilarity = 0.83f;
    public const float DuplicateSimilarity = 0.97f;
    private const int RrfK = 60;
    private const int CandidatePool = 300;
    public const int MaxDuplicateItems = 6000;

    public static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var sum = Vector<float>.Zero;
        var i = 0;
        if (Vector.IsHardwareAccelerated && a.Length >= Vector<float>.Count)
        {
            var va = MemoryMarshal.Cast<float, Vector<float>>(a);
            var vb = MemoryMarshal.Cast<float, Vector<float>>(b);
            for (var k = 0; k < va.Length; k++) sum += va[k] * vb[k];
            i = va.Length * Vector<float>.Count;
        }
        var result = Vector.Sum(sum);
        for (; i < a.Length; i++) result += a[i] * b[i];
        return result;
    }

    public static void Normalize(float[] v)
    {
        var norm = MathF.Sqrt(Dot(v, v));
        if (norm <= 0) return;
        for (var i = 0; i < v.Length; i++) v[i] /= norm;
    }

    // Function words occur in nearly every German or English text and would turn everything into a word match.
    private static readonly HashSet<string> StopWords =
    [
        "der", "die", "das", "den", "dem", "des", "ein", "eine", "einen", "einem", "einer", "eines", "und", "oder",
        "aber", "ist", "sind", "war", "wird", "werden", "hat", "habe", "haben", "hatte", "ich", "du", "er", "sie",
        "es", "wir", "ihr", "mich", "mir", "sich", "wie", "was", "wo", "wann", "warum", "wer", "mit", "von", "zu",
        "zum", "zur", "im", "in", "an", "am", "auf", "aus", "bei", "für", "um", "über", "nach", "vor", "auch",
        "nicht", "noch", "nur", "so", "da", "dass", "als", "ob", "man", "mein", "meine", "kann", "soll", "muss",
        "the", "and", "or", "of", "to", "on", "at", "for", "with", "is", "are", "was", "be", "it",
        "this", "that", "how", "what", "do", "did", "does", "my", "you", "we", "can",
    ];

    /// <summary>Lower-case words of at least two characters, without function words.</summary>
    public static List<string> Terms(string query)
    {
        var terms = new List<string>();
        var sb = new StringBuilder();
        foreach (var ch in query + " ")
        {
            if (char.IsLetterOrDigit(ch)) { sb.Append(char.ToLower(ch, CultureInfo.InvariantCulture)); continue; }
            if (sb.Length >= 2 && !StopWords.Contains(sb.ToString())) terms.Add(sb.ToString());
            sb.Clear();
        }
        return terms.Distinct().ToList();
    }

    /// <summary>Terms up to this length only match at a word start ("art" not in "Wart"); longer terms also match inside compounds ("rechnung" in "Stromrechnung").</summary>
    public const int WordStartMaxLength = 3;

    /// <summary>Occurrences of a term in the text, at most five.</summary>
    public static int Occurrences(string term, string text, int max = 5)
    {
        var count = 0;
        var at = 0;
        while (count < max && (at = text.IndexOf(term, at, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            if (term.Length > WordStartMaxLength || at == 0 || !char.IsLetterOrDigit(text[at - 1])) count++;
            at += term.Length;
        }
        return count;
    }

    /// <summary>
    /// Distinct terms found, each weighted by its inverse document frequency (rare words count more than words that
    /// occur nearly everywhere), plus a small bonus for repetitions; 0 when no term occurs. Also the distinct count.
    /// </summary>
    private static (double Score, int Distinct) Lexical(IReadOnlyList<string> terms, IReadOnlyList<double> weights, string text)
    {
        double score = 0;
        var distinct = 0;
        for (var t = 0; t < terms.Count; t++)
        {
            var count = Occurrences(terms[t], text);
            if (count == 0) continue;
            distinct++;
            score += weights[t] * (1 + 0.1 * (count - 1));
        }
        return (score, distinct);
    }

    /// <summary>Smoothed IDF per term over the chunks: ln(1 + N / df).</summary>
    private static double[] TermWeights(IReadOnlyList<string> terms, IReadOnlyList<IndexedChunk> chunks)
        => terms.Select(term =>
        {
            var df = 0;
            foreach (var c in chunks) if (Occurrences(term, c.Chunk.Text, max: 1) > 0) df++;
            return Math.Log(1 + (double)chunks.Count / Math.Max(1, df));
        }).ToArray();

    public static List<SemanticHit> Search(float[] queryVector, string query, IReadOnlyList<IndexedChunk> chunks, int limit)
    {
        var sims = new float[chunks.Count];
        for (var i = 0; i < chunks.Count; i++) sims[i] = Dot(queryVector, chunks[i].Vector);

        var fused = new Dictionary<int, double>();
        var semanticOrder = Enumerable.Range(0, chunks.Count)
            .Where(i => sims[i] >= MinSemanticSimilarity)
            .OrderByDescending(i => sims[i])
            .Take(CandidatePool)
            .ToList();
        for (var r = 0; r < semanticOrder.Count; r++) fused[semanticOrder[r]] = 1.0 / (RrfK + r + 1);

        var terms = Terms(query);
        var weights = TermWeights(terms, chunks);
        // Without semantic support a hit has to contain most query words, not just one of them.
        var requiredWords = terms.Count <= 2 ? 1 : (terms.Count + 1) / 2;
        var lexical = new HashSet<int>();
        var lexicalOrder = Enumerable.Range(0, chunks.Count)
            .Select(i => (Index: i, Match: Lexical(terms, weights, chunks[i].Chunk.Text)))
            .Where(x => x.Match.Score > 0 && (sims[x.Index] >= MinSemanticSimilarity || x.Match.Distinct >= requiredWords))
            .Select(x => (x.Index, x.Match.Score))
            .OrderByDescending(x => x.Score).ThenByDescending(x => sims[x.Index])
            .Take(CandidatePool)
            .ToList();
        for (var r = 0; r < lexicalOrder.Count; r++)
        {
            var i = lexicalOrder[r].Index;
            lexical.Add(i);
            fused[i] = fused.GetValueOrDefault(i) + 1.0 / (RrfK + r + 1);
        }

        var hits = fused
            .Select(kv => (Index: kv.Key, Score: kv.Value))
            .GroupBy(x => chunks[x.Index].Chunk.ItemKey)
            .Select(g => g.OrderByDescending(x => x.Score).First())
            .OrderByDescending(x => x.Score).ThenByDescending(x => sims[x.Index])
            .Select(x => (Hit: new SemanticHit
            {
                Chunk = chunks[x.Index].Chunk,
                Similarity = sims[x.Index],
                LexicalMatch = lexical.Contains(x.Index),
                Score = x.Score,
            }, chunks[x.Index].Vector));
        return Collapse(hits, limit);
    }

    public static List<SemanticHit> Similar(IndexedChunk source, IReadOnlyList<IndexedChunk> chunks, int limit)
    {
        var hits = chunks
            .Where(c => c.Chunk.ItemKey != source.Chunk.ItemKey)
            .Select(c => (Chunk: c, Sim: Dot(source.Vector, c.Vector)))
            .Where(x => x.Sim >= MinSimilarSimilarity)
            .GroupBy(x => x.Chunk.Chunk.ItemKey)
            .Select(g => g.OrderByDescending(x => x.Sim).First())
            .OrderByDescending(x => x.Sim)
            .Select(x => (Hit: new SemanticHit { Chunk = x.Chunk.Chunk, Similarity = x.Sim, Score = x.Sim }, x.Chunk.Vector));
        return Collapse(hits, limit);
    }

    private static readonly Regex UrlPattern = new(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Query parameters that only track where a link was clicked; they do not change the page.</summary>
    private static bool IsTrackingParameter(string name)
        => name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
           || name is "fbclid" or "gclid" or "dclid" or "msclkid" or "mc_cid" or "mc_eid" or "igshid" or "yclid" or "_hsenc" or "_hsmi";

    /// <summary>
    /// Text key for "gleicher Text": unified whitespace, and links in a canonical form (lower-case scheme and host,
    /// no default port, no tracking parameters, no trailing slash), so the same page copied from a newsletter and from
    /// the address bar counts as the same text. Fragments stay, because single-page apps route with them.
    /// </summary>
    public static string DuplicateKey(string text)
        => Whitespace.Replace(UrlPattern.Replace(text, m => CanonicalUrl(m.Value)), " ").Trim();

    public static string CanonicalUrl(string url)
    {
        var trimmed = url.TrimEnd('.', ',', ';', ':', ')', ']', '!', '?');
        var tail = url[trimmed.Length..];
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return url;
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !IsTrackingParameter(Uri.UnescapeDataString(p.Split('=')[0]))).ToList();
        var path = uri.AbsolutePath.Length > 1 ? uri.AbsolutePath.TrimEnd('/') : "";
        var port = uri.IsDefaultPort ? "" : ":" + uri.Port.ToString(CultureInfo.InvariantCulture);
        return $"{uri.Scheme}://{uri.Host.ToLowerInvariant()}{port}{path}"
               + (query.Count > 0 ? "?" + string.Join("&", query) : "") + uri.Fragment + tail;
    }

    /// <summary>Classic Rocchio weights for the query and the marked (relevant) items.</summary>
    public const float RocchioAlpha = 1f;
    public const float RocchioBeta = 0.75f;

    /// <summary>
    /// Rocchio relevance feedback: q' = norm(alpha * q + beta * mean(marked)); without a query the normalized mean of the
    /// marked items. Each marked item counts once (mean of its chunks), so long items do not outweigh short ones.
    /// </summary>
    public static float[]? RocchioQuery(float[]? queryVector, IReadOnlyList<float[]> markedItems)
    {
        if (markedItems.Count == 0) return queryVector == null ? null : (float[])queryVector.Clone();
        var dimension = markedItems[0].Length;
        var mean = new float[dimension];
        foreach (var v in markedItems)
            for (var d = 0; d < dimension && d < v.Length; d++) mean[d] += v[d];
        Normalize(mean);
        if (queryVector == null) return mean;
        var q = new float[dimension];
        for (var d = 0; d < dimension && d < queryVector.Length; d++) q[d] = RocchioAlpha * queryVector[d] + RocchioBeta * mean[d];
        Normalize(q);
        return q;
    }

    /// <summary>Mean vector per item over its chunks, normalized.</summary>
    public static Dictionary<string, float[]> ItemVectors(IReadOnlyList<IndexedChunk> chunks, IReadOnlySet<string> itemKeys)
    {
        var result = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (var group in chunks.Where(c => itemKeys.Contains(c.Chunk.ItemKey)).GroupBy(c => c.Chunk.ItemKey))
        {
            var sum = new float[group.First().Vector.Length];
            foreach (var c in group)
                for (var d = 0; d < sum.Length && d < c.Vector.Length; d++) sum[d] += c.Vector[d];
            Normalize(sum);
            result[group.Key] = sum;
        }
        return result;
    }

    /// <summary>
    /// "Ähnliche zu Markierten": ranks by the Rocchio vector of the marked items, together with the query when one is
    /// entered (then the word matches of the hybrid search still count). The marked items themselves stay in the list,
    /// so marking more and asking again refines the result step by step.
    /// </summary>
    public static List<SemanticHit> SimilarToMarked(IReadOnlyList<IndexedChunk> all, IReadOnlyList<IndexedChunk> chunks,
        IReadOnlyCollection<string> markedKeys, float[]? queryVector, string? query, int limit)
    {
        var vectors = ItemVectors(all, markedKeys.ToHashSet(StringComparer.Ordinal));
        var target = RocchioQuery(queryVector, vectors.Values.ToList());
        if (target == null || chunks.Count == 0) return [];
        if (queryVector != null && !string.IsNullOrWhiteSpace(query)) return Search(target, query, chunks, limit);
        var hits = chunks
            .Select(c => (Chunk: c, Sim: Dot(target, c.Vector)))
            .Where(x => x.Sim >= MinSimilarSimilarity || vectors.ContainsKey(x.Chunk.Chunk.ItemKey))
            .GroupBy(x => x.Chunk.Chunk.ItemKey)
            .Select(g => g.OrderByDescending(x => x.Sim).First())
            .OrderByDescending(x => x.Sim)
            .Select(x => (Hit: new SemanticHit { Chunk = x.Chunk.Chunk, Similarity = x.Sim, Score = x.Sim }, x.Chunk.Vector));
        return Collapse(hits, limit + vectors.Count);
    }

    /// <summary>
    /// Groups of nearly identical items (first chunk per item), largest groups first. Quadratic in the worst case,
    /// so only the newest <see cref="MaxDuplicateItems"/> items take part.
    /// </summary>
    public static List<SemanticHit> DuplicateGroups(IReadOnlyList<IndexedChunk> chunks, float threshold = DuplicateSimilarity, bool exactOnly = false)
    {
        if (!float.IsFinite(threshold) || threshold < 0.9f || threshold > 1f) throw new ArgumentOutOfRangeException(nameof(threshold));
        var items = chunks
            .GroupBy(c => c.Chunk.ItemKey)
            .Select(g =>
            {
                var parts = g.OrderBy(c => int.TryParse(c.Chunk.Key.AsSpan(c.Chunk.Key.LastIndexOf(':') + 1), out var ordinal) ? ordinal : 0).ToList();
                var vector = new float[parts[0].Vector.Length];
                foreach (var part in parts)
                    for (var d = 0; d < vector.Length && d < part.Vector.Length; d++) vector[d] += part.Vector[d];
                Normalize(vector);
                return new IndexedChunk(parts[0].Chunk with { Text = string.Join("\n", parts.Select(c => c.Chunk.Text)) }, vector);
            })
            .OrderByDescending(c => c.Chunk.Date)
            .Take(MaxDuplicateItems)
            .ToList();
        var groups = new List<(IndexedChunk Representative, List<SemanticChunk> Members)>();
        var exact = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var match = -1;
            var textKey = DuplicateKey(item.Chunk.Text);
            if (textKey.Length > 0 && exact.TryGetValue(textKey, out var same)) match = same;
            for (var g = 0; !exactOnly && match < 0 && g < groups.Count; g++)
            {
                if (Dot(groups[g].Representative.Vector, item.Vector) >= threshold) { match = g; break; }
            }
            if (match >= 0) groups[match].Members.Add(item.Chunk);
            else { match = groups.Count; groups.Add((item, [])); }
            if (textKey.Length > 0) exact.TryAdd(textKey, match);
        }

        return groups
            .Where(g => g.Members.Count > 0)
            .OrderByDescending(g => g.Members.Count).ThenByDescending(g => g.Representative.Chunk.Date)
            .Select(g =>
            {
                var hit = new SemanticHit { Chunk = g.Representative.Chunk, Similarity = 1, Score = g.Members.Count };
                hit.Duplicates.AddRange(g.Members);
                return hit;
            })
            .ToList();
    }

    public static List<SemanticHit> FilterDuplicates(IReadOnlyList<IndexedChunk> chunks, DuplicateView view,
        float threshold = DuplicateSimilarity, bool exactOnly = false, string? query = null)
    {
        var groups = DuplicateGroups(chunks, threshold, exactOnly);
        var words = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        bool Matches(SemanticChunk c) => words.All(word => (c.Title + "\n" + c.Text).Contains(word, StringComparison.OrdinalIgnoreCase));
        if (view == DuplicateView.Groups)
            return groups.Where(g => Matches(g.Chunk) || g.Duplicates.Any(Matches)).ToList();
        if (view == DuplicateView.AllDuplicates)
            return groups.SelectMany(g => new[] { g.Chunk }.Concat(g.Duplicates))
                .Where(Matches).OrderByDescending(c => c.Date).Select(c => new SemanticHit { Chunk = c, Similarity = 1 }).ToList();
        var redundant = groups.SelectMany(g => g.Duplicates).Select(c => c.ItemKey).ToHashSet();
        var merged = chunks.GroupBy(c => c.Chunk.ItemKey).OrderByDescending(g => g.First().Chunk.Date).Take(MaxDuplicateItems)
            .Select(g => g.First().Chunk with { Text = string.Join("\n", g.Select(c => c.Chunk.Text)) });
        return merged.Where(c => !redundant.Contains(c.ItemKey) && Matches(c)).OrderByDescending(c => c.Date)
            .Select(c => new SemanticHit { Chunk = c, Similarity = 1 }).ToList();
    }

    /// <summary>Every indexed item once (its first chunk), newest first; used when no query is entered.</summary>
    public static List<SemanticHit> Browse(IReadOnlyList<IndexedChunk> chunks)
        => chunks
            .GroupBy(c => c.Chunk.ItemKey)
            .Select(g => g.First().Chunk)
            .OrderByDescending(c => c.Date)
            .Select(c => new SemanticHit { Chunk = c, Similarity = 1 })
            .ToList();

    /// <summary>Folds hits into an earlier, better ranked hit when their vectors are nearly identical.</summary>
    private static List<SemanticHit> Collapse(IEnumerable<(SemanticHit Hit, float[] Vector)> ranked, int limit)
    {
        var kept = new List<(SemanticHit Hit, float[] Vector)>();
        foreach (var (hit, vector) in ranked)
        {
            var twin = kept.FindIndex(k => Dot(k.Vector, vector) >= DuplicateSimilarity);
            if (twin >= 0)
            {
                kept[twin].Hit.Duplicates.Add(hit.Chunk);
                continue;
            }
            if (kept.Count >= limit) continue;
            kept.Add((hit, vector));
        }
        foreach (var (hit, _) in kept) hit.Duplicates.Sort((a, b) => b.Date.CompareTo(a.Date));
        return kept.Select(k => k.Hit).ToList();
    }
}

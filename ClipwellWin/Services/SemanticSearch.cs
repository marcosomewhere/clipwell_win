using System.Globalization;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using ClipwellWin.Models;

namespace ClipwellWin.Services;

public enum SemanticSource { Clipboard, Note, Recording }

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

    /// <summary>Distinct terms found plus a small bonus for repetitions (0 when no term occurs), and the distinct count.</summary>
    private static (double Score, int Distinct) Lexical(IReadOnlyList<string> terms, string text)
    {
        double score = 0;
        var distinct = 0;
        foreach (var term in terms)
        {
            var count = 0;
            var at = 0;
            while (count < 5 && (at = text.IndexOf(term, at, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                count++;
                at += term.Length;
            }
            if (count == 0) continue;
            distinct++;
            score += 1 + 0.1 * (count - 1);
        }
        return (score, distinct);
    }

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
        // Without semantic support a hit has to contain most query words, not just one of them.
        var requiredWords = terms.Count <= 2 ? 1 : (terms.Count + 1) / 2;
        var lexical = new HashSet<int>();
        var lexicalOrder = Enumerable.Range(0, chunks.Count)
            .Select(i => (Index: i, Match: Lexical(terms, chunks[i].Chunk.Text)))
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

    /// <summary>
    /// Groups of nearly identical items (first chunk per item), largest groups first. Quadratic in the worst case,
    /// so only the newest <see cref="MaxDuplicateItems"/> items take part.
    /// </summary>
    public static List<SemanticHit> DuplicateGroups(IReadOnlyList<IndexedChunk> chunks)
    {
        var items = chunks
            .GroupBy(c => c.Chunk.ItemKey)
            .Select(g => g.First())
            .OrderByDescending(c => c.Chunk.Date)
            .Take(MaxDuplicateItems)
            .ToList();
        var groups = new List<(IndexedChunk Representative, List<SemanticChunk> Members)>();
        foreach (var item in items)
        {
            var match = -1;
            for (var g = 0; g < groups.Count; g++)
            {
                if (Dot(groups[g].Representative.Vector, item.Vector) >= DuplicateSimilarity) { match = g; break; }
            }
            if (match >= 0) groups[match].Members.Add(item.Chunk);
            else groups.Add((item, []));
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

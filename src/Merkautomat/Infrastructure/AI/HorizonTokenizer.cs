using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Merkautomat.Services;

/// <summary>The pinned Horizon tokenizer uses Metaspace BPE with UTF-8 byte fallback.</summary>
public sealed class HorizonTokenizer
{
    private readonly Dictionary<string, int> _vocabulary;
    private readonly Dictionary<(int Left, int Right), (int Rank, int Token)> _merges = [];
    private readonly Dictionary<string, (int Id, bool LeftStrip, bool RightStrip)> _added;
    private readonly Regex? _addedPattern;
    private static readonly Regex Pieces = new("▁[^▁]*|[^▁]+", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public HorizonTokenizer(Stream tokenizer)
    {
        using var document = JsonDocument.Parse(tokenizer);
        var root = document.RootElement;
        var model = root.GetProperty("model");
        if (model.GetProperty("type").GetString() != "BPE" || !model.GetProperty("byte_fallback").GetBoolean())
            throw new InvalidDataException("Der Horizon-Tokenizer hat ein unerwartetes Format.");
        _vocabulary = model.GetProperty("vocab").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32(), StringComparer.Ordinal);
        var rank = 0;
        foreach (var pair in model.GetProperty("merges").EnumerateArray())
        {
            var left = pair[0].GetString()!;
            var right = pair[1].GetString()!;
            _merges.TryAdd((_vocabulary[left], _vocabulary[right]), (rank++, _vocabulary[left + right]));
        }
        _added = root.GetProperty("added_tokens").EnumerateArray().ToDictionary(
            t => t.GetProperty("content").GetString()!,
            t => (t.GetProperty("id").GetInt32(), t.GetProperty("lstrip").GetBoolean(), t.GetProperty("rstrip").GetBoolean()), StringComparer.Ordinal);
        if (_added.Count > 0)
            _addedPattern = new Regex(string.Join('|', _added.Keys.OrderByDescending(s => s.Length).Select(Regex.Escape)),
                RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1));
    }

    public int[] Encode(string text, int maxTokens)
    {
        if (maxTokens < 1) throw new ArgumentOutOfRangeException(nameof(maxTokens));
        var tokens = new List<int>();
        var start = 0;
        if (_addedPattern != null)
        {
            foreach (Match match in _addedPattern.Matches(text))
            {
                var added = _added[match.Value];
                var end = match.Index;
                if (added.LeftStrip) while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
                EncodeText(text[start..end], tokens, maxTokens);
                if (tokens.Count >= maxTokens) return tokens.ToArray();
                tokens.Add(added.Id);
                start = match.Index + match.Length;
                if (added.RightStrip) while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
                if (tokens.Count >= maxTokens) return tokens.ToArray();
            }
        }
        EncodeText(text[start..], tokens, maxTokens);
        return tokens.ToArray();
    }

    private void EncodeText(string text, List<int> tokens, int maxTokens)
    {
        if (text.Length == 0) return;
        var normalized = text.Replace(' ', '▁');
        if (normalized[0] != '▁') normalized = "▁" + normalized;
        foreach (Match piece in Pieces.Matches(normalized))
        {
            var ids = new List<int>();
            foreach (var rune in piece.Value.EnumerateRunes())
            {
                if (_vocabulary.TryGetValue(rune.ToString(), out var id)) ids.Add(id);
                else
                    foreach (var value in Encoding.UTF8.GetBytes(rune.ToString()))
                        ids.Add(_vocabulary[$"<0x{value:X2}>"]);
            }
            Merge(ids, tokens, maxTokens);
            if (tokens.Count >= maxTokens) break;
        }
    }

    private void Merge(List<int> ids, List<int> tokens, int maxTokens)
    {
        if (ids.Count == 0) return;
        var next = Enumerable.Range(1, ids.Count).ToArray();
        next[^1] = -1;
        var previous = Enumerable.Range(-1, ids.Count).ToArray();
        var versions = new int[ids.Count];
        var queue = new PriorityQueue<(int Left, int Right, int LeftVersion, int RightVersion), (int Rank, int Position)>();
        void Enqueue(int left)
        {
            if (left < 0 || next[left] < 0) return;
            var right = next[left];
            if (_merges.TryGetValue((ids[left], ids[right]), out var rule))
                queue.Enqueue((left, right, versions[left], versions[right]), (rule.Rank, left));
        }
        for (var i = 0; i < ids.Count - 1; i++) Enqueue(i);
        while (queue.TryDequeue(out var pair, out _))
        {
            var (left, right, lv, rv) = pair;
            if (next[left] != right || versions[left] != lv || versions[right] != rv) continue;
            ids[left] = _merges[(ids[left], ids[right])].Token;
            versions[left]++;
            versions[right]++;
            next[left] = next[right];
            if (next[right] >= 0) previous[next[right]] = left;
            next[right] = -1;
            Enqueue(previous[left]);
            Enqueue(left);
        }
        for (var i = 0; i >= 0 && tokens.Count < maxTokens; i = next[i]) tokens.Add(ids[i]);
    }

    public int[] EncodePair(string text, string hypothesis)
    {
        var second = Encode(hypothesis, 128);
        var first = Encode(text, 1024 - second.Length - 3);
        return [2, .. first, 1, .. second, 1];
    }
}

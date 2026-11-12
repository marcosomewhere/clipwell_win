using System.IO;
using System.Text.Json;
using Merkautomat.Services;
using Xunit;

namespace Merkautomat.Tests;

public sealed class HorizonTokenizerTests
{
    private static HorizonTokenizer Create()
    {
        var vocabulary = new Dictionary<string, int>
        {
            ["▁"] = 10, ["a"] = 11, ["b"] = 12, ["ab"] = 13, ["▁ab"] = 14,
            ["<mask>"] = 4,
        };
        for (var b = 0; b < 256; b++) vocabulary[$"<0x{b:X2}>"] = 1000 + b;
        using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new
        {
            model = new { type = "BPE", byte_fallback = true, vocab = vocabulary, merges = new[] { new[] { "a", "b" }, new[] { "▁", "ab" } } },
            added_tokens = new[] { new { content = "<mask>", id = 4, lstrip = true, rstrip = false } },
        }));
        return new(stream);
    }

    [Fact]
    public void MergesRespectWordBoundariesAndUnknownUnicodeUsesUtf8Bytes()
    {
        var tokenizer = Create();
        Assert.Equal([14, 14], tokenizer.Encode("ab ab", 10));
        Assert.Equal([14, 13], tokenizer.Encode("abab", 10));
        Assert.Equal([10, 1240, 1159, 1152, 1128], tokenizer.Encode("\U0001F600", 10));
        Assert.Empty(tokenizer.Encode("", 10));
    }

    [Fact]
    public void AddedTokensRemainAtomicAndStripTheSpecifiedWhitespace()
    {
        var tokenizer = Create();
        Assert.Equal([14, 4, 14], tokenizer.Encode("ab   <mask>ab", 10));
        Assert.Equal([14], tokenizer.Encode("ab ab", 1));
    }

    [Fact]
    public void PairTruncationKeepsTheWholeHypothesisAndAllBoundaryTokens()
    {
        var tokenizer = Create();
        var pair = tokenizer.EncodePair(string.Concat(Enumerable.Repeat("a ", 2000)), "ab");
        Assert.Equal(1024, pair.Length);
        Assert.Equal(2, pair[0]);
        Assert.Equal([1, 14, 1], pair[^3..]);
        Assert.Throws<ArgumentOutOfRangeException>(() => tokenizer.Encode("ab", 0));
    }
}

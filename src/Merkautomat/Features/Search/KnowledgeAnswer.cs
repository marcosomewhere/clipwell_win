using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Merkautomat.Services;

/// <summary>Numbered passage given to the model; <see cref="Number"/> is what the answer cites as [n].</summary>
public sealed record AnswerSource(int Number, SemanticChunk Chunk, string Text);

/// <param name="Found">False when the sources do not answer the question; the answer then says so.</param>
/// <param name="Cited">Source numbers the answer refers to, ascending, only numbers that were given.</param>
public sealed record KnowledgeAnswerResult(string Answer, bool Found, IReadOnlyList<int> Cited);

/// <summary>
/// Answers a question only from the best search hits (retrieval plus local Granite). The sources are data, never
/// instructions; the model has to cite them and say when they do not contain the answer.
/// </summary>
public static partial class KnowledgeAnswer
{
    public const int MaxSources = 6;
    public const int MaxTextPerSource = 800;
    public const int MaxQuestion = 500;
    public const int MaxAnswer = 1500;

    /// <summary>Best hits that carry text, one per item, numbered from 1.</summary>
    public static List<AnswerSource> SelectSources(IEnumerable<SemanticHit> hits, int max = MaxSources)
        => hits.Where(h => !string.IsNullOrWhiteSpace(h.Chunk.Text))
            .DistinctBy(h => h.Chunk.ItemKey)
            .Take(max)
            .Select((h, i) => new AnswerSource(i + 1, h.Chunk, Limit(Clean(h.Chunk.Text), MaxTextPerSource)))
            .ToList();

    public static string BuildInput(string question, IReadOnlyList<AnswerSource> sources)
    {
        var q = Clean(question);
        if (q.Length == 0) throw new ArgumentException("Bitte eine Frage eingeben.");
        if (sources.Count == 0) throw new ArgumentException("Keine Quellen für die Antwort.");
        return JsonSerializer.Serialize(new
        {
            question = Limit(q, MaxQuestion),
            sources = sources.Select(s => new
            {
                n = s.Number,
                kind = SemanticChunk.SourceName(s.Chunk.Source),
                date = s.Chunk.Date.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                title = Limit(Clean(s.Chunk.Title), 120),
                metadata = Limit(Clean(KnowledgeMetadata.Describe(s.Chunk.Metadata)), 800),
                text = s.Text,
            }),
        });
    }

    /// <param name="prompt">Instruction from <see cref="AiPrompts"/>; the default when null.</param>
    public static string BuildRequest(string input, string? prompt = null) => JsonSerializer.Serialize(new
    {
        messages = new[]
        {
            new { role = "system", content = prompt ?? AiPrompts.AnswerDefault },
            new { role = "user", content = input }
        },
        temperature = 0.2, top_p = 0.9, seed = 42, max_tokens = 600, stream = false,
        chat_template_kwargs = new { enable_thinking = false },
        response_format = new
        {
            type = "json_schema",
            json_schema = new
            {
                name = "answer", strict = true,
                schema = new
                {
                    type = "object",
                    properties = new
                    {
                        answer = new { type = "string", maxLength = MaxAnswer },
                        found = new { type = "boolean" },
                        sources = new { type = "array", items = new { type = "integer" }, maxItems = MaxSources }
                    },
                    required = new[] { "answer", "found", "sources" }, additionalProperties = false
                }
            }
        }
    });

    public static KnowledgeAnswerResult Parse(string response, int sourceCount)
    {
        if (response.Length > 65536) throw new InvalidDataException("Die KI-Antwort ist zu lang.");
        try
        {
            using var envelope = JsonDocument.Parse(response);
            var choice = envelope.RootElement.GetProperty("choices")[0];
            if (choice.GetProperty("finish_reason").GetString() != "stop")
                throw new InvalidDataException("Die KI-Antwort ist unvollständig. Bitte die Frage enger stellen und erneut versuchen.");
            using var json = JsonDocument.Parse(choice.GetProperty("message").GetProperty("content").GetString()!);
            var root = json.RootElement;
            var answer = (root.GetProperty("answer").GetString() ?? "").Trim();
            if (answer.Length == 0 || answer.Length > MaxAnswer)
                throw new InvalidDataException("Das Sprachmodell hat keine gültige Antwort geliefert.");
            answer = ControlChars().Replace(answer, " ");
            var found = root.GetProperty("found").GetBoolean();
            var cited = root.GetProperty("sources").EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out _)).Select(e => e.GetInt32())
                .Concat(Citations(answer));
            var valid = cited.Where(n => n >= 1 && n <= sourceCount).Distinct().Order().ToList();
            // An answer that claims to be found but cites nothing cannot be checked, so it is shown as unsupported.
            return new(answer, found && valid.Count > 0, valid);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        { throw new InvalidDataException("Das Sprachmodell hat keine gültige Antwort geliefert.", ex); }
    }

    /// <summary>Numbers in [1], [2, 3] or [1][4] style markers.</summary>
    public static IEnumerable<int> Citations(string answer)
        => CitationMarker().Matches(answer)
            .SelectMany(m => m.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Select(p => int.TryParse(p, out var n) ? n : 0)
            .Where(n => n > 0);

    private static string Clean(string value) => Whitespace().Replace(value, " ").Trim();
    private static string Limit(string text, int count) => text.Length <= count ? text : text[..count] + "…";

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\[(\d+(?:\s*,\s*\d+)*)\]")]
    private static partial Regex CitationMarker();

    [GeneratedRegex(@"[\p{Cc}-[\n]]")]
    private static partial Regex ControlChars();
}

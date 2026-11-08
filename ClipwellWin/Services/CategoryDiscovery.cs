using System.ComponentModel;
using System.IO;
using ClipwellWin.Models;

namespace ClipwellWin.Services;

public sealed record DiscoveryItem(string Key, string Title, string Text, DateTime Date, float[] Vector);

/// <summary>Before a group: Finished is null; after it: the (not yet merged) proposal for that group.</summary>
/// <param name="Resumed">The group was taken over from an interrupted run, no model call.</param>
public sealed record DiscoveryProgress(int Done, int Total, int GroupSize, string Sample, CategoryProposal? Finished = null, bool Resumed = false);

/// <summary>A proposed category for one group of entries; name and acceptance stay editable until applied.</summary>
public sealed class CategoryProposal : INotifyPropertyChanged
{
    private string _name = "";
    private bool _accepted;

    public required IReadOnlyList<string> Keys { get; set; }
    public required IReadOnlyList<string> Titles { get; set; }
    public required string ProposedName { get; set; }
    public required string Reason { get; set; }
    public required bool Uncertain { get; init; }
    /// <summary>Method that named the group: Granite, or the E5 classifier when it settled the group alone.</summary>
    public string Method { get; init; } = ClassifierMethods.Granite;
    private bool _isNew;
    public required bool IsNew { get => _isNew; set { _isNew = value; Changed(nameof(Kind)); } }

    public string Name { get => _name; set { _name = value; Changed(nameof(Name)); Changed(nameof(Kind)); } }
    public bool Accepted { get => _accepted; set { _accepted = value; Changed(nameof(Accepted)); } }
    public int Count => Keys.Count;
    public string Header => $"{Count} ähnliche Einträge";
    public IReadOnlyList<string> Examples => Titles.Take(4).Select(t => "• " + Shorten(t, 110)).ToList();
    public string AllTitles => string.Join("\n", Titles.Select(t => "• " + Shorten(t, 140)));
    public bool HasMore => Titles.Count > 4;
    public string Explanation => Uncertain
        ? $"Granite hat für diese Gruppe keinen eindeutigen Namen gefunden ({Reason.TrimEnd('.')}). Wenn die Einträge zusammengehören, trag links selbst einen Namen ein; sonst lass die Gruppe abgewählt."
        : $"Granite schlägt „{ProposedName}“ vor: {Reason}";
    public string Kind => Name.Trim().Length == 0 ? "noch ohne Namen" : IsNew ? "wird als neue Kategorie angelegt" : "vorhandene Kategorie";

    private static string Shorten(string text, int max)
    {
        var t = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        return t.Length <= max ? t : t[..(max - 1)] + "…";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string name) => PropertyChanged?.Invoke(this, new(name));
}

/// <summary>
/// Category analysis: groups entries by their search vectors (spherical k-means on centered vectors), lets a language
/// model name each group from its most typical entries and merges groups that received the same name. Nothing is
/// saved here; the user reviews and applies the proposals.
/// </summary>
public static class CategoryDiscovery
{
    public const int MaxItems = 600;
    public const int MaxGroups = 15;
    public const int MinGroupSize = 3;
    public const int Representatives = LocalAiCategorization.MaxSources;

    public delegate Task<LocalAiSuggestion> NameGroup(IReadOnlyList<BrainSource> sources, IReadOnlyList<string> categories, CancellationToken ct);

    /// <summary>Groups of item indices, largest first; groups smaller than <see cref="MinGroupSize"/> are left out.</summary>
    public static List<int[]> Group(IReadOnlyList<float[]> vectors)
    {
        if (vectors.Count < MinGroupSize) return [];
        var center = RocchioModel.Mean(vectors);
        var x = vectors.Select(v => RocchioModel.Normalized(RocchioModel.Subtract(v, center))).ToArray();
        var k = Math.Clamp((int)Math.Round(Math.Sqrt(vectors.Count / 2.0)), 1, Math.Min(MaxGroups, Math.Max(1, vectors.Count / MinGroupSize)));
        var centers = new List<float[]> { x[0] };
        while (centers.Count < k) centers.Add(x.OrderBy(v => centers.Max(c => RocchioModel.Cosine(v, c))).First());
        var assignment = new int[x.Length];
        for (var iteration = 0; iteration < 15; iteration++)
        {
            for (var i = 0; i < x.Length; i++)
            {
                var best = 0;
                for (var j = 1; j < centers.Count; j++) if (RocchioModel.Cosine(x[i], centers[j]) > RocchioModel.Cosine(x[i], centers[best])) best = j;
                assignment[i] = best;
            }
            for (var j = 0; j < centers.Count; j++)
            {
                var members = x.Where((_, i) => assignment[i] == j).ToArray();
                if (members.Length > 0) centers[j] = RocchioModel.Mean(members);
            }
        }
        return Enumerable.Range(0, centers.Count)
            .Select(j => Enumerable.Range(0, x.Length).Where(i => assignment[i] == j)
                .OrderByDescending(i => RocchioModel.Cosine(x[i], centers[j])).ToArray())
            .Where(g => g.Length >= MinGroupSize).OrderByDescending(g => g.Length).ToList();
    }

    /// <param name="done">
    /// Groups named in an interrupted run over the same items; a group with exactly these keys is taken over without a
    /// model call (grouping is deterministic for the same items).
    /// </param>
    public static async Task<List<CategoryProposal>> RunAsync(IReadOnlyList<DiscoveryItem> items, IReadOnlyList<string> existing,
        NameGroup name, IProgress<DiscoveryProgress>? progress = null, CancellationToken ct = default,
        IReadOnlyList<CategoryProposal>? done = null)
    {
        var groups = Group(items.Select(i => i.Vector).ToArray());
        var known = new List<string>(existing);
        var proposals = new List<CategoryProposal>();
        for (var g = 0; g < groups.Count; g++)
        {
            ct.ThrowIfCancellationRequested();
            var members = groups[g].Select(i => items[i]).ToList();
            if (done?.FirstOrDefault(d => d.Keys.SequenceEqual(members.Select(m => m.Key))) is { } previous)
            {
                if (!previous.Uncertain && !known.Contains(previous.ProposedName, StringComparer.OrdinalIgnoreCase)) known.Add(previous.ProposedName);
                proposals.Add(previous);
                progress?.Report(new DiscoveryProgress(g + 1, groups.Count, members.Count, "", previous, Resumed: true));
                continue;
            }
            progress?.Report(new DiscoveryProgress(g, groups.Count, members.Count, members[0].Title));
            var sources = members.Take(Representatives).Select(m => new BrainSource(m.Key, m.Title, m.Text, "", m.Date)).ToList();
            LocalAiSuggestion suggestion;
            try { suggestion = await name(sources, known.ToList(), ct).ConfigureAwait(false); }
            // Only an unusable answer for this group is kept as an unnamed group; start or runtime errors abort the analysis.
            catch (InvalidDataException ex)
            {
                suggestion = new LocalAiSuggestion("", "", "Granite hat für diese Gruppe keine gültige Antwort geliefert: " + ex.Message, true);
            }
            var category = suggestion.Uncertain ? "" : suggestion.Category.Trim();
            if (category.Length > 0 && !known.Contains(category, StringComparer.OrdinalIgnoreCase)) known.Add(category);
            var proposal = new CategoryProposal
            {
                Keys = members.Select(m => m.Key).ToList(), Titles = members.Select(m => m.Title).ToList(),
                ProposedName = category, Reason = suggestion.Reason, Uncertain = category.Length == 0,
                IsNew = category.Length > 0 && !existing.Contains(category, StringComparer.OrdinalIgnoreCase),
                Name = category, Accepted = category.Length > 0,
                Method = suggestion.Method.Length > 0 ? suggestion.Method : ClassifierMethods.Granite,
            };
            proposals.Add(proposal);
            progress?.Report(new DiscoveryProgress(g + 1, groups.Count, members.Count, "", proposal));
        }
        return Merge(proposals);
    }

    /// <summary>Groups that got the same name become one new proposal; the given proposals stay unchanged.</summary>
    public static List<CategoryProposal> Merge(List<CategoryProposal> proposals)
    {
        var merged = new List<CategoryProposal>();
        foreach (var p in proposals)
        {
            var index = p.Uncertain ? -1 : merged.FindIndex(m => !m.Uncertain && string.Equals(m.ProposedName, p.ProposedName, StringComparison.OrdinalIgnoreCase));
            if (index < 0) { merged.Add(p); continue; }
            var same = merged[index];
            merged[index] = new CategoryProposal
            {
                Keys = same.Keys.Concat(p.Keys).Distinct().ToList(), Titles = same.Titles.Concat(p.Titles).ToList(),
                ProposedName = same.ProposedName, Reason = same.Reason, Uncertain = same.Uncertain, Method = same.Method,
                IsNew = same.IsNew, Name = same.Name, Accepted = same.Accepted,
            };
        }
        return merged.OrderBy(p => p.Uncertain).ThenByDescending(p => p.Count).ToList();
    }

    /// <summary>
    /// Newest indexed entries without a category (or exactly <paramref name="only"/>, any category), one averaged
    /// vector per entry, at most <see cref="MaxItems"/>.
    /// </summary>
    public static List<DiscoveryItem> Candidates(IReadOnlyList<IndexedChunk> snapshot, IReadOnlyDictionary<string, BrainAnnotation>? annotations,
        Func<IReadOnlyCollection<string>, Dictionary<string, (string Text, DateTime Date)>> texts, IReadOnlySet<string>? only = null)
    {
        var candidates = snapshot.GroupBy(c => c.Chunk.ItemKey)
            .Where(g => only != null ? only.Contains(g.Key) : KnowledgeCategories.Effective(annotations, g.Key).IsEmpty)
            .Select(g => (Key: g.Key, Chunk: g.First().Chunk, Date: g.Max(c => c.Chunk.Date), Vector: RocchioModel.Mean(g.Select(c => c.Vector).ToArray())))
            .OrderByDescending(x => x.Date).Take(MaxItems).ToList();
        var found = texts(candidates.Select(c => c.Key).ToList());
        return candidates.Where(c => found.ContainsKey(c.Key))
            .Select(c => new DiscoveryItem(c.Key, c.Chunk.Title, found[c.Key].Text, c.Date, c.Vector)).ToList();
    }
}

/// <summary>One named group as stored by the background analysis.</summary>
public sealed record StoredProposal(List<string> Keys, List<string> Titles, string ProposedName, string Reason, bool Uncertain, string Method)
{
    public static StoredProposal From(CategoryProposal p)
        => new(p.Keys.ToList(), p.Titles.ToList(), p.ProposedName, p.Reason, p.Uncertain, p.Method);
}

/// <summary>
/// State of the background category analysis in background-discovery.json: the analyzed item keys in order, the
/// groups named so far and whether the run is complete. Nothing in here is a saved category.
/// </summary>
public sealed class BackgroundDiscoveryState
{
    public const string FileName = "background-discovery.json";
    /// <summary>New entries without a category needed before a finished analysis is repeated.</summary>
    public const int NewItemsForRerun = 10;

    public List<string> Keys { get; set; } = [];
    public List<StoredProposal> Groups { get; set; } = [];
    public bool Complete { get; set; }
    public DateTime Started { get; set; }
    public DateTime? Finished { get; set; }
    /// <summary>The user has opened the finished proposals; no more reminders.</summary>
    public bool Seen { get; set; }

    private static readonly System.Text.Json.JsonSerializerOptions Options = new() { WriteIndented = true };

    public static BackgroundDiscoveryState? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return System.Text.Json.JsonSerializer.Deserialize<BackgroundDiscoveryState>(SecureFile.ReadAllText(path), Options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException
            or System.Security.Cryptography.CryptographicException)
        {
            App.LogCrash(ex);
            return null;
        }
    }

    public void Save(string path)
    {
        var temp = path + ".tmp";
        SecureFile.WriteAllText(temp, System.Text.Json.JsonSerializer.Serialize(this, Options));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>True when the run can continue on <paramref name="items"/> (same entries in the same order).</summary>
    public bool CanResume(IReadOnlyList<DiscoveryItem> items) => !Complete && Keys.SequenceEqual(items.Select(i => i.Key));

    /// <summary>Entries without a category that the last analysis did not include.</summary>
    public int NewItems(IEnumerable<string> uncategorized)
    {
        var known = Keys.ToHashSet(StringComparer.Ordinal);
        return uncategorized.Count(k => !known.Contains(k));
    }

    /// <summary>Stored groups as editable proposals for the review window.</summary>
    public List<CategoryProposal> Proposals(IReadOnlyList<string> existing) => Groups.Select(g => ToProposal(g, g.Keys, g.Titles, existing)).ToList();

    /// <summary>
    /// Finished proposals for review: only entries that are still indexed and without a category, groups that keep
    /// at least one entry, merged by name.
    /// </summary>
    public List<CategoryProposal> Ready(IReadOnlySet<string> stillOpen, IReadOnlyList<string> existing)
    {
        var list = new List<CategoryProposal>();
        foreach (var g in Groups)
        {
            var keep = Enumerable.Range(0, g.Keys.Count).Where(i => stillOpen.Contains(g.Keys[i])).ToList();
            if (keep.Count == 0) continue;
            list.Add(ToProposal(g, keep.Select(i => g.Keys[i]).ToList(),
                keep.Select(i => i < g.Titles.Count ? g.Titles[i] : g.Keys[i]).ToList(), existing));
        }
        return CategoryDiscovery.Merge(list);
    }

    private static CategoryProposal ToProposal(StoredProposal g, List<string> keys, List<string> titles, IReadOnlyList<string> existing)
    {
        var name = g.Uncertain ? "" : KnowledgeCategories.Parse(g.ProposedName).ToString();
        return new CategoryProposal
        {
            Keys = keys, Titles = titles, ProposedName = name, Reason = g.Reason, Uncertain = g.Uncertain,
            IsNew = name.Length > 0 && !existing.Contains(name, StringComparer.OrdinalIgnoreCase),
            Name = name, Accepted = name.Length > 0,
            Method = g.Method.Length > 0 ? g.Method : ClassifierMethods.Granite,
        };
    }
}

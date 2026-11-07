using System.ComponentModel;
using System.IO;

namespace ClipwellWin.Services;

public sealed record DiscoveryItem(string Key, string Title, string Text, DateTime Date, float[] Vector);

/// <summary>Before a group: Finished is null; after it: the (not yet merged) proposal for that group.</summary>
public sealed record DiscoveryProgress(int Done, int Total, int GroupSize, string Sample, CategoryProposal? Finished = null);

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

    public static async Task<List<CategoryProposal>> RunAsync(IReadOnlyList<DiscoveryItem> items, IReadOnlyList<string> existing,
        NameGroup name, IProgress<DiscoveryProgress>? progress = null, CancellationToken ct = default)
    {
        var groups = Group(items.Select(i => i.Vector).ToArray());
        var known = new List<string>(existing);
        var proposals = new List<CategoryProposal>();
        for (var g = 0; g < groups.Count; g++)
        {
            ct.ThrowIfCancellationRequested();
            var members = groups[g].Select(i => items[i]).ToList();
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

    /// <summary>Groups that got the same name become one proposal.</summary>
    public static List<CategoryProposal> Merge(List<CategoryProposal> proposals)
    {
        var merged = new List<CategoryProposal>();
        foreach (var p in proposals)
        {
            var same = p.Uncertain ? null : merged.FirstOrDefault(m => !m.Uncertain && string.Equals(m.ProposedName, p.ProposedName, StringComparison.OrdinalIgnoreCase));
            if (same == null) { merged.Add(p); continue; }
            same.Keys = same.Keys.Concat(p.Keys).Distinct().ToList();
            same.Titles = same.Titles.Concat(p.Titles).ToList();
        }
        return merged.OrderBy(p => p.Uncertain).ThenByDescending(p => p.Count).ToList();
    }
}

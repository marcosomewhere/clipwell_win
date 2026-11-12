using System.ComponentModel;

namespace Merkautomat.Services;

public enum CategorizationState { Open, Applied, Skipped }

/// <summary>One entry in a categorization pass: the source, its saved category and the latest suggestion.</summary>
public sealed class CategorizationItem : INotifyPropertyChanged
{
    private bool _marked;
    private string _category = "";
    private CategorizationState _state;
    private LocalAiSuggestion? _suggestion;

    public required BrainSource Source { get; init; }
    public required string SourceName { get; init; }
    public string Key => Source.Key;
    /// <summary>Assignments version the suggestion was computed with; E5 learns from newer assignments.</summary>
    public int SuggestionVersion { get; set; } = -1;

    public bool Marked { get => _marked; set { if (_marked == value) return; _marked = value; Changed(nameof(Marked)); } }
    public string Category { get => _category; set { _category = value; Changed(nameof(Category)); Changed(nameof(Status)); } }
    public CategorizationState State { get => _state; set { _state = value; Changed(nameof(State)); Changed(nameof(Status)); } }
    public LocalAiSuggestion? Suggestion { get => _suggestion; set { _suggestion = value; Changed(nameof(Suggestion)); Changed(nameof(Status)); } }

    public string Meta => $"{SourceName} · {Source.Date:dd.MM.yyyy HH:mm}";
    public string Status => State switch
    {
        CategorizationState.Applied => $"Zugeordnet: {Category}",
        CategorizationState.Skipped => "Übersprungen" + (Category.Length > 0 ? $" · Kategorie: {Category}" : ""),
        _ => (Category.Length > 0 ? $"Kategorie: {Category}" : "Ohne Kategorie")
             + (Suggestion is { Uncertain: false } s && !s.Category.Equals(Category, StringComparison.OrdinalIgnoreCase) ? $" · Vorschlag: {s.Category}" : ""),
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string name) => PropertyChanged?.Invoke(this, new(name));
}

/// <summary>
/// State of a categorization pass. Every confirmed category is written to the assignments immediately, so later
/// E5 suggestions in the same pass already learn from it.
/// </summary>
public sealed class CategorizationQueue
{
    private readonly Dictionary<string, string> _assignments;

    private readonly List<string> _known;

    /// <param name="assignments">Saved assignments, item key to category path "Haupt › Unter".</param>
    /// <param name="known">Defined categories (paths) that are offered even without assignments.</param>
    public CategorizationQueue(IEnumerable<CategorizationItem> items, IReadOnlyDictionary<string, string> assignments,
        IEnumerable<string>? known = null)
    {
        Items = items.ToList();
        _known = (known ?? []).Select(k => KnowledgeCategories.Parse(k).ToString()).Where(k => k.Length > 0).ToList();
        _assignments = assignments.Select(a => (a.Key, Path: KnowledgeCategories.Parse(a.Value).ToString())).Where(a => a.Path.Length > 0)
            .ToDictionary(a => a.Key, a => a.Path, StringComparer.Ordinal);
        foreach (var item in Items) item.Category = _assignments.GetValueOrDefault(item.Key, "");
    }

    public IReadOnlyList<CategorizationItem> Items { get; }
    public IReadOnlyDictionary<string, string> Assignments => _assignments;
    /// <summary>Increases with every saved assignment.</summary>
    public int Version { get; private set; }

    /// <summary>All known paths: main categories, "Haupt › Unter" and "Haupt › Unter › Fein", with every parent level.</summary>
    public IReadOnlyList<string> Categories => _known.Concat(_assignments.Values)
        .Select(KnowledgeCategories.Parse).Where(p => !p.IsEmpty)
        .SelectMany(p => Enumerable.Range(1, p.Depth).Select(d => KnowledgeCategories.Build(p.Levels.Take(d)).ToString()))
        .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<string> Mains => Categories.Select(KnowledgeCategories.MainOf).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Subcategories of a main category and their "Unter › Fein" paths, for the second field of the dialog.</summary>
    public IReadOnlyList<string> SubsOf(string main) => Categories.Select(KnowledgeCategories.Parse)
        .Where(p => p.HasSub && string.Equals(p.Main, main.Trim(), StringComparison.OrdinalIgnoreCase))
        .Select(p => p.HasFine ? p.Sub + KnowledgeCategories.Separator + p.Fine : p.Sub)
        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static string? Validate(string category) => KnowledgeCategories.Validate(KnowledgeCategories.Parse(category));

    /// <summary>Records the category for the items; returns the keys whose saved category changed.</summary>
    public List<string> Apply(IEnumerable<CategorizationItem> items, string category)
    {
        if (Validate(category) is { } error) throw new ArgumentException(error, nameof(category));
        var value = KnowledgeCategories.Parse(category).ToString();
        var changed = new List<string>();
        foreach (var item in items.Distinct())
        {
            if (_assignments.GetValueOrDefault(item.Key) != value) changed.Add(item.Key);
            _assignments[item.Key] = value;
            item.Category = value;
            item.State = CategorizationState.Applied;
        }
        if (changed.Count > 0) Version++;
        return changed;
    }

    public static void Skip(CategorizationItem item) => item.State = CategorizationState.Skipped;

    /// <summary>Next open item after <paramref name="from"/>, wrapping around once; -1 when none is left.</summary>
    public int NextOpen(int from, bool onlyUncategorized)
    {
        for (var step = 1; step <= Items.Count; step++)
        {
            var i = ((from < 0 ? -1 : from) + step) % Items.Count;
            if (i < 0) continue;
            var item = Items[i];
            if (item.State == CategorizationState.Open && (!onlyUncategorized || item.Category.Length == 0)) return i;
        }
        return -1;
    }

    /// <summary>E5 suggestions are refreshed once new assignments exist; other models do not learn from them.</summary>
    public bool NeedsSuggestion(CategorizationItem item, bool learns)
        => item.Suggestion == null || (learns && item.SuggestionVersion != Version);

    public int Count(CategorizationState state) => Items.Count(i => i.State == state);
}

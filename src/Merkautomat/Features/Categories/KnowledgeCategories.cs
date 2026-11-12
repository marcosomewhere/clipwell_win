using System.Text.RegularExpressions;
using Merkautomat.Models;

namespace Merkautomat.Services;

/// <summary>Main category, optional subcategory and optional fine category below it; written as "Haupt › Unter › Fein".</summary>
public readonly record struct CategoryPath
{
    // default(CategoryPath) must read as empty strings, not null.
    private readonly string? _main;
    private readonly string? _sub;
    private readonly string? _fine;

    /// <summary>
    /// A level with the name of the one above adds nothing ("Engineering › Engineering") and is dropped; a fine category
    /// exists only below a subcategory.
    /// </summary>
    public CategoryPath(string? main, string? sub, string? fine = null)
    {
        _main = main;
        _sub = sub != null && main != null && Same(sub, main) ? "" : sub;
        var below = _sub ?? "";
        _fine = fine == null || below.Trim().Length == 0 || Same(fine, below) || (main != null && Same(fine, main)) ? "" : fine;
    }

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    public string Main { get => _main ?? ""; init => _main = value; }
    public string Sub { get => _sub ?? ""; init => _sub = value; }
    public string Fine { get => _fine ?? ""; init => _fine = value; }
    public bool Equals(CategoryPath other) => Main == other.Main && Sub == other.Sub && Fine == other.Fine;
    public override int GetHashCode() => HashCode.Combine(Main, Sub, Fine);
    public bool IsEmpty => Main.Length == 0;
    public bool HasSub => Main.Length > 0 && Sub.Length > 0;
    public bool HasFine => HasSub && Fine.Length > 0;
    /// <summary>0 (empty) to 3 (with fine category).</summary>
    public int Depth => IsEmpty ? 0 : HasFine ? 3 : HasSub ? 2 : 1;
    public CategoryPath Parent => HasFine ? new CategoryPath(Main, Sub) : HasSub ? new CategoryPath(Main, null) : default;
    /// <summary>Name of the deepest level.</summary>
    public string Leaf => HasFine ? Fine : HasSub ? Sub : Main;
    public IReadOnlyList<string> Levels => Depth switch { 0 => [], 1 => [Main], 2 => [Main, Sub], _ => [Main, Sub, Fine] };
    public override string ToString() => string.Join(KnowledgeCategories.Separator, Levels);
}

/// <summary>
/// Category hierarchy of the knowledge base with up to three levels (main, sub, fine), saved per item key in
/// <see cref="AppSettings.BrainAnnotations"/> and defined (with descriptions) in <see cref="AppSettings.KnowledgeCategories"/>.
/// Inside dialogs and classifiers a category is passed as one path string, so string-based code keeps working.
/// </summary>
public static class KnowledgeCategories
{
    public const string Separator = " › ";
    public const int MaxDepth = 3;
    public const int MaxLength = 80;
    public const int MaxDescriptionLength = 300;

    private static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;

    // ---- Paths ---------------------------------------------------------------------------------------------

    /// <summary>"Haupt › Unter › Fein", also with "&gt;"; whitespace is collapsed, further levels join the fine category.</summary>
    public static CategoryPath Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return default;
        var all = text.Split(['›', '>']).Select(Clean).ToList();
        // Without a main category there is no path, even if lower levels were typed.
        if (all[0].Length == 0) return default;
        var parts = all.Where(p => p.Length > 0).ToList();
        return new CategoryPath(parts[0], parts.ElementAtOrDefault(1) ?? "", parts.Count > 2 ? string.Join(" ", parts.Skip(2)) : "");
    }

    /// <summary>Main category plus the rest, which may itself be "Unter" or "Unter › Fein".</summary>
    public static string Combine(string? main, string? rest)
        => Clean(main).Length == 0 ? "" : Parse(Clean(main) + Separator + (rest ?? "")).ToString();

    public static string Combine(string? main, string? sub, string? fine)
        => new CategoryPath(Clean(main), Clean(main).Length == 0 ? "" : Clean(sub), Clean(fine)).ToString();

    /// <summary>Path from up to three level names; deeper levels are dropped.</summary>
    public static CategoryPath Build(IEnumerable<string> levels)
    {
        var l = levels.Select(Clean).Where(x => x.Length > 0).Take(MaxDepth).ToList();
        return l.Count == 0 ? default : new CategoryPath(l[0], l.ElementAtOrDefault(1) ?? "", l.ElementAtOrDefault(2) ?? "");
    }

    public static string MainOf(string? path) => Parse(path).Main;

    public static CategoryPath Of(BrainAnnotation? a)
    {
        var main = Clean(a?.Category);
        return main.Length == 0 ? default : new CategoryPath(main, Clean(a!.Subcategory), Clean(a.Finecategory));
    }

    public static string? Validate(CategoryPath path)
    {
        if (path.Main.Length == 0) return "Bitte eine Hauptkategorie wählen oder eingeben.";
        if (path.Main.Length > MaxLength) return $"Die Hauptkategorie darf höchstens {MaxLength} Zeichen enthalten.";
        if (path.Sub.Length > MaxLength) return $"Die Unterkategorie darf höchstens {MaxLength} Zeichen enthalten.";
        if (path.Fine.Length > MaxLength) return $"Die Feinkategorie darf höchstens {MaxLength} Zeichen enthalten.";
        return null;
    }

    public static bool SameMain(CategoryPath a, CategoryPath b) => Cmp.Equals(a.Main, b.Main);
    public static bool Same(CategoryPath a, CategoryPath b) => Cmp.Equals(a.Main, b.Main) && Cmp.Equals(a.Sub, b.Sub) && Cmp.Equals(a.Fine, b.Fine);

    /// <summary><paramref name="path"/> is <paramref name="scope"/> or lies below it.</summary>
    public static bool IsWithin(CategoryPath path, CategoryPath scope)
    {
        if (scope.IsEmpty || path.Depth < scope.Depth) return false;
        var p = path.Levels;
        var s = scope.Levels;
        for (var i = 0; i < s.Count; i++) if (!Cmp.Equals(p[i], s[i])) return false;
        return true;
    }

    private static string Clean(string? value) => value == null ? "" : Regex.Replace(value, @"\s+", " ").Trim();

    // ---- Lookup ----------------------------------------------------------------------------------------------

    /// <summary>A whole recording ("r:datei.md"); its snapshots have keys "r:datei.md#n".</summary>
    public static string RecordingKey(string fileName) => "r:" + fileName;

    public static bool IsRecordingFileKey(string key) => key.StartsWith("r:", StringComparison.Ordinal) && !key.Contains('#');

    public static string? RecordingKeyOf(string itemKey)
    {
        if (!itemKey.StartsWith("r:", StringComparison.Ordinal)) return null;
        var hash = itemKey.IndexOf('#');
        return hash < 0 ? null : itemKey[..hash];
    }

    /// <summary>Category of an item; recording snapshots without their own fall back to the category of the recording.</summary>
    public static CategoryPath Effective(IReadOnlyDictionary<string, BrainAnnotation>? annotations, string itemKey)
    {
        if (annotations == null) return default;
        var own = Of(annotations.GetValueOrDefault(itemKey));
        if (!own.IsEmpty) return own;
        return RecordingKeyOf(itemKey) is { } file ? Of(annotations.GetValueOrDefault(file)) : default;
    }

    /// <summary>Saved assignments as item key to path, for the categorization pass and classifiers.</summary>
    public static Dictionary<string, string> Assignments(IReadOnlyDictionary<string, BrainAnnotation>? annotations)
        => (annotations ?? new Dictionary<string, BrainAnnotation>())
            .Where(a => !IsRecordingFileKey(a.Key))
            .Select(a => (a.Key, Path: Of(a.Value)))
            .Where(a => !a.Path.IsEmpty)
            .ToDictionary(a => a.Key, a => a.Path.ToString(), StringComparer.Ordinal);

    // ---- Taxonomy ------------------------------------------------------------------------------------------

    public static IEnumerable<CategoryPath> Used(AppSettings s)
        => (s.BrainAnnotations ?? []).Values.Select(Of).Where(p => !p.IsEmpty);

    public static List<string> Mains(AppSettings s)
        => (s.KnowledgeCategories ?? []).Select(c => Clean(c.Name)).Concat(Used(s).Select(p => p.Main))
            .Where(m => m.Length > 0).Distinct(Cmp).Order(Cmp).ToList();

    public static List<string> Subs(AppSettings s, string? main)
    {
        var m = Clean(main);
        if (m.Length == 0) return [];
        var defined = Find(s, m)?.Subcategories.Select(x => Clean(x.Name)) ?? [];
        return defined.Concat(Used(s).Where(p => Cmp.Equals(p.Main, m)).Select(p => p.Sub))
            .Where(x => x.Length > 0).Distinct(Cmp).Order(Cmp).ToList();
    }

    /// <summary>Fine categories below "<paramref name="main"/> › <paramref name="sub"/>", defined or used.</summary>
    public static List<string> Fines(AppSettings s, string? main, string? sub)
    {
        var m = Clean(main);
        var u = Clean(sub);
        if (m.Length == 0 || u.Length == 0) return [];
        var defined = FindSub(s, m, u)?.Subcategories.Select(x => Clean(x.Name)) ?? [];
        return defined.Concat(Used(s).Where(p => Cmp.Equals(p.Main, m) && Cmp.Equals(p.Sub, u)).Select(p => p.Fine))
            .Where(x => x.Length > 0).Distinct(Cmp).Order(Cmp).ToList();
    }

    /// <summary>All main categories with their "Haupt › Unter" and "Haupt › Unter › Fein" paths, parents first.</summary>
    public static List<string> Paths(AppSettings s)
        => Mains(s).SelectMany(m => new[] { m }.Concat(Subs(s, m).SelectMany(x =>
            new[] { Combine(m, x) }.Concat(Fines(s, m, x).Select(f => Combine(m, x, f)))))).ToList();

    /// <summary>Subcategories and "Unter › Fein" below a main category, for the second field of the category dialogs.</summary>
    public static List<string> SubPaths(AppSettings s, string? main)
        => Subs(s, main).SelectMany(x => new[] { x }.Concat(Fines(s, main, x).Select(f => x + Separator + f))).ToList();

    public static KnowledgeCategory? Find(AppSettings s, string main)
        => (s.KnowledgeCategories ?? []).FirstOrDefault(c => Cmp.Equals(Clean(c.Name), Clean(main)));

    public static KnowledgeSubcategory? FindSub(AppSettings s, string main, string sub)
        => Find(s, main)?.Subcategories.FirstOrDefault(x => Cmp.Equals(Clean(x.Name), Clean(sub)));

    private static KnowledgeSubcategory? FindFine(AppSettings s, CategoryPath p)
        => FindSub(s, p.Main, p.Sub)?.Subcategories.FirstOrDefault(x => Cmp.Equals(Clean(x.Name), p.Fine));

    public static string Description(AppSettings s, CategoryPath path) => path.Depth switch
    {
        1 => Find(s, path.Main)?.Description.Trim() ?? "",
        2 => FindSub(s, path.Main, path.Sub)?.Description.Trim() ?? "",
        3 => FindFine(s, path)?.Description.Trim() ?? "",
        _ => "",
    };

    /// <summary>Path to description for every described category (E5 compares names together with descriptions).</summary>
    public static Dictionary<string, string> Descriptions(AppSettings s)
        => DefinedPaths(s).Where(d => d.Description.Length > 0).GroupBy(d => d.Path.ToString(), Cmp)
            .ToDictionary(g => g.Key, g => g.First().Description, Cmp);

    /// <summary>Every defined level with its description, parents before children.</summary>
    public static List<(CategoryPath Path, string Description)> DefinedPaths(AppSettings s)
    {
        var result = new List<(CategoryPath, string)>();
        foreach (var c in s.KnowledgeCategories ?? [])
        {
            if (Clean(c.Name).Length == 0) continue;
            result.Add((new CategoryPath(Clean(c.Name), null), c.Description.Trim()));
            foreach (var sub in c.Subcategories.Where(x => Clean(x.Name).Length > 0))
            {
                result.Add((new CategoryPath(Clean(c.Name), Clean(sub.Name)), sub.Description.Trim()));
                foreach (var fine in sub.Subcategories.Where(x => Clean(x.Name).Length > 0))
                    result.Add((new CategoryPath(Clean(c.Name), Clean(sub.Name), Clean(fine.Name)), fine.Description.Trim()));
            }
        }
        return result;
    }

    /// <summary>Adds the category and its parents to the definitions when missing.</summary>
    public static void Define(AppSettings s, CategoryPath path)
    {
        if (path.IsEmpty) return;
        s.KnowledgeCategories ??= [];
        var main = Find(s, path.Main);
        if (main == null) s.KnowledgeCategories.Add(main = new KnowledgeCategory { Name = path.Main });
        if (!path.HasSub) return;
        var sub = main.Subcategories.FirstOrDefault(x => Cmp.Equals(Clean(x.Name), path.Sub));
        if (sub == null) main.Subcategories.Add(sub = new KnowledgeSubcategory { Name = path.Sub });
        if (path.HasFine && !sub.Subcategories.Any(x => Cmp.Equals(Clean(x.Name), path.Fine)))
            sub.Subcategories.Add(new KnowledgeSubcategory { Name = path.Fine });
    }

    public static void SetDescription(AppSettings s, CategoryPath path, string description)
    {
        Define(s, path);
        var text = Clean(description);
        if (text.Length > MaxDescriptionLength) text = text[..MaxDescriptionLength];
        switch (path.Depth)
        {
            case 1: Find(s, path.Main)!.Description = text; break;
            case 2: FindSub(s, path.Main, path.Sub)!.Description = text; break;
            case 3: FindFine(s, path)!.Description = text; break;
        }
    }

    /// <summary>Sets or (empty path) clears the category of the keys; titles stay. Returns the keys that changed.</summary>
    public static List<string> Assign(AppSettings s, IEnumerable<string> keys, CategoryPath path)
    {
        s.BrainAnnotations ??= [];
        Define(s, path);
        var changed = new List<string>();
        foreach (var key in keys.Distinct(StringComparer.Ordinal))
        {
            var old = s.BrainAnnotations.GetValueOrDefault(key);
            if (Same(Of(old), path)) continue;
            var name = old?.Name ?? "";
            if (path.IsEmpty && name.Length == 0) s.BrainAnnotations.Remove(key);
            else s.BrainAnnotations[key] = new BrainAnnotation(name, path.Main, path.Sub, path.Fine);
            changed.Add(key);
        }
        return changed;
    }

    /// <summary>
    /// A category saved from a path; a shorter path that is a parent of the existing category keeps the deeper levels
    /// (dialogs that only know main categories must not drop subcategories or fine categories).
    /// </summary>
    public static BrainAnnotation Annotation(BrainAnnotation? old, string name, string pathText)
    {
        var path = Parse(pathText);
        var previous = Of(old);
        if (path.Depth < previous.Depth && IsWithin(previous, path)) path = previous;
        return new BrainAnnotation(name, path.Main, path.Sub, path.Fine);
    }

    /// <summary>
    /// Renames or moves a category of any level together with everything below it; moving onto an existing name merges.
    /// Levels that would end up deeper than three are dropped. Returns the number of entries that changed.
    /// </summary>
    public static int Rename(AppSettings s, CategoryPath from, CategoryPath to)
    {
        if (from.IsEmpty || to.IsEmpty) return 0;
        s.BrainAnnotations ??= [];
        s.KnowledgeCategories ??= [];
        CategoryPath Moved(CategoryPath p) => Build(to.Levels.Concat(p.Levels.Skip(from.Depth)));

        var count = 0;
        foreach (var (key, a) in s.BrainAnnotations.ToList())
        {
            var p = Of(a);
            if (!IsWithin(p, from)) continue;
            var target = Moved(p);
            if (Same(p, target)) continue;
            s.BrainAnnotations[key] = a with { Category = target.Main, Subcategory = target.Sub, Finecategory = target.Fine };
            count++;
        }

        // Definitions move with their descriptions; an existing description at the target wins.
        var subtree = DefinedPaths(s).Where(d => IsWithin(d.Path, from)).ToList();
        RemoveDefinition(s, from);
        Define(s, to);
        foreach (var (path, description) in subtree.OrderBy(d => d.Path.Depth))
        {
            var target = Moved(path);
            if (target.IsEmpty) continue;
            Define(s, target);
            if (description.Length > 0 && Description(s, target).Length == 0) SetDescription(s, target, description);
        }
        return count;
    }

    /// <summary>Deletes a category with everything below it; its entries move up to the parent level (a main category: none).</summary>
    public static int Delete(AppSettings s, CategoryPath path)
    {
        if (path.IsEmpty) return 0;
        s.BrainAnnotations ??= [];
        var count = 0;
        foreach (var (key, a) in s.BrainAnnotations.ToList())
        {
            if (!IsWithin(Of(a), path)) continue;
            var target = path.Parent;
            if (target.IsEmpty && a.Name.Length == 0) s.BrainAnnotations.Remove(key);
            else s.BrainAnnotations[key] = a with { Category = target.Main, Subcategory = target.Sub, Finecategory = target.Fine };
            count++;
        }
        RemoveDefinition(s, path);
        return count;
    }

    private static void RemoveDefinition(AppSettings s, CategoryPath path)
    {
        switch (path.Depth)
        {
            case 1: s.KnowledgeCategories?.RemoveAll(c => Cmp.Equals(Clean(c.Name), path.Main)); break;
            case 2: Find(s, path.Main)?.Subcategories.RemoveAll(x => Cmp.Equals(Clean(x.Name), path.Sub)); break;
            case 3: FindSub(s, path.Main, path.Sub)?.Subcategories.RemoveAll(x => Cmp.Equals(Clean(x.Name), path.Fine)); break;
        }
    }

    /// <summary>Number of saved entries per main category and per deeper path; an entry counts for every level above it.</summary>
    public static Dictionary<string, int> Counts(AppSettings s)
    {
        var result = new Dictionary<string, int>(Cmp);
        // A whole recording is counted through its snapshots.
        foreach (var p in (s.BrainAnnotations ?? []).Where(a => !IsRecordingFileKey(a.Key)).Select(a => Of(a.Value)).Where(p => !p.IsEmpty))
            for (var depth = 1; depth <= p.Depth; depth++)
            {
                var key = Build(p.Levels.Take(depth)).ToString();
                result[key] = result.GetValueOrDefault(key) + 1;
            }
        return result;
    }
}

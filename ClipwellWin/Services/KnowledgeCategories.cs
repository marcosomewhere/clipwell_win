using System.Text.RegularExpressions;
using ClipwellWin.Models;

namespace ClipwellWin.Services;

/// <summary>Main category plus optional subcategory; written as "Haupt › Unter".</summary>
public readonly record struct CategoryPath
{
    // default(CategoryPath) must read as empty strings, not null.
    private readonly string? _main;
    private readonly string? _sub;

    /// <summary>A subcategory with the name of its main category adds nothing ("Engineering › Engineering") and is dropped.</summary>
    public CategoryPath(string? main, string? sub)
    {
        _main = main;
        _sub = sub != null && main != null && string.Equals(sub.Trim(), main.Trim(), StringComparison.OrdinalIgnoreCase) ? "" : sub;
    }

    public string Main { get => _main ?? ""; init => _main = value; }
    public string Sub { get => _sub ?? ""; init => _sub = value; }
    public bool Equals(CategoryPath other) => Main == other.Main && Sub == other.Sub;
    public override int GetHashCode() => HashCode.Combine(Main, Sub);
    public bool IsEmpty => Main.Length == 0;
    public bool HasSub => Main.Length > 0 && Sub.Length > 0;
    public override string ToString() => Main.Length == 0 ? "" : Sub.Length == 0 ? Main : Main + KnowledgeCategories.Separator + Sub;
}

/// <summary>
/// Category hierarchy of the knowledge base: main categories with optional subcategories, saved per item key in
/// <see cref="AppSettings.BrainAnnotations"/> and defined (with descriptions) in <see cref="AppSettings.KnowledgeCategories"/>.
/// Inside dialogs and classifiers a category is passed as one path string, so existing string-based code keeps working.
/// </summary>
public static class KnowledgeCategories
{
    public const string Separator = " › ";
    public const int MaxLength = 80;
    public const int MaxDescriptionLength = 300;

    private static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;

    // ---- Paths ---------------------------------------------------------------------------------------------

    /// <summary>"Haupt › Unter", "Haupt > Unter" or "Haupt"; whitespace is collapsed.</summary>
    public static CategoryPath Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return default;
        var split = text.IndexOfAny(['›', '>']);
        var main = Clean(split < 0 ? text : text[..split]);
        var sub = split < 0 ? "" : Clean(text[(split + 1)..].Replace('›', ' ').Replace('>', ' '));
        return main.Length == 0 ? default : new CategoryPath(main, sub);
    }

    public static string Combine(string? main, string? sub) => new CategoryPath(Clean(main), Clean(main).Length == 0 ? "" : Clean(sub)).ToString();

    public static string MainOf(string? path) => Parse(path).Main;

    public static CategoryPath Of(BrainAnnotation? a)
    {
        var main = Clean(a?.Category);
        return main.Length == 0 ? default : new CategoryPath(main, Clean(a!.Subcategory));
    }

    public static string? Validate(CategoryPath path)
    {
        if (path.Main.Length == 0) return "Bitte eine Hauptkategorie wählen oder eingeben.";
        if (path.Main.Length > MaxLength) return $"Die Hauptkategorie darf höchstens {MaxLength} Zeichen enthalten.";
        if (path.Sub.Length > MaxLength) return $"Die Unterkategorie darf höchstens {MaxLength} Zeichen enthalten.";
        return null;
    }

    public static bool SameMain(CategoryPath a, CategoryPath b) => Cmp.Equals(a.Main, b.Main);
    public static bool Same(CategoryPath a, CategoryPath b) => Cmp.Equals(a.Main, b.Main) && Cmp.Equals(a.Sub, b.Sub);

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

    /// <summary>All main categories and all "Haupt › Unter" paths.</summary>
    public static List<string> Paths(AppSettings s)
        => Mains(s).SelectMany(m => new[] { m }.Concat(Subs(s, m).Select(x => Combine(m, x)))).ToList();

    public static KnowledgeCategory? Find(AppSettings s, string main)
        => (s.KnowledgeCategories ?? []).FirstOrDefault(c => Cmp.Equals(Clean(c.Name), Clean(main)));

    public static string Description(AppSettings s, CategoryPath path)
    {
        var main = Find(s, path.Main);
        if (main == null) return "";
        if (!path.HasSub) return main.Description.Trim();
        return main.Subcategories.FirstOrDefault(x => Cmp.Equals(Clean(x.Name), path.Sub))?.Description.Trim() ?? "";
    }

    /// <summary>Path to description for every described category (E5 compares names together with descriptions).</summary>
    public static Dictionary<string, string> Descriptions(AppSettings s)
    {
        var result = new Dictionary<string, string>(Cmp);
        foreach (var c in s.KnowledgeCategories ?? [])
        {
            if (Clean(c.Name).Length == 0) continue;
            if (c.Description.Trim().Length > 0) result[Clean(c.Name)] = c.Description.Trim();
            foreach (var sub in c.Subcategories.Where(x => Clean(x.Name).Length > 0 && x.Description.Trim().Length > 0))
                result[Combine(c.Name, sub.Name)] = sub.Description.Trim();
        }
        return result;
    }

    /// <summary>Adds the category (and subcategory) to the definitions when missing.</summary>
    public static void Define(AppSettings s, CategoryPath path)
    {
        if (path.IsEmpty) return;
        s.KnowledgeCategories ??= [];
        var main = Find(s, path.Main);
        if (main == null) s.KnowledgeCategories.Add(main = new KnowledgeCategory { Name = path.Main });
        if (path.HasSub && !main.Subcategories.Any(x => Cmp.Equals(Clean(x.Name), path.Sub)))
            main.Subcategories.Add(new KnowledgeSubcategory { Name = path.Sub });
    }

    public static void SetDescription(AppSettings s, CategoryPath path, string description)
    {
        Define(s, path);
        var text = Clean(description);
        if (text.Length > MaxDescriptionLength) text = text[..MaxDescriptionLength];
        var main = Find(s, path.Main)!;
        if (!path.HasSub) main.Description = text;
        else main.Subcategories.First(x => Cmp.Equals(Clean(x.Name), path.Sub)).Description = text;
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
            else s.BrainAnnotations[key] = new BrainAnnotation(name, path.Main, path.Sub);
            changed.Add(key);
        }
        return changed;
    }

    /// <summary>
    /// A category saved from a path; a bare main category keeps the existing subcategory when the main category is
    /// unchanged (dialogs that only know main categories must not drop subcategories).
    /// </summary>
    public static BrainAnnotation Annotation(BrainAnnotation? old, string name, string pathText)
    {
        var path = Parse(pathText);
        var previous = Of(old);
        if (!path.HasSub && SameMain(path, previous)) path = previous;
        return new BrainAnnotation(name, path.Main, path.Sub);
    }

    /// <summary>
    /// Renames a main category (<paramref name="from"/> without sub) or moves a subcategory; renaming onto an existing
    /// name merges. Returns the number of entries that changed.
    /// </summary>
    public static int Rename(AppSettings s, CategoryPath from, CategoryPath to)
    {
        if (from.IsEmpty || to.IsEmpty) return 0;
        s.BrainAnnotations ??= [];
        s.KnowledgeCategories ??= [];
        var count = 0;
        foreach (var (key, a) in s.BrainAnnotations.ToList())
        {
            var p = Of(a);
            if (!SameMain(p, from)) continue;
            CategoryPath target;
            if (!from.HasSub) target = new CategoryPath(to.Main, to.HasSub ? to.Sub : p.Sub);
            else if (Cmp.Equals(p.Sub, from.Sub)) target = to;
            else continue;
            if (Same(p, target)) continue;
            s.BrainAnnotations[key] = a with { Category = target.Main, Subcategory = target.Sub };
            count++;
        }

        var source = Find(s, from.Main);
        if (!from.HasSub)
        {
            if (source != null)
            {
                s.KnowledgeCategories.Remove(source);
                var target = Find(s, to.Main);
                if (target == null)
                {
                    source.Name = to.Main;
                    s.KnowledgeCategories.Add(target = source);
                }
                else
                {
                    if (target.Description.Length == 0) target.Description = source.Description;
                    foreach (var sub in source.Subcategories.Where(x => !target.Subcategories.Any(t => Cmp.Equals(t.Name, x.Name))))
                        target.Subcategories.Add(sub);
                }
                if (to.HasSub) Define(s, to);
            }
            else Define(s, to);
        }
        else
        {
            var old = source?.Subcategories.FirstOrDefault(x => Cmp.Equals(Clean(x.Name), from.Sub));
            if (old != null) source!.Subcategories.Remove(old);
            Define(s, to);
            if (old != null && to.HasSub && Description(s, to).Length == 0) SetDescription(s, to, old.Description);
        }
        return count;
    }

    /// <summary>Deletes a main category (all its entries lose their category) or a subcategory (entries keep the main category).</summary>
    public static int Delete(AppSettings s, CategoryPath path)
    {
        if (path.IsEmpty) return 0;
        s.BrainAnnotations ??= [];
        var count = 0;
        foreach (var (key, a) in s.BrainAnnotations.ToList())
        {
            var p = Of(a);
            if (!SameMain(p, path) || (path.HasSub && !Cmp.Equals(p.Sub, path.Sub))) continue;
            var target = path.HasSub ? p with { Sub = "" } : default;
            if (target.IsEmpty && a.Name.Length == 0) s.BrainAnnotations.Remove(key);
            else s.BrainAnnotations[key] = a with { Category = target.Main, Subcategory = target.Sub };
            count++;
        }
        var main = Find(s, path.Main);
        if (main != null)
        {
            if (!path.HasSub) s.KnowledgeCategories.Remove(main);
            else main.Subcategories.RemoveAll(x => Cmp.Equals(Clean(x.Name), path.Sub));
        }
        return count;
    }

    /// <summary>Number of saved entries per main category and per path.</summary>
    public static Dictionary<string, int> Counts(AppSettings s)
    {
        var result = new Dictionary<string, int>(Cmp);
        // A whole recording is counted through its snapshots.
        foreach (var p in (s.BrainAnnotations ?? []).Where(a => !IsRecordingFileKey(a.Key)).Select(a => Of(a.Value)).Where(p => !p.IsEmpty))
        {
            result[p.Main] = result.GetValueOrDefault(p.Main) + 1;
            if (p.HasSub) result[p.ToString()] = result.GetValueOrDefault(p.ToString()) + 1;
        }
        return result;
    }
}

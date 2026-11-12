using System.IO;
using System.Windows;
using System.Windows.Controls;
using Merkautomat.Services;
using MessageBox = Merkautomat.Views.AppMessageBox;

namespace Merkautomat.Views;

/// <summary>Manual main categories and subcategories in the library: display, filter, assign and manage.</summary>
public partial class KnowledgeBaseWindow
{
    private const string FilterAll = "";
    private const string FilterNone = "\u0001";
    private bool _categoryFilterFilling;
    private string _categoryFilter = FilterAll;

    private bool CategoryFilterActive => _categoryFilter != FilterAll;

    /// <summary>Category of a library item; a recording uses its own category, else the most frequent of its snapshots.</summary>
    private CategoryPath CategoryOf(KbItem item)
    {
        var annotations = _app.CurrentSettings.BrainAnnotations;
        if (annotations == null || annotations.Count == 0) return default;
        if (item.ArticleKind != KbArticleKind.Recording) return KnowledgeCategories.Effective(annotations, LibraryKey(item));
        var file = KnowledgeCategories.RecordingKey(Path.GetFileName(item.FilePath));
        var own = KnowledgeCategories.Effective(annotations, file);
        if (!own.IsEmpty) return own;
        var prefix = file + "#";
        return annotations.Where(a => a.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(a => KnowledgeCategories.Of(a.Value)).Where(p => !p.IsEmpty)
            .GroupBy(p => p.ToString(), StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count())
            .Select(g => g.First()).FirstOrDefault();
    }

    /// <summary>Keys to save for a library item: a recording gets its own key plus every indexed snapshot.</summary>
    private List<string> CategoryKeys(KbItem item, IReadOnlyCollection<string> indexed)
    {
        if (item.ArticleKind != KbArticleKind.Recording) return [LibraryKey(item)];
        var file = KnowledgeCategories.RecordingKey(Path.GetFileName(item.FilePath));
        var prefix = file + "#";
        return [file, .. indexed.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))];
    }

    private void UpdateCategoryLabels()
    {
        foreach (var item in AllItems()) item.CategoryLabel = CategoryOf(item).ToString();
    }

    /// <summary>"Alle", "Ohne Kategorie", then every main category with its sub- and fine categories, with counts in the library.</summary>
    private void FillCategoryFilter()
    {
        var items = AllItems().ToList();
        var paths = items.Select(i => KnowledgeCategories.Parse(i.CategoryLabel)).ToList();
        var settings = _app.CurrentSettings;
        var entries = new List<ComboBoxItem>
        {
            new() { Content = "Alle Kategorien", Tag = FilterAll },
            new() { Content = $"Ohne Kategorie ({paths.Count(p => p.IsEmpty)})", Tag = FilterNone },
        };
        foreach (var main in KnowledgeCategories.Mains(settings))
        {
            var count = paths.Count(p => string.Equals(p.Main, main, StringComparison.OrdinalIgnoreCase));
            entries.Add(new ComboBoxItem { Content = $"{main} ({count})", Tag = main, FontWeight = FontWeights.SemiBold });
            foreach (var sub in KnowledgeCategories.Subs(settings, main))
            {
                var path = new CategoryPath(main, sub);
                entries.Add(new ComboBoxItem
                {
                    Content = $"    › {sub} ({paths.Count(p => KnowledgeCategories.IsWithin(p, path))})",
                    Tag = path.ToString(),
                });
                foreach (var fine in KnowledgeCategories.Fines(settings, main, sub))
                {
                    var finePath = new CategoryPath(main, sub, fine);
                    entries.Add(new ComboBoxItem
                    {
                        Content = $"        › {fine} ({paths.Count(p => KnowledgeCategories.Same(p, finePath))})",
                        Tag = finePath.ToString(),
                    });
                }
            }
        }
        _categoryFilterFilling = true;
        CategoryFilterBox.ItemsSource = entries;
        var selected = entries.FirstOrDefault(e => string.Equals((string)e.Tag, _categoryFilter, StringComparison.OrdinalIgnoreCase)) ?? entries[0];
        CategoryFilterBox.SelectedItem = selected;
        _categoryFilter = (string)selected.Tag;
        _categoryFilterFilling = false;
    }

    private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_categoryFilterFilling || CategoryFilterBox.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        _categoryFilter = tag;
        _notesView?.Refresh();
        _recordingsView?.Refresh();
        _clipboardView?.Refresh();
        _knowledgeFolderView?.Refresh();
        UpdateListState();
    }

    /// <summary>A category also matches everything below it.</summary>
    private bool CategoryFilterMatches(KbItem item)
    {
        if (_categoryFilter == FilterAll) return true;
        var path = KnowledgeCategories.Parse(item.CategoryLabel);
        if (_categoryFilter == FilterNone) return path.IsEmpty;
        var filter = KnowledgeCategories.Parse(_categoryFilter);
        return KnowledgeCategories.IsWithin(path, filter);
    }

    private void AssignCategory_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedItems().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Bitte links Einträge auswählen (Strg oder Umschalt für mehrere), dann Kategorie festlegen.", AppNames.KnowledgeBase);
            return;
        }
        var current = selected.Select(CategoryOf).ToList();
        var distinct = current.Select(p => p.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var initial = distinct.Count == 1 ? current[0] : default;
        var categorized = current.Count(p => !p.IsEmpty);
        var scope = SelectionBreakdown(selected) + "."
            + (distinct.Count > 1 ? $" Sie haben unterschiedliche Kategorien; die gewählte gilt danach für alle." : "")
            + (selected.Any(i => i.ArticleKind == KbArticleKind.Recording) ? " Bildschirmaufnahmen erhalten die Kategorie samt allen Bildwechseln." : "");
        var dialog = new AssignCategoryDialog(_app.CurrentSettings, scope, initial, categorized > 0) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var indexed = _app.Semantic.Snapshot().Select(c => c.Chunk.ItemKey).ToHashSet();
        var keys = selected.SelectMany(i => CategoryKeys(i, indexed)).Distinct().ToList();
        if (SaveCategories([(keys, dialog.Result)], "Kategorie speichern") is not { } changed) return;
        StatusLabel.Text = dialog.Result.IsEmpty
            ? $"Kategorie bei {selected.Count} Einträgen entfernt."
            : $"{selected.Count} Einträge sind jetzt „{dialog.Result}“ zugeordnet" + (changed.Count == 0 ? " (unverändert)." : ".");
    }

    /// <summary>
    /// Saves category assignments like a manual choice and refreshes everything that shows categories. On a write
    /// error assignments and definitions return to their previous state; the definitions are copied deeply, because
    /// new levels are added inside existing definitions. Returns the keys that changed, or null when saving failed.
    /// </summary>
    private List<string>? SaveCategories(IEnumerable<(IReadOnlyCollection<string> Keys, CategoryPath Path)> assignments, string attempted)
    {
        var settings = _app.CurrentSettings;
        var annotations = new Dictionary<string, Models.BrainAnnotation>(settings.BrainAnnotations ?? []);
        var definitions = System.Text.Json.JsonSerializer.Serialize(settings.KnowledgeCategories ?? []);
        var changed = new List<string>();
        foreach (var (keys, path) in assignments) changed.AddRange(KnowledgeCategories.Assign(settings, keys, path));
        try { _app.SaveSettings(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.LogError(attempted, ex);
            settings.BrainAnnotations = annotations;
            settings.KnowledgeCategories = System.Text.Json.JsonSerializer.Deserialize<List<Models.KnowledgeCategory>>(definitions) ?? [];
            MessageBox.Show(this, "Die Kategorie konnte nicht gespeichert werden: " + ex.Message, AppNames.KnowledgeBase,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        CategoriesChanged();
        MyKnowledgeView.Reload();
        return changed;
    }

    private void ManageCategories_Click(object sender, RoutedEventArgs e)
    {
        var first = SelectedItems().Select(CategoryOf).FirstOrDefault(p => !p.IsEmpty);
        var dialog = new CategoryManagerDialog(_app.CurrentSettings, _app.SaveSettings, first) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Changed) CategoriesChanged();
    }

    /// <summary>After any category change: library labels, filter, header and the Brain (category view and links).</summary>
    private void CategoriesChanged(bool rebuildBrain = true)
    {
        _app.RefreshPreparedRefinement();
        UpdateCategoryLabels();
        FillCategoryFilter();
        _notesView?.Refresh();
        _recordingsView?.Refresh();
        _clipboardView?.Refresh();
        UpdateListState();
        if (_view == KbView.Articles && SelectedItems().ToList() is [var single])
            ContentSubtitle.Text = single.Subtitle + (single.CategoryLabel.Length > 0 ? " · Kategorie: " + single.CategoryLabel : "");
        if (rebuildBrain && _brain != null) RebuildBrain(keepLayout: true);
    }
}

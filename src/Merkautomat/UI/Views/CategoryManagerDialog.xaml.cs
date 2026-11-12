using System.IO;
using System.Windows;
using System.Windows.Controls;
using Merkautomat.Models;
using Merkautomat.Services;
using MessageBox = Merkautomat.Views.AppMessageBox;

namespace Merkautomat.Views;

/// <summary>
/// Creates, describes, renames, merges, moves and deletes categories on all three levels. Every change is saved at
/// once; a failed save restores the previous state.
/// </summary>
public partial class CategoryManagerDialog : Window
{
    public sealed record CategoryRow(CategoryPath Path, string Label, string Meta, Thickness Indent, FontWeight Weight);

    private readonly AppSettings _settings;
    private readonly Action _save;
    private CategoryPath _selected;
    private bool _filling;

    public bool Changed { get; private set; }

    public CategoryManagerDialog(AppSettings settings, Action save, CategoryPath select = default)
    {
        _settings = settings;
        _save = save;
        InitializeComponent();
        EditorChrome.ApplyTheme(this);
        SourceInitialized += (_, _) => EditorChrome.ApplyFrame(this);
        Fill(select);
    }

    private void Fill(CategoryPath select)
    {
        var counts = KnowledgeCategories.Counts(_settings);
        var rows = new List<CategoryRow>();
        foreach (var main in KnowledgeCategories.Mains(_settings))
        {
            var subs = KnowledgeCategories.Subs(_settings, main);
            var n = counts.GetValueOrDefault(main);
            var described = KnowledgeCategories.Description(_settings, new CategoryPath(main, "")).Length > 0;
            rows.Add(new CategoryRow(new CategoryPath(main, ""), main,
                $"{n} {(n == 1 ? "Eintrag" : "Einträge")}" + (subs.Count > 0 ? $" · {subs.Count} Unterkategorien" : "") + (described ? " · beschrieben" : ""),
                new Thickness(0), FontWeights.SemiBold));
            foreach (var sub in subs)
            {
                var path = new CategoryPath(main, sub);
                var fines = KnowledgeCategories.Fines(_settings, main, sub);
                var m = counts.GetValueOrDefault(path.ToString());
                rows.Add(new CategoryRow(path, "› " + sub, $"{m} {(m == 1 ? "Eintrag" : "Einträge")}"
                    + (fines.Count > 0 ? $" · {fines.Count} Feinkategorien" : "")
                    + (KnowledgeCategories.Description(_settings, path).Length > 0 ? " · beschrieben" : ""), new Thickness(18, 0, 0, 0), FontWeights.Normal));
                foreach (var fine in fines)
                {
                    var finePath = new CategoryPath(main, sub, fine);
                    var f = counts.GetValueOrDefault(finePath.ToString());
                    rows.Add(new CategoryRow(finePath, "› " + fine, $"{f} {(f == 1 ? "Eintrag" : "Einträge")}"
                        + (KnowledgeCategories.Description(_settings, finePath).Length > 0 ? " · beschrieben" : ""), new Thickness(36, 0, 0, 0), FontWeights.Normal));
                }
            }
        }
        _filling = true;
        CategoryList.ItemsSource = rows;
        CategoryList.SelectedItem = rows.FirstOrDefault(r => KnowledgeCategories.Same(r.Path, select)) ?? rows.FirstOrDefault();
        _filling = false;
        ShowSelected();
        Feedback.Text = rows.Count == 0 ? "Noch keine Kategorien. Mit „Neue Hauptkategorie“ anlegen oder in der Bibliothek Einträgen eine Kategorie geben." : Feedback.Text;
    }

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_filling) ShowSelected();
    }

    private void ShowSelected()
    {
        ErrorText.Visibility = Visibility.Collapsed;
        var row = CategoryList.SelectedItem as CategoryRow;
        _selected = row?.Path ?? default;
        EditorPanel.IsEnabled = row != null;
        NewSubButton.IsEnabled = row != null;
        if (row == null)
        {
            EditorHeading.Text = "Keine Kategorie ausgewählt";
            EditorInfo.Text = "";
            NameBox.Text = DescriptionBox.Text = ParentBox.Text = "";
            return;
        }
        var count = KnowledgeCategories.Counts(_settings).GetValueOrDefault(row.Path.ToString());
        EditorHeading.Text = LevelName(row.Path.Depth);
        EditorInfo.Text = (row.Path.HasSub ? $"Unter „{row.Path.Parent}“. " : "") + $"{count} {(count == 1 ? "Eintrag" : "Einträge")} zugeordnet.";
        NameBox.Text = row.Path.Leaf;
        DescriptionBox.Text = KnowledgeCategories.Description(_settings, row.Path);
        ParentPanel.Visibility = row.Path.HasSub ? Visibility.Visible : Visibility.Collapsed;
        // A subcategory can move to another main category, a fine category to another subcategory.
        ParentLabel.Text = row.Path.HasFine ? "Gehört zur Unterkategorie" : "Gehört zur Hauptkategorie";
        ParentBox.ItemsSource = row.Path.HasFine
            ? KnowledgeCategories.Paths(_settings).Where(p => KnowledgeCategories.Parse(p).Depth == 2).ToList()
            : KnowledgeCategories.Mains(_settings);
        ParentBox.Text = row.Path.Parent.ToString();
        NewSubButton.Content = row.Path.Depth == 1 ? "Neue Unterkategorie" : "Neue Feinkategorie";
    }

    private static string LevelName(int depth) => depth switch { 1 => "Hauptkategorie", 2 => "Unterkategorie", _ => "Feinkategorie" };

    private void NewMain_Click(object sender, RoutedEventArgs e)
    {
        var name = Unique(KnowledgeCategories.Mains(_settings), "Neue Kategorie");
        var path = new CategoryPath(name, "");
        if (Apply(() => KnowledgeCategories.Define(_settings, path), $"„{name}“ angelegt. Namen und Beschreibung rechts anpassen.")) Fill(path);
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void NewSub_Click(object sender, RoutedEventArgs e)
    {
        if (_selected.IsEmpty) return;
        // Below a main category a subcategory, below a subcategory (or next to a fine category) a fine category.
        var parent = _selected.HasFine ? _selected.Parent : _selected;
        var siblings = parent.HasSub ? KnowledgeCategories.Fines(_settings, parent.Main, parent.Sub) : KnowledgeCategories.Subs(_settings, parent.Main);
        var name = Unique(siblings, parent.HasSub ? "Neue Feinkategorie" : "Neue Unterkategorie");
        var path = KnowledgeCategories.Build(parent.Levels.Append(name));
        if (Apply(() => KnowledgeCategories.Define(_settings, path), $"„{path}“ angelegt. Namen und Beschreibung rechts anpassen.")) Fill(path);
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_selected.IsEmpty) return;
        var from = _selected;
        var name = KnowledgeCategories.Parse(NameBox.Text).Main;
        var parent = KnowledgeCategories.Parse(ParentBox.Text);
        var to = from.HasSub ? KnowledgeCategories.Build(parent.Levels.Append(name)) : new CategoryPath(name, "");
        var error = KnowledgeCategories.Validate(to);
        if (error == null && from.HasSub && parent.Depth != from.Depth - 1)
            error = from.HasFine ? "Bitte eine Unterkategorie (Haupt › Unter) als übergeordnete Kategorie wählen." : "Bitte eine Hauptkategorie wählen.";
        if (error == null && to.Depth != from.Depth) error = $"Bitte einen Namen für die {LevelName(from.Depth)} eingeben.";
        if (error != null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        var renamed = !KnowledgeCategories.Same(from, to) || from.ToString() != to.ToString();
        var merges = renamed && !KnowledgeCategories.Same(from, to) && Exists(to);
        if (merges && MessageBox.Show(this, $"„{to}“ gibt es schon. „{from}“ damit zusammenführen? Alle Einträge von „{from}“ gehören danach zu „{to}“.",
                AppNames.KnowledgeBase, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        var description = DescriptionBox.Text;
        var moved = 0;
        if (Apply(() =>
            {
                if (renamed) moved = KnowledgeCategories.Rename(_settings, from, to);
                KnowledgeCategories.SetDescription(_settings, to, description);
            }, renamed ? $"„{from}“ heißt jetzt „{to}“{(moved > 0 ? $", {moved} Einträge angepasst" : "")}." : $"„{to}“ gespeichert."))
            Fill(to);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selected.IsEmpty) return;
        var path = _selected;
        var count = KnowledgeCategories.Counts(_settings).GetValueOrDefault(path.ToString());
        var below = path.Depth switch
        {
            1 => KnowledgeCategories.Subs(_settings, path.Main).Count,
            2 => KnowledgeCategories.Fines(_settings, path.Main, path.Sub).Count,
            _ => 0,
        };
        var message = path.HasSub
            ? $"{LevelName(path.Depth)} „{path}“ löschen?" + (below > 0 ? $" Ihre {below} Feinkategorien werden ebenfalls gelöscht." : "")
              + (count > 0 ? $"\n\n{count} Einträge behalten „{path.Parent}“." : "")
            : $"Hauptkategorie „{path.Main}“ löschen?" + (below > 0 ? $" Ihre {below} Unterkategorien und deren Feinkategorien werden ebenfalls gelöscht." : "")
              + (count > 0 ? $"\n\n{count} Einträge verlieren ihre Kategorie. Die Einträge selbst und ihre Titel bleiben erhalten." : "");
        if (MessageBox.Show(this, message, AppNames.KnowledgeBase, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (Apply(() => KnowledgeCategories.Delete(_settings, path), $"„{path}“ gelöscht."))
            Fill(path.Parent);
    }

    private bool Exists(CategoryPath path) => path.Depth switch
    {
        1 => KnowledgeCategories.Mains(_settings).Any(m => string.Equals(m, path.Main, StringComparison.OrdinalIgnoreCase)),
        2 => KnowledgeCategories.Subs(_settings, path.Main).Any(s => string.Equals(s, path.Sub, StringComparison.OrdinalIgnoreCase)),
        _ => KnowledgeCategories.Fines(_settings, path.Main, path.Sub).Any(f => string.Equals(f, path.Fine, StringComparison.OrdinalIgnoreCase)),
    };

    /// <summary>Runs a change and saves; on a write error the previous categories and assignments come back.</summary>
    private bool Apply(Action change, string done)
    {
        var annotations = new Dictionary<string, BrainAnnotation>(_settings.BrainAnnotations ?? []);
        var definitions = System.Text.Json.JsonSerializer.Serialize(_settings.KnowledgeCategories ?? []);
        try
        {
            change();
            _save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.LogCrash(ex);
            _settings.BrainAnnotations = annotations;
            _settings.KnowledgeCategories = System.Text.Json.JsonSerializer.Deserialize<List<KnowledgeCategory>>(definitions) ?? [];
            Feedback.Text = "Speichern fehlgeschlagen: " + ex.Message;
            return false;
        }
        Changed = true;
        Feedback.Text = done;
        return true;
    }

    private static string Unique(IReadOnlyCollection<string> existing, string baseName)
    {
        var name = baseName;
        for (var i = 2; existing.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)); i++) name = $"{baseName} {i}";
        return name;
    }
}

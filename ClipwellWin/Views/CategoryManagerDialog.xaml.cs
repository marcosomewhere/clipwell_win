using System.IO;
using System.Windows;
using System.Windows.Controls;
using ClipwellWin.Models;
using ClipwellWin.Services;
using MessageBox = ClipwellWin.Views.AppMessageBox;

namespace ClipwellWin.Views;

/// <summary>
/// Creates, describes, renames, merges, moves and deletes main categories and subcategories. Every change is saved at
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
                var m = counts.GetValueOrDefault(path.ToString());
                rows.Add(new CategoryRow(path, "› " + sub, $"{m} {(m == 1 ? "Eintrag" : "Einträge")}"
                    + (KnowledgeCategories.Description(_settings, path).Length > 0 ? " · beschrieben" : ""), new Thickness(18, 0, 0, 0), FontWeights.Normal));
            }
        }
        _filling = true;
        CategoryList.ItemsSource = rows;
        CategoryList.SelectedItem = rows.FirstOrDefault(r => KnowledgeCategories.Same(r.Path, select)) ?? rows.FirstOrDefault();
        _filling = false;
        ParentBox.ItemsSource = KnowledgeCategories.Mains(_settings);
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
        EditorHeading.Text = row.Path.HasSub ? "Unterkategorie" : "Hauptkategorie";
        EditorInfo.Text = (row.Path.HasSub ? $"Unter „{row.Path.Main}“. " : "") + $"{count} {(count == 1 ? "Eintrag" : "Einträge")} zugeordnet.";
        NameBox.Text = row.Path.HasSub ? row.Path.Sub : row.Path.Main;
        DescriptionBox.Text = KnowledgeCategories.Description(_settings, row.Path);
        ParentPanel.Visibility = row.Path.HasSub ? Visibility.Visible : Visibility.Collapsed;
        ParentBox.Text = row.Path.Main;
    }

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
        var main = _selected.Main;
        var name = Unique(KnowledgeCategories.Subs(_settings, main), "Neue Unterkategorie");
        var path = new CategoryPath(main, name);
        if (Apply(() => KnowledgeCategories.Define(_settings, path), $"„{path}“ angelegt. Namen und Beschreibung rechts anpassen.")) Fill(path);
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_selected.IsEmpty) return;
        var from = _selected;
        var name = KnowledgeCategories.Parse(NameBox.Text).Main;
        var to = from.HasSub ? new CategoryPath(KnowledgeCategories.Parse(ParentBox.Text).Main, name) : new CategoryPath(name, "");
        var error = KnowledgeCategories.Validate(to);
        if (error == null && from.HasSub && !to.HasSub) error = "Bitte einen Namen für die Unterkategorie eingeben.";
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
        var subs = path.HasSub ? 0 : KnowledgeCategories.Subs(_settings, path.Main).Count;
        var message = path.HasSub
            ? $"Unterkategorie „{path}“ löschen?" + (count > 0 ? $"\n\n{count} Einträge behalten die Hauptkategorie „{path.Main}“." : "")
            : $"Hauptkategorie „{path.Main}“ löschen?" + (subs > 0 ? $" Ihre {subs} Unterkategorien werden ebenfalls gelöscht." : "")
              + (count > 0 ? $"\n\n{count} Einträge verlieren ihre Kategorie. Die Einträge selbst und ihre Titel bleiben erhalten." : "");
        if (MessageBox.Show(this, message, AppNames.KnowledgeBase, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (Apply(() => KnowledgeCategories.Delete(_settings, path), $"„{path}“ gelöscht."))
            Fill(path.HasSub ? path with { Sub = "" } : default);
    }

    private bool Exists(CategoryPath path) => path.HasSub
        ? KnowledgeCategories.Subs(_settings, path.Main).Any(s => string.Equals(s, path.Sub, StringComparison.OrdinalIgnoreCase))
        : KnowledgeCategories.Mains(_settings).Any(m => string.Equals(m, path.Main, StringComparison.OrdinalIgnoreCase));

    /// <summary>Runs a change and saves; on a write error the previous categories and assignments come back.</summary>
    private bool Apply(Action change, string done)
    {
        var annotations = new Dictionary<string, BrainAnnotation>(_settings.BrainAnnotations ?? []);
        var definitions = (_settings.KnowledgeCategories ?? []).Select(c => new KnowledgeCategory
        {
            Name = c.Name, Description = c.Description,
            Subcategories = c.Subcategories.Select(s => new KnowledgeSubcategory { Name = s.Name, Description = s.Description }).ToList(),
        }).ToList();
        try
        {
            change();
            _save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.LogCrash(ex);
            _settings.BrainAnnotations = annotations;
            _settings.KnowledgeCategories = definitions;
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

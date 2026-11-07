using System.Windows;
using System.Windows.Controls;
using ClipwellWin.Models;
using ClipwellWin.Services;
using TextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

namespace ClipwellWin.Views;

/// <summary>Sets main category and optional subcategory for the library selection, or removes it.</summary>
public partial class AssignCategoryDialog : Window
{
    private readonly AppSettings _settings;

    /// <summary>Chosen path; empty when the category is to be removed.</summary>
    public CategoryPath Result { get; private set; }

    public AssignCategoryDialog(AppSettings settings, string scope, CategoryPath initial, bool anyCategorized)
    {
        _settings = settings;
        InitializeComponent();
        EditorChrome.ApplyTheme(this);
        SourceInitialized += (_, _) => EditorChrome.ApplyFrame(this);
        ScopeText.Text = scope;
        MainBox.ItemsSource = KnowledgeCategories.Mains(settings);
        MainBox.Text = initial.Main;
        SubBox.ItemsSource = KnowledgeCategories.Subs(settings, initial.Main);
        SubBox.Text = initial.Sub;
        RemoveButton.IsEnabled = anyCategorized;
        MainBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => MainChanged()));
        SubBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => ShowDescriptions()));
        ShowDescriptions();
        Loaded += (_, _) => MainBox.Focus();
    }

    private void MainChanged()
    {
        var sub = SubBox.Text;
        SubBox.ItemsSource = KnowledgeCategories.Subs(_settings, MainBox.Text);
        SubBox.Text = sub;
        ShowDescriptions();
    }

    private void ShowDescriptions()
    {
        var path = new CategoryPath(KnowledgeCategories.Parse(MainBox.Text).Main, KnowledgeCategories.Parse(SubBox.Text).Main);
        Show(MainDescription, path.IsEmpty ? "" : KnowledgeCategories.Mains(_settings)
            .All(m => !string.Equals(m, path.Main, StringComparison.OrdinalIgnoreCase)) ? "Neue Hauptkategorie." : KnowledgeCategories.Description(_settings, path with { Sub = "" }));
        Show(SubDescription, !path.HasSub ? "" : KnowledgeCategories.Subs(_settings, path.Main)
            .All(s => !string.Equals(s, path.Sub, StringComparison.OrdinalIgnoreCase)) ? "Neue Unterkategorie." : KnowledgeCategories.Description(_settings, path));
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private static void Show(TextBlock block, string text)
    {
        block.Text = text;
        block.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Assign_Click(object sender, RoutedEventArgs e)
    {
        // "Haupt › Unter" typed into the main field is accepted as well.
        var typed = KnowledgeCategories.Parse(MainBox.Text);
        var sub = KnowledgeCategories.Parse(SubBox.Text).Main;
        var path = new CategoryPath(typed.Main, sub.Length > 0 ? sub : typed.Sub);
        if (KnowledgeCategories.Validate(path) is { } error)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        Result = path;
        DialogResult = true;
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        Result = default;
        DialogResult = true;
    }
}

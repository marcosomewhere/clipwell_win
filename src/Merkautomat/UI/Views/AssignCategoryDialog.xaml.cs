using System.Windows;
using System.Windows.Controls;
using Merkautomat.Models;
using Merkautomat.Services;
using TextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

namespace Merkautomat.Views;

/// <summary>Sets main category, optional subcategory and fine category ("Unter › Fein") for the selection, or removes it.</summary>
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
        SubBox.ItemsSource = KnowledgeCategories.SubPaths(settings, initial.Main);
        SubBox.Text = initial.HasFine ? initial.Sub + KnowledgeCategories.Separator + initial.Fine : initial.Sub;
        RemoveButton.IsEnabled = anyCategorized;
        MainBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => MainChanged()));
        SubBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => ShowDescriptions()));
        ShowDescriptions();
        Loaded += (_, _) => MainBox.Focus();
    }

    private void MainChanged()
    {
        var sub = SubBox.Text;
        SubBox.ItemsSource = KnowledgeCategories.SubPaths(_settings, MainBox.Text);
        SubBox.Text = sub;
        ShowDescriptions();
    }

    private void ShowDescriptions()
    {
        var path = PathFromFields();
        Show(MainDescription, path.IsEmpty ? "" : KnowledgeCategories.Mains(_settings)
            .All(m => !string.Equals(m, path.Main, StringComparison.OrdinalIgnoreCase)) ? "Neue Hauptkategorie." : KnowledgeCategories.Description(_settings, new CategoryPath(path.Main, null)));
        var lower = path.HasFine ? path.Parent : path;
        var subText = !path.HasSub ? ""
            : KnowledgeCategories.Subs(_settings, path.Main).All(s => !string.Equals(s, path.Sub, StringComparison.OrdinalIgnoreCase))
                ? "Neue Unterkategorie." : KnowledgeCategories.Description(_settings, lower);
        if (path.HasFine)
            subText = (subText.Length > 0 ? subText + "\n" : "") + (KnowledgeCategories.Fines(_settings, path.Main, path.Sub)
                .All(f => !string.Equals(f, path.Fine, StringComparison.OrdinalIgnoreCase)) ? "Neue Feinkategorie." : KnowledgeCategories.Description(_settings, path));
        Show(SubDescription, subText);
        ErrorText.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// The second field takes "Unter" or "Unter › Fein"; a whole path typed into the main field is accepted as well
    /// and is completed by the second field.
    /// </summary>
    private CategoryPath PathFromFields()
    {
        var typed = KnowledgeCategories.Parse(MainBox.Text);
        return SubBox.Text.Trim().Length > 0 ? KnowledgeCategories.Parse(KnowledgeCategories.Combine(typed.Main, SubBox.Text)) : typed;
    }

    private static void Show(TextBlock block, string text)
    {
        block.Text = text;
        block.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Assign_Click(object sender, RoutedEventArgs e)
    {
        var path = PathFromFields();
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

using System.ComponentModel;
using System.Windows;
using MessageBox = Merkautomat.Views.AppMessageBox;

namespace Merkautomat.Views;

/// <summary>Checklist of items in sections, optionally with a name; used for collections and open points.</summary>
public partial class BrainChoiceDialog : Window
{
    public sealed class Choice : INotifyPropertyChanged
    {
        private bool _isChecked;

        public required string Key { get; init; }
        public required string Title { get; init; }
        public string Detail { get; init; } = "";
        /// <summary>The state the dialog opened with, so callers can tell what the user changed.</summary>
        public bool Initial { get; init; }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public sealed record Section(string Header, IReadOnlyList<Choice> Items);

    private readonly IReadOnlyList<Section> _sections;
    private readonly bool _requireName;

    /// <param name="name">Null hides the name field.</param>
    /// <param name="removeText">Null hides the extra button on the left (e.g. "Mappe auflösen").</param>
    public BrainChoiceDialog(string heading, string intro, string? name, IReadOnlyList<Section> sections,
        string confirmText, string? removeText = null, string nameLabel = "Name")
    {
        InitializeComponent();
        EditorChrome.ApplyTheme(this);
        App.ApplyAppIcon(this);
        _sections = sections;
        foreach (var choice in sections.SelectMany(s => s.Items)) choice.IsChecked = choice.Initial;
        Title = "Merkautomat – " + heading;
        HeadingText.Text = heading;
        IntroText.Text = intro;
        _requireName = name != null;
        if (name != null)
        {
            NamePanel.Visibility = Visibility.Visible;
            NameLabel.Text = nameLabel;
            NameBox.Text = name;
            Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
        }
        SectionsList.ItemsSource = sections.Where(s => s.Items.Count > 0).ToList();
        ConfirmButton.Content = confirmText;
        if (removeText != null)
        {
            RemoveButton.Content = removeText;
            RemoveButton.Visibility = Visibility.Visible;
        }
        UpdateState();
    }

    public string NameText => NameBox.Text.Trim();
    public bool RemoveRequested { get; private set; }
    public IEnumerable<Choice> Choices => _sections.SelectMany(s => s.Items);
    public IReadOnlyList<string> CheckedKeys => Choices.Where(c => c.IsChecked).Select(c => c.Key).ToList();

    private void Window_SourceInitialized(object sender, EventArgs e) => EditorChrome.ApplyFrame(this);

    private void UpdateState()
    {
        var all = Choices.ToList();
        CountText.Text = $"{all.Count(c => c.IsChecked)} von {all.Count} markiert";
        ConfirmButton.IsEnabled = !_requireName || NameText.Length > 0;
    }

    private void Choice_Click(object sender, RoutedEventArgs e) => UpdateState();

    private void NameBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateState();

    private void SetAll(bool value)
    {
        foreach (var choice in Choices) choice.IsChecked = value;
        UpdateState();
    }

    private void CheckAll_Click(object sender, RoutedEventArgs e) => SetAll(true);

    private void CheckNone_Click(object sender, RoutedEventArgs e) => SetAll(false);

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Wirklich entfernen? Die Einträge selbst bleiben erhalten.", HeadingText.Text,
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        RemoveRequested = true;
        DialogResult = true;
    }
}

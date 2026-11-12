using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Merkautomat.Services;

namespace Merkautomat.Views;

public partial class BrainKnowledgeDialog : Window
{
    public sealed class SourceChoice
    {
        public required BrainSource Source { get; init; }
        public string Reason { get; init; } = "";
        public bool Selected { get; set; }
    }

    public sealed record SummaryChoice(string Label, string? Id, string Text, IReadOnlyList<string>? SourceKeys = null);
    private readonly List<SourceChoice> _sources;
    private HashSet<string>? _draftKeys;
    private bool _busy;
    private bool _loadingDraft;
    private bool _draftEdited;
    private bool _closed;
    private string? _activeTarget;
    private readonly Dictionary<string, (string Text, HashSet<string>? Keys, bool Edited, HashSet<string>? Selection)> _drafts = [];
    public bool SaveSummary { get; private set; }
    public string TopicTitle => TopicName.Text.Trim();
    public string TopicCategory => Category.Text.Trim();
    public string Draft => SummaryText.Text.Trim();
    public string? SummaryId => (SummaryTarget.SelectedItem as SummaryChoice)?.Id;
    public List<BrainSource> SelectedSources => _sources.Where(s => s.Selected).Select(s => s.Source).ToList();

    public BrainKnowledgeDialog(string title, string category, IEnumerable<string> categories,
        List<SourceChoice> sources, List<SummaryChoice> summaries, bool summarize = true, LocalAiService? localAi = null,
        IReadOnlyDictionary<string, string>? assignments = null)
    {
        _assignments = assignments;
        InitializeComponent();
        EditorChrome.ApplyTheme(this);
        SourceInitialized += (_, _) => EditorChrome.ApplyFrame(this);
        Closed += (_, _) => _closed = true;
        Width = Math.Min(Width, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 40);
        _sources = sources;
        TopicName.Text = title;
        Category.ItemsSource = categories.ToList();
        Category.Text = category;
        SourceList.ItemsSource = sources;
        SummaryTarget.ItemsSource = summaries;
        SummaryTarget.SelectedIndex = summarize && summaries.Count > 1 ? 1 : 0;
        KnowledgeMode.SelectedIndex = summarize ? 1 : 0;
        SourceList.SelectedIndex = sources.Count > 0 ? 0 : -1;
        UpdateSelection();
        UpdateMode();
        InitializeLocalAi(localAi);
        Loaded += (_, _) => PrepareOnOpenAsync(summarize).Forget("Zusammenfassung und Vorschlag vorbereiten");
    }

    private async Task PrepareOnOpenAsync(bool summarize)
    {
        if (summarize && SummaryText.Text.Length == 0) await GenerateAsync();
        if (!_closed && _localAi?.Ready == true) await SuggestCategoryAsync();
    }

    private void SummaryTarget_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SummaryText == null || SummaryTarget.SelectedItem is not SummaryChoice choice) return;
        if (_activeTarget != null) _drafts[_activeTarget] = (SummaryText.Text, _draftKeys, _draftEdited, SelectedSources.Select(s => s.Key).ToHashSet());
        _activeTarget = choice.Id ?? "new";
        var draft = _drafts.GetValueOrDefault(_activeTarget,
            (Text: choice.Text, Keys: choice.SourceKeys?.ToHashSet(), Edited: false, Selection: choice.SourceKeys?.ToHashSet()));
        _loadingDraft = true;
        SummaryText.Text = draft.Text;
        _loadingDraft = false;
        _draftKeys = draft.Keys;
        _draftEdited = draft.Edited;
        if (_sources != null && draft.Selection != null)
        {
            foreach (var source in _sources) source.Selected = draft.Selection.Contains(source.Source.Key);
            SourceList.Items.Refresh();
        }
        UpdateSelection();
        UpdateMode();
        Preview_Click(this, new RoutedEventArgs());
    }

    private void All_Click(object sender, RoutedEventArgs e)
    {
        foreach (var s in SourceList.Items.Cast<SourceChoice>()) s.Selected = true;
        SourceList.Items.Refresh();
        UpdateSelection();
    }

    private void None_Click(object sender, RoutedEventArgs e)
    {
        foreach (var s in _sources) s.Selected = false;
        SourceList.Items.Refresh();
        UpdateSelection();
    }

    private void SourceSelection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SourceChoice choice }) SourceList.SelectedItem = choice;
        UpdateSelection();
    }

    private void SourceFilter_Changed(object sender, TextChangedEventArgs e)
    {
        if (_sources == null) return;
        var query = SourceFilter.Text.Trim();
        CollectionViewSource.GetDefaultView(SourceList.ItemsSource).Filter = item => item is SourceChoice s
            && (s.Source.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || s.Source.Text.Contains(query, StringComparison.OrdinalIgnoreCase));
        UpdateSelection();
    }

    private void SourceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourcePreview != null) SourcePreview.Text = (SourceList.SelectedItem as SourceChoice)?.Source.Text ?? "";
    }

    private void UpdateSelection()
    {
        InvalidateAiSuggestion();
        if (_sources == null || SelectionCount == null || SaveButton == null) return;
        var count = SelectedSources.Count;
        SelectionCount.Text = $"{count} von {_sources.Count} markiert · {SourceList.Items.Count} sichtbar";
        SaveButton.IsEnabled = GenerateButton.IsEnabled = count > 0 && !_busy;
        if (_draftKeys != null && !_draftKeys.SetEquals(SelectedSources.Select(s => s.Key)))
            Feedback.Text = "Quellenauswahl geändert. Für die Zusammenfassung den Entwurf neu erstellen.";
        else Feedback.Text = count == 0 ? "Mindestens eine Quelle markieren." : $"{count} Quellen ausgewählt. Originale bleiben erhalten.";
    }

    private void KnowledgeMode_Changed(object sender, SelectionChangedEventArgs e) => KnowledgeMode_ChangedAsync(sender, e).Forget("Thema laden");

    private async Task KnowledgeMode_ChangedAsync(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != KnowledgeMode || SaveButton == null) return;
        UpdateMode();
        if (IsLoaded && KnowledgeMode.SelectedIndex == 1 && SummaryText.Text.Length == 0) await GenerateAsync();
    }

    private void UpdateMode()
    {
        if (SaveButton == null) return;
        SaveButton.Content = KnowledgeMode.SelectedIndex == 0 ? "Zuordnung speichern"
            : SummaryId == null ? "Zusammenfassung speichern" : "Zusammenfassung aktualisieren";
    }

    private void Draft_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_loadingDraft) _draftEdited = true;
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        var preview = PreviewToggle.IsChecked == true;
        if (preview)
            SummaryPreview.Document = MarkdownPreview.Build(SummaryText.Text,
                (System.Windows.Media.Brush)FindResource("EditorTextBrush"), (System.Windows.Media.Brush)FindResource("EditorTextBodyBrush"),
                TryFindResource("OnboardingAccentBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.DarkOrange,
                (System.Windows.Media.Brush)FindResource("EditorBorderBrush"));
        SummaryPreview.Visibility = preview ? Visibility.Visible : Visibility.Collapsed;
        SummaryText.Visibility = preview ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Generate_Click(object sender, RoutedEventArgs e) => Generate_ClickAsync(sender, e).Forget("Zusammenfassung erstellen");

    private async Task Generate_ClickAsync(object sender, RoutedEventArgs e)
    {
        if (_draftEdited && Merkautomat.Views.AppMessageBox.Show(this, "Den bearbeiteten Entwurf durch eine neue Zusammenfassung der Auswahl ersetzen?",
                AppNames.KnowledgeBase, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await GenerateAsync();
    }

    private async Task GenerateAsync()
    {
        if (_busy || _closed) return;
        var sources = SelectedSources;
        if (sources.Count == 0) { Feedback.Text = "Bitte mindestens einen Eintrag markieren."; return; }
        var title = TopicTitle;
        var category = TopicCategory;
        _busy = true;
        GenerateButton.IsEnabled = SaveButton.IsEnabled = false;
        SourcePanel.IsEnabled = SummaryTarget.IsEnabled = TopicName.IsEnabled = Category.IsEnabled = KnowledgeMode.IsEnabled = false;
        Feedback.Text = "Originalaussagen werden lokal ausgewählt …";
        try
        {
            var text = await Task.Run(() => BrainKnowledge.Summarize(title, category, sources));
            if (_closed) return;
            _loadingDraft = true;
            SummaryText.Text = text;
            _loadingDraft = false;
            _draftEdited = false;
            _draftKeys = sources.Select(s => s.Key).ToHashSet();
            Preview_Click(this, new RoutedEventArgs());
            Feedback.Text = $"Entwurf aus {sources.Count} Einträgen. Vor dem Speichern prüfen und bei Bedarf bearbeiten.";
        }
        catch (Exception ex)
        {
            App.LogCrash(ex);
            Feedback.Text = $"Der Entwurf konnte nicht erstellt werden: {ex.Message}";
        }
        finally
        {
            _busy = false;
            SourcePanel.IsEnabled = SummaryTarget.IsEnabled = TopicName.IsEnabled = Category.IsEnabled = KnowledgeMode.IsEnabled = true;
            GenerateButton.IsEnabled = SaveButton.IsEnabled = SelectedSources.Count > 0;
        }
    }

    private bool Validate()
    {
        if (TopicTitle.Length == 0) { Feedback.Text = "Bitte einen Themennamen eingeben."; return false; }
        if (TopicCategory.Length > 80) { Feedback.Text = "Die Kategorie darf höchstens 80 Zeichen enthalten."; return false; }
        if (SelectedSources.Count == 0) { Feedback.Text = "Bitte mindestens einen Eintrag markieren."; return false; }
        return true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!Validate()) return;
        if (KnowledgeMode.SelectedIndex == 0) { DialogResult = true; return; }
        if (Draft.Length == 0) { Feedback.Text = "Bitte zuerst einen Entwurf erstellen oder Text eingeben."; return; }
        if (SummaryId != null && SelectedSources.Any(s => s.Key == "n:" + SummaryId + ".md"))
        {
            Feedback.Text = "Die zu aktualisierende Zusammenfassung kann nicht ihre eigene Quelle sein. Bitte abwählen und den Entwurf neu erstellen.";
            return;
        }
        if (_draftKeys != null && !_draftKeys.SetEquals(SelectedSources.Select(s => s.Key)))
        {
            Feedback.Text = "Die Quellenauswahl wurde geändert. Bitte den Entwurf aus der Auswahl neu erstellen.";
            return;
        }
        SaveSummary = true;
        DialogResult = true;
    }
}

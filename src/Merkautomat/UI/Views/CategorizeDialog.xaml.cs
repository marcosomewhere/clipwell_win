using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Merkautomat.Services;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = Merkautomat.Views.AppMessageBox;

namespace Merkautomat.Views;

/// <summary>
/// Categorization pass over a set of entries with the model chosen in the Kontrollstation: one by one with
/// confirmation, or all marked entries together. Every confirmation is saved at once.
/// </summary>
public partial class CategorizeDialog : Window
{
    private readonly CategorizationQueue _queue;
    private readonly LocalAiService? _ai;
    private readonly Action<IReadOnlyList<string>, string> _persist;
    private readonly Action<LocalAiSuggestion?, string>? _feedback;
    private LocalAiSuggestion? _groupSuggestion;
    // One local model request at a time; requests for entries the user has left are dropped.
    private readonly SemaphoreSlim _slot = new(1, 1);
    private readonly CancellationTokenSource _closing = new();
    private CategorizationItem? _current;
    private bool _settingText;
    private bool _categoryEdited;
    private bool _groupEdited;
    private bool _closed;
    private bool _groupBusy;
    private enum Source { Off, E5, Granite, SelectedModel }
    private Source _source;
    private bool _sourceLoading;
    private readonly Func<IReadOnlyList<string>, Window, Task<IReadOnlyDictionary<string, string>>>? _analyze;
    private readonly System.Windows.Threading.DispatcherTimer _subTimer;
    // Subcategory suggestion for a main category the user chose; replaces the subcategories of the item suggestion.
    private (CategorizationItem Item, string Main, LocalAiSuggestion? Result)? _subSuggestion;

    public bool Changed { get; private set; }

    public CategorizeDialog(string heading, string scope, CategorizationQueue queue, LocalAiService? ai,
        Action<IReadOnlyList<string>, string> persist, bool onlyUncategorized, bool groupFirst,
        Action<LocalAiSuggestion?, string>? feedback = null,
        Func<IReadOnlyList<string>, Window, Task<IReadOnlyDictionary<string, string>>>? analyze = null,
        string? presetGroupCategory = null)
    {
        _feedback = feedback;
        _analyze = analyze;
        InitializeComponent();
        EditorChrome.ApplyTheme(this);
        SourceInitialized += (_, _) => EditorChrome.ApplyFrame(this);
        Width = Math.Min(Width, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 40);
        _queue = queue;
        _ai = ai;
        _persist = persist;
        Heading.Text = heading;
        ScopeText.Text = scope;
        OnlyUncategorized.IsChecked = onlyUncategorized;
        ItemList.ItemsSource = queue.Items;
        FillSources();
        GroupAnalyzeButton.Visibility = analyze != null ? Visibility.Visible : Visibility.Collapsed;
        ItemCategory.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) =>
        {
            if (!_settingText) _categoryEdited = true;
            RefreshSubs(ItemCategory, ItemSubcategory);
            if (!_settingText) { _subTimer?.Stop(); _subTimer?.Start(); }
        }));
        ItemSubcategory.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => { if (!_settingText) _categoryEdited = true; }));
        GroupCategory.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) =>
        {
            if (!_settingText) _groupEdited = true;
            RefreshSubs(GroupCategory, GroupSubcategory);
        }));
        GroupSubcategory.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => { if (!_settingText) _groupEdited = true; }));
        _subTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _subTimer.Tick += (_, _) => { _subTimer.Stop(); if (_current != null) _ = SuggestSubAsync(_current); };
        Closed += (_, _) => { _closed = true; _subTimer.Stop(); _closing.Cancel(); };
        RefreshCategories();
        UpdateCounts();
        GroupStatus.Text = "Markierte Einträge bekommen eine gemeinsame Kategorie."
            + (Ready ? $" Der gemeinsame Vorschlag wertet bis zu {LocalAiCategorization.MaxSources} markierte Einträge aus." : "");
        if (presetGroupCategory != null)
        {
            SetPath(GroupCategory, GroupSubcategory, presetGroupCategory);
            _groupEdited = true;
            GroupStatus.Text = $"Passende markieren und „{presetGroupCategory}“ gemeinsam zuordnen, oder einzeln prüfen.";
        }
        Loaded += (_, _) => StartAsync(groupFirst).Forget("Einordnen starten");
    }

    private async Task StartAsync(bool groupFirst)
    {
        var group = groupFirst && Ready && _queue.Items.Any(i => i.Marked) ? SuggestGroupAsync() : Task.CompletedTask;
        var first = _queue.NextOpen(-1, OnlyUncategorized.IsChecked == true);
        if (first < 0 && _queue.Items.Count > 0) first = 0;
        if (first >= 0) Select(first);
        else Feedback.Text = "Keine Einträge zum Einordnen.";
        await group;
    }

    private bool Ready => _source != Source.Off;
    private string LanguageModelName => _ai?.Model.Name ?? "Granite";
    private string SourceName => _source == Source.Granite ? LanguageModelName : _source == Source.SelectedModel ? _ai!.DisplayName : "E5";
    private bool HasSeparateSelectedModel => _ai?.Enabled == true && !_ai.UsesE5
        && (_ai.CategoryModel != null || _ai.SuggestionModel.ModelId != _ai.Model.ModelId);

    // ---- Suggestion source ------------------------------------------------------------------------------------

    /// <summary>Preselects the Kontrollstation choice; with "Aus" E5 is used when available, since it needs no download.</summary>
    private void FillSources()
    {
        _sourceLoading = true;
        var e5 = _ai?.E5Ready == true;
        var granite = _ai?.Model.IsInstalled == true;
        SourceBox.Items.Clear();
        SourceBox.Items.Add(new ComboBoxItem { Tag = Source.E5, IsEnabled = e5,
            Content = "E5 – schnell, lernt aus deinen Zuordnungen" + (e5 ? "" : " (Suchmodell fehlt)") });
        SourceBox.Items.Add(new ComboBoxItem { Tag = Source.Granite, IsEnabled = granite,
            Content = $"{LanguageModelName} – kann neue Kategorien vorschlagen" + (granite ? "" : " (nicht installiert)") });
        SourceBox.Items.Add(new ComboBoxItem { Tag = Source.Off, Content = "Aus – nur manuell" });
        if (HasSeparateSelectedModel)
            SourceBox.Items.Add(new ComboBoxItem { Tag = Source.SelectedModel, IsEnabled = _ai!.Ready,
                Content = _ai.DisplayName + (_ai.UsesCategoryOnly ? " – vorhandene Kategorien" : " – Kategorie und Titel") + (_ai.Ready ? "" : " (nicht installiert)") });
        var preferred = _ai?.Ready == true ? (_ai.UsesE5 ? Source.E5 : HasSeparateSelectedModel ? Source.SelectedModel : Source.Granite) : e5 ? Source.E5 : Source.Off;
        SourceBox.SelectedItem = SourceBox.Items.OfType<ComboBoxItem>().First(i => (Source)i.Tag == preferred);
        _sourceLoading = false;
        ApplySource(preferred);
    }

    private void SourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_sourceLoading || SourceBox.SelectedItem is not ComboBoxItem { Tag: Source source } || source == _source) return;
        ApplySource(source);
        // Suggestions of the previous source no longer apply.
        foreach (var item in _queue.Items) { item.Suggestion = null; item.SuggestionVersion = -1; }
        _groupSuggestion = null;
        ShowCandidates(GroupCandidatePanel, GroupCategory, null);
        ShowCandidates(GroupSubCandidatePanel, GroupSubcategory, null);
        _subSuggestion = null;
        if (_current != null) { ShowSuggestion(_current); _ = SuggestItemAsync(_current, force: false); }
    }

    private void ApplySource(Source source)
    {
        _source = source;
        ModelText.Text = source switch
        {
            Source.E5 => "E5 wählt unter deinen vorhandenen Kategorien und lernt aus jeder Zuordnung, die du hier speicherst. Für Vorschläge braucht es mindestens zwei Kategorien.",
            Source.SelectedModel => _ai!.UsesCategoryOnly
                ? $"{_ai.DisplayName} vergleicht den Inhalt mit deinen vorhandenen Kategorien. Titel bleiben erhalten. Es werden keine neuen Kategorien erzeugt; das Modell lernt nicht aus Zuordnungen."
                : $"{_ai.DisplayName} liest den Text und schlägt eine Kategorie und einen Titel vor. Lokal auf der CPU.",
            Source.Granite => $"{LanguageModelName} liest den Text und schlägt eine Kategorie vor, auch eine neue. Die Laufzeit hängt von der CPU und dem Modell ab."
                + (_ai?.E5Ready == true ? $" Ist E5 mit deinen eigenen Beispielen eindeutig, wird {LanguageModelName} übersprungen; „Neu vorschlagen“ fragt {LanguageModelName} trotzdem." : ""),
            _ => "Keine Vorschläge. Kategorie wählen oder neu eingeben, dann Enter.",
        };
        GroupSuggestButton.Visibility = ResuggestButton.Visibility = Ready ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <param name="forceGranite">Skip the E5 shortcut, e.g. when the user explicitly asks again.</param>
    private Task<LocalAiSuggestion> SuggestFrom(IEnumerable<BrainSource> sources, bool forceGranite = false)
        => _source == Source.SelectedModel
            ? _ai!.SuggestAsync(sources, _queue.Categories, _closing.Token, _queue.Assignments)
            : _source == Source.Granite
            ? _ai!.SuggestWithLanguageModelAsync(sources, _queue.Categories, _closing.Token, _queue.Assignments, e5First: !forceGranite)
            : _ai!.SuggestWithE5Async(sources, _queue.Categories, _closing.Token, _queue.Assignments);

    /// <summary>E5 answers depend on the saved assignments and are refreshed after each one; Granite answers are kept.</summary>
    private bool Learns(CategorizationItem item)
        => _source == Source.E5 || (_source == Source.SelectedModel && _ai!.UsesCategoryOnly)
            || item.Suggestion is { Method: var method } && !ClassifierMethods.IsLanguageModel(method);

    // ---- Single entries ------------------------------------------------------------------------------------

    private void ItemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ItemList.SelectedItem is CategorizationItem item && item != _current) ShowItem(item);
    }

    private void Select(int index)
    {
        ItemList.SelectedIndex = index;
        ItemList.ScrollIntoView(ItemList.SelectedItem);
    }

    private void ShowItem(CategorizationItem item)
    {
        _current = item;
        ItemTitle.Text = item.Source.Title;
        ItemMeta.Text = item.Meta + (item.Category.Length > 0 ? $" · gespeichert: {item.Category}" : " · noch ohne Kategorie");
        ItemPreview.Text = item.Source.Text;
        ItemPreview.ScrollToHome();
        _subSuggestion = null;
        SetPath(ItemCategory, ItemSubcategory, item.Category.Length > 0 ? item.Category : item.Suggestion is { Uncertain: false } s ? s.Category : "");
        _categoryEdited = false;
        ShowSuggestion(item);
        ApplyButton.IsEnabled = SkipButton.IsEnabled = true;
        _ = SuggestItemAsync(item, force: false);
    }

    private void ShowSuggestion(CategorizationItem item)
    {
        if (item != _current) return;
        SuggestionText.Text = !Ready ? "Kategorie wählen oder neu eingeben, dann Enter."
            : item.Suggestion is not { } s ? ""
            : s.Uncertain ? "Kein eindeutiger Vorschlag: " + s.Reason
            : $"Vorschlag: {s.Category} – {s.Reason}";
        var sub = _subSuggestion is { } own && own.Item == item ? own.Result : null;
        if (sub != null) SuggestionText.Text += (SuggestionText.Text.Length > 0 ? " " : "") + sub.Reason;
        ShowCandidates(CandidatePanel, ItemCategory, item.Suggestion?.Candidates);
        ShowCandidates(SubCandidatePanel, ItemSubcategory, sub?.SubCandidates ?? (_subSuggestion?.Item == item ? null
            : KnowledgeCategories.SameMain(item.Suggestion?.Path ?? default, KnowledgeCategories.Parse(ItemCategory.Text)) ? item.Suggestion?.SubCandidates : null),
            "Unterkategorien:");
    }

    /// <summary>
    /// Subcategory for the main category in the field when it differs from the suggested one (E5 only); hierarchical,
    /// so only subcategories and examples of that main category compete.
    /// </summary>
    private async Task SuggestSubAsync(CategorizationItem item)
    {
        var main = KnowledgeCategories.MainOf(ItemCategory.Text);
        if (_source != Source.E5 || _ai?.E5 is not { } e5 || item != _current) return;
        if (main.Length == 0 || _queue.SubsOf(main).Count == 0
            || (item.Suggestion is { Uncertain: false } s && KnowledgeCategories.SameMain(s.Path, new CategoryPath(main, ""))))
        {
            _subSuggestion = null;
            ShowSuggestion(item);
            return;
        }
        if (_subSuggestion is { } done && done.Item == item && string.Equals(done.Main, main, StringComparison.OrdinalIgnoreCase)) return;
        try { await _slot.WaitAsync(_closing.Token); }
        catch (OperationCanceledException) { return; }
        LocalAiSuggestion? result;
        try
        {
            if (_closed || item != _current || !string.Equals(KnowledgeCategories.MainOf(ItemCategory.Text), main, StringComparison.OrdinalIgnoreCase)) return;
            result = await e5.SuggestSubcategoryAsync([item.Source], main, _queue.Categories, _closing.Token, _queue.Assignments);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { result = new LocalAiSuggestion("", "", "Unterkategorie: kein Vorschlag (" + ex.Message + ").", true); }
        finally { _slot.Release(); }
        if (_closed || item != _current) return;
        _subSuggestion = (item, main, result);
        ShowSuggestion(item);
        if (result is { Uncertain: false } r && ItemSubcategory.Text.Trim().Length == 0)
            SetText(ItemSubcategory, LowerLevels(r.Path));
    }

    /// <summary>Best categories as one-click choices, also when the model abstains.</summary>
    private void ShowCandidates(WrapPanel panel, ComboBox target, IReadOnlyList<CategoryCandidate>? candidates, string caption = "Beste Kandidaten:")
    {
        panel.Children.Clear();
        if (candidates is not { Count: > 0 }) return;
        panel.Children.Add(new TextBlock { Text = caption, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 4),
            Foreground = (System.Windows.Media.Brush)FindResource("EditorMutedBrush"), FontSize = 12 });
        foreach (var candidate in candidates)
        {
            var button = new Button
            {
                Content = candidate.Category, Style = (Style)FindResource("EditorButtonStyle"), Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(8, 4, 8, 4),
                FontSize = 12, ToolTip = (candidate.Score != 0 ? $"Relative Ähnlichkeit {candidate.Score:0.00} (keine Wahrscheinlichkeit). " : "") + "Klicken übernimmt den Namen ins Feld.",
            };
            button.Click += (_, _) => { target.Text = candidate.Category; target.Focus(); };
            panel.Children.Add(button);
        }
    }

    private async Task SuggestItemAsync(CategorizationItem item, bool force)
    {
        if (!Ready || (!force && !_queue.NeedsSuggestion(item, Learns(item)))) return;
        var source = _source;
        if (item == _current) SuggestionText.Text = _source == Source.Granite && !force
            ? $"Vorschlag wird erstellt: zuerst E5 mit deinen Beispielen, {LanguageModelName} nur wenn nötig …"
            : $"{SourceName} erstellt einen Vorschlag …";
        try { await _slot.WaitAsync(_closing.Token); }
        catch (OperationCanceledException) { return; }
        try
        {
            if (_closed || item != _current) return;
            if (source != _source) return;
            if (!force && !_queue.NeedsSuggestion(item, Learns(item))) { ShowSuggestion(item); return; }
            var version = _queue.Version;
            var fresh = await SuggestFrom([item.Source], forceGranite: force);
            if (source != _source) return;
            item.Suggestion = fresh;
            item.SuggestionVersion = version;
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            if (!_closed && item == _current) SuggestionText.Text = "Kein Vorschlag: " + ex.Message;
            return;
        }
        finally { _slot.Release(); }
        if (_closed || item != _current) return;
        if (!_categoryEdited && item.Category.Length == 0 && item.Suggestion is { Uncertain: false } suggestion)
        {
            SetPath(ItemCategory, ItemSubcategory, suggestion.Category);
            _categoryEdited = false;
        }
        ShowSuggestion(item);
    }

    private void Resuggest_Click(object sender, RoutedEventArgs e)
    {
        if (_current != null) _ = SuggestItemAsync(_current, force: true);
    }

    private void ItemCategory_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || ItemCategory.IsDropDownOpen || ItemSubcategory.IsDropDownOpen) return;
        e.Handled = true;
        Apply_Click(sender, e);
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not { } item) return;
        var suggestion = item.Suggestion;
        if (!Save([item], PathOf(ItemCategory, ItemSubcategory))) return;
        _feedback?.Invoke(suggestion, item.Category);
        Feedback.Text = $"„{Shorten(item.Source.Title)}“ ist jetzt {item.Category} zugeordnet.";
        MoveNext(item);
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not { } item) return;
        CategorizationQueue.Skip(item);
        UpdateCounts();
        MoveNext(item);
    }

    private void MoveNext(CategorizationItem from)
    {
        var next = _queue.NextOpen(IndexOf(from), OnlyUncategorized.IsChecked == true);
        if (next >= 0) { Select(next); return; }
        Feedback.Text = OnlyUncategorized.IsChecked == true
            ? "Alle Einträge ohne Kategorie sind bearbeitet." : "Alle Einträge sind bearbeitet.";
        ApplyButton.IsEnabled = SkipButton.IsEnabled = ItemList.SelectedItem is CategorizationItem { State: CategorizationState.Open };
    }

    private void OnlyUncategorized_Click(object sender, RoutedEventArgs e)
    {
        if (_current is { State: CategorizationState.Open } current && (OnlyUncategorized.IsChecked != true || current.Category.Length == 0)) return;
        var next = _queue.NextOpen(_current == null ? -1 : IndexOf(_current), OnlyUncategorized.IsChecked == true);
        if (next >= 0) Select(next);
    }

    private int IndexOf(CategorizationItem item)
    {
        for (var i = 0; i < _queue.Items.Count; i++) if (_queue.Items[i] == item) return i;
        return -1;
    }

    // ---- Marked entries together -----------------------------------------------------------------------------

    private void Mark_Click(object sender, RoutedEventArgs e) => UpdateCounts();
    private void MarkAll_Click(object sender, RoutedEventArgs e) { foreach (var i in _queue.Items) i.Marked = true; UpdateCounts(); }
    private void MarkNone_Click(object sender, RoutedEventArgs e) { foreach (var i in _queue.Items) i.Marked = false; UpdateCounts(); }

    private void GroupSuggest_Click(object sender, RoutedEventArgs e) => SuggestGroupAsync().Forget("Gemeinsamen Vorschlag erstellen");

    private async Task SuggestGroupAsync()
    {
        var marked = _queue.Items.Where(i => i.Marked).ToList();
        if (marked.Count == 0) { GroupStatus.Text = "Bitte zuerst links Einträge markieren."; return; }
        if (!Ready) return;
        _groupBusy = true;
        UpdateCounts();
        GroupStatus.Text = $"{SourceName} sucht eine gemeinsame Kategorie für {marked.Count} Einträge …";
        try { await _slot.WaitAsync(_closing.Token); }
        catch (OperationCanceledException) { _groupBusy = false; return; }
        try
        {
            // Asking again after an E5 shortcut means the user wants Granite's own reading.
            var again = _groupSuggestion is { Method: var previous } && !ClassifierMethods.IsLanguageModel(previous);
            var result = await SuggestFrom(marked.Select(i => i.Source), forceGranite: again);
            if (_closed) return;
            var limit = marked.Count > LocalAiCategorization.MaxSources
                ? $" Ausgewertet wurden die ersten {LocalAiCategorization.MaxSources} markierten Einträge." : "";
            ShowCandidates(GroupCandidatePanel, GroupCategory, result.Candidates);
            ShowCandidates(GroupSubCandidatePanel, GroupSubcategory, result.SubCandidates, "Unterkategorien:");
            _groupSuggestion = result;
            GroupStatus.Text = result.Uncertain ? $"Keine gemeinsame Kategorie: {result.Reason}{limit}"
                : $"Vorschlag für alle {marked.Count} Markierten: {result.Category}. {result.Reason}{limit}";
            if (!result.Uncertain && !_groupEdited)
            {
                SetPath(GroupCategory, GroupSubcategory, result.Category);
                _groupEdited = false;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed) GroupStatus.Text = "Kein gemeinsamer Vorschlag: " + ex.Message; }
        finally
        {
            _slot.Release();
            _groupBusy = false;
            if (!_closed) UpdateCounts();
        }
    }

    private void GroupApply_Click(object sender, RoutedEventArgs e)
    {
        var marked = _queue.Items.Where(i => i.Marked).ToList();
        if (marked.Count == 0) { GroupStatus.Text = "Bitte zuerst links Einträge markieren."; return; }
        var category = PathOf(GroupCategory, GroupSubcategory);
        if (CategorizationQueue.Validate(category) is { } error) { GroupStatus.Text = error; return; }
        var overwrite = marked.Count(i => i.Category.Length > 0 && !i.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        if (marked.Count > 1 && MessageBox.Show(this,
                $"{marked.Count} markierte Einträge der Kategorie „{category}“ zuordnen?"
                + (overwrite > 0 ? $"\n\n{overwrite} davon haben bereits eine andere Kategorie, die ersetzt wird." : ""),
                AppNames.KnowledgeBase, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (!Save(marked, category)) return;
        _feedback?.Invoke(_groupSuggestion, category);
        _groupSuggestion = null;
        GroupStatus.Text = $"{marked.Count} Einträge sind jetzt {category} zugeordnet.";
        foreach (var i in marked) i.Marked = false;
        UpdateCounts();
        if (_current != null && marked.Contains(_current)) MoveNext(_current);
    }

    private void GroupAnalyze_Click(object sender, RoutedEventArgs e) => GroupAnalyze_ClickAsync(sender, e).Forget("Markierte mit Sprachmodell analysieren");

    private async Task GroupAnalyze_ClickAsync(object sender, RoutedEventArgs e)
    {
        var marked = _queue.Items.Where(i => i.Marked).ToList();
        if (_analyze == null) return;
        if (marked.Count < CategoryDiscovery.MinGroupSize)
        {
            GroupStatus.Text = $"Für die Analyse bitte mindestens {CategoryDiscovery.MinGroupSize} Einträge markieren.";
            return;
        }
        var applied = await _analyze(marked.Select(i => i.Key).ToList(), this);
        if (_closed || applied.Count == 0) return;
        foreach (var group in applied.GroupBy(a => a.Value, StringComparer.OrdinalIgnoreCase))
        {
            var keys = group.Select(a => a.Key).ToHashSet();
            _queue.Apply(_queue.Items.Where(i => keys.Contains(i.Key)).ToList(), group.First().Value);
        }
        Changed = true;
        foreach (var item in _queue.Items.Where(i => applied.ContainsKey(i.Key))) item.Marked = false;
        RefreshCategories();
        UpdateCounts();
        GroupStatus.Text = $"{applied.Count} Einträge über die Sprachmodell-Analyse zugeordnet.";
        if (_current != null && applied.ContainsKey(_current.Key)) MoveNext(_current);
    }

    // ---- Shared --------------------------------------------------------------------------------------------

    private bool Save(IReadOnlyList<CategorizationItem> items, string category)
    {
        if (CategorizationQueue.Validate(category) is { } error) { Feedback.Text = error; return false; }
        category = KnowledgeCategories.Parse(category).ToString();
        try { _persist(items.Select(i => i.Key).ToList(), category); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.LogCrash(ex);
            Feedback.Text = "Speichern fehlgeschlagen: " + ex.Message;
            return false;
        }
        if (_queue.Apply(items, category).Count > 0) Changed = true;
        RefreshCategories();
        UpdateCounts();
        return true;
    }

    private void RefreshCategories()
    {
        var mains = _queue.Mains;
        foreach (var box in new[] { ItemCategory, GroupCategory })
        {
            var text = box.Text;
            _settingText = true;
            box.ItemsSource = mains;
            box.Text = text;
            _settingText = false;
        }
        RefreshSubs(ItemCategory, ItemSubcategory);
        RefreshSubs(GroupCategory, GroupSubcategory);
    }

    /// <summary>The subcategory list follows the main category in the field next to it.</summary>
    private void RefreshSubs(ComboBox main, ComboBox sub)
    {
        if (sub == null) return;
        var text = sub.Text;
        var was = _settingText;
        _settingText = true;
        sub.ItemsSource = _queue.SubsOf(KnowledgeCategories.MainOf(main.Text));
        sub.Text = text;
        _settingText = was;
    }

    private static string PathOf(ComboBox main, ComboBox sub)
    {
        var typed = KnowledgeCategories.Parse(main.Text);
        return KnowledgeCategories.Combine(typed.Main, sub.Text.Trim().Length > 0 ? sub.Text : typed.Sub);
    }

    /// <summary>"Unter" or "Unter › Fein" for the second field.</summary>
    private static string LowerLevels(CategoryPath p) => p.HasFine ? p.Sub + KnowledgeCategories.Separator + p.Fine : p.Sub;

    /// <summary>A typed "Haupt › Unter › Fein" in the main field is split as well.</summary>
    private void SetPath(ComboBox main, ComboBox sub, string path)
    {
        var p = KnowledgeCategories.Parse(path);
        var was = _settingText;
        _settingText = true;
        main.Text = p.Main;
        sub.ItemsSource = _queue.SubsOf(p.Main);
        sub.Text = LowerLevels(p);
        _settingText = was;
    }

    private void SetText(ComboBox box, string text)
    {
        _settingText = true;
        box.Text = text;
        _settingText = false;
    }

    private void UpdateCounts()
    {
        var items = _queue.Items;
        var marked = items.Count(i => i.Marked);
        CountText.Text = $"{items.Count} Einträge · {_queue.Count(CategorizationState.Applied)} zugeordnet · "
            + $"{_queue.Count(CategorizationState.Skipped)} übersprungen · {items.Count(i => i.Category.Length == 0)} ohne Kategorie";
        GroupHeader.Text = $"Markierte gemeinsam einordnen ({marked})";
        GroupApplyButton.IsEnabled = marked > 0;
        GroupSuggestButton.IsEnabled = marked > 0 && !_groupBusy;
    }

    private static string Shorten(string text)
    {
        var t = text.ReplaceLineEndings(" ").Trim();
        return t.Length <= 60 ? t : t[..57] + "...";
    }
}

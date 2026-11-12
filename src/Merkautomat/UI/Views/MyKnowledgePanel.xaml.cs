using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Merkautomat.Services;
using MessageBox = Merkautomat.Views.AppMessageBox;
using UserControl = System.Windows.Controls.UserControl;

namespace Merkautomat.Views;

/// <summary>
/// Ideenkombinat section "Mein Wissen": the weighted daily collection of all sources with a profile of its main
/// categories, word and meaning search, filters and the keep/remove actions. The window owns navigation, the
/// header and the Brain; it listens to <see cref="StatusMessage"/>, <see cref="ShowInBrain"/> and
/// <see cref="AssignCategory"/>.
/// </summary>
public partial class MyKnowledgePanel : UserControl
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private const double MaxBar = 44;
    private const double MaxProfileBar = 160;
    private const int ExpiringDays = 14;

    public sealed class Row(MyKnowledgeEntry entry, string category, string state, string extra = "")
    {
        public MyKnowledgeEntry Entry { get; } = entry;
        public string Category { get; } = category;
        public string State { get; } = state;
        /// <summary>Open category proposal or duplicate group, shown after the state.</summary>
        public string Extra { get; } = extra;
        public string Title => MyKnowledgeAi.DisplayTitle(Entry) is { Length: > 0 } title ? title : "(ohne Titel)";
        public double BarWidth => Math.Max(2, Entry.Weight * MaxBar);
        public string WeightLabel => $"{Entry.Weight * 100:0} %";
        public string WeightTip => $"Gewicht {Entry.Weight * 100:0} % (Quelle, Alter, Zeit in der Wissensbasis, Kategorie, Mappe, Nähe zum Kernwissen, erneutes Kopieren; dauerhaft behalten = 100 %)";
        public string Meta => string.Join(" · ", new[]
        {
            Entry.Digest ? $"Zusammenfassung aus {Entry.Members} Einträgen" : SemanticChunk.SourceName(Entry.Source),
            Category, Entry.Date.ToString("dd.MM.yyyy", De), State, Extra,
        }.Where(s => s.Length > 0));
    }

    public sealed record ProfileRow(string Name, double BarWidth, string Label, string Tip, bool IsActive);

    public sealed record AnswerSourceButton(string Label, string Tip, string Key);

    private App? _app;
    private List<MyKnowledgeEntry> _entries = [];
    private MyKnowledgeContext? _context;
    private string? _categoryFilter;
    private DispatcherTimer? _searchDebounce;
    private CancellationTokenSource? _searchCts;
    /// <summary>Meaning hits of the current query: item key to normalized score.</summary>
    private Dictionary<string, double>? _semanticScores;
    private bool _active;
    /// <summary>"Fast gleich": item key to group number, null until computed for this visit.</summary>
    private Dictionary<string, int>? _similarGroups;
    private bool _similarLoading;
    private CancellationTokenSource? _askCts;

    public MyKnowledgePanel()
    {
        InitializeComponent();
    }

    /// <summary>A message for the window's status line.</summary>
    public event Action<string>? StatusMessage;
    /// <summary>Highlight these item keys in the Brain.</summary>
    public event Action<IReadOnlyCollection<string>, string>? ShowInBrain;
    /// <summary>Let the user choose a category for these item keys.</summary>
    public event Action<IReadOnlyCollection<string>>? AssignCategory;
    public event EventHandler? StateChanged;

    /// <summary>Meaning search over the index; null while the model is missing.</summary>
    public Func<string, CancellationToken, Task<IReadOnlyList<SemanticHit>>>? SemanticSearch { get; set; }
    /// <summary>Groups of nearly equal items (item keys) from the index.</summary>
    public Func<CancellationToken, Task<List<List<string>>>>? SimilarGroups { get; set; }
    /// <summary>The user agreed to these proposals: item key to category path.</summary>
    public event Action<IReadOnlyDictionary<string, string>>? AcceptSuggestions;

    public bool HasSelection => EntryList.SelectedItems.Count > 0;
    public bool ListHasKeyboardFocus => EntryList.IsKeyboardFocusWithin;
    public int Count => _entries.Count;

    public string Subtitle
    {
        get
        {
            if (_app == null) return "";
            if (!_app.CurrentSettings.MyKnowledgeEnabled) return $"Ausgeschaltet: {AppNames.Settings} → Allgemein → {AppNames.MyKnowledge}.";
            var lasting = _context == null ? 0 : _entries.Count(e => MyKnowledge.IsProtected(e, _context));
            return $"{_entries.Count:N0} Einträge · {lasting:N0} gefestigt · {_app.MyKnowledgeStatus}";
        }
    }

    public void Init(App app)
    {
        _app = app;
        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            SearchAsync().Forget("In Mein Wissen suchen");
        };
        _app.MyKnowledgeChanged += App_MyKnowledgeChanged;
    }

    public void Close()
    {
        _searchDebounce?.Stop();
        _searchCts?.Cancel();
        _askCts?.Cancel();
        if (_app != null) _app.MyKnowledgeChanged -= App_MyKnowledgeChanged;
    }

    public void Enter()
    {
        _active = true;
        // The index may have changed since the last visit.
        _similarGroups = null;
        Reload();
        // Before the first daily run the collection is empty; fill it right away instead of waiting for the timer.
        if (_app is { MyKnowledgeLastRun: null }) _app.RunMyKnowledge();
    }

    public void Leave() => _active = false;

    private void App_MyKnowledgeChanged(object? sender, EventArgs e)
    {
        if (_active) Reload();
        else StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reads the collection again, e.g. after a category change in the window.</summary>
    public void Reload()
    {
        if (_app == null) return;
        _entries = _app.MyKnowledgeSnapshot();
        _context = _app.MyKnowledgeContext();
        RunButton.IsEnabled = _app.CurrentSettings.MyKnowledgeEnabled && !_app.MyKnowledgeRunning;
        var sure = MyKnowledgeAi.SureSuggestions(_entries, _context).Count;
        AcceptSureButton.IsEnabled = sure > 0;
        AcceptSureText.Text = sure > 0 ? $"Sichere Vorschläge übernehmen ({sure})" : "Sichere Vorschläge übernehmen";
        FillProfile();
        Apply();
    }

    /// <summary>Shows one main category, e.g. from the weekly review on the dashboard.</summary>
    public void ShowCategory(string name)
    {
        SourceAll.IsChecked = true;
        StateAll.IsChecked = true;
        QueryBox.Text = "";
        _categoryFilter = name;
        FillProfile();
        Apply();
    }

    private void FillProfile()
    {
        if (_context == null) return;
        var profile = MyKnowledge.Profile(_entries, _context);
        var max = profile.Count == 0 ? 1 : profile.Max(r => r.Weight);
        ProfileList.ItemsSource = profile.Select(r => new ProfileRow(r.Category, Math.Max(2, r.Weight / max * MaxProfileBar),
            $"{r.Share * 100:0} % · {r.Count:N0}",
            $"{r.Category}: {r.Count:N0} Einträge, {r.Share * 100:0.#} % des Gewichts, zuletzt {r.Latest.ToString("dd.MM.yyyy", De)}",
            string.Equals(r.Category, _categoryFilter, StringComparison.OrdinalIgnoreCase))).ToList();
        ProfileSubtitle.Text = profile.Count == 0
            ? $"Noch leer. {AppNames.MyKnowledge} füllt sich einmal am Tag aus allen Quellen, oder sofort mit „Jetzt aktualisieren“."
            : "Hauptkategorien nach Gewicht: woraus dein Wissen besteht. Ein Klick filtert die Liste.";
    }

    // ---- Filtering and search --------------------------------------------------------------------------------

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) Apply();
    }

    private void QueryBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        QueryHint.Visibility = QueryBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _semanticScores = null;
        Apply();
        _searchDebounce?.Stop();
        if (QueryBox.Text.Trim().Length >= 3) _searchDebounce?.Start();
    }

    private async Task SearchAsync()
    {
        var query = QueryBox.Text.Trim();
        if (SemanticSearch == null || query.Length < 3) return;
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        IReadOnlyList<SemanticHit> hits;
        try { hits = await SemanticSearch(query, cts.Token); }
        catch (OperationCanceledException) { return; }
        if (cts.IsCancellationRequested || query != QueryBox.Text.Trim()) return;
        var best = hits.Count == 0 ? 1 : hits.Max(h => h.Score);
        _semanticScores = hits.GroupBy(h => h.Chunk.ItemKey).ToDictionary(g => g.Key, g => best > 0 ? g.Max(h => h.Score) / best : 0);
        Apply();
    }

    private void Apply()
    {
        if (_context == null) return;
        var selected = EntryList.SelectedItems.OfType<Row>().Select(r => r.Entry.Key).ToHashSet(StringComparer.Ordinal);
        var now = DateTime.Now;
        IEnumerable<MyKnowledgeEntry> items = _entries.Where(MatchesSource).Where(e => MatchesState(e, now));
        if (_categoryFilter != null)
            items = items.Where(e => string.Equals(MainCategory(e), _categoryFilter, StringComparison.OrdinalIgnoreCase));

        var query = QueryBox.Text.Trim();
        if (query.Length > 0)
        {
            var list = items.ToList();
            var lexical = MyKnowledge.Search(list, query).Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
            var semantic = _semanticScores ?? [];
            // Relevance first, weight breaks near-ties: a heavy item ranks above a light one with a similar match.
            items = list.Where(e => lexical.Contains(e.Key) || semantic.ContainsKey(e.Key))
                .OrderByDescending(e => semantic.GetValueOrDefault(e.Key) + (lexical.Contains(e.Key) ? 0.5 : 0) + 0.25 * e.Weight);
        }

        var similar = StateSimilar.IsChecked == true;
        if (similar)
        {
            if (_similarGroups == null) LoadSimilarGroups();
            var groups = _similarGroups ?? [];
            items = items.Where(e => groups.ContainsKey(e.Key)).OrderBy(e => groups[e.Key]).ThenByDescending(e => e.Weight);
        }

        var rows = items.Select(e => new Row(e, _context.CategoryOf(e), StateLabel(e, now), ExtraLabel(e, similar))).ToList();
        EntryList.ItemsSource = rows;
        foreach (var row in rows.Where(r => selected.Contains(r.Entry.Key))) EntryList.SelectedItems.Add(row);
        EntriesEmpty.Text = _entries.Count == 0 ? "Noch nichts übernommen."
            : similar && (_similarLoading || _similarGroups == null) ? "Fast gleiche Einträge werden gesucht …"
            : similar && SimilarGroups == null ? "Dafür wird das Modell der semantischen Suche gebraucht."
            : similar ? "Keine fast gleichen Einträge. Der Suchindex muss die Einträge kennen; er wird beim Öffnen von Suche oder Brain aktualisiert."
            : StateSuggested.IsChecked == true ? "Keine offenen Vorschläge. Sie entstehen nach dem täglichen Lauf, sobald es mindestens zwei Kategorien gibt."
            : query.Length > 0 ? "Keine Treffer." : "Keine Einträge für diesen Filter.";
        EntriesEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CategoryFilterBar.Visibility = _categoryFilter == null ? Visibility.Collapsed : Visibility.Visible;
        CategoryFilterText.Text = _categoryFilter == null ? "" : $"Kategorie: {_categoryFilter} ({rows.Count:N0})  ";
        ShowDetail();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private string ExtraLabel(MyKnowledgeEntry e, bool similar)
    {
        if (similar && _similarGroups != null && _similarGroups.TryGetValue(e.Key, out var group)) return $"Gruppe {group + 1}";
        if (!MyKnowledgeAi.HasOpenSuggestion(e, _context!)) return "";
        return $"Vorschlag: {e.SuggestedCategory}" + (e.SuggestionSure ? " (sicher)" : "");
    }

    private void LoadSimilarGroups()
    {
        if (_similarLoading || SimilarGroups == null) return;
        _similarLoading = true;
        LoadSimilarGroupsAsync().Forget("Fast gleiche Einträge suchen");
    }

    private async Task LoadSimilarGroupsAsync()
    {
        try
        {
            var keys = _entries.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
            var groups = await SimilarGroups!(CancellationToken.None);
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            var number = 0;
            // Only groups with at least two entries in Mein Wissen are worth showing.
            foreach (var group in groups.Select(g => g.Where(keys.Contains).Distinct().ToList()).Where(g => g.Count >= 2))
            {
                foreach (var key in group) result.TryAdd(key, number);
                number++;
            }
            _similarGroups = result;
        }
        finally { _similarLoading = false; }
        if (StateSimilar.IsChecked == true) Apply();
    }

    private string MainCategory(MyKnowledgeEntry e)
    {
        var main = _context!.MainOf(e);
        return main.Length == 0 ? "Ohne Kategorie" : main;
    }

    private bool MatchesSource(MyKnowledgeEntry e)
        => SourceAll.IsChecked == true
           || (SourceFolder.IsChecked == true && HasSource(e, SemanticSource.KnowledgeFolder))
           || (SourceNotes.IsChecked == true && HasSource(e, SemanticSource.Note))
           || (SourceRecordings.IsChecked == true && HasSource(e, SemanticSource.Recording))
           || (SourceClipboard.IsChecked == true && HasSource(e, SemanticSource.Clipboard));

    /// <summary>A digest counts for every source it condensed.</summary>
    private static bool HasSource(MyKnowledgeEntry e, SemanticSource source)
        => e.Digest ? e.SourceCounts.ContainsKey(SemanticChunk.SourceName(source)) : e.Source == source;

    private bool MatchesState(MyKnowledgeEntry e, DateTime now)
    {
        if (StateNew.IsChecked == true) return e.Added.Date == now.Date;
        if (StateLasting.IsChecked == true) return !e.Digest && MyKnowledge.IsProtected(e, _context!);
        if (StateDigest.IsChecked == true) return e.Digest;
        if (StateSuggested.IsChecked == true) return MyKnowledgeAi.HasOpenSuggestion(e, _context!);
        if (StateExpiring.IsChecked == true) return MyKnowledge.DaysLeft(e, now, _context!) is <= ExpiringDays;
        return true;
    }

    private string StateLabel(MyKnowledgeEntry e, DateTime now)
    {
        if (e.Digest) return e.Kept ? "dauerhaft" : "";
        if (e.Kept) return "dauerhaft";
        if (e.Consolidated) return "gefestigt";
        if (_context!.InCollection(e.Key)) return "in einer Mappe";
        if (_context.HasCategory(e.Key)) return "";
        if (e.Source == SemanticSource.KnowledgeFolder) return "";
        var verb = _context.Condense ? "wird {0} zusammengefasst" : "läuft {0} aus";
        return MyKnowledge.DaysLeft(e, now, _context) switch
        {
            null => "",
            0 => string.Format(verb, "heute"),
            1 => string.Format(verb, "morgen"),
            var d => string.Format(verb, $"in {d} Tagen"),
        };
    }

    private void ProfileRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ProfileRow row) return;
        _categoryFilter = string.Equals(_categoryFilter, row.Name, StringComparison.OrdinalIgnoreCase) ? null : row.Name;
        FillProfile();
        Apply();
    }

    private void CategoryFilterClear_Click(object sender, RoutedEventArgs e)
    {
        _categoryFilter = null;
        FillProfile();
        Apply();
    }

    // ---- Detail and actions ----------------------------------------------------------------------------------

    private List<MyKnowledgeEntry> Selected() => EntryList.SelectedItems.OfType<Row>().Select(r => r.Entry).ToList();

    private void EntryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ShowDetail();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowDetail()
    {
        var selected = EntryList.SelectedItems.OfType<Row>().ToList();
        DetailActions.IsEnabled = selected.Count > 0;
        if (selected.Count == 0)
        {
            DetailTitle.Text = "Kein Eintrag gewählt";
            DetailMeta.Text = $"{AppNames.MyKnowledge} sammelt einmal am Tag aus allen Quellen, gewichtet nach Knowledgeordner, Sprachnotizen, "
                + "Bildschirmaufnahmen und Zwischenablage. Zwischenablage und Aufnahmen verlieren mit dem Alter an Gewicht. "
                + "Was lange genug relevant bleibt, wird gefestigt; Einträge mit Kategorie, in einer Mappe oder dauerhaft behalten bleiben ebenfalls. "
                + $"Alles andere wird nach der in der {AppNames.Settings} eingestellten Zeit zu Monatszusammenfassungen verdichtet; "
                + "Einzelheiten gehen dabei verloren. Der Knowledgeordner hat keine Fristen.";
            DetailText.Text = "";
            return;
        }
        var allKept = selected.All(r => r.Entry.Kept);
        KeepButtonText.Text = allKept ? "Nicht mehr dauerhaft" : "Dauerhaft behalten";
        System.Windows.Automation.AutomationProperties.SetName(KeepButton, KeepButtonText.Text);
        AcceptSuggestionButton.Visibility = selected.Any(r => MyKnowledgeAi.HasOpenSuggestion(r.Entry, _context!)) ? Visibility.Visible : Visibility.Collapsed;
        if (selected.Count == 1)
        {
            var row = selected[0];
            var e = row.Entry;
            DetailTitle.Text = row.Title;
            var parts = new List<string>
            {
                e.Digest ? $"Zusammenfassung aus {e.Members} Einträgen" : SemanticChunk.SourceName(e.Source), $"Gewicht {e.Weight * 100:0} %",
                row.Category.Length > 0 ? "Kategorie: " + row.Category : "ohne Kategorie",
                "zuletzt " + e.Date.ToString("dd.MM.yyyy HH:mm", De), (e.Digest ? "angelegt " : "übernommen ") + e.Added.ToString("dd.MM.yyyy", De),
            };
            if (row.State.Length > 0) parts.Add(row.State);
            if (!e.Digest && !e.Kept && _context != null && MyKnowledge.Maturity(e, DateTime.Now, _context) is var ripe and < 1)
                parts.Add($"noch jung: zählt erst zu {ripe * 100:0} %");
            if (e.Origin.Length > 0) parts.Add(e.Origin);
            if (e.AiTitle.Length > 0) parts.Add($"Titel vom Sprachmodell, ursprünglich: {e.Title}");
            if (MyKnowledgeAi.HasOpenSuggestion(e, _context!))
                parts.Add($"Vorschlag ({MyKnowledgeAi.SuggestionSource(e)}{(e.SuggestionSure ? ", sicher" : "")}): {e.SuggestedCategory}" + (e.SuggestionReason.Length > 0 ? $" – {e.SuggestionReason}" : ""));
            if (e.Affinity >= 0.3) parts.Add($"nah an deinem Kernwissen ({e.Affinity * 100:0} %)");
            if (!e.Digest && e.LastSeen < (_app?.MyKnowledgeLastRun ?? e.LastSeen)) parts.Add("Quelle nicht mehr vorhanden, gespeicherte Kopie");
            DetailMeta.Text = string.Join(" · ", parts);
            DetailText.Text = e.Text;
        }
        else
        {
            DetailTitle.Text = $"{selected.Count} Einträge gewählt";
            DetailMeta.Text = $"Zusammen {selected.Sum(r => r.Entry.Weight) * 100:0} Gewichtspunkte";
            DetailText.Text = CopyText();
        }
        DetailText.ScrollToHome();
    }

    private void EntryMenu_Opened(object sender, RoutedEventArgs e)
    {
        var selected = Selected();
        EntryMenuKeep.Header = selected.Count > 0 && selected.All(x => x.Kept) ? "Nicht mehr dauerhaft" : "Dauerhaft behalten";
        EntryMenuAccept.Visibility = _context != null && selected.Any(x => MyKnowledgeAi.HasOpenSuggestion(x, _context)) ? Visibility.Visible : Visibility.Collapsed;
        if (sender is System.Windows.Controls.ContextMenu menu)
            foreach (var item in menu.Items.OfType<System.Windows.Controls.MenuItem>()) item.IsEnabled = selected.Count > 0;
    }

    // ---- Proposals -------------------------------------------------------------------------------------------

    private void AcceptSuggestion_Click(object sender, RoutedEventArgs e)
    {
        if (_context == null) return;
        var chosen = Selected().Where(x => MyKnowledgeAi.HasOpenSuggestion(x, _context)).ToList();
        if (chosen.Count > 0) AcceptSuggestions?.Invoke(chosen.ToDictionary(x => x.Key, x => x.SuggestedCategory, StringComparer.Ordinal));
    }

    private void AcceptSure_Click(object sender, RoutedEventArgs e)
    {
        if (_context == null) return;
        var sure = MyKnowledgeAi.SureSuggestions(_entries, _context);
        if (sure.Count == 0) return;
        var counts = string.Join(", ", sure.GroupBy(x => x.SuggestedCategory, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).Take(6).Select(g => $"{g.Key} ({g.Count()})"));
        if (MessageBox.Show(Window.GetWindow(this), $"{sure.Count} sichere Vorschläge übernehmen?\n\n{counts}\n\n"
                + "E5 war bei diesen Einträgen mit deinen eigenen Beispielen eindeutig. Eingeordnete Einträge werden nicht mehr zusammengefasst.",
                AppNames.KnowledgeBase, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes) != MessageBoxResult.Yes) return;
        AcceptSuggestions?.Invoke(sure.ToDictionary(x => x.Key, x => x.SuggestedCategory, StringComparer.Ordinal));
    }

    // ---- Questions -------------------------------------------------------------------------------------------

    private void QueryBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter || (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0) return;
        e.Handled = true;
        AskAsync().Forget("Mein Wissen fragen");
    }

    private void Ask_Click(object sender, RoutedEventArgs e) => AskAsync().Forget("Mein Wissen fragen");

    private async Task AskAsync()
    {
        if (_app == null) return;
        var question = QueryBox.Text.Trim();
        if (question.Length < 3)
        {
            QueryBox.Focus();
            StatusMessage?.Invoke("Bitte zuerst eine Frage eingeben.");
            return;
        }
        if (!_app.LocalAi.Model.IsInstalled)
        {
            MessageBox.Show(Window.GetWindow(this), $"Für Antworten wird das lokale Sprachmodell {_app.LocalAi.Model.Name} gebraucht. "
                + $"Es lässt sich in der {AppNames.Settings} unter „Lokale KI“ herunterladen.", AppNames.KnowledgeBase);
            return;
        }
        _askCts?.Cancel();
        var cts = _askCts = new CancellationTokenSource();
        AnswerPanel.Visibility = Visibility.Visible;
        AnswerTitle.Text = $"Frage: {question}";
        AnswerText.Text = "Das Sprachmodell sucht die Antwort in deinem Wissen …";
        AnswerSources.ItemsSource = null;
        AskButton.IsEnabled = false;
        try
        {
            var (answer, sources) = await _app.AskMyKnowledgeAsync(question, cts.Token);
            if (cts.IsCancellationRequested) return;
            AnswerText.Text = answer.Answer + (answer.Found ? "" : "\n\nNicht belegt: Die gefundenen Einträge beantworten die Frage nicht sicher.");
            AnswerSources.ItemsSource = sources.Select((s, i) => new AnswerSourceButton(
                $"[{i + 1}] {Shorten(MyKnowledgeAi.DisplayTitle(s), 40)}" + (answer.Cited.Contains(i + 1) ? " ✓" : ""),
                $"{MyKnowledgeAi.DisplayTitle(s)} · {SemanticChunk.SourceName(s.Source)} · Gewicht {s.Weight * 100:0} %", s.Key)).ToList();
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or TimeoutException or System.Net.Http.HttpRequestException or ArgumentException)
        {
            AnswerText.Text = "Keine Antwort möglich: " + ex.Message;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        finally { AskButton.IsEnabled = true; }
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    private void AnswerClose_Click(object sender, RoutedEventArgs e)
    {
        _askCts?.Cancel();
        AnswerPanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>Selects the quoted entry; filters and the question are cleared so it is visible.</summary>
    private void AnswerSource_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AnswerSourceButton source) return;
        if (EntryList.Items.OfType<Row>().FirstOrDefault(r => r.Entry.Key == source.Key) is not { } row)
        {
            SourceAll.IsChecked = true;
            StateAll.IsChecked = true;
            _categoryFilter = null;
            QueryBox.Text = "";
            FillProfile();
            Apply();
            row = EntryList.Items.OfType<Row>().FirstOrDefault(r => r.Entry.Key == source.Key);
        }
        if (row == null) return;
        EntryList.SelectedItems.Clear();
        EntryList.SelectedItem = row;
        EntryList.ScrollIntoView(row);
    }

    private void Keep_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selected();
        if (_app == null || selected.Count == 0) return;
        var keep = !selected.All(x => x.Kept);
        _app.KeepMyKnowledge(selected.Select(x => x.Key).ToList(), keep);
        StatusMessage?.Invoke(keep ? $"{selected.Count} Einträge dauerhaft in {AppNames.MyKnowledge} (100 %)." : $"{selected.Count} Einträge nicht mehr dauerhaft; sie zählen wieder nach Quelle und Alter.");
    }

    private void Category_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selected();
        if (selected.Count > 0) AssignCategory?.Invoke(selected.Select(x => x.Key).ToList());
    }

    private void Brain_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selected();
        if (selected.Count == 0) return;
        // A digest is not in the net itself; its condensed entries may still be.
        var keys = selected.SelectMany(x => x.Digest ? x.Sources.Select(s => s.Key) : [x.Key]).Distinct().ToList();
        ShowInBrain?.Invoke(keys, selected.Count == 1 ? selected[0].Title : $"{selected.Count} Einträge aus {AppNames.MyKnowledge}");
    }

    private void Summarize_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selected();
        if (_app == null) return;
        var usable = selected.Where(x => x.Source != SemanticSource.KnowledgeFolder || x.Digest).ToList();
        var window = Window.GetWindow(this);
        if (usable.Count < 2)
        {
            MessageBox.Show(window, "Bitte mindestens zwei Einträge wählen (Strg oder Umschalt). Dateien aus dem Knowledgeordner "
                + "werden nicht zusammengefasst; sie bleiben, solange sie im Ordner liegen.", AppNames.KnowledgeBase);
            return;
        }
        var question = $"{usable.Count} Einträge zu einer Zusammenfassung verdichten?\n\n"
            + $"Erhalten bleiben bis zu {MyKnowledge.MaxDigestStatements} ausgewählte Originalaussagen und die Titel. "
            + $"Die übrigen Inhalte gehen in {AppNames.MyKnowledge} verloren. Die Quellen selbst bleiben unverändert.";
        if (selected.Count > usable.Count) question += $"\n\n{selected.Count - usable.Count} Dateien aus dem Knowledgeordner bleiben unverändert.";
        if (MessageBox.Show(window, question, AppNames.KnowledgeBase, MessageBoxButton.YesNo, MessageBoxImage.Question,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;
        var key = _app.SummarizeMyKnowledge(usable.Select(x => x.Key).ToList());
        if (key == null) return;
        // Show the new digest selected, so its kept statements can be checked right away.
        EntryList.SelectedItems.Clear();
        if (EntryList.ItemsSource is IEnumerable<Row> rows && rows.FirstOrDefault(r => r.Entry.Key == key) is { } created)
        {
            EntryList.SelectedItem = created;
            EntryList.ScrollIntoView(created);
        }
        StatusMessage?.Invoke($"{usable.Count} Einträge zu einer Zusammenfassung verdichtet.");
    }

    private void Remove_Click(object sender, RoutedEventArgs e) => RemoveSelected();

    /// <summary>Removes the selected items after a question; the sources stay as they are.</summary>
    public void RemoveSelected()
    {
        var selected = Selected();
        if (_app == null || selected.Count == 0) return;
        var question = selected.Count == 1
            ? $"„{selected[0].Title}“ aus {AppNames.MyKnowledge} entfernen?"
            : $"{selected.Count} Einträge aus {AppNames.MyKnowledge} entfernen?";
        question += "\n\nDie Quellen bleiben unverändert. Entfernte Einträge werden nicht wieder übernommen, solange ihre Quelle besteht.";
        if (MessageBox.Show(Window.GetWindow(this), question, AppNames.KnowledgeBase, MessageBoxButton.YesNo, MessageBoxImage.Question,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;
        _app.DismissMyKnowledge(selected.Select(x => x.Key).ToList());
        StatusMessage?.Invoke($"{selected.Count} Einträge aus {AppNames.MyKnowledge} entfernt.");
    }

    private void Run_Click(object sender, RoutedEventArgs e)
    {
        if (_app == null) return;
        _app.RunMyKnowledge();
        RunButton.IsEnabled = false;
        StatusMessage?.Invoke($"{AppNames.MyKnowledge} wird aktualisiert …");
    }

    /// <summary>Selected entries as Markdown, or the profile when nothing is selected.</summary>
    public string CopyText()
    {
        var selected = Selected();
        var sb = new StringBuilder();
        if (selected.Count == 0)
        {
            sb.AppendLine("# " + AppNames.MyKnowledge).AppendLine();
            foreach (var row in ProfileList.Items.OfType<ProfileRow>()) sb.AppendLine($"- {row.Name}: {row.Label}");
            return sb.ToString();
        }
        foreach (var entry in selected)
        {
            sb.AppendLine($"## {entry.Title}").AppendLine();
            sb.AppendLine($"{SemanticChunk.SourceName(entry.Source)} · {entry.Date.ToString("dd.MM.yyyy HH:mm", De)} · Gewicht {entry.Weight * 100:0} %").AppendLine();
            sb.AppendLine(entry.Text).AppendLine();
        }
        return sb.ToString().TrimEnd() + "\n";
    }
}

using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ClipwellWin.Services;
using MessageBox = ClipwellWin.Views.AppMessageBox;
using TextBox = System.Windows.Controls.TextBox;

namespace ClipwellWin.Views;

/// <summary>
/// Category analysis with Granite: groups uncategorized entries, lets Granite name each group and applies the
/// proposals the user keeps. Offers the Granite download when the model is missing.
/// </summary>
public partial class CategoryDiscoveryWindow : Window
{
    private readonly LocalAiService? _ai;
    private readonly IReadOnlyList<DiscoveryItem> _items;
    private readonly IReadOnlyList<string> _existing;
    private readonly Action<IReadOnlyList<string>, string> _persist;
    private readonly Action<LocalAiSuggestion?, string>? _feedback;
    private readonly CategoryDiscovery.NameGroup _name;
    private readonly bool _modelOverride;
    private readonly ObservableCollection<CategoryProposal> _proposals = [];
    private CancellationTokenSource? _running;
    // Smooth progress: Granite gives no intermediate signal, so the bar advances by the measured time per group.
    private const double FirstGroupSeconds = 30;
    private readonly System.Windows.Threading.DispatcherTimer _ticker = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly List<double> _groupSeconds = [];
    private DateTime _analysisStarted;
    private DateTime _groupStarted;
    private DiscoveryProgress? _current;

    public bool Changed { get; private set; }
    /// <summary>Item key to category for everything saved in this window.</summary>
    public Dictionary<string, string> Applied { get; } = new(StringComparer.Ordinal);
    private readonly IReadOnlySet<string> _categorized;
    private readonly bool _e5First;

    public CategoryDiscoveryWindow(LocalAiService? ai, IReadOnlyList<DiscoveryItem> items, IReadOnlyList<string> existing,
        Action<IReadOnlyList<string>, string> persist, Action<LocalAiSuggestion?, string>? feedback = null,
        CategoryDiscovery.NameGroup? name = null, string? scope = null, IReadOnlySet<string>? alreadyCategorized = null,
        IReadOnlyDictionary<string, string>? assignments = null, IReadOnlyList<CategoryProposal>? prepared = null, DateTime? preparedAt = null)
    {
        _categorized = alreadyCategorized ?? new HashSet<string>();
        InitializeComponent();
        EditorChrome.ApplyTheme(this);
        SourceInitialized += (_, _) => EditorChrome.ApplyFrame(this);
        Width = Math.Min(Width, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 40);
        _ai = ai;
        _items = items;
        _existing = existing;
        _persist = persist;
        _feedback = feedback;
        _modelOverride = name != null;
        // Groups that E5 confidently puts into an existing category from own examples need no Granite call.
        _name = name ?? ((sources, categories, ct) => ai!.SuggestWithGraniteAsync(sources, categories, ct, assignments, e5First: true));
        _e5First = name == null && assignments is { Count: > 0 } && ai?.E5Ready == true;
        ProposalList.ItemsSource = _proposals;
        Closed += (_, _) => { _running?.Cancel(); _ticker.Stop(); };
        _ticker.Tick += (_, _) => Tick();
        ScopeText.Text = (scope ?? $"{items.Count} Einträge ohne Kategorie")
            + (existing.Count > 0 ? $" · vorhandene Kategorien werden bevorzugt: {string.Join(", ", existing.Take(12))}{(existing.Count > 12 ? " …" : "")}" : "");
        if (prepared is { Count: > 0 })
        {
            foreach (var p in prepared) _proposals.Add(p);
            ResultHeader.Text = $"Vorschläge ({prepared.Count} Gruppen, {prepared.Sum(p => p.Count)} Einträge)";
            UpdateState($"Im Hintergrund vorbereitet{(preparedAt is { } at ? $" am {at:dd.MM.} um {at:HH:mm}" : "")}. Einträge, die inzwischen eine Kategorie haben, sind herausgenommen. Namen prüfen, unpassende Gruppen abwählen, dann „Ausgewählte übernehmen“. „Erneut analysieren“ startet eine frische Analyse.");
        }
        else UpdateState();
    }

    private bool Installed => _modelOverride || _ai?.Model.IsInstalled == true;

    private void UpdateState(string? status = null)
    {
        var busy = _running != null;
        StartButton.Content = Installed ? (_proposals.Count > 0 ? "Erneut analysieren" : "Analyse starten") : "Granite herunterladen (2,27 GB)";
        StartButton.IsEnabled = !busy && (_items.Count >= CategoryDiscovery.MinGroupSize || !Installed);
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ApplyButton.IsEnabled = !busy && _proposals.Any(p => p.Accepted);
        StatusText.Text = status ?? (!Installed
            ? "Granite ist noch nicht installiert. Einmaliger Download von festen offiziellen Adressen mit Prüfsumme; danach läuft die Analyse ohne Internet auf deinem Rechner. 8 GB RAM oder mehr empfohlen."
            : _items.Count < CategoryDiscovery.MinGroupSize ? "Zu wenige Einträge ohne Kategorie für eine Analyse."
            : "Bereit. Der erste Aufruf lädt Granite (etwa 20 Sekunden), danach dauert jede Gruppe etwa 10 bis 20 Sekunden."
              + (_e5First ? " Gruppen, die E5 mit deinen eigenen Beispielen eindeutig einer vorhandenen Kategorie zuordnet, sind ohne Granite in Sekundenbruchteilen fertig." : ""));
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_running != null) return;
        using var operation = _running = new CancellationTokenSource();
        try
        {
            if (!Installed) await DownloadAsync(operation.Token);
            else await AnalyzeAsync(operation.Token);
        }
        finally
        {
            _running = null;
            if (IsLoaded) UpdateState(StatusText.Text);
        }
    }

    private async Task DownloadAsync(CancellationToken ct)
    {
        UpdateState("Granite wird heruntergeladen …");
        try
        {
            var progress = new Progress<LocalAiProgress>(p => OnUi(() => { StatusText.Text = p.Stage; Progress.Value = p.Percent / 100; }));
            await _ai!.Model.InstallAsync(progress, ct);
            StatusText.Text = "Granite ist installiert. Jetzt „Analyse starten“.";
        }
        catch (OperationCanceledException) { StatusText.Text = "Download abgebrochen. Vollständig geprüfte Dateien bleiben erhalten."; }
        catch (Exception ex) { StatusText.Text = "Download fehlgeschlagen: " + ex.Message; }
    }

    private async Task AnalyzeAsync(CancellationToken ct)
    {
        _proposals.Clear();
        UpdateState("Einträge werden nach Inhalt gruppiert …");
        Progress.Value = 0;
        var started = _analysisStarted = DateTime.Now;
        _groupSeconds.Clear();
        _current = null;
        _ticker.Start();
        try
        {
            var progress = new Progress<DiscoveryProgress>(p => OnUi(() => OnProgress(p)));
            var proposals = await CategoryDiscovery.RunAsync(_items, _existing, _name, progress, ct);
            _ticker.Stop();
            Progress.Value = 1;
            _proposals.Clear();
            foreach (var p in proposals) _proposals.Add(p);
            var named = proposals.Count(p => !p.Uncertain);
            ResultHeader.Text = $"Vorschläge ({proposals.Count} Gruppen, {proposals.Sum(p => p.Count)} Einträge)";
            StatusText.Text = proposals.Count == 0
                ? "Keine ausreichend großen Gruppen gefunden. Zum Einordnen einzelner Einträge: Mehr → Alle ohne Kategorie einordnen."
                : $"Fertig nach {(DateTime.Now - started).TotalSeconds:0} s: {named} benannte Gruppen. Namen prüfen und bei Bedarf ändern, unpassende Gruppen abwählen, dann „Ausgewählte übernehmen“.";
        }
        catch (OperationCanceledException) { StatusText.Text = "Analyse abgebrochen. Bereits benannte Gruppen bleiben in der Liste."; }
        catch (Exception ex) { StatusText.Text = "Analyse fehlgeschlagen: " + ex.Message; }
        finally { _ticker.Stop(); }
    }

    private void OnProgress(DiscoveryProgress p)
    {
        if (p.Finished != null)
        {
            _groupSeconds.Add((DateTime.Now - _groupStarted).TotalSeconds);
            _proposals.Add(p.Finished);
            ResultHeader.Text = $"Vorschläge (bisher {p.Done} von {p.Total} Gruppen)";
        }
        else _groupStarted = DateTime.Now;
        _current = p;
        Tick();
    }

    private void Tick()
    {
        if (_current is not { } p || p.Total == 0) return;
        var expected = _groupSeconds.Count == 0 ? FirstGroupSeconds : _groupSeconds.Average();
        var running = p.Finished == null && p.Done < p.Total;
        var inGroup = running ? Math.Min(0.95, (DateTime.Now - _groupStarted).TotalSeconds / expected) : 0;
        Progress.Value = (p.Done + inGroup) / p.Total;
        var elapsed = DateTime.Now - _analysisStarted;
        var remaining = TimeSpan.FromSeconds(Math.Max(0, (p.Total - p.Done - inGroup) * expected));
        var what = !running ? "Vorschläge werden zusammengeführt …"
            : (_e5First ? "E5 prüft, Granite liest nur bei Bedarf," : p.Done == 0 && _groupSeconds.Count == 0 ? "Granite startet und liest" : "Granite liest")
              + $" Gruppe {p.Done + 1} von {p.Total} ({p.GroupSize} Einträge, z. B. „{Shorten(p.Sample)}“)";
        StatusText.Text = $"{what} · läuft seit {Clock(elapsed)}" + (running ? $" · noch etwa {Clock(remaining)}" : "");
    }

    private static string Clock(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    private static string Shorten(string text) { var t = text.ReplaceLineEndings(" ").Trim(); return t.Length <= 50 ? t : t[..47] + "..."; }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _running?.Cancel();

    /// <summary>Progress may arrive from a worker thread; late reports after closing are ignored.</summary>
    private void OnUi(Action action)
    {
        if (Dispatcher.CheckAccess()) { if (IsLoaded) action(); }
        else Dispatcher.BeginInvoke(() => { if (IsLoaded) action(); });
    }

    private void Accepted_Click(object sender, RoutedEventArgs e) => ApplyButton.IsEnabled = _running == null && _proposals.Any(p => p.Accepted);

    private void Name_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox { DataContext: CategoryProposal p } box || !box.IsKeyboardFocusWithin) return;
        p.IsNew = !_existing.Contains(p.Name.Trim(), StringComparer.OrdinalIgnoreCase);
        if (p.Name.Trim().Length > 0) p.Accepted = true;
        ApplyButton.IsEnabled = _running == null && _proposals.Any(x => x.Accepted);
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var chosen = _proposals.Where(p => p.Accepted).ToList();
        if (chosen.FirstOrDefault(p => CategorizationQueue.Validate(p.Name) != null) is { } invalid)
        {
            Feedback.Text = $"Bitte für die Gruppe mit „{invalid.Titles.FirstOrDefault()}“ einen Namen eingeben oder sie abwählen.";
            return;
        }
        if (chosen.Count == 0) return;
        var names = chosen.Select(PathOf).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var fresh = names.Where(n => !_existing.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
        var question = $"{chosen.Sum(p => p.Count)} Einträge in {names.Count} Kategorien zuordnen?\n\n" + string.Join("\n", chosen.Select(p => $"• {PathOf(p)} ({p.Count})"))
            + (fresh.Count > 0 ? $"\n\nNeu angelegt: {string.Join(", ", fresh)}" : "")
            + (chosen.Sum(p => p.Keys.Count(_categorized.Contains)) is var replaced and > 0
                ? $"\n\n{replaced} dieser Einträge haben schon eine Kategorie; sie wird ersetzt."
                : "\n\nDiese Einträge haben noch keine Kategorie, es wird nichts überschrieben.")
            + " Einzelne Einträge lassen sich später im Einordnungsfenster ändern.";
        if (MessageBox.Show(this, question, AppNames.KnowledgeBase, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        var applied = 0;
        foreach (var p in chosen)
        {
            var category = PathOf(p);
            try { _persist(p.Keys, category); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                App.LogCrash(ex);
                Feedback.Text = $"Speichern fehlgeschlagen bei „{category}“: {ex.Message}";
                break;
            }
            _feedback?.Invoke(new LocalAiSuggestion("", p.ProposedName, p.Reason, p.Uncertain, null, p.Method), category);
            foreach (var key in p.Keys) Applied[key] = category;
            _proposals.Remove(p);
            applied += p.Count;
            Changed = true;
        }
        if (applied > 0) Feedback.Text = $"{applied} Einträge zugeordnet. Ab jetzt schlägt E5 für neue Einträge passende Kategorien vor.";
        UpdateState(StatusText.Text);
    }

    /// <summary>Name as a clean category path; "Haupt › Haupt" becomes "Haupt".</summary>
    private static string PathOf(CategoryProposal p) => KnowledgeCategories.Parse(p.Name).ToString();
}

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClipwellWin.Models;
using ClipwellWin.Services;
using Brush = System.Windows.Media.Brush;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace ClipwellWin.Views;

/// <summary>
/// Report for the E5 classifiers: cross-validated comparison (chart and table), per-category results with a
/// confusion matrix, real-world feedback from saved suggestions and the run history. Also selects the method.
/// </summary>
public partial class ClassifierReportWindow : Window
{
    private const int MaxConfusionCategories = 15;
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private readonly E5Categorization? _e5;
    private readonly AppSettings _settings;
    private readonly Action _save;
    private readonly Func<IReadOnlyList<string>, Task>? _categorize;
    private readonly CancellationTokenSource _closing = new();
    private BenchmarkReport? _report;
    private bool _loading;

    public ClassifierReportWindow(E5Categorization? e5, AppSettings settings, Action save, bool runOnOpen = true,
        Func<IReadOnlyList<string>, Task>? categorize = null)
    {
        _categorize = categorize;
        InitializeComponent();
        EditorChrome.ApplyTheme(this);
        SourceInitialized += (_, _) => EditorChrome.ApplyFrame(this);
        Width = Math.Min(Width, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 40);
        _e5 = e5;
        _settings = settings;
        _save = save;
        Closed += (_, _) => _closing.Cancel();
        FillMethodBox();
        Render();
        Loaded += async (_, _) =>
        {
            if (runOnOpen && _e5?.Ready == true) await RunAsync();
            else if (_e5?.Ready != true) StatusText.Text = "Das E5-Suchmodell fehlt. Bitte unter Suche oder Kontrollstation → Lokale KI laden.";
        };
    }

    public BenchmarkReport? Report => _report;

    private string Setting => ClassifierMethods.Normalize(_settings.CategorizationMethod);
    private string ActiveMethod => Setting == ClassifierMethods.Auto ? _report?.Recommended ?? ClassifierMethods.Rocchio : Setting;

    // ---- Actions -------------------------------------------------------------------------------------------

    private async void Run_Click(object sender, RoutedEventArgs e) => await RunAsync();

    public async Task RunAsync()
    {
        if (_e5 == null || !RunButton.IsEnabled) return;
        RunButton.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        Progress.Value = 0;
        StatusText.Text = "Werte gespeicherte Zuordnungen aus …";
        try
        {
            var assignments = (_settings.BrainAnnotations ?? []).Where(a => a.Value.Category.Trim().Length > 0)
                .ToDictionary(a => a.Key, a => a.Value.Category);
            var progress = new Progress<double>(p => Progress.Value = p);
            var (report, message) = await _e5.BenchmarkAsync(assignments, progress, _closing.Token);
            if (report == null) { StatusText.Text = message; return; }
            _report = report;
            CategorizationReporting.Append(_settings.CategorizationRuns ??= [], CategorizationReporting.ToRun(report), CategorizationReporting.MaxRuns);
            try { _save(); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { App.LogCrash(ex); }
            StatusText.Text = $"Ausgewertet: {report.Examples} Beispiele in {report.Categories} Kategorien.";
            FillMethodBox();
            Render();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { StatusText.Text = "Auswertung fehlgeschlagen: " + ex.Message; }
        finally
        {
            if (!_closing.IsCancellationRequested)
            {
                RunButton.IsEnabled = true;
                Progress.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var text = CategorizationReporting.ToMarkdown(_report, ActiveMethod, CategorizationReporting.Summarize(_settings.CategorizationFeedback ?? []),
            _settings.CategorizationRuns ?? []);
        try { System.Windows.Clipboard.SetText(text); StatusText.Text = "Bericht als Markdown kopiert."; }
        catch (System.Runtime.InteropServices.ExternalException ex) { StatusText.Text = "Kopieren fehlgeschlagen: " + ex.Message; }
    }

    private void FillMethodBox()
    {
        _loading = true;
        MethodBox.Items.Clear();
        var recommended = _report != null ? $" (zurzeit: {ClassifierMethods.DisplayName(_report.Recommended)})" : "";
        MethodBox.Items.Add(new ComboBoxItem { Tag = ClassifierMethods.Auto, Content = "Automatisch – nach Auswertung" + recommended });
        foreach (var m in ClassifierMethods.All) MethodBox.Items.Add(new ComboBoxItem { Tag = m, Content = ClassifierMethods.DisplayName(m) });
        MethodBox.SelectedItem = MethodBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == Setting);
        _loading = false;
    }

    private void MethodBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || MethodBox.SelectedItem is not ComboBoxItem { Tag: string method }) return;
        _settings.CategorizationMethod = method;
        try { _save(); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { App.LogCrash(ex); StatusText.Text = "Speichern fehlgeschlagen: " + ex.Message; }
        StatusText.Text = $"Vorschläge nutzen jetzt: {ClassifierMethods.DisplayName(ActiveMethod)}.";
        Render();
    }

    private void DetailBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading) RenderDetail();
    }

    // ---- Rendering -----------------------------------------------------------------------------------------

    private void Render()
    {
        RenderComparison();
        _loading = true;
        DetailBox.Items.Clear();
        if (_report != null)
            foreach (var m in _report.Methods) DetailBox.Items.Add(new ComboBoxItem { Tag = m.Method, Content = ClassifierMethods.DisplayName(m.Method) });
        DetailBox.SelectedItem = DetailBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == ActiveMethod);
        _loading = false;
        RenderDetail();
        RenderSuspicious();
        RenderFeedback();
        RenderHistory();
    }

    private void RenderComparison()
    {
        ChartGrid.Children.Clear();
        ChartGrid.RowDefinitions.Clear();
        ChartGrid.ColumnDefinitions.Clear();
        if (_report == null)
        {
            ProtocolText.Text = "Noch keine Auswertung. „Neu auswerten“ prüft alle Verfahren auf deinen gespeicherten Zuordnungen.";
            Table(MethodTable, [], []);
            return;
        }
        ProtocolText.Text = $"Stand {_report.Date.ToString("dd.MM.yyyy HH:mm", De)} · {_report.Examples} Beispiele in {_report.Categories} Kategorien · "
            + $"{_report.Folds}-fache Kreuzvalidierung, {_report.Repeats} Wiederholungen · empfohlen: {ClassifierMethods.DisplayName(_report.Recommended)} · "
            + $"aktiv: {ClassifierMethods.DisplayName(ActiveMethod)}"
            + (_report.EmbeddingMilliseconds > 0 ? $" · Einbetten der Texte: {_report.EmbeddingMilliseconds.ToString("0", De)} ms (bereits berechnete Texte kommen aus dem Zwischenspeicher)" : "");
        ChartGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        ChartGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ChartGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        var accent = Accent();
        var row = 0;
        foreach (var m in _report.Methods)
        {
            ChartGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            var hit = new Border { Background = System.Windows.Media.Brushes.Transparent, ToolTip = MethodTooltip(m) };
            Grid.SetRow(hit, row);
            Grid.SetColumnSpan(hit, 3);
            ChartGrid.Children.Add(hit);
            Add(ChartGrid, Label(m.Method), row, 0);
            var track = new Grid { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
            var value = Math.Clamp(m.MacroF1, 0, 1);
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(value, 0.0001), GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - value, 0.0001), GridUnitType.Star) });
            var axis = new Border { Width = 1, Background = Muted(), HorizontalAlignment = HorizontalAlignment.Left, Height = 22 };
            Grid.SetColumnSpan(axis, 2);
            track.Children.Add(axis);
            track.Children.Add(new Border { Background = accent, Height = 14, CornerRadius = new CornerRadius(0, 4, 4, 0) });
            Add(ChartGrid, track, row, 1);
            Add(ChartGrid, Text($"{CategorizationReporting.Number(m.MacroF1)} ± {CategorizationReporting.Number(m.MacroF1Spread)}", align: TextAlignment.Right), row, 2);
            row++;
        }
        ChartGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var scale = new Grid { IsHitTestVisible = false };
        scale.Children.Add(Text("0", muted: true, size: 11));
        scale.Children.Add(Text("0,5", muted: true, size: 11, align: TextAlignment.Center));
        scale.Children.Add(Text("1", muted: true, size: 11, align: TextAlignment.Right));
        Add(ChartGrid, scale, row, 1);

        Table(MethodTable, ["Verfahren", "Macro-F1", "Top-1", "beantwortet", "davon richtig (95 %-Intervall)", "Top-3", "Abdeckung bei max. 10 % Fehler",
                "Unbekanntes abgewiesen", "Schwelle kalibriert", "ECE", "Lernen / Vorschlag"],
            _report.Methods.Select(m => CategorizationReporting.Row(m).Prepend(ClassifierMethods.DisplayName(m.Method)).ToArray()).ToList(), _report.Methods.Select((m, i) => (m, i)).Where(x => x.m.Method == ActiveMethod).Select(x => x.i).ToHashSet());
    }

    private FrameworkElement Label(string method)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        panel.Children.Add(Text(ClassifierMethods.DisplayName(method), bold: method == ActiveMethod));
        var tags = new List<string>();
        if (_report?.Recommended == method) tags.Add("empfohlen");
        if (ActiveMethod == method) tags.Add("aktiv");
        if (tags.Count > 0) panel.Children.Add(Text("  · " + string.Join(" · ", tags), muted: true, size: 11));
        return panel;
    }

    private static string MethodTooltip(MethodReport m)
        => $"{ClassifierMethods.DisplayName(m.Method)}\nMacro-F1 {CategorizationReporting.Number(m.MacroF1)} ± {CategorizationReporting.Number(m.MacroF1Spread)}"
           + $"\nbeantwortet {CategorizationReporting.Percent(m.Coverage)}, davon richtig {CategorizationReporting.Percent(m.SelectiveAccuracy)}"
           + $"\nTop-3 {CategorizationReporting.Percent(m.TopThree)} · Abdeckung bei max. 10 % Fehler {CategorizationReporting.Percent(m.CoverageAtTenPercentError)}";

    private void RenderDetail()
    {
        ConfusionGrid.Children.Clear();
        ConfusionGrid.RowDefinitions.Clear();
        ConfusionGrid.ColumnDefinitions.Clear();
        ConfusionNote.Text = "";
        var method = (DetailBox.SelectedItem as ComboBoxItem)?.Tag as string;
        if (_report?.Get(method ?? "") is not { } m)
        {
            Table(CategoryTable, [], []);
            return;
        }
        Table(CategoryTable, ["Kategorie", "Beispiele", "Precision", "Recall", "F1", "häufigste Verwechslung"],
            m.PerCategory.Select(c => new[]
            {
                c.Category, c.Examples.ToString(De), CategorizationReporting.Percent(c.Precision), CategorizationReporting.Percent(c.Recall),
                CategorizationReporting.Number(c.F1), c.MostConfusedWith ?? "–",
            }).ToList());

        var shown = Enumerable.Range(0, _report.Labels.Count).Where(i => m.Confusion[i].Sum() > 0)
            .OrderByDescending(i => m.Confusion[i].Sum()).Take(MaxConfusionCategories).OrderBy(i => _report.Labels[i]).ToList();
        if (_report.Labels.Count > MaxConfusionCategories && shown.Count == MaxConfusionCategories)
            ConfusionNote.Text = $"Gezeigt werden die {shown.Count} Kategorien mit den meisten Beispielen.";
        var columns = shown.Append(_report.Labels.Count).ToList();
        ConfusionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (var _ in columns) ConfusionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        ConfusionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var j = 0; j < columns.Count; j++)
        {
            var name = columns[j] == _report.Labels.Count ? "kein Vorschlag" : _report.Labels[columns[j]];
            var header = Text(Shorten(name, 18), muted: true, size: 11);
            header.LayoutTransform = new RotateTransform(-60);
            header.ToolTip = name;
            header.Margin = new Thickness(0, 0, 0, 4);
            header.VerticalAlignment = VerticalAlignment.Bottom;
            Add(ConfusionGrid, header, 0, j + 1);
        }
        var accent = Accent();
        for (var r = 0; r < shown.Count; r++)
        {
            ConfusionGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            var i = shown[r];
            var total = m.Confusion[i].Sum();
            var rowLabel = Text(Shorten(_report.Labels[i], 28), size: 12);
            rowLabel.Margin = new Thickness(0, 0, 10, 0);
            rowLabel.VerticalAlignment = VerticalAlignment.Center;
            rowLabel.ToolTip = _report.Labels[i];
            Add(ConfusionGrid, rowLabel, r + 1, 0);
            for (var j = 0; j < columns.Count; j++)
            {
                var count = m.Confusion[i][columns[j]];
                var share = total == 0 ? 0 : (double)count / total;
                var target = columns[j] == _report.Labels.Count ? "kein Vorschlag" : _report.Labels[columns[j]];
                var fill = accent.Clone();
                fill.Opacity = count == 0 ? 0 : 0.12 + 0.88 * share;
                var cell = new Border
                {
                    Background = fill, BorderBrush = (Brush)FindResource("EditorBorderBrush"), BorderThickness = new Thickness(0.5),
                    Margin = new Thickness(1), CornerRadius = new CornerRadius(2),
                    ToolTip = $"{_report.Labels[i]} → {target}: {count} von {total} ({CategorizationReporting.Percent(share)})",
                    Child = count == 0 ? null : Text(count.ToString(De), size: 11, align: TextAlignment.Center, bold: columns[j] == i),
                };
                Add(ConfusionGrid, cell, r + 1, j + 1);
            }
        }
    }

    private void RenderSuspicious()
    {
        var list = _report?.Suspicious ?? [];
        SuspiciousButton.IsEnabled = _categorize != null && list.Any(s => s.Key.Length > 0);
        if (_report == null) { Table(SuspiciousTable, ["Noch keine Auswertung."], []); return; }
        if (list.Count == 0) { Table(SuspiciousTable, ["Keine auffälligen Zuordnungen."], []); return; }
        Table(SuspiciousTable, ["Eintrag (Auszug)", "gespeichert", "Rocchio sagt"],
            list.Select(s => new[] { s.Excerpt, s.Category, s.Suggested }).ToList());
    }

    private async void Suspicious_Click(object sender, RoutedEventArgs e)
    {
        var keys = (_report?.Suspicious ?? []).Select(s => s.Key).Where(k => k.Length > 0).Distinct().ToList();
        if (_categorize == null || keys.Count == 0) return;
        await _categorize(keys);
    }

    private void RenderFeedback()
    {
        var summary = CategorizationReporting.Summarize(_settings.CategorizationFeedback ?? []);
        if (summary.Count == 0)
        {
            Table(FeedbackTable, ["Noch keine Vorschläge gespeichert. Sie werden beim Zuordnen automatisch mitgezählt."], []);
            return;
        }
        Table(FeedbackTable, ["Verfahren", "Vorschläge", "angenommen", "korrigiert", "ohne Empfehlung", "Treffer, wenn vorgeschlagen", "richtige unter Top-3"],
            summary.Select(s => new[]
            {
                ClassifierMethods.DisplayName(s.Method), s.Suggestions.ToString(De), $"{s.Accepted} ({CategorizationReporting.Percent(s.AcceptedShare)})",
                s.Corrected.ToString(De), s.NoSuggestion.ToString(De), CategorizationReporting.Percent(s.Precision), CategorizationReporting.Percent(s.TopThreeShare),
            }).ToList());
    }

    private void RenderHistory()
    {
        var runs = (_settings.CategorizationRuns ?? []).OrderByDescending(r => r.Date).Take(20).ToList();
        if (runs.Count == 0)
        {
            Table(HistoryTable, ["Noch keine gespeicherte Auswertung."], []);
            return;
        }
        Table(HistoryTable, ["Datum", "Beispiele", "Kategorien", "empfohlen", "Macro-F1 Rocchio", "Macro-F1 empfohlen"],
            runs.Select(r => new[]
            {
                r.Date.ToString("dd.MM.yyyy HH:mm", De), r.Examples.ToString(De), r.Categories.ToString(De), ClassifierMethods.DisplayName(r.Recommended),
                CategorizationReporting.Number(r.Results.FirstOrDefault(x => x.Method == ClassifierMethods.Rocchio)?.MacroF1 ?? 0),
                CategorizationReporting.Number(r.Results.FirstOrDefault(x => x.Method == r.Recommended)?.MacroF1 ?? 0),
            }).ToList());
    }

    // ---- Helpers -------------------------------------------------------------------------------------------

    private void Table(Grid grid, string[] headers, List<string[]> rows, ISet<int>? bold = null)
    {
        grid.Children.Clear();
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();
        if (headers.Length == 0) return;
        for (var c = 0; c < headers.Length; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = c == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var c = 0; c < headers.Length; c++)
        {
            var h = Text(headers[c], muted: true, size: 11, align: c == 0 ? TextAlignment.Left : TextAlignment.Right);
            h.Margin = new Thickness(c == 0 ? 0 : 14, 0, 0, 6);
            h.TextWrapping = TextWrapping.Wrap;
            h.MaxWidth = c == 0 ? double.PositiveInfinity : 130;
            Add(grid, h, 0, c);
        }
        for (var r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var line = new Border { BorderBrush = (Brush)FindResource("EditorBorderBrush"), BorderThickness = new Thickness(0, 1, 0, 0) };
            Grid.SetColumnSpan(line, headers.Length);
            Add(grid, line, r + 1, 0);
            for (var c = 0; c < rows[r].Length && c < headers.Length; c++)
            {
                var t = Text(rows[r][c], bold: bold?.Contains(r) == true, align: c == 0 ? TextAlignment.Left : TextAlignment.Right);
                t.Margin = new Thickness(c == 0 ? 0 : 14, 5, 0, 5);
                Add(grid, t, r + 1, c);
            }
        }
    }

    private TextBlock Text(string text, bool muted = false, double size = 12.5, TextAlignment align = TextAlignment.Left, bool bold = false)
        => new()
        {
            Text = text, FontSize = size, TextAlignment = align, VerticalAlignment = VerticalAlignment.Center,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = muted ? Muted() : (Brush)FindResource("EditorTextBrush"),
        };

    private Brush Muted() => (Brush)FindResource("EditorMutedBrush");

    private SolidColorBrush Accent() => TryFindResource("OnboardingAccentBrush") as SolidColorBrush ?? new SolidColorBrush(Colors.DarkOrange);

    private static void Add(Grid grid, UIElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}

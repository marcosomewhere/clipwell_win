using System.Windows;
using Merkautomat.Services;

namespace Merkautomat.Views;

public partial class BrainKnowledgeDialog
{
    private LocalAiService? _localAi;
    private CancellationTokenSource? _aiRequest;
    private LocalAiSuggestion? _aiSuggestion;
    private HashSet<string>? _aiKeys;
    private readonly IReadOnlyDictionary<string, string>? _assignments;

    private void InitializeLocalAi(LocalAiService? service)
    {
        _localAi = service;
        if (service == null) return;
        LocalAiPanel.Visibility = Visibility.Visible;
        SizeChanged += (_, _) => { if (ActualHeight < 720) LocalAiPanel.IsExpanded = false; };
        LocalAiPanel.Header = service.Ready ? service.UsesCategoryOnly ? $"{service.DisplayName} · vorhandene Kategorie vorschlagen" : $"{service.DisplayName} · Kategorie und Titel vorschlagen"
            : service.Enabled ? "Lokale KI · Download in der Kontrollstation erforderlich" : "Lokale KI · in der Kontrollstation aktivieren";
        AiSuggestButton.IsEnabled = service.Ready;
        AiStatus.Text = service.Ready ? service.DisplayName + " erstellt einen Vorschlag aus der Auswahl …"
            : service.Enabled ? "Das ausgewählte Modell fehlt. Download unter Kontrollstation → Lokale KI."
            : "Automatische Kategorien: Modell unter Kontrollstation → Lokale KI aktivieren.";
        Closed += (_, _) => _aiRequest?.Cancel();
    }

    /// <summary>The model suggestion that belongs to the current selection, for usage statistics.</summary>
    public LocalAiSuggestion? AiSuggestionForSelection
        => _aiSuggestion != null && _aiKeys != null && _aiKeys.SetEquals(SelectedSources.Select(s => s.Key)) ? _aiSuggestion : null;

    private void InvalidateAiSuggestion()
    {
        if (_aiKeys == null || _sources == null || _aiKeys.SetEquals(SelectedSources.Select(s => s.Key))) return;
        _aiRequest?.Cancel();
        _aiSuggestion = null;
        _aiKeys = null;
        AiApplyButton.IsEnabled = false;
        LocalAiPanel.Header = "Lokale KI · Auswahl geändert";
        AiStatus.Text = "Auswahl geändert. Bitte den Kategorie-Vorschlag neu erstellen.";
    }

    private void AiSuggest_Click(object sender, RoutedEventArgs e) => SuggestCategoryAsync().Forget("KI-Vorschlag erstellen");
    private void AiCancel_Click(object sender, RoutedEventArgs e) => _aiRequest?.Cancel();

    private async Task SuggestCategoryAsync()
    {
        if (_closed || _aiRequest != null || _localAi?.Ready != true) return;
        var sources = SelectedSources;
        if (sources.Count == 0) { AiStatus.Text = "Bitte mindestens eine Quelle markieren."; return; }
        using var request = _aiRequest = new CancellationTokenSource();
        _aiKeys = sources.Select(s => s.Key).ToHashSet();
        _aiSuggestion = null;
        AiApplyButton.IsEnabled = AiSuggestButton.IsEnabled = false;
        AiCancelButton.Visibility = Visibility.Visible;
        AiStatus.Text = _localAi.UsesE5
            ? "E5 wertet die Auswahl lokal aus … Beim ersten Aufruf werden Modell und gespeicherte Beispiele einmalig berechnet, das kann einige Sekunden dauern."
            : _localAi.DisplayName + " wertet die Auswahl lokal aus … Beim ersten Aufruf wird das Modell geladen.";
        LocalAiPanel.Header = "Lokale KI · Vorschlag wird erstellt …";
        LocalAiPanel.IsExpanded = ActualHeight >= 720;
        try
        {
            var categories = Category.Items.Cast<string>().ToList();
            var suggestion = await _localAi.SuggestAsync(sources, categories, request.Token, _assignments);
            if (_closed || request.IsCancellationRequested || _aiKeys == null) return;
            _aiSuggestion = suggestion;
            LocalAiPanel.Header = suggestion.Uncertain ? "Lokale KI · keine eindeutige Kategorie"
                : $"Lokale KI · Vorschlag: {suggestion.Category}";
            AiApplyButton.IsEnabled = !suggestion.Uncertain;
            var scope = $"Auszug: bis zu {LocalAiCategorization.MaxSources} Quellen, je {LocalAiCategorization.MaxTextPerSource} Zeichen.";
            var label = suggestion.Title.Length == 0 ? suggestion.Category : $"{suggestion.Category} · {suggestion.Title}";
            var candidates = suggestion.Candidates is { Count: > 0 } list ? " Beste Kandidaten: " + string.Join(", ", list.Select(c => c.Category)) + "." : "";
            AiStatus.Text = suggestion.Uncertain ? $"Keine eindeutige Kategorie: {suggestion.Reason}{candidates} {scope}"
                : $"{label} — {suggestion.Reason} {scope}";
        }
        catch (OperationCanceledException) { if (!_closed && _aiKeys != null) { LocalAiPanel.Header = "Lokale KI · Vorschlag abgebrochen"; AiStatus.Text = "Vorschlag abgebrochen."; } }
        catch (Exception ex) { if (!_closed) { LocalAiPanel.Header = "Lokale KI · kein Vorschlag verfügbar"; AiStatus.Text = "Kein KI-Vorschlag: " + ex.Message; } }
        finally
        {
            _aiRequest = null;
            if (!_closed)
            {
                AiSuggestButton.IsEnabled = _localAi.Ready;
                AiCancelButton.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void AiApply_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _aiSuggestion is not { Uncertain: false } suggestion || _aiKeys == null
            || !_aiKeys.SetEquals(SelectedSources.Select(s => s.Key))) return;
        if (suggestion.Title.Length > 0) TopicName.Text = suggestion.Title;
        Category.Text = suggestion.Category;
        AiApplyButton.IsEnabled = false;
        AiStatus.Text = suggestion.Title.Length == 0 ? "Kategorie übernommen. Der Titel bleibt erhalten. Zum Behalten bitte speichern."
            : "Vorschlag übernommen. Titel und Kategorie lassen sich weiter bearbeiten. Zum Behalten bitte speichern.";
    }
}

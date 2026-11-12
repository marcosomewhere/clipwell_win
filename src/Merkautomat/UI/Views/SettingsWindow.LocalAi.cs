using System.Windows;
using System.Windows.Controls;
using Merkautomat.Services;

namespace Merkautomat.Views;

public partial class SettingsWindow
{
    private bool _localAiLoaded;
    private bool _localAiClosed;
    private bool _settingLanguageModel;
    private CancellationTokenSource? _localAiOperation;

    private void InitializeLocalAi()
    {
        EditorChrome.ApplyTheme(this);
        SelectByTag(LocalAiModelBox, LocalAiModel.Normalize(_app.CurrentSettings.LocalAiModel));
        SelectLanguageModel();
        SelectByTag(AiComputeModeBox, AiExecutionPolicy.Normalize(_app.CurrentSettings.LocalAiComputeMode));
        SelectByTag(BackgroundModeBox, BackgroundWork.Normalize(_app.CurrentSettings.BackgroundWorkMode));
        BackgroundKeepAwakeBox.IsChecked = _app.CurrentSettings.BackgroundKeepAwake;
        _localAiLoaded = true;
        UpdateLocalAiControls();
        UpdateBackgroundStatus();
        _app.BackgroundStatusChanged += App_BackgroundStatusChanged;
        if (_app.LocalAi.Execution != null) _app.LocalAi.Execution.Changed += AiExecutionChanged;
        Closed += (_, _) =>
        {
            _localAiClosed = true;
            _localAiOperation?.Cancel();
            _app.BackgroundStatusChanged -= App_BackgroundStatusChanged;
            if (_app.LocalAi.Execution != null) _app.LocalAi.Execution.Changed -= AiExecutionChanged;
        };
    }

    private void App_BackgroundStatusChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(UpdateBackgroundStatus);

    private void UpdateBackgroundStatus()
    {
        if (_localAiClosed) return;
        BackgroundStatusText.Text = _app.BackgroundStatus.Length > 0 ? _app.BackgroundStatus : "Wird geprüft …";
        BackgroundKeepAwakeBox.IsEnabled = BackgroundWork.Normalize(_app.CurrentSettings.BackgroundWorkMode) != BackgroundWork.Off;
    }

    private void BackgroundMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_localAiLoaded) return;
        _app.CurrentSettings.BackgroundWorkMode = BackgroundWork.Normalize((BackgroundModeBox.SelectedItem as ComboBoxItem)?.Tag as string);
        _app.SaveSettings();
        _app.BackgroundSettingsChanged();
        UpdateBackgroundStatus();
    }

    private void BackgroundKeepAwake_Changed(object sender, RoutedEventArgs e)
    {
        if (!_localAiLoaded) return;
        _app.CurrentSettings.BackgroundKeepAwake = BackgroundKeepAwakeBox.IsChecked == true;
        _app.SaveSettings();
        _app.BackgroundSettingsChanged();
    }

    private void LocalAiModel_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_localAiLoaded) return;
        _app.CurrentSettings.LocalAiLanguageModel = _app.LocalAi.Model.ModelId;
        _app.CurrentSettings.LocalAiModel = LocalAiModel.Normalize((LocalAiModelBox.SelectedItem as ComboBoxItem)?.Tag as string);
        _app.SaveSettings();
        _localAiOperation?.Cancel();
        _app.LocalAi.Stop();
        UpdateLocalAiControls();
    }

    private void SelectLanguageModel()
    {
        _settingLanguageModel = true;
        try { SelectByTag(LanguageModelBox, _app.LocalAi.Model.ModelId); }
        finally { _settingLanguageModel = false; }
    }

    private void LanguageModel_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_localAiLoaded || _settingLanguageModel) return;
        _app.CurrentSettings.LocalAiLanguageModel = LocalAiModel.ResolveLanguageModelId(null, (LanguageModelBox.SelectedItem as ComboBoxItem)?.Tag as string);
        _app.SaveSettings();
        _localAiOperation?.Cancel();
        _app.LocalAi.Stop();
        UpdateLocalAiControls();
    }

    private void UpdateLocalAiControls(string? status = null)
    {
        var busy = _localAiOperation != null;
        var enabled = _app.LocalAi.Enabled;
        var installed = _app.LocalAi.Ready;
        var e5 = _app.LocalAi.UsesE5;
        var categoryModel = _app.LocalAi.CategoryModel;
        LocalAiModelBox.IsEnabled = !busy;
        LanguageModelBox.IsEnabled = !busy;
        var languageModel = _app.LocalAi.Model;
        AiComputeModeBox.IsEnabled = !busy;
        GpuRuntimeButton.IsEnabled = !busy;
        AiBenchmarkButton.IsEnabled = !busy && languageModel.IsInstalled;
        GpuRuntimeButton.Content = _app.LocalAi.Execution?.Runtime.IsInstalled == true ? "GPU-Unterstützung prüfen / reparieren" : "GPU-Unterstützung laden (38 MB)";
        UpdateAiExecution();
        LanguageModelDownloadButton.IsEnabled = !busy;
        LanguageModelDownloadButton.Content = languageModel.IsInstalled ? "Sprachmodell prüfen / reparieren" : "Sprachmodell herunterladen";
        LanguageModelTestButton.IsEnabled = languageModel.IsInstalled && !busy;
        LanguageModelStatus.Text = $"{languageModel.Name}: {(languageModel.IsInstalled ? "bereit" : "noch nicht installiert")}. Download ca. {languageModel.DownloadSize}; lokal auf CPU oder GPU mit Speicherreserve für Windows.";
        LocalAiDownloadButton.IsEnabled = enabled && !busy;
        LocalAiDownloadButton.Visibility = e5 && installed ? Visibility.Collapsed : Visibility.Visible;
        LocalAiDownloadButton.Content = installed ? "Download prüfen / reparieren" : "Modell herunterladen";
        LocalAiTestButton.IsEnabled = enabled && installed && !busy;
        LocalAiEvaluateButton.Visibility = e5 && installed ? Visibility.Visible : Visibility.Collapsed;
        LocalAiEvaluateButton.IsEnabled = !busy;
        LocalAiCancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        LocalAiProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        LocalAiStatus.Text = status ?? (!enabled ? "Automatische Einordnung ausgeschaltet." : installed ? $"{_app.LocalAi.DisplayName} ist bereit." : "Das ausgewählte Modell fehlt. Bitte herunterladen.");
        LocalAiModelHint.Text = !enabled ? "Für vorhandene Kategorien genügt das mitgelieferte E5-Modell. Granite und Qwen können zusätzlich neue Kategorien und Titel formulieren."
            : categoryModel != null
            ? $"{categoryModel.Name}: einmalig ca. {categoryModel.DownloadSize} herunterladen. Ordnet Texte vorhandenen Kategorien zu; erzeugt keine Titel oder Antworten und lernt nicht aus gespeicherten Zuordnungen. "
                + (categoryModel.Id == CategoryModelFiles.MiniLmId
                    ? "Vorwiegend für englische Texte geeignet; für Deutsch ist E5 meist die passendere Ausgangswahl."
                    : "Mehrsprachige Texte, auch Deutsch. Der Hersteller empfiehlt englische Kategorienamen; die Qualität hängt stark von deren Formulierung ab.")
            : e5
            ? "Nutzt das mitgelieferte E5-Suchmodell gemeinsam mit der semantischen Suche. Kein zusätzlicher Download. Vergleicht Texte mit vorhandenen Kategorien und behält den Titel bei."
            : $"{_app.LocalAi.SuggestionModel.Name}: einmalig ca. {_app.LocalAi.SuggestionModel.DownloadSize} herunterladen. Läuft ohne Konto oder API-Schlüssel auf deinem Rechner. 8 GB RAM oder mehr empfohlen; die Geschwindigkeit hängt von der CPU ab.";
    }

    private void LocalAiDownload_Click(object sender, RoutedEventArgs e) => DownloadLocalAiAsync(false).Forget("Einordnungsmodell laden");
    private void LanguageModelDownload_Click(object sender, RoutedEventArgs e) => DownloadLocalAiAsync(true).Forget("Sprachmodell laden");

    private async Task DownloadLocalAiAsync(bool languageModel)
    {
        if (_localAiOperation != null) return;
        using var operation = _localAiOperation = new CancellationTokenSource();
        _app.LocalAi.Stop();
        LocalAiProgressBar.IsIndeterminate = false;
        LocalAiProgressBar.Value = 0;
        UpdateLocalAiControls("Download wird vorbereitet …");
        var result = "Das Modell ist bereit. Mit „Funktion testen“ lässt sich die lokale Auswertung prüfen.";
        try
        {
            var progress = new Progress<LocalAiProgress>(p =>
            {
                if (_localAiClosed || _localAiOperation != operation) return;
                LocalAiStatus.Text = p.Stage;
                LocalAiProgressBar.Value = p.Percent;
            });
            if (languageModel) await _app.LocalAi.Model.InstallAsync(progress, operation.Token);
            else await _app.LocalAi.InstallSelectedAsync(progress, operation.Token);
        }
        catch (OperationCanceledException) { result = "Download abgebrochen. Vollständig geprüfte Dateien bleiben erhalten."; }
        catch (Exception ex) { result = "Download fehlgeschlagen: " + ex.Message; }
        finally { _localAiOperation = null; if (!_localAiClosed) UpdateLocalAiControls(result); }
    }

    private void LocalAiTest_Click(object sender, RoutedEventArgs e) => TestLocalAiAsync(false).Forget("Einordnungsmodell testen");
    private void LanguageModelTest_Click(object sender, RoutedEventArgs e) => TestLocalAiAsync(true).Forget("Sprachmodell testen");

    private async Task TestLocalAiAsync(bool languageModel)
    {
        if (_localAiOperation != null) return;
        using var operation = _localAiOperation = new CancellationTokenSource();
        LocalAiProgressBar.IsIndeterminate = true;
        var name = languageModel ? _app.LocalAi.Model.Name : _app.LocalAi.DisplayName;
        UpdateLocalAiControls($"{name} wird mit einem Beispieltext getestet …");
        var result = "";
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var english = !languageModel && _app.LocalAi.CategoryModel?.Id == CategoryModelFiles.MiniLmId;
            BrainSource[] sources = [new BrainSource("test", english ? "Travel plans" : "Urlaubsplanung",
                    english ? "Travel plans for our vacation: we are travelling to Italy for a holiday, booking flights and hotels, and visiting museums as tourists."
                        : "Wir planen eine Reise nach Italien. Auf unserer Liste stehen Zugtickets, ein Hotel in Rom und die Buchung einer Stadtführung.", "", DateTime.MinValue)];
            string[] categories = english ? ["Travel and tourism", "Office work and business", "Computers and technology"] : ["Reisen", "Arbeit", "Technik"];
            var suggestion = languageModel
                ? await _app.LocalAi.SuggestWithLanguageModelAsync(sources, categories, operation.Token)
                : await _app.LocalAi.SuggestAsync(sources, categories, operation.Token);
            result = suggestion.Uncertain ? $"{name} antwortet, konnte den Beispieltext aber nicht eindeutig zuordnen. {suggestion.Reason}"
                : $"Test erfolgreich ({timer.Elapsed.TotalSeconds:0.00} s): {suggestion.Category}" + (suggestion.Title.Length > 0 ? $" · {suggestion.Title}" : "");
        }
        catch (OperationCanceledException) { result = "Test abgebrochen."; }
        catch (Exception ex) { result = "Test fehlgeschlagen: " + ex.Message; }
        finally { _localAiOperation = null; if (!_localAiClosed) UpdateLocalAiControls(result); }
    }

    private void LocalAiEvaluate_Click(object sender, RoutedEventArgs e) => _app.OpenCategorizationReport(this);
    private void LocalAiCancel_Click(object sender, RoutedEventArgs e) => _localAiOperation?.Cancel();
}

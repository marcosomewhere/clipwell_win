using System.Windows;
using System.Windows.Controls;
using ClipwellWin.Services;

namespace ClipwellWin.Views;

public partial class SettingsWindow
{
    private bool _localAiLoaded;
    private bool _localAiClosed;
    private CancellationTokenSource? _localAiOperation;

    private void InitializeLocalAi()
    {
        EditorChrome.ApplyTheme(this);
        SelectByTag(LocalAiModelBox, LocalAiModel.Normalize(_app.CurrentSettings.LocalAiModel));
        _localAiLoaded = true;
        UpdateLocalAiControls();
        Closed += (_, _) => { _localAiClosed = true; _localAiOperation?.Cancel(); };
    }

    private void LocalAiModel_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_localAiLoaded) return;
        _app.CurrentSettings.LocalAiModel = LocalAiModel.Normalize((LocalAiModelBox.SelectedItem as ComboBoxItem)?.Tag as string);
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
        LocalAiModelBox.IsEnabled = !busy;
        LocalAiDownloadButton.IsEnabled = enabled && !busy;
        LocalAiDownloadButton.Visibility = e5 && installed ? Visibility.Collapsed : Visibility.Visible;
        LocalAiDownloadButton.Content = installed ? "Download prüfen / reparieren" : "Modell herunterladen";
        LocalAiTestButton.IsEnabled = enabled && installed && !busy;
        LocalAiEvaluateButton.Visibility = e5 && installed ? Visibility.Visible : Visibility.Collapsed;
        LocalAiEvaluateButton.IsEnabled = !busy;
        LocalAiCancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        LocalAiProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        LocalAiStatus.Text = status ?? (!enabled ? "Lokale KI ausgeschaltet." : installed ? $"{_app.LocalAi.DisplayName} ist bereit." : "Das ausgewählte Modell fehlt. Bitte herunterladen.");
        LocalAiModelHint.Text = !enabled ? "Für vorhandene Kategorien genügt das mitgelieferte E5-Modell. Granite kann zusätzlich neue Kategorien und Titel formulieren."
            : e5
            ? "Nutzt das mitgelieferte E5-Suchmodell gemeinsam mit der semantischen Suche. Kein zusätzlicher Download. Vergleicht Texte mit vorhandenen Kategorien und behält den Titel bei."
            : "Granite: einmalig ca. 2,27 GB herunterladen. Läuft ohne Konto oder API-Schlüssel auf deinem Rechner. 8 GB RAM oder mehr empfohlen; die Geschwindigkeit hängt von der CPU ab.";
    }

    private async void LocalAiDownload_Click(object sender, RoutedEventArgs e)
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
            await _app.LocalAi.InstallSelectedAsync(progress, operation.Token);
        }
        catch (OperationCanceledException) { result = "Download abgebrochen. Vollständig geprüfte Dateien bleiben erhalten."; }
        catch (Exception ex) { result = "Download fehlgeschlagen: " + ex.Message; }
        finally { _localAiOperation = null; if (!_localAiClosed) UpdateLocalAiControls(result); }
    }

    private async void LocalAiTest_Click(object sender, RoutedEventArgs e)
    {
        if (_localAiOperation != null) return;
        using var operation = _localAiOperation = new CancellationTokenSource();
        LocalAiProgressBar.IsIndeterminate = true;
        UpdateLocalAiControls($"{_app.LocalAi.DisplayName} wird mit einem Beispieltext getestet …");
        var result = "";
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var suggestion = await _app.LocalAi.SuggestAsync(
                [new BrainSource("test", "Urlaubsplanung", "Wir planen eine Reise nach Italien. Auf unserer Liste stehen Zugtickets, ein Hotel in Rom und die Buchung einer Stadtführung.", "", DateTime.MinValue)],
                ["Reisen", "Arbeit", "Technik"], operation.Token);
            result = suggestion.Uncertain ? $"{_app.LocalAi.DisplayName} antwortet, konnte den Beispieltext aber nicht eindeutig zuordnen. {suggestion.Reason}"
                : $"Test erfolgreich ({timer.Elapsed.TotalSeconds:0.00} s): {suggestion.Category}" + (suggestion.Title.Length > 0 ? $" · {suggestion.Title}" : "");
        }
        catch (OperationCanceledException) { result = "Test abgebrochen."; }
        catch (Exception ex) { result = "Test fehlgeschlagen: " + ex.Message; }
        finally { _localAiOperation = null; if (!_localAiClosed) UpdateLocalAiControls(result); }
    }

    private void LocalAiEvaluate_Click(object sender, RoutedEventArgs e) => _app.OpenCategorizationReport(this);
    private void LocalAiCancel_Click(object sender, RoutedEventArgs e) => _localAiOperation?.Cancel();
}

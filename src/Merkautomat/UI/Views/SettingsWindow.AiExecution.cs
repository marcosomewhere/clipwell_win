using System.Windows;
using System.Windows.Controls;
using Merkautomat.Services;

namespace Merkautomat.Views;

public partial class SettingsWindow
{
    private void AiExecutionChanged()
    {
        if (!_localAiClosed && !Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(UpdateAiExecution);
    }

    private void UpdateAiExecution()
    {
        if (_localAiClosed || _app.LocalAi.Execution is not { } execution) return;
        var r = execution.Resources.Latest;
        static string Percent(double? value) => value.HasValue ? $"{value:0} %" : "nicht verfügbar";
        AiHardwareText.Text = $"RAM frei: {r.FreeRamMiB / 1024.0:0.0} / {r.TotalRamMiB / 1024.0:0.0} GB · CPU: {Percent(r.CpuUtilization)}\n"
            + (r.Gpu is { } g ? $"{g.Name}: {g.FreeMiB} / {g.TotalMiB} MiB frei · Last: {Percent(g.Utilization)} · Temperatur: {(g.Temperature.HasValue ? $"{g.Temperature:0} °C" : "nicht verfügbar")} · thermische Drosselung: {(g.ThermalThrottle.HasValue ? g.ThermalThrottle.Value ? "ja" : "nein" : "unbekannt")}\n" : "GPU-Sensoren nicht verfügbar; CPU-Ausführung bleibt möglich.\n")
            + "CPU-Temperatur: nicht verfügbar. Reduzierter CPU-Takt allein beweist keine thermische Drosselung."
            + (!r.Fresh ? "\nRessourcenmessung ausstehend oder veraltet." : "");
        AiExecutionStatus.Text = execution.Status;
    }

    private void AiComputeMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_localAiLoaded) return;
        _app.CurrentSettings.LocalAiComputeMode = AiExecutionPolicy.Normalize((AiComputeModeBox.SelectedItem as ComboBoxItem)?.Tag as string);
        _app.SaveSettings();
        _app.LocalAi.Stop();
        _app.BackgroundSettingsChanged();
    }

    private void GpuRuntime_Click(object sender, RoutedEventArgs e) => RunAiExecutionAsync(true).Forget("GPU-Unterstützung laden");
    private void AiBenchmark_Click(object sender, RoutedEventArgs e) => RunAiExecutionAsync(false).Forget("CPU und GPU vergleichen");

    private async Task RunAiExecutionAsync(bool install)
    {
        if (_localAiOperation != null || _app.LocalAi.Execution is not { } execution) return;
        using var operation = _localAiOperation = new CancellationTokenSource();
        _app.LocalAi.Stop();
        LocalAiProgressBar.IsIndeterminate = !install;
        UpdateLocalAiControls(install ? "GPU-Unterstützung wird geladen …" : "CPU/GPU-Vergleich wird vorbereitet …");
        var result = "";
        try
        {
            if (install)
            {
                await execution.Runtime.InstallAsync(new Progress<LocalAiProgress>(p =>
                {
                    if (_localAiClosed) return;
                    LocalAiStatus.Text = p.Stage;
                    LocalAiProgressBar.Value = p.Percent;
                }), operation.Token);
                result = "GPU-Unterstützung bereit. Jetzt CPU/GPU vergleichen oder das Sprachmodell testen.";
            }
            else
            {
                await _app.LocalAi.BenchmarkAsync(operation.Token);
                result = execution.Status;
            }
        }
        catch (OperationCanceledException) { result = "Vorgang abgebrochen."; }
        catch (Exception ex) { App.LogError("KI-Ausführung vorbereiten", ex); result = "Vorgang fehlgeschlagen: " + ex.Message; }
        finally { _localAiOperation = null; if (!_localAiClosed) UpdateLocalAiControls(result); }
    }
}

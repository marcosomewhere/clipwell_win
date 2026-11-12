using System.Diagnostics;
using System.IO;
using System.Windows;
using Merkautomat.Services;
using MessageBox = Merkautomat.Views.AppMessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace Merkautomat.Views;

/// <summary>Kontrollstation "Outlook": set up and remove the Outlook add-in for the current user.</summary>
public partial class SettingsWindow
{
    private static bool OutlookRunning()
    {
        var processes = Process.GetProcessesByName("OUTLOOK");
        foreach (var p in processes) p.Dispose();
        return processes.Length > 0;
    }

    private void UpdateOutlookAddinStatus()
    {
        using var root = OutlookAddin.OpenUserRoot();
        var state = OutlookAddin.State(root, AppPaths.OutlookAddinDir);
        var bundled = File.Exists(OutlookAddin.BundledDll(AppContext.BaseDirectory));
        OutlookAddinStatus.Text = state switch
        {
            OutlookAddinState.Active => "Eingerichtet. Nach einem Outlook-Neustart steht „Aufgabe in Merkautomat erstellen“ im Kontextmenü einer Mail.",
            OutlookAddinState.DisabledByOutlook => "Eingerichtet, aber von Outlook abgeschaltet (Fehler oder langsamer Start). „Wieder aktivieren“ und Outlook neu starten.",
            OutlookAddinState.FileMissing => "Eingetragen, aber die Add-in-Datei fehlt. „Add-in einrichten“ wählen.",
            _ => bundled ? "Nicht eingerichtet." : "Nicht verfügbar: outlook\\MerkautomatOutlook.dll fehlt neben Merkautomat.exe.",
        };
        OutlookAddinInstallButton.IsEnabled = bundled;
        OutlookAddinInstallButton.Content = state == OutlookAddinState.NotInstalled ? "Add-in einrichten" : "Erneut einrichten";
        OutlookAddinRemoveButton.IsEnabled = state != OutlookAddinState.NotInstalled;
        OutlookAddinReenableButton.Visibility = state == OutlookAddinState.DisabledByOutlook ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OutlookAddinInstall_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var root = OutlookAddin.OpenUserRoot();
            OutlookAddin.Install(root, OutlookAddin.BundledDll(AppContext.BaseDirectory), AppPaths.OutlookAddinDir,
                Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Merkautomat.exe"), AppPaths.DataDir);
            MessageBox.Show(this, OutlookRunning()
                    ? "Das Add-in ist eingerichtet. Bitte Outlook beenden und neu starten, dann steht „Aufgabe in Merkautomat erstellen“ im Kontextmenü einer Mail."
                    : "Das Add-in ist eingerichtet. Beim nächsten Start von Outlook steht „Aufgabe in Merkautomat erstellen“ im Kontextmenü einer Mail.",
                AppNames.App, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (IOException ex) when (OutlookRunning())
        {
            App.LogCrash(ex);
            MessageBox.Show(this, "Outlook verwendet die bisherige Add-in-Datei gerade. Bitte Outlook beenden und erneut „Einrichten“ wählen.",
                AppNames.App, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or BadImageFormatException)
        {
            App.LogCrash(ex);
            MessageBox.Show(this, $"Das Add-in konnte nicht eingerichtet werden: {ex.Message}", AppNames.App, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        UpdateOutlookAddinStatus();
    }

    private void OutlookAddinReenable_Click(object sender, RoutedEventArgs e)
    {
        using var root = OutlookAddin.OpenUserRoot();
        OutlookAddin.Reenable(root);
        UpdateOutlookAddinStatus();
    }

    private void OutlookAddinRemove_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Outlook-Add-in entfernen? Bereits angelegte Aufgaben bleiben erhalten.", AppNames.App,
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try
        {
            using var root = OutlookAddin.OpenUserRoot();
            OutlookAddin.Uninstall(root, AppPaths.OutlookAddinDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            App.LogCrash(ex);
            MessageBox.Show(this, $"Das Add-in konnte nicht entfernt werden: {ex.Message}", AppNames.App, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        UpdateOutlookAddinStatus();
    }
}

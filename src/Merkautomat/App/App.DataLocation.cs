using System.IO;
using System.Windows;
using Merkautomat.Services;
using MessageBox = Merkautomat.Views.AppMessageBox;

namespace Merkautomat;

public partial class App
{
    private static bool PrepareLocalDataDirectory()
    {
        try
        {
            LocalDataMigration.ImportLegacyDirectory(AppPaths.LegacyDataDir, AppPaths.DataDir);
            Directory.CreateDirectory(AppPaths.DataDir);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Abort before opening any store or creating a key in an incomplete destination.
            MessageBox.Show($"Der lokale Datenordner konnte nicht vorbereitet werden:\n{AppPaths.DataDir}\n\n{ex.Message}\n\nDie bisherigen Daten bleiben erhalten. Bitte den Programmordner an einem beschreibbaren Ort ablegen.",
                AppNames.App, MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private static void UpdateOutlookDataLocation()
    {
        try
        {
            using var root = OutlookAddin.OpenUserRoot();
            OutlookAddin.UpdateLocation(root, OutlookAddin.BundledDll(AppContext.BaseDirectory), AppPaths.OutlookAddinDir,
                Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Merkautomat.exe"), AppPaths.DataDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or BadImageFormatException)
        { LogError("Outlook-Add-in auf lokalen Datenordner umstellen", ex); }
    }
}

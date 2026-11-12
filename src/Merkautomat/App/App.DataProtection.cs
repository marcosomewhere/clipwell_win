using System.IO;
using System.Security.Cryptography;
using System.Windows;
using Merkautomat.Services;
using Merkautomat.Views;
using MessageBox = Merkautomat.Views.AppMessageBox;

namespace Merkautomat;

/// <summary>Opens the data key before anything reads data, and encrypts data from before the encryption.</summary>
public partial class App
{
    private bool _recoveryKeyPending;

    /// <summary>False when the data cannot be opened; then nothing has been read or changed and the app ends.</summary>
    private bool UnlockData()
    {
        DataKeyOpenResult result;
        try { result = DataProtection.Open(AppPaths.DataDir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            LogCrash(ex);
            MessageBox.Show($"Der Datenschlüssel konnte nicht geladen werden: {ex.Message}\n\nEs wurde nichts verändert.",
                AppNames.App, MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        switch (result.State)
        {
            case DataKeyState.Created:
                _recoveryKeyPending = true;
                break;
            case DataKeyState.Ready:
                _recoveryKeyPending = !result.RecoveryKeyConfirmed;
                break;
            case DataKeyState.NeedsRecovery:
                var wrap = result.RecoveryWrap!;
                var opened = RecoveryKeyWindow.AskKey(null, "Notschlüssel eingeben",
                    $"Die Daten von {AppNames.App} sind verschlüsselt, und dieses Windows-Konto kann ihren Schlüssel nicht öffnen " +
                    "(anderer PC, neues Windows-Profil oder vom Administrator zurückgesetztes Passwort).\n\n" +
                    "Gib den Notschlüssel ein, der beim Einrichten der Verschlüsselung angezeigt wurde. Danach ist der Schlüssel " +
                    "an dieses Konto gebunden.",
                    input => DataProtection.TryRecover(AppPaths.DataDir, wrap, input));
                if (!opened)
                {
                    MessageBox.Show($"{AppNames.App} wird beendet. Die verschlüsselten Daten bleiben unverändert.",
                        AppNames.App, MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }
                break;
            default:
                MessageBox.Show(
                    $"Im Datenordner liegen verschlüsselte Daten, aber die Schlüsseldatei {DataProtection.KeyFileName} fehlt und keine " +
                    "Datei enthält die Angaben für den Notschlüssel.\n\nEs wurde nichts verändert. Bitte die Schlüsseldatei aus einer " +
                    $"Sicherung nach {AppPaths.DataDir} zurückkopieren.",
                    AppNames.App, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
        }

        var migration = DataMigration.EncryptExistingFiles(AppPaths.DataDir);
        if (migration.Failed.Count > 0)
            LogCrash(new IOException($"{migration.Failed.Count} Datei(en) konnten nicht verschlüsselt werden: {string.Join(", ", migration.Failed)}"));
        return true;
    }

    private void ShowRecoveryKeyIfPending()
    {
        if (!_recoveryKeyPending || DataProtection.Current.RecoveryKey is not { } key) return;
        RecoveryKeyWindow.ShowKey(null, key, "Deine Daten sind jetzt verschlüsselt",
            "History, Sprachnotizen, Bildschirmaufnahmen, Aufgaben, Einstellungen und Auto-Backups liegen verschlüsselt auf der Platte. " +
            "Der Schlüssel ist an dein Windows-Konto gebunden.\n\n" +
            "Mit diesem Notschlüssel öffnest du die Daten und Auto-Backups auch auf einem anderen PC oder nach einem neuen " +
            "Windows-Profil. Ohne ihn sind sie dann verloren. Bewahre ihn im Passwortmanager oder auf Papier auf, nicht als Datei " +
            $"auf diesem PC. Du findest ihn jederzeit in der {AppNames.Settings} unter Allgemein.",
            requireConfirmation: true);
        try
        {
            DataProtection.ConfirmRecoveryKey(AppPaths.DataDir);
            _recoveryKeyPending = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LogCrash(ex); }
    }

    /// <summary>
    /// For a file from another installation (e.g. an auto-backup from the old PC): asks for its recovery key and keeps
    /// that key for this session. False when the user cancels.
    /// </summary>
    internal static bool OpenForeignData(Window? owner, ForeignKeyException foreign)
        => RecoveryKeyWindow.AskKey(owner, "Notschlüssel der Sicherung",
            "Diese Datei wurde auf einem anderen PC oder mit einem anderen Schlüssel verschlüsselt. Gib den Notschlüssel " +
            "der Installation ein, von der sie stammt.",
            input =>
            {
                var key = DataProtection.TryUnwrap(foreign.RecoveryWrap, input);
                if (key == null) return false;
                DataProtection.AddForeign(key);
                return true;
            });
}

using System.IO;
using System.Windows;
using ClipwellWin.Services;
using Clipboard = System.Windows.Clipboard;
using DataObject = System.Windows.DataObject;

namespace ClipwellWin.Views;

/// <summary>Shows the recovery key once after it was created (and on request), or asks for it to open the data.</summary>
public partial class RecoveryKeyWindow : Window
{
    private readonly Func<string, bool>? _check;

    private RecoveryKeyWindow(Func<string, bool>? check)
    {
        InitializeComponent();
        EditorChrome.ApplyTheme(this);
        SourceInitialized += (_, _) => EditorChrome.ApplyFrame(this);
        _check = check;
    }

    /// <summary>Displays the key; with <paramref name="requireConfirmation"/> it closes only after the user confirms.</summary>
    public static void ShowKey(Window? owner, string key, string heading, string explanation, bool requireConfirmation)
    {
        var dialog = new RecoveryKeyWindow(null);
        dialog.Heading.Text = heading;
        dialog.Explanation.Text = explanation;
        dialog.KeyBox.Text = key;
        dialog.KeyBox.IsReadOnly = true;
        dialog.OkButton.Content = "Fertig";
        dialog.CancelButton.Visibility = requireConfirmation ? Visibility.Collapsed : Visibility.Visible;
        dialog.CancelButton.Content = "Schließen";
        dialog.ConfirmBox.Visibility = requireConfirmation ? Visibility.Visible : Visibility.Collapsed;
        dialog.OkButton.IsEnabled = !requireConfirmation;
        if (requireConfirmation) dialog.Closing += (_, e) => e.Cancel = dialog.DialogResult != true;
        Place(dialog, owner);
        dialog.Loaded += (_, _) => dialog.KeyBox.SelectAll();
        dialog.ShowDialog();
    }

    /// <summary>Asks for the recovery key until <paramref name="check"/> accepts it; false when the user cancels.</summary>
    public static bool AskKey(Window? owner, string heading, string explanation, Func<string, bool> check)
    {
        var dialog = new RecoveryKeyWindow(check);
        dialog.Heading.Text = heading;
        dialog.Explanation.Text = explanation;
        dialog.OkButton.Content = "Öffnen";
        dialog.CopyButton.Visibility = Visibility.Collapsed;
        dialog.ConfirmBox.Visibility = Visibility.Collapsed;
        Place(dialog, owner);
        dialog.Loaded += (_, _) => dialog.KeyBox.Focus();
        return dialog.ShowDialog() == true;
    }

    private static void Place(Window dialog, Window? owner)
    {
        if (owner is { IsVisible: true }) { dialog.Owner = owner; dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        else dialog.Topmost = true;
    }

    private void ConfirmBox_Changed(object sender, RoutedEventArgs e) => OkButton.IsEnabled = ConfirmBox.IsChecked == true;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_check != null)
        {
            if (RecoveryCode.Parse(KeyBox.Text) == null)
            {
                ShowError("Der Notschlüssel hat 32 Zeichen in acht Vierergruppen, z. B. ABCD-EFGH-…");
                return;
            }
            Cursor = System.Windows.Input.Cursors.Wait;
            var ok = _check(KeyBox.Text);
            Cursor = null;
            if (!ok)
            {
                ShowError("Dieser Notschlüssel passt nicht zu den Daten.");
                return;
            }
        }
        DialogResult = true;
    }

    private void ShowError(string text)
    {
        ErrorText.Text = text;
        ErrorText.Visibility = Visibility.Visible;
        KeyBox.Focus();
        KeyBox.SelectAll();
    }

    /// <summary>
    /// Copies with the markers that keep the key out of Merkautomat's own history, the Windows clipboard history and
    /// cloud clipboard sync.
    /// </summary>
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var data = new DataObject();
        data.SetText(KeyBox.Text);
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream([0, 0, 0, 0]));
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream([0, 0, 0, 0]));
        data.SetData("CanUploadToCloudClipboard", new MemoryStream([0, 0, 0, 0]));
        try
        {
            Clipboard.SetDataObject(data, copy: true);
            CopyButton.Content = "Kopiert";
        }
        catch (System.Runtime.InteropServices.COMException ex) { App.LogCrash(ex); }
    }
}

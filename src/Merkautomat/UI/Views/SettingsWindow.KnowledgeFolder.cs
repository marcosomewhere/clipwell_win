using System.IO;
using System.Windows;
using System.Windows.Threading;
using Merkautomat.Models;
using Merkautomat.Services;

namespace Merkautomat.Views;

public partial class SettingsWindow
{
    private string _savedKnowledgeFolderPath = "";
    private bool _savedKnowledgeFolderEnabled;

    private void InitializeKnowledgeFolder()
    {
        _savedKnowledgeFolderPath = _app.CurrentSettings.KnowledgeFolderPath;
        _savedKnowledgeFolderEnabled = _app.CurrentSettings.KnowledgeFolderEnabled;
        UpdateKnowledgeFolderPath();
        KnowledgeFolderSwitch.IsChecked = _app.CurrentSettings.KnowledgeFolderEnabled;
        BrainMaxNodesBox.Text = _app.CurrentSettings.EffectiveBrainMaxNodes().ToString();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => KnowledgeFolderStatusLabel.Text = _app.KnowledgeFolderStatus;
        timer.Start();
        KnowledgeFolderStatusLabel.Text = _app.KnowledgeFolderStatus;
        Closed += (_, _) => timer.Stop();
    }

    private void UpdateKnowledgeFolderPath()
        => KnowledgeFolderPathLabel.Text = string.IsNullOrWhiteSpace(_app.CurrentSettings.KnowledgeFolderPath)
            ? "Kein Ordner ausgewählt." : _app.CurrentSettings.KnowledgeFolderPath;

    private void KnowledgeFolderChoose_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Knowledgeordner auswählen", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        if (KnowledgeFolder.IsWithin(dialog.FolderName, AppPaths.DataDir)
            || KnowledgeFolder.IsWithin(dialog.FolderName, AppPaths.ModelsDir)
            || KnowledgeFolder.IsWithin(dialog.FolderName, System.IO.Path.Combine(AppContext.BaseDirectory, "models")))
        {
            AppMessageBox.Show(this, "Bitte einen Ordner außerhalb der Daten- und Modellordner wählen.", AppNames.App);
            return;
        }
        _app.CurrentSettings.KnowledgeFolderPath = dialog.FolderName;
        _app.CurrentSettings.KnowledgeFolderEnabled = true;
        KnowledgeFolderSwitch.IsChecked = true;
        SaveKnowledgeFolder();
    }

    private void KnowledgeFolderRemove_Click(object sender, RoutedEventArgs e)
    {
        _app.CurrentSettings.KnowledgeFolderPath = "";
        _app.CurrentSettings.KnowledgeFolderEnabled = false;
        KnowledgeFolderSwitch.IsChecked = false;
        SaveKnowledgeFolder();
    }

    private void KnowledgeFolderEnabled_Click(object sender, RoutedEventArgs e)
    {
        _app.CurrentSettings.KnowledgeFolderEnabled = KnowledgeFolderSwitch.IsChecked == true;
        SaveKnowledgeFolder();
    }

    private void SaveKnowledgeFolder()
    {
        try { _app.SaveSettings(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.LogError("Knowledgeordner-Einstellungen speichern", ex);
            _app.CurrentSettings.KnowledgeFolderPath = _savedKnowledgeFolderPath;
            _app.CurrentSettings.KnowledgeFolderEnabled = _savedKnowledgeFolderEnabled;
            KnowledgeFolderSwitch.IsChecked = _savedKnowledgeFolderEnabled;
            UpdateKnowledgeFolderPath();
            AppMessageBox.Show(this, "Die Änderung am Knowledgeordner konnte nicht gespeichert werden. Bitte erneut versuchen.", AppNames.App);
            return;
        }
        _savedKnowledgeFolderPath = _app.CurrentSettings.KnowledgeFolderPath;
        _savedKnowledgeFolderEnabled = _app.CurrentSettings.KnowledgeFolderEnabled;
        UpdateKnowledgeFolderPath();
        _app.RefreshKnowledgeFolder();
    }

    private void KnowledgeFolderRefresh_Click(object sender, RoutedEventArgs e) => _app.RefreshKnowledgeFolder();

    private void BrainMaxNodes_LostFocus(object sender, RoutedEventArgs e) => SaveBrainMaxNodes();

    private void SaveBrainMaxNodes()
    {
        var value = ReadInt(BrainMaxNodesBox.Text, AppSettings.BrainMaxNodesMin, AppSettings.BrainMaxNodesMax, AppSettings.BrainMaxNodesDefault);
        BrainMaxNodesBox.Text = value.ToString();
        if (_app.CurrentSettings.BrainMaxNodes == value) return;
        var previous = _app.CurrentSettings.BrainMaxNodes;
        _app.CurrentSettings.BrainMaxNodes = value;
        try { _app.SaveSettings(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.LogError("Brain-Limit speichern", ex);
            _app.CurrentSettings.BrainMaxNodes = previous;
            BrainMaxNodesBox.Text = _app.CurrentSettings.EffectiveBrainMaxNodes().ToString();
            AppMessageBox.Show(this, "Das Brain-Limit konnte nicht gespeichert werden. Bitte erneut versuchen.", AppNames.App);
            return;
        }
        _app.NotifyKnowledgeBaseChanged();
    }
}

using System.IO;
using System.Windows;
using System.Windows.Threading;
using Merkautomat.Models;
using Merkautomat.Services;

namespace Merkautomat.Views;

/// <summary>Kontrollstation: "Mein Wissen" (retention, clipboard decay) and the editable categorization prompt.</summary>
public partial class SettingsWindow
{
    private void InitializeMyKnowledge()
    {
        var s = _app.CurrentSettings;
        MyKnowledgeSwitch.IsChecked = s.MyKnowledgeEnabled;
        MyKnowledgeRetentionBox.Text = ClampInt(s.MyKnowledgeRetentionDays, 0, 3650, AppSettings.MyKnowledgeRetentionDefault).ToString();
        MyKnowledgeHalfLifeBox.Text = ClampInt(s.MyKnowledgeClipboardHalfLifeDays, 1, 365, AppSettings.MyKnowledgeClipboardHalfLifeDefault).ToString();
        MyKnowledgeCondenseSwitch.IsChecked = s.MyKnowledgeCondense;
        MyKnowledgeMaturityBox.Text = ClampInt(s.MyKnowledgeMaturityHours, 0, AppSettings.MyKnowledgeMaturityMax, AppSettings.MyKnowledgeMaturityDefault).ToString();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => MyKnowledgeStatusLabel.Text = _app.MyKnowledgeStatus;
        timer.Start();
        MyKnowledgeStatusLabel.Text = _app.MyKnowledgeStatus;
        Closed += (_, _) => timer.Stop();

        InitializePrompts();
    }

    private void MyKnowledgeSettings_Changed(object sender, RoutedEventArgs e) => SaveMyKnowledgeSettings();

    private void SaveMyKnowledgeSettings()
    {
        var s = _app.CurrentSettings;
        var enabled = MyKnowledgeSwitch.IsChecked == true;
        var retention = ReadInt(MyKnowledgeRetentionBox.Text, 0, 3650, AppSettings.MyKnowledgeRetentionDefault);
        var halfLife = ReadInt(MyKnowledgeHalfLifeBox.Text, 1, 365, AppSettings.MyKnowledgeClipboardHalfLifeDefault);
        var condense = MyKnowledgeCondenseSwitch.IsChecked == true;
        var maturity = ReadInt(MyKnowledgeMaturityBox.Text, 0, AppSettings.MyKnowledgeMaturityMax, AppSettings.MyKnowledgeMaturityDefault);
        MyKnowledgeRetentionBox.Text = retention.ToString();
        MyKnowledgeHalfLifeBox.Text = halfLife.ToString();
        MyKnowledgeMaturityBox.Text = maturity.ToString();
        if (s.MyKnowledgeEnabled == enabled && s.MyKnowledgeRetentionDays == retention && s.MyKnowledgeClipboardHalfLifeDays == halfLife
            && s.MyKnowledgeCondense == condense && s.MyKnowledgeMaturityHours == maturity) return;
        var previous = (s.MyKnowledgeEnabled, s.MyKnowledgeRetentionDays, s.MyKnowledgeClipboardHalfLifeDays, s.MyKnowledgeCondense, s.MyKnowledgeMaturityHours);
        s.MyKnowledgeEnabled = enabled;
        s.MyKnowledgeRetentionDays = retention;
        s.MyKnowledgeClipboardHalfLifeDays = halfLife;
        s.MyKnowledgeCondense = condense;
        s.MyKnowledgeMaturityHours = maturity;
        try { _app.SaveSettings(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.LogError("Mein-Wissen-Einstellungen speichern", ex);
            (s.MyKnowledgeEnabled, s.MyKnowledgeRetentionDays, s.MyKnowledgeClipboardHalfLifeDays, s.MyKnowledgeCondense, s.MyKnowledgeMaturityHours) = previous;
            MyKnowledgeCondenseSwitch.IsChecked = s.MyKnowledgeCondense;
            AppMessageBox.Show(this, $"Die Einstellungen für {AppNames.MyKnowledge} konnten nicht gespeichert werden. Bitte erneut versuchen.", AppNames.App);
        }
    }

    private void MyKnowledgeRun_Click(object sender, RoutedEventArgs e)
    {
        SaveMyKnowledgeSettings();
        _app.RunMyKnowledge();
    }

    // ---- Prompts of the local AI ---------------------------------------------------------------------------------

    /// <summary>The prompt whose text is in the box; saved before another one is shown.</summary>
    private AiPromptDefinition? _shownPrompt;

    private void InitializePrompts()
    {
        CategorizationPromptBox.MaxLength = AiPrompts.MaxChars;
        PromptBox.ItemsSource = AiPrompts.All;
        PromptBox.SelectedIndex = 0;
    }

    private void PromptBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        SaveCategorizationPrompt();
        _shownPrompt = PromptBox.SelectedItem as AiPromptDefinition;
        if (_shownPrompt == null) return;
        PromptUsedFor.Text = "Eingesetzt: " + _shownPrompt.UsedFor;
        PromptData.Text = _shownPrompt.Data;
        CategorizationPromptBox.Text = AiPrompts.Effective(_shownPrompt.Id, AiPrompts.Custom(_app.CurrentSettings, _shownPrompt.Id));
        UpdatePromptHint();
    }

    private void CategorizationPrompt_LostFocus(object sender, RoutedEventArgs e) => SaveCategorizationPrompt();

    private void CategorizationPromptReset_Click(object sender, RoutedEventArgs e)
    {
        if (_shownPrompt == null) return;
        CategorizationPromptBox.Text = _shownPrompt.Default;
        SaveCategorizationPrompt();
    }

    private void SaveCategorizationPrompt()
    {
        if (_shownPrompt is not { } prompt) return;
        var s = _app.CurrentSettings;
        var before = AiPrompts.Custom(s, prompt.Id);
        // The default is not stored, so a later improvement of the built-in prompt reaches users who never changed it.
        if (CategorizationPromptBox.Text.Trim().Length == 0) CategorizationPromptBox.Text = prompt.Default;
        AiPrompts.Set(s, prompt.Id, CategorizationPromptBox.Text);
        if (AiPrompts.Custom(s, prompt.Id) == before) { UpdatePromptHint(); return; }
        try { _app.SaveSettings(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.LogError("Prompt speichern", ex);
            AiPrompts.Set(s, prompt.Id, before);
            AppMessageBox.Show(this, "Der Prompt konnte nicht gespeichert werden. Bitte erneut versuchen.", AppNames.App);
        }
        UpdatePromptHint();
    }

    private void UpdatePromptHint()
        => CategorizationPromptState.Text = _shownPrompt == null || AiPrompts.Custom(_app.CurrentSettings, _shownPrompt.Id).Length == 0
            ? "Standard-Prompt aktiv."
            : "Eigener Prompt aktiv. „Standard wiederherstellen“ setzt ihn zurück.";
}

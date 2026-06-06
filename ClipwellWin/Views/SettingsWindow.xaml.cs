using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using ClipwellWin.Models;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace ClipwellWin.Views;

public partial class SettingsWindow : FluentWindow
{
    private readonly App _app;
    private uint _pendingHotkeyModifiers;
    private uint _pendingHotkeyVk;

    public SettingsWindow(App app)
    {
        _app = app;
        InitializeComponent();
        LoadCurrentSettings();
        PopulateAboutTab();
    }

    private void LoadCurrentSettings()
    {
        var s = _app.CurrentSettings;

        ModWin.IsChecked   = (s.HotkeyModifiers & NativeMethods.MOD_WIN)     != 0;
        ModShift.IsChecked = (s.HotkeyModifiers & NativeMethods.MOD_SHIFT)   != 0;
        ModCtrl.IsChecked  = (s.HotkeyModifiers & NativeMethods.MOD_CONTROL) != 0;
        ModAlt.IsChecked   = (s.HotkeyModifiers & NativeMethods.MOD_ALT)     != 0;

        HotkeyChar.Text = s.HotkeyVk is >= 0x41 and <= 0x5A
            ? ((char)s.HotkeyVk).ToString() : "V";
        HotkeyRecorderBox.Text = App.FormatHotkey(s.HotkeyModifiers, s.HotkeyVk);
        _pendingHotkeyModifiers = s.HotkeyModifiers;
        _pendingHotkeyVk = s.HotkeyVk;

        HotkeyActionBox.SelectedIndex = s.HotkeyAction == HotkeyAction.PasteLatest ? 1 : 0;
        CodeModeBox.SelectedIndex = s.CodeDetectionMode switch
        {
            CodeDetectionMode.Conservative => 0,
            CodeDetectionMode.Aggressive   => 2,
            _                              => 1,
        };
        MaxItemsBox.Value = ClampInt(s.MaxHistoryItems, 50, 5000, 500);
        PauseSwitch.IsChecked = s.PauseMonitoring;
        RememberPositionSwitch.IsChecked = s.RememberPopupPosition;

        ThemeBox.SelectedIndex = s.Theme switch
        {
            ThemeMode.Dark    => 1,
            ThemeMode.Light   => 2,
            _                 => 0,
        };
        if (s.Theme == ThemeMode.Compact)
        {
            s.Theme = ThemeMode.Light;
            ThemeBox.SelectedIndex = 2;
            _app.ApplyTheme(s.Theme);
            _app.SaveSettings();
        }

        IncognitoSwitch.IsChecked      = s.IncognitoMode;
        FilterSensitiveSwitch.IsChecked = s.FilterSensitiveContent;
        UrlPreviewSwitch.IsChecked      = s.UrlPreviewEnabled;
        IncognitoAppsBox.Text           = string.Join(", ", s.IncognitoApps);
        IncognitoDomainsBox.Text        = string.Join(", ", s.IncognitoDomains);

        MaxAgeBox.Value  = ClampInt(s.MaxAgeInDays, 0, 3650, 0);
        MaxSizeBox.Value = ClampInt(s.MaxSizeInMb, 0, 10240, 0);

        AutoBackupSwitch.IsChecked = s.AutoBackupEnabled;
        AutoBackupDirBox.Text = s.AutoBackupDirectory;
        UpdateAutoBackupStatus();

        PinCtrlSwitch.IsChecked       = s.KeyPinCtrl;
        PinKeyBox.Text                = s.KeyPin;
        DetailsKeyBox.Text            = s.KeyDetails;
        QuickNoteCtrlSwitch.IsChecked = s.KeyQuickNoteCtrl;
        QuickNoteKeyBox.Text          = s.KeyQuickNote;
        PinboardCtrlSwitch.IsChecked  = s.KeyPinboardCtrl;
        PinboardKeyBox.Text           = s.KeyPinboard;

        UpdateHotkeyStatus();
    }

    private void HotkeyRecorderBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk < 0x30 || vk > 0x5A)
        {
            ShowError("Bitte eine Buchstaben- oder Zifferntaste aufnehmen.");
            return;
        }

        _pendingHotkeyModifiers = NormalizeHotkeyModifiers(ModifiersFromKeyboard());
        _pendingHotkeyVk = (uint)vk;
        SyncHotkeyControls(_pendingHotkeyModifiers, _pendingHotkeyVk);
    }

    private void ApplyHotkey_Click(object sender, RoutedEventArgs e)
        => ApplyHotkeyValues(_pendingHotkeyModifiers, _pendingHotkeyVk);

    private void ApplyManualHotkey_Click(object sender, RoutedEventArgs e)
    {
        var ch = HotkeyChar.Text.ToUpperInvariant().FirstOrDefault();
        if (ch < 'A' || ch > 'Z')
        {
            ShowError("Nur Buchstaben A-Z erlaubt.");
            return;
        }
        ApplyHotkeyValues(ModifiersFromSwitches(), ch);
    }

    private void ApplyHotkeyValues(uint modifiers, uint vk)
    {
        modifiers = NormalizeHotkeyModifiers(modifiers);
        var s = _app.CurrentSettings;
        s.HotkeyModifiers = modifiers;
        s.HotkeyVk = vk;
        SyncHotkeyControls(s.HotkeyModifiers, vk);

        SaveGeneralSettings();
        var registered = _app.ReRegisterHotkey();
        UpdateHotkeyStatus();

        if (registered) ShowInfo("Hotkey wurde gespeichert.");
        else ShowError($"Hotkey gespeichert, konnte aber nicht registriert werden. Fehlercode: {_app.LastHotkeyError}");
    }

    private static uint NormalizeHotkeyModifiers(uint modifiers)
    {
        const uint baseMask = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT |
                              NativeMethods.MOD_SHIFT | NativeMethods.MOD_WIN;
        var baseModifiers = modifiers & baseMask;
        if (baseModifiers == 0)
            baseModifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT;
        return baseModifiers | NativeMethods.MOD_NOREPEAT;
    }

    private uint ModifiersFromKeyboard()
    {
        uint mods = 0;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) mods |= NativeMethods.MOD_CONTROL;
        if ((Keyboard.Modifiers & ModifierKeys.Alt)     != 0) mods |= NativeMethods.MOD_ALT;
        if ((Keyboard.Modifiers & ModifierKeys.Shift)   != 0) mods |= NativeMethods.MOD_SHIFT;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) mods |= NativeMethods.MOD_WIN;
        return mods == 0 ? NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT : mods;
    }

    private uint ModifiersFromSwitches()
    {
        uint mods = 0;
        if (ModWin.IsChecked   == true) mods |= NativeMethods.MOD_WIN;
        if (ModShift.IsChecked == true) mods |= NativeMethods.MOD_SHIFT;
        if (ModCtrl.IsChecked  == true) mods |= NativeMethods.MOD_CONTROL;
        if (ModAlt.IsChecked   == true) mods |= NativeMethods.MOD_ALT;
        return mods;
    }

    private void SyncHotkeyControls(uint modifiers, uint vk)
    {
        ModWin.IsChecked   = (modifiers & NativeMethods.MOD_WIN)     != 0;
        ModShift.IsChecked = (modifiers & NativeMethods.MOD_SHIFT)   != 0;
        ModCtrl.IsChecked  = (modifiers & NativeMethods.MOD_CONTROL) != 0;
        ModAlt.IsChecked   = (modifiers & NativeMethods.MOD_ALT)     != 0;
        HotkeyChar.Text = vk is >= 0x41 and <= 0x5A ? ((char)vk).ToString() : "";
        HotkeyRecorderBox.Text = App.FormatHotkey(modifiers, vk);
        _pendingHotkeyModifiers = modifiers;
        _pendingHotkeyVk = vk;
    }

    private void UpdateHotkeyStatus()
    {
        var s = _app.CurrentSettings;
        HotkeyStatusLabel.Text = _app.IsHotkeyRegistered
            ? $"Aktiv: {App.FormatHotkey(s.HotkeyModifiers, s.HotkeyVk)}"
            : $"Blockiert: {App.FormatHotkey(s.HotkeyModifiers, s.HotkeyVk)}";
    }

    private void SaveGeneral_Click(object sender, RoutedEventArgs e)
    {
        SaveGeneralSettings();
        ShowInfo("Gespeichert.");
    }

    private void SaveGeneralSettings()
    {
        var s = _app.CurrentSettings;
        s.MaxHistoryItems = ClampInt((int)(MaxItemsBox.Value ?? 500), 50, 5000, 500);
        s.HotkeyAction = HotkeyActionBox.SelectedIndex == 1
            ? HotkeyAction.PasteLatest
            : HotkeyAction.OpenMenu;
        s.CodeDetectionMode = CodeModeBox.SelectedIndex switch
        {
            0 => CodeDetectionMode.Conservative,
            2 => CodeDetectionMode.Aggressive,
            _ => CodeDetectionMode.Normal,
        };
        s.PauseMonitoring = PauseSwitch.IsChecked == true;
        _app.SaveSettings();
    }

    private void PauseSwitch_Click(object sender, RoutedEventArgs e)
        => SaveGeneralSettings();

    private void RememberPosition_Click(object sender, RoutedEventArgs e)
    {
        _app.CurrentSettings.RememberPopupPosition = RememberPositionSwitch.IsChecked == true;
        _app.SaveSettings();
    }

    private void ThemeBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var mode = ThemeBox.SelectedIndex switch
        {
            1 => ThemeMode.Dark,
            2 => ThemeMode.Light,
            _ => ThemeMode.System,
        };
        _app.CurrentSettings.Theme = mode;
        _app.ApplyTheme(mode);
        _app.SaveSettings();
    }

    private void IncognitoSwitch_Click(object sender, RoutedEventArgs e)
    {
        _app.CurrentSettings.IncognitoMode = IncognitoSwitch.IsChecked == true;
        _app.SaveSettings();
    }

    private void FilterSensitive_Click(object sender, RoutedEventArgs e)
    {
        _app.CurrentSettings.FilterSensitiveContent = FilterSensitiveSwitch.IsChecked == true;
        _app.SaveSettings();
    }

    private void UrlPreview_Click(object sender, RoutedEventArgs e)
    {
        _app.CurrentSettings.UrlPreviewEnabled = UrlPreviewSwitch.IsChecked == true;
        _app.SaveSettings();
    }

    private void SaveIncognitoApps_Click(object sender, RoutedEventArgs e)
    {
        _app.CurrentSettings.IncognitoApps = ParseList(IncognitoAppsBox.Text);
        _app.SaveSettings();
        ShowInfo("Apps gespeichert.");
    }

    private void SaveIncognitoDomains_Click(object sender, RoutedEventArgs e)
    {
        _app.CurrentSettings.IncognitoDomains = ParseList(IncognitoDomainsBox.Text);
        _app.SaveSettings();
        ShowInfo("Domains gespeichert.");
    }

    private static List<string> ParseList(string text)
        => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private void SaveStorageLimits_Click(object sender, RoutedEventArgs e)
    {
        _app.CurrentSettings.MaxAgeInDays = ClampInt((int)(MaxAgeBox.Value ?? 0), 0, 3650, 0);
        _app.CurrentSettings.MaxSizeInMb  = ClampInt((int)(MaxSizeBox.Value ?? 0), 0, 10240, 0);
        _app.SaveSettings();
        ShowInfo("Gespeichert.");
    }

    private static int ClampInt(int value, int min, int max, int fallback)
    {
        if (value < min || value > max)
            return fallback;
        return value;
    }

    private void ExportJson_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title    = "History als JSON exportieren",
            Filter   = "JSON-Datei|*.json",
            FileName = $"clipwell-history-{DateTime.Now:yyyyMMdd-HHmmss}.json",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _app.Database.ExportAsJson(dlg.FileName);
            ShowInfo($"Export erfolgreich:\n{dlg.FileName}");
        }
        catch (Exception ex) { ShowError($"Export fehlgeschlagen: {ex.Message}"); }
    }

    private void ExportSqlite_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title    = "History als SQLite-Backup exportieren",
            Filter   = "SQLite-Datenbank|*.db",
            FileName = $"clipwell-backup-{DateTime.Now:yyyyMMdd-HHmmss}.db",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _app.Database.ExportAsSqliteCopy(dlg.FileName);
            ShowInfo($"Backup erfolgreich:\n{dlg.FileName}");
        }
        catch (Exception ex) { ShowError($"Backup fehlgeschlagen: {ex.Message}"); }
    }

    private void ImportJson_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "History aus JSON importieren",
            Filter = "JSON-Datei|*.json",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            int count = _app.Database.ImportFromJson(dlg.FileName);
            ShowInfo($"{count} Einträge importiert. Bitte Clipwell neu starten.");
        }
        catch (Exception ex) { ShowError($"Import fehlgeschlagen: {ex.Message}"); }
    }

    private void ResetHistory_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show(
            "Gesamte History löschen? Gepinnte Einträge bleiben erhalten.",
            "History löschen",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        if (r == MessageBoxResult.Cancel) return;

        bool keepPinned = r == MessageBoxResult.Yes;
        _app.Database.ClearHistory(keepPinned);
        ShowInfo(keepPinned
            ? "History gelöscht (gepinnte Einträge behalten)."
            : "Gesamte History gelöscht.");
    }

    private void ResetUrlCache_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show(
            "URL-Cache löschen? Favicon und Titel werden beim nächsten Besuch neu geladen.",
            "URL-Cache löschen",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (r != MessageBoxResult.OK) return;
        _app.Database.ClearUrlCache();
        ShowInfo("URL-Cache gelöscht.");
    }

    private void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show(
            "Alle Einstellungen auf die Standardwerte zurücksetzen?\nDie History bleibt erhalten.",
            "Einstellungen zurücksetzen",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (r != MessageBoxResult.OK) return;

        var defaults = new AppSettings();
        var path = Services.SettingsService.SettingsPath;
        var json = System.Text.Json.JsonSerializer.Serialize(defaults,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(path, json);

        ShowInfo("Einstellungen zurückgesetzt. Bitte Clipwell neu starten.");
    }

    private void AutoBackupSwitch_Click(object sender, RoutedEventArgs e)
    {
        _app.CurrentSettings.AutoBackupEnabled = AutoBackupSwitch.IsChecked == true;
        _app.SaveSettings();
        UpdateAutoBackupStatus();
    }

    private void AutoBackupBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Backup-Verzeichnis wählen",
            Multiselect = false,
        };
        if (dlg.ShowDialog() != true) return;
        _app.CurrentSettings.AutoBackupDirectory = dlg.FolderName;
        AutoBackupDirBox.Text = dlg.FolderName;
        _app.SaveSettings();
        UpdateAutoBackupStatus();
    }

    private void UpdateAutoBackupStatus()
    {
        var last = _app.CurrentSettings.LastAutoBackupDate;
        AutoBackupStatusLabel.Text = last.HasValue
            ? $"Letztes Backup: {last.Value:dd.MM.yyyy HH:mm}"
            : "Noch kein automatisches Backup erstellt.";
    }

    private void SaveShortcuts_Click(object sender, RoutedEventArgs e)
    {
        var s = _app.CurrentSettings;
        s.KeyPinCtrl      = PinCtrlSwitch.IsChecked == true;
        s.KeyPin          = PinKeyBox.Text.ToUpperInvariant().FirstOrDefault() is char c1 && c1 != '\0' ? c1.ToString() : "P";
        s.KeyDetails      = string.IsNullOrWhiteSpace(DetailsKeyBox.Text) ? "F2" : DetailsKeyBox.Text.Trim();
        s.KeyQuickNoteCtrl = QuickNoteCtrlSwitch.IsChecked == true;
        s.KeyQuickNote    = QuickNoteKeyBox.Text.ToUpperInvariant().FirstOrDefault() is char c2 && c2 != '\0' ? c2.ToString() : "N";
        s.KeyPinboardCtrl  = PinboardCtrlSwitch.IsChecked == true;
        s.KeyPinboard     = PinboardKeyBox.Text.ToUpperInvariant().FirstOrDefault() is char c3 && c3 != '\0' ? c3.ToString() : "B";
        _app.SaveSettings();
        ShowInfo("Tastenkürzel gespeichert.");
    }

    private void ResetShortcuts_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new AppSettings();
        var s = _app.CurrentSettings;
        s.KeyPin = defaults.KeyPin; s.KeyPinCtrl = defaults.KeyPinCtrl;
        s.KeyDetails = defaults.KeyDetails;
        s.KeyQuickNote = defaults.KeyQuickNote; s.KeyQuickNoteCtrl = defaults.KeyQuickNoteCtrl;
        s.KeyPinboard = defaults.KeyPinboard; s.KeyPinboardCtrl = defaults.KeyPinboardCtrl;
        _app.SaveSettings();

        PinCtrlSwitch.IsChecked       = s.KeyPinCtrl;
        PinKeyBox.Text                = s.KeyPin;
        DetailsKeyBox.Text            = s.KeyDetails;
        QuickNoteCtrlSwitch.IsChecked = s.KeyQuickNoteCtrl;
        QuickNoteKeyBox.Text          = s.KeyQuickNote;
        PinboardCtrlSwitch.IsChecked  = s.KeyPinboardCtrl;
        PinboardKeyBox.Text           = s.KeyPinboard;
        ShowInfo("Tastenkürzel zurückgesetzt.");
    }

    private void PopulateAboutTab()
    {
        AboutLogoImage.Source = App.CreateAppIconImageSource(192);

        var asm = Assembly.GetExecutingAssembly();
        var ver = asm.GetName().Version;
        AboutVersionLabel.Text = ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "1.0.0";

        var buildTime = File.GetLastWriteTime(asm.Location);
        AboutBuildLabel.Text = buildTime.ToString("yyyy.MM.dd.HHmm");

        var arch = Environment.Is64BitProcess ? "64-bit" : "32-bit";
        AboutPlatformLabel.Text = $"Windows 10/11 ({arch})";
    }

    private void AboutGitHub_Click(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo("https://github.com/marcoseefeldt/clipwell-win") { UseShellExecute = true });

    private void ShowError(string msg) =>
        MessageBox.Show(msg, "Clipwell", MessageBoxButton.OK, MessageBoxImage.Warning);

    private void ShowInfo(string msg) =>
        MessageBox.Show(msg, "Clipwell", MessageBoxButton.OK, MessageBoxImage.Information);
}

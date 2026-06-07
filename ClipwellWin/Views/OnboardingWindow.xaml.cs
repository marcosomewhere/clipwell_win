using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using ClipwellWin.Models;

namespace ClipwellWin.Views;

public partial class OnboardingWindow : Window
{
    private readonly AppSettings _settings;
    private readonly App _app;
    private uint _pendingHotkeyModifiers;
    private uint _pendingHotkeyVk;
    private bool _isWaitingForHotkey;

    public OnboardingWindow(AppSettings settings, App app)
    {
        _settings = settings;
        _app = app;
        InitializeComponent();
        App.ApplyAppIcon(this);

        _pendingHotkeyModifiers = settings.HotkeyModifiers;
        _pendingHotkeyVk = settings.HotkeyVk;
        OpenMenuRadio.IsChecked = settings.HotkeyAction != HotkeyAction.PasteLatest;
        PasteLatestRadio.IsChecked = settings.HotkeyAction == HotkeyAction.PasteLatest;
        StartWithWindowsCheck.IsChecked = settings.StartWithWindows;
        SyncHotkeyControls(_pendingHotkeyModifiers, _pendingHotkeyVk);
        UpdateHotkeyStatus();
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        ApplyWindowTheme();
    }

    internal void ApplyWindowTheme()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            NativeMethods.SetImmersiveDarkMode(hwnd, _app.IsEffectiveDarkTheme);
    }

    private void HotkeyRecorderBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _app.SuspendHotkey();
        UpdateHotkeyStatus();
        FooterStatusLabel.Text = "Tastenkombination drücken, z.B. Win+Shift+V oder Ctrl+Shift+V.";
    }

    private void HotkeyRecorderBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_isWaitingForHotkey) return;
        _app.ReRegisterHotkey();
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
        if (!IsSupportedHotkeyKey(vk))
        {
            FooterStatusLabel.Text = "Bitte einen Buchstaben oder eine Ziffer mit mindestens einer Modifikatortaste verwenden.";
            return;
        }

        _pendingHotkeyModifiers = NormalizeHotkeyModifiers(ModifiersFromKeyboard());
        _pendingHotkeyVk = (uint)vk;
        SyncHotkeyControls(_pendingHotkeyModifiers, _pendingHotkeyVk);
        FooterStatusLabel.Text = "Hotkey aufgenommen. Speichern registriert ihn global.";
    }

    private void Modifier_Click(object sender, RoutedEventArgs e)
    {
        _pendingHotkeyModifiers = NormalizeHotkeyModifiers(ModifiersFromSwitches());
        SyncHotkeyControls(_pendingHotkeyModifiers, _pendingHotkeyVk);
    }

    private void HotkeyAction_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settings.HotkeyAction = PasteLatestRadio.IsChecked == true
            ? HotkeyAction.PasteLatest
            : HotkeyAction.OpenMenu;
        _app.SaveSettings();
        UpdateHotkeyStatus();
    }

    private void SaveHotkey_Click(object sender, RoutedEventArgs e)
    {
        ApplyHotkeyValues();
    }

    private void TestHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (!ApplyHotkeyValues()) return;

        _isWaitingForHotkey = true;
        TestHotkeyBtn.IsEnabled = false;
        HotkeyStatusLabel.Text = $"Warte auf {App.FormatHotkey(_settings.HotkeyModifiers, _settings.HotkeyVk)} ...";
        FooterStatusLabel.Text = "Drücke jetzt den gespeicherten Hotkey.";

        _ = Task.Delay(5000).ContinueWith(_ => Dispatcher.Invoke(() =>
        {
            if (!_isWaitingForHotkey) return;
            _isWaitingForHotkey = false;
            TestHotkeyBtn.IsEnabled = true;
            HotkeyStatusLabel.Text = "Kein Hotkey-Signal empfangen.";
            FooterStatusLabel.Text = "Wenn der Hotkey blockiert ist, nimm eine andere Kombination auf und speichere erneut.";
        }));
    }

    public bool NotifyHotkeyTriggered()
    {
        if (!_isWaitingForHotkey) return false;

        _isWaitingForHotkey = false;
        Dispatcher.Invoke(() =>
        {
            TestHotkeyBtn.IsEnabled = true;
            HotkeyStatusLabel.Text = "Hotkey funktioniert.";
            FooterStatusLabel.Text = "Der Hotkey ist bereit.";
        });
        return true;
    }

    private bool ApplyHotkeyValues()
    {
        _pendingHotkeyModifiers = NormalizeHotkeyModifiers(_pendingHotkeyModifiers);
        _settings.HotkeyModifiers = _pendingHotkeyModifiers;
        _settings.HotkeyVk = _pendingHotkeyVk;
        _settings.HotkeyAction = PasteLatestRadio.IsChecked == true
            ? HotkeyAction.PasteLatest
            : HotkeyAction.OpenMenu;

        SyncHotkeyControls(_settings.HotkeyModifiers, _settings.HotkeyVk);
        _app.SaveSettings();
        var registered = _app.ReRegisterHotkey();
        Keyboard.ClearFocus();
        UpdateHotkeyStatus();
        FooterStatusLabel.Text = registered
            ? "Gespeichert."
            : $"Gespeichert, aber Windows blockiert diese Kombination. Fehlercode: {_app.LastHotkeyError}";
        return registered;
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        _app.OpenSettings();
    }

    private void StartWithWindows_Click(object sender, RoutedEventArgs e)
    {
        var enabled = StartWithWindowsCheck.IsChecked == true;
        if (_app.SetStartWithWindows(enabled))
        {
            FooterStatusLabel.Text = enabled
                ? "Autostart ist aktiv."
                : "Autostart ist aus.";
            return;
        }

        StartWithWindowsCheck.IsChecked = _settings.StartWithWindows;
        FooterStatusLabel.Text = "Autostart konnte nicht aktualisiert werden.";
    }

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        _app.SetStartWithWindows(StartWithWindowsCheck.IsChecked == true);
        _app.SaveSettings();
        Close();
    }

    private void UpdateHotkeyStatus()
    {
        var hotkey = App.FormatHotkey(_settings.HotkeyModifiers, _settings.HotkeyVk);
        var action = _settings.HotkeyAction == HotkeyAction.PasteLatest
            ? "neuesten Eintrag einfügen"
            : "Menü öffnen";
        HotkeyStatusLabel.Text = _app.IsHotkeyRegistered
            ? $"Aktiv: {hotkey} ({action})"
            : $"Noch nicht aktiv: {hotkey}";
        FooterStatusLabel.Text = _app.IsHotkeyRegistered
            ? "Du kannst den Hotkey testen oder direkt fertigstellen."
            : "Hotkey speichern, um ihn global zu registrieren.";
    }

    private static bool IsSupportedHotkeyKey(int vk)
        => vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A;

    private static uint NormalizeHotkeyModifiers(uint modifiers)
    {
        const uint baseMask = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT |
                              NativeMethods.MOD_SHIFT | NativeMethods.MOD_WIN;
        var baseModifiers = modifiers & baseMask;
        if (baseModifiers == 0)
            baseModifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT;
        return baseModifiers | NativeMethods.MOD_NOREPEAT;
    }

    private static uint ModifiersFromKeyboard()
    {
        uint mods = 0;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) mods |= NativeMethods.MOD_CONTROL;
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) mods |= NativeMethods.MOD_ALT;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) mods |= NativeMethods.MOD_SHIFT;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) mods |= NativeMethods.MOD_WIN;
        return mods;
    }

    private uint ModifiersFromSwitches()
    {
        uint mods = 0;
        if (ModWin.IsChecked == true) mods |= NativeMethods.MOD_WIN;
        if (ModShift.IsChecked == true) mods |= NativeMethods.MOD_SHIFT;
        if (ModCtrl.IsChecked == true) mods |= NativeMethods.MOD_CONTROL;
        if (ModAlt.IsChecked == true) mods |= NativeMethods.MOD_ALT;
        return mods;
    }

    private void SyncHotkeyControls(uint modifiers, uint vk)
    {
        modifiers = NormalizeHotkeyModifiers(modifiers);
        ModWin.IsChecked = (modifiers & NativeMethods.MOD_WIN) != 0;
        ModShift.IsChecked = (modifiers & NativeMethods.MOD_SHIFT) != 0;
        ModCtrl.IsChecked = (modifiers & NativeMethods.MOD_CONTROL) != 0;
        ModAlt.IsChecked = (modifiers & NativeMethods.MOD_ALT) != 0;
        HotkeyRecorderBox.Text = App.FormatHotkey(modifiers, vk);
        _pendingHotkeyModifiers = modifiers;
        _pendingHotkeyVk = vk;
    }
}

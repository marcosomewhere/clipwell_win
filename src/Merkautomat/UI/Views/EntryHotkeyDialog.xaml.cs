using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Merkautomat.Models;
using Merkautomat.Services;

namespace Merkautomat.Views;

/// <summary>Records a global hotkey for one history entry; the app validates and registers it.</summary>
public partial class EntryHotkeyDialog : Window
{
    private readonly Func<uint, uint, bool, string?> _assign;
    private readonly Action _remove;
    private uint _modifiers;
    private uint _vk;

    /// <param name="assign">Validates, registers and saves; returns an error text or null.</param>
    public EntryHotkeyDialog(string entryTitle, EntryHotkey? current, Func<uint, uint, bool, string?> assign, Action remove)
    {
        _assign = assign;
        _remove = remove;
        InitializeComponent();
        App.ApplyAppIcon(this);
        EditorChrome.ApplyTheme(this);
        SourceInitialized += (_, _) => EditorChrome.ApplyFrame(this);
        EntryText.Text = entryTitle;
        RemoveButton.Visibility = current == null ? Visibility.Collapsed : Visibility.Visible;
        if (current != null)
        {
            _modifiers = current.Modifiers;
            _vk = current.Vk;
            RecorderBox.Text = EntryHotkeys.Format(_modifiers, _vk);
            PlainTextBox.IsChecked = current.PlainText;
        }
        Loaded += (_, _) => RecorderBox.Focus();
    }

    private static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private void RecorderBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = CurrentModifiers();
        // Without modifiers, Tab, Enter and Esc keep their dialog meaning.
        if ((mods & ~NativeMethods.MOD_SHIFT) == 0 && key is Key.Tab or Key.Enter or Key.Escape) return;
        e.Handled = true;
        if (IsModifierKey(key)) return;

        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        _modifiers = EntryHotkeys.Normalize(mods);
        _vk = vk;
        RecorderBox.Text = EntryHotkeys.Format(_modifiers, _vk);
        ShowError(EntryHotkeys.Validate(_modifiers, _vk));
    }

    private static uint CurrentModifiers()
    {
        uint mods = 0;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) mods |= NativeMethods.MOD_CONTROL;
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) mods |= NativeMethods.MOD_ALT;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) mods |= NativeMethods.MOD_SHIFT;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) mods |= NativeMethods.MOD_WIN;
        return mods;
    }

    private void Assign_Click(object sender, RoutedEventArgs e)
    {
        if (_vk == 0)
        {
            ShowError("Bitte zuerst in das Feld klicken und eine Tastenkombination drücken.");
            RecorderBox.Focus();
            return;
        }
        var error = _assign(_modifiers, _vk, PlainTextBox.IsChecked == true);
        if (error != null) { ShowError(error); return; }
        DialogResult = true;
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        _remove();
        DialogResult = true;
    }

    private void ShowError(string? text)
    {
        ErrorText.Text = text ?? "";
        ErrorText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }
}

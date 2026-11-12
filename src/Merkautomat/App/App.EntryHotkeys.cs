using Merkautomat.Models;
using Merkautomat.Services;
using Merkautomat.ViewModels;
using Merkautomat.Views;

namespace Merkautomat;

public partial class App
{
    private const int MissingEntryError = -2;
    private const int ModifierReleaseTimeoutMs = 1500;

    private readonly Dictionary<int, long> _entryHotkeyIds = [];
    private readonly Dictionary<long, int> _entryHotkeyErrors = [];
    private bool _entryHotkeysSuspended;

    private EntryViewModel? FindEntry(long id) => _popupVm?.Entries.FirstOrDefault(e => e.Id == id);

    internal bool EntryExists(long id) => FindEntry(id) != null;

    /// <summary>(Re)registers all entry hotkeys and refreshes the labels in the popup list.</summary>
    internal void RegisterEntryHotkeys()
    {
        if (_msgWin == null || _settings == null) return;
        _msgWin.UnregisterEntryHotkeys();
        _entryHotkeyIds.Clear();
        _entryHotkeyErrors.Clear();

        var s = _settings.Settings;
        if (EntryHotkeys.Clean(s) > 0) _settings.Save();
        if (s.EntryHotkeysEnabled && !_entryHotkeysSuspended)
        {
            var id = MessageWindowService.EntryHotkeyBaseId;
            foreach (var h in s.EntryHotkeys)
            {
                if (FindEntry(h.EntryId) == null) { _entryHotkeyErrors[h.EntryId] = MissingEntryError; continue; }
                var error = _msgWin.RegisterEntryHotkey(id, h.Modifiers, h.Vk);
                if (error == 0) _entryHotkeyIds[id] = h.EntryId;
                else _entryHotkeyErrors[h.EntryId] = error;
                id++;
            }
        }
        RefreshEntryHotkeyLabels();
    }

    private void RefreshEntryHotkeyLabels()
    {
        if (_popupVm == null || _settings == null) return;
        var byId = _settings.Settings.EntryHotkeys.ToDictionary(h => h.EntryId);
        foreach (var vm in _popupVm.Entries)
            vm.HotkeyText = byId.TryGetValue(vm.Id, out var h) ? EntryHotkeys.Format(h.Modifiers, h.Vk) : null;
    }

    /// <summary>Recording a new combination must not trigger the existing hotkeys.</summary>
    internal void SuspendEntryHotkeys(bool suspend)
    {
        _entryHotkeysSuspended = suspend;
        RegisterEntryHotkeys();
    }

    internal string EntryHotkeyStatus(EntryHotkey h)
    {
        if (!CurrentSettings.EntryHotkeysEnabled) return "Ausgeschaltet";
        if (!_entryHotkeyErrors.TryGetValue(h.EntryId, out var error)) return _entryHotkeysSuspended ? "Pausiert" : "Aktiv";
        return error == MissingEntryError ? "Eintrag gelöscht" : "Belegt durch ein anderes Programm";
    }

    internal string EntryHotkeyTitle(long entryId)
        => FindEntry(entryId) is { } vm ? KnowledgeExport.ClipboardTitle(vm.Entry) : "(Eintrag nicht mehr vorhanden)";

    /// <summary>Validates, registers and saves; returns an error text or null. Pins the entry so purging keeps it.</summary>
    internal string? TryAssignEntryHotkey(long entryId, uint modifiers, uint vk, bool plainText)
    {
        var s = CurrentSettings;
        var vm = FindEntry(entryId);
        if (vm == null) return "Den Eintrag gibt es nicht mehr.";
        modifiers = EntryHotkeys.Normalize(modifiers);
        var error = EntryHotkeys.Validate(modifiers, vk) ?? EntryHotkeys.Conflict(s, entryId, modifiers, vk);
        if (error != null) return error;

        var previous = EntryHotkeys.Find(s, entryId);
        EntryHotkeys.Assign(s, entryId, modifiers, vk, plainText);
        var wasSuspended = _entryHotkeysSuspended;
        _entryHotkeysSuspended = false;
        RegisterEntryHotkeys();
        var failed = s.EntryHotkeysEnabled && _entryHotkeyErrors.ContainsKey(entryId);
        _entryHotkeysSuspended = wasSuspended;
        if (failed)
        {
            EntryHotkeys.Remove(s, entryId);
            if (previous != null) s.EntryHotkeys.Add(previous);
            RegisterEntryHotkeys();
            return $"{EntryHotkeys.Format(modifiers, vk)} ist schon von Windows oder einem anderen Programm belegt. Bitte eine andere Kombination wählen.";
        }

        if (!vm.IsPinned) _popupVm!.TogglePin(vm);
        _settings!.Save();
        RegisterEntryHotkeys();
        return null;
    }

    internal void RemoveEntryHotkey(long entryId)
    {
        if (!EntryHotkeys.Remove(CurrentSettings, entryId)) return;
        _settings!.Save();
        RegisterEntryHotkeys();
    }

    internal void RemoveAllEntryHotkeys()
    {
        CurrentSettings.EntryHotkeys.Clear();
        _settings!.Save();
        RegisterEntryHotkeys();
    }

    internal void SetEntryHotkeysEnabled(bool enabled)
    {
        if (CurrentSettings.EntryHotkeysEnabled == enabled) return;
        CurrentSettings.EntryHotkeysEnabled = enabled;
        _settings!.Save();
        RegisterEntryHotkeys();
    }

    /// <summary>Opens the recording dialog for an entry; true when something changed.</summary>
    internal bool ShowEntryHotkeyDialog(long entryId, System.Windows.Window? owner = null)
    {
        var current = EntryHotkeys.Find(CurrentSettings, entryId);
        var dialog = new EntryHotkeyDialog(EntryHotkeyTitle(entryId), current,
            (mods, vk, plain) => TryAssignEntryHotkey(entryId, mods, vk, plain),
            () => RemoveEntryHotkey(entryId));
        if (owner is { IsVisible: true }) { dialog.Owner = owner; dialog.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner; }

        var mainWasRegistered = IsHotkeyRegistered;
        SuspendHotkey();
        SuspendEntryHotkeys(true);
        try
        {
            return dialog.ShowDialog() == true;
        }
        finally
        {
            SuspendEntryHotkeys(false);
            if (mainWasRegistered) ReRegisterHotkey();
        }
    }

    private void OnEntryHotkeyPressed(object? sender, int id)
    {
        if (!_entryHotkeyIds.TryGetValue(id, out var entryId)) return;
        var hotkey = EntryHotkeys.Find(CurrentSettings, entryId);
        var vm = FindEntry(entryId);
        if (hotkey == null || vm == null)
        {
            _trayIcon?.ShowBalloonTip(3000, AppNames.App,
                "Der Eintrag zu diesem Hotkey wurde gelöscht. Zuweisung in der Kontrollstation unter Tastenkürzel entfernen.",
                ToolTipIcon.Info);
            RegisterEntryHotkeys();
            return;
        }

        if (_popup?.IsVisible == true) { PasteEntry(vm, hotkey.PlainText); return; }
        PasteIntoForeground(vm, hotkey.PlainText);
    }

    private void PasteIntoForeground(EntryViewModel vm, bool plainText)
    {
        var target = NativeMethods.GetForegroundWindow();
        _ignoreNextClipboard = true;
        if (!TryWriteEntryToClipboard(vm, plainText))
        {
            _ignoreNextClipboard = false;
            _trayIcon?.ShowBalloonTip(3000, AppNames.App, ClipboardBusyMessage, ToolTipIcon.Warning);
            return;
        }
        RecordEntryUsed(vm);

        // Still-held Alt, Shift or Win would turn the synthetic Ctrl+V into another shortcut.
        _ = Task.Run(async () =>
        {
            var waited = 0;
            while (ModifiersHeld() && waited < ModifierReleaseTimeoutMs) { await Task.Delay(15); waited += 15; }
            await Task.Delay(30);
            Dispatcher.Invoke(() =>
            {
                if (target != IntPtr.Zero) NativeMethods.SetForegroundWindow(target);
                NativeMethods.SendCtrlV();
            });
        });
    }

    private static bool ModifiersHeld()
    {
        foreach (var vk in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
            if ((NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0) return true;
        return false;
    }
}

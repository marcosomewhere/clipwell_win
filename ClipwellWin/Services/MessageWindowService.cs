using System.Windows.Interop;

namespace ClipwellWin.Services;

// Must be created on the UI thread.
public sealed class MessageWindowService : IDisposable
{
    private const int HotkeyId = 9001;
    // Entry hotkeys use their own id range so they never collide with the popup hotkey.
    public const int EntryHotkeyBaseId = 9100;
    private readonly HashSet<int> _entryHotkeyIds = [];

    private HwndSource? _source;

    public event EventHandler? ClipboardChanged;
    public event EventHandler? HotkeyPressed;
    public event EventHandler<int>? EntryHotkeyPressed;

    public bool IsHotkeyRegistered { get; private set; }
    public int LastHotkeyError { get; private set; }

    public void Initialize()
    {
        var p = new HwndSourceParameters("ClipwellMsgWnd")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
            Width = 0,
            Height = 0,
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
        NativeMethods.AddClipboardFormatListener(_source.Handle);
    }

    public bool RegisterHotkey(uint modifiers, uint vk)
    {
        if (_source == null) return false;
        UnregisterHotkey();

        IsHotkeyRegistered = NativeMethods.RegisterHotKey(_source.Handle, HotkeyId, modifiers, vk);
        LastHotkeyError = IsHotkeyRegistered
            ? 0
            : System.Runtime.InteropServices.Marshal.GetLastWin32Error();

        return IsHotkeyRegistered;
    }

    public void UnregisterHotkey()
    {
        if (_source == null) return;
        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        IsHotkeyRegistered = false;
    }

    /// <summary>Registers an entry hotkey under <paramref name="id"/>; returns the Win32 error code, 0 on success.</summary>
    public int RegisterEntryHotkey(int id, uint modifiers, uint vk)
    {
        if (_source == null) return -1;
        if (NativeMethods.RegisterHotKey(_source.Handle, id, modifiers, vk))
        {
            _entryHotkeyIds.Add(id);
            return 0;
        }
        var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        return error == 0 ? -1 : error;
    }

    public void UnregisterEntryHotkeys()
    {
        if (_source == null) return;
        foreach (var id in _entryHotkeyIds) NativeMethods.UnregisterHotKey(_source.Handle, id);
        _entryHotkeyIds.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WM_CLIPBOARDUPDATE:
                ClipboardChanged?.Invoke(this, EventArgs.Empty);
                handled = true;
                break;

            case NativeMethods.WM_HOTKEY when wParam.ToInt32() == HotkeyId:
                HotkeyPressed?.Invoke(this, EventArgs.Empty);
                handled = true;
                break;

            case NativeMethods.WM_HOTKEY when _entryHotkeyIds.Contains(wParam.ToInt32()):
                EntryHotkeyPressed?.Invoke(this, wParam.ToInt32());
                handled = true;
                break;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source == null) return;
        NativeMethods.RemoveClipboardFormatListener(_source.Handle);
        UnregisterHotkey();
        UnregisterEntryHotkeys();
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _source = null;
    }
}

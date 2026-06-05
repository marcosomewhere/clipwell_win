using System.Windows.Interop;

namespace ClipwellWin.Services;

/// <summary>
/// Message-only window that receives WM_CLIPBOARDUPDATE and WM_HOTKEY.
/// Must be created on the UI thread.
/// </summary>
public sealed class MessageWindowService : IDisposable
{
    private const int HotkeyId = 9001;

    private HwndSource? _source;

    public event EventHandler? ClipboardChanged;
    public event EventHandler? HotkeyPressed;

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
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source == null) return;
        NativeMethods.RemoveClipboardFormatListener(_source.Handle);
        UnregisterHotkey();
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _source = null;
    }
}

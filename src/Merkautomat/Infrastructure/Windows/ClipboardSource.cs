using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Merkautomat.Models;

namespace Merkautomat.Services;

internal static class ClipboardSource
{
    public static ContentMetadata Capture(ContentMetadata metadata)
    {
        var window = GetClipboardOwner();
        if (window == IntPtr.Zero) return metadata;
        GetWindowThreadProcessId(window, out var pid);
        if (pid == 0) return metadata;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            // Clipboard owners can be invisible helper windows. Only use a foreground title from the same process.
            var foreground = NativeMethods.GetForegroundWindow();
            GetWindowThreadProcessId(foreground, out var foregroundPid);
            var title = new StringBuilder(1024);
            GetWindowText(foregroundPid == pid ? foreground : window, title, title.Capacity);
            return metadata with { Application = process.ProcessName, WindowTitle = title.Length > 0 ? title.ToString() : null };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return metadata;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardOwner();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);
}

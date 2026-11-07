using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClipwellWin.Views;

/// <summary>
/// Semi-transparent snapshot of a dragged entry that follows the cursor, also outside Clipwell.
/// The window is click-through so OLE drag and drop still resolves the real drop target under it.
/// </summary>
internal sealed class DragGhost : IDisposable
{
    private const int CursorOffset = 14;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private readonly Window _window;
    private IntPtr _hwnd;

    private DragGhost(Window window) => _window = window;

    public static DragGhost? Create(FrameworkElement source)
    {
        var width = source.ActualWidth;
        var height = source.ActualHeight;
        if (width < 1 || height < 1) return null;

        var bitmap = Snapshot(source, width, height);
        if (bitmap == null) return null;
        var dpi = VisualTreeHelper.GetDpi(source);
        var ghostWidth = bitmap.PixelWidth / dpi.DpiScaleX;
        var ghostHeight = bitmap.PixelHeight / dpi.DpiScaleY;

        // No extra frame: the entry already carries its own selection ring, a second one doubled it.
        var image = new System.Windows.Controls.Image
        {
            Source = bitmap,
            Width = ghostWidth,
            Height = ghostHeight,
            Stretch = Stretch.Fill,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            Focusable = false,
            IsHitTestVisible = false,
            // Explicit size: SizeToContent left the window at its default size on some setups,
            // which framed the snapshot in a much larger box.
            SizeToContent = SizeToContent.Manual,
            ResizeMode = ResizeMode.NoResize,
            Width = ghostWidth,
            Height = ghostHeight,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
            Opacity = 0.85,
            Content = image,
        };
        var ghost = new DragGhost(window);
        window.SourceInitialized += (_, _) =>
        {
            ghost._hwnd = new WindowInteropHelper(window).Handle;
            var style = GetWindowLong(ghost._hwnd, GWL_EXSTYLE);
            SetWindowLong(ghost._hwnd, GWL_EXSTYLE,
                style | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };
        return ghost;
    }

    /// <summary>
    /// Renders the whole host window and cuts the element out of it. A VisualBrush of the element
    /// alone came out blank on some drags; the window render is what is on screen right now.
    /// </summary>
    private static BitmapSource? Snapshot(FrameworkElement source, double width, double height)
    {
        if (Window.GetWindow(source) is not { } host || host.ActualWidth < 1 || host.ActualHeight < 1)
            return null;
        var dpi = VisualTreeHelper.GetDpi(host);
        var full = new RenderTargetBitmap(
            (int)Math.Ceiling(host.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(host.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        full.Render(host);

        var bounds = source.TransformToAncestor(host).TransformBounds(new Rect(0, 0, width, height));
        var x = Math.Max(0, (int)Math.Round(bounds.X * dpi.DpiScaleX));
        var y = Math.Max(0, (int)Math.Round(bounds.Y * dpi.DpiScaleY));
        var right = Math.Min(full.PixelWidth, (int)Math.Round(bounds.Right * dpi.DpiScaleX));
        var bottom = Math.Min(full.PixelHeight, (int)Math.Round(bounds.Bottom * dpi.DpiScaleY));
        if (right - x < 1 || bottom - y < 1) return null;

        var cropped = new CroppedBitmap(full, new Int32Rect(x, y, right - x, bottom - y));
        cropped.Freeze();
        return cropped;
    }

    public void Show()
    {
        _window.Show();
        UpdatePosition();
    }

    public void UpdatePosition()
    {
        if (_hwnd == IntPtr.Zero || !GetCursorPos(out var cursor)) return;
        SetWindowPos(_hwnd, HWND_TOPMOST, cursor.X + CursorOffset, cursor.Y + CursorOffset, 0, 0,
            SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public void Dispose() => _window.Close();

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}

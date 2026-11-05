using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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
    private const double MaxWidth = 320;
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

        var dpi = VisualTreeHelper.GetDpi(source);
        // Drawing through a VisualBrush at the origin avoids the parent offset that
        // RenderTargetBitmap.Render applies when given the element directly.
        var drawing = new DrawingVisual();
        using (var ctx = drawing.RenderOpen())
            ctx.DrawRectangle(new VisualBrush(source), null, new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(width * dpi.DpiScaleX), (int)Math.Ceiling(height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        bitmap.Freeze();

        var scale = Math.Min(1.0, MaxWidth / width);
        var frame = new Border
        {
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(2),
            Child = new System.Windows.Controls.Image { Source = bitmap, Width = width * scale, Height = height * scale, Stretch = Stretch.Fill },
        };
        frame.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        frame.SetResourceReference(Border.BackgroundProperty, "PopupBackgroundBrush");

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
            SizeToContent = SizeToContent.WidthAndHeight,
            Opacity = 0.85,
            Content = frame,
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

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;

namespace Merkautomat.Views;

public partial class EyedropperWindow : Window
{
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const int WM_DPICHANGED = 0x02E0;

    public string? PickedHex { get; private set; }

    public EyedropperWindow()
    {
        InitializeComponent();
        App.ApplyAppIcon(this);
        Loaded += (_, _) =>
        {
            RootCanvas.Focus();
            Keyboard.Focus(RootCanvas);
            UpdateFromCursor();
        };
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProcHook);

        // A maximized window only covers one monitor; size the overlay to the whole virtual screen
        // in physical pixels so every monitor can be picked from.
        SetWindowPos(hwnd, IntPtr.Zero,
            GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
            GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN),
            SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private static IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // A window spanning monitors with different scaling would otherwise be resized to the
        // suggested rectangle of one monitor and stop covering the others.
        if (msg == WM_DPICHANGED)
            handled = true;
        return IntPtr.Zero;
    }

    private void Canvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        => UpdateFromCursor();

    private void Canvas_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!GetCursorPos(out var cursor)) return;
        var color = GetScreenPixel(cursor.X, cursor.Y);
        PickedHex    = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        DialogResult = true;
        Close();
    }

    private void Canvas_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }

    // Physical cursor coordinates stay correct across monitors with different scaling,
    // unlike WPF mouse positions inside a window that spans them.
    private void UpdateFromCursor()
    {
        if (!GetCursorPos(out var cursor)) return;
        UpdatePreview(cursor.X, cursor.Y);
        if (PresentationSource.FromVisual(this) == null) return;
        PositionPreview(PointFromScreen(new WpfPoint(cursor.X, cursor.Y)));
    }

    private void UpdatePreview(int x, int y)
    {
        var color    = GetScreenPixel(x, y);
        var wpfColor = WpfColor.FromRgb(color.R, color.G, color.B);
        ColorPreviewBox.Background = new SolidColorBrush(wpfColor);
        HexLabel.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        RgbLabel.Text = $"rgb({color.R}, {color.G}, {color.B})";
    }

    private void PositionPreview(WpfPoint cursor)
    {
        double px = cursor.X + 20;
        double py = cursor.Y + 20;
        if (px + PreviewBorder.ActualWidth + 10 > ActualWidth)
            px = cursor.X - PreviewBorder.ActualWidth - 10;
        if (py + PreviewBorder.ActualHeight + 10 > ActualHeight)
            py = cursor.Y - PreviewBorder.ActualHeight - 10;

        Canvas.SetLeft(PreviewBorder, px);
        Canvas.SetTop(PreviewBorder, py);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr hDC, int x, int y);

    private static (byte R, byte G, byte B) GetScreenPixel(int x, int y)
    {
        var dc = GetDC(IntPtr.Zero);
        try
        {
            uint color = GetPixel(dc, x, y);
            byte r = (byte)(color & 0xFF);
            byte g = (byte)((color >> 8) & 0xFF);
            byte b = (byte)((color >> 16) & 0xFF);
            return (r, g, b);
        }
        finally { ReleaseDC(IntPtr.Zero, dc); }
    }
}

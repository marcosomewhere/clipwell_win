using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;

namespace ClipwellWin.Views;

public partial class EyedropperWindow : Window
{
    public string? PickedHex { get; private set; }

    public EyedropperWindow()
    {
        InitializeComponent();
        App.ApplyAppIcon(this);
        Loaded += (_, _) =>
        {
            RootCanvas.Focus();
            Keyboard.Focus(RootCanvas);
        };
    }

    private void Canvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        UpdatePreview((int)pos.X, (int)pos.Y);
        PositionPreview(pos);
    }

    private void Canvas_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var pos   = e.GetPosition(this);
        var color = GetScreenPixel((int)pos.X, (int)pos.Y);
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
        if (px + PreviewBorder.Width + 10 > ActualWidth)
            px = cursor.X - PreviewBorder.Width - 10;
        if (py + PreviewBorder.Height + 10 > ActualHeight)
            py = cursor.Y - PreviewBorder.Height - 10;

        Canvas.SetLeft(PreviewBorder, px);
        Canvas.SetTop(PreviewBorder, py);
    }

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

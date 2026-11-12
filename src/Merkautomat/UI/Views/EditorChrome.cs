using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace Merkautomat.Views;

/// <summary>Theme helper for windows that merge EditorChrome.xaml (same chrome as the detail window).</summary>
internal static class EditorChrome
{
    public static void ApplyTheme(Window window)
    {
        var light = System.Windows.Application.Current is App app && !app.IsEffectiveDarkTheme;
        void Set(string key, byte r, byte g, byte b) => window.Resources[key] = new SolidColorBrush(Color.FromRgb(r, g, b));
        if (light)
        {
            Set("EditorChromeBrush", 247, 249, 252);
            Set("EditorPanelBrush", 255, 255, 255);
            Set("EditorPanelAltBrush", 239, 243, 247);
            Set("EditorBorderBrush", 207, 216, 226);
            Set("EditorMutedBrush", 96, 105, 118);
            Set("EditorHoverBrush", 226, 232, 240);
            Set("EditorPressedBrush", 216, 224, 235);
            Set("EditorSelectedBrush", 253, 236, 218);
            Set("EditorTextBodyBrush", 255, 255, 255);
            Set("EditorTextBrush", 17, 24, 39);
            Set("EditorTextMutedBrush", 100, 116, 139);
            Set("EditorSelectionTextBrush", 17, 20, 24);
        }
    }

    public static void ApplyFrame(Window window)
    {
        if (System.Windows.Application.Current is not App app) return;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.SetImmersiveDarkMode(hwnd, app.IsEffectiveDarkTheme);
        NativeMethods.SetWindowFrameColors(
            hwnd,
            app.IsEffectiveDarkTheme ? 0x000A0A0A : 0x00F9F7F4,
            app.IsEffectiveDarkTheme ? 0x00EDEDED : 0x00221F1B,
            app.IsEffectiveDarkTheme ? 0x00262626 : 0x00D0C8BD);
    }
}

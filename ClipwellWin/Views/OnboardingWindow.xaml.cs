using System.Windows;
using ClipwellWin.Models;

namespace ClipwellWin.Views;

public partial class OnboardingWindow : Window
{
    private readonly AppSettings _settings;
    private readonly App _app;

    public OnboardingWindow(AppSettings settings, App app)
    {
        _settings = settings;
        _app = app;
        InitializeComponent();

        HotkeyLabel.Text = $"Hotkey: {App.FormatHotkey(settings.HotkeyModifiers, settings.HotkeyVk)}";
    }

    private bool _hotkeyDetected;

    private void TestHotkey_Click(object sender, RoutedEventArgs e)
    {
        TestHotkeyBtn.IsEnabled = false;
        HotkeyResultLabel.Text = "Jetzt den Hotkey drücken…";
        HotkeyResultLabel.Visibility = Visibility.Visible;

        _ = Task.Delay(4000).ContinueWith(_ => Dispatcher.Invoke(() =>
        {
            TestHotkeyBtn.IsEnabled = true;
            if (!_hotkeyDetected)
            {
                HotkeyResultLabel.Text = "Kein Signal empfangen. Ist der Hotkey in den Einstellungen korrekt?";
            }
        }));
    }

    public void NotifyHotkeyTriggered()
    {
        _hotkeyDetected = true;
        Dispatcher.Invoke(() =>
        {
            HotkeyResultLabel.Text = "✓ Hotkey funktioniert!";
        });
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        _app.OpenSettings();
    }

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        _settings.FirstRunCompleted = true;
        _app.SaveSettings();
        Close();
    }
}

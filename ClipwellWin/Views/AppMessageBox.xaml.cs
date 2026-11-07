using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ClipwellWin.Services;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ClipwellWin.Views;

/// <summary>
/// Message box in the application design (colors, buttons, accent line), with the same call signature as
/// <see cref="System.Windows.MessageBox"/> so existing calls can switch by alias.
/// </summary>
public partial class AppMessageBox : Window
{
    private MessageBoxResult _result;
    private readonly MessageBoxResult _cancelResult;

    private AppMessageBox(string text, string caption, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult)
    {
        InitializeComponent();
        EditorChrome.ApplyTheme(this);
        SourceInitialized += (_, _) => EditorChrome.ApplyFrame(this);
        Title = caption;
        MessageText.Text = text;
        (Glyph.Text, var color) = image switch
        {
            MessageBoxImage.Error => ("", "#E5534B"),
            MessageBoxImage.Warning => ("", null),
            MessageBoxImage.Question => ("", null),
            MessageBoxImage.Information => ("", null),
            _ => ("", null),
        };
        if (color != null) Glyph.Foreground = (Brush)new BrushConverter().ConvertFromString(color)!;
        if (Glyph.Text.Length == 0) Glyph.Visibility = Visibility.Collapsed;

        var results = buttons switch
        {
            MessageBoxButton.OKCancel => new[] { MessageBoxResult.OK, MessageBoxResult.Cancel },
            MessageBoxButton.YesNo => [MessageBoxResult.Yes, MessageBoxResult.No],
            MessageBoxButton.YesNoCancel => [MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel],
            _ => [MessageBoxResult.OK],
        };
        _cancelResult = results.Contains(MessageBoxResult.Cancel) ? MessageBoxResult.Cancel
            : results.Contains(MessageBoxResult.No) ? MessageBoxResult.No : MessageBoxResult.OK;
        _result = _cancelResult;
        var focus = results.Contains(defaultResult) ? defaultResult : results[0];
        foreach (var result in results)
        {
            var button = new Button
            {
                Content = Label(result), MinWidth = 92, Margin = new Thickness(8, 0, 0, 0),
                Style = (Style)FindResource(result == focus ? "BrainPrimaryButton" : "EditorButtonStyle"),
                IsDefault = result == focus, IsCancel = result == _cancelResult,
            };
            button.Click += (_, _) => { _result = result; DialogResult = true; };
            ButtonPanel.Children.Add(button);
            if (result == focus) Loaded += (_, _) => button.Focus();
        }
        PreviewKeyDown += OnKey;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        _result = _cancelResult;
        Close();
        e.Handled = true;
    }

    private static string Label(MessageBoxResult result) => result switch
    {
        MessageBoxResult.Yes => "Ja",
        MessageBoxResult.No => "Nein",
        MessageBoxResult.Cancel => "Abbrechen",
        _ => "OK",
    };

    public static MessageBoxResult Show(Window? owner, string text, string caption = "", MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        var dialog = new AppMessageBox(text, caption.Length > 0 ? caption : AppNames.App, buttons, image, defaultResult);
        if (owner is { IsVisible: true }) dialog.Owner = owner;
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.Topmost = true;
            dialog.ShowInTaskbar = true;
        }
        dialog.ShowDialog();
        return dialog._result;
    }

    public static MessageBoxResult Show(string text, string caption = "", MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None)
        => Show(System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive), text, caption, buttons, image, defaultResult);
}

using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Merkautomat.Views;

public partial class StoryViewerWindow : Window
{
    public const int PageCount = 5;

    private int _page;

    public StoryViewerWindow()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
        ShowPage(0);
    }

    public static Uri PageUri(int page) =>
        new($"pack://application:,,,/Merkautomat;component/Assets/story-{page + 1}.jpg", UriKind.Absolute);

    private void ShowPage(int page)
    {
        _page = Math.Clamp(page, 0, PageCount - 1);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = PageUri(_page);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        PageImage.Source = bitmap;
        PageLabel.Text = $"Seite {_page + 1} von {PageCount}";
        PrevButton.IsEnabled = _page > 0;
        NextButton.Content = _page < PageCount - 1 ? "Weiter ›" : "Schließen";
    }

    private void Next()
    {
        if (_page < PageCount - 1) ShowPage(_page + 1);
        else Close();
    }

    private void PrevButton_Click(object sender, RoutedEventArgs e) => ShowPage(_page - 1);

    private void NextButton_Click(object sender, RoutedEventArgs e) => Next();

    private void PageImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => Next();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Right or Key.PageDown or Key.Space:
                if (_page < PageCount - 1) ShowPage(_page + 1);
                e.Handled = true;
                break;
            case Key.Left or Key.PageUp or Key.Back:
                ShowPage(_page - 1);
                e.Handled = true;
                break;
            case Key.Home:
                ShowPage(0);
                e.Handled = true;
                break;
            case Key.End:
                ShowPage(PageCount - 1);
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }
}

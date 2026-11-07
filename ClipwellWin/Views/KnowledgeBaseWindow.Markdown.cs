using System.Windows;
using Brush = System.Windows.Media.Brush;

namespace ClipwellWin.Views;

public partial class KnowledgeBaseWindow
{
    private bool _formattedMarkdown = true;
    private string? _renderedMarkdown;

    private void ArticleViewMode_Checked(object sender, RoutedEventArgs e)
    {
        if (MarkdownView == null || FormattedMarkdownView == null) return;
        _formattedMarkdown = sender == ArticleMarkdown;
        RefreshMarkdownPreview(keepScroll: true);
    }

    private void RefreshMarkdownPreview(bool keepScroll)
    {
        var article = _view is KbView.Overview or KbView.Articles;
        ArticleViewSwitch.Visibility = article ? Visibility.Visible : Visibility.Collapsed;
        MarkdownView.Visibility = article && !_formattedMarkdown ? Visibility.Visible : Visibility.Collapsed;
        FormattedMarkdownView.Visibility = article && _formattedMarkdown ? Visibility.Visible : Visibility.Collapsed;
        if (!article || !_formattedMarkdown) return;
        if (_renderedMarkdown != MarkdownView.Text)
        {
            var offset = FormattedMarkdownView.VerticalOffset;
            FormattedMarkdownView.Document = MarkdownPreview.Build(MarkdownView.Text,
                (Brush)FindResource("EditorTextBrush"), (Brush)FindResource("EditorTextBodyBrush"),
                (Brush)FindResource("OnboardingAccentBrush"), (Brush)FindResource("EditorBorderBrush"));
            _renderedMarkdown = MarkdownView.Text;
            if (keepScroll) FormattedMarkdownView.ScrollToVerticalOffset(offset);
        }
        if (!keepScroll) FormattedMarkdownView.ScrollToHome();
    }
}

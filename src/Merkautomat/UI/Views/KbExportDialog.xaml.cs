using System.Windows;

namespace Merkautomat.Views;

public partial class KbExportDialog : Window
{
    public KbExportDialog(string scopeText, bool includeOverview, bool canExportArticles)
    {
        InitializeComponent();
        EditorChrome.ApplyTheme(this);
        App.ApplyAppIcon(this);
        ScopeLabel.Text = scopeText;
        OverviewCheck.IsChecked = includeOverview;
        if (!canExportArticles)
        {
            // Nothing but the overview is available; the overview has to be part of the export.
            OverviewCheck.IsChecked = true;
            OverviewCheck.IsEnabled = false;
        }
    }

    public bool AsZip => ZipOption.IsChecked == true;
    public bool IncludeOverview => OverviewCheck.IsChecked == true;

    private void Window_SourceInitialized(object sender, EventArgs e) => EditorChrome.ApplyFrame(this);

    private void Export_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}

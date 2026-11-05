using System.Windows;
using System.Windows.Controls;
using ClipwellWin.Views;
using Xunit;

namespace ClipwellWin.Tests;

public class KbExportDialogSmokeTests
{
    [Fact]
    public void ExportDialog_LoadsSharedChromeAndDefaultsToCombinedMarkdown()
    {
        StaTestRunner.Run(() =>
        {
            if (Application.Current == null)
                _ = new Application();

            var dialog = new KbExportDialog("3 ausgewählte Artikel", includeOverview: false, canExportArticles: true);
            dialog.Show();
            dialog.UpdateLayout();

            Assert.False(dialog.AsZip);
            Assert.False(dialog.IncludeOverview);
            Assert.NotNull(dialog.TryFindResource("PrimaryEditorButtonStyle"));
            Assert.Equal("3 ausgewählte Artikel", Assert.IsType<TextBlock>(dialog.FindName("ScopeLabel")).Text);
            dialog.Close();
        });
    }

    [Fact]
    public void ExportDialog_WithoutArticles_ForcesOverview()
    {
        StaTestRunner.Run(() =>
        {
            if (Application.Current == null)
                _ = new Application();

            var dialog = new KbExportDialog("leer", includeOverview: false, canExportArticles: false);
            Assert.True(dialog.IncludeOverview);
            Assert.False(Assert.IsType<CheckBox>(dialog.FindName("OverviewCheck")).IsEnabled);
            dialog.Close();
        });
    }
}

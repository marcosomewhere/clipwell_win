using System.Windows;
using System.Windows.Media;
using Merkautomat.Views;
using Xunit;

namespace Merkautomat.Tests;

public class MyKnowledgePanelTests
{
    /// <summary>Loads the XAML with all styles, filters, the answer area and the context menu; without App nothing is shown.</summary>
    [Fact]
    public void Panel_LoadsItsXamlWithoutData()
    {
        StaTestRunner.Run(() =>
        {
            var app = TestApplication.Ensure();
            foreach (var key in new[] { "OnboardingAccentBrush", "EditorMutedBrush" })
                if (!app.Resources.Contains(key)) app.Resources[key] = Brushes.Gray;

            var panel = new MyKnowledgePanel();
            var window = new Window { Content = panel, Width = 1100, Height = 700, ShowActivated = false };
            window.Show();
            try
            {
                window.UpdateLayout();
                Assert.False(panel.HasSelection);
                Assert.Equal(0, panel.Count);
                Assert.Equal("", panel.Subtitle);
                Assert.StartsWith("# Mein Wissen", panel.CopyText());
                Assert.NotNull(panel.FindName("AnswerPanel"));
                Assert.NotNull(panel.FindName("StateSimilar"));
            }
            finally { window.Close(); }
        });
    }
}

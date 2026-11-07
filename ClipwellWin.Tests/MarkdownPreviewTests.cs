using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ClipwellWin.Views;
using Xunit;

namespace ClipwellWin.Tests;

public sealed class MarkdownPreviewTests
{
    private static FlowDocument Build(string markdown) => MarkdownPreview.Build(markdown, Brushes.White, Brushes.Black, Brushes.Orange, Brushes.Gray);

    [Fact]
    public void Build_RendersHeadingsListsAndCodeWithoutReinterpretingCode()
        => StaTestRunner.Run(() =>
        {
            var document = Build("# Uebersicht\n\n- **Wichtig**\n- Zweiter Punkt\n\n```csharp\n**kein Fettdruck**\n```");
            var blocks = document.Blocks.ToList();
            var heading = Assert.IsType<Paragraph>(blocks[0]);
            Assert.Equal("# Uebersicht", heading.Tag);
            Assert.Equal(24, heading.FontSize);
            Assert.Equal(2, Assert.IsType<System.Windows.Documents.List>(blocks[1]).ListItems.Count);
            var code = Assert.IsType<Paragraph>(blocks[2]);
            Assert.Equal("**kein Fettdruck**", Assert.IsType<Run>(Assert.Single(code.Inlines)).Text);
        });

    [Fact]
    public void Build_RendersTableAndKeepsMissingCells()
        => StaTestRunner.Run(() =>
        {
            var table = Assert.IsType<Table>(Assert.Single(Build("| Name | Zahl |\n| --- | ---: |\n| Test | 3 |\n| Leer |").Blocks));
            Assert.Equal(2, table.Columns.Count);
            Assert.Equal(3, table.RowGroups[0].Rows.Count);
            Assert.Equal(2, table.RowGroups[0].Rows[2].Cells.Count);
        });

    [Fact]
    public void Build_OnlyActivatesWebLinksAndDoesNotLoadImagesOrHtml()
        => StaTestRunner.Run(() =>
        {
            var document = Build("[Web](https://example.org) [Datei](file:///C:/test.txt) ![Bild](https://example.org/image.png) <script>test</script>");
            var paragraph = Assert.IsType<Paragraph>(Assert.Single(document.Blocks));
            var link = Assert.Single(paragraph.Inlines.OfType<Hyperlink>());
            Assert.Equal("https", link.NavigateUri.Scheme);
            var text = new TextRange(document.ContentStart, document.ContentEnd).Text;
            Assert.Contains("file:///C:/test.txt", text);
            Assert.Contains("Bild: Bild", text);
            Assert.Contains("<script>test</script>", text);
        });

    [Fact]
    public void Build_HandlesUnclosedFenceAndCanLayoutInNarrowReader()
        => StaTestRunner.Run(() =>
        {
            var document = Build("# Titel\n\n```\nEine lange Codezeile ohne Abschluss");
            var reader = new System.Windows.Controls.RichTextBox { Document = document, Width = 300, Height = 400, IsReadOnly = true };
            reader.Measure(new Size(300, 400));
            reader.Arrange(new Rect(0, 0, 300, 400));
            Assert.True(reader.DesiredSize.Height > 0);
            Assert.Contains("Eine lange Codezeile", new TextRange(document.ContentStart, document.ContentEnd).Text);
        });
}

using System.IO;
using Merkautomat.Models;
using Merkautomat.Services;
using Xunit;

namespace Merkautomat.Tests;

public class WebContentTests
{
    [Fact]
    public void Convert_RendersHeadingsParagraphsAndInlineFormatting()
    {
        var md = HtmlMarkdownConverter.Convert(
            "<h2>Datenbank</h2><p>Typ: <strong>Postgres</strong> 18.4</p><p>Server: <em>demo_db01</em>:5432</p>");

        Assert.Equal("## Datenbank\n\nTyp: **Postgres** 18.4\n\nServer: *demo_db01*:5432", md);
    }

    [Fact]
    public void Convert_RendersTableWithHeaderRow()
    {
        var md = HtmlMarkdownConverter.Convert(
            "<table><tr><th>Port</th><th>Destination</th></tr>" +
            "<tr><td>3000</td><td>mockoon:80</td></tr>" +
            "<tr><td>9443</td><td>portainer|9443</td></tr></table>");

        Assert.Equal(
            "| Port | Destination |\n| --- | --- |\n| 3000 | mockoon:80 |\n| 9443 | portainer\\|9443 |",
            md);
    }

    [Fact]
    public void Convert_DropsScriptsStylesAndNavigation()
    {
        var md = HtmlMarkdownConverter.Convert(
            "<nav><a href=\"/home\">Home</a></nav><style>p{color:red}</style>" +
            "<script>alert(1)</script><p>Inhalt</p><footer>Impressum</footer>");

        Assert.Equal("Inhalt", md);
    }

    [Fact]
    public void Convert_RendersLinksListsAndCodeBlocks()
    {
        var md = HtmlMarkdownConverter.Convert(
            "<ul><li>Admin: <a href=\"https://example.com:9443/\">Portainer</a></li>" +
            "<li>Zweiter<ol><li>Unterpunkt</li></ol></li></ul>" +
            "<pre><code>docker ps\ndocker logs app</code></pre>" +
            "<a href=\"javascript:void(0)\">kein Link</a>");

        Assert.Equal(
            "- Admin: [Portainer](https://example.com:9443/)\n- Zweiter\n  1. Unterpunkt\n\n" +
            "```\ndocker ps\ndocker logs app\n```\n\nkein Link",
            md);
    }

    [Fact]
    public void Convert_DecodesEntitiesAndCollapsesWhitespace()
    {
        var md = HtmlMarkdownConverter.Convert("<p>Gr&uuml;&szlig;e   &amp;\n  Dank&nbsp;Euch</p>");

        Assert.Equal("Grüße & Dank Euch", md);
    }

    [Fact]
    public void ClipboardHtml_RoundTripsFragmentAndSourceUrl()
    {
        const string fragment = "<p>Grüße <b>aus</b> Köln</p>";
        var cfHtml = ClipboardProcessor.BuildClipboardHtml(fragment, "https://example.com/seite");

        var (parsed, sourceUrl) = ClipboardProcessor.ParseClipboardHtml(cfHtml);

        Assert.Equal(fragment, parsed);
        Assert.Equal("https://example.com/seite", sourceUrl);
    }

    [Fact]
    public void ParseClipboardHtml_IgnoresFragmentsWithoutStructure()
    {
        var cfHtml = ClipboardProcessor.BuildClipboardHtml("<span>nur Text</span>", "https://example.com/");

        var (parsed, sourceUrl) = ClipboardProcessor.ParseClipboardHtml(cfHtml);

        Assert.Null(parsed);
        Assert.Equal("https://example.com/", sourceUrl);
    }

    [Fact]
    public void ParseClipboardHtml_RejectsNonHttpSourceUrl()
    {
        var cfHtml = ClipboardProcessor.BuildClipboardHtml("<p>x</p>", "file:///C:/secret.html");

        var (_, sourceUrl) = ClipboardProcessor.ParseClipboardHtml(cfHtml);

        Assert.Null(sourceUrl);
    }

    [Fact]
    public void Database_PersistsHtmlAndLoadsItOnDemand()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"merkautomat-test-{Guid.NewGuid():N}.db");
        try
        {
            long id;
            using (var db = new DatabaseService(dbPath))
            {
                id = db.Insert(new ClipboardEntry
                {
                    Type = EntryType.Text,
                    Content = "Port 3000",
                    HtmlContent = "<table><tr><td>3000</td></tr></table>",
                    SourceUrl = "https://example.com/wiki",
                    Timestamp = new DateTime(2026, 11, 5, 9, 0, 0),
                });
            }

            using (var db = new DatabaseService(dbPath))
            {
                var lite = Assert.Single(db.LoadAllLite());
                Assert.Null(lite.HtmlContent);
                Assert.True(lite.HasHtml);
                Assert.Equal("https://example.com/wiki", lite.SourceUrl);
                Assert.Equal("<table><tr><td>3000</td></tr></table>", db.LoadHtml(id));

                var full = Assert.Single(db.LoadAll());
                Assert.Equal("<table><tr><td>3000</td></tr></table>", full.HtmlContent);
            }
        }
        finally
        {
            foreach (var path in new[] { dbPath, $"{dbPath}-wal", $"{dbPath}-shm" })
                if (File.Exists(path)) File.Delete(path);
        }
    }
}

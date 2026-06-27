using ClipwellWin.Models;
using ClipwellWin.Views;
using Xunit;

namespace ClipwellWin.Tests;

public class DetailWindowExportTests
{
    [Fact]
    public void BuildTextExportFormats_DefaultsMarkdownForMarkdownContent()
    {
        var formats = DetailWindow.BuildTextExportFormats(
            EntryType.Text,
            language: null,
            contentKind: "MARKDOWN",
            text: "# Titel");

        Assert.Equal("md", formats[0].Extension);
        Assert.Contains(formats, f => f.Extension == "html");
        Assert.Contains(formats, f => f.Extension == "js");
    }

    [Fact]
    public void ResolveSelectedExportFormat_PrefersSelectedFilterOverDefaultFileExtension()
    {
        var formats = DetailWindow.BuildTextExportFormats(
            EntryType.Text,
            language: null,
            contentKind: null,
            text: "plain");

        var selected = DetailWindow.ResolveSelectedExportFormat(
            formats,
            filterIndex: 2,
            fileName: "clipwell.txt");

        Assert.Equal("md", selected.Extension);
    }

    [Fact]
    public void FormatExportContent_WritesWindowsUrlShortcut()
    {
        var output = DetailWindow.FormatExportContent(
            "https://example.com/path",
            "url",
            EntryType.Url,
            language: null,
            contentKind: "example.com",
            title: "Example");

        Assert.Contains("[InternetShortcut]", output);
        Assert.Contains("URL=https://example.com/path", output);
    }

    [Fact]
    public void FormatExportContent_WrapsPlainTextAsHtml()
    {
        var output = DetailWindow.FormatExportContent(
            "Hallo <Welt>",
            "html",
            EntryType.Text,
            language: null,
            contentKind: null,
            title: null);

        Assert.Contains("<!DOCTYPE html>", output);
        Assert.Contains("Hallo &lt;Welt&gt;", output);
    }
}

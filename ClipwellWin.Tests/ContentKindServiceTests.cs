using ClipwellWin.Services;
using Xunit;

namespace ClipwellWin.Tests;

public class ContentKindServiceTests
{
    [Fact]
    public void DetectTextKind_KeepsColonNotesAsText()
    {
        var text = """
            Problem: The popup sometimes stays open after focus changes.
            Action: Restart the app and test the hotkey again.
            Result: No crash was observed during the manual run.
            """;

        Assert.Null(ContentKindService.DetectTextKind(text, language: null));
    }

    [Fact]
    public void DetectTextKind_StillDetectsProperties()
    {
        var text = """
            server.port=8080
            app.name=Clipwell
            """;

        Assert.Equal("PROPS", ContentKindService.DetectTextKind(text, language: null));
    }

    [Fact]
    public void DetectTextKind_KeepsHtmlLanguageAsHtml()
    {
        var text = """
            <!doctype html>
            <html lang="de">
            <head><title>Clipwell</title></head>
            <body><main class="panel"></main></body>
            </html>
            """;

        Assert.Equal("HTML", ContentKindService.DetectTextKind(text, language: "HTML"));
    }
}

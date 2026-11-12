using Merkautomat.Services;
using Xunit;

namespace Merkautomat.Tests;

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
    public void DetectTextKind_LabelsPropertiesAsCode()
    {
        var text = """
            server.port=8080
            app.name=Merkautomat
            """;

        Assert.Equal("CODE", ContentKindService.DetectTextKind(text, language: null));
    }

    [Fact]
    public void DetectTextKind_DoesNotLabelTwoEnvLinesAsCode()
    {
        var text = """
            API_KEY=test
            FEATURE_FLAG=true
            """;

        Assert.Null(ContentKindService.DetectTextKind(text, language: null));
    }

    [Fact]
    public void DetectTextKind_LabelsThreeEnvLinesAsCode()
    {
        var text = """
            API_KEY=test
            FEATURE_FLAG=true
            MERKAUTOMAT_MODE=dev
            """;

        Assert.Equal("CODE", ContentKindService.DetectTextKind(text, language: null));
    }

    [Fact]
    public void DetectTextKind_LabelsLanguagesAsCode()
    {
        var text = """
            <!doctype html>
            <html lang="de">
            <head><title>Merkautomat</title></head>
            <body><main class="panel"></main></body>
            </html>
            """;

        Assert.Equal("CODE", ContentKindService.DetectTextKind(text, language: "HTML"));
    }
}

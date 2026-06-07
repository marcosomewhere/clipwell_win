using ClipwellWin.Models;
using ClipwellWin.Services;
using Xunit;

namespace ClipwellWin.Tests;

public class SyntaxServiceTests
{
    [Theory]
    [InlineData("This is a normal sentence with enough ordinary words.", null)]
    [InlineData("SELECT Id, Name FROM Users WHERE IsActive = 1", "SQL")]
    [InlineData("{ \"name\": \"clipwell\", \"enabled\": true }", "JSON")]
    [InlineData("Get-ChildItem -Recurse | Select-String -Pattern TODO", "PowerShell")]
    [InlineData("const value = items.map(x => x.id);", "JavaScript")]
    public void DetectLanguage_Classifies_CommonInputs(string text, string? expected)
    {
        Assert.Equal(expected, SyntaxService.DetectLanguage(text));
    }

    [Fact]
    public void DetectLanguage_ConservativeMode_AvoidsWeakSignals()
    {
        var text = "type words: string and number appear in this sentence";

        Assert.Null(SyntaxService.DetectLanguage(text, CodeDetectionMode.Conservative));
    }

    [Fact]
    public void DetectLanguage_AggressiveMode_ReturnsGenericCodeForUnknownCodeShape()
    {
        var text = "foo := bar\nbaz := qux";

        Assert.Equal("Go", SyntaxService.DetectLanguage(text, CodeDetectionMode.Aggressive));
    }

    [Fact]
    public void DetectLanguage_AggressiveMode_DoesNotClassifyColonNotesAsCode()
    {
        var text = """
            Problem: The popup sometimes stays open (after focus changes).
            Action: Restart the app and test the hotkey again.
            Result: The state is normal and no crash was observed.
            """;

        Assert.Null(SyntaxService.DetectLanguage(text, CodeDetectionMode.Aggressive));
    }
}

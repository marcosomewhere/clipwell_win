using ClipwellWin.Services;
using Xunit;

namespace ClipwellWin.Tests;

public class SensitiveContentServiceTests
{
    [Theory]
    [InlineData("api_key=abcdefghijklmnopqrstuvwxyz123456", "API Key")]
    [InlineData("Authorization: Bearer abcdefghijklmnopqrstuvwxyz.1234567890", "Bearer Token")]
    [InlineData("password=correct-horse", "Password Assignment")]
    [InlineData("-----BEGIN PRIVATE KEY-----", "Private Key Block")]
    public void IsSensitive_DetectsSecrets(string content, string expectedRule)
    {
        Assert.True(SensitiveContentService.IsSensitive(content, out var rule));
        Assert.Equal(expectedRule, rule);
    }

    [Fact]
    public void IsSensitive_AllowsOrdinaryText()
    {
        Assert.False(SensitiveContentService.IsSensitive("A normal clipboard sentence.", out var rule));
        Assert.Null(rule);
    }
}

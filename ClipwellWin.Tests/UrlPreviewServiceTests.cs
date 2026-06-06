using ClipwellWin.Services;
using Xunit;

namespace ClipwellWin.Tests;

public class UrlPreviewServiceTests
{
    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://github.com/foo")]
    [InlineData("https://8.8.8.8/path")]
    public void ShouldFetch_AllowsPublicUrls(string url)
        => Assert.True(UrlPreviewService.ShouldFetch(url));

    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("http://127.0.0.1/admin")]
    [InlineData("http://10.0.0.5")]
    [InlineData("http://192.168.1.1")]
    [InlineData("http://172.16.4.4")]
    [InlineData("http://169.254.1.1")]
    [InlineData("http://[::1]/")]
    [InlineData("ftp://example.com")]
    [InlineData("not a url")]
    [InlineData(null)]
    public void ShouldFetch_BlocksInternalOrInvalid(string? url)
        => Assert.False(UrlPreviewService.ShouldFetch(url));
}

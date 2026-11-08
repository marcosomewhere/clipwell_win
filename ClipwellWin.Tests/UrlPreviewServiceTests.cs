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

    [Theory]
    [InlineData("http://0.0.0.0/")]
    [InlineData("http://100.100.1.1/")]
    [InlineData("http://192.0.0.8/")]
    [InlineData("http://198.18.0.1/")]
    [InlineData("http://224.0.0.1/")]
    [InlineData("http://255.255.255.255/")]
    [InlineData("http://[::]/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    [InlineData("http://[::ffff:192.168.0.1]/")]
    [InlineData("http://[fe80::1]/")]
    [InlineData("http://[fd00::1]/")]
    [InlineData("http://[ff02::1]/")]
    [InlineData("http://2130706433/")]
    public void ShouldFetch_BlocksSpecialAddressRanges(string url)
        => Assert.False(UrlPreviewService.ShouldFetch(url));

    [Theory]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.1")]
    [InlineData("172.32.0.1")]
    [InlineData("2001:4860:4860::8888")]
    public void IsPrivate_LeavesPublicNeighboursAlone(string address)
        => Assert.False(UrlPreviewService.IsPrivate(System.Net.IPAddress.Parse(address)));
}

public class WebLinkTests
{
    [Theory]
    [InlineData("https://example.com/a b", "https://example.com/a%20b")]
    [InlineData("  http://example.com/\"x\"  ", "http://example.com/%22x%22")]
    [InlineData("HTTPS://Example.com", "https://example.com/")]
    public void Normalize_EscapesForTheShell(string input, string expected)
        => Assert.Equal(expected, WebLink.Normalize(input));

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("C:\\Windows\\System32\\calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:privacy")]
    [InlineData("http://")]
    [InlineData("https://example.com\ncalc.exe")]
    [InlineData(null)]
    public void Normalize_RejectsEverythingButWebAddresses(string? input)
        => Assert.Null(WebLink.Normalize(input));
}

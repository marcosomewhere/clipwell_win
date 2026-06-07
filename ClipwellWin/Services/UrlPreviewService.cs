using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ClipwellWin.Services;

public class UrlPreviewService : IDisposable
{
    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(3),
        DefaultRequestHeaders = { { "User-Agent", "Clipwell/1.0" } },
    };

    private static readonly Regex TitleRx = new(@"<title[^>]*>([^<]{1,300})</title>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex LinkRx = new(@"<link\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex AttrRx = new(@"(?<name>[\w:-]+)\s*=\s*([""'])(?<value>.*?)\2",
        RegexOptions.IgnoreCase);

    public static bool IsUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        return (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
               && !t.Contains('\n') && t.Length < 2048;
    }

    // skips loopback, link-local, and private ranges — SSRF/privacy guard
    public static bool ShouldFetch(string? url)
    {
        if (!IsUrl(url)) return false;
        if (!Uri.TryCreate(url!.Trim(), UriKind.Absolute, out var uri)) return false;

        var host = uri.Host;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return false;
        if (IPAddress.TryParse(host, out var ip))
        {
            if (IPAddress.IsLoopback(ip)) return false;
            if (IsPrivate(ip)) return false;
        }
        return true;
    }

    private static bool IsPrivate(IPAddress ip)
    {
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            if (b[0] == 10) return true;                              // 10.0.0.0/8
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true; // 172.16.0.0/12
            if (b[0] == 192 && b[1] == 168) return true;             // 192.168.0.0/16
            if (b[0] == 169 && b[1] == 254) return true;             // 169.254.0.0/16 link-local
            return false;
        }
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal
                || (ip.GetAddressBytes()[0] & 0xFE) == 0xFC;          // fc00::/7 unique-local
        return false;
    }

    private const int MaxHtmlBytes = 512 * 1024;

    public async Task<(string? title, byte[]? favicon)> FetchAsync(string url)
    {
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            using var stream = await response.Content.ReadAsStreamAsync();
            var buf = new byte[MaxHtmlBytes];
            int total = 0, read;
            while (total < buf.Length && (read = await stream.ReadAsync(buf.AsMemory(total))) > 0)
                total += read;
            var html = Encoding.UTF8.GetString(buf, 0, total);
            var title = WebUtility.HtmlDecode(TitleRx.Match(html).Groups[1].Value.Trim());
            if (title.Length == 0) title = null;

            byte[]? favicon = null;
            foreach (var candidate in GetFaviconCandidates(url, html))
            {
                try
                {
                    var bytes = await _http.GetByteArrayAsync(candidate);
                    if (IsSupportedFavicon(bytes))
                    {
                        favicon = bytes;
                        break;
                    }
                }
                catch { }
            }

            return (title, favicon?.Length > 0 ? favicon : null);
        }
        catch { return (null, null); }
    }

    private static IEnumerable<string> GetFaviconCandidates(string pageUrl, string html)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var baseUri))
            yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match link in LinkRx.Matches(html))
        {
            var attrs = ParseAttributes(link.Value);
            if (!attrs.TryGetValue("rel", out var rel) || !attrs.TryGetValue("href", out var href))
                continue;

            var relTokens = rel.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (!relTokens.Any(t => t.Contains("icon", StringComparison.OrdinalIgnoreCase)))
                continue;
            if (href.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!Uri.TryCreate(baseUri, WebUtility.HtmlDecode(href), out var iconUri))
                continue;
            if (seen.Add(iconUri.AbsoluteUri))
                yield return iconUri.AbsoluteUri;
        }

        var fallback = new Uri(baseUri, "/favicon.ico").AbsoluteUri;
        if (seen.Add(fallback))
            yield return fallback;
    }

    private static Dictionary<string, string> ParseAttributes(string tag)
    {
        var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match attr in AttrRx.Matches(tag))
            attrs[attr.Groups["name"].Value] = attr.Groups["value"].Value;
        return attrs;
    }

    private static bool IsSupportedFavicon(byte[] bytes)
    {
        if (bytes.Length < 4) return false;
        if (bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0x01 && bytes[3] == 0x00) return true;
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return true;
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return true;
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38) return true;
        if (bytes[0] == 0x42 && bytes[1] == 0x4D) return true;
        if (bytes.Length >= 12
            && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
            && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50) return true;
        return false;
    }

    public void Dispose() => _http.Dispose();
}

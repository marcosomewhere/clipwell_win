using System.Text.RegularExpressions;

namespace Merkautomat.Services;

/// <summary>A piece of plain text, or a web link when <see cref="Link"/> is set.</summary>
public sealed record LinkSegment(string Text, Uri? Link = null);

/// <summary>Finds http(s) links in plain text (also Outlook's "&lt;url&gt;" form) and gives them a short readable label.</summary>
public static partial class LinkText
{
    public const int MaxLabel = 48;

    public static List<LinkSegment> Split(string text)
    {
        var result = new List<LinkSegment>();
        var at = 0;
        foreach (Match match in Url().Matches(text))
        {
            var raw = match.Groups["bracketed"].Success ? match.Groups["bracketed"].Value : match.Groups["plain"].Value;
            var url = match.Groups["bracketed"].Success ? raw : raw.TrimEnd('.', ',', ';', ':', '!', '?', ')', '\'', '"');
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) continue;
            if (match.Index > at) result.Add(new LinkSegment(text[at..match.Index]));
            result.Add(new LinkSegment(Label(uri), uri));
            // Punctuation trimmed off a plain link stays visible as text.
            at = match.Groups["bracketed"].Success ? match.Index + match.Length : match.Index + url.Length;
        }
        if (at < text.Length) result.Add(new LinkSegment(text[at..]));
        return result;
    }

    /// <summary>"example.com/docs" for short links; long tracking links shrink to their host.</summary>
    public static string Label(Uri uri)
    {
        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        var full = host + uri.PathAndQuery.TrimEnd('/') + uri.Fragment;
        return full.Length <= MaxLabel ? full : host + "/…";
    }

    [GeneratedRegex(@"<(?<bracketed>https?://[^\s<>]+)>|(?<plain>https?://[^\s<>""]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Url();
}

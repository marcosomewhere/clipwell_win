using System.Text.RegularExpressions;

namespace ClipwellWin.Services;

public static class SensitiveContentService
{
    private static readonly (string Name, Regex Pattern)[] Rules =
    [
        ("API Key",              new Regex(@"(?:api[-_]?key|apikey)\s*[=:]\s*[""']?[A-Za-z0-9_\-]{20,}", RegexOptions.IgnoreCase)),
        ("Bearer Token",         new Regex(@"Bearer\s+[A-Za-z0-9\-_.~+/]+=*", RegexOptions.IgnoreCase)),
        ("Private Key Block",    new Regex(@"-----BEGIN (?:RSA |EC |OPENSSH |DSA |PGP )?PRIVATE KEY-----", RegexOptions.IgnoreCase)),
        ("Encrypted Block",      new Regex(@"-----BEGIN ENCRYPTED PRIVATE KEY-----", RegexOptions.IgnoreCase)),
        ("AWS Access Key",       new Regex(@"AKIA[0-9A-Z]{16}")),
        ("GitHub Token",         new Regex(@"gh[pousr]_[A-Za-z0-9_]{36,}")),
        ("Password Assignment",  new Regex(@"(?:password|passwd|pwd)\s*[=:]\s*[""']?\S{6,}", RegexOptions.IgnoreCase)),
        ("Secret Assignment",    new Regex(@"(?:secret|client_secret)\s*[=:]\s*[""']?\S{8,}", RegexOptions.IgnoreCase)),
        ("JWT Token",            new Regex(@"eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}")),
        ("Connection String",    new Regex(@"(?:password|pwd)=[^;]{3,}", RegexOptions.IgnoreCase)),
        ("Slack Token",          new Regex(@"xox[bpaso]-[0-9A-Za-z\-]+")),
        ("Google API Key",       new Regex(@"AIza[0-9A-Za-z\-_]{35}")),
        ("Stripe Key",           new Regex(@"(?:sk|pk)_(?:live|test)_[0-9A-Za-z]{20,}")),
        ("npm Token",            new Regex(@"npm_[A-Za-z0-9]{36}")),
        ("Azure Storage Key",    new Regex(@"AccountKey=[A-Za-z0-9+/=]{60,}")),
    ];

    public static bool IsSensitive(string? content, out string? matchedRule)
    {
        matchedRule = null;
        if (string.IsNullOrEmpty(content)) return false;

        foreach (var (name, pattern) in Rules)
        {
            if (pattern.IsMatch(content))
            {
                matchedRule = name;
                return true;
            }
        }
        return false;
    }
}

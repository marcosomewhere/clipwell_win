using System.Diagnostics;

namespace ClipwellWin.Services;

/// <summary>Opens web links from stored content in the default browser, never anything else.</summary>
public static class WebLink
{
    /// <summary>
    /// The normalized http(s) address of <paramref name="text"/>, or null. The shell only ever gets the escaped
    /// <see cref="Uri.AbsoluteUri"/>, so quotes or spaces in copied text cannot become extra browser arguments.
    /// </summary>
    public static string? Normalize(string? text)
    {
        if (!UrlPreviewService.IsUrl(text)) return null;
        if (!Uri.TryCreate(text!.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host)) return null;
        return uri.AbsoluteUri;
    }

    public static bool TryOpen(Uri uri) => TryOpen(uri.AbsoluteUri);

    public static bool TryOpen(string? text)
    {
        var target = Normalize(text);
        if (target == null) return false;
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            App.LogCrash(ex);
            return false;
        }
    }
}

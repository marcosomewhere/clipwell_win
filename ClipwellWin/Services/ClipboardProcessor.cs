using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using ClipwellWin.Models;

namespace ClipwellWin.Services;

// Must be called on the UI thread (Clipboard API requires it).
public static class ClipboardProcessor
{
    private static readonly Regex HexColorRx = new(@"#(?:[0-9A-Fa-f]{3,4}){1,2}\b");

    public static ClipboardEntry? BuildEntry(
        CodeDetectionMode codeDetectionMode = CodeDetectionMode.Normal,
        bool filterSensitive = true)
    {
        try
        {
            var entry = BuildEntry(System.Windows.Clipboard.GetDataObject(), codeDetectionMode);
            if (entry == null) return null;

            if (filterSensitive && entry.Content != null
                && SensitiveContentService.IsSensitive(entry.Content, out _))
                return null;

            return entry;
        }
        catch { return null; }
    }

    public static ClipboardEntry? BuildEntry(System.Windows.IDataObject? data, CodeDetectionMode codeDetectionMode = CodeDetectionMode.Normal)
    {
        try
        {
            if (data == null) return null;

            // Image
            if (data.GetDataPresent(System.Windows.DataFormats.Bitmap))
            {
                var src = data.GetData(System.Windows.DataFormats.Bitmap) as BitmapSource;
                if (src == null) return null;
                var bytes = OcrService.BitmapSourceToBytes(src);
                if (bytes == null) return null;
                var imageKind = ContentKindService.DetectImageKind(bytes);
                return new ClipboardEntry
                {
                    Type = EntryType.Image,
                    ImageData = bytes,
                    ContentKind = imageKind,
                    DetectionReason = $"Clipboard enthaelt Bilddaten ({imageKind}).",
                    Timestamp = DateTime.Now,
                };
            }

            // Text / HTML / RTF
            string? text = null;
            if (data.GetDataPresent(System.Windows.DataFormats.UnicodeText))
                text = data.GetData(System.Windows.DataFormats.UnicodeText) as string;
            else if (data.GetDataPresent(System.Windows.DataFormats.Text))
                text = data.GetData(System.Windows.DataFormats.Text) as string;
            else if (data.GetDataPresent(System.Windows.DataFormats.Html))
                text = StripHtml(data.GetData(System.Windows.DataFormats.Html) as string);
            else if (data.GetDataPresent(System.Windows.DataFormats.Rtf))
                text = data.GetData(System.Windows.DataFormats.Rtf) as string;

            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Trim();

            // Determine type
            EntryType type = EntryType.Text;
            string? language = null;
            string? hexColor = null;
            string? contentKind = null;
            string? codeReason = null;

            if (UrlPreviewService.IsUrl(text))
            {
                type = EntryType.Url;
                language = null;
                hexColor = null;
                contentKind = ContentKindService.DetectUrlKind(text);
            }
            else
            {
                var analysis = SyntaxService.Analyze(text, codeDetectionMode);
                language = analysis.language;
                codeReason = analysis.reason;
                if (language != null) type = EntryType.Code;
                contentKind = ContentKindService.DetectTextKind(text, language);
                if (language == null && contentKind is "TOML" or "INI" or "PROPS" or "ENV" or "DOCKER" or "CONFIG")
                    type = EntryType.Code;

                var hexMatch = HexColorRx.Match(text);
                if (hexMatch.Success)
                {
                    hexColor = hexMatch.Value;
                    if (type == EntryType.Text) type = EntryType.Color;
                }
            }

            var reason = type switch
            {
                EntryType.Url => "URL erkannt: beginnt mit http:// oder https:// und ist einzeilig.",
                EntryType.Code when language == null && contentKind != null => $"Als {contentKind} erkannt: typische Konfigurations- oder Dateistruktur.",
                EntryType.Code => codeReason ?? "",
                EntryType.Color => $"Farbwert erkannt: {hexColor}.",
                _ => "Als Text behandelt: keine URL-, Farb- oder Code-Regel passte.",
            };

            return new ClipboardEntry
            {
                Type = type,
                Content = text,
                Language = language,
                HexColor = hexColor,
                ContentKind = contentKind,
                DetectionReason = reason,
                Timestamp = DateTime.Now,
            };
        }
        catch { return null; }
    }

    public static string GetPlainText(ClipboardEntry entry)
    {
        if (entry.Type == EntryType.Image)
            return entry.OcrText ?? "";
        return entry.Content ?? "";
    }

    private static string? StripHtml(string? html)
    {
        if (html == null) return null;
        // Remove HTML clipboard format header
        var bodyIdx = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        if (bodyIdx >= 0) html = html[bodyIdx..];
        return Regex.Replace(html, "<[^>]*>", "").Trim();
    }
}

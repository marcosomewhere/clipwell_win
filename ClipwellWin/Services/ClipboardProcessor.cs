using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using ClipwellWin.Models;

namespace ClipwellWin.Services;

// Must be called on the UI thread (Clipboard API requires it).
public static class ClipboardProcessor
{
    private static readonly Regex HexColorRx = new(@"#(?:[0-9A-Fa-f]{3,4}){1,2}\b");

    public static ClipboardEntry? BuildEntry(CodeDetectionMode codeDetectionMode = CodeDetectionMode.Normal)
    {
        try
        {
            return BuildEntry(System.Windows.Clipboard.GetDataObject(), codeDetectionMode);
        }
        catch { return null; }
    }

    public static ClipboardEntry? BuildEntry(System.Windows.IDataObject? data, CodeDetectionMode codeDetectionMode = CodeDetectionMode.Normal)
    {
        try
        {
            if (data == null) return null;

            var imageBytes = ExtractImageBytes(data);
            if (imageBytes != null)
            {
                var imageKind = ContentKindService.DetectImageKind(imageBytes);
                return new ClipboardEntry
                {
                    Type = EntryType.Image,
                    ImageData = imageBytes,
                    ContentKind = imageKind,
                    DetectionReason = $"Clipboard enthaelt Bilddaten ({imageKind}).",
                    Timestamp = DateTime.Now,
                };
            }

            string? text = null;
            if (data.GetDataPresent(System.Windows.DataFormats.UnicodeText))
                text = data.GetData(System.Windows.DataFormats.UnicodeText) as string;
            else if (data.GetDataPresent(System.Windows.DataFormats.Text))
                text = data.GetData(System.Windows.DataFormats.Text) as string;
            else if (data.GetDataPresent(System.Windows.DataFormats.Html))
                text = StripHtml(data.GetData(System.Windows.DataFormats.Html) as string);
            else if (data.GetDataPresent(System.Windows.DataFormats.Rtf))
                text = StripRtf(data.GetData(System.Windows.DataFormats.Rtf) as string);

            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Trim();

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
                if (language != null || contentKind is "CODE" or "YAML")
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
                EntryType.Code when language == null && contentKind != null => "Als Code erkannt: typische Konfigurations- oder Dateistruktur.",
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

    private static byte[]? ExtractImageBytes(System.Windows.IDataObject data)
    {
        foreach (var format in new[] { "PNG", "Portable Network Graphics", "image/png" })
        {
            if (!data.GetDataPresent(format, autoConvert: false)) continue;
            var bytes = TryGetBytes(data.GetData(format, autoConvert: false));
            if (IsPng(bytes)) return bytes;
        }

        if (data.GetDataPresent(System.Windows.DataFormats.Dib, autoConvert: false))
        {
            var dibBytes = TryGetBytes(data.GetData(System.Windows.DataFormats.Dib, autoConvert: false));
            var pngBytes = DecodeDibToPng(dibBytes);
            if (pngBytes != null) return pngBytes;
        }

        if (data.GetDataPresent(System.Windows.DataFormats.Bitmap))
        {
            var src = data.GetData(System.Windows.DataFormats.Bitmap) as BitmapSource;
            if (src != null) return OcrService.BitmapSourceToBytes(src);
        }

        return null;
    }

    private static byte[]? DecodeDibToPng(byte[]? dibBytes)
    {
        if (dibBytes is not { Length: > 40 }) return null;

        try
        {
            var bmpBytes = BuildBmpBytesFromDib(dibBytes);
            if (bmpBytes == null) return null;

            using var input = new MemoryStream(bmpBytes);
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return null;
            return OcrService.BitmapSourceToBytes(decoder.Frames[0]);
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? BuildBmpBytesFromDib(byte[] dibBytes)
    {
        var headerSize = BitConverter.ToInt32(dibBytes, 0);
        if (headerSize <= 0 || headerSize > dibBytes.Length) return null;

        var bitsPerPixel = headerSize == 12
            ? BitConverter.ToUInt16(dibBytes, 10)
            : BitConverter.ToUInt16(dibBytes, 14);
        var compression = headerSize >= 40 && dibBytes.Length >= 20
            ? BitConverter.ToInt32(dibBytes, 16)
            : 0;
        var colorsUsed = headerSize >= 40 && dibBytes.Length >= 36
            ? BitConverter.ToInt32(dibBytes, 32)
            : 0;

        var paletteEntrySize = headerSize == 12 ? 3 : 4;
        var paletteColors = colorsUsed > 0 ? colorsUsed : bitsPerPixel <= 8 ? 1 << bitsPerPixel : 0;
        var extraMasks = headerSize == 40 && (compression == 3 || compression == 6)
            ? compression == 6 ? 16 : 12
            : 0;
        var pixelOffset = 14 + headerSize + extraMasks + paletteColors * paletteEntrySize;
        var fileSize = 14 + dibBytes.Length;
        if (pixelOffset > fileSize) return null;

        using var output = new MemoryStream(fileSize);
        using var writer = new BinaryWriter(output);
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(fileSize);
        writer.Write(0);
        writer.Write(pixelOffset);
        writer.Write(dibBytes);
        return output.ToArray();
    }

    private static byte[]? TryGetBytes(object? data)
    {
        switch (data)
        {
            case byte[] bytes:
                return bytes;
            case MemoryStream ms:
                return ms.ToArray();
            case Stream stream:
                using (var copy = new MemoryStream())
                {
                    if (stream.CanSeek) stream.Position = 0;
                    stream.CopyTo(copy);
                    return copy.ToArray();
                }
            default:
                return null;
        }
    }

    private static bool IsPng(byte[]? bytes)
        => bytes is { Length: >= 8 }
           && bytes[0] == 0x89
           && bytes[1] == 0x50
           && bytes[2] == 0x4E
           && bytes[3] == 0x47
           && bytes[4] == 0x0D
           && bytes[5] == 0x0A
           && bytes[6] == 0x1A
           && bytes[7] == 0x0A;

    private static readonly Regex ScriptStyleRx = new(
        @"<(script|style)\b[^>]*>[\s\S]*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HtmlTagRx = new(@"<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex MultiSpaceRx = new(@"\s{2,}", RegexOptions.Compiled);

    private static string? StripHtml(string? html)
    {
        if (html == null) return null;
        html = ExtractWindowsHtmlFragment(html);
        var bodyIdx = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        if (bodyIdx >= 0) html = html[bodyIdx..];
        html = ScriptStyleRx.Replace(html, " ");
        html = HtmlTagRx.Replace(html, " ");
        return MultiSpaceRx.Replace(html, " ").Trim();
    }

    private static string ExtractWindowsHtmlFragment(string html)
    {
        if (!html.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
            return html;

        var start = ReadHtmlOffset(html, "StartFragment");
        var end = ReadHtmlOffset(html, "EndFragment");
        if (start < 0 || end <= start)
        {
            start = ReadHtmlOffset(html, "StartHTML");
            end = ReadHtmlOffset(html, "EndHTML");
        }

        if (start < 0 || end <= start)
            return html;

        var bytes = Encoding.UTF8.GetBytes(html);
        if (start >= bytes.Length)
            return html;

        end = Math.Min(end, bytes.Length);
        return Encoding.UTF8.GetString(bytes, start, end - start);
    }

    private static int ReadHtmlOffset(string html, string name)
    {
        var match = Regex.Match(html, $@"^{Regex.Escape(name)}:(\d+)\s*$", RegexOptions.Multiline);
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : -1;
    }

    private static string? StripRtf(string? rtf)
    {
        if (string.IsNullOrWhiteSpace(rtf)) return null;
        try
        {
            var text = StripRtfPlainText(rtf);
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string StripRtfPlainText(string rtf)
    {
        var output = new StringBuilder(rtf.Length);
        var skipStack = new Stack<bool>();
        var skipGroup = false;
        var unicodeFallbackSkip = 1;
        var skipChars = 0;

        for (var i = 0; i < rtf.Length; i++)
        {
            var ch = rtf[i];

            if (skipChars > 0)
            {
                skipChars--;
                continue;
            }

            if (ch == '{')
            {
                skipStack.Push(skipGroup);
                continue;
            }

            if (ch == '}')
            {
                skipGroup = skipStack.Count > 0 && skipStack.Pop();
                continue;
            }

            if (ch == '\r' || ch == '\n')
                continue;

            if (ch != '\\')
            {
                if (!skipGroup) output.Append(ch);
                continue;
            }

            if (++i >= rtf.Length) break;
            ch = rtf[i];

            if (ch is '\\' or '{' or '}')
            {
                if (!skipGroup) output.Append(ch);
                continue;
            }

            if (ch == '~')
            {
                if (!skipGroup) output.Append(' ');
                continue;
            }

            if (ch == '*')
            {
                skipGroup = true;
                continue;
            }

            if (ch == '\'')
            {
                if (i + 2 < rtf.Length
                    && byte.TryParse(rtf.AsSpan(i + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out var value))
                {
                    if (!skipGroup) output.Append((char)value);
                    i += 2;
                }
                continue;
            }

            if (!char.IsLetter(ch))
            {
                if (!skipGroup) output.Append(ch);
                continue;
            }

            var wordStart = i;
            while (i < rtf.Length && char.IsLetter(rtf[i]))
                i++;
            var word = rtf[wordStart..i];

            var sign = 1;
            if (i < rtf.Length && rtf[i] == '-')
            {
                sign = -1;
                i++;
            }

            var numberStart = i;
            while (i < rtf.Length && char.IsDigit(rtf[i]))
                i++;
            var hasNumber = i > numberStart;
            var number = hasNumber && int.TryParse(rtf[numberStart..i], out var parsed)
                ? sign * parsed
                : 0;

            if (i < rtf.Length && rtf[i] != ' ')
                i--;

            if (IsRtfDestination(word))
            {
                skipGroup = true;
                continue;
            }

            if (skipGroup) continue;

            switch (word)
            {
                case "par":
                case "line":
                    output.AppendLine();
                    break;
                case "tab":
                    output.Append('\t');
                    break;
                case "uc" when hasNumber:
                    unicodeFallbackSkip = Math.Max(0, number);
                    break;
                case "u" when hasNumber:
                    output.Append(char.ConvertFromUtf32(number < 0 ? number + 65536 : number));
                    skipChars = unicodeFallbackSkip;
                    break;
            }
        }

        return output.ToString();
    }

    private static bool IsRtfDestination(string word)
        => word is "fonttbl" or "colortbl" or "datastore" or "stylesheet" or "info"
            or "pict" or "object" or "header" or "footer" or "footnote" or "annotation";
}

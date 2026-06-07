using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using WpfBitmapDecoder = System.Windows.Media.Imaging.BitmapDecoder;
using WpfBitmapFrame = System.Windows.Media.Imaging.BitmapFrame;
using WinBitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;

namespace ClipwellWin.Services;

public static class OcrService
{
    private static OcrEngine? _engine;
    private static readonly object _engineLock = new();

    public static bool IsAvailable()
    {
        if (_engine != null) return true;
        lock (_engineLock) { _engine ??= OcrEngine.TryCreateFromUserProfileLanguages(); }
        return _engine != null;
    }

    public static async Task<string?> RecognizeAsync(byte[] pngBytes)
    {
        OcrEngine? engine = _engine;
        if (engine == null)
        {
            lock (_engineLock)
            {
                _engine ??= OcrEngine.TryCreateFromUserProfileLanguages();
                engine = _engine;
            }
        }
        if (engine == null || pngBytes.Length == 0) return null;

        try
        {
            using var ras = new InMemoryRandomAccessStream();
            using var writer = new DataWriter(ras.GetOutputStreamAt(0));
            writer.WriteBytes(pngBytes);
            await writer.StoreAsync();
            ras.Seek(0);

            var decoder = await WinBitmapDecoder.CreateAsync(ras);
            var softBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            var result = await engine.RecognizeAsync(softBitmap);
            return result.Text;
        }
        catch { return null; }
    }

    private const int MaxImageSizeBytes = 2 * 1024 * 1024;

    public static byte[]? BitmapSourceToBytes(BitmapSource src)
    {
        try
        {
            src = RepairFullyTransparentColorBitmap(src);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(WpfBitmapFrame.Create(src));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            var bytes = ms.ToArray();
            return bytes.Length <= MaxImageSizeBytes ? bytes : null;
        }
        catch { return null; }
    }

    private static BitmapSource RepairFullyTransparentColorBitmap(BitmapSource src)
    {
        if (src.PixelWidth <= 0 || src.PixelHeight <= 0) return src;

        var converted = src.Format == PixelFormats.Bgra32 || src.Format == PixelFormats.Pbgra32
            ? src
            : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        var hasColor = false;
        var hasVisibleAlpha = false;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0 || pixels[i + 1] != 0 || pixels[i + 2] != 0)
                hasColor = true;
            if (pixels[i + 3] != 0)
            {
                hasVisibleAlpha = true;
                break;
            }
        }

        if (!hasColor || hasVisibleAlpha) return src;

        for (var i = 3; i < pixels.Length; i += 4)
            pixels[i] = 255;

        var repaired = BitmapSource.Create(
            converted.PixelWidth,
            converted.PixelHeight,
            converted.DpiX,
            converted.DpiY,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        repaired.Freeze();
        return repaired;
    }
}

using System.IO;
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

    public static bool IsAvailable()
    {
        _engine ??= OcrEngine.TryCreateFromUserProfileLanguages();
        return _engine != null;
    }

    public static async Task<string?> RecognizeAsync(byte[] pngBytes)
    {
        _engine ??= OcrEngine.TryCreateFromUserProfileLanguages();
        if (_engine == null || pngBytes.Length == 0) return null;

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

            var result = await _engine.RecognizeAsync(softBitmap);
            return result.Text;
        }
        catch { return null; }
    }

    public static byte[]? BitmapSourceToBytes(BitmapSource src)
    {
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(WpfBitmapFrame.Create(src));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            var bytes = ms.ToArray();
            // Discard if > 2 MB
            return bytes.Length <= 2 * 1024 * 1024 ? bytes : null;
        }
        catch { return null; }
    }
}

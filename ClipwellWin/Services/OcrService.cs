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
            await writer.FlushAsync();
            writer.DetachStream();
            ras.Seek(0);

            var decoder = await WinBitmapDecoder.CreateAsync(ras);
            var (scaledWidth, scaledHeight) = CalculateOcrScaledSize(
                decoder.PixelWidth,
                decoder.PixelHeight,
                (uint)OcrEngine.MaxImageDimension);
            var transform = new BitmapTransform
            {
                ScaledWidth = scaledWidth,
                ScaledHeight = scaledHeight
            };
            using var softBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);

            var result = await engine.RecognizeAsync(softBitmap);
            return result.Text;
        }
        catch (Exception ex)
        {
            App.LogCrash(ex);
            return null;
        }
    }

    public static (uint Width, uint Height) CalculateOcrScaledSize(uint width, uint height, uint maxDimension)
    {
        if (width == 0 || height == 0 || maxDimension == 0) return (width, height);
        var longestSide = Math.Max(width, height);
        if (longestSide <= maxDimension) return (width, height);

        var scale = maxDimension / (double)longestSide;
        return (
            Math.Max(1, (uint)Math.Round(width * scale)),
            Math.Max(1, (uint)Math.Round(height * scale)));
    }

    private const int MaxImageSizeBytes = 2 * 1024 * 1024;

    public static byte[]? BitmapSourceToBytes(BitmapSource src)
    {
        try
        {
            src = ImageUtils.RepairFullyTransparentColorBitmap(src);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(WpfBitmapFrame.Create(src));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            var bytes = ms.ToArray();
            return bytes.Length <= MaxImageSizeBytes ? bytes : null;
        }
        catch { return null; }
    }

}

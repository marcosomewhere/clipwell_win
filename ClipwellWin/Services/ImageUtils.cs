using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClipwellWin.Services;

public static class ImageUtils
{
    // Composites transparent pixels onto a white background at pixel level.
    // CF_DIB (used by Clipboard.SetImage) carries no alpha channel — without this
    // pre-compositing, transparent areas appear black in email clients and documents.
    public static BitmapSource BlendOnWhite(BitmapSource src)
    {
        src = RepairFullyTransparentColorBitmap(src);
        var w = src.PixelWidth;
        var h = src.PixelHeight;

        // Convert to straight Bgra32 so alpha is readable/writable directly.
        var bgra = src.Format == PixelFormats.Bgra32
            ? src
            : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);

        var stride = w * 4;
        var px = new byte[stride * h];
        bgra.CopyPixels(px, stride, 0);

        for (var i = 0; i < px.Length; i += 4)
        {
            var a = px[i + 3];
            if (a == 255) continue;
            if (a == 0)
            {
                px[i] = px[i + 1] = px[i + 2] = 255;
            }
            else
            {
                var af = a / 255.0;
                px[i]     = (byte)(px[i]     * af + 255 * (1.0 - af));
                px[i + 1] = (byte)(px[i + 1] * af + 255 * (1.0 - af));
                px[i + 2] = (byte)(px[i + 2] * af + 255 * (1.0 - af));
            }
            px[i + 3] = 255;
        }

        var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, stride);
        result.Freeze();
        return result;
    }

    public static BitmapSource RepairFullyTransparentColorBitmap(BitmapSource src)
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

    public static System.Windows.DataObject CreateClipboardImageDataObject(BitmapSource src)
    {
        var opaque = BlendOnWhite(src);
        var bitmap = ToBgr24(opaque);
        var data = new System.Windows.DataObject();
        data.SetImage(bitmap);
        data.SetData(System.Windows.DataFormats.Dib, new MemoryStream(EncodeDib24(opaque)), autoConvert: false);
        data.SetData("PNG", new MemoryStream(EncodePng(opaque)), autoConvert: false);
        data.SetData("Portable Network Graphics", new MemoryStream(EncodePng(opaque)), autoConvert: false);
        data.SetData("image/png", new MemoryStream(EncodePng(opaque)), autoConvert: false);
        return data;
    }

    public static System.Drawing.Bitmap CreateOpaqueDrawingBitmap(BitmapSource src)
    {
        var bgra32 = BlendOnWhite(src);
        var width = bgra32.PixelWidth;
        var height = bgra32.PixelHeight;
        var sourceStride = checked(width * 4);
        var sourcePixels = new byte[checked(sourceStride * height)];
        bgra32.CopyPixels(sourcePixels, sourceStride, 0);

        using var staging = new System.Drawing.Bitmap(width, height);
        if (bgra32.DpiX > 0 && bgra32.DpiY > 0)
            staging.SetResolution((float)bgra32.DpiX, (float)bgra32.DpiY);

        var rect = new System.Drawing.Rectangle(0, 0, width, height);
        var data = staging.LockBits(
            rect,
            System.Drawing.Imaging.ImageLockMode.WriteOnly,
            staging.PixelFormat);
        try
        {
            for (var y = 0; y < height; y++)
            {
                var sourceOffset = y * sourceStride;
                var target = data.Scan0 + y * data.Stride;
                System.Runtime.InteropServices.Marshal.Copy(sourcePixels, sourceOffset, target, sourceStride);
            }
        }
        finally
        {
            staging.UnlockBits(data);
        }

        var bitmap = new System.Drawing.Bitmap(width, height);
        if (bgra32.DpiX > 0 && bgra32.DpiY > 0)
            bitmap.SetResolution((float)bgra32.DpiX, (float)bgra32.DpiY);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.DrawImageUnscaled(staging, 0, 0);
        }

        return bitmap;
    }

    internal static byte[] EncodeDib24(BitmapSource src)
    {
        var bgr24 = ToBgr24(src);

        var width = bgr24.PixelWidth;
        var height = bgr24.PixelHeight;
        var sourceStride = checked((width * 24 + 7) / 8);
        var sourcePixels = new byte[checked(sourceStride * height)];
        bgr24.CopyPixels(sourcePixels, sourceStride, 0);

        var dibStride = checked(((width * 24 + 31) / 32) * 4);
        var imageSize = checked(dibStride * height);
        var bytes = new byte[checked(40 + imageSize)];

        WriteInt32(bytes, 0, 40);
        WriteInt32(bytes, 4, width);
        WriteInt32(bytes, 8, height);
        WriteUInt16(bytes, 12, 1);
        WriteUInt16(bytes, 14, 24);
        WriteInt32(bytes, 16, 0);
        WriteInt32(bytes, 20, imageSize);
        WriteInt32(bytes, 24, (int)Math.Round(bgr24.DpiX * 39.37007874015748));
        WriteInt32(bytes, 28, (int)Math.Round(bgr24.DpiY * 39.37007874015748));

        for (var y = 0; y < height; y++)
        {
            var sourceOffset = y * sourceStride;
            var targetOffset = 40 + (height - 1 - y) * dibStride;
            Buffer.BlockCopy(sourcePixels, sourceOffset, bytes, targetOffset, sourceStride);
        }

        return bytes;
    }

    private static BitmapSource ToBgr24(BitmapSource src)
    {
        if (src.Format == PixelFormats.Bgr24) return src;

        var converted = new FormatConvertedBitmap(src, PixelFormats.Bgr24, null, 0);
        converted.Freeze();
        return converted;
    }

    private static byte[] EncodePng(BitmapSource src)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(src));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void WriteInt32(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteUInt16(byte[] bytes, int offset, ushort value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
    }
}

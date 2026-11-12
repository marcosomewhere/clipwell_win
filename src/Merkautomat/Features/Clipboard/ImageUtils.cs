using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Merkautomat.Services;

public static class ImageUtils
{
    /// <summary>Header bytes needed by <see cref="TryReadImageSize"/>.</summary>
    public const int ImageHeaderBytes = 32;

    /// <summary>Pixel size from a PNG, GIF or BMP header without decoding the image.</summary>
    public static bool TryReadImageSize(ReadOnlySpan<byte> head, out int width, out int height)
    {
        width = height = 0;
        if (head.Length >= 24 && head[0] == 0x89 && head[1] == (byte)'P' && head[2] == (byte)'N' && head[3] == (byte)'G')
        {
            width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(head[16..]);
            height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(head[20..]);
        }
        else if (head.Length >= 10 && head[0] == (byte)'G' && head[1] == (byte)'I' && head[2] == (byte)'F')
        {
            width = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(head[6..]);
            height = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(head[8..]);
        }
        else if (head.Length >= 26 && head[0] == (byte)'B' && head[1] == (byte)'M')
        {
            width = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(head[18..]);
            height = Math.Abs(System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(head[22..]));
        }
        if (width > 0 && height > 0 && width <= 100_000 && height <= 100_000) return true;
        width = height = 0;
        return false;
    }

    // OnLoad plus Freeze releases the stream and makes the bitmap usable across threads.
    public static BitmapImage? LoadBitmap(byte[] data, int decodePixelWidth = 0)
    {
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.StreamSource = new MemoryStream(data);
            if (decodePixelWidth > 0) img.DecodePixelWidth = decodePixelWidth;
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch (Exception ex) when (IsDecodeFailure(ex)) { return null; }
        catch (Exception ex)
        {
            App.LogError("Bild laden", ex);
            return null;
        }
    }

    /// <summary>
    /// Failures WPF raises for damaged, truncated or unsupported image data. They are expected for clipboard and
    /// dropped content and are not logged; anything else is.
    /// </summary>
    public static bool IsDecodeFailure(Exception ex)
        => ex is NotSupportedException or IOException or ArgumentException or OverflowException
            or System.Runtime.InteropServices.COMException;

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
}

using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipwellWin.Services;
using Xunit;

namespace ClipwellWin.Tests;

public class ImageUtilsTests
{
    // Helper: creates a 1×1 Bgra32 BitmapSource with given B,G,R,A values.
    private static BitmapSource Pixel(byte b, byte g, byte r, byte a)
        => BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null,
               new byte[] { b, g, r, a }, 4);

    // Helper: reads B,G,R,A from a 1×1 Bgra32 BitmapSource.
    private static (byte B, byte G, byte R, byte A) ReadPixel(BitmapSource src)
    {
        var bgra = src.Format == PixelFormats.Bgra32
            ? src
            : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        var px = new byte[4];
        bgra.CopyPixels(px, 4, 0);
        return (px[0], px[1], px[2], px[3]);
    }

    [Fact]
    public void BlendOnWhite_FullyOpaque_PixelUnchanged()
    {
        StaTestRunner.Run(() =>
        {
            var src = Pixel(10, 20, 30, 255);
            var result = ImageUtils.BlendOnWhite(src);
            var (b, g, r, a) = ReadPixel(result);

            Assert.Equal(10, b);
            Assert.Equal(20, g);
            Assert.Equal(30, r);
            Assert.Equal(255, a);
        });
    }

    [Fact]
    public void BlendOnWhite_FullyTransparent_BecomesWhite()
    {
        StaTestRunner.Run(() =>
        {
            var src = Pixel(0, 0, 0, 0);
            var result = ImageUtils.BlendOnWhite(src);
            var (b, g, r, a) = ReadPixel(result);

            Assert.Equal(255, b);
            Assert.Equal(255, g);
            Assert.Equal(255, r);
            Assert.Equal(255, a);
        });
    }

    [Fact]
    public void BlendOnWhite_FullyTransparentWithColor_RepairsHiddenColor()
    {
        StaTestRunner.Run(() =>
        {
            // Some apps write color data into fully-transparent pixels
            var src = Pixel(100, 50, 200, 0);
            var result = ImageUtils.BlendOnWhite(src);
            var (b, g, r, a) = ReadPixel(result);

            Assert.Equal(100, b);
            Assert.Equal(50, g);
            Assert.Equal(200, r);
            Assert.Equal(255, a);
        });
    }

    [Fact]
    public void BlendOnWhite_HalfTransparent_BlendsCorrectly()
    {
        StaTestRunner.Run(() =>
        {
            // B=0, G=0, R=0, A=128 (≈50%) — blended with white should be ~127
            var src = Pixel(0, 0, 0, 128);
            var result = ImageUtils.BlendOnWhite(src);
            var (b, g, r, a) = ReadPixel(result);

            // af = 128/255 ≈ 0.502 → output = 0 * 0.502 + 255 * 0.498 ≈ 127
            Assert.InRange(b, 126, 128);
            Assert.InRange(g, 126, 128);
            Assert.InRange(r, 126, 128);
            Assert.Equal(255, a);
        });
    }

    [Fact]
    public void BlendOnWhite_OutputIsAlwaysFullyOpaque()
    {
        StaTestRunner.Run(() =>
        {
            // All alpha values must produce alpha=255 in the output
            foreach (var alpha in new byte[] { 0, 1, 64, 128, 200, 254, 255 })
            {
                var src = Pixel(100, 100, 100, alpha);
                var result = ImageUtils.BlendOnWhite(src);
                var (_, _, _, a) = ReadPixel(result);
                Assert.Equal(255, a);
            }
        });
    }

    [Fact]
    public void BlendOnWhite_NonAlphaFormat_PixelUnchanged()
    {
        StaTestRunner.Run(() =>
        {
            // Bgr32 has no alpha channel; pixels should be preserved as-is
            var src = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgr32, null,
                new byte[] { 10, 20, 30, 0 }, 4);
            var result = ImageUtils.BlendOnWhite(src);
            var (b, g, r, a) = ReadPixel(result);

            Assert.Equal(10, b);
            Assert.Equal(20, g);
            Assert.Equal(30, r);
            Assert.Equal(255, a);
        });
    }

    [Fact]
    public void BlendOnWhite_PreservesImageDimensions()
    {
        StaTestRunner.Run(() =>
        {
            var src = BitmapSource.Create(8, 4, 96, 96, PixelFormats.Bgra32, null,
                new byte[8 * 4 * 4], 8 * 4);
            var result = ImageUtils.BlendOnWhite(src);

            Assert.Equal(8, result.PixelWidth);
            Assert.Equal(4, result.PixelHeight);
        });
    }

    [Fact]
    public void BlendOnWhite_ResultIsFrozen()
    {
        StaTestRunner.Run(() =>
        {
            var src = Pixel(255, 0, 0, 128);
            var result = ImageUtils.BlendOnWhite(src);
            Assert.True(result.IsFrozen);
        });
    }

    // Verifies the BMP encoding path used by SetClipboardImageViaGdi:
    // output must be 24-bit (no alpha channel) and transparent pixels must be white.
    [Fact]
    public void BlendOnWhite_BmpEncoding_Is24Bit_AndTransparentBecomesWhite()
    {
        StaTestRunner.Run(() =>
        {
            // 2×1 image in BGRA: pixel 0 = transparent black, pixel 1 = opaque red
            var src = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null,
                new byte[] { 0, 0, 0, 0,   0, 0, 255, 255 }, 8);

            var blended = ImageUtils.BlendOnWhite(src);

            var bgr24 = new FormatConvertedBitmap(blended, PixelFormats.Bgr24, null, 0);
            var enc = new System.Windows.Media.Imaging.BmpBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bgr24));
            using var ms = new System.IO.MemoryStream();
            enc.Save(ms);
            var bmp = ms.ToArray();

            // BMP file signature
            Assert.Equal(0x42, bmp[0]); // 'B'
            Assert.Equal(0x4D, bmp[1]); // 'M'

            // Bit count at header offset 28 (BITMAPINFOHEADER.biBitCount, little-endian)
            var bitCount = BitConverter.ToUInt16(bmp, 28);
            Assert.Equal(24, bitCount);

            // Pixel data offset stored at BMP header bytes 10-13
            var pixelOffset = BitConverter.ToInt32(bmp, 10);

            // BMP stores rows bottom-up; 1-row image has exactly one row.
            // 24bpp, width=2: 6 bytes data, padded to 8 (stride = ⌈(2×3)/4⌉×4).
            // Pixel 0 (was transparent): must be white (B=255,G=255,R=255 in BGR order)
            Assert.Equal(255, bmp[pixelOffset + 0]); // B
            Assert.Equal(255, bmp[pixelOffset + 1]); // G
            Assert.Equal(255, bmp[pixelOffset + 2]); // R

            // Pixel 1 (was opaque red = BGRA 0,0,255,255): must still be red
            Assert.Equal(0,   bmp[pixelOffset + 3]); // B
            Assert.Equal(0,   bmp[pixelOffset + 4]); // G
            Assert.Equal(255, bmp[pixelOffset + 5]); // R
        });
    }

    [Fact]
    public void CreateClipboardImageDataObject_DibIs24Bit_AndTransparentBecomesWhite()
    {
        StaTestRunner.Run(() =>
        {
            var src = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null,
                new byte[] { 0, 0, 0, 0, 0, 0, 255, 255 }, 8);

            var data = ImageUtils.CreateClipboardImageDataObject(src);
            var stream = Assert.IsType<System.IO.MemoryStream>(
                data.GetData(System.Windows.DataFormats.Dib, autoConvert: false));
            var dib = stream.ToArray();

            Assert.Equal(40, BitConverter.ToInt32(dib, 0));
            Assert.Equal(2, BitConverter.ToInt32(dib, 4));
            Assert.Equal(1, BitConverter.ToInt32(dib, 8));
            Assert.Equal(24, BitConverter.ToUInt16(dib, 14));

            Assert.Equal(255, dib[40]);
            Assert.Equal(255, dib[41]);
            Assert.Equal(255, dib[42]);
            Assert.Equal(0, dib[43]);
            Assert.Equal(0, dib[44]);
            Assert.Equal(255, dib[45]);
        });
    }

    [Fact]
    public void CreateClipboardImageDataObject_ProvidesOpaqueBitmapDibAndPng()
    {
        StaTestRunner.Run(() =>
        {
            var data = ImageUtils.CreateClipboardImageDataObject(Pixel(0, 0, 0, 0));

            Assert.True(data.GetDataPresent(System.Windows.DataFormats.Bitmap, autoConvert: false));
            Assert.True(data.GetDataPresent(System.Windows.DataFormats.Dib, autoConvert: false));
            Assert.True(data.GetDataPresent("PNG", autoConvert: false));
            var bitmap = Assert.IsAssignableFrom<BitmapSource>(
                data.GetData(System.Windows.DataFormats.Bitmap, autoConvert: false));
            Assert.Equal(PixelFormats.Bgr24, bitmap.Format);
        });
    }

    [Fact]
    public void CreateOpaqueDrawingBitmap_IsOpaque32Bit_AndTransparentBecomesWhite()
    {
        StaTestRunner.Run(() =>
        {
            var src = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null,
                new byte[] { 0, 0, 0, 0, 0, 0, 255, 255 }, 8);

            using var bitmap = ImageUtils.CreateOpaqueDrawingBitmap(src);

            Assert.Contains(bitmap.PixelFormat, new[]
            {
                System.Drawing.Imaging.PixelFormat.Format32bppArgb,
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb,
            });
            var left = bitmap.GetPixel(0, 0);
            var right = bitmap.GetPixel(1, 0);
            Assert.Equal(System.Drawing.Color.White.ToArgb(), left.ToArgb());
            Assert.Equal(System.Drawing.Color.Red.ToArgb(), right.ToArgb());
            Assert.Equal(255, left.A);
            Assert.Equal(255, right.A);
        });
    }

    [Fact]
    public void CreateOpaqueDrawingBitmap_RepairsFullyTransparentColoredImage()
    {
        StaTestRunner.Run(() =>
        {
            var src = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null,
                new byte[] { 255, 0, 0, 0, 0, 0, 255, 0 }, 8);

            using var bitmap = ImageUtils.CreateOpaqueDrawingBitmap(src);

            var left = bitmap.GetPixel(0, 0);
            var right = bitmap.GetPixel(1, 0);
            Assert.Equal(System.Drawing.Color.Blue.ToArgb(), left.ToArgb());
            Assert.Equal(System.Drawing.Color.Red.ToArgb(), right.ToArgb());
            Assert.Equal(255, left.A);
            Assert.Equal(255, right.A);
        });
    }
}

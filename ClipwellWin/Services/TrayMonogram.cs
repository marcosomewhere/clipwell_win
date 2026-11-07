using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ClipwellWin.Services;

internal static class TrayMonogram
{
    internal static Bitmap Render(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.ScaleTransform(size / 64f, size / 64f);

        using var letters = new GraphicsPath();
        using var font = new FontFamily("Arial");
        letters.AddString("MA", font, (int)FontStyle.Bold, 48, PointF.Empty, StringFormat.GenericTypographic);
        var bounds = letters.GetBounds();
        using var transform = new Matrix(59 / bounds.Width, 0, 0, 45 / bounds.Height,
            2.5f - bounds.X * 59 / bounds.Width, 9.5f - bounds.Y * 45 / bounds.Height);
        letters.Transform(transform);
        using var coat = new LinearGradientBrush(new RectangleF(0, 9, 64, 46), Color.White, Color.Black, 90);
        coat.InterpolationColors = new ColorBlend
        {
            Positions = [0f, .48f, .65f, 1f],
            Colors = [Color.FromArgb(192, 184, 168), Color.FromArgb(157, 151, 139),
                Color.FromArgb(220, 132, 36), Color.FromArgb(239, 128, 9)],
        };
        g.FillPath(coat, letters);

        // Fixed seed keeps the mottled coat stable across icon sizes and restarts.
        var state = g.Save();
        g.SetClip(letters);
        var random = new System.Random(731);
        using var dark = new SolidBrush(Color.FromArgb(210, 39, 32, 24));
        using var light = new SolidBrush(Color.FromArgb(175, 238, 226, 202));
        using var spots = new Pen(Color.FromArgb(225, 43, 35, 25), 1.25f)
            { StartCap = LineCap.Round, EndCap = LineCap.Round };
        for (var row = 0; row < 9; row++)
            for (var column = 0; column < 12; column++)
            {
                var x = column * 5.5f + (float)random.NextDouble() * 3;
                var y = 8 + row * 5.5f + (float)random.NextDouble() * 2;
                var width = 2.1f + (float)random.NextDouble() * 2.4f;
                g.DrawArc(spots, x, y, width, width * .8f, random.Next(360), 220 + random.Next(90));
            }
        for (var i = 0; i < 230; i++)
        {
            var x = (float)random.NextDouble() * 64;
            var y = 9 + (float)random.NextDouble() * 46;
            var radius = .45f + (float)random.NextDouble() * 1.2f;
            g.FillEllipse(i % 3 == 0 ? light : dark, x, y, radius, radius * .65f);
        }
        g.Restore(state);
        return bitmap;
    }
}

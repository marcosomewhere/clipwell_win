using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;
using Pen = System.Windows.Media.Pen;
using FontFamily = System.Windows.Media.FontFamily;

namespace ClipwellWin.Views;

internal static class SevenSegment
{
    public static readonly Brush LitBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x24)));
    public static readonly Brush GhostBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0x8A, 0x24)));
    public static readonly Brush GlowBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0x7A, 0x10)));

    // Segment order: a (top), b (top right), c (bottom right), d (bottom), e (bottom left), f (top left), g (middle).
    private static readonly byte[] DigitMasks =
    [
        0b0111111, 0b0000110, 0b1011011, 0b1001111, 0b1100110,
        0b1101101, 0b1111101, 0b0000111, 0b1111111, 0b1101111,
    ];

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private static byte MaskFor(char c) => c switch
    {
        >= '0' and <= '9' => DigitMasks[c - '0'],
        '-' => 0b1000000,
        _ => 0,
    };

    /// <summary>Draws right-aligned text in seven-segment style. Unused positions show the unlit "8" like a real display.</summary>
    public static void Draw(DrawingContext dc, string text, Rect bounds, int minDigits, bool glow = true)
    {
        var digits = Math.Max(minDigits, text.Length);
        if (digits == 0 || bounds.Width <= 0 || bounds.Height <= 0) return;

        var h = bounds.Height;
        var w = Math.Min(h * 0.52, bounds.Width / (digits + (digits - 1) * 0.28));
        h = Math.Min(h, w / 0.52);
        var gap = w * 0.28;
        var total = digits * w + (digits - 1) * gap;
        var x = bounds.X + (bounds.Width - total) / 2;
        var y = bounds.Y + (bounds.Height - h) / 2;
        var t = Math.Max(1.5, w * 0.17);

        var padded = text.PadLeft(digits);
        for (var i = 0; i < digits; i++)
        {
            var mask = MaskFor(padded[i]);
            var cell = new Rect(x + i * (w + gap), y, w, h);
            DrawDigit(dc, cell, t, 0b1111111, GhostBrush, null);
            if (mask != 0) DrawDigit(dc, cell, t, mask, LitBrush, glow ? GlowBrush : null);
        }
    }

    private static void DrawDigit(DrawingContext dc, Rect r, double t, byte mask, Brush fill, Brush? glow)
    {
        var g = t * 0.14;
        var left = r.X + t / 2;
        var right = r.Right - t / 2;
        var top = r.Y + t / 2;
        var mid = r.Y + r.Height / 2;
        var bottom = r.Bottom - t / 2;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            if ((mask & 0b0000001) != 0) Horizontal(ctx, left + g, right - g, top, t);
            if ((mask & 0b0000010) != 0) Vertical(ctx, right, top + g, mid - g, t);
            if ((mask & 0b0000100) != 0) Vertical(ctx, right, mid + g, bottom - g, t);
            if ((mask & 0b0001000) != 0) Horizontal(ctx, left + g, right - g, bottom, t);
            if ((mask & 0b0010000) != 0) Vertical(ctx, left, mid + g, bottom - g, t);
            if ((mask & 0b0100000) != 0) Vertical(ctx, left, top + g, mid - g, t);
            if ((mask & 0b1000000) != 0) Horizontal(ctx, left + g, right - g, mid, t);
        }
        geo.Freeze();
        if (glow != null) dc.DrawGeometry(null, new Pen(glow, t * 0.9) { LineJoin = PenLineJoin.Round }, geo);
        dc.DrawGeometry(fill, null, geo);
    }

    private static void Horizontal(StreamGeometryContext ctx, double x1, double x2, double y, double t)
    {
        var h = t / 2;
        ctx.BeginFigure(new Point(x1, y), true, true);
        ctx.PolyLineTo([new(x1 + h, y - h), new(x2 - h, y - h), new(x2, y), new(x2 - h, y + h), new(x1 + h, y + h)], true, false);
    }

    private static void Vertical(StreamGeometryContext ctx, double x, double y1, double y2, double t)
    {
        var h = t / 2;
        ctx.BeginFigure(new Point(x, y1), true, true);
        ctx.PolyLineTo([new(x + h, y1 + h), new(x + h, y2 - h), new(x, y2), new(x - h, y2 - h), new(x - h, y1 + h)], true, false);
    }
}

/// <summary>Digital counter in seven-segment style.</summary>
public sealed class SevenSegmentDisplay : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(SevenSegmentDisplay),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DigitsProperty = DependencyProperty.Register(
        nameof(Digits), typeof(int), typeof(SevenSegmentDisplay),
        new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsRender));

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int Digits
    {
        get => (int)GetValue(DigitsProperty);
        set => SetValue(DigitsProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new Rect(RenderSize));
        SevenSegment.Draw(dc, Value.ToString(CultureInfo.InvariantCulture), new Rect(RenderSize), Digits);
    }
}

/// <summary>Analog VU-style meter with an animated needle and a digital reading above the pivot.</summary>
public sealed class AnalogGauge : FrameworkElement
{
    private const double StartAngle = 152;
    private const double Sweep = 124;

    private static readonly Brush ScaleBrush = Frozen(Color.FromRgb(0xC8, 0x8A, 0x4E));
    private static readonly Brush NeedleBrush = Frozen(Color.FromRgb(0xFF, 0x9A, 0x3C));
    private static readonly Brush NeedleGlowBrush = Frozen(Color.FromArgb(0x50, 0xFF, 0x8A, 0x24));
    private static readonly Brush FrameBrush = Frozen(Color.FromRgb(0x5A, 0x3A, 0x1E));
    private static readonly Brush PivotFill = Frozen(Color.FromRgb(0x14, 0x10, 0x0C));
    private static readonly Typeface LabelFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private static readonly Brush FaceBrush = CreateFace();

    public static readonly DependencyProperty NeedleProperty = DependencyProperty.Register(
        nameof(Needle), typeof(double), typeof(AnalogGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ReadingProperty = DependencyProperty.Register(
        nameof(Reading), typeof(int), typeof(AnalogGauge),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender, OnReadingChanged));

    /// <summary>Needle position in scale units; animated towards <see cref="Reading"/>.</summary>
    public double Needle
    {
        get => (double)GetValue(NeedleProperty);
        set => SetValue(NeedleProperty, value);
    }

    public int Reading
    {
        get => (int)GetValue(ReadingProperty);
        set => SetValue(ReadingProperty, value);
    }

    public int ScaleMaximum => NiceMaximum(Reading);

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Brush CreateFace()
    {
        var b = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.15),
            GradientOrigin = new Point(0.5, 0.0),
            RadiusX = 0.85,
            RadiusY = 1.1,
        };
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0x5A, 0x38, 0x1A), 0.0));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0x2A, 0x1B, 0x10), 0.45));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0x10, 0x0D, 0x0B), 1.0));
        b.Freeze();
        return b;
    }

    /// <summary>Smallest of 5, 10, 25, 50, 100, 250, ... that holds the value.</summary>
    public static int NiceMaximum(int value)
    {
        var max = 5;
        var steps = new[] { 2, 2.5, 2 };
        var i = 0;
        while (max < value && max < 100_000_000)
        {
            max = (int)Math.Round(max * steps[i % 3]);
            i++;
        }
        return max;
    }

    private static void OnReadingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var gauge = (AnalogGauge)d;
        var target = Math.Clamp((int)e.NewValue, 0, gauge.ScaleMaximum);
        gauge.BeginAnimation(NeedleProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(900))
        {
            EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 6, EasingMode = EasingMode.EaseOut },
        });
    }

    private Point PointAt(Point center, double radius, double fraction)
    {
        var rad = (StartAngle - Sweep * fraction) * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(rad), center.Y - radius * Math.Sin(rad));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 20 || h < 20) return;

        dc.DrawRoundedRectangle(FaceBrush, new Pen(FrameBrush, 1), new Rect(0.5, 0.5, w - 1, h - 1), 4, 4);

        var center = new Point(w / 2, h * 0.86);
        var radius = Math.Min(w * 0.44, h * 0.74);
        var max = ScaleMaximum;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var arc = new StreamGeometry();
        using (var ctx = arc.Open())
        {
            ctx.BeginFigure(PointAt(center, radius, 0), false, false);
            ctx.ArcTo(PointAt(center, radius, 1), new System.Windows.Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
        }
        arc.Freeze();
        var scalePen = new Pen(ScaleBrush, 1.4);
        dc.DrawGeometry(null, scalePen, arc);

        const int majors = 5;
        const int minorPerMajor = 4;
        var fontSize = Math.Max(7, radius * 0.11);
        for (var i = 0; i <= majors * minorPerMajor; i++)
        {
            var f = (double)i / (majors * minorPerMajor);
            var major = i % minorPerMajor == 0;
            var inner = radius - (major ? radius * 0.12 : radius * 0.06);
            dc.DrawLine(major ? scalePen : new Pen(ScaleBrush, 0.9), PointAt(center, inner, f), PointAt(center, radius, f));
            if (!major) continue;

            var value = max * i / (majors * minorPerMajor);
            var label = new FormattedText(FormatScale(value), CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
                LabelFace, fontSize, ScaleBrush, dpi);
            var p = PointAt(center, radius * 0.76, f);
            dc.DrawText(label, new Point(p.X - label.Width / 2, p.Y - label.Height / 2));
        }

        var digitHeight = radius * 0.2;
        SevenSegment.Draw(dc, Reading.ToString(CultureInfo.InvariantCulture),
            new Rect(center.X - radius * 0.4, center.Y - radius * 0.5, radius * 0.8, digitHeight), 1);

        var tip = PointAt(center, radius * 0.94, max == 0 ? 0 : Math.Clamp(Needle / max, 0, 1));
        dc.DrawLine(new Pen(NeedleGlowBrush, 6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, center, tip);
        dc.DrawLine(new Pen(NeedleBrush, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, center, tip);

        var pivot = Math.Max(5, radius * 0.11);
        dc.DrawEllipse(PivotFill, new Pen(NeedleBrush, 1.6), center, pivot, pivot);
        dc.DrawEllipse(System.Windows.Media.Brushes.Black, null, center, pivot * 0.4, pivot * 0.4);
    }

    private static string FormatScale(int value) => value >= 10_000
        ? (value / 1000).ToString(CultureInfo.InvariantCulture) + "k"
        : value.ToString(CultureInfo.InvariantCulture);
}

using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipwellWin.Models;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace ClipwellWin.Converters;

[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        bool b = v is bool b2 && b2;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => v is Visibility vis && vis == Visibility.Visible;
}

[ValueConversion(typeof(object), typeof(Visibility))]
public class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        bool hasValue = v != null && (v is not string s || !string.IsNullOrEmpty(s));
        if (Invert) hasValue = !hasValue;
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

[ValueConversion(typeof(byte[]), typeof(BitmapImage))]
public class BytesToImageConverter : IValueConverter
{
    public object? Convert(object v, Type t, object p, CultureInfo c)
    {
        if (v is not byte[] bytes || bytes.Length == 0) return null;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.StreamSource = new MemoryStream(bytes);
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch { return null; }
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

[ValueConversion(typeof(EntryType), typeof(string))]
public class EntryTypeToIconConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) => v switch
    {
        EntryType.Image => "",   // WPF-UI: Photo
        EntryType.Url => "",     // Globe
        EntryType.Code => "",    // Code
        EntryType.Color => "",   // Color
        _ => "",                 // Copy
    };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

[ValueConversion(typeof(EntryType), typeof(Brush))]
public class EntryTypeToColorConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) => v switch
    {
        EntryType.Image => new SolidColorBrush(Color.FromRgb(118, 185, 0)),
        EntryType.Url => new SolidColorBrush(Color.FromRgb(0, 120, 215)),
        EntryType.Code => new SolidColorBrush(Color.FromRgb(200, 88, 208)),
        EntryType.Color => new SolidColorBrush(Color.FromRgb(230, 162, 0)),
        _ => new SolidColorBrush(Color.FromRgb(130, 130, 130)),
    };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

[ValueConversion(typeof(string), typeof(Brush))]
public class HexToBrushConverter : IValueConverter
{
    public object? Convert(object v, Type t, object p, CultureInfo c)
    {
        if (v is not string hex || string.IsNullOrEmpty(hex)) return null;
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return null; }
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

[ValueConversion(typeof(bool), typeof(string))]
public class PinnedToIconConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
        => v is true ? "" : ""; // Pin filled / Pin
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

[ValueConversion(typeof(EntryType), typeof(Visibility))]
public class TypeToVisibilityConverter : IValueConverter
{
    public EntryType TargetType { get; set; }
    public bool Invert { get; set; }
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        bool match = v is EntryType et && et == TargetType;
        if (Invert) match = !match;
        return match ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

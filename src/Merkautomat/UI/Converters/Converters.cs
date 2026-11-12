using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Merkautomat.Models;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace Merkautomat.Converters;

[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
        => v is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => v is Visibility vis && vis == Visibility.Visible;
}

[ValueConversion(typeof(object), typeof(Visibility))]
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
        => v != null && (v is not string s || s.Length > 0) ? Visibility.Visible : Visibility.Collapsed;
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
        catch (FormatException) { return null; }
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

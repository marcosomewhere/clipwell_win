using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipwellWin.Models;
using ClipwellWin.Services;
using ClipwellWin.ViewModels;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily = System.Windows.Media.FontFamily;
using WpfButton = System.Windows.Controls.Button;
using WpfCursors = System.Windows.Input.Cursors;

namespace ClipwellWin.Views;

public partial class DetailWindow : Window
{
    private readonly EntryViewModel _vm;
    private string? _ocrText;
    private bool _showingOcr;
    private double _imageZoom = 1.0;
    private string[]? _pendingCodeLines;
    private TableRowGroup? _pendingRowGroup;
    private HashSet<string>? _pendingKeywords;
    private Color _pendingKeywordColor;

    public DetailWindow(EntryViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        Populate(vm);
    }

    private void Populate(EntryViewModel vm)
    {
        TitleLabel.Text = vm.Type switch
        {
            EntryType.Image => "Bild",
            EntryType.Url   => vm.UrlTitle ?? vm.Content ?? "URL",
            EntryType.Code  => vm.Language ?? "Code",
            EntryType.Color => vm.HexColor ?? "Farbe",
            _               => "Text",
        };
        ReasonLabel.Text = vm.DetectionReason ?? "Keine Erkennungsdetails gespeichert.";

        if (vm.Type == EntryType.Image && vm.Entry.ImageData != null)
        {
            ShowImage(vm);
            return;
        }

        if (vm.Type == EntryType.Color && vm.HexColor != null)
        {
            ShowColor(vm.HexColor);
            return;
        }

        var text = vm.Content ?? vm.Entry.OcrText ?? "";
        _ocrText = vm.Entry.OcrText;

        if (vm.Type == EntryType.Code)
        {
            CodeViewer.Visibility  = Visibility.Visible;
            TextPreview.Visibility = Visibility.Collapsed;
            WrapToggle.Visibility  = Visibility.Collapsed;

            var lines = text.Split('\n');
            if (lines.Length > SyntaxService.LazyThreshold)
            {
                var (doc, _, rowGroup) = SyntaxService.HighlightCore(text, vm.Language, SyntaxService.LazyThreshold);
                CodeViewer.Document = doc;
                var (kw, kwColor) = SyntaxService.GetKeywordSet(vm.Language);
                _pendingCodeLines   = lines;
                _pendingRowGroup    = rowGroup;
                _pendingKeywords    = kw;
                _pendingKeywordColor = kwColor;
                LoadMoreBtn.Visibility = Visibility.Visible;
                LoadMoreBtn.Content = $"Restliche {lines.Length - SyntaxService.LazyThreshold} Zeilen laden…";
            }
            else
            {
                CodeViewer.Document = SyntaxService.Highlight(text, vm.Language);
            }
        }
        else
        {
            TextPreview.Text = text;
        }
    }

    private void ShowColor(string hex)
    {
        TextPreview.Visibility      = Visibility.Collapsed;
        CodeViewer.Visibility       = Visibility.Collapsed;
        ImageBorder.Visibility      = Visibility.Collapsed;
        WrapToggle.Visibility       = Visibility.Collapsed;
        ColorPanel.Visibility       = Visibility.Visible;

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            ColorSwatch.Background = new SolidColorBrush(color);

            int r = color.R, g = color.G, b = color.B;
            var (h, s, l) = RgbToHsl(r, g, b);

            var formats = new (string label, string value, string copyVal)[]
            {
                ("HEX",  hex.ToUpperInvariant(),      hex.ToUpperInvariant()),
                ("RGB",  $"rgb({r}, {g}, {b})",       $"rgb({r}, {g}, {b})"),
                ("HSL",  $"hsl({h:0}, {s:0}%, {l:0}%)", $"hsl({h:0}, {s:0}%, {l:0}%)"),
                ("CSS",  $"color: {hex.ToUpperInvariant()};", $"color: {hex.ToUpperInvariant()};"),
                ("TW",   $"text-[{hex.ToUpperInvariant()}]",  $"text-[{hex.ToUpperInvariant()}]"),
            };

            ColorValuesGrid.Children.Clear();
            ColorValuesGrid.RowDefinitions.Clear();
            for (int i = 0; i < formats.Length; i++)
                ColorValuesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (int i = 0; i < formats.Length; i++)
            {
                var (label, display, copyVal) = formats[i];

                var lbl = new TextBlock
                {
                    Text = label,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12,
                    Opacity = 0.6,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 4, 12, 4),
                };
                Grid.SetRow(lbl, i); Grid.SetColumn(lbl, 0);
                ColorValuesGrid.Children.Add(lbl);

                var val = new TextBlock
                {
                    Text = display,
                    FontFamily = new FontFamily("Cascadia Code, Consolas"),
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 4),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                Grid.SetRow(val, i); Grid.SetColumn(val, 1);
                ColorValuesGrid.Children.Add(val);

                var copy = copyVal; // closure capture
                var btn = new WpfButton
                {
                    Content = "Kopieren",
                    Padding = new Thickness(10, 3, 10, 3),
                    FontSize = 11,
                    Margin = new Thickness(8, 4, 0, 4),
                    Cursor = WpfCursors.Hand,
                };
                btn.Click += (_, _) =>
                {
                    try { App.TrySetClipboardText(copy); }
                    catch { }
                };
                Grid.SetRow(btn, i); Grid.SetColumn(btn, 2);
                ColorValuesGrid.Children.Add(btn);
            }
        }
        catch { /* ungültiger Farbwert – TextPreview als Fallback */ }
    }

    private static (double h, double s, double l) RgbToHsl(int r, int g, int b)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        double max = Math.Max(rf, Math.Max(gf, bf));
        double min = Math.Min(rf, Math.Min(gf, bf));
        double l = (max + min) / 2.0;
        if (max == min) return (0, 0, Math.Round(l * 100));
        double d = max - min;
        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == rf ? ((gf - bf) / d + (gf < bf ? 6 : 0)) / 6
                 : max == gf ? ((bf - rf) / d + 2) / 6
                 :             ((rf - gf) / d + 4) / 6;
        return (Math.Round(h * 360), Math.Round(s * 100), Math.Round(l * 100));
    }

    private void ShowImage(EntryViewModel vm)
    {
        var bmp = LoadImage(vm.Entry.ImageData!);
        ImagePreview.Source = bmp;
        ImageBorder.Visibility      = Visibility.Visible;
        TextPreview.Visibility      = Visibility.Collapsed;
        CodeViewer.Visibility       = Visibility.Collapsed;
        ColorPanel.Visibility       = Visibility.Collapsed;
        WrapToggle.Visibility       = Visibility.Collapsed;
        ZoomResetBtn.Visibility     = Visibility.Visible;
        ZoomLabel.Visibility        = Visibility.Visible;
        ImageDetailsPanel.Visibility = Visibility.Visible;
        UpdateZoomLabel();

        var (w, h) = vm.ImagePixels;
        if (w > 0)
            ImageSizeLabel.Text = $"{w} × {h} px  ({vm.ImageSizeLabel})";

        ImageFormatLabel.Text = vm.ContentKind ?? vm.Entry.ContentKind ?? "Bild";

        var ocr = vm.Entry.OcrText;
        if (!string.IsNullOrEmpty(ocr))
        {
            OcrStatusLabel.Text = "OCR: verfügbar";
            _ocrText = ocr;
            ShowOcrBtn.Visibility = Visibility.Visible;
        }
        else
        {
            OcrStatusLabel.Text = OcrService.IsAvailable() ? "OCR: wird verarbeitet…" : "OCR: nicht verfügbar";
        }
    }

    private void ImagePreview_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double delta = e.Delta > 0 ? 1.15 : (1.0 / 1.15);
        _imageZoom = Math.Clamp(_imageZoom * delta, 0.1, 8.0);
        ImageScale.ScaleX = _imageZoom;
        ImageScale.ScaleY = _imageZoom;
        UpdateZoomLabel();
        e.Handled = true;
    }

    private void ZoomReset_Click(object sender, RoutedEventArgs e)
    {
        _imageZoom = 1.0;
        ImageScale.ScaleX = 1;
        ImageScale.ScaleY = 1;
        UpdateZoomLabel();
    }

    private void UpdateZoomLabel()
        => ZoomLabel.Text = $"{_imageZoom * 100:0}%";

    private void LoadMore_Click(object sender, RoutedEventArgs e)
    {
        LoadMoreBtn.IsEnabled = false;
        LoadMoreBtn.Content   = "Wird geladen…";

        var lines    = _pendingCodeLines!;
        var rowGroup = _pendingRowGroup!;
        var kw       = _pendingKeywords;
        var kwColor  = _pendingKeywordColor;
        int from     = SyntaxService.LazyThreshold;

        _ = Dispatcher.InvokeAsync(() =>
        {
            SyntaxService.AppendLines(rowGroup, lines, from, lines.Length, kw, kwColor);
            LoadMoreBtn.Visibility = Visibility.Collapsed;
            _pendingCodeLines  = null;
            _pendingRowGroup   = null;
        }, DispatcherPriority.Background);
    }

    private void WrapToggle_Changed(object sender, RoutedEventArgs e)
    {
        TextPreview.TextWrapping = WrapToggle.IsChecked == true
            ? System.Windows.TextWrapping.Wrap
            : System.Windows.TextWrapping.NoWrap;
        TextPreview.HorizontalScrollBarVisibility = WrapToggle.IsChecked == true
            ? System.Windows.Controls.ScrollBarVisibility.Disabled
            : System.Windows.Controls.ScrollBarVisibility.Auto;
    }

    private void ShowOcr_Click(object sender, RoutedEventArgs e)
    {
        _showingOcr = !_showingOcr;
        if (_showingOcr && !string.IsNullOrEmpty(_ocrText))
        {
            ImageBorder.Visibility  = Visibility.Collapsed;
            TextPreview.Text        = _ocrText;
            TextPreview.Visibility  = Visibility.Visible;
            ShowOcrBtn.Content      = "Bild anzeigen";
            WrapToggle.Visibility   = Visibility.Visible;
        }
        else
        {
            TextPreview.Visibility  = Visibility.Collapsed;
            ImageBorder.Visibility  = Visibility.Visible;
            ShowOcrBtn.Content      = "OCR-Text anzeigen";
            WrapToggle.Visibility   = Visibility.Collapsed;
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = TextPreview.Visibility == Visibility.Visible
                ? TextPreview.Text
                : _vm.Content ?? _vm.Entry.OcrText ?? "";

            if (!string.IsNullOrEmpty(text))
                App.TrySetClipboardText(text);
        }
        catch { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static BitmapImage? LoadImage(byte[] data)
    {
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.StreamSource = new MemoryStream(data);
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch { return null; }
    }
}

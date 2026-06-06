using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using ClipwellWin.Models;
using ClipwellWin.Services;
using ClipwellWin.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily = System.Windows.Media.FontFamily;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Path = System.Windows.Shapes.Path;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using WpfButton = System.Windows.Controls.Button;
using WpfCursors = System.Windows.Input.Cursors;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace ClipwellWin.Views;

public partial class DetailWindow : Window
{
    private readonly EntryViewModel _vm;
    private string? _ocrText;
    private bool _showingOcr;
    private double _imageZoom = 1.0;
    private BitmapSource? _baseImage;
    private EditorTool _activeTool = EditorTool.Select;
    private readonly List<EditorAnnotation> _annotations = [];
    private readonly Stack<EditorSnapshot> _undo = new();
    private readonly Stack<EditorSnapshot> _redo = new();
    private EditorAnnotation? _selectedAnnotation;
    private Point? _drawStart;
    private Point? _moveStart;
    private EditorAnnotation? _movingAnnotation;
    private bool _didMove;
    private Shape? _draftShape;
    private Brush _editorBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E53935"));
    private string _currentColor = "#E53935";
    private int _nextMarkerNumber = 1;
    private Border? _inlineTextEditor;
    private WpfTextBox? _inlineTextBox;
    private Point _inlineTextPoint;
    private EditorAnnotation? _inlineTextTarget;
    private bool _textPreviewEditable;
    private string _savedTextPreview = "";
    private bool _isCodeEditor;
    private string? _editorLanguage;
    private bool _loadingTextEditor;
    private double _activeInlineTextSize = 18;
    private bool _textEditorSyncQueued;
    private double _textScrollX;
    private double _textScrollY;

    private enum EditorTool
    {
        Select,
        Arrow,
        Line,
        Rectangle,
        Ellipse,
        Pen,
        Text,
        Number,
        Highlight,
        Redact,
        Pixelate,
        Blur,
        Crop,
    }

    private sealed class EditorAnnotation
    {
        public EditorTool Tool { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }
        public double Thickness { get; set; } = 4;
        public string Color { get; set; } = "#E53935";
        public string Text { get; set; } = "";
        public int Number { get; set; }
        public List<Point> Points { get; set; } = [];

        public EditorAnnotation Clone() => new()
        {
            Tool = Tool,
            X = X,
            Y = Y,
            W = W,
            H = H,
            Thickness = Thickness,
            Color = Color,
            Text = Text,
            Number = Number,
            Points = Points.Select(p => new Point(p.X, p.Y)).ToList(),
        };
    }

    private sealed class EditorSnapshot
    {
        public List<EditorAnnotation> Annotations { get; set; } = [];
        public double CanvasWidth { get; set; }
        public double CanvasHeight { get; set; }
        public double ImageLeft { get; set; }
        public double ImageTop { get; set; }
        public double ImageWidth { get; set; }
        public double ImageHeight { get; set; }
        public int NextMarkerNumber { get; set; }
    }

    public DetailWindow(EntryViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        Icon = App.CreateAppIconImageSource(32);
        TextPreview.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(TextPreview_ScrollChanged));
        InitializeTextEditorControls();
        Populate(vm);
    }

    private void InitializeTextEditorControls()
    {
        EditorLanguageBox.ItemsSource = new[]
        {
            "Code", "C#", "JavaScript", "TypeScript", "Python", "PowerShell", "SQL",
            "JSON", "XML", "YAML", "HTML", "CSS", "Bash", "Go", "Rust", "C++",
            "PHP", "Ruby",
        };
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        if (System.Windows.Application.Current is not App app) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.SetImmersiveDarkMode(hwnd, app.IsEffectiveDarkTheme);
            NativeMethods.SetWindowFrameColors(
                hwnd,
                app.IsEffectiveDarkTheme ? 0x00181410 : 0x00F9F7F4,
                app.IsEffectiveDarkTheme ? 0x00F7F5F3 : 0x00221F1B,
                app.IsEffectiveDarkTheme ? 0x00433930 : 0x00D0C8BD);
        }
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
        ReasonLabel.Text = "";
        ReasonLabel.Visibility = Visibility.Collapsed;
        TitleSeparator.Visibility = Visibility.Collapsed;

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

        ConfigureTextEditor(vm, text);
    }

    private void ConfigureTextEditor(EntryViewModel vm, string text)
    {
        TextEditorPanel.Visibility = Visibility.Visible;
        BottomActionBar.Visibility = Visibility.Collapsed;

        _savedTextPreview = text;
        _isCodeEditor = vm.Type == EntryType.Code;
        _textPreviewEditable = vm.Type is EntryType.Text or EntryType.Code;
        _editorLanguage = _isCodeEditor
            ? vm.Language ?? SyntaxService.DetectLanguage(text) ?? "Code"
            : null;

        _loadingTextEditor = true;
        TextPreview.IsUndoEnabled = false;
        TextPreview.Text = text;
        TextPreview.IsUndoEnabled = true;
        _loadingTextEditor = false;
        TextPreview.IsReadOnly = !_textPreviewEditable;
        TextPreview.FontSize = _isCodeEditor ? 15 : 16;
        TextPreview.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        TextPreview.HorizontalScrollBarVisibility = EditorWrapToggle.IsChecked == true
            ? ScrollBarVisibility.Disabled
            : ScrollBarVisibility.Auto;
        CodeHighlightOverlay.Visibility = _isCodeEditor ? Visibility.Visible : Visibility.Collapsed;
        TextPreview.Foreground = _isCodeEditor ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(244, 246, 248));
        TextPreview.CaretBrush = new SolidColorBrush(Color.FromRgb(168, 230, 35));
        TextEditorTitleLabel.Text = _isCodeEditor
            ? "Skripteditor"
            : (string.Equals(vm.ContentKind, "NOTE", StringComparison.OrdinalIgnoreCase)
                ? "Notiz aus Zwischenablage"
                : "Text aus Zwischenablage");
        TextModeLabel.Text = _isCodeEditor ? $"Sprache: {_editorLanguage}" : "Nur-Text";
        EditorLanguageBox.Visibility = _isCodeEditor ? Visibility.Visible : Visibility.Collapsed;
        if (_isCodeEditor)
            EditorLanguageBox.SelectedItem = _editorLanguage;

        SaveTextBtn.Visibility = _textPreviewEditable ? Visibility.Visible : Visibility.Collapsed;
        SaveTextBtn.IsEnabled = false;
        UndoTextBtn.Visibility = _textPreviewEditable ? Visibility.Visible : Visibility.Collapsed;
        UndoTextBtn.IsEnabled = false;
        UpdateTextEditorStats();
        UpdateCodeHighlightOverlay();
        QueueTextEditorSync();
        ApplyEditorTheme();
    }

    private void ShowColor(string hex)
    {
        TextPreview.Visibility      = Visibility.Collapsed;
        TextEditorPanel.Visibility  = Visibility.Collapsed;
        ImageBorder.Visibility      = Visibility.Collapsed;
        ColorPanel.Visibility       = Visibility.Visible;
        BottomActionBar.Visibility  = Visibility.Collapsed;

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
        if (bmp != null)
        {
            _baseImage = bmp;
            EditorCanvas.Width = bmp.PixelWidth;
            EditorCanvas.Height = bmp.PixelHeight;
            EditorCanvas.Clip = new RectangleGeometry(new Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight));
            ImagePreview.Source = bmp;
            ImagePreview.Width = bmp.PixelWidth;
            ImagePreview.Height = bmp.PixelHeight;
            Canvas.SetLeft(ImagePreview, 0);
            Canvas.SetTop(ImagePreview, 0);
        }
        ImageBorder.Visibility      = Visibility.Visible;
        TextEditorPanel.Visibility  = Visibility.Collapsed;
        TextPreview.Visibility      = Visibility.Collapsed;
        ColorPanel.Visibility       = Visibility.Collapsed;
        BottomActionBar.Visibility  = Visibility.Visible;
        ZoomResetBtn.Visibility     = Visibility.Visible;
        ZoomLabel.Visibility        = Visibility.Visible;
        ZoomControlsPanel.Visibility = Visibility.Visible;
        EditorActionsPanel.Visibility = Visibility.Visible;
        ImageEditorToolbar.Visibility = Visibility.Visible;
        ImageDetailsPanel.Visibility = Visibility.Visible;
        TitleSeparator.Visibility = Visibility.Visible;
        OcrStatusLabel.Visibility = Visibility.Collapsed;
        ReasonLabel.Visibility = Visibility.Collapsed;
        UpdateZoomLabel();

        var (w, h) = vm.ImagePixels;
        if (w > 0)
            ImageSizeLabel.Text = $"{w} × {h} px  ({vm.ImageSizeLabel})";

        ImageFormatLabel.Text = vm.ContentKind ?? vm.Entry.ContentKind ?? "Bild";

        var ocr = vm.Entry.OcrText;
        if (!string.IsNullOrEmpty(ocr))
        {
            _ocrText = ocr;
            ShowOcrBtn.Visibility = Visibility.Visible;
        }
        else
        {
            ShowOcrBtn.Visibility = Visibility.Collapsed;
        }
    }

    private void EditorTool_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string tag &&
            Enum.TryParse(tag, out EditorTool tool))
        {
            CommitInlineTextEditor();
            _activeTool = tool;
            if (PixelateOptions != null)
                PixelateOptions.Visibility = tool == EditorTool.Pixelate ? Visibility.Visible : Visibility.Collapsed;
            if (EditorCanvas != null)
                EditorCanvas.Cursor = tool switch
                {
                    EditorTool.Select => WpfCursors.Arrow,
                    EditorTool.Text => WpfCursors.IBeam,
                    _ => WpfCursors.Cross,
                };
        }
    }

    private void EditorColorSwatch_Checked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string color) return;
        _currentColor = color;
        _editorBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        if (_selectedAnnotation == null) return;
        PushUndo();
        _selectedAnnotation.Color = color;
        RenderAnnotations();
    }

    private void StrokeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        StrokeValueLabel.Text = $"{(int)StrokeSlider.Value} px";
        if (_selectedAnnotation == null) return;
        PushUndo();
        _selectedAnnotation.Thickness = StrokeSlider.Value;
        RenderAnnotations();
    }

    private void TextSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        TextSizeValueLabel.Text = $"{(int)TextSizeSlider.Value} px";
        _activeInlineTextSize = TextSizeSlider.Value;
        if (_selectedAnnotation?.Tool != EditorTool.Text) return;
        PushUndo();
        _selectedAnnotation.Thickness = TextSizeSlider.Value;
        RenderAnnotations();
    }

    private void PixelateSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        PixelateValueLabel.Text = $"{(int)PixelateSlider.Value} px";
        if (_selectedAnnotation?.Tool != EditorTool.Pixelate) return;
        PushUndo();
        _selectedAnnotation.Thickness = PixelateSlider.Value;
        RenderAnnotations();
    }

    private void EditorCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_baseImage == null) return;
        var point = ClampPoint(e.GetPosition(EditorCanvas));
        _selectedAnnotation = null;

        if (_activeTool == EditorTool.Select)
        {
            RenderAnnotations();
            return;
        }

        if (_activeTool == EditorTool.Text)
        {
            ShowInlineTextEditor(point);
            return;
        }

        CommitInlineTextEditor();

        if (_activeTool == EditorTool.Number)
        {
            PushUndo();
            _annotations.Add(new EditorAnnotation
            {
                Tool = EditorTool.Number,
                X = point.X - 14,
                Y = point.Y - 14,
                W = 28,
                H = 28,
                Color = CurrentColor(),
                Number = _nextMarkerNumber++,
            });
            RenderAnnotations();
            return;
        }

        _drawStart = point;
        _draftShape = CreateDraftShape(_activeTool);
        if (_draftShape != null)
        {
            EditorCanvas.Children.Add(_draftShape);
            if (_draftShape is Line line)
            {
                line.X1 = point.X;
                line.Y1 = point.Y;
                line.X2 = point.X;
                line.Y2 = point.Y;
            }
            else
            {
                Canvas.SetLeft(_draftShape, point.X);
                Canvas.SetTop(_draftShape, point.Y);
            }
        }
        EditorCanvas.CaptureMouse();
    }

    private void EditorCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        var point = ClampPoint(e.GetPosition(EditorCanvas));

        if (_movingAnnotation != null && _moveStart.HasValue)
        {
            var dx = point.X - _moveStart.Value.X;
            var dy = point.Y - _moveStart.Value.Y;
            if (Math.Abs(dx) > 0.5 || Math.Abs(dy) > 0.5) _didMove = true;
            MoveAnnotation(_movingAnnotation, dx, dy);
            _moveStart = point;
            RenderAnnotations();
            return;
        }

        if (_drawStart == null) return;

        if (_activeTool == EditorTool.Pen)
        {
            if (_draftShape is not Polyline polyline) return;
            polyline.Points.Add(point);
            return;
        }

        if (_draftShape == null) return;
        if (_draftShape is Line draftLine)
        {
            draftLine.X1 = _drawStart.Value.X;
            draftLine.Y1 = _drawStart.Value.Y;
            draftLine.X2 = point.X;
            draftLine.Y2 = point.Y;
            return;
        }
        var rect = RectFrom(_drawStart.Value, point);
        Canvas.SetLeft(_draftShape, rect.X);
        Canvas.SetTop(_draftShape, rect.Y);
        _draftShape.Width = Math.Max(1, rect.Width);
        _draftShape.Height = Math.Max(1, rect.Height);
    }

    private void EditorCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_movingAnnotation != null)
        {
            if (!_didMove && _undo.Count > 0)
                _undo.Pop();
            _movingAnnotation = null;
            _moveStart = null;
            _didMove = false;
            EditorCanvas.ReleaseMouseCapture();
            RenderAnnotations();
            return;
        }

        if (_drawStart == null) return;
        var end = ClampPoint(e.GetPosition(EditorCanvas));
        var start = _drawStart.Value;
        var penPoints = _draftShape is Polyline draftPolyline
            ? draftPolyline.Points.ToList()
            : null;
        _drawStart = null;
        if (_draftShape != null)
        {
            EditorCanvas.Children.Remove(_draftShape);
            _draftShape = null;
        }
        EditorCanvas.ReleaseMouseCapture();

        var rect = RectFrom(start, end);
        if (_activeTool != EditorTool.Pen && (rect.Width < 3 || rect.Height < 3)) return;

        if (_activeTool == EditorTool.Crop)
        {
            PushUndo();
            CropCanvas(rect);
            RenderAnnotations();
            return;
        }

        PushUndo();
        var annotation = new EditorAnnotation
        {
            Tool = _activeTool,
            X = rect.X,
            Y = rect.Y,
            W = rect.Width,
            H = rect.Height,
            Color = CurrentColor(),
            Thickness = StrokeSlider.Value,
        };
        if (_activeTool == EditorTool.Pixelate)
            annotation.Thickness = PixelateSlider.Value;

        if (_activeTool is EditorTool.Arrow or EditorTool.Line)
        {
            annotation.X = start.X;
            annotation.Y = start.Y;
            annotation.W = end.X - start.X;
            annotation.H = end.Y - start.Y;
        }
        else if (_activeTool == EditorTool.Pen)
        {
            annotation.Points = penPoints ?? [];
            if (annotation.Points.Count < 2) return;
        }

        _annotations.Add(annotation);
        RenderAnnotations();
    }

    private Shape? CreateDraftShape(EditorTool tool)
    {
        var stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(CurrentColor()));
        var thickness = StrokeSlider.Value;
        return tool switch
        {
            EditorTool.Arrow or EditorTool.Line => new Line { Stroke = stroke, StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round },
            EditorTool.Rectangle or EditorTool.Crop => new Rectangle { Stroke = stroke, StrokeThickness = thickness, StrokeDashArray = tool == EditorTool.Crop ? new DoubleCollection([4, 3]) : null, Fill = Brushes.Transparent },
            EditorTool.Ellipse => new Ellipse { Stroke = stroke, StrokeThickness = thickness, Fill = Brushes.Transparent },
            EditorTool.Highlight => new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(90, 255, 235, 59)), Stroke = Brushes.Transparent },
            EditorTool.Redact => new Rectangle { Fill = Brushes.Black, Stroke = Brushes.Black },
            EditorTool.Pixelate => new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(170, 80, 80, 80)), Stroke = Brushes.Black, StrokeDashArray = new DoubleCollection([2, 2]) },
            EditorTool.Blur => new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(150, 220, 220, 220)) },
            EditorTool.Pen => new Polyline { Stroke = stroke, StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round },
            _ => new Rectangle { Stroke = stroke, StrokeThickness = thickness, Fill = Brushes.Transparent },
        };
    }

    private void RenderAnnotations()
    {
        for (var i = EditorCanvas.Children.Count - 1; i >= 0; i--)
        {
            var child = EditorCanvas.Children[i];
            if (ReferenceEquals(child, ImagePreview) || ReferenceEquals(child, _inlineTextEditor))
                continue;
            EditorCanvas.Children.RemoveAt(i);
        }

        foreach (var annotation in _annotations)
        {
            var element = CreateElement(annotation);
            element.Tag = annotation;
            element.MouseLeftButtonDown += Annotation_MouseLeftButtonDown;
            element.MouseRightButtonDown += Annotation_MouseRightButtonDown;
            EditorCanvas.Children.Add(element);
        }

        if (_inlineTextEditor != null && EditorCanvas.Children.Contains(_inlineTextEditor))
        {
            EditorCanvas.Children.Remove(_inlineTextEditor);
            EditorCanvas.Children.Add(_inlineTextEditor);
        }

        var hasSelection = _selectedAnnotation != null;
        DuplicateBtn.IsEnabled = hasSelection;
        DeleteBtn.IsEnabled    = hasSelection;
    }

    private FrameworkElement CreateElement(EditorAnnotation a)
    {
        var color = new SolidColorBrush((Color)ColorConverter.ConvertFromString(a.Color));
        FrameworkElement element;
        switch (a.Tool)
        {
            case EditorTool.Rectangle:
                element = new Rectangle { Width = a.W, Height = a.H, Stroke = color, StrokeThickness = a.Thickness, Fill = Brushes.Transparent };
                Canvas.SetLeft(element, a.X); Canvas.SetTop(element, a.Y);
                break;
            case EditorTool.Ellipse:
                element = new Ellipse { Width = a.W, Height = a.H, Stroke = color, StrokeThickness = a.Thickness, Fill = Brushes.Transparent };
                Canvas.SetLeft(element, a.X); Canvas.SetTop(element, a.Y);
                break;
            case EditorTool.Highlight:
                element = new Rectangle { Width = a.W, Height = a.H, Fill = new SolidColorBrush(Color.FromArgb(90, 255, 235, 59)), Stroke = color, StrokeThickness = 1 };
                Canvas.SetLeft(element, a.X); Canvas.SetTop(element, a.Y);
                break;
            case EditorTool.Redact:
                element = new Rectangle { Width = a.W, Height = a.H, Fill = Brushes.Black, Stroke = Brushes.Black };
                Canvas.SetLeft(element, a.X); Canvas.SetTop(element, a.Y);
                break;
            case EditorTool.Pixelate:
                element = PixelateBlock(a);
                Canvas.SetLeft(element, a.X); Canvas.SetTop(element, a.Y);
                break;
            case EditorTool.Blur:
                element = new Rectangle
                {
                    Width = a.W,
                    Height = a.H,
                    Fill = new SolidColorBrush(Color.FromArgb(180, 200, 200, 200)),
                };
                Canvas.SetLeft(element, a.X); Canvas.SetTop(element, a.Y);
                break;
            case EditorTool.Line:
                element = new Line { X1 = a.X, Y1 = a.Y, X2 = a.X + a.W, Y2 = a.Y + a.H, Stroke = color, StrokeThickness = a.Thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                break;
            case EditorTool.Arrow:
                element = ArrowPath(a, color);
                break;
            case EditorTool.Pen:
                element = new Polyline { Points = new PointCollection(a.Points), Stroke = color, StrokeThickness = a.Thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
                break;
            case EditorTool.Text:
                element = new Border
                {
                    Width = Math.Max(80, a.W),
                    MinHeight = Math.Max(32, a.H),
                    Background = new SolidColorBrush(Color.FromArgb(235, 20, 24, 28)),
                    BorderBrush = color,
                    BorderThickness = new Thickness(2),
                    Padding = new Thickness(8, 5, 8, 5),
                    Child = new TextBlock { Text = a.Text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, FontSize = Math.Max(10, a.Thickness) },
                };
                element.MouseLeftButtonDown += (_, e) =>
                {
                    if (e.ClickCount == 2)
                    {
                        ShowInlineTextEditor(new Point(a.X, a.Y), a);
                        e.Handled = true;
                    }
                };
                Canvas.SetLeft(element, a.X); Canvas.SetTop(element, a.Y);
                break;
            case EditorTool.Number:
                element = new Border
                {
                    Width = 30,
                    Height = 30,
                    CornerRadius = new CornerRadius(15),
                    Background = color,
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(2),
                    Child = new TextBlock { Text = a.Number.ToString(), Foreground = ContrastBrush(a.Color), FontWeight = FontWeights.Bold, HorizontalAlignment = WpfHorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                };
                Canvas.SetLeft(element, a.X); Canvas.SetTop(element, a.Y);
                break;
            default:
                element = new Rectangle { Width = a.W, Height = a.H, Stroke = color, StrokeThickness = a.Thickness };
                Canvas.SetLeft(element, a.X); Canvas.SetTop(element, a.Y);
                break;
        }

        if (ReferenceEquals(a, _selectedAnnotation))
            element.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.DeepSkyBlue, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.95 };
        return element;
    }

    private static FrameworkElement PixelateBlock(EditorAnnotation a)
    {
        var canvas = new Canvas { Width = a.W, Height = a.H, ClipToBounds = true, Background = Brushes.Black };
        var cell = Math.Clamp((int)Math.Round(a.Thickness), 4, 28);
        for (double y = 0; y < a.H; y += cell)
        for (double x = 0; x < a.W; x += cell)
        {
            var shade = (byte)(70 + (((int)(x + y) / cell) % 4) * 28);
            var rect = new Rectangle
            {
                Width = Math.Min(cell, a.W - x),
                Height = Math.Min(cell, a.H - y),
                Fill = new SolidColorBrush(Color.FromRgb(shade, shade, shade)),
            };
            Canvas.SetLeft(rect, x); Canvas.SetTop(rect, y);
            canvas.Children.Add(rect);
        }
        return canvas;
    }

    private static Path ArrowPath(EditorAnnotation a, Brush color)
    {
        var start = new Point(a.X, a.Y);
        var end = new Point(a.X + a.W, a.Y + a.H);
        var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        var head = Math.Max(12, a.Thickness * 4);
        var left = new Point(end.X - head * Math.Cos(angle - Math.PI / 6), end.Y - head * Math.Sin(angle - Math.PI / 6));
        var right = new Point(end.X - head * Math.Cos(angle + Math.PI / 6), end.Y - head * Math.Sin(angle + Math.PI / 6));
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.LineTo(end, true, false);
            ctx.BeginFigure(left, false, false);
            ctx.LineTo(end, true, false);
            ctx.LineTo(right, true, false);
        }
        geo.Freeze();
        return new Path { Data = geo, Stroke = color, StrokeThickness = a.Thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
    }

    private void Annotation_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_activeTool != EditorTool.Select || (sender as FrameworkElement)?.Tag is not EditorAnnotation annotation)
            return;

        if (!ReferenceEquals(_selectedAnnotation, annotation))
            _selectedAnnotation = annotation;
        PushUndo();
        _movingAnnotation = annotation;
        _moveStart = ClampPoint(e.GetPosition(EditorCanvas));
        _didMove = false;
        EditorCanvas.CaptureMouse();
        RenderAnnotations();
        e.Handled = true;
    }

    private void Annotation_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not EditorAnnotation annotation) return;
        PushUndo();
        _annotations.Remove(annotation);
        if (ReferenceEquals(_selectedAnnotation, annotation)) _selectedAnnotation = null;
        RenderAnnotations();
        e.Handled = true;
    }

    private void MoveAnnotation(EditorAnnotation a, double dx, double dy)
    {
        if (a.Tool == EditorTool.Pen)
        {
            for (var i = 0; i < a.Points.Count; i++)
                a.Points[i] = new Point(a.Points[i].X + dx, a.Points[i].Y + dy);
            return;
        }
        a.X += dx;
        a.Y += dy;
    }

    private void EditorUndo_Click(object sender, RoutedEventArgs e)
    {
        CancelInlineTextEditor();
        if (_undo.Count == 0) return;
        _redo.Push(CreateSnapshot());
        RestoreSnapshot(_undo.Pop());
    }

    private void EditorRedo_Click(object sender, RoutedEventArgs e)
    {
        CancelInlineTextEditor();
        if (_redo.Count == 0) return;
        _undo.Push(CreateSnapshot());
        RestoreSnapshot(_redo.Pop());
    }

    private void EditorDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAnnotation == null) return;
        PushUndo();
        var clone = _selectedAnnotation.Clone();
        MoveAnnotation(clone, 18, 18);
        _annotations.Add(clone);
        _selectedAnnotation = clone;
        RenderAnnotations();
    }

    private void EditorDelete_Click(object sender, RoutedEventArgs e)
    {
        CancelInlineTextEditor();
        if (_selectedAnnotation == null) return;
        PushUndo();
        _annotations.Remove(_selectedAnnotation);
        _selectedAnnotation = null;
        RenderAnnotations();
    }

    private void EditorSmaller_Click(object sender, RoutedEventArgs e) => ScaleSelected(0.9);
    private void EditorLarger_Click(object sender, RoutedEventArgs e) => ScaleSelected(1.1);

    private void ScaleSelected(double factor)
    {
        if (_selectedAnnotation == null) return;
        PushUndo();
        if (_selectedAnnotation.Tool == EditorTool.Pen)
        {
            var bounds = BoundsOf(_selectedAnnotation);
            var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
            for (var i = 0; i < _selectedAnnotation.Points.Count; i++)
            {
                var p = _selectedAnnotation.Points[i];
                _selectedAnnotation.Points[i] = new Point(center.X + (p.X - center.X) * factor, center.Y + (p.Y - center.Y) * factor);
            }
        }
        else
        {
            var cx = _selectedAnnotation.X + _selectedAnnotation.W / 2;
            var cy = _selectedAnnotation.Y + _selectedAnnotation.H / 2;
            _selectedAnnotation.W *= factor;
            _selectedAnnotation.H *= factor;
            _selectedAnnotation.X = cx - _selectedAnnotation.W / 2;
            _selectedAnnotation.Y = cy - _selectedAnnotation.H / 2;
        }
        RenderAnnotations();
    }

    private void EditorExpandCanvas_Click(object sender, RoutedEventArgs e)
    {
        var amountText = Prompt("Rand erweitern", "Rand in Pixeln:", "120");
        if (!double.TryParse(amountText, out var amount) || amount <= 0) return;
        PushUndo();
        EditorCanvas.Width += amount * 2;
        EditorCanvas.Height += amount * 2;
        UpdateCanvasClip();
        Canvas.SetLeft(ImagePreview, Canvas.GetLeft(ImagePreview) + amount);
        Canvas.SetTop(ImagePreview, Canvas.GetTop(ImagePreview) + amount);
        foreach (var a in _annotations) MoveAnnotation(a, amount, amount);
        RenderAnnotations();
    }

    private void EditorAutoTrim_Click(object sender, RoutedEventArgs e)
    {
        var rect = new Rect(Canvas.GetLeft(ImagePreview), Canvas.GetTop(ImagePreview), ImagePreview.Width, ImagePreview.Height);
        foreach (var a in _annotations)
            rect.Union(BoundsOf(a));
        rect.Inflate(16, 16);
        PushUndo();
        CropCanvas(rect);
        RenderAnnotations();
    }

    private void EditorResize_Click(object sender, RoutedEventArgs e)
    {
        var size = PromptSize("Skalieren", Math.Round(EditorCanvas.Width).ToString(), Math.Round(EditorCanvas.Height).ToString());
        if (size == null) return;
        var (newWidth, newHeight) = size.Value;
        if (newWidth < 64 || newHeight < 64) return;
        var factorX = newWidth / EditorCanvas.Width;
        var factorY = newHeight / EditorCanvas.Height;
        var thicknessFactor = (factorX + factorY) / 2;
        PushUndo();
        EditorCanvas.Width = newWidth;
        EditorCanvas.Height = newHeight;
        UpdateCanvasClip();
        ImagePreview.Width *= factorX;
        ImagePreview.Height *= factorY;
        Canvas.SetLeft(ImagePreview, Canvas.GetLeft(ImagePreview) * factorX);
        Canvas.SetTop(ImagePreview, Canvas.GetTop(ImagePreview) * factorY);
        foreach (var a in _annotations)
        {
            a.X *= factorX; a.Y *= factorY; a.W *= factorX; a.H *= factorY;
            if (a.Tool is not EditorTool.Text and not EditorTool.Pixelate)
                a.Thickness *= thicknessFactor;
            for (var i = 0; i < a.Points.Count; i++)
                a.Points[i] = new Point(a.Points[i].X * factorX, a.Points[i].Y * factorY);
        }
        RenderAnnotations();
    }

    private RenderTargetBitmap? RenderEditedImage()
    {
        CommitInlineTextEditor();
        if (EditorCanvas.Width <= 0 || EditorCanvas.Height <= 0) return null;
        EditorCanvas.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(EditorCanvas.Width));
        var height = Math.Max(1, (int)Math.Ceiling(EditorCanvas.Height));
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(EditorCanvas);
        rtb.Freeze();
        return rtb;
    }

    private void CropCanvas(Rect rect)
    {
        rect.Intersect(new Rect(0, 0, EditorCanvas.Width, EditorCanvas.Height));
        if (rect.Width < 8 || rect.Height < 8) return;
        Canvas.SetLeft(ImagePreview, Canvas.GetLeft(ImagePreview) - rect.X);
        Canvas.SetTop(ImagePreview, Canvas.GetTop(ImagePreview) - rect.Y);
        foreach (var a in _annotations) MoveAnnotation(a, -rect.X, -rect.Y);
        EditorCanvas.Width = rect.Width;
        EditorCanvas.Height = rect.Height;
        UpdateCanvasClip();
    }

    private void UpdateCanvasClip()
        => EditorCanvas.Clip = new RectangleGeometry(new Rect(0, 0, EditorCanvas.Width, EditorCanvas.Height));

    private void PushUndo()
    {
        _undo.Push(CreateSnapshot());
        _redo.Clear();
    }

    private List<EditorAnnotation> CloneAnnotations() => _annotations.Select(a => a.Clone()).ToList();

    private EditorSnapshot CreateSnapshot() => new()
    {
        Annotations = CloneAnnotations(),
        CanvasWidth = EditorCanvas.Width,
        CanvasHeight = EditorCanvas.Height,
        ImageLeft = Canvas.GetLeft(ImagePreview),
        ImageTop = Canvas.GetTop(ImagePreview),
        ImageWidth = ImagePreview.Width,
        ImageHeight = ImagePreview.Height,
        NextMarkerNumber = _nextMarkerNumber,
    };

    private void RestoreSnapshot(EditorSnapshot snapshot)
    {
        CancelInlineTextEditor();
        _annotations.Clear();
        _annotations.AddRange(snapshot.Annotations.Select(a => a.Clone()));
        EditorCanvas.Width = snapshot.CanvasWidth;
        EditorCanvas.Height = snapshot.CanvasHeight;
        ImagePreview.Width = snapshot.ImageWidth;
        ImagePreview.Height = snapshot.ImageHeight;
        Canvas.SetLeft(ImagePreview, snapshot.ImageLeft);
        Canvas.SetTop(ImagePreview, snapshot.ImageTop);
        _nextMarkerNumber = snapshot.NextMarkerNumber;
        UpdateCanvasClip();
        _selectedAnnotation = null;
        RenderAnnotations();
    }

    private string CurrentColor()
        => _currentColor;

    private Point ClampPoint(Point p)
        => new(Math.Clamp(p.X, 0, EditorCanvas.Width), Math.Clamp(p.Y, 0, EditorCanvas.Height));

    private static Rect RectFrom(Point a, Point b)
        => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static Rect BoundsOf(EditorAnnotation a)
    {
        if (a.Tool == EditorTool.Pen && a.Points.Count > 0)
        {
            var minX = a.Points.Min(p => p.X);
            var minY = a.Points.Min(p => p.Y);
            var maxX = a.Points.Max(p => p.X);
            var maxY = a.Points.Max(p => p.Y);
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }
        if (a.Tool is EditorTool.Line or EditorTool.Arrow)
            return RectFrom(new Point(a.X, a.Y), new Point(a.X + a.W, a.Y + a.H));
        return new Rect(a.X, a.Y, a.W, a.H);
    }

    private static Brush ContrastBrush(string color)
    {
        var c = (Color)ColorConverter.ConvertFromString(color);
        var luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
        return luminance > 0.55 ? Brushes.Black : Brushes.White;
    }

    private void ShowInlineTextEditor(Point point, EditorAnnotation? target = null)
    {
        CommitInlineTextEditor();

        _inlineTextPoint = ClampPoint(point);
        _inlineTextTarget = target;
        _activeInlineTextSize = target?.Thickness > 0 ? target.Thickness : TextSizeSlider.Value;
        _inlineTextBox = new WpfTextBox
        {
            Text = target?.Text ?? "",
            MinWidth = 180,
            MaxWidth = 360,
            Height = 30,
            AcceptsReturn = false,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 3, 8, 3),
            FontSize = Math.Clamp(_activeInlineTextSize, 10, 48),
            Cursor = WpfCursors.IBeam,
        };
        _inlineTextBox.SetResourceReference(WpfTextBox.BackgroundProperty, "OnboardingInputBrush");
        _inlineTextBox.SetResourceReference(WpfTextBox.ForegroundProperty, "OnboardingTextPrimaryBrush");
        _inlineTextBox.SetResourceReference(WpfTextBox.CaretBrushProperty, "OnboardingTextPrimaryBrush");
        _inlineTextBox.SetResourceReference(WpfTextBox.SelectionBrushProperty, "OnboardingAccentBrush");
        _inlineTextBox.KeyDown += InlineTextBox_KeyDown;

        var ok = new WpfButton
        {
            Content = "OK",
            MinWidth = 42,
            Height = 30,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(6, 0, 0, 0),
            Cursor = WpfCursors.Hand,
        };
        ok.Click += (_, _) => CommitInlineTextEditor();

        var cancel = new WpfButton
        {
            Content = "X",
            MinWidth = 30,
            Height = 30,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(4, 0, 0, 0),
            ToolTip = "Abbrechen",
            Cursor = WpfCursors.Hand,
        };
        cancel.Click += (_, _) => CancelInlineTextEditor();

        var fontSizeLabel = new TextBlock
        {
            Text = $"Text {_activeInlineTextSize:0}px",
            Foreground = Brushes.White,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var inlineSizeSlider = new Slider
        {
            Minimum = 10,
            Maximum = 48,
            Value = _activeInlineTextSize,
            Width = 90,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        inlineSizeSlider.ValueChanged += (_, args) =>
        {
            _activeInlineTextSize = args.NewValue;
            fontSizeLabel.Text = $"Text {_activeInlineTextSize:0}px";
            if (_inlineTextBox != null)
                _inlineTextBox.FontSize = _activeInlineTextSize;
        };
        var styleBar = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            Margin = new Thickness(2, 0, 2, 5),
            Children =
            {
                fontSizeLabel,
                inlineSizeSlider,
                new Border
                {
                    Width = 14,
                    Height = 14,
                    CornerRadius = new CornerRadius(7),
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(target?.Color ?? CurrentColor())),
                    VerticalAlignment = VerticalAlignment.Center,
                },
            },
        };

        var inputRow = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            Children = { _inlineTextBox, ok, cancel },
        };

        _inlineTextEditor = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(248, 20, 24, 28)),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(target?.Color ?? CurrentColor())),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(4),
            Cursor = WpfCursors.Arrow,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 14,
                ShadowDepth = 3,
                Opacity = 0.45,
            },
            Child = new StackPanel
            {
                Children = { styleBar, inputRow },
            },
        };

        var x = Math.Min(_inlineTextPoint.X, Math.Max(0, EditorCanvas.Width - 300));
        var y = _inlineTextPoint.Y >= 74
            ? _inlineTextPoint.Y - 70
            : Math.Min(_inlineTextPoint.Y + 10, Math.Max(0, EditorCanvas.Height - 70));
        System.Windows.Controls.Panel.SetZIndex(_inlineTextEditor, 10000);
        Canvas.SetLeft(_inlineTextEditor, x);
        Canvas.SetTop(_inlineTextEditor, y);
        EditorCanvas.Children.Add(_inlineTextEditor);
        Dispatcher.BeginInvoke(() =>
        {
            _inlineTextBox?.Focus();
            _inlineTextBox?.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void InlineTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitInlineTextEditor();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelInlineTextEditor();
            e.Handled = true;
        }
    }

    private bool CommitInlineTextEditor()
    {
        if (_inlineTextEditor == null || _inlineTextBox == null)
            return false;

        var text = _inlineTextBox.Text.Trim();
        var target = _inlineTextTarget;
        RemoveInlineTextEditor();
        if (string.IsNullOrWhiteSpace(text))
        {
            RenderAnnotations();
            return false;
        }

        PushUndo();
        if (target != null)
        {
            target.Text = text;
            target.Thickness = _activeInlineTextSize;
            target.W = Math.Clamp(text.Length * Math.Max(8, _activeInlineTextSize * 0.65), 120, 420);
            target.H = Math.Max(36, target.H);
            _selectedAnnotation = target;
        }
        else
        {
            var width = Math.Clamp(text.Length * Math.Max(8, _activeInlineTextSize * 0.65), 120, 420);
            var annotation = new EditorAnnotation
            {
                Tool = EditorTool.Text,
                X = _inlineTextPoint.X,
                Y = _inlineTextPoint.Y,
                W = width,
                H = 44,
                Color = CurrentColor(),
                Thickness = _activeInlineTextSize,
                Text = text,
            };
            _annotations.Add(annotation);
            _selectedAnnotation = annotation;
        }

        RenderAnnotations();
        return true;
    }

    private void CancelInlineTextEditor()
    {
        if (_inlineTextEditor == null) return;
        RemoveInlineTextEditor();
        RenderAnnotations();
    }

    private void RemoveInlineTextEditor()
    {
        if (_inlineTextEditor != null)
            EditorCanvas.Children.Remove(_inlineTextEditor);
        if (_inlineTextBox != null)
            _inlineTextBox.KeyDown -= InlineTextBox_KeyDown;
        _inlineTextEditor = null;
        _inlineTextBox = null;
        _inlineTextTarget = null;
    }

    private string? Prompt(string title, string label, string defaultValue)
    {
        var box = new WpfTextBox { Text = defaultValue, MinWidth = 280, AcceptsReturn = false, Margin = new Thickness(0, 6, 0, 12) };
        var ok = new WpfButton { Content = "OK", MinWidth = 76, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new WpfButton { Content = "Abbrechen", MinWidth = 86, IsCancel = true };
        var dialog = new Window
        {
            Title = title,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Children =
                {
                    new TextBlock { Text = label },
                    box,
                    new StackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = WpfHorizontalAlignment.Right, Children = { ok, cancel } },
                },
            },
        };
        ok.Click += (_, _) => dialog.DialogResult = true;
        box.SelectAll();
        return dialog.ShowDialog() == true ? box.Text : null;
    }

    private (double Width, double Height)? PromptSize(string title, string defaultWidth, string defaultHeight)
    {
        var widthBox = new WpfTextBox { Text = defaultWidth, MinWidth = 120, AcceptsReturn = false, Margin = new Thickness(0, 6, 12, 12) };
        var heightBox = new WpfTextBox { Text = defaultHeight, MinWidth = 120, AcceptsReturn = false, Margin = new Thickness(0, 6, 0, 12) };
        var ok = new WpfButton { Content = "OK", MinWidth = 76, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new WpfButton { Content = "Abbrechen", MinWidth = 86, IsCancel = true };
        var fields = new Grid();
        fields.ColumnDefinitions.Add(new ColumnDefinition());
        fields.ColumnDefinitions.Add(new ColumnDefinition());
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.Children.Add(new TextBlock { Text = "Breite (px)" });
        var heightLabel = new TextBlock { Text = "Höhe (px)" };
        Grid.SetColumn(heightLabel, 1);
        fields.Children.Add(heightLabel);
        Grid.SetRow(widthBox, 1);
        fields.Children.Add(widthBox);
        Grid.SetRow(heightBox, 1);
        Grid.SetColumn(heightBox, 1);
        fields.Children.Add(heightBox);

        var dialog = new Window
        {
            Title = title,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Children =
                {
                    fields,
                    new StackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = WpfHorizontalAlignment.Right, Children = { ok, cancel } },
                },
            },
        };
        ok.Click += (_, _) => dialog.DialogResult = true;
        widthBox.SelectAll();
        if (dialog.ShowDialog() != true) return null;
        if (!double.TryParse(widthBox.Text, out var width) || !double.TryParse(heightBox.Text, out var height))
            return null;
        return (width, height);
    }

    private void ImagePreview_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double delta = e.Delta > 0 ? 1.15 : (1.0 / 1.15);
        SetImageZoom(_imageZoom * delta);
        e.Handled = true;
    }

    private void ZoomReset_Click(object sender, RoutedEventArgs e)
    {
        SetImageZoom(1.0);
    }

    private void EditorZoomOut_Click(object sender, RoutedEventArgs e) => SetImageZoom(_imageZoom / 1.15);

    private void EditorZoomIn_Click(object sender, RoutedEventArgs e) => SetImageZoom(_imageZoom * 1.15);

    private void SetImageZoom(double zoom)
    {
        _imageZoom = Math.Clamp(zoom, 0.1, 8.0);
        ImageScale.ScaleX = _imageZoom;
        ImageScale.ScaleY = _imageZoom;
        UpdateZoomLabel();
    }

    private void UpdateZoomLabel()
        => ZoomLabel.Text = $"{_imageZoom * 100:0}%";

    private void UpdateTextEditorStats()
    {
        if (LineNumberText == null || TextLineCountLabel == null || TextCharCountLabel == null) return;

        var text = TextPreview.Text ?? "";
        var lineCount = TextPreview.IsLoaded
            ? Math.Max(1, TextPreview.LineCount)
            : Math.Max(1, text.Replace("\r", "").Split('\n').Length);
        LineNumberText.Text = string.Join(Environment.NewLine, Enumerable.Range(1, lineCount));
        TextLineCountLabel.Text = $"Zeilen: {lineCount}";
        TextCharCountLabel.Text = $"Zeichen: {text.Length}";
    }

    private void TextPreview_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (LineNumberText == null) return;
        _textScrollX = e.HorizontalOffset;
        _textScrollY = e.VerticalOffset;
        SyncTextEditorViewport();
    }

    private void TextPreview_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        SyncTextEditorViewport();
        QueueTextEditorSync();
    }

    private void QueueTextEditorSync()
    {
        if (_textEditorSyncQueued) return;
        _textEditorSyncQueued = true;

        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _textEditorSyncQueued = false;
            UpdateTextEditorStats();
            UpdateCodeHighlightOverlay();
            SyncTextEditorViewport();
        }));
    }

    private void SyncTextEditorViewport()
    {
        if (LineNumberText == null || TextPreview == null) return;

        LineNumberText.RenderTransform = null;
        LineNumberScrollViewer.ScrollToVerticalOffset(_textScrollY);
        if (CodeHighlightOverlay != null)
        {
            var overlayWidth = Math.Max(TextPreview.ActualWidth, TextPreview.ViewportWidth);
            if (TextPreview.TextWrapping == TextWrapping.NoWrap)
                overlayWidth = Math.Max(overlayWidth, TextPreview.ExtentWidth);

            CodeHighlightOverlay.Width = overlayWidth;
            CodeHighlightOverlay.Height = double.NaN;
            CodeHighlightOverlay.RenderTransform = null;
            CodeHighlightOverlay.Clip = null;
            CodeHighlightScrollViewer.ScrollToHorizontalOffset(_textScrollX);
            CodeHighlightScrollViewer.ScrollToVerticalOffset(_textScrollY);
        }
    }

    private void ApplyEditorTheme()
    {
        if (TextPreview == null || LineNumberText == null) return;

        var light = System.Windows.Application.Current is App app && !app.IsEffectiveDarkTheme;
        if (light)
        {
            var panel = new SolidColorBrush(Color.FromRgb(247, 249, 252));
            var chrome = new SolidColorBrush(Color.FromRgb(239, 243, 247));
            var gutter = new SolidColorBrush(Color.FromRgb(231, 236, 242));
            var border = new SolidColorBrush(Color.FromRgb(207, 216, 226));
            var text = new SolidColorBrush(Color.FromRgb(24, 28, 34));
            var muted = new SolidColorBrush(Color.FromRgb(96, 105, 118));
            ApplyTextEditorColors(panel, chrome, gutter, border, text, muted);
        }
        else
        {
            var panel = new SolidColorBrush(Color.FromRgb(11, 17, 23));
            var chrome = new SolidColorBrush(Color.FromRgb(20, 28, 36));
            var gutter = new SolidColorBrush(Color.FromRgb(14, 21, 28));
            var border = new SolidColorBrush(Color.FromRgb(48, 57, 67));
            var text = new SolidColorBrush(Color.FromRgb(244, 246, 248));
            var muted = new SolidColorBrush(Color.FromRgb(135, 145, 156));
            ApplyTextEditorColors(panel, chrome, gutter, border, text, muted);
        }
    }

    private void ApplyTextEditorColors(Brush panel, Brush chrome, Brush gutter, Brush border, Brush text, Brush muted)
    {
        TextEditorPanel.Background = panel;
        TextEditorPanel.BorderBrush = border;
        TextEditorHeader.Background = chrome;
        TextEditorBody.Background = panel;
        TextEditorFooter.Background = chrome;
        LineNumberPanel.Background = gutter;
        LineNumberPanel.BorderBrush = border;
        TextPreview.Background = panel;
        TextPreview.Foreground = _isCodeEditor ? Brushes.Transparent : text;
        CodeHighlightOverlay.Foreground = text;
        LineNumberText.Foreground = muted;
        TextLineCountLabel.Foreground = muted;
        TextCharCountLabel.Foreground = muted;
        TextModeLabel.Foreground = muted;
        TextStatusLabel.Foreground = muted;
    }

    private void UpdateCodeHighlightOverlay()
    {
        if (CodeHighlightOverlay == null || !_isCodeEditor)
            return;

        CodeHighlightOverlay.Inlines.Clear();
        var text = TextPreview.Text ?? "";
        var keywordSet = KeywordsFor(_editorLanguage);
        var lines = text.Replace("\r", "").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            AppendHighlightedLine(CodeHighlightOverlay, lines[i], keywordSet);
            if (i < lines.Length - 1)
                CodeHighlightOverlay.Inlines.Add(new LineBreak());
        }
    }

    private static void AppendHighlightedLine(TextBlock target, string line, HashSet<string> keywords)
    {
        foreach (Match match in Regex.Matches(line, @"//.*|#.*|""(?:\\.|[^""])*""|'(?:\\.|[^'])*'|\b\d+(?:\.\d+)?\b|\b[A-Za-z_][A-Za-z0-9_]*\b|\s+|."))
        {
            var token = match.Value;
            var run = new Run(token);
            if (token.StartsWith("//", StringComparison.Ordinal) || token.StartsWith("#", StringComparison.Ordinal))
                run.Foreground = new SolidColorBrush(Color.FromRgb(93, 181, 89));
            else if (token.StartsWith("\"", StringComparison.Ordinal) || token.StartsWith("'", StringComparison.Ordinal))
                run.Foreground = new SolidColorBrush(Color.FromRgb(241, 196, 83));
            else if (Regex.IsMatch(token, @"^\d"))
                run.Foreground = new SolidColorBrush(Color.FromRgb(181, 206, 168));
            else if (keywords.Contains(token))
                run.Foreground = new SolidColorBrush(Color.FromRgb(197, 134, 232));
            else if (token is "true" or "false" or "null")
                run.Foreground = new SolidColorBrush(Color.FromRgb(86, 156, 214));
            target.Inlines.Add(run);
        }
    }

    private static HashSet<string> KeywordsFor(string? language)
    {
        var common = new HashSet<string>(["true", "false", "null"], StringComparer.OrdinalIgnoreCase);
        var languageKeywords = language switch
        {
            "HTML" => new[]
            {
                "doctype", "html", "head", "body", "main", "section", "article", "header",
                "footer", "div", "span", "button", "input", "script", "style", "link",
                "meta", "title", "class", "id", "href", "src", "type", "role", "aria",
            },
            "XML" => ["xml", "version", "encoding", "xmlns", "schema", "configuration", "appSettings", "add", "key", "value"],
            "JSON" => ["true", "false", "null"],
            "CSS" => ["display", "grid", "flex", "block", "none", "color", "background", "margin", "padding", "border", "font", "width", "height", "media"],
            "SQL" => ["SELECT", "FROM", "WHERE", "INSERT", "UPDATE", "DELETE", "CREATE", "TABLE", "AND", "OR", "JOIN", "ORDER", "GROUP", "BY"],
            "JavaScript" or "TypeScript" => ["function", "return", "const", "let", "var", "if", "else", "for", "while", "class", "new", "import", "export", "async", "await"],
            "C#" => ["using", "namespace", "public", "private", "protected", "internal", "class", "record", "static", "void", "async", "await", "return", "new"],
            "Python" => ["def", "class", "import", "from", "return", "if", "elif", "else", "for", "while", "with", "as", "None", "True", "False"],
            "PowerShell" => ["param", "function", "foreach", "where", "if", "else", "try", "catch", "Get", "Set", "Invoke", "Write"],
            _ => new[]
            {
                "function", "return", "const", "let", "var", "if", "else", "for", "while",
                "class", "new", "public", "private", "static", "void", "using", "namespace",
                "def", "import", "from", "SELECT", "FROM", "WHERE", "INSERT", "UPDATE",
                "DELETE", "CREATE", "TABLE", "AND", "OR",
            },
        };
        foreach (var keyword in languageKeywords)
            common.Add(keyword);
        return common;
    }

    private void TextPreview_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateTextEditorStats();
        UpdateCodeHighlightOverlay();
        QueueTextEditorSync();
        if (UndoTextBtn != null)
            UndoTextBtn.IsEnabled = !_loadingTextEditor && TextPreview.CanUndo;
        if (!_textPreviewEditable || SaveTextBtn == null) return;
        var dirty = !string.Equals(TextPreview.Text, _savedTextPreview, StringComparison.Ordinal);
        SaveTextBtn.IsEnabled = dirty && TextPreview.IsReadOnly == false;
        TextDirtyDot.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        TextStatusLabel.Text = dirty ? "Geändert" : "Bereit";
    }

    private void TextPreview_KeyDown(object sender, KeyEventArgs e)
    {
        if (!_textPreviewEditable) return;
        if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            SaveText();
            e.Handled = true;
        }
    }

    private void SaveText_Click(object sender, RoutedEventArgs e)
        => SaveText();

    private void UndoText_Click(object sender, RoutedEventArgs e)
    {
        if (TextPreview.CanUndo)
            TextPreview.Undo();
        UndoTextBtn.IsEnabled = TextPreview.CanUndo;
    }

    private void EditorLanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isCodeEditor || EditorLanguageBox.SelectedItem is not string language) return;
        _editorLanguage = language;
        TextEditorTitleLabel.Text = "Skripteditor";
        TextModeLabel.Text = $"Sprache: {language}";
        UpdateCodeHighlightOverlay();
        if (SaveTextBtn != null && !TextPreview.IsReadOnly)
        {
            SaveTextBtn.IsEnabled = true;
            TextDirtyDot.Visibility = Visibility.Visible;
            TextStatusLabel.Text = "Geändert";
        }
    }

    private void EditorWrapToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (TextPreview == null) return;

        TextPreview.TextWrapping = EditorWrapToggle.IsChecked == true
            ? System.Windows.TextWrapping.Wrap
            : System.Windows.TextWrapping.NoWrap;
        TextPreview.HorizontalScrollBarVisibility = EditorWrapToggle.IsChecked == true
            ? System.Windows.Controls.ScrollBarVisibility.Disabled
            : System.Windows.Controls.ScrollBarVisibility.Auto;
        QueueTextEditorSync();
    }

    private void SaveText()
    {
        if (!_textPreviewEditable || TextPreview.IsReadOnly) return;

        var text = TextPreview.Text;
        var isNote = string.Equals(_vm.ContentKind, "NOTE", StringComparison.OrdinalIgnoreCase);
        var language = _isCodeEditor ? _editorLanguage : isNote ? null : SyntaxService.DetectLanguage(text);
        var kind = isNote ? "NOTE" : ContentKindService.DetectTextKind(text, language);
        var reason = _isCodeEditor ? "Code manuell bearbeitet." : isNote ? "Notiz manuell bearbeitet." : "Text manuell bearbeitet.";

        if (System.Windows.Application.Current is App app)
            app.Database.UpdateContent(_vm.Id, text, kind, reason, language);

        _vm.SetContent(text, kind, reason, language);
        _savedTextPreview = text;
        _loadingTextEditor = true;
        TextPreview.IsUndoEnabled = false;
        TextPreview.IsUndoEnabled = true;
        _loadingTextEditor = false;
        SaveTextBtn.IsEnabled = false;
        UndoTextBtn.IsEnabled = false;
        TextDirtyDot.Visibility = Visibility.Collapsed;
        TextStatusLabel.Text = "Gespeichert";
        UpdateTextEditorStats();
    }

    private void ShowOcr_Click(object sender, RoutedEventArgs e)
    {
        _showingOcr = !_showingOcr;
        if (_showingOcr && !string.IsNullOrEmpty(_ocrText))
        {
            ImageBorder.Visibility  = Visibility.Collapsed;
            TextEditorPanel.Visibility = Visibility.Visible;
            TextPreview.Text        = _ocrText;
            TextPreview.Visibility  = Visibility.Visible;
            TextPreview.IsReadOnly  = true;
            TextEditorTitleLabel.Text = "OCR-Text aus Zwischenablage";
            TextModeLabel.Text = "OCR";
            EditorLanguageBox.Visibility = Visibility.Collapsed;
            SaveTextBtn.Visibility = Visibility.Collapsed;
            UndoTextBtn.Visibility = Visibility.Collapsed;
            _textPreviewEditable = false;
            UpdateTextEditorStats();
            ShowOcrBtn.Content      = "Bild anzeigen";
        }
        else
        {
            TextPreview.Visibility  = Visibility.Collapsed;
            TextEditorPanel.Visibility = Visibility.Collapsed;
            ImageBorder.Visibility  = Visibility.Visible;
            ShowOcrBtn.Content      = "OCR-Text anzeigen";
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ImageBorder.Visibility == Visibility.Visible && _baseImage != null)
            {
                var image = RenderEditedImage();
                if (image != null)
                    App.TrySetClipboardImage(image);
                return;
            }

            var text = TextPreview.Visibility == Visibility.Visible
                ? TextPreview.Text
                : _vm.Content ?? _vm.Entry.OcrText ?? "";

            if (!string.IsNullOrEmpty(text))
                App.TrySetClipboardText(text);
        }
        catch { }
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        if (ImageBorder.Visibility == Visibility.Visible && _baseImage != null)
        {
            SaveImageAs();
            return;
        }

        SaveTextAs();
    }

    private void SaveImageAs()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Bild speichern unter",
            Filter = "PNG-Bild|*.png",
            DefaultExt = ".png",
            FileName = DefaultExportName("png"),
        };
        if (dlg.ShowDialog(this) != true) return;

        var image = RenderEditedImage();
        if (image == null) return;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(dlg.FileName);
        encoder.Save(stream);
    }

    private void SaveTextAs()
    {
        var text = TextPreview.Visibility == Visibility.Visible
            ? TextPreview.Text
            : _vm.Content ?? _vm.Entry.OcrText ?? _vm.HexColor ?? "";
        if (string.IsNullOrEmpty(text)) return;

        var extension = ExportExtension();
        var dlg = new SaveFileDialog
        {
            Title = "Text speichern unter",
            Filter = ExportFilter(extension),
            DefaultExt = "." + extension,
            FileName = DefaultExportName(extension),
        };
        if (dlg.ShowDialog(this) != true) return;
        File.WriteAllText(dlg.FileName, text);
    }

    private string ExportExtension()
    {
        if (_vm.Type == EntryType.Color) return "txt";
        var language = _isCodeEditor ? _editorLanguage : _vm.Language;
        return language switch
        {
            "HTML" => "html",
            "XML" => "xml",
            "JSON" => "json",
            "CSS" => "css",
            "SQL" => "sql",
            "JavaScript" => "js",
            "TypeScript" => "ts",
            "Python" => "py",
            "PowerShell" => "ps1",
            "Bash" => "sh",
            "C#" => "cs",
            "C++" => "cpp",
            "Go" => "go",
            "Rust" => "rs",
            "PHP" => "php",
            "Ruby" => "rb",
            "YAML" => "yaml",
            _ when string.Equals(_vm.ContentKind, "NOTE", StringComparison.OrdinalIgnoreCase) => "txt",
            _ => "txt",
        };
    }

    private static string ExportFilter(string extension)
    {
        var label = extension switch
        {
            "png" => "PNG-Bild",
            "html" => "HTML-Datei",
            "xml" => "XML-Datei",
            "json" => "JSON-Datei",
            "css" => "CSS-Datei",
            "sql" => "SQL-Datei",
            "js" => "JavaScript-Datei",
            "ts" => "TypeScript-Datei",
            "py" => "Python-Datei",
            "ps1" => "PowerShell-Datei",
            "sh" => "Shell-Skript",
            "cs" => "C#-Datei",
            "cpp" => "C++-Datei",
            "go" => "Go-Datei",
            "rs" => "Rust-Datei",
            "php" => "PHP-Datei",
            "rb" => "Ruby-Datei",
            "yaml" => "YAML-Datei",
            _ => "Textdatei",
        };
        return $"{label}|*.{extension}|Alle Dateien|*.*";
    }

    private static string DefaultExportName(string extension)
        => $"clipwell-{DateTime.Now:yyyyMMdd-HHmmss}.{extension}";

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

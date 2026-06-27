using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using YamlDotNet.Core;
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
    private bool _ocrRequested;
    private EditorAnnotation? _selectedAnnotation;
    private Point? _drawStart;
    private Point? _moveStart;
    private EditorAnnotation? _movingAnnotation;
    private bool _didMove;
    private Shape? _draftShape;
    private Rect? _pendingCropRect;
    private readonly List<UIElement> _cropOverlayElements = [];
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
    private string? _savedEditorLanguage;
    private bool _loadingTextEditor;
    private double _activeInlineTextSize = 18;
    private bool _textEditorSyncQueued;
    private double _textScrollX;
    private double _textScrollY;
    private bool _isMarkdownPreviewActive;
    private static readonly TimeSpan UiRegexTimeout = TimeSpan.FromMilliseconds(100);

    private sealed record StyleMemory(string Color, double Thickness);
    private static readonly string StyleMemoryPath = System.IO.Path.Combine(AppPaths.DataDir, "editor-styles.json");
    private readonly List<StyleMemory> _recentStyles = [];

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

    public sealed record ExportFormat(string Label, string Extension);

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
        App.ApplyAppIcon(this);
        ApplyDetailTheme();
        TextPreview.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(TextPreview_ScrollChanged));
        InitializeTextEditorControls();
        Populate(vm);
        LoadStyleMemory();
        RestoreDetailWindowSize();
        _vm.PropertyChanged += Entry_PropertyChanged;
        Closing += DetailWindow_Closing;
        Closed += (_, _) =>
        {
            _vm.PropertyChanged -= Entry_PropertyChanged;
            SaveDetailWindowSize();
        };
    }

    private void DetailWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (System.Windows.Application.Current is not App)
            return;

        if (HasUnsavedImageChanges())
        {
            var r = System.Windows.MessageBox.Show(
                "Bildaenderungen gehen verloren. Trotzdem schliessen?",
                "Ungespeicherte Aenderungen",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (r != MessageBoxResult.OK) { e.Cancel = true; return; }
        }

        if (!HasUnsavedTextChanges()) return;

        var result = System.Windows.MessageBox.Show(
            "Textaenderungen vor dem Schliessen speichern?",
            "Ungespeicherte Aenderungen",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Cancel)
        {
            e.Cancel = true;
            return;
        }

        if (result == MessageBoxResult.Yes)
            SaveText();
    }

    private bool HasUnsavedImageChanges()
        => ImageBorder.Visibility == Visibility.Visible && _baseImage != null && _undo.Count > 0;

    private void RestoreDetailWindowSize()
    {
        if (System.Windows.Application.Current is not App app) return;
        var s = app.CurrentSettings;
        Width  = s.DetailWindowWidth  > 0 ? s.DetailWindowWidth  : 1250;
        Height = s.DetailWindowHeight > 0 ? s.DetailWindowHeight : 1020;
        if (s.DetailWindowMaximized)
            WindowState = WindowState.Maximized;
    }

    private void SaveDetailWindowSize()
    {
        if (System.Windows.Application.Current is not App app) return;
        app.CurrentSettings.DetailWindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            app.CurrentSettings.DetailWindowWidth  = Width;
            app.CurrentSettings.DetailWindowHeight = Height;
        }
        app.SaveSettings();
    }

    private void Entry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm.Type != EntryType.Image) return;
        if (e.PropertyName is not (nameof(EntryViewModel.PreviewText) or nameof(EntryViewModel.SublineText)))
            return;

        Dispatcher.Invoke(() =>
        {
            _ocrText = _vm.Entry.OcrText;
            if (_showingOcr)
                ShowOcrTextView();
            UpdateOcrButtonState();
        });
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

    private void ApplyDetailTheme()
    {
        var light = System.Windows.Application.Current is App app && !app.IsEffectiveDarkTheme;
        if (light)
        {
            SetBrushResource("EditorChromeBrush", Color.FromRgb(247, 249, 252));
            SetBrushResource("EditorPanelBrush", Color.FromRgb(255, 255, 255));
            SetBrushResource("EditorPanelAltBrush", Color.FromRgb(239, 243, 247));
            SetBrushResource("EditorCanvasBackBrush", Color.FromRgb(229, 234, 241));
            SetBrushResource("EditorBorderBrush", Color.FromRgb(207, 216, 226));
            SetBrushResource("EditorMutedBrush", Color.FromRgb(96, 105, 118));
            SetBrushResource("EditorHoverBrush", Color.FromRgb(226, 232, 240));
            SetBrushResource("EditorPressedBrush", Color.FromRgb(216, 224, 235));
            SetBrushResource("EditorTextPanelBrush", Color.FromRgb(255, 255, 255));
            SetBrushResource("EditorTextHeaderBrush", Color.FromRgb(244, 247, 251));
            SetBrushResource("EditorTextBodyBrush", Color.FromRgb(255, 255, 255));
            SetBrushResource("EditorTextLineBrush", Color.FromRgb(241, 245, 249));
            SetBrushResource("EditorTextBrush", Color.FromRgb(17, 24, 39));
            SetBrushResource("EditorTextMutedBrush", Color.FromRgb(100, 116, 139));
            SetBrushResource("EditorSelectionTextBrush", Color.FromRgb(17, 20, 24));
            EditorCanvas.Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));
        }
        else
        {
            SetBrushResource("EditorChromeBrush", Color.FromRgb(16, 20, 24));
            SetBrushResource("EditorPanelBrush", Color.FromRgb(23, 29, 34));
            SetBrushResource("EditorPanelAltBrush", Color.FromRgb(31, 38, 45));
            SetBrushResource("EditorCanvasBackBrush", Color.FromRgb(35, 42, 49));
            SetBrushResource("EditorBorderBrush", Color.FromRgb(48, 57, 67));
            SetBrushResource("EditorMutedBrush", Color.FromRgb(154, 166, 178));
            SetBrushResource("EditorHoverBrush", Color.FromRgb(40, 49, 58));
            SetBrushResource("EditorPressedBrush", Color.FromRgb(50, 60, 69));
            SetBrushResource("EditorTextPanelBrush", Color.FromRgb(16, 24, 32));
            SetBrushResource("EditorTextHeaderBrush", Color.FromRgb(20, 28, 36));
            SetBrushResource("EditorTextBodyBrush", Color.FromRgb(11, 17, 23));
            SetBrushResource("EditorTextLineBrush", Color.FromRgb(14, 21, 28));
            SetBrushResource("EditorTextBrush", Color.FromRgb(244, 246, 248));
            SetBrushResource("EditorTextMutedBrush", Color.FromRgb(135, 145, 156));
            SetBrushResource("EditorSelectionTextBrush", Color.FromRgb(17, 20, 24));
            EditorCanvas.Background = new SolidColorBrush(Color.FromRgb(239, 239, 239));
        }
    }

    private void SetBrushResource(string key, Color color)
    {
        Resources[key] = new SolidColorBrush(color);
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
        OpenUrlBtn.Visibility = vm.Type == EntryType.Url && UrlPreviewService.IsUrl(vm.Content)
            ? Visibility.Visible
            : Visibility.Collapsed;

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
        _savedEditorLanguage = _editorLanguage;

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
        TextPreview.Foreground = _isCodeEditor ? Brushes.Transparent : (Brush)Resources["EditorTextBrush"];
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

        var isMarkdown = string.Equals(vm.ContentKind, "MARKDOWN", StringComparison.OrdinalIgnoreCase);
        if (MarkdownPreviewToggle != null)
            MarkdownPreviewToggle.Visibility = isMarkdown ? Visibility.Visible : Visibility.Collapsed;

        ValidateStructuredContent(text, _editorLanguage, vm.ContentKind);
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

        _ocrText = vm.Entry.OcrText;
        UpdateOcrButtonState();
        StartOcrIfNeeded();
    }

    private void UpdateOcrButtonState()
    {
        if (_vm.Type != EntryType.Image)
        {
            ShowOcrBtn.Visibility = Visibility.Collapsed;
            OcrStatusLabel.Visibility = Visibility.Collapsed;
            return;
        }

        ShowOcrBtn.Visibility = Visibility.Visible;
        var hasOcr = !string.IsNullOrWhiteSpace(_ocrText);
        ShowOcrBtn.IsEnabled = true;
        ShowOcrBtnLabel.Text = _showingOcr
            ? "Bild anzeigen"
            : "OCR-Text anzeigen";

        OcrStatusLabel.Visibility = Visibility.Visible;
        OcrStatusLabel.Text = hasOcr ? "OCR bereit" : "OCR läuft...";
    }

    private void StartOcrIfNeeded()
    {
        if (_ocrRequested || _vm.Type != EntryType.Image || _vm.Entry.ImageData == null) return;
        if (!string.IsNullOrWhiteSpace(_vm.Entry.OcrText)) return;
        if (!OcrService.IsAvailable()) return;

        _ocrRequested = true;
        var data = _vm.Entry.ImageData;
        _ = Task.Run(async () =>
        {
            var text = await OcrService.RecognizeAsync(data);
            Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    _ocrText = text;
                    _vm.Entry.OcrText = text;
                    if (System.Windows.Application.Current is App app)
                        app.Database.UpdateOcr(_vm.Id, text);
                    _vm.RefreshPreview();
                }
                else if (string.IsNullOrWhiteSpace(_vm.Entry.OcrText))
                {
                    _ocrText = "";
                    if (_showingOcr)
                    {
                        TextPreview.Text = "Kein OCR-Text erkannt.";
                        UpdateTextEditorStats();
                    }
                }
                UpdateOcrButtonState();
            });
        });
    }

    private void EditorTool_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string tag &&
            Enum.TryParse(tag, out EditorTool tool))
        {
            ClearCropOverlay();
            CommitInlineTextEditor();
            _activeTool = tool;
            if (PixelateOptions != null)
                PixelateOptions.Visibility = tool == EditorTool.Pixelate ? Visibility.Visible : Visibility.Collapsed;
            if (BlurOptions != null)
                BlurOptions.Visibility = tool == EditorTool.Blur ? Visibility.Visible : Visibility.Collapsed;
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
        if (_selectedAnnotation.Tool is EditorTool.Text or EditorTool.Pixelate or EditorTool.Blur) return;
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

    private void BlurSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        BlurValueLabel.Text = $"{(int)BlurSlider.Value} px";
        if (_selectedAnnotation?.Tool != EditorTool.Blur) return;
        PushUndo();
        _selectedAnnotation.Thickness = BlurSlider.Value;
        RenderAnnotations();
    }

    private void EditorCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_baseImage == null) return;
        if (_pendingCropRect.HasValue) return;
        var point = EditorPoint(e);
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
            SaveCurrentStyle();
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
                Canvas.SetLeft(_draftShape, 0);
                Canvas.SetTop(_draftShape, 0);
                if (_draftShape is Polyline polyline)
                    polyline.Points.Add(point);
                else
                {
                    Canvas.SetLeft(_draftShape, point.X);
                    Canvas.SetTop(_draftShape, point.Y);
                }
            }
        }
        EditorCanvas.CaptureMouse();
    }

    private void EditorCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        var point = EditorPoint(e);

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
        var end = EditorPoint(e);
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
            ShowCropPreview(rect);
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
        else if (_activeTool == EditorTool.Blur)
            annotation.Thickness = BlurSlider.Value;

        if (_activeTool is EditorTool.Arrow or EditorTool.Line)
        {
            annotation.X = start.X;
            annotation.Y = start.Y;
            annotation.W = end.X - start.X;
            annotation.H = end.Y - start.Y;
        }
        else if (_activeTool == EditorTool.Pen)
        {
            annotation.Points = penPoints != null ? SimplifyPath(penPoints) : [];
            if (annotation.Points.Count < 2) return;
        }

        _annotations.Add(annotation);
        if (_activeTool is not (EditorTool.Pixelate or EditorTool.Blur or EditorTool.Highlight or EditorTool.Redact or EditorTool.Crop))
            SaveCurrentStyle();
        RenderAnnotations();
    }

    private Shape? CreateDraftShape(EditorTool tool)
    {
        var stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(CurrentColor()));
        var thickness = StrokeSlider.Value;
        return tool switch
        {
            EditorTool.Arrow or EditorTool.Line => new Line { Stroke = stroke, StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round },
            EditorTool.Rectangle or EditorTool.Crop => new Rectangle { RadiusX = tool == EditorTool.Rectangle ? 7 : 0, RadiusY = tool == EditorTool.Rectangle ? 7 : 0, Stroke = stroke, StrokeThickness = thickness, StrokeDashArray = tool == EditorTool.Crop ? new DoubleCollection([4, 3]) : null, Fill = Brushes.Transparent, Effect = tool == EditorTool.Rectangle ? AnnotationShadow(thickness) : null },
            EditorTool.Ellipse => new Ellipse { Stroke = stroke, StrokeThickness = thickness, Fill = Brushes.Transparent },
            EditorTool.Highlight => new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(90, 255, 235, 59)), Stroke = Brushes.Transparent },
            EditorTool.Redact => new Rectangle { Fill = Brushes.Black, Stroke = Brushes.Black },
            EditorTool.Pixelate => new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(170, 80, 80, 80)), Stroke = Brushes.Black, StrokeDashArray = new DoubleCollection([2, 2]) },
            EditorTool.Blur => new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(40, 80, 160, 220)), Stroke = stroke, StrokeThickness = 1, StrokeDashArray = new DoubleCollection([4, 3]) },
            EditorTool.Pen => new Polyline { Stroke = stroke, StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round },
            _ => new Rectangle { Stroke = stroke, StrokeThickness = thickness, Fill = Brushes.Transparent },
        };
    }

    private void RenderAnnotations()
    {
        for (var i = EditorCanvas.Children.Count - 1; i >= 0; i--)
        {
            var child = EditorCanvas.Children[i];
            if (ReferenceEquals(child, ImagePreview)) continue;
            if (ReferenceEquals(child, _inlineTextEditor)) continue;
            if (_cropOverlayElements.Count > 0 && _cropOverlayElements.Contains((UIElement)child)) continue;
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
                element = new Rectangle { Width = a.W, Height = a.H, RadiusX = 7, RadiusY = 7, Stroke = color, StrokeThickness = a.Thickness, Fill = Brushes.Transparent, Effect = AnnotationShadow(a.Thickness) };
                Canvas.SetLeft(element, a.X); Canvas.SetTop(element, a.Y);
                break;
            case EditorTool.Ellipse:
                element = new Ellipse { Width = a.W, Height = a.H, Stroke = color, StrokeThickness = a.Thickness, Fill = Brushes.Transparent, Effect = AnnotationShadow(a.Thickness) };
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
                element = BlurBlock(a);
                break;
            case EditorTool.Line:
                element = new Line { X1 = a.X, Y1 = a.Y, X2 = a.X + a.W, Y2 = a.Y + a.H, Stroke = color, StrokeThickness = a.Thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Effect = AnnotationShadow(a.Thickness) };
                break;
            case EditorTool.Arrow:
                element = ArrowPath(a, color);
                break;
            case EditorTool.Pen:
                element = new Polyline { Points = new PointCollection(a.Points), Stroke = color, StrokeThickness = a.Thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Effect = AnnotationShadow(a.Thickness) };
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
                    Effect = AnnotationShadow(4),
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
                    Effect = AnnotationShadow(4),
                };
                Canvas.SetLeft(element, a.X); Canvas.SetTop(element, a.Y);
                break;
            default:
                element = new Rectangle { Width = a.W, Height = a.H, Stroke = color, StrokeThickness = a.Thickness };
                Canvas.SetLeft(element, a.X); Canvas.SetTop(element, a.Y);
                break;
        }

        if (ReferenceEquals(a, _selectedAnnotation) && a.Tool != EditorTool.Blur)
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

    private FrameworkElement BlurBlock(EditorAnnotation a)
    {
        var imageRect = new Rect(Canvas.GetLeft(ImagePreview), Canvas.GetTop(ImagePreview), ImagePreview.Width, ImagePreview.Height);
        var annotationRect = new Rect(a.X, a.Y, a.W, a.H);
        var visibleRect = annotationRect;
        visibleRect.Intersect(imageRect);
        if (_baseImage == null || visibleRect.IsEmpty || ImagePreview.Width <= 0 || ImagePreview.Height <= 0)
        {
            var fallback = new Rectangle
            {
                Width = a.W,
                Height = a.H,
                Fill = new SolidColorBrush(Color.FromArgb(90, 180, 190, 200)),
            };
            Canvas.SetLeft(fallback, a.X);
            Canvas.SetTop(fallback, a.Y);
            return fallback;
        }

        var scaleX = _baseImage.PixelWidth / ImagePreview.Width;
        var scaleY = _baseImage.PixelHeight / ImagePreview.Height;
        var sourceX = (visibleRect.X - imageRect.X) * scaleX;
        var sourceY = (visibleRect.Y - imageRect.Y) * scaleY;
        var sourceW = visibleRect.Width * scaleX;
        var sourceH = visibleRect.Height * scaleY;
        var crop = new Int32Rect(
            Math.Clamp((int)Math.Floor(sourceX), 0, _baseImage.PixelWidth - 1),
            Math.Clamp((int)Math.Floor(sourceY), 0, _baseImage.PixelHeight - 1),
            Math.Max(1, Math.Min(_baseImage.PixelWidth - (int)Math.Floor(sourceX), (int)Math.Ceiling(sourceW))),
            Math.Max(1, Math.Min(_baseImage.PixelHeight - (int)Math.Floor(sourceY), (int)Math.Ceiling(sourceH))));

        var source = new CroppedBitmap(_baseImage, crop);
        var img = new System.Windows.Controls.Image
        {
            Source = source,
            Width = visibleRect.Width,
            Height = visibleRect.Height,
            Stretch = Stretch.Fill,
            Effect = new System.Windows.Media.Effects.BlurEffect
            {
                Radius = Math.Clamp(a.Thickness, 3, 32),
                KernelType = System.Windows.Media.Effects.KernelType.Gaussian,
                RenderingBias = System.Windows.Media.Effects.RenderingBias.Quality,
            },
            Clip = new RectangleGeometry(new Rect(0, 0, visibleRect.Width, visibleRect.Height)),
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        Canvas.SetLeft(img, visibleRect.X);
        Canvas.SetTop(img, visibleRect.Y);
        return img;
    }

    private static System.Windows.Media.Effects.DropShadowEffect AnnotationShadow(double thickness)
        => new()
        {
            Color = Colors.Black,
            BlurRadius = Math.Max(8, thickness * 2.8),
            ShadowDepth = Math.Max(2, thickness * 0.65),
            Opacity = 0.28,
        };

    private static Path ArrowPath(EditorAnnotation a, Brush color)
    {
        var start = new Point(a.X, a.Y);
        var end = new Point(a.X + a.W, a.Y + a.H);
        var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        var length = Math.Max(1, Math.Sqrt(a.W * a.W + a.H * a.H));
        var ux = Math.Cos(angle);
        var uy = Math.Sin(angle);
        var nx = -uy;
        var ny = ux;
        var head = Math.Clamp(length * 0.22, Math.Max(16, a.Thickness * 4), Math.Max(22, a.Thickness * 7));
        var bodyEnd = new Point(end.X - ux * head * 0.35, end.Y - uy * head * 0.35);
        var curve = Math.Clamp(length * 0.14, 0, 34) * (a.W * a.H >= 0 ? -1 : 1);
        var control = new Point(
            start.X + a.W * 0.52 + nx * curve,
            start.Y + a.H * 0.52 + ny * curve);
        var baseCenter = new Point(end.X - ux * head, end.Y - uy * head);
        var left = new Point(baseCenter.X + nx * head * 0.42, baseCenter.Y + ny * head * 0.42);
        var right = new Point(baseCenter.X - nx * head * 0.42, baseCenter.Y - ny * head * 0.42);

        var geo = new GeometryGroup();
        var body = new PathGeometry();
        var bodyFigure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        bodyFigure.Segments.Add(new QuadraticBezierSegment(control, bodyEnd, true));
        body.Figures.Add(bodyFigure);
        geo.Children.Add(body);

        var headGeometry = new PathGeometry();
        var headFigure = new PathFigure { StartPoint = end, IsClosed = true, IsFilled = true };
        headFigure.Segments.Add(new LineSegment(left, true));
        headFigure.Segments.Add(new LineSegment(right, true));
        headGeometry.Figures.Add(headFigure);
        geo.Children.Add(headGeometry);

        return new Path
        {
            Data = geo,
            Stroke = color,
            Fill = color,
            StrokeThickness = a.Thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Effect = AnnotationShadow(a.Thickness),
        };
    }

    private void Annotation_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled) return;
        if (_activeTool != EditorTool.Select || (sender as FrameworkElement)?.Tag is not EditorAnnotation annotation)
            return;

        if (!ReferenceEquals(_selectedAnnotation, annotation))
            _selectedAnnotation = annotation;
        PushUndo();
        _movingAnnotation = annotation;
        _moveStart = EditorPoint(e);
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

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (ImageBorder.Visibility != Visibility.Visible) return;
        if (_inlineTextBox != null) return;

        if (_pendingCropRect.HasValue)
        {
            if (e.Key == Key.Return) { CommitCropPreview(); e.Handled = true; return; }
            if (e.Key == Key.Escape) { CancelCropPreview(); e.Handled = true; return; }
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (e.Key == Key.Z) { EditorUndo_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Y) { EditorRedo_Click(sender, e); e.Handled = true; }
    }

    private void EditorUndo_Click(object sender, RoutedEventArgs e)
    {
        CancelInlineTextEditor();
        ClearCropOverlay();
        if (_undo.Count == 0) return;
        _redo.Push(CreateSnapshot());
        RestoreSnapshot(_undo.Pop());
    }

    private void EditorRedo_Click(object sender, RoutedEventArgs e)
    {
        CancelInlineTextEditor();
        ClearCropOverlay();
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
        ApplyResize(size.Value.Width, size.Value.Height);
    }

    private RenderTargetBitmap? RenderEditedImage()
    {
        CommitInlineTextEditor();
        if (_baseImage == null || EditorCanvas.Width <= 0 || EditorCanvas.Height <= 0) return null;
        var width = Math.Max(1, (int)Math.Ceiling(EditorCanvas.Width));
        var height = Math.Max(1, (int)Math.Ceiling(EditorCanvas.Height));
        var exportCanvas = new Canvas
        {
            Width = width,
            Height = height,
            Background = Brushes.White,
            Clip = new RectangleGeometry(new Rect(0, 0, width, height)),
        };

        var image = new System.Windows.Controls.Image
        {
            Source = ImageUtils.RepairFullyTransparentColorBitmap(_baseImage),
            Width = ImagePreview.Width,
            Height = ImagePreview.Height,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        Canvas.SetLeft(image, Canvas.GetLeft(ImagePreview));
        Canvas.SetTop(image, Canvas.GetTop(ImagePreview));
        exportCanvas.Children.Add(image);

        foreach (var annotation in _annotations)
        {
            var element = CreateElement(annotation);
            exportCanvas.Children.Add(element);
        }

        exportCanvas.Measure(new System.Windows.Size(width, height));
        exportCanvas.Arrange(new Rect(0, 0, width, height));
        exportCanvas.UpdateLayout();

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(exportCanvas);
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

    private void ShowCropPreview(Rect cropRect)
    {
        ClearCropOverlay();
        _pendingCropRect = cropRect;

        var cw = EditorCanvas.Width;
        var ch = EditorCanvas.Height;
        var overlay = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));

        void AddDim(double x, double y, double w, double h)
        {
            if (w <= 0 || h <= 0) return;
            var r = new Rectangle { Width = w, Height = h, Fill = overlay };
            Canvas.SetLeft(r, x);
            Canvas.SetTop(r, y);
            System.Windows.Controls.Panel.SetZIndex(r, 900);
            EditorCanvas.Children.Add(r);
            _cropOverlayElements.Add(r);
        }

        AddDim(0, 0, cw, cropRect.Top);
        AddDim(0, cropRect.Bottom, cw, ch - cropRect.Bottom);
        AddDim(0, cropRect.Top, cropRect.Left, cropRect.Height);
        AddDim(cropRect.Right, cropRect.Top, cw - cropRect.Right, cropRect.Height);

        var border = new Rectangle
        {
            Width = cropRect.Width,
            Height = cropRect.Height,
            Stroke = Brushes.White,
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection([5, 3]),
            Fill = Brushes.Transparent,
        };
        Canvas.SetLeft(border, cropRect.Left);
        Canvas.SetTop(border, cropRect.Top);
        System.Windows.Controls.Panel.SetZIndex(border, 901);
        EditorCanvas.Children.Add(border);
        _cropOverlayElements.Add(border);

        var confirmBtn = new WpfButton
        {
            Content = "Zuschneiden",
            Padding = new Thickness(12, 5, 12, 5),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Cursor = WpfCursors.Hand,
        };
        confirmBtn.Click += (_, _) => CommitCropPreview();

        var cancelBtn = new WpfButton
        {
            Content = "Abbrechen",
            Padding = new Thickness(10, 5, 10, 5),
            FontSize = 12,
            Cursor = WpfCursors.Hand,
            Margin = new Thickness(6, 0, 0, 0),
        };
        cancelBtn.Click += (_, _) => CancelCropPreview();

        var btnBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(235, 16, 20, 24)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(5),
            Child = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                Children = { confirmBtn, cancelBtn },
            },
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 2, Opacity = 0.55 },
        };

        var btnX = Math.Clamp(cropRect.X + cropRect.Width / 2 - 90, 4, Math.Max(4, cw - 200));
        var btnY = cropRect.Bottom + 10 < ch - 44
            ? cropRect.Bottom + 10
            : cropRect.Top > 50
                ? cropRect.Top - 48
                : Math.Clamp(cropRect.Y + cropRect.Height / 2 - 18, 4, ch - 44);

        Canvas.SetLeft(btnBorder, btnX);
        Canvas.SetTop(btnBorder, btnY);
        System.Windows.Controls.Panel.SetZIndex(btnBorder, 902);
        EditorCanvas.Children.Add(btnBorder);
        _cropOverlayElements.Add(btnBorder);
    }

    private void CommitCropPreview()
    {
        if (!_pendingCropRect.HasValue) return;
        var rect = _pendingCropRect.Value;
        ClearCropOverlay();
        PushUndo();
        CropCanvas(rect);
        RenderAnnotations();
    }

    private void CancelCropPreview()
    {
        if (!_pendingCropRect.HasValue) return;
        ClearCropOverlay();
        RenderAnnotations();
    }

    private void ClearCropOverlay()
    {
        foreach (var el in _cropOverlayElements)
            EditorCanvas.Children.Remove(el);
        _cropOverlayElements.Clear();
        _pendingCropRect = null;
    }

    private static List<Point> SimplifyPath(List<Point> points, double epsilon = 2.0)
    {
        if (points.Count <= 2) return points;
        var maxDist = 0.0;
        var maxIdx = 0;
        var start = points[0];
        var end = points[^1];
        for (var i = 1; i < points.Count - 1; i++)
        {
            var d = PerpendicularDistance(points[i], start, end);
            if (d > maxDist) { maxDist = d; maxIdx = i; }
        }
        if (maxDist <= epsilon) return [start, end];
        var left = SimplifyPath(points[..(maxIdx + 1)], epsilon);
        var right = SimplifyPath(points[maxIdx..], epsilon);
        return [.. left[..^1], .. right];
    }

    private static double PerpendicularDistance(Point p, Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        if (dx == 0 && dy == 0)
            return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        var t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy);
        var nx = a.X + t * dx;
        var ny = a.Y + t * dy;
        return Math.Sqrt((p.X - nx) * (p.X - nx) + (p.Y - ny) * (p.Y - ny));
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

    private Point EditorPoint(MouseEventArgs e)
    {
        try
        {
            var transform = EditorCanvas.TransformToAncestor(ImageScrollViewer);
            var inverse = transform.Inverse;
            if (inverse != null)
                return ClampPoint(inverse.Transform(e.GetPosition(ImageScrollViewer)));
        }
        catch
        {
            // Fallback for transient layout states while the editor is opening.
        }

        return ClampPoint(e.GetPosition(EditorCanvas));
    }

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
            SaveCurrentStyle();
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
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
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
        var syntax = SyntaxProfileFor(_editorLanguage);
        var lines = text.Replace("\r", "").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            AppendHighlightedLine(CodeHighlightOverlay, lines[i], syntax);
            if (i < lines.Length - 1)
                CodeHighlightOverlay.Inlines.Add(new LineBreak());
        }
    }

    private static void AppendHighlightedLine(TextBlock target, string line, SyntaxProfile syntax)
    {
        var directiveStart = DirectiveStartIndex(line, syntax.Language);
        if (directiveStart >= 0)
        {
            if (directiveStart > 0)
                AddRun(target, line[..directiveStart]);
            AddRun(target, line[directiveStart..], SyntaxDirectiveBrush);
            return;
        }

        var commentStart = FindLineCommentStart(line, syntax.Language);
        var code = commentStart >= 0 ? line[..commentStart] : line;
        AppendCodeTokens(target, code, syntax);
        if (commentStart >= 0)
            AddRun(target, line[commentStart..], SyntaxCommentBrush);
    }

    private static void AppendCodeTokens(TextBlock target, string text, SyntaxProfile syntax)
    {
        foreach (Match match in Regex.Matches(text, SyntaxTokenPattern, RegexOptions.None, UiRegexTimeout))
        {
            var token = match.Value;
            AddRun(target, token, SyntaxBrushFor(token, syntax));
        }
    }

    private static Brush? SyntaxBrushFor(string token, SyntaxProfile syntax)
    {
        if (token.Length == 0 || string.IsNullOrWhiteSpace(token))
            return null;
        if (IsStringToken(token))
            return SyntaxStringBrush;
        if (IsNumberToken(token))
            return SyntaxNumberBrush;
        if (IsHexColorToken(token))
            return SyntaxLiteralBrush;
        if (syntax.Literals.Contains(token))
            return SyntaxLiteralBrush;
        if (syntax.Keywords.Contains(token))
            return SyntaxKeywordBrush;
        if (syntax.Types.Contains(token))
            return SyntaxTypeBrush;
        if (syntax.Builtins.Contains(token))
            return SyntaxBuiltinBrush;
        if (syntax.Attributes.Contains(token) || IsVariableToken(token, syntax.Language))
            return SyntaxAttributeBrush;
        if (IsOperatorToken(token))
            return SyntaxOperatorBrush;
        return null;
    }

    private static void AddRun(TextBlock target, string text, Brush? foreground = null)
    {
        var run = new Run(text);
        if (foreground != null)
            run.Foreground = foreground;
        target.Inlines.Add(run);
    }

    private static int DirectiveStartIndex(string line, string? language)
    {
        var start = 0;
        while (start < line.Length && char.IsWhiteSpace(line[start]))
            start++;
        if (start >= line.Length || line[start] != '#')
            return -1;
        return language is "C#" or "C++" ? start : -1;
    }

    private static int FindLineCommentStart(string line, string? language)
    {
        var slashComments = language is null or "Code" or "JavaScript" or "TypeScript" or "C#" or "C++" or "Go" or "Rust" or "PHP";
        var hashComments = language is null or "Code" or "Python" or "Bash" or "PowerShell" or "Ruby" or "YAML";
        var sqlComments = language == "SQL";
        var blockComments = slashComments || language == "CSS";

        var inSingle = false;
        var inDouble = false;
        var inBacktick = false;
        var escaped = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if ((inSingle || inDouble || inBacktick) && c == '\\')
            {
                escaped = true;
                continue;
            }

            if (!inDouble && !inBacktick && c == '\'')
            {
                inSingle = !inSingle;
                continue;
            }
            if (!inSingle && !inBacktick && c == '"')
            {
                inDouble = !inDouble;
                continue;
            }
            if (!inSingle && !inDouble && c == '`')
            {
                inBacktick = !inBacktick;
                continue;
            }
            if (inSingle || inDouble || inBacktick)
                continue;

            if (slashComments && i + 1 < line.Length && line[i] == '/' && line[i + 1] == '/')
                return i;
            if (blockComments && i + 1 < line.Length && line[i] == '/' && line[i + 1] == '*')
                return i;
            if (sqlComments && i + 1 < line.Length && line[i] == '-' && line[i + 1] == '-')
                return i;
            if (hashComments && line[i] == '#')
                return i;
        }
        return -1;
    }

    private static bool IsStringToken(string token)
        => token.StartsWith("\"", StringComparison.Ordinal)
           || token.StartsWith("'", StringComparison.Ordinal)
           || token.StartsWith("`", StringComparison.Ordinal);

    private static bool IsNumberToken(string token)
        => char.IsDigit(token[0]);

    private static bool IsHexColorToken(string token)
        => token.Length is 4 or 5 or 7 or 9
           && token[0] == '#'
           && token.Skip(1).All(Uri.IsHexDigit);

    private static bool IsVariableToken(string token, string? language)
        => token.StartsWith("$", StringComparison.Ordinal)
           && language is "PowerShell" or "Bash" or "PHP";

    private static bool IsOperatorToken(string token)
        => token.All(c => "{}[]().,;:+-*/%=!<>|&?~^#".IndexOf(c) >= 0);

    private static SyntaxProfile SyntaxProfileFor(string? language)
    {
        var exact = StringComparer.Ordinal;
        var ignore = StringComparer.OrdinalIgnoreCase;

        return language switch
        {
            "HTML" => Profile(language, ignore,
                keywords: ["doctype"],
                types: ["html", "head", "body", "main", "section", "article", "header", "footer", "nav", "div", "span", "button", "input", "form", "label", "script", "style", "link", "meta", "title", "img", "a", "ul", "ol", "li", "table", "thead", "tbody", "tr", "th", "td"],
                attributes: ["class", "id", "href", "src", "type", "role", "aria", "alt", "title", "name", "value", "data", "style", "rel", "target", "placeholder", "disabled", "checked"]),
            "XML" => Profile(language, ignore,
                keywords: ["xml", "version", "encoding", "xmlns", "schema"],
                types: ["configuration", "appSettings", "connectionStrings", "package", "project", "itemGroup", "propertyGroup"],
                attributes: ["name", "key", "value", "type", "include", "remove", "condition"]),
            "JSON" => Profile(language, exact,
                literals: ["true", "false", "null"]),
            "CSS" => Profile(language, ignore,
                keywords: ["display", "grid", "flex", "block", "inline", "none", "position", "absolute", "relative", "fixed", "color", "background", "margin", "padding", "border", "font", "width", "height", "media", "container", "supports", "import", "keyframes"],
                builtins: ["rgb", "rgba", "hsl", "hsla", "var", "calc", "min", "max", "clamp", "url", "linear-gradient", "repeat", "minmax"]),
            "SQL" => Profile(language, ignore,
                keywords: ["SELECT", "FROM", "WHERE", "INSERT", "UPDATE", "DELETE", "CREATE", "ALTER", "DROP", "TABLE", "VIEW", "INDEX", "AND", "OR", "NOT", "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "ON", "ORDER", "GROUP", "BY", "HAVING", "AS", "IN", "IS", "LIKE", "DISTINCT", "LIMIT", "OFFSET", "UNION", "VALUES", "SET", "INTO", "PRIMARY", "KEY", "FOREIGN"],
                types: ["INTEGER", "INT", "TEXT", "VARCHAR", "CHAR", "DATETIME", "DATE", "BOOLEAN", "REAL", "BLOB", "NUMERIC"],
                builtins: ["COUNT", "SUM", "AVG", "MIN", "MAX", "COALESCE", "CAST", "CONVERT", "LOWER", "UPPER", "TRIM"],
                literals: ["NULL", "TRUE", "FALSE"]),
            "JavaScript" or "TypeScript" => Profile(language, exact,
                keywords: ["function", "return", "const", "let", "var", "if", "else", "for", "while", "do", "switch", "case", "break", "continue", "class", "extends", "new", "import", "from", "export", "default", "async", "await", "try", "catch", "finally", "throw", "typeof", "instanceof", "in", "of", "yield", "delete", "void", "get", "set", "static", "super", "this"],
                types: ["string", "number", "boolean", "unknown", "any", "never", "void", "object", "Promise", "Array", "Record", "Map", "Set", "Date", "Error"],
                builtins: ["console", "log", "JSON", "Math", "Object", "String", "Number", "Boolean", "fetch", "window", "document", "setTimeout", "setInterval"],
                literals: ["true", "false", "null", "undefined", "NaN", "Infinity"]),
            "C#" => Profile(language, exact,
                keywords: ["using", "namespace", "public", "private", "protected", "internal", "class", "record", "struct", "interface", "enum", "static", "void", "async", "await", "return", "new", "sealed", "virtual", "override", "abstract", "event", "delegate", "typeof", "nameof", "where", "readonly", "required", "partial", "if", "else", "for", "foreach", "while", "switch", "case", "break", "continue", "try", "catch", "finally", "throw", "get", "set", "init", "var", "this", "base", "is", "as"],
                types: ["string", "int", "bool", "double", "decimal", "float", "long", "short", "byte", "object", "dynamic", "Task", "List", "Dictionary", "IEnumerable", "DateTime", "Guid"],
                builtins: ["Console", "Math", "File", "Path", "Enumerable", "Regex", "JsonSerializer"],
                literals: ["true", "false", "null", "default"]),
            "C++" => Profile(language, exact,
                keywords: ["include", "using", "namespace", "class", "struct", "public", "private", "protected", "virtual", "template", "typename", "auto", "const", "constexpr", "static", "return", "new", "delete", "if", "else", "for", "while", "switch", "case", "break", "continue", "try", "catch", "throw", "noexcept", "override", "final"],
                types: ["int", "double", "float", "bool", "void", "char", "long", "short", "unsigned", "signed", "size_t", "std", "string", "vector", "map", "unordered_map"],
                builtins: ["cout", "cin", "endl", "printf", "scanf", "make_unique", "make_shared"],
                literals: ["true", "false", "nullptr", "NULL"]),
            "Go" => Profile(language, exact,
                keywords: ["package", "import", "func", "return", "var", "const", "type", "struct", "interface", "defer", "go", "chan", "select", "if", "else", "for", "range", "switch", "case", "break", "continue", "fallthrough", "map"],
                types: ["string", "int", "int64", "float64", "bool", "error", "byte", "rune", "any"],
                builtins: ["make", "new", "append", "copy", "len", "cap", "panic", "recover", "println", "fmt"],
                literals: ["true", "false", "nil", "iota"]),
            "Rust" => Profile(language, exact,
                keywords: ["use", "mod", "pub", "fn", "let", "mut", "const", "static", "struct", "enum", "impl", "trait", "match", "if", "else", "loop", "while", "for", "in", "return", "async", "await", "move", "ref", "crate", "super", "self", "Self", "where", "unsafe"],
                types: ["String", "Vec", "Option", "Result", "HashMap", "bool", "i32", "i64", "u32", "u64", "usize", "str"],
                builtins: ["println", "format", "Some", "None", "Ok", "Err", "Default", "Clone"],
                literals: ["true", "false"]),
            "Bash" => Profile(language, exact,
                keywords: ["if", "then", "else", "elif", "fi", "for", "while", "do", "done", "case", "esac", "function", "return", "in"],
                builtins: ["echo", "export", "local", "readonly", "cd", "pwd", "test", "grep", "sed", "awk", "printf", "source"],
                literals: ["true", "false"]),
            "PHP" => Profile(language, exact,
                keywords: ["function", "return", "class", "public", "private", "protected", "static", "new", "echo", "if", "else", "elseif", "foreach", "while", "try", "catch", "finally", "throw", "namespace", "use", "extends", "implements"],
                builtins: ["array", "count", "isset", "empty", "json_encode", "json_decode", "strlen", "var_dump"],
                literals: ["true", "false", "null"]),
            "Ruby" => Profile(language, exact,
                keywords: ["def", "end", "class", "module", "require", "include", "return", "if", "elsif", "else", "unless", "while", "do", "begin", "rescue", "ensure", "raise", "yield", "self"],
                builtins: ["puts", "print", "attr_reader", "attr_writer", "attr_accessor", "Array", "Hash", "String"],
                literals: ["nil", "true", "false"]),
            "Python" => Profile(language, exact,
                keywords: ["def", "class", "import", "from", "return", "if", "elif", "else", "for", "while", "with", "as", "pass", "lambda", "yield", "in", "not", "and", "or", "raise", "try", "except", "finally", "assert", "break", "continue", "global", "nonlocal", "async", "await", "del", "is"],
                builtins: ["print", "len", "range", "enumerate", "zip", "open", "list", "dict", "set", "tuple", "str", "int", "float", "bool", "type", "super", "self", "cls"],
                literals: ["None", "True", "False"]),
            "PowerShell" => Profile(language, ignore,
                keywords: ["param", "function", "foreach", "where", "if", "else", "elseif", "try", "catch", "finally", "switch", "return", "begin", "process", "end", "filter", "class", "using", "namespace"],
                builtins: ["Get", "Set", "Invoke", "Write", "Read", "Start", "Stop", "New", "Remove", "Select", "Where", "ForEach", "Out", "Test", "Join", "Split"],
                literals: ["$true", "$false", "$null"]),
            "YAML" => Profile(language, ignore,
                keywords: ["true", "false", "null", "yes", "no", "on", "off"],
                attributes: ["apiVersion", "kind", "metadata", "spec", "services", "steps", "name", "version"]),
            _ => Profile("Code", ignore,
                keywords: ["if", "else", "for", "while", "return", "class", "new"],
                literals: ["true", "false", "null"]),
        };
    }

    private static SyntaxProfile Profile(
        string? language,
        StringComparer comparer,
        string[]? keywords = null,
        string[]? types = null,
        string[]? builtins = null,
        string[]? literals = null,
        string[]? attributes = null)
        => new(
            language,
            SyntaxSet(comparer, keywords),
            SyntaxSet(comparer, types),
            SyntaxSet(comparer, builtins),
            SyntaxSet(comparer, literals),
            SyntaxSet(comparer, attributes));

    private static HashSet<string> SyntaxSet(StringComparer comparer, string[]? values)
        => values is { Length: > 0 }
            ? new HashSet<string>(values, comparer)
            : new HashSet<string>(comparer);

    private static SolidColorBrush SyntaxBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private sealed record SyntaxProfile(
        string? Language,
        HashSet<string> Keywords,
        HashSet<string> Types,
        HashSet<string> Builtins,
        HashSet<string> Literals,
        HashSet<string> Attributes);

    private const string SyntaxTokenPattern =
        @"#[0-9A-Fa-f]{3,8}\b|`(?:\\.|[^`])*`|""(?:\\.|[^""])*""|'(?:\\.|[^'])*'|\b0x[0-9A-Fa-f]+\b|\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b|\$[A-Za-z_][A-Za-z0-9_]*|@[A-Za-z_][A-Za-z0-9_]*|\b[A-Za-z_][A-Za-z0-9_]*\b|\s+|.";

    private static readonly Brush SyntaxKeywordBrush = SyntaxBrush(197, 134, 232);
    private static readonly Brush SyntaxTypeBrush = SyntaxBrush(78, 201, 176);
    private static readonly Brush SyntaxBuiltinBrush = SyntaxBrush(220, 220, 170);
    private static readonly Brush SyntaxLiteralBrush = SyntaxBrush(86, 156, 214);
    private static readonly Brush SyntaxStringBrush = SyntaxBrush(241, 196, 83);
    private static readonly Brush SyntaxNumberBrush = SyntaxBrush(181, 206, 168);
    private static readonly Brush SyntaxCommentBrush = SyntaxBrush(93, 181, 89);
    private static readonly Brush SyntaxAttributeBrush = SyntaxBrush(156, 220, 254);
    private static readonly Brush SyntaxDirectiveBrush = SyntaxBrush(86, 156, 214);
    private static readonly Brush SyntaxOperatorBrush = SyntaxBrush(128, 144, 160);

    private void TextPreview_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateTextEditorStats();
        UpdateCodeHighlightOverlay();
        if (_isMarkdownPreviewActive)
            RefreshMarkdownPreview();
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

    private bool HasUnsavedTextChanges()
        => _textPreviewEditable
           && TextPreview != null
           && !TextPreview.IsReadOnly
           && (!string.Equals(TextPreview.Text, _savedTextPreview, StringComparison.Ordinal)
               || !string.Equals(_editorLanguage, _savedEditorLanguage, StringComparison.Ordinal));

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
        QueueTextEditorSync();
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
        _savedEditorLanguage = _editorLanguage;
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
        if (_showingOcr)
        {
            ShowOcrTextView();
        }
        else
        {
            TextPreview.Visibility  = Visibility.Collapsed;
            TextEditorPanel.Visibility = Visibility.Collapsed;
            ImageBorder.Visibility  = Visibility.Visible;
            UpdateOcrButtonState();
        }
    }

    private void ShowOcrTextView()
    {
        ImageBorder.Visibility  = Visibility.Collapsed;
        TextEditorPanel.Visibility = Visibility.Visible;
        TextPreview.Text = !string.IsNullOrWhiteSpace(_ocrText)
            ? _ocrText
            : _ocrRequested ? "OCR läuft..." : "Noch kein OCR-Text vorhanden.";
        TextPreview.Visibility  = Visibility.Visible;
        TextPreview.IsReadOnly  = true;
        TextEditorTitleLabel.Text = "OCR-Text aus Zwischenablage";
        TextModeLabel.Text = "OCR";
        EditorLanguageBox.Visibility = Visibility.Collapsed;
        SaveTextBtn.Visibility = Visibility.Collapsed;
        UndoTextBtn.Visibility = Visibility.Collapsed;
        _textPreviewEditable = false;
        UpdateTextEditorStats();
        UpdateOcrButtonState();
        StartOcrIfNeeded();
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

    private void OpenUrl_Click(object sender, RoutedEventArgs e)
        => OpenUrl(_vm.Content);

    private static void OpenUrl(string? url)
    {
        if (!UrlPreviewService.IsUrl(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url!)
            {
                UseShellExecute = true,
            });
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
        var image = RenderEditedImage();
        if (image == null) return;
        SaveImageAsJpg(image);
    }

    private void SaveTextAs()
    {
        var text = TextPreview.Visibility == Visibility.Visible
            ? TextPreview.Text
            : _vm.Content ?? _vm.Entry.OcrText ?? _vm.HexColor ?? "";
        if (string.IsNullOrEmpty(text)) return;

        var language = _isCodeEditor ? _editorLanguage : _vm.Language;
        var formats = BuildTextExportFormats(_vm.Type, language, _vm.ContentKind, text);
        var extension = formats[0].Extension;
        var dlg = new SaveFileDialog
        {
            Title = "Text speichern unter",
            Filter = BuildExportFilter(formats),
            DefaultExt = "." + extension,
            FileName = DefaultExportName(extension),
        };
        if (dlg.ShowDialog(this) != true) return;

        var format = ResolveSelectedExportFormat(formats, dlg.FilterIndex, dlg.FileName);
        var output = FormatExportContent(text, format.Extension, _vm.Type, language, _vm.ContentKind, _vm.UrlTitle);
        try
        {
            File.WriteAllText(dlg.FileName, output);
        }
        catch (Exception ex)
        {
            App.LogCrash(ex);
            System.Windows.MessageBox.Show(
                this,
                $"Die Datei konnte nicht gespeichert werden:\n{ex.Message}",
                "Speichern fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    public static IReadOnlyList<ExportFormat> BuildTextExportFormats(
        EntryType type,
        string? language,
        string? contentKind,
        string text)
    {
        var formats = new List<ExportFormat>();

        void Add(string label, string extension)
        {
            if (formats.Any(f => string.Equals(f.Extension, extension, StringComparison.OrdinalIgnoreCase))) return;
            formats.Add(new ExportFormat(label, extension));
        }

        if (type == EntryType.Url || UrlPreviewService.IsUrl(text))
        {
            Add("Windows-Internetverknuepfung", "url");
            Add("Textdatei", "txt");
            Add("Markdown-Link", "md");
            Add("HTML-Link", "html");
            Add("JSON-Datei", "json");
            return formats;
        }

        var defaultExtension = ExportExtension(language, contentKind);
        Add(ExportLabel(defaultExtension), defaultExtension);

        Add("Textdatei", "txt");
        if (type == EntryType.Color)
            Add("CSS-Farbe", "css");
        Add("Markdown-Datei", "md");
        Add("HTML-Dokument", "html");
        Add("JSON-Datei", "json");
        Add("XML-Datei", "xml");
        Add("CSV-Datei", "csv");
        Add("JavaScript-Datei", "js");
        Add("TypeScript-Datei", "ts");
        Add("CSS-Datei", "css");
        Add("SQL-Datei", "sql");
        Add("PowerShell-Datei", "ps1");
        Add("Shell-Skript", "sh");
        Add("C#-Datei", "cs");
        Add("Python-Datei", "py");
        Add("YAML-Datei", "yaml");

        return formats;
    }

    private static string ExportExtension(string? language, string? contentKind)
    {
        if (string.Equals(contentKind, "MARKDOWN", StringComparison.OrdinalIgnoreCase))
            return "md";

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
            _ => "txt",
        };
    }

    public static string BuildExportFilter(IReadOnlyList<ExportFormat> formats)
        => string.Join("|", formats.Select(f => $"{f.Label}|*.{f.Extension}").Append("Alle Dateien|*.*"));

    public static ExportFormat ResolveSelectedExportFormat(
        IReadOnlyList<ExportFormat> formats,
        int filterIndex,
        string fileName)
    {
        var extension = System.IO.Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        var byFileName = formats.FirstOrDefault(f =>
            string.Equals(f.Extension, extension, StringComparison.OrdinalIgnoreCase));
        var index = filterIndex - 1;
        var byFilter = index >= 0 && index < formats.Count ? formats[index] : null;
        if (byFileName != null
            && (byFilter == null || !string.Equals(extension, formats[0].Extension, StringComparison.OrdinalIgnoreCase)))
            return byFileName;

        return byFilter ?? formats[0];
    }

    public static string FormatExportContent(
        string text,
        string extension,
        EntryType type,
        string? language,
        string? contentKind,
        string? title)
    {
        extension = extension.TrimStart('.').ToLowerInvariant();
        if (extension == "url" && UrlPreviewService.IsUrl(text))
            return $"[InternetShortcut]\r\nURL={text.Trim()}\r\n";

        if (extension == "json")
        {
            var payload = new
            {
                Type = type.ToString(),
                Title = title,
                Language = language,
                ContentKind = contentKind,
                Content = text,
                ExportedAt = DateTime.Now,
            };
            return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        }

        if (extension == "html")
        {
            if (string.Equals(language, "HTML", StringComparison.OrdinalIgnoreCase))
                return text;
            if (string.Equals(contentKind, "MARKDOWN", StringComparison.OrdinalIgnoreCase))
                return WrapMarkdownHtml(MarkdownToHtml(text));
            if (type == EntryType.Url && UrlPreviewService.IsUrl(text))
                return BuildUrlHtml(text, title);
            return BuildPlainTextHtml(text, title);
        }

        if (extension == "md" && type == EntryType.Url && UrlPreviewService.IsUrl(text))
            return $"[{EscapeMarkdownLinkText(title ?? text.Trim())}]({text.Trim()}){Environment.NewLine}";

        if (extension == "css" && type == EntryType.Color)
            return $":root {{\r\n  --clipwell-color: {text.Trim()};\r\n}}\r\n";

        return text;
    }

    private static string ExportLabel(string extension)
    {
        return extension switch
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
            "md" => "Markdown-Datei",
            "csv" => "CSV-Datei",
            "yaml" => "YAML-Datei",
            _ => "Textdatei",
        };
    }

    private static string BuildPlainTextHtml(string text, string? title)
    {
        var safeTitle = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(title) ? "Clipwell Export" : title);
        var body = System.Net.WebUtility.HtmlEncode(text).Replace("\r\n", "\n").Replace("\n", "<br>\r\n");
        return $$"""
            <!DOCTYPE html>
            <html lang="de">
            <head>
              <meta charset="utf-8">
              <title>{{safeTitle}}</title>
              <style>
                body { font-family: 'Segoe UI', sans-serif; line-height: 1.55; margin: 2rem; }
                main { white-space: normal; max-width: 80rem; }
              </style>
            </head>
            <body><main>{{body}}</main></body>
            </html>
            """;
    }

    private static string BuildUrlHtml(string url, string? title)
    {
        var safeUrl = System.Net.WebUtility.HtmlEncode(url.Trim());
        var safeTitle = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(title) ? url.Trim() : title);
        return $$"""
            <!DOCTYPE html>
            <html lang="de">
            <head><meta charset="utf-8"><title>{{safeTitle}}</title></head>
            <body><p><a href="{{safeUrl}}">{{safeTitle}}</a></p></body>
            </html>
            """;
    }

    private static string EscapeMarkdownLinkText(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace("[", "\\[")
            .Replace("]", "\\]");
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

    // ── Structured-content validation ───────────────────────────────────────

    private void ValidateStructuredContent(string text, string? language, string? kind)
    {
        if (ValidationLabel == null) return;
        ValidationLabel.Visibility = Visibility.Collapsed;

        var lang = language ?? kind ?? "";
        if (string.Equals(lang, "JSON", StringComparison.OrdinalIgnoreCase))
            ShowValidationResult(ValidateJson(text));
        else if (string.Equals(lang, "XML", StringComparison.OrdinalIgnoreCase)
              || string.Equals(lang, "HTML", StringComparison.OrdinalIgnoreCase)
              || string.Equals(kind, "CODE", StringComparison.OrdinalIgnoreCase) && text.TrimStart().StartsWith('<'))
            ShowValidationResult(ValidateXml(text));
        else if (string.Equals(lang, "YAML", StringComparison.OrdinalIgnoreCase)
              || string.Equals(kind, "YAML", StringComparison.OrdinalIgnoreCase))
            ShowValidationResult(ValidateYaml(text));
    }

    private static string? ValidateJson(string text)
    {
        try { JsonDocument.Parse(text); return null; }
        catch (JsonException ex) { return $"JSON: {ex.Message}"; }
    }

    private static string? ValidateXml(string text)
    {
        try { XDocument.Parse(text); return null; }
        catch (System.Xml.XmlException ex) { return $"XML: Zeile {ex.LineNumber}, Pos {ex.LinePosition}: {ex.Message}"; }
    }

    private static string? ValidateYaml(string text)
    {
        try
        {
            var parser = new Parser(new StringReader(text));
            while (parser.MoveNext()) { }
            return null;
        }
        catch (YamlException ex) { return $"YAML: Zeile {ex.Start.Line}, Pos {ex.Start.Column}: {ex.Message}"; }
    }

    private void ShowValidationResult(string? error)
    {
        if (ValidationLabel == null) return;
        if (error == null)
        {
            ValidationLabel.Text = "Valide";
            ValidationLabel.Foreground = new SolidColorBrush(Color.FromRgb(80, 200, 120));
        }
        else
        {
            ValidationLabel.Text = error;
            ValidationLabel.Foreground = new SolidColorBrush(Color.FromRgb(229, 57, 53));
        }
        ValidationLabel.Visibility = Visibility.Visible;
    }

    // ── Shape / style memory ─────────────────────────────────────────────────

    private void LoadStyleMemory()
    {
        try
        {
            if (!File.Exists(StyleMemoryPath)) return;
            var list = JsonSerializer.Deserialize<List<JsonElement>>(File.ReadAllText(StyleMemoryPath));
            if (list == null) return;
            _recentStyles.Clear();
            foreach (var el in list.Take(3))
            {
                var color = el.GetProperty("Color").GetString() ?? "#E53935";
                var thickness = el.GetProperty("Thickness").GetDouble();
                _recentStyles.Add(new StyleMemory(color, thickness));
            }
        }
        catch { }
        RefreshRecentStylesPanel();
    }

    private void SaveCurrentStyle()
    {
        var current = new StyleMemory(_currentColor, StrokeSlider.Value);
        _recentStyles.RemoveAll(s => s.Color == current.Color && Math.Abs(s.Thickness - current.Thickness) < 0.5);
        _recentStyles.Insert(0, current);
        if (_recentStyles.Count > 3) _recentStyles.RemoveRange(3, _recentStyles.Count - 3);
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            var data = _recentStyles.Select(s => new { s.Color, s.Thickness }).ToList();
            File.WriteAllText(StyleMemoryPath, JsonSerializer.Serialize(data));
        }
        catch { }
        RefreshRecentStylesPanel();
    }

    private void RefreshRecentStylesPanel()
    {
        if (RecentStylesPanel == null) return;
        RecentStylesPanel.Children.Clear();
        var visible = _recentStyles.Count > 0;
        RecentStylesLabel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        RecentStylesPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        foreach (var style in _recentStyles)
        {
            var dotSize = Math.Clamp(style.Thickness * 1.5, 4, 18);
            var dot = new Ellipse
            {
                Width = dotSize,
                Height = dotSize,
                Fill = new SolidColorBrush(ContrastColor(style.Color)),
                IsHitTestVisible = false,
            };
            var btn = new WpfButton
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(0, 0, 4, 4),
                Padding = new Thickness(0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(style.Color)),
                BorderBrush = (Brush)FindResource("EditorBorderBrush"),
                BorderThickness = new Thickness(1),
                Content = dot,
                ToolTip = $"{style.Color} / {(int)style.Thickness} px",
                Tag = style,
            };
            btn.Click += RecentStyle_Click;
            RecentStylesPanel.Children.Add(btn);
        }
    }

    private void RecentStyle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as WpfButton)?.Tag is not StyleMemory style) return;
        _currentColor = style.Color;
        _editorBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(style.Color));
        StrokeSlider.Value = style.Thickness;
        foreach (var rb in EditorColorSwatches.Children.OfType<System.Windows.Controls.RadioButton>())
        {
            if ((string?)rb.Tag == style.Color)
            {
                rb.IsChecked = true;
                break;
            }
        }
    }

    private static Color ContrastColor(string hex)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
            return luminance > 0.5 ? Color.FromRgb(0, 0, 0) : Color.FromRgb(255, 255, 255);
        }
        catch { return Color.FromRgb(255, 255, 255); }
    }

    // ── Markdown preview ─────────────────────────────────────────────────────

    private void MarkdownPreviewToggle_Click(object sender, RoutedEventArgs e)
    {
        _isMarkdownPreviewActive = !_isMarkdownPreviewActive;
        if (_isMarkdownPreviewActive)
            ShowMarkdownPreview();
        else
            HideMarkdownPreview();
    }

    private void ShowMarkdownPreview()
    {
        if (MarkdownBrowser == null) return;
        RefreshMarkdownPreview();
        MarkdownBrowser.Visibility = Visibility.Visible;
        TextPreview.Visibility = Visibility.Collapsed;
        CodeHighlightScrollViewer.Visibility = Visibility.Collapsed;
        if (MarkdownPreviewToggle != null) MarkdownPreviewToggle.Content = "Quelltext";
    }

    private void RefreshMarkdownPreview()
    {
        if (MarkdownBrowser == null) return;
        var html = MarkdownToHtml(TextPreview.Text ?? "");
        MarkdownBrowser.NavigateToString(WrapMarkdownHtml(html));
    }

    private void HideMarkdownPreview()
    {
        if (MarkdownBrowser != null) MarkdownBrowser.Visibility = Visibility.Collapsed;
        TextPreview.Visibility = Visibility.Visible;
        CodeHighlightScrollViewer.Visibility = Visibility.Visible;
        if (MarkdownPreviewToggle != null) MarkdownPreviewToggle.Content = "Vorschau";
    }

    private static string WrapMarkdownHtml(string body)
    {
        var isDark = System.Windows.Application.Current is App app && app.IsEffectiveDarkTheme;
        var bg     = isDark ? "#0D1117" : "#F7F9FC";
        var fg     = isDark ? "#E6EDF3" : "#1A1D21";
        var code   = isDark ? "#161B22" : "#EEF2F7";
        var border = isDark ? "#30363D" : "#D0DAE6";
        var link   = isDark ? "#79B8FF" : "#1A6BB5";
        var muted  = isDark ? "#8B949E" : "#6B7A8A";
        return $$"""
            <!DOCTYPE html>
            <html><head>
            <meta charset="utf-8">
            <meta http-equiv="X-UA-Compatible" content="IE=edge">
            <style>
              html, body { background: {{bg}}; color: {{fg}}; margin: 0; padding: 0; }
              body { font-family: 'Segoe UI', sans-serif; font-size: 14px; line-height: 1.65;
                      padding: 24px 28px; }
              h1,h2,h3,h4,h5,h6 { margin: 1em 0 .4em; font-weight: 600; }
              h1 { font-size: 1.7em; border-bottom: 1px solid {{border}}; padding-bottom: .3em; }
              h2 { font-size: 1.4em; border-bottom: 1px solid {{border}}; padding-bottom: .2em; }
              h3 { font-size: 1.2em; }
              p { margin: .6em 0; }
              code { background: {{code}}; padding: 2px 5px; border-radius: 4px;
                      font-family: 'Cascadia Code', Consolas, monospace; font-size: 0.88em; }
              pre { background: {{code}}; padding: 12px 16px; border-radius: 6px; overflow-x: auto;
                     border: 1px solid {{border}}; }
              pre code { background: transparent; padding: 0; }
              blockquote { border-left: 3px solid {{border}}; margin: 0; padding: 4px 16px;
                            color: {{muted}}; }
              ul,ol { padding-left: 1.5em; }
              li { margin: .2em 0; }
              a { color: {{link}}; }
              hr { border: none; border-top: 1px solid {{border}}; margin: 1.2em 0; }
              table { border-collapse: collapse; width: 100%; }
              th,td { border: 1px solid {{border}}; padding: 6px 12px; text-align: left; }
              th { background: {{code}}; font-weight: 600; }
              img { max-width: 100%; }
            </style></head><body>
            {{body}}
            </body></html>
            """;
    }

    private static string MarkdownToHtml(string md)
    {
        md = System.Net.WebUtility.HtmlEncode(md);

        // fenced code blocks first (multi-line)
        md = Regex.Replace(md, @"```(\w*)\r?\n([\s\S]*?)```",
            m => $"<pre><code>{m.Groups[2].Value}</code></pre>",
            RegexOptions.Multiline);

        // inline code
        md = Regex.Replace(md, @"`([^`]+)`",
            m => $"<code>{m.Groups[1].Value}</code>");

        // headings
        md = Regex.Replace(md, @"^######\s+(.+)$", "<h6>$1</h6>", RegexOptions.Multiline);
        md = Regex.Replace(md, @"^#####\s+(.+)$",  "<h5>$1</h5>", RegexOptions.Multiline);
        md = Regex.Replace(md, @"^####\s+(.+)$",   "<h4>$1</h4>", RegexOptions.Multiline);
        md = Regex.Replace(md, @"^###\s+(.+)$",    "<h3>$1</h3>", RegexOptions.Multiline);
        md = Regex.Replace(md, @"^##\s+(.+)$",     "<h2>$2</h2>".Replace("$2", "$1"), RegexOptions.Multiline);
        md = Regex.Replace(md, @"^#\s+(.+)$",      "<h1>$1</h1>", RegexOptions.Multiline);

        // horizontal rules
        md = Regex.Replace(md, @"^(\*{3,}|-{3,}|_{3,})\s*$", "<hr>", RegexOptions.Multiline);

        // bold + italic
        md = Regex.Replace(md, @"\*\*\*(.+?)\*\*\*", "<strong><em>$1</em></strong>");
        md = Regex.Replace(md, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        md = Regex.Replace(md, @"__(.+?)__", "<strong>$1</strong>");
        md = Regex.Replace(md, @"\*(.+?)\*", "<em>$1</em>");
        md = Regex.Replace(md, @"_(.+?)_", "<em>$1</em>");

        // images
        md = Regex.Replace(md, @"!\[(.+?)\]\((.+?)\)",
            m => $"<img alt=\"{m.Groups[1].Value}\" src=\"{SafeMarkdownUrl(m.Groups[2].Value)}\">");

        // links
        md = Regex.Replace(md, @"\[(.+?)\]\((.+?)\)",
            m => $"<a href=\"{SafeMarkdownUrl(m.Groups[2].Value)}\">{m.Groups[1].Value}</a>");

        // blockquotes
        md = Regex.Replace(md, @"^>\s*(.+)$", "<blockquote>$1</blockquote>", RegexOptions.Multiline);

        // unordered lists (simple, non-nested)
        md = Regex.Replace(md, @"((?:^[ \t]*[-*+][ \t]+.+\n?)+)", m =>
        {
            var items = Regex.Matches(m.Value, @"^[ \t]*[-*+][ \t]+(.+)$", RegexOptions.Multiline);
            var sb = new System.Text.StringBuilder("<ul>");
            foreach (Match item in items) sb.Append($"<li>{item.Groups[1].Value}</li>");
            sb.Append("</ul>");
            return sb.ToString();
        }, RegexOptions.Multiline);

        // ordered lists (simple)
        md = Regex.Replace(md, @"((?:^[ \t]*\d+\.[ \t]+.+\n?)+)", m =>
        {
            var items = Regex.Matches(m.Value, @"^[ \t]*\d+\.[ \t]+(.+)$", RegexOptions.Multiline);
            var sb = new System.Text.StringBuilder("<ol>");
            foreach (Match item in items) sb.Append($"<li>{item.Groups[1].Value}</li>");
            sb.Append("</ol>");
            return sb.ToString();
        }, RegexOptions.Multiline);

        // paragraphs: wrap sequences of plain lines
        var lines = md.Split('\n');
        var result = new System.Text.StringBuilder();
        var para = new System.Text.StringBuilder();

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                FlushPara(result, para);
                continue;
            }
            if (line.StartsWith('<'))
            {
                FlushPara(result, para);
                result.AppendLine(line);
                continue;
            }
            if (para.Length > 0) para.Append(' ');
            para.Append(line);
        }
        FlushPara(result, para);
        return result.ToString();
    }

    private static void FlushPara(System.Text.StringBuilder result, System.Text.StringBuilder para)
    {
        if (para.Length == 0) return;
        result.AppendLine($"<p>{para}</p>");
        para.Clear();
    }

    private static string SafeMarkdownUrl(string encodedUrl)
    {
        var url = System.Net.WebUtility.HtmlDecode(encodedUrl).Trim();
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https" or "mailto")
            return System.Net.WebUtility.HtmlEncode(uri.AbsoluteUri);
        return "#";
    }

    // ── Image rotate / flip ──────────────────────────────────────────────────

    private void RotateImage(double angle)
    {
        if (_baseImage == null) return;
        PushUndo();
        var rotated = new TransformedBitmap(_baseImage, new RotateTransform(angle));
        rotated.Freeze();
        ApplyNewBaseImage(rotated);
    }

    private void FlipImageHorizontal()
    {
        if (_baseImage == null) return;
        PushUndo();
        var flipped = new TransformedBitmap(_baseImage, new ScaleTransform(-1, 1));
        flipped.Freeze();
        ApplyNewBaseImage(flipped);
    }

    private void FlipImageVertical()
    {
        if (_baseImage == null) return;
        PushUndo();
        var flipped = new TransformedBitmap(_baseImage, new ScaleTransform(1, -1));
        flipped.Freeze();
        ApplyNewBaseImage(flipped);
    }

    private void ApplyNewBaseImage(BitmapSource newImage)
    {
        _baseImage = newImage;
        _annotations.Clear();
        _nextMarkerNumber = 1;
        EditorCanvas.Width = newImage.PixelWidth;
        EditorCanvas.Height = newImage.PixelHeight;
        UpdateCanvasClip();
        ImagePreview.Source = newImage;
        ImagePreview.Width = newImage.PixelWidth;
        ImagePreview.Height = newImage.PixelHeight;
        Canvas.SetLeft(ImagePreview, 0);
        Canvas.SetTop(ImagePreview, 0);
        RenderAnnotations();
    }

    private void EditorRotateCW_Click(object sender, RoutedEventArgs e)    => RotateImage(90);
    private void EditorRotateCCW_Click(object sender, RoutedEventArgs e)   => RotateImage(270);
    private void EditorFlipH_Click(object sender, RoutedEventArgs e)       => FlipImageHorizontal();
    private void EditorFlipV_Click(object sender, RoutedEventArgs e)       => FlipImageVertical();

    // ── Resize with presets ──────────────────────────────────────────────────

    private void EditorResizePreset_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string tag) return;
        var parts = tag.Split('x');
        if (parts.Length != 2
            || !double.TryParse(parts[0], out var pw)
            || !double.TryParse(parts[1], out var ph)) return;

        double newWidth, newHeight;
        if (pw == 0 || ph == 0)
        {
            // half / double shorthand: Tag = "0.5x" or "2x"
            if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var factor)) return;
            newWidth  = Math.Round(EditorCanvas.Width  * factor);
            newHeight = Math.Round(EditorCanvas.Height * factor);
        }
        else
        {
            newWidth  = pw;
            newHeight = ph;
        }
        ApplyResize(newWidth, newHeight);
    }

    private void ApplyResize(double newWidth, double newHeight)
    {
        if (newWidth < 64 || newHeight < 64) return;
        var factorX = newWidth / EditorCanvas.Width;
        var factorY = newHeight / EditorCanvas.Height;
        var thicknessFactor = (factorX + factorY) / 2;
        PushUndo();
        EditorCanvas.Width = newWidth;
        EditorCanvas.Height = newHeight;
        UpdateCanvasClip();
        ImagePreview.Width  *= factorX;
        ImagePreview.Height *= factorY;
        Canvas.SetLeft(ImagePreview, Canvas.GetLeft(ImagePreview) * factorX);
        Canvas.SetTop(ImagePreview,  Canvas.GetTop(ImagePreview)  * factorY);
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

    // ── JPG export ───────────────────────────────────────────────────────────

    private void SaveImageAsJpg(RenderTargetBitmap image)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Bild speichern unter",
            Filter = "JPEG-Bild|*.jpg;*.jpeg|PNG-Bild|*.png|Alle Dateien|*.*",
            DefaultExt = ".jpg",
            FileName = DefaultExportName("jpg"),
        };
        if (dlg.ShowDialog(this) != true) return;

        var ext = System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant();
        BitmapEncoder encoder = (ext == ".jpg" || ext == ".jpeg")
            ? new JpegBitmapEncoder { QualityLevel = 92 }
            : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(dlg.FileName);
        encoder.Save(stream);
    }
}

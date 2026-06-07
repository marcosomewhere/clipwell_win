using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ClipwellWin.Models;
using ClipwellWin.ViewModels;
using Microsoft.Win32;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using RadioButton = System.Windows.Controls.RadioButton;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace ClipwellWin.Views;

public partial class PopupWindow : Window
{
    private const double DefaultPopupWidth = 560;
    private const double DefaultPopupHeight = 720;
    private const double MinimumPopupWidth = 500;
    private const double MinimumPopupHeight = 620;
    private const double TaskbarPopupGap = 8;

    private readonly PopupViewModel _vm;
    private readonly App _app = null!;
    private readonly DispatcherTimer _timestampTimer;
    private ScrollViewer? _entryScrollViewer;

    internal DateTime LastHiddenAtUtc { get; private set; } = DateTime.MinValue;

    public PopupWindow(PopupViewModel vm, App app)
    {
        _vm = vm;
        _app = app;
        DataContext = vm;
        InitializeComponent();
        App.ApplyAppIcon(this);

        _timestampTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMinutes(1),
        };
        _timestampTimer.Tick += (_, _) => RefreshVisibleTimestamps();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                RefreshVisibleTimestamps();
                _timestampTimer.Start();
            }
            else
            {
                LastHiddenAtUtc = DateTime.UtcNow;
                _timestampTimer.Stop();
            }
        };

        var s = _app?.CurrentSettings;
        Width  = Math.Max(MinimumPopupWidth, s?.PopupWidth  > 0 ? s.PopupWidth  : DefaultPopupWidth);
        Height = Math.Max(MinimumPopupHeight, s?.PopupHeight > 0 ? s.PopupHeight : DefaultPopupHeight);
    }

    private const int WM_NCHITTEST = 0x0084;
    private const int HTLEFT = 10, HTRIGHT = 11;
    private const int HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17, HTBOTTOM = 15;
    private const int EdgeSize = 8;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyWindowTheme();
        ClampSizeToCurrentMonitor();
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProcHook);
        PositionAtCursor();
    }

    internal void ApplyWindowTheme()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var tint = _app?.IsEffectiveDarkTheme == false
            ? Color.FromArgb(240, 250, 250, 248)
            : Color.FromArgb(238, 23, 26, 30);
        NativeMethods.EnableAcrylic(hwnd, tint);
        NativeMethods.SetImmersiveDarkMode(hwnd, _app?.IsEffectiveDarkTheme == true);
    }

    private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_NCHITTEST) return IntPtr.Zero;

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null) return IntPtr.Zero;

        int raw = lParam.ToInt32();
        int rawX = unchecked((short)(raw & 0xFFFF));
        int rawY = unchecked((short)((raw >> 16) & 0xFFFF));

        double sx = source.CompositionTarget.TransformFromDevice.M11;
        double sy = source.CompositionTarget.TransformFromDevice.M22;
        double x  = rawX * sx - Left;
        double y  = rawY * sy - Top;

        bool left   = x < EdgeSize;
        bool right  = x > Width  - EdgeSize;
        bool bottom = y > Height - EdgeSize;

        int hit = 0;
        if      (bottom && left)  hit = HTBOTTOMLEFT;
        else if (bottom && right) hit = HTBOTTOMRIGHT;
        else if (bottom)          hit = HTBOTTOM;
        else if (left)            hit = HTLEFT;
        else if (right)           hit = HTRIGHT;

        if (hit != 0)
        {
            handled = true;
            return new IntPtr(hit);
        }
        return IntPtr.Zero;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        RefreshVisibleTimestamps();
        SearchBox.Focus();
        SelectFirst();
        PlayOpenAnimation();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timestampTimer.Stop();
        base.OnClosed(e);
    }

    private void RefreshVisibleTimestamps()
    {
        var visibleEntries = EntryList.Items.OfType<EntryViewModel>().ToList();
        if (visibleEntries.Count == 0) return;
        _vm.RefreshTimestamps(visibleEntries);
    }

    private void GetMonitorAndScale(
        out NativeMethods.POINT cursor,
        out NativeMethods.MONITORINFO mi,
        out double scaleX,
        out double scaleY)
    {
        NativeMethods.GetCursorPos(out cursor);
        var monitorHandle = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
        mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        NativeMethods.GetMonitorInfo(monitorHandle, ref mi);

        scaleX = 1; scaleY = 1;
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            scaleX = source.CompositionTarget.TransformFromDevice.M11;
            scaleY = source.CompositionTarget.TransformFromDevice.M22;
        }
    }

    public void PositionAtCursor()
    {
        ClampSizeToCurrentMonitor();

        GetMonitorAndScale(out var cursor, out var mi, out var scaleX, out var scaleY);

        double workLeft   = mi.rcWork.Left   * scaleX;
        double workTop    = mi.rcWork.Top    * scaleY;
        double workRight  = mi.rcWork.Right  * scaleX;
        double workBottom = mi.rcWork.Bottom * scaleY;
        double cx = cursor.X * scaleX;
        double cy = cursor.Y * scaleY;

        double left = cx + 4;
        double top  = cy + 4;
        if (left + Width  > workRight)  left = workRight  - Width  - 4;
        if (top  + Height > workBottom) top  = cy - Height - 4;
        if (left < workLeft) left = workLeft + 4;
        if (top  < workTop)  top  = workTop  + 4;

        Left = left;
        Top  = top;
    }

    public void PositionAboveTaskbar()
    {
        var s = _app?.CurrentSettings;
        if (s == null) return;

        ClampSizeToCurrentMonitor();

        GetMonitorAndScale(out var cursor, out var mi, out var scaleX, out var scaleY);

        double workLeft   = mi.rcWork.Left   * scaleX;
        double workTop    = mi.rcWork.Top    * scaleY;
        double workRight  = mi.rcWork.Right  * scaleX;
        double workBottom = mi.rcWork.Bottom * scaleY;
        double cx = cursor.X * scaleX;

        double minLeft = workLeft + TaskbarPopupGap;
        double maxLeft = workRight - Width - TaskbarPopupGap;
        double left = cx - Width / 2;
        if (maxLeft >= minLeft)
            left = Math.Min(Math.Max(left, minLeft), maxLeft);
        else
            left = workLeft;

        double minTop = workTop + TaskbarPopupGap;
        double maxTop = workBottom - Height - TaskbarPopupGap;
        double top = mi.rcWork.Bottom < mi.rcMonitor.Bottom
            ? workBottom - Height - TaskbarPopupGap
            : mi.rcWork.Top > mi.rcMonitor.Top
                ? workTop + TaskbarPopupGap
                : workBottom - Height - TaskbarPopupGap;

        if (maxTop >= minTop)
            top = Math.Min(Math.Max(top, minTop), maxTop);
        else
            top = workTop;

        Left = left;
        Top  = top;
    }

    private void ClampSizeToCurrentMonitor()
    {
        GetMonitorAndScale(out _, out var mi, out var scaleX, out var scaleY);

        var maxWidth = Math.Max(MinimumPopupWidth, (mi.rcWork.Right - mi.rcWork.Left) * scaleX - 16);
        var maxHeight = Math.Max(MinimumPopupHeight, (mi.rcWork.Bottom - mi.rcWork.Top) * scaleY - 16);
        Width = Math.Min(Math.Max(Width, MinimumPopupWidth), maxWidth);
        Height = Math.Min(Math.Max(Height, MinimumPopupHeight), maxHeight);
    }

    public void FocusSearch()
    {
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    private void PlayOpenAnimation()
    {
        Opacity = 0;
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var dur  = new Duration(TimeSpan.FromMilliseconds(150));

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur) { EasingFunction = ease });

        RootBorder.RenderTransformOrigin = new Point(0.5, 0.5);
        RootBorder.RenderTransform = new ScaleTransform(0.97, 0.97);
        RootBorder.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.97, 1, dur) { EasingFunction = ease });
        RootBorder.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.97, 1, dur) { EasingFunction = ease });
    }

    private void SelectFirst()
    {
        if (EntryList.Items.Count > 0)
        {
            EntryList.SelectedIndex = 0;
            EntryList.ScrollIntoView(EntryList.SelectedItem);
        }
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        SaveSizeAndPosition();
        Hide();
        _vm.SearchText  = "";
        _vm.IsBulkMode  = false;
        ChipAll.IsChecked = true;
        _vm.TypeFilter    = null;
        _vm.ShowPinnedOnly = false;
    }

    private void SaveSizeAndPosition()
    {
        if (_app == null) return;
        var s = _app.CurrentSettings;
        s.PopupWidth  = Width;
        s.PopupHeight = Height;
        _app.SaveSettings();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 1 || e.LeftButton != MouseButtonState.Pressed) return;
        e.Handled = true;
        try
        {
            DragMove();
        }
        catch { }
    }

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        Width  = Math.Max(MinimumPopupWidth, Width  + e.HorizontalChange);
        Height = Math.Max(MinimumPopupHeight, Height + e.VerticalChange);
    }

    private void ToggleBulkMode_Click(object sender, RoutedEventArgs e)
        => _vm.IsBulkMode = !_vm.IsBulkMode;

    private void BulkSelectAll_Click(object sender, RoutedEventArgs e)
        => _vm.ToggleSelectAll();

    private void BulkPin_Click(object sender, RoutedEventArgs e)
        => _vm.PinSelected(true);

    private void BulkUnpin_Click(object sender, RoutedEventArgs e)
        => _vm.PinSelected(false);

    private void BulkDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedCount == 0) return;
        var r = System.Windows.MessageBox.Show(
            $"{_vm.SelectedCount} Einträge löschen?", "Clipwell",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (r == System.Windows.MessageBoxResult.Yes)
            _vm.DeleteSelected();
    }

    private void BulkExport_Click(object sender, RoutedEventArgs e)
    {
        bool exportAll = _vm.SelectedCount == 0;
        if (exportAll)
        {
            var ask = System.Windows.MessageBox.Show(
                "Keine Einträge ausgewählt. Gesamte History exportieren?", "Clipwell",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);
            if (ask != System.Windows.MessageBoxResult.Yes) return;
        }

        var dlg = new SaveFileDialog
        {
            Title  = exportAll ? "Gesamte History exportieren" : "Auswahl exportieren",
            Filter = "JSON-Datei|*.json",
            FileName = $"clipwell-export-{DateTime.Now:yyyyMMdd-HHmmss}.json",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            if (exportAll)
            {
                _app.Database.ExportAsJson(dlg.FileName);
                System.Windows.MessageBox.Show(
                    "Gesamte History exportiert.", "Clipwell",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            else
            {
                var entries = _vm.GetSelectedEntries();
                _app.Database.ExportAsJson(entries, dlg.FileName);
                System.Windows.MessageBox.Show(
                    $"{entries.Count} Einträge exportiert.", "Clipwell",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Export fehlgeschlagen: {ex.Message}", "Clipwell",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private void BulkCancel_Click(object sender, RoutedEventArgs e)
        => _vm.IsBulkMode = false;

    private void Eyedropper_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        var picker = new EyedropperWindow();
        if (picker.ShowDialog() == true && picker.PickedHex != null)
        {
            var entry = new ClipboardEntry
            {
                Type      = EntryType.Color,
                Content   = picker.PickedHex,
                HexColor  = picker.PickedHex,
                Timestamp = DateTime.Now,
                ContentKind = "COLOR",
                DetectionReason = "Vom Bildschirm gewählt (Eyedropper)",
            };
            _app.CopyColorToClipboard(picker.PickedHex);
            _vm.AddEntry(entry);
        }
        Show();
        FocusSearch();
    }

    private void ToggleQuickNote_Click(object sender, RoutedEventArgs e)
    {
        bool show = QuickNotePanel.Visibility != Visibility.Visible;
        QuickNotePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            QuickNoteBox.Text = "";
            QuickNoteBox.Focus();
        }
    }

    private void QuickNoteSave_Click(object sender, RoutedEventArgs e)
        => SaveQuickNote();

    private void QuickNoteCancel_Click(object sender, RoutedEventArgs e)
    {
        QuickNotePanel.Visibility = Visibility.Collapsed;
        QuickNoteBox.Text = "";
    }

    private void QuickNoteBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            SaveQuickNote();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            QuickNotePanel.Visibility = Visibility.Collapsed;
            QuickNoteBox.Text = "";
            e.Handled = true;
        }
    }

    private void SaveQuickNote()
    {
        var text = QuickNoteBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;
        _vm.AddQuickNote(text);
        QuickNotePanel.Visibility = Visibility.Collapsed;
        QuickNoteBox.Text = "";
    }

    private Point? _dragStart;

    private void EntryList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => _dragStart = e.GetPosition(null);

    private void EntryList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStart == null) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Value.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _dragStart = null;
        var vm = _vm.SelectedEntry;
        if (vm == null) return;

        var text = vm.Content ?? vm.Entry.OcrText ?? "";
        if (!string.IsNullOrEmpty(text))
            DragDrop.DoDragDrop(EntryList,
                new System.Windows.DataObject(System.Windows.DataFormats.UnicodeText, text),
                System.Windows.DragDropEffects.Copy);
    }

    private void EntryList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _entryScrollViewer ??= FindVisualChild<ScrollViewer>(EntryList);
        if (_entryScrollViewer == null) return;

        _entryScrollViewer.ScrollToVerticalOffset(
            Math.Clamp(_entryScrollViewer.VerticalOffset - e.Delta / 2.5, 0, _entryScrollViewer.ScrollableHeight));
        e.Handled = true;
    }

    private void FilterChip_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab)
        {
            EntryList.Focus();
            if (EntryList.SelectedIndex < 0 && EntryList.Items.Count > 0)
                EntryList.SelectedIndex = 0;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SearchBox.Focus();
            e.Handled = true;
        }
    }

    private void FilterChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb) return;
        var tag = rb.Tag as string ?? "";

        if (string.IsNullOrEmpty(tag))
        {
            _vm.TypeFilter     = null;
            _vm.ShowPinnedOnly = false;
        }
        else if (tag == "Pinned")
        {
            _vm.TypeFilter     = null;
            _vm.ShowPinnedOnly = true;
        }
        else if (Enum.TryParse<EntryType>(tag, out var type))
        {
            _vm.TypeFilter     = type;
            _vm.ShowPinnedOnly = false;
        }
    }

    private void Item_MouseEnter(object sender, MouseEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is EntryViewModel vm)
            vm.IsHovered = true;
    }

    private void Item_MouseLeave(object sender, MouseEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is EntryViewModel vm)
            vm.IsHovered = false;
    }

    private void HoverCopy_Click(object sender, RoutedEventArgs e)
    {
        if (HoverEntry(sender) is { } vm)
            _app.CopyEntryToClipboard(vm, plainText: false);
    }

    private void HoverPin_Click(object sender, RoutedEventArgs e)
    {
        if (HoverEntry(sender) is { } vm)
            _vm.TogglePin(vm);
    }

    private void HoverDelete_Click(object sender, RoutedEventArgs e)
    {
        if (HoverEntry(sender) is not { } vm) return;
        var idx = EntryList.SelectedIndex;
        _vm.Delete(vm);
        EntryList.SelectedIndex = Math.Min(idx, EntryList.Items.Count - 1);
    }

    private void HoverDetails_Click(object sender, RoutedEventArgs e)
    {
        if (HoverEntry(sender) is not { } vm) return;
        OpenDetails(vm);
    }

    private static EntryViewModel? HoverEntry(object sender)
    {
        // DataContext wird über das Grid gesetzt; Button findet den VM über VisualTree
        var el = sender as FrameworkElement;
        return el?.DataContext as EntryViewModel
            ?? FindParentDataContext<EntryViewModel>(el);
    }

    private static T? FindParentDataContext<T>(DependencyObject? obj) where T : class
    {
        while (obj != null)
        {
            if (obj is FrameworkElement fe && fe.DataContext is T t) return t;
            obj = VisualTreeHelper.GetParent(obj);
        }
        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = VisualTreeHelper.GetChild(obj, i);
            if (child is T match) return match;
            var descendant = FindVisualChild<T>(child);
            if (descendant != null) return descendant;
        }
        return null;
    }

    private static readonly string[] SearchPrefixSuggestions =
    [
        "type:Text", "type:Code", "type:Url", "type:Image", "type:Color",
        "kind:", "domain:",
        "pinned:true", "pinned:false",
    ];

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = SearchBox.Text;
        if (string.IsNullOrEmpty(text))
        {
            SuggestionsPopup.IsOpen = false;
            return;
        }

        var suggestions = GetSuggestions(text);
        if (suggestions.Count == 0)
        {
            SuggestionsPopup.IsOpen = false;
            return;
        }

        SuggestionsList.ItemsSource = suggestions;
        SuggestionsPopup.IsOpen = true;
    }

    private List<string> GetSuggestions(string text)
    {
        var matches = SearchPrefixSuggestions
            .Where(s => s.StartsWith(text, StringComparison.OrdinalIgnoreCase)
                     && !s.Equals(text, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (text.StartsWith("kind:", StringComparison.OrdinalIgnoreCase) && text.Length > 5)
        {
            var partial = text[5..];
            var kinds = _vm.Entries
                .Select(v => v.ContentKind)
                .Where(k => k != null && k.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .Select(k => $"kind:{k}");
            matches = kinds.ToList();
        }
        else if (text.Equals("kind:", StringComparison.OrdinalIgnoreCase))
        {
            matches = _vm.Entries
                .Select(v => v.ContentKind)
                .Where(k => k != null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .Select(k => $"kind:{k}")
                .ToList();
        }

        if (text.StartsWith("domain:", StringComparison.OrdinalIgnoreCase))
        {
            var partial = text.Length > 7 ? text[7..] : "";
            matches = _vm.Entries
                .Where(v => v.Type == EntryType.Url && v.Content != null)
                .Select(v =>
                {
                    try { return new Uri(v.Content!).Host.Replace("www.", ""); }
                    catch { return null; }
                })
                .Where(h => h != null && (string.IsNullOrEmpty(partial)
                    || h.StartsWith(partial, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .Select(h => $"domain:{h}")
                .ToList()!;
        }

        return matches;
    }

    private void Suggestions_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SuggestionsList.SelectedItem is string suggestion)
        {
            _vm.SearchText = suggestion;
            SearchBox.CaretIndex = suggestion.Length;
            SuggestionsPopup.IsOpen = false;
            SuggestionsList.SelectedItem = null;
            SearchBox.Focus();
        }
    }

    private void Suggestions_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SuggestionsPopup.IsOpen = false;
            SearchBox.Focus();
            e.Handled = true;
        }
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Tab:
                SuggestionsPopup.IsOpen = false;
                ChipAll.Focus();
                e.Handled = true;
                break;
            case Key.Down:
                if (SuggestionsPopup.IsOpen && SuggestionsList.Items.Count > 0)
                {
                    SuggestionsList.SelectedIndex = 0;
                    SuggestionsList.Focus();
                    e.Handled = true;
                    break;
                }
                MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                PasteSelected(plainText: (Keyboard.Modifiers & ModifierKeys.Control) != 0);
                e.Handled = true;
                break;
            case Key.Escape:
                if (SuggestionsPopup.IsOpen)
                {
                    SuggestionsPopup.IsOpen = false;
                    e.Handled = true;
                    break;
                }
                Hide();
                _vm.SearchText = "";
                e.Handled = true;
                break;
        }
    }

    private void EntryList_KeyDown(object sender, KeyEventArgs e)
    {
        var s = _app?.CurrentSettings;

        if (e.Key == Key.Enter)
        {
            PasteSelected(plainText: (Keyboard.Modifiers & ModifierKeys.Control) != 0);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            Hide();
            _vm.SearchText = "";
            e.Handled = true;
            return;
        }

        if (s != null && MatchShortcut(e, s.KeyPin, s.KeyPinCtrl, s.KeyPinAlt, s.KeyPinShift, s.KeyPinWin))
        {
            if (_vm.SelectedEntry != null) _vm.TogglePin(_vm.SelectedEntry);
            e.Handled = true;
            return;
        }

        if (s != null && MatchShortcut(e, s.KeyDetails, s.KeyDetailsCtrl, s.KeyDetailsAlt, s.KeyDetailsShift, s.KeyDetailsWin))
        {
            if (_vm.SelectedEntry != null) OpenDetails(_vm.SelectedEntry);
            e.Handled = true;
            return;
        }

        if (s != null && MatchShortcut(e, s.KeyQuickNote, s.KeyQuickNoteCtrl, s.KeyQuickNoteAlt, s.KeyQuickNoteShift, s.KeyQuickNoteWin))
        {
            ToggleQuickNote_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (s != null && MatchShortcut(e, s.KeyPinboard, s.KeyPinboardCtrl, s.KeyPinboardAlt, s.KeyPinboardShift, s.KeyPinboardWin))
        {
            _app?.TogglePinboard();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            if (_vm.SelectedEntry != null)
            {
                var idx = EntryList.SelectedIndex;
                _vm.Delete(_vm.SelectedEntry);
                EntryList.SelectedIndex = Math.Min(idx, EntryList.Items.Count - 1);
            }
            e.Handled = true;
        }
    }

    private static bool MatchShortcut(KeyEventArgs e, string keyStr, bool ctrl, bool alt = false, bool shift = false, bool win = false)
    {
        bool hasCtrl  = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool hasAlt   = (Keyboard.Modifiers & ModifierKeys.Alt)     != 0;
        bool hasShift = (Keyboard.Modifiers & ModifierKeys.Shift)   != 0;
        bool hasWin   = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);
        if (ctrl != hasCtrl || alt != hasAlt || shift != hasShift || win != hasWin) return false;

        return keyStr switch
        {
            "F1"  => e.Key == Key.F1,
            "F2"  => e.Key == Key.F2,
            "F3"  => e.Key == Key.F3,
            "F4"  => e.Key == Key.F4,
            "F5"  => e.Key == Key.F5,
            "F6"  => e.Key == Key.F6,
            "F7"  => e.Key == Key.F7,
            "F8"  => e.Key == Key.F8,
            "F9"  => e.Key == Key.F9,
            "F10" => e.Key == Key.F10,
            "F11" => e.Key == Key.F11,
            "F12" => e.Key == Key.F12,
            _ when keyStr.Length == 1 => e.Key == KeyInterop.KeyFromVirtualKey(char.ToUpper(keyStr[0])),
            _ => false,
        };
    }

    private void EntryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => PasteSelected(false);

    private void CopyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (MenuEntry(sender) is { } vm)
            _app.CopyEntryToClipboard(vm, plainText: false);
    }

    private void PlainCopyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (MenuEntry(sender) is { } vm)
            _app.CopyEntryToClipboard(vm, plainText: true);
    }

    private void PinMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (MenuEntry(sender) is { } vm) _vm.TogglePin(vm);
    }

    private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (MenuEntry(sender) is { } vm) _vm.Delete(vm);
    }

    private void SecureDeleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (MenuEntry(sender) is not { } vm) return;
        var r = System.Windows.MessageBox.Show(
            "Eintrag sicher löschen? Die Daten werden durch VACUUM endgültig aus der Datenbank entfernt.",
            "Sicher löschen", System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (r != System.Windows.MessageBoxResult.Yes) return;
        _vm.SecureDelete(vm);
    }

    private void DetailsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (MenuEntry(sender) is { } vm) OpenDetails(vm);
    }

    private void ReloadUrlMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (MenuEntry(sender) is { } vm) _app.ReloadUrlPreview(vm);
    }

    private void TreatAsTextMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (MenuEntry(sender) is { } vm) _vm.SetType(vm, EntryType.Text);
    }

    private void TreatAsCodeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (MenuEntry(sender) is { } vm) _vm.SetType(vm, EntryType.Code);
    }

    private static EntryViewModel? MenuEntry(object sender)
        => (sender as FrameworkElement)?.DataContext as EntryViewModel;

    private void OpenDetails(EntryViewModel vm)
    {
        var details = new DetailWindow(vm)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        details.Show();
    }

    private void MoveSelection(int delta)
    {
        EntryList.Focus();
        int idx  = EntryList.SelectedIndex;
        int next = Math.Clamp(idx + delta, 0, EntryList.Items.Count - 1);
        EntryList.SelectedIndex = next;
        EntryList.ScrollIntoView(EntryList.SelectedItem);
    }

    private void PasteSelected(bool plainText)
    {
        if (_vm.SelectedEntry == null) return;
        _app.PasteEntry(_vm.SelectedEntry, plainText);
    }
}

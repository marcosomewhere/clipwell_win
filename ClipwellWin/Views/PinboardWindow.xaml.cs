using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipwellWin.Models;
using ClipwellWin.Services;
using ClipwellWin.ViewModels;
using Button = System.Windows.Controls.Button;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using TextDataFormat = System.Windows.TextDataFormat;

namespace ClipwellWin.Views;

public partial class PinboardWindow : Window
{
    private readonly PopupViewModel _vm;
    private readonly App _app;
    private readonly HashSet<EntryViewModel> _observedEntries = [];
    private Point? _dragStart;
    private EntryViewModel? _dragEntry;

    public PinboardWindow(PopupViewModel vm, App app)
    {
        _vm  = vm;
        _app = app;
        InitializeComponent();
        App.ApplyAppIcon(this);
        foreach (var entry in _vm.Entries)
            WatchEntry(entry);
        Refresh();
        _vm.Entries.CollectionChanged += Entries_CollectionChanged;
        Closed += (_, _) =>
        {
            _vm.Entries.CollectionChanged -= Entries_CollectionChanged;
            foreach (var entry in _observedEntries.ToList())
                UnwatchEntry(entry);
        };
    }

    private double? _pendingScrollOffset;

    internal void Refresh()
    {
        var search = PinSearchBox?.Text?.Trim() ?? "";
        var allPinned = _vm.Entries
            .Where(e => e.IsPinned)
            .OrderByDescending(e => e.PinSortKey)
            .ToList();

        var shown = string.IsNullOrEmpty(search)
            ? allPinned
            : allPinned.Where(e =>
                (e.Entry.PreviewText?.Contains(search, StringComparison.OrdinalIgnoreCase) == true) ||
                (e.Entry.OcrText?.Contains(search, StringComparison.OrdinalIgnoreCase) == true) ||
                (e.Entry.UrlTitle?.Contains(search, StringComparison.OrdinalIgnoreCase) == true)).ToList();

        var scroller = FindScrollViewer(PinList);
        // Several refreshes can run back to back before the restore happens; keep the first captured offset.
        var offset = _pendingScrollOffset ?? scroller?.VerticalOffset ?? 0;
        PinList.ItemsSource = shown;
        // A new ItemsSource resets the scroll position; restore it so unpinning does not jump the list.
        if (scroller != null && offset > 0 && _pendingScrollOffset == null)
        {
            _pendingScrollOffset = offset;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                scroller.ScrollToVerticalOffset(_pendingScrollOffset ?? 0);
                _pendingScrollOffset = null;
            }));
        }
        CountLabel.Text = allPinned.Count == 0
            ? "Keine gepinnten Einträge"
            : string.IsNullOrEmpty(search)
                ? $"{allPinned.Count} gepinnte{(allPinned.Count == 1 ? "r Eintrag" : " Einträge")}"
                : $"{shown.Count} von {allPinned.Count} angezeigt";
    }

    private static System.Windows.Controls.ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.ScrollViewer sv) return sv;
            if (FindScrollViewer(child) is { } found) return found;
        }
        return null;
    }

    private void PinSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => Refresh();

    private void PinList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (PinList.SelectedItem is EntryViewModel vm)
            _vm.SelectedEntry = vm;
    }

    private void PinList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        if (IsInsideButton(source))
        {
            _dragStart = null;
            _dragEntry = null;
            return;
        }

        _dragStart = e.GetPosition(null);
        _dragEntry = FindDataContext<EntryViewModel>(source);
    }

    private void PinList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStart == null || _dragEntry == null) return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Value.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var entry = _dragEntry;
        _dragStart = null;
        _dragEntry = null;

        var dragData = new System.Windows.DataObject();
        dragData.SetData("ClipwellPinboardEntry", entry.Id.ToString());
        var text = entry.Content ?? entry.Entry.OcrText ?? "";
        if (!string.IsNullOrEmpty(text))
            dragData.SetText(text, TextDataFormat.UnicodeText);

        _dragSourceItem = PinList.ItemContainerGenerator.ContainerFromItem(entry) as System.Windows.Controls.ListBoxItem;
        using var ghost = _dragSourceItem != null ? DragGhost.Create(_dragSourceItem) : null;
        SetItemBorder(_dragSourceItem, DragVisual.Source);
        void OnFeedback(object s, System.Windows.GiveFeedbackEventArgs args) => ghost?.UpdatePosition();
        PinList.GiveFeedback += OnFeedback;
        try
        {
            ghost?.Show();
            DragDrop.DoDragDrop(PinList, dragData, DragDropEffects.Move | DragDropEffects.Copy);
        }
        finally
        {
            PinList.GiveFeedback -= OnFeedback;
            SetItemBorder(_dragSourceItem, DragVisual.None);
            SetItemBorder(_dropTargetItem, DragVisual.None);
            _dragSourceItem = null;
            _dropTargetItem = null;
        }
    }

    private System.Windows.Controls.ListBoxItem? _dragSourceItem;
    private System.Windows.Controls.ListBoxItem? _dropTargetItem;
    private bool _dropAfter;

    private enum DragVisual { None, Source, InsertBefore, InsertAfter }

    private void SetItemBorder(System.Windows.Controls.ListBoxItem? item, DragVisual visual)
    {
        if (item?.Template?.FindName("Bd", item) is not System.Windows.Controls.Border bd) return;

        var accent = TryFindResource("AccentBrush") as System.Windows.Media.Brush ?? System.Windows.SystemColors.HighlightBrush;
        // Thickness plus padding always add up to the template default (top 8, sides 8, bottom 6) so rows never shift.
        (bd.BorderThickness, bd.Padding, bd.BorderBrush, item.Opacity) = visual switch
        {
            DragVisual.Source       => (new Thickness(2), new Thickness(6, 6, 6, 4), accent, 0.6),
            DragVisual.InsertBefore => (new Thickness(0, 2, 0, 0), new Thickness(8, 6, 8, 6), accent, 1.0),
            DragVisual.InsertAfter  => (new Thickness(0, 0, 0, 2), new Thickness(8, 8, 8, 4), accent, 1.0),
            _                       => (new Thickness(0, 2, 0, 0), new Thickness(8, 6, 8, 6), (System.Windows.Media.Brush)System.Windows.Media.Brushes.Transparent, 1.0),
        };
    }

    private static System.Windows.Controls.ListBoxItem? FindListBoxItem(DependencyObject? source)
    {
        while (source != null && source is not System.Windows.Controls.ListBoxItem)
            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        return source as System.Windows.Controls.ListBoxItem;
    }

    private void PinList_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("ClipwellPinboardEntry"))
        {
            e.Effects = DragDropEffects.Move;
            UpdateDropIndicator(e);
        }
        else
            UpdateDropEffects(e);
        e.Handled = true;
    }

    private void UpdateDropIndicator(DragEventArgs e)
    {
        var item = FindListBoxItem(e.OriginalSource as DependencyObject);
        var after = item != null && e.GetPosition(item).Y > item.ActualHeight / 2;

        if (item == _dropTargetItem && after == _dropAfter) return;
        if (_dropTargetItem != _dragSourceItem)
            SetItemBorder(_dropTargetItem, DragVisual.None);

        _dropTargetItem = item;
        _dropAfter = after;
        if (item != null && item != _dragSourceItem)
            SetItemBorder(item, after ? DragVisual.InsertAfter : DragVisual.InsertBefore);
    }

    private void PinList_DragLeave(object sender, DragEventArgs e)
    {
        // DragLeave also fires when crossing between child elements; only clear when the pointer really left the list.
        var pos = e.GetPosition(PinList);
        if (pos.X >= 0 && pos.Y >= 0 && pos.X < PinList.ActualWidth && pos.Y < PinList.ActualHeight) return;
        if (_dropTargetItem != _dragSourceItem)
            SetItemBorder(_dropTargetItem, DragVisual.None);
        _dropTargetItem = null;
    }

    private void PinList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("ClipwellPinboardEntry"))
        {
            var idStr = e.Data.GetData("ClipwellPinboardEntry") as string;
            if (long.TryParse(idStr, out var draggedId))
            {
                var dropItem = FindListBoxItem(e.OriginalSource as DependencyObject);
                var dropTarget = dropItem?.DataContext as EntryViewModel;
                var after = dropItem != null && e.GetPosition(dropItem).Y > dropItem.ActualHeight / 2;
                ReorderPinnedEntry(draggedId, dropTarget?.Id, after);
            }
            e.Handled = true;
            return;
        }

        Pinboard_Drop(sender, e);
    }

    private void ReorderPinnedEntry(long draggedId, long? targetId, bool insertAfter)
    {
        if (draggedId == targetId) return;

        var items = (PinList.ItemsSource as System.Collections.Generic.List<EntryViewModel>)?.ToList();
        if (items == null || items.Count == 0) return;

        // Dropping on empty space below the list moves the entry to the end.
        if (!targetId.HasValue)
        {
            targetId = items[^1].Id;
            insertAfter = true;
            if (targetId == draggedId) return;
        }

        // The visible list may be filtered by the search box; the view model reorders all pinned entries.
        _vm.MovePinned(draggedId, targetId.Value, insertAfter);
        Refresh();
    }

    private void CopyBtn_Click(object sender, RoutedEventArgs e)
    {
        var vm = (sender as FrameworkElement)?.DataContext as EntryViewModel;
        if (vm == null) return;
        _app.CopyEntryToClipboard(vm, plainText: false);
    }

    private void UnpinBtn_Click(object sender, RoutedEventArgs e)
    {
        var vm = (sender as FrameworkElement)?.DataContext as EntryViewModel;
        if (vm == null) return;
        if (vm.IsPinned)
            _vm.TogglePin(vm);
        Refresh();
    }

    private void Entries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (EntryViewModel entry in e.OldItems)
                UnwatchEntry(entry);
        if (e.NewItems != null)
            foreach (EntryViewModel entry in e.NewItems)
                WatchEntry(entry);
        Dispatcher.Invoke(Refresh);
    }

    private void WatchEntry(EntryViewModel entry)
    {
        if (!_observedEntries.Add(entry)) return;
        entry.PropertyChanged += Entry_PropertyChanged;
    }

    private void UnwatchEntry(EntryViewModel entry)
    {
        if (!_observedEntries.Remove(entry)) return;
        entry.PropertyChanged -= Entry_PropertyChanged;
    }

    private void Entry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EntryViewModel.IsPinned) or nameof(EntryViewModel.RelativeTime))
            Dispatcher.Invoke(Refresh);
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            try { DragMove(); } catch { }
    }

    private void Pinboard_DragEnter(object sender, DragEventArgs e)
        => UpdateDropEffects(e);

    private void Pinboard_DragOver(object sender, DragEventArgs e)
        => UpdateDropEffects(e);

    private void Pinboard_Drop(object sender, DragEventArgs e)
    {
        if (TryBuildPinnedEntry(e.Data, out var entry))
        {
            _vm.AddEntry(entry);
            Refresh();
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    private void UpdateDropEffects(DragEventArgs e)
    {
        e.Effects = HasSupportedDropData(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private bool TryBuildPinnedEntry(System.Windows.IDataObject data, out ClipboardEntry entry)
    {
        entry = null!;

        if (TryBuildFileDropEntry(data, out entry) || TryBuildDataEntry(data, out entry))
        {
            entry.IsPinned = true;
            entry.Timestamp = DateTime.Now;
            return true;
        }

        return false;
    }

    private bool TryBuildDataEntry(System.Windows.IDataObject data, out ClipboardEntry entry)
    {
        entry = ClipboardProcessor.BuildEntry(data, _app.CurrentSettings.CodeDetectionMode,
            FileContentOptions.From(_app.CurrentSettings, always: true))!;
        if (entry == null) return false;

        return true;
    }

    private bool TryBuildFileDropEntry(System.Windows.IDataObject data, out ClipboardEntry entry)
    {
        entry = null!;
        if (!data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return false;

        if (data.GetData(System.Windows.DataFormats.FileDrop) is not string[] files)
            return false;

        foreach (var file in files)
        {
            if (TryBuildImageFileEntry(file, out entry))
                return true;
        }

        return false;
    }

    private static bool TryBuildImageFileEntry(string file, out ClipboardEntry entry)
    {
        entry = null!;
        if (!File.Exists(file) || !IsKnownImageFile(file)) return false;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(file);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();

            var bytes = OcrService.BitmapSourceToBytes(image);
            if (bytes == null) return false;

            var kind = ContentKindService.DetectImageKind(bytes);
            entry = new ClipboardEntry
            {
                Type = EntryType.Image,
                ImageData = bytes,
                ContentKind = kind,
                DetectionReason = $"Per Drag-and-drop hinzugefuegte Bilddatei ({kind}).",
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool HasSupportedDropData(System.Windows.IDataObject data)
        => data.GetDataPresent(System.Windows.DataFormats.Bitmap, true)
           || data.GetDataPresent(System.Windows.DataFormats.UnicodeText, true)
           || data.GetDataPresent(System.Windows.DataFormats.Text, true)
           || data.GetDataPresent(System.Windows.DataFormats.Html, true)
           || data.GetDataPresent(System.Windows.DataFormats.Rtf, true)
           || HasSupportedFileDrop(data);

    private bool HasSupportedFileDrop(System.Windows.IDataObject data)
    {
        if (!data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return false;
        if (data.GetData(System.Windows.DataFormats.FileDrop) is not string[] files) return false;
        var extensions = FileContentReader.ParseExtensions(_app.CurrentSettings.FileContentExtensions);
        return files.Any(file => IsKnownImageFile(file) || FileContentReader.IsReadable(file, extensions));
    }

    private static bool IsKnownImageFile(string file)
    {
        var ext = Path.GetExtension(file);
        return ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".tif", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".tiff", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInsideButton(DependencyObject? source)
        => FindAncestor<Button>(source) != null;

    private static T? FindDataContext<T>(DependencyObject? source) where T : class
    {
        for (var current = source; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { DataContext: T vm })
                return vm;
        }

        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        for (var current = source; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T typed)
                return typed;
        }

        return null;
    }
}

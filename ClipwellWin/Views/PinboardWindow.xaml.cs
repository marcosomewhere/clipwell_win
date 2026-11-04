using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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

    internal void Refresh()
    {
        var search = PinSearchBox?.Text?.Trim() ?? "";
        var allPinned = _vm.Entries
            .Where(e => e.IsPinned)
            .OrderByDescending(e => e.PinOrder > 0 ? e.PinOrder : e.Timestamp.Ticks)
            .ToList();

        var shown = string.IsNullOrEmpty(search)
            ? allPinned
            : allPinned.Where(e =>
                (e.Entry.PreviewText?.Contains(search, StringComparison.OrdinalIgnoreCase) == true) ||
                (e.Entry.OcrText?.Contains(search, StringComparison.OrdinalIgnoreCase) == true) ||
                (e.Entry.UrlTitle?.Contains(search, StringComparison.OrdinalIgnoreCase) == true)).ToList();

        PinList.ItemsSource = shown;
        CountLabel.Text = allPinned.Count == 0
            ? "Keine gepinnten Einträge"
            : string.IsNullOrEmpty(search)
                ? $"{allPinned.Count} gepinnte{(allPinned.Count == 1 ? "r Eintrag" : " Einträge")}"
                : $"{shown.Count} von {allPinned.Count} angezeigt";
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

        DragDrop.DoDragDrop(PinList, dragData, DragDropEffects.Move | DragDropEffects.Copy);
    }

    private void PinList_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("ClipwellPinboardEntry"))
            e.Effects = DragDropEffects.Move;
        else
            UpdateDropEffects(e);
        e.Handled = true;
    }

    private void PinList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("ClipwellPinboardEntry"))
        {
            var idStr = e.Data.GetData("ClipwellPinboardEntry") as string;
            if (long.TryParse(idStr, out var draggedId))
            {
                var dropTarget = FindDataContext<EntryViewModel>(e.OriginalSource as DependencyObject);
                ReorderPinnedEntry(draggedId, dropTarget?.Id);
            }
            e.Handled = true;
            return;
        }

        Pinboard_Drop(sender, e);
    }

    private void ReorderPinnedEntry(long draggedId, long? targetId)
    {
        if (!targetId.HasValue || draggedId == targetId.Value) return;

        var items = (PinList.ItemsSource as System.Collections.Generic.List<EntryViewModel>)?.ToList();
        if (items == null) return;

        var dragged = items.FirstOrDefault(e => e.Id == draggedId);
        var target  = items.FirstOrDefault(e => e.Id == targetId.Value);
        if (dragged == null || target == null) return;

        items.Remove(dragged);
        var insertIdx = items.IndexOf(target);
        items.Insert(insertIdx, dragged);

        // Reassign PinOrder with gaps
        for (int i = 0; i < items.Count; i++)
        {
            var newOrder = (long)(items.Count - i) * 1000;
            if (items[i].PinOrder != newOrder)
            {
                items[i].PinOrder = newOrder;
                _app.Database.UpdatePinOrder(items[i].Id, newOrder);
            }
        }

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
        entry = ClipboardProcessor.BuildEntry(data, _app.CurrentSettings.CodeDetectionMode)!;
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
            if (TryBuildImageFileEntry(file, out entry) || TryBuildTextFileEntry(file, out entry))
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

    private bool TryBuildTextFileEntry(string file, out ClipboardEntry entry)
    {
        entry = null!;
        if (!File.Exists(file) || !IsKnownTextFile(file)) return false;

        try
        {
            var info = new FileInfo(file);
            if (info.Length > 1024 * 1024) return false;

            var text = File.ReadAllText(file).Trim();
            if (string.IsNullOrWhiteSpace(text)) return false;

            var data = new System.Windows.DataObject(System.Windows.DataFormats.UnicodeText, text);
            return TryBuildDataEntry(data, out entry);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasSupportedDropData(System.Windows.IDataObject data)
        => data.GetDataPresent(System.Windows.DataFormats.Bitmap, true)
           || data.GetDataPresent(System.Windows.DataFormats.UnicodeText, true)
           || data.GetDataPresent(System.Windows.DataFormats.Text, true)
           || data.GetDataPresent(System.Windows.DataFormats.Html, true)
           || data.GetDataPresent(System.Windows.DataFormats.Rtf, true)
           || HasSupportedFileDrop(data);

    private static bool HasSupportedFileDrop(System.Windows.IDataObject data)
    {
        if (!data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return false;
        if (data.GetData(System.Windows.DataFormats.FileDrop) is not string[] files) return false;
        return files.Any(file => IsKnownImageFile(file) || IsKnownTextFile(file));
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

    private static bool IsKnownTextFile(string file)
    {
        var ext = Path.GetExtension(file);
        return ext.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".json", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".xml", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".html", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".htm", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".css", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".js", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".ts", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".cs", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".xaml", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".yml", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".ini", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".toml", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".env", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".ps1", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".sh", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".sql", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".csv", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".log", StringComparison.OrdinalIgnoreCase);
    }

    private static BitmapSource? LoadBitmapSource(byte[] bytes)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = new MemoryStream(bytes);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
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

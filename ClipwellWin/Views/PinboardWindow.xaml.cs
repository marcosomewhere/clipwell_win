using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using ClipwellWin.ViewModels;

namespace ClipwellWin.Views;

public partial class PinboardWindow : Window
{
    private readonly PopupViewModel _vm;
    private readonly App _app;
    private readonly HashSet<EntryViewModel> _observedEntries = [];

    public PinboardWindow(PopupViewModel vm, App app)
    {
        _vm  = vm;
        _app = app;
        InitializeComponent();
        foreach (var entry in _vm.Entries)
            WatchEntry(entry);
        Refresh();
        _vm.Entries.CollectionChanged += Entries_CollectionChanged;
    }

    internal void Refresh()
    {
        var pinned = _vm.Entries
            .Where(e => e.IsPinned)
            .OrderByDescending(e => e.Timestamp)
            .ToList();

        PinList.ItemsSource = pinned;
        CountLabel.Text = pinned.Count == 0
            ? "Keine gepinnten Einträge"
            : $"{pinned.Count} gepinnte{(pinned.Count == 1 ? "r Eintrag" : " Einträge")}";
    }

    private void PinList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (PinList.SelectedItem is EntryViewModel vm)
            _vm.SelectedEntry = vm;
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

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

}

using System.Windows;
using System.Windows.Input;
using ClipwellWin.ViewModels;

namespace ClipwellWin.Views;

public partial class PinboardWindow : Window
{
    private readonly PopupViewModel _vm;
    private readonly App _app;

    public PinboardWindow(PopupViewModel vm, App app)
    {
        _vm  = vm;
        _app = app;
        InitializeComponent();
        Refresh();
        _vm.Entries.CollectionChanged += (_, _) => Dispatcher.Invoke(Refresh);
    }

    private void Refresh()
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

    private void PasteBtn_Click(object sender, RoutedEventArgs e)
    {
        var vm = (sender as FrameworkElement)?.DataContext as EntryViewModel;
        if (vm == null) return;
        _app.PasteEntry(vm, plainText: false);
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            try { DragMove(); } catch { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    private void Window_Deactivated(object sender, EventArgs e) { }
}

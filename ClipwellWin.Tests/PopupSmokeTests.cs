using System.IO;
using System.Windows;
using System.Windows.Controls;
using ClipwellWin.Models;
using ClipwellWin.Services;
using ClipwellWin.ViewModels;
using ClipwellWin.Views;
using Xunit;

namespace ClipwellWin.Tests;

public class PopupSmokeTests
{
    [Fact]
    public void PopupWindow_CanOpenSearchAndSelect()
    {
        StaTestRunner.Run(() =>
        {
            if (Application.Current == null)
                _ = new Application();

            var dbPath = Path.Combine(Path.GetTempPath(), $"clipwell-test-{Guid.NewGuid():N}.db");
            using var db = new DatabaseService(dbPath);
            var vm = new PopupViewModel(db);
            vm.AddEntry(new ClipboardEntry
            {
                Type = EntryType.Text,
                Content = "needle value",
                DetectionReason = "Test entry",
                Timestamp = DateTime.Now,
            });
            vm.AddEntry(new ClipboardEntry
            {
                Type = EntryType.Text,
                Content = "other value",
                DetectionReason = "Test entry",
                Timestamp = DateTime.Now,
            });

            var window = new PopupWindow(vm, null!);
            window.Show();
            window.FocusSearch();
            vm.SearchText = "needle";
            window.UpdateLayout();
            var list = Assert.IsType<ListBox>(window.FindName("EntryList"));
            list.SelectedIndex = 0;

            Assert.Single(vm.FilteredEntries.Cast<EntryViewModel>());
            Assert.NotNull(vm.SelectedEntry);

            window.Close();
            db.Dispose();
            DeleteIfExists(dbPath);
            DeleteIfExists($"{dbPath}-wal");
            DeleteIfExists($"{dbPath}-shm");
        });
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}

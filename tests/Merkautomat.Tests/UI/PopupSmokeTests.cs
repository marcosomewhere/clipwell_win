using System.IO;
using System.Windows;
using System.Windows.Controls;
using Merkautomat.Models;
using Merkautomat.Services;
using Merkautomat.ViewModels;
using Merkautomat.Views;
using Xunit;

namespace Merkautomat.Tests;

public class PopupSmokeTests
{
    [Fact]
    public void PopupWindow_CanOpenSearchAndSelect()
    {
        StaTestRunner.Run(() =>
        {
            TestApplication.Ensure();

            var dbPath = Path.Combine(Path.GetTempPath(), $"merkautomat-test-{Guid.NewGuid():N}.db");
            using var db = new DatabaseService(dbPath);
            var vm = new PopupViewModel(db, runDatabase: PopupViewModel.InlineDatabase);
            vm.AddNow(new ClipboardEntry
            {
                Type = EntryType.Text,
                Content = "needle value",
                DetectionReason = "Test entry",
                Timestamp = DateTime.Now,
            });
            vm.AddNow(new ClipboardEntry
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

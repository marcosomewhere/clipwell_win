using System.IO;
using ClipwellWin.Models;
using ClipwellWin.Services;
using ClipwellWin.ViewModels;
using Xunit;

namespace ClipwellWin.Tests;

public class PopupViewModelTests
{
    [Fact]
    public void AddEntry_SkipsDuplicateLatestImage()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"clipwell-test-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(dbPath);
            var vm = new PopupViewModel(db);
            var image = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 };

            vm.AddEntry(new ClipboardEntry
            {
                Type = EntryType.Image,
                ImageData = image,
                ContentKind = "PNG",
                Timestamp = DateTime.Now,
            });
            vm.AddEntry(new ClipboardEntry
            {
                Type = EntryType.Image,
                ImageData = image.ToArray(),
                ContentKind = "PNG",
                Timestamp = DateTime.Now.AddMilliseconds(10),
            });

            Assert.Single(vm.Entries);
            Assert.Single(db.LoadAll());
        }
        finally
        {
            DeleteIfExists(dbPath);
            DeleteIfExists($"{dbPath}-wal");
            DeleteIfExists($"{dbPath}-shm");
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}

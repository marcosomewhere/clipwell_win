using System.IO;
using ClipwellWin.Models;
using ClipwellWin.Services;
using ClipwellWin.ViewModels;
using Xunit;

namespace ClipwellWin.Tests;

public class PopupViewModelTests
{
    [Fact]
    public void AddEntry_ReusesExistingTextEntryAndMakesItLatest()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"clipwell-test-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(dbPath);
            var vm = new PopupViewModel(db);
            var firstTimestamp = new DateTime(2026, 6, 6, 10, 0, 0);
            var secondTimestamp = firstTimestamp.AddMinutes(1);
            var duplicateTimestamp = firstTimestamp.AddMinutes(2);

            vm.AddEntry(new ClipboardEntry
            {
                Type = EntryType.Text,
                Content = "123",
                Timestamp = firstTimestamp,
            });
            var originalId = vm.Entries.Single().Id;
            vm.AddEntry(new ClipboardEntry
            {
                Type = EntryType.Text,
                Content = "456",
                Timestamp = secondTimestamp,
            });
            var duplicate = new ClipboardEntry
            {
                Type = EntryType.Text,
                Content = "123",
                Timestamp = duplicateTimestamp,
            };

            vm.AddEntry(duplicate);

            Assert.Equal(2, vm.Entries.Count);
            Assert.Equal(originalId, duplicate.Id);
            Assert.Equal("123", vm.LatestEntry()?.Content);

            var entries = db.LoadAll();
            Assert.Equal(2, entries.Count);
            Assert.Equal("123", entries[0].Content);
            Assert.Equal(originalId, entries[0].Id);
            Assert.Equal(duplicateTimestamp, entries[0].Timestamp);
        }
        finally
        {
            DeleteIfExists(dbPath);
            DeleteIfExists($"{dbPath}-wal");
            DeleteIfExists($"{dbPath}-shm");
        }
    }

    [Fact]
    public void AddEntry_PinsExistingDuplicateWhenIncomingEntryIsPinned()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"clipwell-test-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(dbPath);
            var vm = new PopupViewModel(db);

            vm.AddEntry(new ClipboardEntry
            {
                Type = EntryType.Text,
                Content = "pin me",
                Timestamp = new DateTime(2026, 6, 6, 10, 0, 0),
            });
            var originalId = vm.Entries.Single().Id;

            vm.AddEntry(new ClipboardEntry
            {
                Type = EntryType.Text,
                Content = "pin me",
                IsPinned = true,
                Timestamp = new DateTime(2026, 6, 6, 10, 1, 0),
            });

            Assert.Single(vm.Entries);
            Assert.Equal(originalId, vm.Entries[0].Id);
            Assert.True(vm.Entries[0].IsPinned);

            var entry = db.LoadAll().Single();
            Assert.Equal(originalId, entry.Id);
            Assert.True(entry.IsPinned);
        }
        finally
        {
            DeleteIfExists(dbPath);
            DeleteIfExists($"{dbPath}-wal");
            DeleteIfExists($"{dbPath}-shm");
        }
    }

    [Fact]
    public void TogglePin_MovesEntryToTopOfFilteredView()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"clipwell-test-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(dbPath);
            var vm = new PopupViewModel(db);

            vm.AddEntry(new ClipboardEntry
            {
                Type = EntryType.Text,
                Content = "older",
                Timestamp = new DateTime(2026, 6, 6, 10, 0, 0),
            });
            vm.AddEntry(new ClipboardEntry
            {
                Type = EntryType.Text,
                Content = "newer",
                Timestamp = new DateTime(2026, 6, 6, 10, 1, 0),
            });

            var older = vm.Entries.Single(e => e.Content == "older");
            Assert.Equal("newer", vm.FilteredEntries.Cast<EntryViewModel>().First().Content);

            vm.TogglePin(older);

            Assert.Equal("older", vm.FilteredEntries.Cast<EntryViewModel>().First().Content);
            Assert.Equal("older", vm.Entries[0].Content);
            var firstGroup = Assert.IsAssignableFrom<System.Windows.Data.CollectionViewGroup>(
                vm.FilteredEntries.Groups!.Cast<object>().First());
            Assert.Equal("Gepinnt", firstGroup.Name);
            Assert.Contains(firstGroup.Items.Cast<EntryViewModel>(), e => e.Content == "older");
        }
        finally
        {
            DeleteIfExists(dbPath);
            DeleteIfExists($"{dbPath}-wal");
            DeleteIfExists($"{dbPath}-shm");
        }
    }

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

    [Fact]
    public void AddEntry_DeduplicatesEmptyImageDataWithoutThrowing()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"clipwell-test-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(dbPath);
            var vm = new PopupViewModel(db);

            vm.AddEntry(new ClipboardEntry
            {
                Type = EntryType.Image,
                ImageData = [],
                Timestamp = DateTime.Now,
            });
            vm.AddEntry(new ClipboardEntry
            {
                Type = EntryType.Image,
                ImageData = [],
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

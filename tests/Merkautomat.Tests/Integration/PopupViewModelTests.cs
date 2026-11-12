using System.IO;
using Merkautomat.Models;
using Merkautomat.Services;
using Merkautomat.ViewModels;
using Xunit;

namespace Merkautomat.Tests;

public class PopupViewModelTests
{
    [Fact]
    public void AddEntry_ReusesExistingTextEntryAndMakesItLatest()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"merkautomat-test-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(dbPath);
            var vm = new PopupViewModel(db, runDatabase: PopupViewModel.InlineDatabase);
            var firstTimestamp = new DateTime(2026, 6, 6, 10, 0, 0);
            var secondTimestamp = firstTimestamp.AddMinutes(1);
            var duplicateTimestamp = firstTimestamp.AddMinutes(2);

            vm.AddNow(new ClipboardEntry
            {
                Type = EntryType.Text,
                Content = "123",
                Timestamp = firstTimestamp,
            });
            var originalId = vm.Entries.Single().Id;
            vm.AddNow(new ClipboardEntry
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

            vm.AddNow(duplicate);

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
        var dbPath = Path.Combine(Path.GetTempPath(), $"merkautomat-test-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(dbPath);
            var vm = new PopupViewModel(db, runDatabase: PopupViewModel.InlineDatabase);

            vm.AddNow(new ClipboardEntry
            {
                Type = EntryType.Text,
                Content = "pin me",
                Timestamp = new DateTime(2026, 6, 6, 10, 0, 0),
            });
            var originalId = vm.Entries.Single().Id;

            vm.AddNow(new ClipboardEntry
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
        var dbPath = Path.Combine(Path.GetTempPath(), $"merkautomat-test-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(dbPath);
            var vm = new PopupViewModel(db, runDatabase: PopupViewModel.InlineDatabase);

            vm.AddNow(new ClipboardEntry
            {
                Type = EntryType.Text,
                Content = "older",
                Timestamp = new DateTime(2026, 6, 6, 10, 0, 0),
            });
            vm.AddNow(new ClipboardEntry
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
    public void TogglePin_PutsLatestPinnedEntryFirstEvenWhenItIsOlder()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"merkautomat-test-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(dbPath);
            var vm = new PopupViewModel(db, runDatabase: PopupViewModel.InlineDatabase);

            foreach (var (content, minute) in new[] { ("oldest", 0), ("middle", 1), ("newest", 2) })
                vm.AddNow(new ClipboardEntry
                {
                    Type = EntryType.Text,
                    Content = content,
                    Timestamp = new DateTime(2026, 6, 6, 10, minute, 0),
                });

            vm.TogglePin(vm.Entries.Single(e => e.Content == "newest"));
            vm.TogglePin(vm.Entries.Single(e => e.Content == "oldest"));

            var pinned = vm.FilteredEntries.Cast<EntryViewModel>().Where(e => e.IsPinned).Select(e => e.Content).ToList();
            Assert.Equal(["oldest", "newest"], pinned);
            Assert.Equal("oldest", vm.Entries[0].Content);
        }
        finally
        {
            DeleteIfExists(dbPath);
            DeleteIfExists($"{dbPath}-wal");
            DeleteIfExists($"{dbPath}-shm");
        }
    }

    [Theory]
    [InlineData(false, new[] { "B", "D", "C", "A" })]
    [InlineData(true, new[] { "D", "B", "C", "A" })]
    public void MovePinned_RenumbersHiddenPinsToo(bool insertAfter, string[] expected)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"merkautomat-test-{Guid.NewGuid():N}.db");
        try
        {
            using (var db = new DatabaseService(dbPath))
            {
                var vm = new PopupViewModel(db, runDatabase: PopupViewModel.InlineDatabase);
                var minute = 0;
                foreach (var name in new[] { "A", "B", "C", "D" })
                    vm.AddNow(new ClipboardEntry
                    {
                        Type = EntryType.Text,
                        Content = name,
                        Timestamp = new DateTime(2026, 6, 6, 10, minute++, 0),
                    });
                foreach (var name in new[] { "A", "B", "C", "D" })
                    vm.TogglePin(vm.Entries.Single(e => e.Content == name));

                // Pinboard search shows only B and D; B is dropped onto D.
                var b = vm.Entries.Single(e => e.Content == "B");
                var d = vm.Entries.Single(e => e.Content == "D");
                Assert.True(vm.MovePinned(b.Id, d.Id, insertAfter));

                var order = vm.Entries.Where(e => e.IsPinned).OrderByDescending(e => e.PinSortKey).ToList();
                Assert.Equal(expected, order.Select(e => e.Content).ToArray());
                Assert.Equal(order.Count, order.Select(e => e.PinOrder).Distinct().Count());
            }

            using (var reopened = new DatabaseService(dbPath))
            {
                var vm = new PopupViewModel(reopened, runDatabase: PopupViewModel.InlineDatabase);
                vm.LoadFromDb();
                var persisted = vm.Entries.Where(e => e.IsPinned).OrderByDescending(e => e.PinSortKey)
                    .Select(e => e.Content).ToArray();
                Assert.Equal(expected, persisted);
            }
        }
        finally
        {
            DeleteIfExists(dbPath);
            DeleteIfExists($"{dbPath}-wal");
            DeleteIfExists($"{dbPath}-shm");
        }
    }

    [Fact]
    public void Search_AboveFtsThreshold_FiltersByFtsAndDropsPreviousHits()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"merkautomat-test-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(dbPath);
            var start = new DateTime(2026, 6, 1, 8, 0, 0);
            for (var i = 0; i < 1001; i++)
                db.Insert(new ClipboardEntry { Type = EntryType.Text, Content = $"eintrag {i}", Timestamp = start.AddMinutes(i) });
            db.Insert(new ClipboardEntry { Type = EntryType.Text, Content = "Zertifikat erneuern", Timestamp = start.AddDays(2) });
            db.Insert(new ClipboardEntry { Type = EntryType.Text, Content = "Apfelkuchen backen", Timestamp = start.AddDays(3) });
            var vm = new PopupViewModel(db, maxHistoryItems: () => 5000, runDatabase: PopupViewModel.InlineDatabase);
            vm.LoadFromDb();

            vm.SearchText = "zertif";
            Assert.Equal(["Zertifikat erneuern"], vm.FilteredEntries.Cast<EntryViewModel>().Select(e => e.Content));

            vm.SearchText = "apfel";
            Assert.Equal(["Apfelkuchen backen"], vm.FilteredEntries.Cast<EntryViewModel>().Select(e => e.Content));
        }
        finally
        {
            DeleteIfExists(dbPath);
            DeleteIfExists(dbPath + "-wal");
            DeleteIfExists(dbPath + "-shm");
        }
    }

    [Fact]
    public void AddEntry_SkipsDuplicateLatestImage()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"merkautomat-test-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(dbPath);
            var vm = new PopupViewModel(db, runDatabase: PopupViewModel.InlineDatabase);
            var image = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 };

            vm.AddNow(new ClipboardEntry
            {
                Type = EntryType.Image,
                ImageData = image,
                ContentKind = "PNG",
                Timestamp = DateTime.Now,
            });
            vm.AddNow(new ClipboardEntry
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
        var dbPath = Path.Combine(Path.GetTempPath(), $"merkautomat-test-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(dbPath);
            var vm = new PopupViewModel(db, runDatabase: PopupViewModel.InlineDatabase);

            vm.AddNow(new ClipboardEntry
            {
                Type = EntryType.Image,
                ImageData = [],
                Timestamp = DateTime.Now,
            });
            vm.AddNow(new ClipboardEntry
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

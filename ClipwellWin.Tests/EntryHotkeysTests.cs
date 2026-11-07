using System.Text.Json;
using ClipwellWin.Models;
using ClipwellWin.Services;
using Xunit;

namespace ClipwellWin.Tests;

public class EntryHotkeysTests
{
    private const uint Alt = 0x0001, Ctrl = 0x0002, Shift = 0x0004, Win = 0x0008, NoRepeat = 0x4000;
    private const uint Key1 = 0x31, KeyA = 0x41, F5 = 0x74, Num3 = 0x63, Space = 0x20;

    [Theory]
    [InlineData(Ctrl | Alt, Key1, true)]
    [InlineData(Win, KeyA, true)]
    [InlineData(Ctrl, Num3, true)]
    [InlineData(0u, F5, true)]
    [InlineData(Shift, F5, true)]
    [InlineData(Shift, KeyA, false)]
    [InlineData(0u, Key1, false)]
    [InlineData(Ctrl, Space, false)]
    public void Validate_RequiresRealModifierForTypingKeys(uint mods, uint vk, bool ok)
        => Assert.Equal(ok, EntryHotkeys.Validate(mods, vk) == null);

    [Fact]
    public void Format_NamesModifiersAndKeys()
    {
        Assert.Equal("Ctrl+Alt+1", EntryHotkeys.Format(Ctrl | Alt | NoRepeat, Key1));
        Assert.Equal("Shift+Win+A", EntryHotkeys.Format(Win | Shift, KeyA));
        Assert.Equal("Ctrl+Num 3", EntryHotkeys.Format(Ctrl, Num3));
        Assert.Equal("F5", EntryHotkeys.Format(0, F5));
    }

    [Fact]
    public void Assign_ReplacesHotkeyOfSameEntryAndAddsNoRepeat()
    {
        var s = new AppSettings();
        EntryHotkeys.Assign(s, 7, Ctrl | Alt, Key1, plainText: false);
        EntryHotkeys.Assign(s, 7, Ctrl | Alt, KeyA, plainText: true);

        var h = Assert.Single(s.EntryHotkeys);
        Assert.Equal(KeyA, h.Vk);
        Assert.True(h.PlainText);
        Assert.Equal(Ctrl | Alt | NoRepeat, h.Modifiers);
        Assert.Same(h, EntryHotkeys.Find(s, 7));
        Assert.True(EntryHotkeys.Remove(s, 7));
        Assert.Empty(s.EntryHotkeys);
        Assert.False(EntryHotkeys.Remove(s, 7));
    }

    [Fact]
    public void Conflict_DetectsPopupHotkeyAndOtherEntries()
    {
        var s = new AppSettings { HotkeyModifiers = Win | Shift | NoRepeat, HotkeyVk = 0x56 };
        Assert.NotNull(EntryHotkeys.Conflict(s, 1, Win | Shift, 0x56));

        EntryHotkeys.Assign(s, 1, Ctrl | Alt, Key1, false);
        Assert.NotNull(EntryHotkeys.Conflict(s, 2, Ctrl | Alt | NoRepeat, Key1));
        Assert.Null(EntryHotkeys.Conflict(s, 1, Ctrl | Alt, Key1));
        Assert.Null(EntryHotkeys.Conflict(s, 2, Ctrl | Alt | Shift, Key1));
    }

    [Fact]
    public void Clean_DropsInvalidAndDuplicateAssignments()
    {
        var s = new AppSettings
        {
            EntryHotkeys =
            [
                new() { EntryId = 1, Modifiers = Ctrl | Alt, Vk = Key1 },
                new() { EntryId = 2, Modifiers = Ctrl | Alt | NoRepeat, Vk = Key1 },
                new() { EntryId = 1, Modifiers = Ctrl, Vk = KeyA },
                new() { EntryId = 3, Modifiers = 0, Vk = KeyA },
                new() { EntryId = 0, Modifiers = Ctrl, Vk = F5 },
                new() { EntryId = 4, Modifiers = Win, Vk = F5 },
            ],
        };

        Assert.Equal(4, EntryHotkeys.Clean(s));
        Assert.Equal([1L, 4L], s.EntryHotkeys.Select(h => h.EntryId));
        Assert.All(s.EntryHotkeys, h => Assert.Equal(NoRepeat, h.Modifiers & NoRepeat));
    }

    [Fact]
    public void Settings_RoundTripAndOldFilesWithoutHotkeys()
    {
        var s = new AppSettings();
        EntryHotkeys.Assign(s, 42, Ctrl | Alt, Key1, true);
        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s))!;
        Assert.True(back.EntryHotkeysEnabled);
        Assert.Equal(42, Assert.Single(back.EntryHotkeys).EntryId);
        Assert.True(back.EntryHotkeys[0].PlainText);

        var old = JsonSerializer.Deserialize<AppSettings>("{}")!;
        Assert.Empty(old.EntryHotkeys);
        Assert.True(old.EntryHotkeysEnabled);
    }
}

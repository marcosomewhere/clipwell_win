using Merkautomat.Models;

namespace Merkautomat.Services;

/// <summary>Rules and bookkeeping for global hotkeys assigned to single history entries.</summary>
public static class EntryHotkeys
{
    private const uint ModMask = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT | NativeMethods.MOD_WIN;

    public static uint Normalize(uint modifiers) => (modifiers & ModMask) | NativeMethods.MOD_NOREPEAT;

    public static bool Same(uint modsA, uint vkA, uint modsB, uint vkB)
        => vkA == vkB && (modsA & ModMask) == (modsB & ModMask);

    private static bool IsLetterOrDigit(uint vk) => vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x60 and <= 0x69;
    private static bool IsFunctionKey(uint vk) => vk is >= 0x70 and <= 0x87;

    /// <summary>Null when the combination is usable, otherwise the reason in German.</summary>
    public static string? Validate(uint modifiers, uint vk)
    {
        if (!IsLetterOrDigit(vk) && !IsFunctionKey(vk))
            return "Bitte eine Buchstaben-, Ziffern- oder F-Taste verwenden.";
        // Letters and digits with only Shift (or nothing) would swallow normal typing in every program.
        if (IsLetterOrDigit(vk) && (modifiers & (NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_WIN)) == 0)
            return "Buchstaben und Ziffern brauchen Ctrl, Alt oder Win.";
        return null;
    }

    /// <summary>Null when no other hotkey uses the combination, otherwise the reason in German.</summary>
    public static string? Conflict(AppSettings s, long entryId, uint modifiers, uint vk)
    {
        if (Same(modifiers, vk, s.HotkeyModifiers, s.HotkeyVk))
            return $"{Format(modifiers, vk)} ist schon der globale Hotkey zum Öffnen des Popups.";
        var other = s.EntryHotkeys.FirstOrDefault(h => h.EntryId != entryId && Same(h.Modifiers, h.Vk, modifiers, vk));
        return other == null ? null : $"{Format(modifiers, vk)} ist schon einem anderen Eintrag zugewiesen.";
    }

    public static EntryHotkey? Find(AppSettings s, long entryId) => s.EntryHotkeys.FirstOrDefault(h => h.EntryId == entryId);

    /// <summary>Replaces the hotkey of the entry; returns the stored assignment.</summary>
    public static EntryHotkey Assign(AppSettings s, long entryId, uint modifiers, uint vk, bool plainText)
    {
        s.EntryHotkeys.RemoveAll(h => h.EntryId == entryId);
        var hotkey = new EntryHotkey { EntryId = entryId, Modifiers = Normalize(modifiers), Vk = vk, PlainText = plainText };
        s.EntryHotkeys.Add(hotkey);
        return hotkey;
    }

    public static bool Remove(AppSettings s, long entryId) => s.EntryHotkeys.RemoveAll(h => h.EntryId == entryId) > 0;

    /// <summary>Drops broken or duplicate assignments from settings.json (first one wins).</summary>
    public static int Clean(AppSettings s)
    {
        s.EntryHotkeys ??= [];
        var kept = new List<EntryHotkey>();
        foreach (var h in s.EntryHotkeys)
        {
            if (h == null || h.EntryId <= 0 || Validate(h.Modifiers, h.Vk) != null) continue;
            if (kept.Any(k => k.EntryId == h.EntryId || Same(k.Modifiers, k.Vk, h.Modifiers, h.Vk))) continue;
            h.Modifiers = Normalize(h.Modifiers);
            kept.Add(h);
        }
        var removed = s.EntryHotkeys.Count - kept.Count;
        s.EntryHotkeys = kept;
        return removed;
    }

    public static string Format(uint modifiers, uint vk)
    {
        var parts = new List<string>();
        if ((modifiers & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((modifiers & NativeMethods.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(vk switch
        {
            >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)vk).ToString(),
            >= 0x60 and <= 0x69 => $"Num {vk - 0x60}",
            >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
            _ => $"VK {vk:X2}",
        });
        return string.Join("+", parts);
    }
}

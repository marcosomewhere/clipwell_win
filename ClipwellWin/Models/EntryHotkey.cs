namespace ClipwellWin.Models;

/// <summary>Global hotkey that pastes one history entry into the active program.</summary>
public sealed class EntryHotkey
{
    public long EntryId { get; set; }
    /// <summary>RegisterHotKey modifiers (MOD_*), always including MOD_NOREPEAT.</summary>
    public uint Modifiers { get; set; }
    public uint Vk { get; set; }
    public bool PlainText { get; set; }
}

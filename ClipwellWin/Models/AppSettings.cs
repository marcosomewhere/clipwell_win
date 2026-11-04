namespace ClipwellWin.Models;

public enum HotkeyAction
{
    OpenMenu = 0,
    PasteLatest = 1,
}

public enum CodeDetectionMode
{
    Conservative = 0,
    Normal = 1,
    Aggressive = 2,
}

public enum ThemeMode
{
    System = 0,
    Dark = 1,
    Light = 2,
    Compact = 3,
}

public class AppSettings
{
    // Win+Shift+V | MOD_NOREPEAT
    public uint HotkeyModifiers { get; set; } = NativeMethods.MOD_WIN | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT;
    public uint HotkeyVk { get; set; } = NativeMethods.VK_V;
    public bool PauseMonitoring { get; set; } = false;
    public int MaxHistoryItems { get; set; } = 500;
    public HotkeyAction HotkeyAction { get; set; } = HotkeyAction.OpenMenu;
    public CodeDetectionMode CodeDetectionMode { get; set; } = CodeDetectionMode.Normal;

    public ThemeMode Theme { get; set; } = ThemeMode.System;

    public int MaxAgeInDays { get; set; } = 0;   // 0 = deaktiviert
    public int MaxSizeInMb { get; set; } = 0;    // 0 = deaktiviert

    public bool UrlPreviewEnabled { get; set; } = true;

    public double PopupWidth { get; set; } = 560;
    public double PopupHeight { get; set; } = 720;

    public bool StartWithWindows { get; set; } = true;

    public bool FirstRunCompleted { get; set; } = false;

    public bool AutoBackupEnabled { get; set; } = false;
    public string AutoBackupDirectory { get; set; } = "";
    public DateTime? LastAutoBackupDate { get; set; } = null;

    public string KeyPin { get; set; } = "P";
    public bool   KeyPinCtrl { get; set; } = true;
    public bool   KeyPinAlt { get; set; } = false;
    public bool   KeyPinShift { get; set; } = false;
    public bool   KeyPinWin { get; set; } = false;
    public string KeyDetails { get; set; } = "F2";
    public bool   KeyDetailsCtrl { get; set; } = false;
    public bool   KeyDetailsAlt { get; set; } = false;
    public bool   KeyDetailsShift { get; set; } = false;
    public bool   KeyDetailsWin { get; set; } = false;
    public string KeyQuickNote { get; set; } = "N";
    public bool   KeyQuickNoteCtrl { get; set; } = true;
    public bool   KeyQuickNoteAlt { get; set; } = false;
    public bool   KeyQuickNoteShift { get; set; } = false;
    public bool   KeyQuickNoteWin { get; set; } = false;
    public string KeyPinboard { get; set; } = "B";
    public bool   KeyPinboardCtrl { get; set; } = true;
    public bool   KeyPinboardAlt { get; set; } = false;
    public bool   KeyPinboardShift { get; set; } = false;
    public bool   KeyPinboardWin { get; set; } = false;

    public string QuickNoteDraft { get; set; } = "";

    public int UrlCacheTtlDays { get; set; } = 7;

    public double DetailWindowWidth { get; set; } = 0;
    public double DetailWindowHeight { get; set; } = 0;
    public bool DetailWindowMaximized { get; set; } = false;
}

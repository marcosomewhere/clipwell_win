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

    // Theme
    public ThemeMode Theme { get; set; } = ThemeMode.System;

    // Storage limits
    public int MaxAgeInDays { get; set; } = 0;   // 0 = deaktiviert
    public int MaxSizeInMb { get; set; } = 0;    // 0 = deaktiviert

    // Inkognito / Datenschutz
    public bool IncognitoMode { get; set; } = false;
    public List<string> IncognitoApps { get; set; } = [];
    public List<string> IncognitoDomains { get; set; } = [];

    // Sensible Inhalte
    public bool FilterSensitiveContent { get; set; } = true;

    // Popup-Grösse und Position
    public double PopupWidth { get; set; } = 560;
    public double PopupHeight { get; set; } = 720;
    public bool RememberPopupPosition { get; set; } = false;
    public double PopupLeft { get; set; } = -1;
    public double PopupTop { get; set; } = -1;

    // Onboarding
    public bool FirstRunCompleted { get; set; } = false;

    // Automatische JSON-Backups
    public bool AutoBackupEnabled { get; set; } = false;
    public string AutoBackupDirectory { get; set; } = "";
    public DateTime? LastAutoBackupDate { get; set; } = null;

    // Konfigurierbare Popup-Tastenkürzel (Taste als VK-Hex-String, z.B. "46" = F, "7B" = F12)
    public string KeyPin { get; set; } = "P";                // Ctrl+P
    public bool   KeyPinCtrl { get; set; } = true;
    public string KeyDelete { get; set; } = "Delete";        // Delete
    public string KeyDetails { get; set; } = "F2";          // F2 = Details öffnen
    public string KeyQuickNote { get; set; } = "N";          // Ctrl+N
    public bool   KeyQuickNoteCtrl { get; set; } = true;
    public string KeyPinboard { get; set; } = "B";           // Ctrl+B = Pinboard
    public bool   KeyPinboardCtrl { get; set; } = true;
}

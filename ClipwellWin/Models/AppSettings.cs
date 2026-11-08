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
    public string LocalAiModel { get; set; } = "off";
    /// <summary>E5 classifier for suggestions: "auto" picks the benchmark winner, otherwise a fixed method id.</summary>
    public string CategorizationMethod { get; set; } = "auto";
    public List<CategorizationFeedback> CategorizationFeedback { get; set; } = [];
    public List<CategorizationRun> CategorizationRuns { get; set; } = [];
    /// <summary>Background work with Granite: "off", "idle" (only while the PC is idle) or "always" (throttled).</summary>
    public string BackgroundWorkMode { get; set; } = Services.BackgroundWork.Idle;
    /// <summary>Keep the PC from sleeping while background work runs.</summary>
    public bool BackgroundKeepAwake { get; set; } = false;
    // Win+Shift+V | MOD_NOREPEAT
    public uint HotkeyModifiers { get; set; } = NativeMethods.MOD_WIN | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT;
    public uint HotkeyVk { get; set; } = NativeMethods.VK_V;
    /// <summary>Global hotkeys assigned to single entries (popup context menu "Hotkey zuweisen").</summary>
    public List<EntryHotkey> EntryHotkeys { get; set; } = [];
    public bool EntryHotkeysEnabled { get; set; } = true;
    public bool PauseMonitoring { get; set; } = false;
    public int MaxHistoryItems { get; set; } = 500;
    public HotkeyAction HotkeyAction { get; set; } = HotkeyAction.OpenMenu;
    public CodeDetectionMode CodeDetectionMode { get; set; } = CodeDetectionMode.Normal;

    public ThemeMode Theme { get; set; } = ThemeMode.System;

    public int MaxAgeInDays { get; set; } = 0;   // 0 = deaktiviert
    public int MaxSizeInMb { get; set; } = 0;    // 0 = deaktiviert

    public bool UrlPreviewEnabled { get; set; } = true;

    /// <summary>Copied files of a listed type are stored with their text content, not only as a file item.</summary>
    public bool FileContentEnabled { get; set; } = true;
    public string FileContentExtensions { get; set; } = Services.FileContentReader.DefaultExtensions;
    public int FileContentMaxKb { get; set; } = Services.FileContentReader.DefaultMaxKb;

    /// <summary>Desktop recording keeps only URL and page content of browser windows.</summary>
    public bool RecordingContentOnly { get; set; } = true;

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

    public string DictationModel { get; set; } = "base";
    public string DictationLanguage { get; set; } = "de";

    public int UrlCacheTtlDays { get; set; } = 7;

    public double DetailWindowWidth { get; set; } = 0;
    public double DetailWindowHeight { get; set; } = 0;
    public bool DetailWindowMaximized { get; set; } = false;

    // KB "Brain": view settings, remembered between sessions.
    public const double BrainThresholdMin = 0.80;
    public const double BrainThresholdMax = 0.95;
    public const double BrainThresholdDefault = 0.86;

    public double BrainThreshold { get; set; } = BrainThresholdDefault;
    public bool BrainShowClipboard { get; set; } = true;
    public bool BrainShowNotes { get; set; } = true;
    public bool BrainShowRecordings { get; set; } = true;
    public bool BrainEdgeSemantic { get; set; } = true;
    public bool BrainEdgeTime { get; set; } = true;
    public bool BrainEdgeSession { get; set; } = true;
    public bool BrainEdgeDomain { get; set; } = true;
    public bool BrainEdgeSummary { get; set; } = true;
    public bool BrainEdgeManual { get; set; } = true;
    public bool BrainFocus { get; set; } = false;
    public Dictionary<string, BrainAnnotation> BrainAnnotations { get; set; } = [];
    /// <summary>Defined main categories with subcategories and descriptions; used ones without a definition also count.</summary>
    public List<KnowledgeCategory> KnowledgeCategories { get; set; } = [];
    /// <summary>Brain groups and colors nodes by saved main category instead of automatic topics.</summary>
    public bool BrainGroupByCategory { get; set; } = false;
    public bool BrainEdgeCategory { get; set; } = true;
    public List<BrainSavedSummary> BrainSummaries { get; set; } = [];
    public List<BrainRelation> BrainRelations { get; set; } = [];

    /// <summary>Stored threshold within the slider range, rounded to whole percent; invalid values fall back to the default.</summary>
    public double EffectiveBrainThreshold()
        => double.IsFinite(BrainThreshold)
            ? Math.Round(Math.Clamp(BrainThreshold, BrainThresholdMin, BrainThresholdMax), 2)
            : BrainThresholdDefault;

    /// <summary>At least one source stays visible; with all of them switched off, all are shown again.</summary>
    public void NormalizeBrainSources()
    {
        if (BrainShowClipboard || BrainShowNotes || BrainShowRecordings) return;
        BrainShowClipboard = BrainShowNotes = BrainShowRecordings = true;
    }
}

using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Merkautomat.Models;
using Merkautomat.Services;
using Merkautomat.ViewModels;
using Merkautomat.Views;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using MessageBox = Merkautomat.Views.AppMessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using AppThemeMode = Merkautomat.Models.ThemeMode;

namespace Merkautomat;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = "Local\\Merkautomat.SingleInstance";
    private const string ShowEventName = "Local\\Merkautomat.ShowPopup";
    private const string StartupRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupRunValueName = "Merkautomat";
    private const string OnboardingSeenFileName = "onboarding.seen";
    private static readonly object LogLock = new();
    private readonly object _settingsMutationLock = new();
    private const int PasteDelayMs = 140;
    private const string ClipboardBusyMessage = "Clipboard konnte gerade nicht geschrieben werden.";
    private enum PopupPlacement { Cursor, Taskbar }

    private NotifyIcon? _trayIcon;
    private ToolStripMenuItem? _pinboardMenuItem;
    private ToolStripMenuItem? _recordMenuItem;
    private DesktopRecorderService? _recorder;
    private KnowledgeBaseWindow? _knowledgeBase;
    private DictationService? _dictation;
    private SemanticIndex? _semantic;
    private LocalAiService? _localAi;
    private AiResourceMonitor? _aiResources;
    private AiExecutionManager? _aiExecution;
    private MessageWindowService? _msgWin;
    private DatabaseService? _db;
    private SettingsService? _settings;
    private UrlPreviewService? _urlService;
    private PopupViewModel? _popupVm;
    private PopupWindow? _popup;
    private PinboardWindow? _pinboard;
    private OnboardingWindow? _onboarding;
    private Mutex? _singleInstanceMutex;
    private Mutex? _legacyInstanceMutex;
    private bool _ownsLegacyInstance;
    private EventWaitHandle? _legacyShowEvent;
    private EventWaitHandle? _showEvent;
    private CancellationTokenSource? _showEventCts;
    private bool _ownsSingleInstance;
    private bool _isEffectiveDarkTheme = true;
    private uint _registeredHotkeyModifiers;
    private uint _registeredHotkeyVk;
    private bool _usingHotkeyFallback;

    private readonly System.Threading.SemaphoreSlim _urlFetchSemaphore = new(3, 3);

    private IntPtr _previousForeground;
    private bool _ignoreNextClipboard;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        RegisterCrashLogging();

        if (!StartSingleInstance())
        {
            Shutdown();
            return;
        }
        if (!PrepareLocalDataDirectory())
        {
            Shutdown();
            return;
        }
        // Settings, history and all files are encrypted with this key, so it comes before any of them is read.
        if (!UnlockData())
        {
            Shutdown();
            return;
        }

        _settings = new SettingsService();
        _settings.Load();
        UpdateOutlookDataLocation();
        ApplyStartWithWindowsSetting();
        _db = OpenDatabaseWithRecovery();
        if (_db == null)
        {
            Shutdown();
            return;
        }
        _urlService = new UrlPreviewService();
        _recorder = new DesktopRecorderService(
            Path.Combine(AppPaths.DataDir, AppPaths.RecordingsFolderName),
            () => _settings?.Settings.PauseMonitoring == true,
            () => _settings?.Settings.RecordingContentOnly != false);
        _recorder.StateChanged += (_, _) => Dispatcher.BeginInvoke(OnRecordingStateChanged);
        _dictation = new DictationService(AppPaths.ModelsDir,
            () => (_settings?.Settings.DictationModel ?? "base", _settings?.Settings.DictationLanguage ?? "de"));
        _semantic = new SemanticIndex(Path.Combine(AppPaths.DataDir, SemanticIndex.FileName), EmbeddingModel.Id,
            () => new EmbeddingModel(EmbeddingModel.ResolveModelsDir(AppContext.BaseDirectory, AppPaths.ModelsDir)));

        ApplyTheme(_settings.Settings.Theme);
        _aiResources = new AiResourceMonitor();
        _aiExecution = new AiExecutionManager(AppPaths.ModelsDir, _aiResources, () => _settings.Settings.LocalAiComputeMode);
        AiCpuWork.Resources = () => _aiResources.Latest;
        _aiResources.Start();
        _localAi = new LocalAiService(AppPaths.ModelsDir, () => _settings.Settings.LocalAiModel,
            new E5Categorization(_semantic.EmbedForCategorizationAsync,
                () => EmbeddingModel.IsInstalled(EmbeddingModel.ResolveModelsDir(AppContext.BaseDirectory, AppPaths.ModelsDir)),
                _semantic.ItemTexts, () => _settings.Settings.CategorizationMethod,
                () => KnowledgeCategories.Descriptions(_settings.Settings)), () => _settings.Settings.LocalAiLanguageModel, _aiExecution)
        {
            Prompt = id => AiPrompts.Custom(_settings.Settings, id),
        };
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

        _popupVm = new PopupViewModel(
            _db,
            () => _settings.Settings.MaxHistoryItems,
            () => _settings.Settings.MaxAgeInDays);
        _popupVm.LoadFromDb();
        RecheckMarkdownEntries();

        _msgWin = new MessageWindowService();
        _msgWin.Initialize();
        _msgWin.ClipboardChanged += OnClipboardChanged;
        _msgWin.HotkeyPressed += OnHotkeyPressed;
        _msgWin.EntryHotkeyPressed += OnEntryHotkeyPressed;

        SetupTray();

        if (!RegisterConfiguredHotkey())
            ShowHotkeyRegistrationWarning();
        RegisterEntryHotkeys();
        StartMailInbox();
        StartBackgroundWork();
        StartKnowledgeFolder();
        StartMyKnowledge();

        _ = Task.Run(() => RunAutoBackupIfNeeded());
        var liveIds = _popupVm.Entries.Select(e => e.Id).ToHashSet();
        _ = Task.Run(() =>
        {
            try { Semantic.PruneClipboard(liveIds); }
            catch (Exception ex) when (ex is SqliteException or IOException) { LogCrash(ex); }
        });

        // Pre-build the popup while idle so the first hotkey press does not pay for XAML parsing and HWND creation.
        Dispatcher.InvokeAsync(EnsurePopupCreated, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        if (MarkOnboardingSeenIfNeeded())
        {
            Dispatcher.InvokeAsync(() =>
            {
                _onboarding = new OnboardingWindow(_settings.Settings, this);
                _onboarding.Closed += (_, _) => _onboarding = null;
                _onboarding.Show();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
        Dispatcher.InvokeAsync(ShowRecoveryKeyIfPending, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private bool MarkOnboardingSeenIfNeeded()
    {
        if (_settings == null) return false;

        var markerPath = Path.Combine(AppPaths.DataDir, OnboardingSeenFileName);
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            if (File.Exists(markerPath))
            {
                if (!_settings.Settings.FirstRunCompleted)
                {
                    _settings.Settings.FirstRunCompleted = true;
                    _settings.Save();
                }
                return false;
            }

            if (_settings.Settings.FirstRunCompleted)
            {
                File.WriteAllText(markerPath, DateTime.UtcNow.ToString("O"));
                return false;
            }

            _settings.Settings.FirstRunCompleted = true;
            _settings.Save();
            File.WriteAllText(markerPath, DateTime.UtcNow.ToString("O"));
            return true;
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            var shouldShow = !_settings.Settings.FirstRunCompleted;
            _settings.Settings.FirstRunCompleted = true;
            _settings.Save();
            return shouldShow;
        }
    }

    private static void RegisterCrashLogging()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown unhandled exception"));
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogCrash(e.Exception);
            e.SetObserved();
        };
        Current.DispatcherUnhandledException += (_, e) =>
        {
            LogCrash(e.Exception);
            e.Handled = IsRecoverableUiException(e.Exception);
        };
    }

    private static bool IsRecoverableUiException(Exception ex)
        => ex is COMException { HResult: unchecked((int)0x800401D0) or unchecked((int)0x800401D3) };

    internal static void LogCrash(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            lock (LogLock)
            {
                var log = Path.Combine(AppPaths.DataDir, "merkautomat.log");
                // Keeps the log bounded: one previous generation, then the oldest lines go.
                if (new FileInfo(log) is { Exists: true, Length: > 2 * 1024 * 1024 })
                    File.Move(log, log + ".1", overwrite: true);
                File.AppendAllText(log, $"[{DateTime.Now:O}] {ex}\r\n\r\n");
            }
        }
        // There is nowhere left to report a failing log; it must never take the app down with it.
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Logs an unexpected failure that the caller recovers from. Pass what was attempted, never content.</summary>
    internal static void LogError(string attempted, Exception ex)
        => LogCrash(new InvalidOperationException(attempted, ex));

    private bool StartSingleInstance()
    {
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var createdNew);
        _ownsSingleInstance = createdNew;
        if (!createdNew)
        {
            // The first instance may still be starting and not have created the event yet; it shows itself anyway.
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            catch (UnauthorizedAccessException) { }
            return false;
        }

        // Older builds must not open the same encrypted profile alongside this instance.
        _legacyInstanceMutex = new Mutex(true, "Local\\ClipwellWin.SingleInstance", out _ownsLegacyInstance);
        if (!_ownsLegacyInstance)
        {
            try { using var signal = EventWaitHandle.OpenExisting("Local\\ClipwellWin.ShowPopup"); signal.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            catch (UnauthorizedAccessException) { }
            return false;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _legacyShowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\ClipwellWin.ShowPopup");
        _showEventCts = new CancellationTokenSource();
        var token = _showEventCts.Token;
        _ = Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (WaitHandle.WaitAny([_showEvent, _legacyShowEvent], 500) != WaitHandle.WaitTimeout)
                        Dispatcher.Invoke(() => ShowPopup());
                }
                catch (ObjectDisposedException) { return; }
            }
        }, token);
        return true;
    }

    private DatabaseService? OpenDatabaseWithRecovery()
    {
        try
        {
            return new DatabaseService();
        }
        catch (SqliteException ex)
        {
            LogCrash(ex);
            var answer = MessageBox.Show(
                $"Die {AppNames.App}-Datenbank ist defekt oder gesperrt. Soll {AppNames.App} die Datei sichern und mit einer neuen History starten?",
                $"{AppNames.App} Datenbank-Recovery",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return null;

            try
            {
                DatabaseService.BackupAndReset();
                return new DatabaseService();
            }
            catch (Exception resetEx)
            {
                LogCrash(resetEx);
                MessageBox.Show(
                    $"Die Datenbank konnte nicht zurückgesetzt werden. Details stehen in {Path.Combine(DatabaseService.DataDir, "merkautomat.log")}.",
                    AppNames.App,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return null;
            }
        }
    }

    internal void ApplyTheme(AppThemeMode mode)
    {
        bool dark;
        if (mode == AppThemeMode.System)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var val = key?.GetValue("AppsUseLightTheme");
                dark = !(val is int v && v == 1);
            }
            catch { dark = true; }
        }
        else
        {
            dark = mode == AppThemeMode.Dark;
        }

        _isEffectiveDarkTheme = dark;
        var uiTheme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ApplicationThemeManager.Apply(uiTheme);
        // All four accent levels get the exact app accent; the single-color overload derives lighter
        // tints for dark themes, which showed up as a second, paler orange in toggles, buttons and focus lines.
        var accent = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(AccentHex);
        ApplicationAccentColorManager.Apply(accent, accent, accent, accent);
        ApplyAccentResources();
        ApplyPopupResources(dark);
        ApplyOnboardingResources(dark);
        ApplyDefaultSizeResources();
        _popup?.ApplyWindowTheme();
        _onboarding?.ApplyWindowTheme();
        _pinboard?.Refresh();
    }

    internal bool IsEffectiveDarkTheme => _isEffectiveDarkTheme;

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General &&
            e.Category != UserPreferenceCategory.Color)
            return;

        if (_settings?.Settings.Theme == AppThemeMode.System)
            Dispatcher.InvokeAsync(() => ApplyTheme(AppThemeMode.System));
    }

    internal const string AccentHex = "#FFE88832";

    private static void ApplyAccentResources()
    {
        var res = Current.Resources;
        res["AccentBrush"] = Brush(AccentHex);
        // Dark text on the accent: white on this orange stays below 3:1 contrast.
        res["AccentTextBrush"] = Brush("#FF0A0A0A");
        res["AccentStripeBrush"] = Brush("#D9E88832");
    }

    private static void ApplyPopupResources(bool dark)
    {
        var res = Current.Resources;
        if (dark)
        {
            res["PopupBackgroundBrush"] = Brush("#FF0A0A0A");
            res["PopupBorderBrush"] = Brush("#FF262626");
            res["PopupPanelBrush"] = Brush("#FF121212");
            res["PopupPanelBorderBrush"] = Brush("#FF262626");
            res["PopupTextPrimaryBrush"] = Brush("#FFEDEDED");
            res["PopupTextSecondaryBrush"] = Brush("#D8EDEDED");
            res["PopupTextMutedBrush"] = Brush("#A8EDEDED");
            res["PopupTextSubtleBrush"] = Brush("#8AEDEDED");
            res["PopupTextFaintBrush"] = Brush("#66EDEDED");
            res["PopupHoverBrush"] = Brush("#FF161616");
            res["PopupSelectedBrush"] = Brush("#FF1E1E1E");
            res["PopupChipBrush"] = Brush("#FF141414");
            res["PopupChipCheckedBrush"] = Brush("#FF2A2A2A");
            res["PopupBadgeBrush"] = Brush("#FF242424");
            res["PopupBadgeSecondaryBrush"] = Brush("#FF1A1A1A");
            res["PopupShadowBrush"] = Brush("#FF000000");
        }
        else
        {
            res["PopupBackgroundBrush"] = Brush("#FFFAFAF8");
            res["PopupBorderBrush"] = Brush("#FFE0E2E6");
            res["PopupPanelBrush"] = Brush("#FFFFFFFF");
            res["PopupPanelBorderBrush"] = Brush("#FFE0E2E6");
            res["PopupTextPrimaryBrush"] = Brush("#FF15171A");
            res["PopupTextSecondaryBrush"] = Brush("#B815171A");
            res["PopupTextMutedBrush"] = Brush("#8A15171A");
            res["PopupTextSubtleBrush"] = Brush("#6615171A");
            res["PopupTextFaintBrush"] = Brush("#5515171A");
            res["PopupHoverBrush"] = Brush("#FFF3F4F6");
            res["PopupSelectedBrush"] = Brush("#FFECEEF1");
            res["PopupChipBrush"] = Brush("#FFF0F1F3");
            res["PopupChipCheckedBrush"] = Brush("#FFD9DDE3");
            res["PopupBadgeBrush"] = Brush("#FFE1E4EA");
            res["PopupBadgeSecondaryBrush"] = Brush("#FFF0F1F3");
            res["PopupShadowBrush"] = Brush("#66000000");
        }
    }

    private static void ApplyOnboardingResources(bool dark)
    {
        var res = Current.Resources;
        if (dark)
        {
            res["OnboardingBackgroundBrush"] = Brush("#FF0A0A0A");
            res["OnboardingSurfaceBrush"] = Brush("#FF121212");
            res["OnboardingSurfaceAltBrush"] = Brush("#FF0E0E0E");
            res["OnboardingBorderBrush"] = Brush("#FF262626");
            res["OnboardingTextPrimaryBrush"] = Brush("#FFEDEDED");
            res["OnboardingTextSecondaryBrush"] = Brush("#D8EDEDED");
            res["OnboardingTextMutedBrush"] = Brush("#A8EDEDED");
            res["OnboardingInputBrush"] = Brush("#FF0E0E0E");
            res["OnboardingInputFocusedBrush"] = Brush("#FF141414");
            res["OnboardingButtonBrush"] = Brush("#FFEEF1F5");
            res["OnboardingButtonHoverBrush"] = Brush("#FFFFFFFF");
            res["OnboardingButtonTextBrush"] = Brush("#FF15171A");
            res["OnboardingAccentBrush"] = Brush(AccentHex);
        }
        else
        {
            res["OnboardingBackgroundBrush"] = Brush("#FFFAFAF8");
            res["OnboardingSurfaceBrush"] = Brush("#FFFFFFFF");
            res["OnboardingSurfaceAltBrush"] = Brush("#FFF3F4F6");
            res["OnboardingBorderBrush"] = Brush("#FFE0E2E6");
            res["OnboardingTextPrimaryBrush"] = Brush("#FF15171A");
            res["OnboardingTextSecondaryBrush"] = Brush("#B815171A");
            res["OnboardingTextMutedBrush"] = Brush("#8A15171A");
            res["OnboardingInputBrush"] = Brush("#FFFFFFFF");
            res["OnboardingInputFocusedBrush"] = Brush("#FFFFFFFF");
            res["OnboardingButtonBrush"] = Brush("#FF24282D");
            res["OnboardingButtonHoverBrush"] = Brush("#FF343A40");
            res["OnboardingButtonTextBrush"] = Brush("#FFFFFFFF");
            res["OnboardingAccentBrush"] = Brush(AccentHex);
        }
    }

    private static SolidColorBrush Brush(string color)
        => new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));

    private static void ApplyDefaultSizeResources()
    {
        var res = Current.Resources;
        res["EntryItemMargin"]       = new System.Windows.Thickness(8, 2, 8, 2);
        res["EntryContentMargin"]    = new System.Windows.Thickness(12, 8, 12, 8);
        res["EntryMinHeight"]        = 52.0;
        res["EntryFontSizeMain"]     = 12.5;
        res["EntryFontSizeMeta"]     = 11.0;
        res["EntryFontWeightMain"]   = System.Windows.FontWeights.Normal;
        res["EntryFontWeightMeta"]   = System.Windows.FontWeights.Normal;
        res["FilterChipPadding"]     = new System.Windows.Thickness(10, 4, 10, 4);
        res["FilterChipFontSize"]    = 11.0;
        res["BadgeFontSize"]         = 10.0;
        res["BadgePadding"]          = new System.Windows.Thickness(6, 2, 6, 2);
    }

    private void SetupTray()
    {
        _trayIcon = new NotifyIcon
        {
            Text = AppNames.App,
            Icon = CreateTrayIcon(),
            Visible = true,
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Öffnen", RenderAppLogo(SystemInformation.SmallIconSize.Width), (_, _) => ShowPopup());
        menu.Items.Add(new ToolStripSeparator());
        _pinboardMenuItem = new ToolStripMenuItem(AppNames.Pinboard);
        _pinboardMenuItem.Click += (_, _) => TogglePinboard();
        menu.Items.Add(_pinboardMenuItem);
        _recordMenuItem = new ToolStripMenuItem("Desktop aufzeichnen");
        _recordMenuItem.Click += (_, _) => ToggleRecording();
        menu.Items.Add(_recordMenuItem);
        menu.Items.Add(AppNames.KnowledgeBase, LoadKbMenuIcon(), (_, _) => OpenKnowledgeBase());
        menu.Items.Add(AppNames.Settings, null, (_, _) => OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => ExitApp());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) TogglePopupFromTray(); };
    }

    private void ShowHotkeyRegistrationWarning()
    {
        var error = _msgWin?.LastHotkeyError ?? 0;
        _trayIcon?.ShowBalloonTip(
            5000,
            $"{AppNames.App} Hotkey nicht aktiv",
            $"Die Tastenkombination ist schon belegt oder wurde von Windows blockiert. Fehlercode: {error}",
            ToolTipIcon.Warning);
    }

    private void ShowHotkeyFallbackWarning(uint requestedModifiers, uint requestedVk)
    {
        _trayIcon?.ShowBalloonTip(
            7000,
            $"{AppNames.App} Hotkey-Fallback aktiv",
            $"{FormatHotkey(requestedModifiers, requestedVk)} ist belegt. Fuer diese Sitzung wurde {FormatHotkey(_registeredHotkeyModifiers, _registeredHotkeyVk)} registriert; die Einstellung wurde nicht ueberschrieben.",
            ToolTipIcon.Warning);
    }

    /// <summary>Application logo (transparent PNG, 256 px); every size is scaled down from it.</summary>
    internal const string AppLogoUri = "pack://application:,,,/Merkautomat;component/Assets/AppLogo.png";

    private static Icon CreateTrayIcon()
    {
        // The tray shows the small system icon size; drawing exactly that size keeps the thin ring sharp.
        using var bmp = RenderAppLogo(SystemInformation.SmallIconSize.Width);
        var hIcon = bmp.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(hIcon);
            return (Icon)icon.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }

    /// <summary>The logo scaled to <paramref name="size"/> pixels with high quality; a blank bitmap if the resource is missing.</summary>
    internal static Bitmap RenderAppLogo(int size)
    {
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var info = GetResourceStream(new Uri(AppLogoUri));
            if (info == null) return bmp;
            using var stream = info.Stream;
            using var source = System.Drawing.Image.FromStream(stream);
            using var g = Graphics.FromImage(bmp);
            g.Clear(System.Drawing.Color.Transparent);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            g.DrawImage(source, 0, 0, size, size);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            LogCrash(ex);
        }
        return bmp;
    }

    /// <summary>The logo for window title bars; WPF picks and scales from the full-size image.</summary>
    internal static ImageSource CreateAppIconImageSource()
    {
        var image = new System.Windows.Media.Imaging.BitmapImage();
        image.BeginInit();
        image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(AppLogoUri);
        image.EndInit();
        image.Freeze();
        return image;
    }

    internal static void ApplyAppIcon(Window window)
    {
        window.Icon = CreateAppIconImageSource();
    }

    /// <summary>Small KB symbol for the tray menu and popup.</summary>
    internal const string KbIconUri = "pack://application:,,,/Merkautomat;component/Assets/kb-icon-16.png";

    private static System.Drawing.Image? LoadKbMenuIcon()
    {
        try
        {
            var info = GetResourceStream(new Uri(KbIconUri));
            if (info == null) return null;
            using var stream = info.Stream;
            using var source = System.Drawing.Image.FromStream(stream);
            // Detach from the stream, which is disposed here.
            return new Bitmap(source);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            LogCrash(ex);
            return null;
        }
    }

    private void ShowPopup(PopupPlacement placement = PopupPlacement.Cursor)
    {
        _previousForeground = NativeMethods.GetForegroundWindow();

        EnsurePopupCreated();
        _popup!.ApplyWindowTheme();
        RefreshEntryHotkeyLabels();

        if (_popup.IsVisible)
        {
            PositionPopup(placement);
            _popup.FocusSearch();
            _popup.Activate();
            return;
        }

        // Position only after the HWND exists so DPI scaling is known; repositioning after Show() makes the window jump.
        PositionPopup(placement);
        _popup.Show();
        _popup.FocusSearch();
        _popup.Activate();
    }

    private void EnsurePopupCreated()
    {
        if (_popup != null && _popup.IsInitializedHandle) return;
        _popup = new PopupWindow(_popupVm!, this);
        _popup.EnsureHandle();
    }

    private void TogglePopupFromTray()
    {
        if (_popup?.IsVisible == true)
        {
            HidePopup();
            return;
        }

        if (_popup != null && DateTime.UtcNow - _popup.LastHiddenAtUtc < TimeSpan.FromMilliseconds(500))
            return;

        ShowPopup(PopupPlacement.Taskbar);
    }

    private void PositionPopup(PopupPlacement placement)
    {
        if (_popup == null) return;
        if (placement == PopupPlacement.Taskbar)
            _popup.PositionAboveTaskbar();
        else
            _popup.PositionAtCursor();
    }

    internal void HidePopup() => _popup?.Hide();

    internal AppSettings CurrentSettings => _settings!.Settings;

    internal void SaveSettings() => _settings!.Save();

    internal bool SetStartWithWindows(bool enabled)
    {
        if (_settings == null) return false;
        if (!TrySetStartupRegistration(enabled))
            return false;

        _settings.Settings.StartWithWindows = enabled;
        _settings.Save();
        return true;
    }

    internal DatabaseService Database => _db!;

    private void ApplyStartWithWindowsSetting()
    {
        if (_settings == null) return;
        if (!TrySetStartupRegistration(_settings.Settings.StartWithWindows))
            LogCrash(new InvalidOperationException("Windows startup registration could not be updated."));
    }

    private static bool TrySetStartupRegistration(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(StartupRunKeyPath, writable: true);
            if (key == null) return false;

            if (enabled)
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exePath))
                    return false;

                key.SetValue(StartupRunValueName, $"\"{exePath}\"", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(StartupRunValueName, throwOnMissingValue: false);
            }

            key.DeleteValue("Clipwell", throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            return false;
        }
    }

    internal bool ReRegisterHotkey()
    {
        var ok = RegisterConfiguredHotkey();
        if (!ok) ShowHotkeyRegistrationWarning();
        return ok;
    }

    internal void SuspendHotkey()
    {
        _msgWin?.UnregisterHotkey();
    }

    internal bool IsHotkeyRegistered => _msgWin?.IsHotkeyRegistered == true;
    internal int LastHotkeyError => _msgWin?.LastHotkeyError ?? 0;
    internal bool IsHotkeyFallbackActive => IsHotkeyRegistered && _usingHotkeyFallback;
    internal string RegisteredHotkeyText => IsHotkeyRegistered
        ? FormatHotkey(_registeredHotkeyModifiers, _registeredHotkeyVk)
        : "";

    private bool RegisterConfiguredHotkey()
    {
        if (_msgWin == null || _settings == null) return false;

        var s = _settings.Settings;
        s.HotkeyModifiers = NormalizeHotkeyModifiers(s.HotkeyModifiers);
        if (_msgWin.RegisterHotkey(s.HotkeyModifiers, s.HotkeyVk))
        {
            _registeredHotkeyModifiers = s.HotkeyModifiers;
            _registeredHotkeyVk = s.HotkeyVk;
            _usingHotkeyFallback = false;
            _settings.Save();
            return true;
        }

        var requested = (s.HotkeyModifiers, s.HotkeyVk);
        foreach (var fallback in HotkeyFallbacks(s.HotkeyVk))
        {
            if (fallback == requested) continue;
            if (!_msgWin.RegisterHotkey(fallback.modifiers, fallback.vk)) continue;

            _registeredHotkeyModifiers = fallback.modifiers;
            _registeredHotkeyVk = fallback.vk;
            _usingHotkeyFallback = true;
            ShowHotkeyFallbackWarning(requested.HotkeyModifiers, requested.HotkeyVk);
            return true;
        }

        s.HotkeyModifiers = requested.HotkeyModifiers;
        s.HotkeyVk = requested.HotkeyVk;
        _registeredHotkeyModifiers = 0;
        _registeredHotkeyVk = 0;
        _usingHotkeyFallback = false;
        return false;
    }

    private static uint NormalizeHotkeyModifiers(uint modifiers)
    {
        const uint baseMask = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT |
                              NativeMethods.MOD_SHIFT | NativeMethods.MOD_WIN;
        var baseModifiers = modifiers & baseMask;
        if (baseModifiers == 0)
            baseModifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT;
        return baseModifiers | NativeMethods.MOD_NOREPEAT;
    }

    private static IEnumerable<(uint modifiers, uint vk)> HotkeyFallbacks(uint vk)
    {
        yield return (NativeMethods.MOD_WIN | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT, vk);
        yield return (NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT, vk);
        yield return (NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, vk);
        yield return (NativeMethods.MOD_WIN | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, vk);
    }

    private void OnClipboardChanged(object? sender, EventArgs e)
    {
        if (_settings!.Settings.PauseMonitoring) return;
        if (_ignoreNextClipboard) { _ignoreNextClipboard = false; return; }

        var entry = ClipboardProcessor.BuildEntry(_settings.Settings.CodeDetectionMode,
            FileContentOptions.From(_settings.Settings));
        if (entry == null) return;

        // Reading the clipboard needs the UI thread; decoding, storing and purging do not.
        StoreClipboardEntryAsync(entry).Forget("Zwischenablage speichern");
    }

    private async Task StoreClipboardEntryAsync(ClipboardEntry entry)
    {
        if (entry.Type == EntryType.Image)
        {
            var image = entry.ImageData;
            entry.ThumbnailData = await Task.Run(() => GenerateThumbnail120(image));
        }

        await _popupVm!.AddEntryAsync(entry);

        var maxMb = _settings!.Settings.MaxSizeInMb;
        if (maxMb > 0)
            await Task.Run(() => _db!.PurgeBySizeIfNeeded(maxMb));

        if (entry.Type == EntryType.Image && OcrService.IsAvailable())
        {
            var id = entry.Id;
            var data = entry.ImageData!;
            _ = Task.Run(async () =>
            {
                var text = await OcrService.RecognizeAsync(data);
                if (string.IsNullOrEmpty(text)) return;
                Dispatcher.Invoke(() => _popupVm.UpdateOcr(id, text));
            });
        }

        if (entry.Type == EntryType.Url && entry.Content != null
            && _settings.Settings.UrlPreviewEnabled
            && UrlPreviewService.ShouldFetch(entry.Content))
        {
            var id = entry.Id;
            var url = entry.Content;

            var urlVm = _popupVm.Entries.FirstOrDefault(x => x.Id == id);
            if (urlVm != null) urlVm.UrlState = UrlPreviewState.Loading;

            _ = Task.Run(async () =>
            {
                await _urlFetchSemaphore.WaitAsync();
                try
                {
                    var ttl = _settings?.Settings.UrlCacheTtlDays ?? 7;
                    var (cachedTitle, cachedFavicon) = _db!.GetUrlCache(url, ttl);
                    if (cachedTitle != null || cachedFavicon != null)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _popupVm.UpdateUrlPreview(id, cachedTitle, cachedFavicon);
                            var v = _popupVm.Entries.FirstOrDefault(x => x.Id == id);
                            if (v != null) v.UrlState = UrlPreviewState.Loaded;
                        });
                        return;
                    }
                    var (title, favicon) = await _urlService!.FetchAsync(url);
                    _db.UpsertUrlCache(url, title, favicon);
                    Dispatcher.Invoke(() =>
                    {
                        _popupVm.UpdateUrlPreview(id, title, favicon);
                        var v = _popupVm.Entries.FirstOrDefault(x => x.Id == id);
                        if (v != null) v.UrlState = title != null || favicon != null
                            ? UrlPreviewState.Loaded
                            : UrlPreviewState.Failed;
                    });
                }
                catch (Exception ex)
                {
                    LogError("URL-Vorschau laden", ex);
                    Dispatcher.Invoke(() =>
                    {
                        var v = _popupVm?.Entries.FirstOrDefault(x => x.Id == id);
                        if (v != null) v.UrlState = UrlPreviewState.Failed;
                    });
                }
                finally
                {
                    _urlFetchSemaphore.Release();
                }
            });
        }
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        if (_onboarding?.NotifyHotkeyTriggered() == true)
            return;

        if (_settings?.Settings.HotkeyAction == HotkeyAction.PasteLatest)
        {
            var latest = _popupVm?.LatestEntry();
            if (latest != null)
            {
                PasteEntry(latest, plainText: false);
                return;
            }
        }
        ShowPopup();
    }

    internal void ReloadUrlPreview(EntryViewModel vm)
    {
        if (vm.Type != EntryType.Url || vm.Content == null) return;
        if (!UrlPreviewService.ShouldFetch(vm.Content)) { vm.UrlState = UrlPreviewState.Failed; return; }
        vm.UrlState = UrlPreviewState.Loading;
        var id = vm.Id;
        var url = vm.Content;
        _ = Task.Run(async () =>
        {
            await _urlFetchSemaphore.WaitAsync();
            try
            {
                var (title, favicon) = await _urlService!.FetchAsync(url);
                _db!.UpsertUrlCache(url, title, favicon);
                Dispatcher.Invoke(() =>
                {
                    _popupVm!.UpdateUrlPreview(id, title, favicon);
                    vm.UrlState = title != null || favicon != null
                        ? UrlPreviewState.Loaded
                        : UrlPreviewState.Failed;
                });
            }
            catch (Exception ex)
            {
                LogError("URL-Vorschau neu laden", ex);
                Dispatcher.Invoke(() => vm.UrlState = UrlPreviewState.Failed);
            }
            finally
            {
                _urlFetchSemaphore.Release();
            }
        });
    }

    private bool TryWriteEntryToClipboard(EntryViewModel vm, bool plainText)
    {
        if (vm.Type == EntryType.Image)
        {
            // LoadAllLite skips the ImageData BLOB for performance — fetch on demand.
            if (vm.Entry.ImageData == null)
                vm.Entry.ImageData = _db?.LoadImageData(vm.Id);

            if (vm.Entry.ImageData != null)
            {
                var src = ImageUtils.LoadBitmap(vm.Entry.ImageData);
                if (src == null) return false;
                return TrySetClipboardImage(src);
            }
        }
        var text = plainText
            ? ClipboardProcessor.GetPlainText(vm.Entry)
            : (vm.Content ?? vm.Entry.OcrText ?? "");

        if (!plainText && vm.Entry.HasHtml)
        {
            // LoadAllLite skips the HTML column as well; fetch it only when pasting with formatting.
            vm.Entry.HtmlContent ??= _db?.LoadHtml(vm.Id);
            if (vm.Entry.HtmlContent != null)
            {
                var html = ClipboardProcessor.BuildClipboardHtml(vm.Entry.HtmlContent, vm.Entry.SourceUrl);
                return TryClipboardWrite(() =>
                {
                    var data = new System.Windows.DataObject();
                    data.SetData(System.Windows.DataFormats.UnicodeText, text);
                    data.SetData(System.Windows.DataFormats.Html, html);
                    System.Windows.Clipboard.SetDataObject(data, copy: true);
                });
            }
        }
        return TrySetClipboardText(text);
    }

    internal void PasteEntry(EntryViewModel vm, bool plainText)
    {
        _ignoreNextClipboard = true;
        if (!TryWriteEntryToClipboard(vm, plainText))
        {
            _ignoreNextClipboard = false;
            _trayIcon?.ShowBalloonTip(3000, AppNames.App, ClipboardBusyMessage, ToolTipIcon.Warning);
            return;
        }

        RecordEntryUsed(vm);
        HidePopup();

        var target = _previousForeground;
        _ = Task.Delay(PasteDelayMs).ContinueWith(_ => Dispatcher.Invoke(() =>
        {
            if (target != IntPtr.Zero) NativeMethods.SetForegroundWindow(target);
            NativeMethods.SendCtrlV();
        }));
    }

    internal void CopyColorToClipboard(string hex)
    {
        _ignoreNextClipboard = true;
        if (!TrySetClipboardText(hex))
            _ignoreNextClipboard = false;
    }

    /// <summary>Copies text without adding it to the history (used for KB summaries).</summary>
    internal bool CopyTextWithoutHistory(string text)
    {
        _ignoreNextClipboard = true;
        if (TrySetClipboardText(text)) return true;
        _ignoreNextClipboard = false;
        return false;
    }

    internal void CopyEntryToClipboard(EntryViewModel vm, bool plainText)
    {
        _ignoreNextClipboard = true;
        if (!TryWriteEntryToClipboard(vm, plainText))
        {
            _ignoreNextClipboard = false;
            _trayIcon?.ShowBalloonTip(3000, AppNames.App, ClipboardBusyMessage, ToolTipIcon.Warning);
            return;
        }
        RecordEntryUsed(vm);
    }

    private void RecordEntryUsed(EntryViewModel vm)
    {
        try
        {
            var usedAt = _db?.IncrementUseCount(vm.Id) ?? DateTime.Now;
            vm.MarkUsed(usedAt);
        }
        catch (Exception ex)
        {
            LogCrash(ex);
        }
    }

    private static byte[]? GenerateThumbnail120(byte[]? imageData)
    {
        if (imageData == null) return null;
        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new System.IO.MemoryStream(imageData);
            bmp.DecodePixelWidth = 120;
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var ms = new System.IO.MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }
        catch (Exception ex) when (ImageUtils.IsDecodeFailure(ex)) { return null; }
        catch (Exception ex)
        {
            LogError("Vorschaubild erzeugen", ex);
            return null;
        }
    }

    internal static bool TrySetClipboardText(string text)
        => TryClipboardWrite(() => System.Windows.Clipboard.SetText(text));

    internal static bool TrySetClipboardImage(System.Windows.Media.Imaging.BitmapSource image)
        => TryClipboardWrite(() =>
        {
            using var bitmap = ImageUtils.CreateOpaqueDrawingBitmap(image);
            System.Windows.Forms.Clipboard.SetImage(bitmap);
        });

    private static bool TryClipboardWrite(Action write)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                write();
                return true;
            }
            catch (Exception ex) when (attempt < 4)
            {
                LogCrash(ex);
                Thread.Sleep(30);
            }
            catch (Exception ex)
            {
                LogCrash(ex);
                return false;
            }
        }
        return false;
    }

    internal void OpenSettings()
    {
        var win = new SettingsWindow(this);
        win.Show();
    }

    internal void TogglePinboard()
    {
        if (_pinboard == null || !_pinboard.IsLoaded)
        {
            _pinboard = new Views.PinboardWindow(_popupVm!, this);
            _pinboard.Closed += (_, _) =>
            {
                _pinboard = null;
                if (_pinboardMenuItem != null) _pinboardMenuItem.Checked = false;
            };
        }

        if (_pinboard.IsVisible)
        {
            _pinboard.Hide();
            if (_pinboardMenuItem != null) _pinboardMenuItem.Checked = false;
        }
        else
        {
            _pinboard.Show();
            _pinboard.Activate();
            if (_pinboardMenuItem != null) _pinboardMenuItem.Checked = true;
        }
    }

    internal DesktopRecorderService Recorder => _recorder!;

    /// <summary>Raised on the UI thread whenever desktop recording starts or stops.</summary>
    internal event EventHandler? RecordingStateChanged;

    internal void ToggleRecording()
    {
        if (_recorder == null) return;
        if (_recorder.IsRecording)
        {
            // Stop may wait briefly for a UI Automation call in flight; keep the UI thread responsive.
            _ = Task.Run(_recorder.Stop);
            return;
        }

        try
        {
            _recorder.Start();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogCrash(ex);
            MessageBox.Show($"Die Aufnahme konnte nicht gestartet werden: {ex.Message}", AppNames.App,
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnRecordingStateChanged()
    {
        var recording = _recorder?.IsRecording == true;
        if (_recordMenuItem != null)
        {
            _recordMenuItem.Checked = recording;
            _recordMenuItem.Text = recording ? "Aufnahme beenden" : "Desktop aufzeichnen";
        }
        if (_trayIcon != null)
        {
            _trayIcon.Text = recording ? $"{AppNames.App} - Aufnahme läuft" : AppNames.App;
            if (recording)
                _trayIcon.ShowBalloonTip(3000, "Desktop-Aufnahme läuft",
                    $"Sichtbarer Text des Vordergrundfensters wird als Markdown gespeichert. Beenden über Tray, Popup oder {AppNames.KnowledgeBase}.",
                    ToolTipIcon.Info);
        }
        RecordingStateChanged?.Invoke(this, EventArgs.Empty);
    }

    internal IReadOnlyList<ClipboardEntry> SnapshotEntries()
        => _popupVm?.Entries.Select(e => e.Entry).ToList() ?? [];

    internal void OpenKnowledgeBase()
    {
        if (_knowledgeBase == null || !_knowledgeBase.IsLoaded)
        {
            _knowledgeBase = new KnowledgeBaseWindow(this);
            _knowledgeBase.Closed += (_, _) => _knowledgeBase = null;
        }
        _knowledgeBase.Show();
        if (_knowledgeBase.WindowState == WindowState.Minimized) _knowledgeBase.WindowState = WindowState.Normal;
        _knowledgeBase.Activate();
    }

    /// <summary>Raised after tasks.json was written, so an open KB window can reload its task list.</summary>
    internal event EventHandler? TasksChanged;

    internal void NotifyTasksChanged() => TasksChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Adds a history entry as a task and shows it in the Aufgaben section of the KB.</summary>
    internal void AddTaskFromEntry(long id)
    {
        var entry = _popupVm?.Entries.FirstOrDefault(e => e.Id == id)?.Entry;
        if (entry == null) return;
        var task = TaskStore.FromEntry(entry, KnowledgeBaseWindow.ClipboardKey(entry.Id), DateTime.Now);
        try
        {
            var tasks = TaskStore.Load(AppPaths.TasksFile);
            tasks.Add(task);
            TaskStore.Save(AppPaths.TasksFile, tasks);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogCrash(ex);
            MessageBox.Show($"Aufgabe konnte nicht gespeichert werden: {ex.Message}", AppNames.App, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        NotifyTasksChanged();
        OpenKnowledgeBase();
        _knowledgeBase?.OpenTasks(task.Id);
    }

    internal DictationService Dictation => _dictation!;

    internal SemanticIndex Semantic => _semantic!;
    internal LocalAiService LocalAi => _localAi!;

    private Views.ClassifierReportWindow? _classifierReport;

    internal void OpenCategorizationReport(Window owner)
    {
        if (_classifierReport is { IsLoaded: true })
        {
            _classifierReport.Activate();
            return;
        }
        _classifierReport = new Views.ClassifierReportWindow(LocalAi.E5, CurrentSettings, SaveSettings, categorize: async keys =>
        {
            OpenKnowledgeBase();
            if (_knowledgeBase != null) await _knowledgeBase.CategorizeKeysAsync(keys);
        }) { Owner = owner };
        _classifierReport.Closed += (_, _) => _classifierReport = null;
        _classifierReport.Show();
    }

    /// <summary>Counts what happened to a shown suggestion; saved with the next settings write.</summary>
    internal void RecordCategorizationFeedback(LocalAiSuggestion? suggestion, string appliedCategory)
    {
        if (CategorizationReporting.Feedback(suggestion, appliedCategory, DateTime.Now) is not { } feedback) return;
        CategorizationReporting.Append(CurrentSettings.CategorizationFeedback ??= [], feedback, CategorizationReporting.MaxFeedback);
    }

    /// <summary>Raised on the UI thread whenever the clipboard history gains, loses or reorders entries.</summary>
    internal event System.Collections.Specialized.NotifyCollectionChangedEventHandler? HistoryChanged
    {
        add => _popupVm!.Entries.CollectionChanged += value;
        remove => _popupVm!.Entries.CollectionChanged -= value;
    }

    /// <summary>Deletes history entries as the popup does (database row plus list entry).</summary>
    internal void DeleteHistoryEntries(IEnumerable<long> ids)
    {
        var targets = new HashSet<long>(ids);
        foreach (var vm in _popupVm!.Entries.Where(e => targets.Contains(e.Id)).ToList()) _popupVm.Delete(vm);
    }

    /// <summary>Opens the detail window for a history entry; false when it is no longer in the history.</summary>
    internal bool OpenEntryDetails(long id)
    {
        var vm = _popupVm?.Entries.FirstOrDefault(e => e.Id == id);
        if (vm == null) return false;
        if (vm.Type == EntryType.Image && vm.Entry.ImageData == null) vm.Entry.ImageData = _db?.LoadImageData(vm.Id);
        new DetailWindow(vm) { WindowStartupLocation = WindowStartupLocation.CenterScreen }.Show();
        return true;
    }

    /// <summary>Raised after a note was exported into the KB, so an open KB window can reload.</summary>
    internal event EventHandler? KnowledgeBaseChanged;

    internal void NotifyKnowledgeBaseChanged() => KnowledgeBaseChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Saves a quick note (popup or KB window) as a regular note and puts it into the KB right away.
    /// Returns the KB file path. IO errors are left to the caller.
    /// </summary>
    internal string SaveQuickNote(string text, ContentMetadata? metadata = null)
    {
        var (_, kbPath) = NoteStore.CreateInKb(AppPaths.NotesDir, AppPaths.KbNotesDir, text, DateTime.Now, metadata);
        NotifyKnowledgeBaseChanged();
        return kbPath;
    }

    internal void OpenVoiceNotes(string? initialText = null)
    {
        OpenKnowledgeBase();
        _knowledgeBase!.OpenVoiceNotes(initialText);
    }

    /// <summary>
    /// Starts dictation for <paramref name="owner"/>, or stops it and returns the transcript.
    /// Returns null when nothing was transcribed (started, cancelled, failed or busy elsewhere).
    /// </summary>
    internal async Task<string?> ToggleDictationAsync(object owner, Window ownerWindow)
    {
        var dictation = Dictation;
        if (dictation.IsBusyFor(owner))
        {
            MessageBox.Show(ownerWindow, "Ein anderes Fenster diktiert gerade. Bitte dort zuerst beenden.", AppNames.App,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        try
        {
            switch (dictation.State)
            {
                case DictationState.Recording:
                    return await dictation.StopAndTranscribeAsync();
                case DictationState.Downloading:
                case DictationState.Transcribing:
                    return null;
            }

            if (!dictation.IsModelAvailable)
            {
                var size = DictationService.ApproxModelSizeMb(dictation.ModelName);
                var answer = MessageBox.Show(ownerWindow,
                    $"Für das Diktat wird das Whisper-Sprachmodell '{dictation.ModelName}' (ca. {size} MB) einmalig heruntergeladen " +
                    $"und unter {AppPaths.ModelsDir} gespeichert. Die Spracherkennung läuft danach komplett lokal.\n\nJetzt herunterladen?",
                    $"{AppNames.App} - {AppNames.VoiceNotes}", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return null;
                await dictation.DownloadModelAsync(owner);
            }

            dictation.StartRecording(owner);
            return null;
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            dictation.Cancel();
            MessageBox.Show(ownerWindow, $"Diktat fehlgeschlagen: {ex.Message}", AppNames.App,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    private void ExitApp()
    {
        if (_trayIcon != null) _trayIcon.Visible = false;
        _recorder?.Stop();
        _knowledgeBase?.Close();
        _pinboard?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        _showEventCts?.Cancel();
        _showEvent?.Dispose();
        _legacyShowEvent?.Dispose();
        _showEventCts?.Dispose();
        if (_ownsLegacyInstance) _legacyInstanceMutex?.ReleaseMutex();
        _legacyInstanceMutex?.Dispose();
        if (_ownsSingleInstance) _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        _trayIcon?.Dispose();
        StopMailInbox();
        StopBackgroundWork();
        StopKnowledgeFolder();
        StopMyKnowledge();
        _msgWin?.Dispose();
        _recorder?.Dispose();
        _dictation?.Dispose();
        _localAi?.Dispose();
        _aiExecution?.Dispose();
        _aiResources?.Dispose();
        AiCpuWork.Resources = null;
        _semantic?.Dispose();
        _db?.Dispose();
        _urlService?.Dispose();
        _urlFetchSemaphore.Dispose();
        base.OnExit(e);
    }

    private void RunAutoBackupIfNeeded()
    {
        try
        {
            (bool Enabled, string Directory, DateTime? LastRun) snapshot = Dispatcher.Invoke(() =>
            {
                lock (_settingsMutationLock)
                {
                    var s = _settings?.Settings;
                    return s == null
                        ? (Enabled: false, Directory: "", LastRun: (DateTime?)null)
                        : (Enabled: s.AutoBackupEnabled, Directory: s.AutoBackupDirectory, LastRun: s.LastAutoBackupDate);
                }
            });

            if (!snapshot.Enabled || string.IsNullOrWhiteSpace(snapshot.Directory))
                return;

            var lastRun = snapshot.LastRun;
            if (lastRun.HasValue && (DateTime.Today - lastRun.Value.Date).TotalDays < 1)
                return;

            _db!.AutoBackupIfNeeded(snapshot.Directory);
            Dispatcher.Invoke(() =>
            {
                lock (_settingsMutationLock)
                {
                    var s = _settings?.Settings;
                    if (s == null) return;
                    s.LastAutoBackupDate = DateTime.Now;
                    _settings?.Save();
                }
            });
        }
        catch (Exception ex)
        {
            LogCrash(ex);
        }
    }

    internal static string FormatHotkey(uint modifiers, uint vk)
    {
        var parts = new List<string>();
        if ((modifiers & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((modifiers & NativeMethods.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(vk switch
        {
            >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)vk).ToString(),
            >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
            _ => $"VK {vk:X2}",
        });
        return string.Join("+", parts);
    }
}

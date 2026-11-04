using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using ClipwellWin.Models;
using ClipwellWin.Services;
using ClipwellWin.ViewModels;
using ClipwellWin.Views;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using AppThemeMode = ClipwellWin.Models.ThemeMode;

namespace ClipwellWin;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = "Local\\ClipwellWin.SingleInstance";
    private const string ShowEventName = "Local\\ClipwellWin.ShowPopup";
    private const string StartupRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupRunValueName = "Clipwell";
    private const string OnboardingSeenFileName = "onboarding.seen";
    private static readonly object LogLock = new();
    private readonly object _settingsMutationLock = new();
    private const int PasteDelayMs = 140;
    private const string ClipboardBusyMessage = "Clipboard konnte gerade nicht geschrieben werden.";
    private enum PopupPlacement { Cursor, Taskbar }

    private NotifyIcon? _trayIcon;
    private ToolStripMenuItem? _pinboardMenuItem;
    private MessageWindowService? _msgWin;
    private DatabaseService? _db;
    private SettingsService? _settings;
    private UrlPreviewService? _urlService;
    private PopupViewModel? _popupVm;
    private PopupWindow? _popup;
    private PinboardWindow? _pinboard;
    private OnboardingWindow? _onboarding;
    private Mutex? _singleInstanceMutex;
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

        _settings = new SettingsService();
        _settings.Load();
        ApplyStartWithWindowsSetting();
        _db = OpenDatabaseWithRecovery();
        if (_db == null)
        {
            Shutdown();
            return;
        }
        _urlService = new UrlPreviewService();

        ApplyTheme(_settings.Settings.Theme);
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

        _popupVm = new PopupViewModel(
            _db,
            () => _settings.Settings.MaxHistoryItems,
            () => _settings.Settings.MaxAgeInDays);
        _popupVm.LoadFromDb();

        _msgWin = new MessageWindowService();
        _msgWin.Initialize();
        _msgWin.ClipboardChanged += OnClipboardChanged;
        _msgWin.HotkeyPressed += OnHotkeyPressed;

        SetupTray();

        if (!RegisterConfiguredHotkey())
            ShowHotkeyRegistrationWarning();

        _ = Task.Run(() => RunAutoBackupIfNeeded());

        if (MarkOnboardingSeenIfNeeded())
        {
            Dispatcher.InvokeAsync(() =>
            {
                _onboarding = new OnboardingWindow(_settings.Settings, this);
                _onboarding.Closed += (_, _) => _onboarding = null;
                _onboarding.Show();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
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
                File.AppendAllText(
                    Path.Combine(AppPaths.DataDir, "clipwell.log"),
                    $"[{DateTime.Now:O}] {ex}\r\n\r\n");
            }
        }
        catch { }
    }

    private bool StartSingleInstance()
    {
        _singleInstanceMutex = new Mutex(false, SingleInstanceMutexName, out var createdNew);
        _ownsSingleInstance = createdNew;
        if (!createdNew)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); }
            catch { }
            return false;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showEventCts = new CancellationTokenSource();
        var token = _showEventCts.Token;
        _ = Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_showEvent.WaitOne(500))
                        Dispatcher.Invoke(ShowPopup);
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
                "Die Clipwell-Datenbank ist defekt oder gesperrt. Soll Clipwell die Datei sichern und mit einer neuen History starten?",
                "Clipwell Datenbank-Recovery",
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
                    $"Die Datenbank konnte nicht zurückgesetzt werden. Details stehen in {Path.Combine(DatabaseService.DataDir, "clipwell.log")}.",
                    "Clipwell",
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
        ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light);
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

    private static void ApplyPopupResources(bool dark)
    {
        var res = Current.Resources;
        if (dark)
        {
            res["PopupBackgroundBrush"] = Brush("#FF171A1E");
            res["PopupBorderBrush"] = Brush("#FF3A414A");
            res["PopupPanelBrush"] = Brush("#FF23272D");
            res["PopupPanelBorderBrush"] = Brush("#FF3A414A");
            res["PopupTextPrimaryBrush"] = Brush("#FFF7F8FA");
            res["PopupTextSecondaryBrush"] = Brush("#D8F7F8FA");
            res["PopupTextMutedBrush"] = Brush("#A8F7F8FA");
            res["PopupTextSubtleBrush"] = Brush("#8AF7F8FA");
            res["PopupTextFaintBrush"] = Brush("#66F7F8FA");
            res["PopupHoverBrush"] = Brush("#FF2A3037");
            res["PopupSelectedBrush"] = Brush("#FF303740");
            res["PopupChipBrush"] = Brush("#FF2B3037");
            res["PopupChipCheckedBrush"] = Brush("#FF4A535E");
            res["PopupBadgeBrush"] = Brush("#FF4A535E");
            res["PopupBadgeSecondaryBrush"] = Brush("#FF343B43");
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
            res["OnboardingBackgroundBrush"] = Brush("#FF171A1E");
            res["OnboardingSurfaceBrush"] = Brush("#FF23272D");
            res["OnboardingSurfaceAltBrush"] = Brush("#FF1D2126");
            res["OnboardingBorderBrush"] = Brush("#FF3A414A");
            res["OnboardingTextPrimaryBrush"] = Brush("#FFF7F8FA");
            res["OnboardingTextSecondaryBrush"] = Brush("#D8F7F8FA");
            res["OnboardingTextMutedBrush"] = Brush("#A8F7F8FA");
            res["OnboardingInputBrush"] = Brush("#FF111418");
            res["OnboardingInputFocusedBrush"] = Brush("#FF151A20");
            res["OnboardingButtonBrush"] = Brush("#FFEEF1F5");
            res["OnboardingButtonHoverBrush"] = Brush("#FFFFFFFF");
            res["OnboardingButtonTextBrush"] = Brush("#FF15171A");
            res["OnboardingAccentBrush"] = Brush("#FF9BCB3F");
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
            res["OnboardingAccentBrush"] = Brush("#FF7BA82A");
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
            Text = "Clipwell",
            Icon = CreateTrayIcon(),
            Visible = true,
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Öffnen", null, (_, _) => ShowPopup());
        menu.Items.Add(new ToolStripSeparator());
        _pinboardMenuItem = new ToolStripMenuItem("Pinboard");
        _pinboardMenuItem.Click += (_, _) => TogglePinboard();
        menu.Items.Add(_pinboardMenuItem);
        menu.Items.Add("Einstellungen", null, (_, _) => OpenSettings());
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
            "Clipwell Hotkey nicht aktiv",
            $"Die Tastenkombination ist schon belegt oder wurde von Windows blockiert. Fehlercode: {error}",
            ToolTipIcon.Warning);
    }

    private void ShowHotkeyFallbackWarning(uint requestedModifiers, uint requestedVk)
    {
        _trayIcon?.ShowBalloonTip(
            7000,
            "Clipwell Hotkey-Fallback aktiv",
            $"{FormatHotkey(requestedModifiers, requestedVk)} ist belegt. Fuer diese Sitzung wurde {FormatHotkey(_registeredHotkeyModifiers, _registeredHotkeyVk)} registriert; die Einstellung wurde nicht ueberschrieben.",
            ToolTipIcon.Warning);
    }

    private static Icon CreateTrayIcon()
    {
        using var bmp = RenderTrayBitmap(96);
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

    internal static ImageSource CreateAppIconImageSource(int size)
    {
        using var bmp = RenderTrayBitmap(size);
        using var stream = new MemoryStream();
        bmp.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        stream.Position = 0;

        var image = new System.Windows.Media.Imaging.BitmapImage();
        image.BeginInit();
        image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    internal static void ApplyAppIcon(Window window)
    {
        window.Icon = CreateAppIconImageSource(32);
    }

    private static void FillRoundedRect(Graphics g, System.Drawing.Brush br, float x, float y, float w, float h, float r)
    {
        using var path = RoundedRect(x, y, w, h, r);
        g.FillPath(br, path);
    }

    private static Bitmap RenderTrayBitmap(int size)
    {
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(System.Drawing.Color.Transparent);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

        float s = size / 56f;
        g.TranslateTransform(-4 * s, -4 * s);
        using var shadow = new SolidBrush(System.Drawing.Color.FromArgb(90, 0, 0, 0));
        FillRoundedRect(g, shadow, 13 * s, 15 * s, 40 * s, 42 * s, 8 * s);

        using (var boardPath = RoundedRect(10 * s, 12 * s, 44 * s, 44 * s, 8 * s))
        using (var boardBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                   new RectangleF(10 * s, 12 * s, 44 * s, 44 * s),
                   System.Drawing.Color.FromArgb(255, 67, 76, 82),
                   System.Drawing.Color.FromArgb(255, 18, 24, 28),
                   90f))
        using (var boardPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(230, 120, 133, 140), 1.4f * s))
        {
            g.FillPath(boardBrush, boardPath);
            g.DrawPath(boardPen, boardPath);
        }

        using (var paperPath = RoundedRect(16 * s, 16 * s, 34 * s, 38 * s, 3 * s))
        using (var paperBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                   new RectangleF(16 * s, 16 * s, 34 * s, 38 * s),
                   System.Drawing.Color.White,
                   System.Drawing.Color.FromArgb(255, 230, 234, 238),
                   90f))
        using (var paperPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(210, 40, 45, 48), 1f * s))
        {
            g.FillPath(paperBrush, paperPath);
            g.DrawPath(paperPen, paperPath);
        }

        using (var curlPath = new GraphicsPath())
        {
            curlPath.AddBezier(39 * s, 54 * s, 46 * s, 53 * s, 48 * s, 47 * s, 50 * s, 44 * s);
            curlPath.AddLine(50 * s, 44 * s, 50 * s, 54 * s);
            curlPath.CloseFigure();
            using var curlBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                new RectangleF(39 * s, 44 * s, 11 * s, 10 * s),
                System.Drawing.Color.White,
                System.Drawing.Color.FromArgb(255, 178, 188, 199),
                135f);
            g.FillPath(curlBrush, curlPath);
        }

        DrawClip(g, s);
        DrawDocumentLines(g, s);
        DrawHeart(g, s);

        return bmp;
    }

    private static void DrawClip(Graphics g, float s)
    {
        using var clipShadow = new SolidBrush(System.Drawing.Color.FromArgb(120, 0, 0, 0));
        FillRoundedRect(g, clipShadow, 24 * s, 12 * s, 22 * s, 8 * s, 2 * s);

        using var basePath = RoundedRect(23 * s, 9 * s, 22 * s, 10 * s, 2 * s);
        using var baseBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
            new RectangleF(23 * s, 9 * s, 22 * s, 10 * s),
            System.Drawing.Color.FromArgb(255, 248, 252, 253),
            System.Drawing.Color.FromArgb(255, 93, 107, 116),
            90f);
        using var basePen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(230, 37, 46, 51), 1f * s);
        g.FillPath(baseBrush, basePath);
        g.DrawPath(basePen, basePath);

        using var loopPath = new GraphicsPath();
        loopPath.AddEllipse(29 * s, 5 * s, 12 * s, 9 * s);
        loopPath.AddEllipse(32 * s, 7 * s, 6 * s, 5 * s);
        loopPath.FillMode = System.Drawing.Drawing2D.FillMode.Alternate;
        using var loopBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
            new RectangleF(29 * s, 5 * s, 12 * s, 9 * s),
            System.Drawing.Color.FromArgb(255, 242, 247, 249),
            System.Drawing.Color.FromArgb(255, 66, 78, 86),
            90f);
        g.FillPath(loopBrush, loopPath);
        g.DrawPath(basePen, loopPath);
    }

    private static void DrawDocumentLines(Graphics g, float s)
    {
        using var linePen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(190, 124, 130, 133), 1.3f * s)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
        };
        g.DrawLine(linePen, 21 * s, 27 * s, 40 * s, 27 * s);
        g.DrawLine(linePen, 21 * s, 31 * s, 44 * s, 31 * s);
        g.DrawLine(linePen, 21 * s, 35 * s, 39 * s, 35 * s);
    }

    private static void DrawHeart(Graphics g, float s)
    {
        using var heartPath = new GraphicsPath();
        heartPath.AddBezier(40 * s, 38 * s, 35 * s, 32 * s, 28 * s, 37 * s, 34 * s, 45 * s);
        heartPath.AddBezier(34 * s, 45 * s, 36 * s, 48 * s, 40 * s, 51 * s, 40 * s, 51 * s);
        heartPath.AddBezier(40 * s, 51 * s, 44 * s, 48 * s, 46 * s, 45 * s, 46 * s, 45 * s);
        heartPath.AddBezier(46 * s, 45 * s, 52 * s, 37 * s, 45 * s, 32 * s, 40 * s, 38 * s);
        heartPath.CloseFigure();

        using var heartBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
            new RectangleF(30 * s, 34 * s, 20 * s, 18 * s),
            System.Drawing.Color.FromArgb(255, 255, 91, 91),
            System.Drawing.Color.FromArgb(255, 185, 0, 16),
            90f);
        using var heartPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(230, 120, 0, 10), 1f * s);
        g.FillPath(heartBrush, heartPath);
        g.DrawPath(heartPen, heartPath);

        using var shineBrush = new SolidBrush(System.Drawing.Color.FromArgb(190, 255, 255, 255));
        g.FillEllipse(shineBrush, 33 * s, 36 * s, 5 * s, 3 * s);
    }

    private static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        var path = new GraphicsPath();
        path.AddArc(x, y, r * 2, r * 2, 180, 90);
        path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
        path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
        path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void ShowPopup(PopupPlacement placement = PopupPlacement.Cursor)
    {
        _previousForeground = NativeMethods.GetForegroundWindow();

        if (_popup == null || !_popup.IsLoaded)
            _popup = new PopupWindow(_popupVm!, this);
        _popup.ApplyWindowTheme();

        if (_popup.IsVisible)
        {
            PositionPopup(placement);
            _popup.FocusSearch();
            _popup.Activate();
            return;
        }

        PositionPopup(placement);
        _popup.Show();
        PositionPopup(placement);
        _popup.FocusSearch();
        _popup.Activate();
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

    internal IntPtr PreviousForeground => _previousForeground;

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

        var entry = ClipboardProcessor.BuildEntry(_settings.Settings.CodeDetectionMode);
        if (entry == null) return;

        if (entry.Type == EntryType.Image)
            entry.ThumbnailData = GenerateThumbnail120(entry.ImageData);

        _popupVm!.AddEntry(entry);

        if (_settings.Settings.MaxSizeInMb > 0)
            _db!.PurgeBySizeIfNeeded(_settings.Settings.MaxSizeInMb);

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
                catch
                {
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
            catch
            {
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
                var src = LoadBitmapSource(vm.Entry.ImageData);
                if (src == null) return false;
                return TrySetClipboardImage(src);
            }
        }
        var text = plainText
            ? ClipboardProcessor.GetPlainText(vm.Entry)
            : (vm.Content ?? vm.Entry.OcrText ?? "");
        return TrySetClipboardText(text);
    }

    internal void PasteEntry(EntryViewModel vm, bool plainText)
    {
        _ignoreNextClipboard = true;
        if (!TryWriteEntryToClipboard(vm, plainText))
        {
            _ignoreNextClipboard = false;
            _trayIcon?.ShowBalloonTip(3000, "Clipwell", ClipboardBusyMessage, ToolTipIcon.Warning);
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

    internal void CopyEntryToClipboard(EntryViewModel vm, bool plainText)
    {
        _ignoreNextClipboard = true;
        if (!TryWriteEntryToClipboard(vm, plainText))
        {
            _ignoreNextClipboard = false;
            _trayIcon?.ShowBalloonTip(3000, "Clipwell", ClipboardBusyMessage, ToolTipIcon.Warning);
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
        catch { return null; }
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

    private static System.Windows.Media.Imaging.BitmapSource? LoadBitmapSource(byte[] data)
    {
        try
        {
            var img = new System.Windows.Media.Imaging.BitmapImage();
            img.BeginInit();
            img.StreamSource = new System.IO.MemoryStream(data);
            img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch { return null; }
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

    private void ExitApp()
    {
        if (_trayIcon != null) _trayIcon.Visible = false;
        _pinboard?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        _showEventCts?.Cancel();
        _showEvent?.Dispose();
        _showEventCts?.Dispose();
        if (_ownsSingleInstance) _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        _trayIcon?.Dispose();
        _msgWin?.Dispose();
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

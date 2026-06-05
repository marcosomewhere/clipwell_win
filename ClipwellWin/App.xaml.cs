using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
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

namespace ClipwellWin;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = "Local\\ClipwellWin.SingleInstance";
    private const string ShowEventName = "Local\\ClipwellWin.ShowPopup";
    private static readonly object LogLock = new();

    private NotifyIcon? _trayIcon;
    private ToolStripMenuItem? _hotkeyStatusItem;
    private ToolStripMenuItem? _incognitoItem;
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
        UpdateHotkeyTrayStatus();

        _ = Task.Run(() => RunAutoBackupIfNeeded());

        if (!_settings.Settings.FirstRunCompleted)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _onboarding = new OnboardingWindow(_settings.Settings, this);
                _onboarding.Closed += (_, _) => _onboarding = null;
                _onboarding.Show();
            }, System.Windows.Threading.DispatcherPriority.Background);
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
            e.Handled = true;
        };
    }

    private static void LogCrash(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(DatabaseService.DataDir);
            lock (LogLock)
            {
                File.AppendAllText(
                    Path.Combine(DatabaseService.DataDir, "clipwell.log"),
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

    internal void ApplyTheme(ThemeMode mode)
    {
        bool dark;
        if (mode == ThemeMode.System)
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
            dark = mode == ThemeMode.Dark;
        }

        _isEffectiveDarkTheme = dark;
        ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light);
        ApplyPopupResources(dark);
        ApplyCompactResources(mode == ThemeMode.Compact);
        _popup?.ApplyWindowTheme();
    }

    internal bool IsEffectiveDarkTheme => _isEffectiveDarkTheme;

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General &&
            e.Category != UserPreferenceCategory.Color)
            return;

        if (_settings?.Settings.Theme == ThemeMode.System)
            Dispatcher.InvokeAsync(() => ApplyTheme(ThemeMode.System));
    }

    private static void ApplyPopupResources(bool dark)
    {
        var res = Current.Resources;
        if (dark)
        {
            res["PopupBackgroundBrush"] = Brush("#F21E1E2E");
            res["PopupBorderBrush"] = Brush("#22FFFFFF");
            res["PopupPanelBrush"] = Brush("#0AFFFFFF");
            res["PopupPanelBorderBrush"] = Brush("#15FFFFFF");
            res["PopupTextPrimaryBrush"] = Brush("#FFFFFFFF");
            res["PopupTextSecondaryBrush"] = Brush("#CCFFFFFF");
            res["PopupTextMutedBrush"] = Brush("#88FFFFFF");
            res["PopupTextSubtleBrush"] = Brush("#66FFFFFF");
            res["PopupTextFaintBrush"] = Brush("#55FFFFFF");
            res["PopupHoverBrush"] = Brush("#18FFFFFF");
            res["PopupSelectedBrush"] = Brush("#28FFFFFF");
            res["PopupChipBrush"] = Brush("#18FFFFFF");
            res["PopupChipCheckedBrush"] = Brush("#44FFFFFF");
            res["PopupBadgeBrush"] = Brush("#2AFFFFFF");
            res["PopupBadgeSecondaryBrush"] = Brush("#18FFFFFF");
            res["PopupShadowBrush"] = Brush("#FF000000");
        }
        else
        {
            res["PopupBackgroundBrush"] = Brush("#F8FAFAF8");
            res["PopupBorderBrush"] = Brush("#33000000");
            res["PopupPanelBrush"] = Brush("#0D000000");
            res["PopupPanelBorderBrush"] = Brush("#16000000");
            res["PopupTextPrimaryBrush"] = Brush("#F0181818");
            res["PopupTextSecondaryBrush"] = Brush("#CC202020");
            res["PopupTextMutedBrush"] = Brush("#8A202020");
            res["PopupTextSubtleBrush"] = Brush("#66202020");
            res["PopupTextFaintBrush"] = Brush("#55202020");
            res["PopupHoverBrush"] = Brush("#10000000");
            res["PopupSelectedBrush"] = Brush("#18000000");
            res["PopupChipBrush"] = Brush("#0E000000");
            res["PopupChipCheckedBrush"] = Brush("#1F000000");
            res["PopupBadgeBrush"] = Brush("#14000000");
            res["PopupBadgeSecondaryBrush"] = Brush("#0C000000");
            res["PopupShadowBrush"] = Brush("#66000000");
        }
    }

    private static SolidColorBrush Brush(string color)
        => new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));

    private static void ApplyCompactResources(bool compact)
    {
        var res = Current.Resources;
        if (compact)
        {
            res["EntryItemMargin"]       = new System.Windows.Thickness(6, 1, 6, 1);
            res["EntryContentMargin"]    = new System.Windows.Thickness(8, 5, 8, 5);
            res["EntryMinHeight"]        = 38.0;
            res["EntryFontSizeMain"]     = 11.5;
            res["EntryFontSizeMeta"]     = 10.0;
            res["EntryFontWeightMain"]   = System.Windows.FontWeights.Normal;
            res["EntryFontWeightMeta"]   = System.Windows.FontWeights.Light;
            res["FilterChipPadding"]     = new System.Windows.Thickness(8, 2, 8, 2);
            res["FilterChipFontSize"]    = 10.0;
            res["BadgeFontSize"]         = 9.0;
            res["BadgePadding"]          = new System.Windows.Thickness(4, 1, 4, 1);
        }
        else
        {
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
        _hotkeyStatusItem = new ToolStripMenuItem("Hotkey: wird geprüft") { Enabled = false };
        menu.Items.Add(_hotkeyStatusItem);
        menu.Items.Add(new ToolStripSeparator());

        var pauseItem = new ToolStripMenuItem("Überwachung pausieren") { CheckOnClick = true };
        pauseItem.CheckedChanged += (_, _) =>
        {
            _settings!.Settings.PauseMonitoring = pauseItem.Checked;
            _settings.Save();
        };
        menu.Items.Add(pauseItem);

        _incognitoItem = new ToolStripMenuItem("Inkognito-Modus") { CheckOnClick = true };
        _incognitoItem.CheckedChanged += (_, _) =>
        {
            _settings!.Settings.IncognitoMode = _incognitoItem.Checked;
            _settings.Save();
        };
        menu.Items.Add(_incognitoItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Pinboard", null, (_, _) => TogglePinboard());
        menu.Items.Add("Einstellungen", null, (_, _) => OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => ExitApp());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.MouseDoubleClick += (_, _) => ShowPopup();
    }

    private void UpdateHotkeyTrayStatus()
    {
        if (_hotkeyStatusItem == null || _settings == null || _msgWin == null) return;
        var hotkey = FormatHotkey(_settings.Settings.HotkeyModifiers, _settings.Settings.HotkeyVk);
        _hotkeyStatusItem.Text = _msgWin.IsHotkeyRegistered
            ? $"Hotkey aktiv: {hotkey}"
            : $"Hotkey blockiert: {hotkey} (Fehler {_msgWin.LastHotkeyError})";
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

    private static Icon CreateTrayIcon()
    {
        using var bmp = RenderTrayBitmap(64);
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

    private static void FillRoundedRect(Graphics g, System.Drawing.Brush br, float x, float y, float w, float h, float r)
    {
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(x, y, r * 2, r * 2, 180, 90);
        path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
        path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
        path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
        path.CloseFigure();
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

        float s = size / 64f;
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

    private void ShowPopup()
    {
        _previousForeground = NativeMethods.GetForegroundWindow();

        if (_popup == null || !_popup.IsLoaded)
            _popup = new PopupWindow(_popupVm!, this);

        if (_popup.IsVisible)
        {
            _popup.PositionAtCursor();
            _popup.FocusSearch();
            _popup.Activate();
            return;
        }

        _popup.PositionAtCursor();
        _popup.Show();
        _popup.FocusSearch();
        _popup.Activate();
    }

    internal void HidePopup() => _popup?.Hide();

    internal IntPtr PreviousForeground => _previousForeground;

    internal AppSettings CurrentSettings => _settings!.Settings;

    internal void SaveSettings() => _settings!.Save();

    internal DatabaseService Database => _db!;

    internal bool ReRegisterHotkey()
    {
        var ok = RegisterConfiguredHotkey();
        if (!ok) ShowHotkeyRegistrationWarning();
        UpdateHotkeyTrayStatus();
        return ok;
    }

    internal bool IsHotkeyRegistered => _msgWin?.IsHotkeyRegistered == true;
    internal int LastHotkeyError => _msgWin?.LastHotkeyError ?? 0;

    private bool RegisterConfiguredHotkey()
    {
        if (_msgWin == null || _settings == null) return false;

        var s = _settings.Settings;
        s.HotkeyModifiers = NormalizeHotkeyModifiers(s.HotkeyModifiers);
        if (_msgWin.RegisterHotkey(s.HotkeyModifiers, s.HotkeyVk))
        {
            _settings.Save();
            return true;
        }

        var requested = (s.HotkeyModifiers, s.HotkeyVk);
        foreach (var fallback in HotkeyFallbacks())
        {
            if (fallback == requested) continue;
            if (!_msgWin.RegisterHotkey(fallback.modifiers, fallback.vk)) continue;

            s.HotkeyModifiers = fallback.modifiers;
            s.HotkeyVk = fallback.vk;
            _settings.Save();
            return true;
        }

        s.HotkeyModifiers = requested.HotkeyModifiers;
        s.HotkeyVk = requested.HotkeyVk;
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

    private static IEnumerable<(uint modifiers, uint vk)> HotkeyFallbacks()
    {
        yield return (NativeMethods.MOD_WIN | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_V);
        yield return (NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_V);
        yield return (NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_V);
        yield return (NativeMethods.MOD_WIN | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_V);
    }

    private void OnClipboardChanged(object? sender, EventArgs e)
    {
        if (_settings!.Settings.PauseMonitoring) return;
        if (_settings.Settings.IncognitoMode) return;
        if (_ignoreNextClipboard) { _ignoreNextClipboard = false; return; }

        if (_settings.Settings.IncognitoApps.Count > 0)
        {
            var proc = NativeMethods.GetForegroundProcessName();
            if (proc != null && _settings.Settings.IncognitoApps
                .Any(a => a.Equals(proc, StringComparison.OrdinalIgnoreCase)))
                return;
        }

        var entry = ClipboardProcessor.BuildEntry(
            _settings.Settings.CodeDetectionMode,
            _settings.Settings.FilterSensitiveContent);
        if (entry == null) return;

        if (entry.Type == EntryType.Url && entry.Content != null
            && _settings.Settings.IncognitoDomains.Count > 0)
        {
            if (Uri.TryCreate(entry.Content, UriKind.Absolute, out var uri))
            {
                if (_settings.Settings.IncognitoDomains.Any(d =>
                    uri.Host.Contains(d, StringComparison.OrdinalIgnoreCase)))
                    return;
            }
        }

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
                if (!string.IsNullOrEmpty(text))
                    Dispatcher.Invoke(() => _popupVm.UpdateOcr(id, text));
            });
        }

        if (entry.Type == EntryType.Url && entry.Content != null)
        {
            var id = entry.Id;
            var url = entry.Content;

            var urlVm = _popupVm.Entries.FirstOrDefault(x => x.Id == id);
            if (urlVm != null) urlVm.UrlState = UrlPreviewState.Loading;

            _ = Task.Run(async () =>
            {
                try
                {
                    var (cachedTitle, cachedFavicon) = _db!.GetUrlCache(url);
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
            });
        }
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        _onboarding?.NotifyHotkeyTriggered();

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
        vm.UrlState = UrlPreviewState.Loading;
        var id = vm.Id;
        var url = vm.Content;
        _ = Task.Run(async () =>
        {
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
        });
    }

    internal void PasteEntry(EntryViewModel vm, bool plainText)
    {
        _ignoreNextClipboard = true;
        bool clipboardWritten;

        if (vm.Type == EntryType.Image && vm.Entry.ImageData != null)
        {
            var src = LoadBitmapSource(vm.Entry.ImageData);
            clipboardWritten = src != null && TrySetClipboardImage(src);
        }
        else
        {
            var text = plainText
                ? ClipboardProcessor.GetPlainText(vm.Entry)
                : (vm.Content ?? vm.Entry.OcrText ?? "");
            clipboardWritten = TrySetClipboardText(text);
        }

        if (!clipboardWritten)
        {
            _ignoreNextClipboard = false;
            _trayIcon?.ShowBalloonTip(3000, "Clipwell", "Clipboard konnte gerade nicht geschrieben werden.", ToolTipIcon.Warning);
            return;
        }

        HidePopup();

        var target = _previousForeground;
        _ = Task.Delay(140).ContinueWith(_ => Dispatcher.Invoke(() =>
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
        bool clipboardWritten;

        if (vm.Type == EntryType.Image && vm.Entry.ImageData != null)
        {
            var src = LoadBitmapSource(vm.Entry.ImageData);
            clipboardWritten = src != null && TrySetClipboardImage(src);
        }
        else
        {
            var text = plainText
                ? ClipboardProcessor.GetPlainText(vm.Entry)
                : (vm.Content ?? vm.Entry.OcrText ?? "");
            clipboardWritten = TrySetClipboardText(text);
        }

        if (!clipboardWritten)
        {
            _ignoreNextClipboard = false;
            _trayIcon?.ShowBalloonTip(3000, "Clipwell", "Clipboard konnte gerade nicht geschrieben werden.", ToolTipIcon.Warning);
        }
    }

    internal static bool TrySetClipboardText(string text)
        => TryClipboardWrite(() => System.Windows.Clipboard.SetText(text));

    internal static bool TrySetClipboardImage(System.Windows.Media.Imaging.BitmapSource image)
        => TryClipboardWrite(() => System.Windows.Clipboard.SetImage(image));

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
            _pinboard.Closed += (_, _) => _pinboard = null;
        }

        if (_pinboard.IsVisible)
        {
            _pinboard.Hide();
        }
        else
        {
            _pinboard.Show();
            _pinboard.Activate();
        }
    }

    private void ExitApp()
    {
        _trayIcon!.Visible = false;
        _trayIcon.Dispose();
        _msgWin?.Dispose();
        _db?.Dispose();
        _urlService?.Dispose();
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
        base.OnExit(e);
    }

    private void RunAutoBackupIfNeeded()
    {
        try
        {
            var s = _settings?.Settings;
            if (s == null || !s.AutoBackupEnabled || string.IsNullOrWhiteSpace(s.AutoBackupDirectory))
                return;

            var lastRun = s.LastAutoBackupDate;
            if (lastRun.HasValue && (DateTime.Today - lastRun.Value.Date).TotalDays < 1)
                return;

            _db!.AutoBackupIfNeeded(s.AutoBackupDirectory);
            s.LastAutoBackupDate = DateTime.Now;
            _settings!.Save();
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
        parts.Add(vk is >= 0x41 and <= 0x5A ? ((char)vk).ToString() : $"VK {vk:X2}");
        return string.Join("+", parts);
    }
}

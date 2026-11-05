using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace ClipwellWin.Services;

/// <summary>
/// Records what is visible in the foreground window as Markdown. Reads text through UI Automation only;
/// no pixels are captured, so it does not trip screen-capture detection and works without OCR.
/// </summary>
public sealed class DesktopRecorderService : IDisposable
{
    private const int PollIntervalMs = 500;
    private const int QuietPeriodMs = 500;
    private const int RecheckIntervalMs = 2500;
    // Chromium/Electron apps expose their accessibility tree only after the first UIA request.
    private const int MinLinesBeforeRetry = 5;
    private const int MaxWarmupRetries = 2;
    private const int MaxNodes = 20000;
    private const int MaxDepth = 100;
    private const int MaxTextPatternChars = 20000;
    private const int MaxSeenLinesPerWindow = 5000;
    // Rechecks of a window whose tree is expensive to read are spaced out so the target app is not kept busy.
    private const int RecheckCostFactor = 4;
    private static readonly TimeSpan StopJoinTimeout = TimeSpan.FromSeconds(3);

    private readonly string _directory;
    private readonly Func<bool> _isPaused;
    private readonly Func<bool> _contentOnly;
    private readonly object _writeLock = new();
    private readonly Dictionary<int, (string Display, string Process)?> _apps = [];

    private StreamWriter? _writer;
    private Thread? _thread;
    private CancellationTokenSource? _cts;
    private DateTime _start;
    private int _snapshotCount;

    public bool IsRecording { get; private set; }
    public string? CurrentSessionPath { get; private set; }

    /// <summary>Raised on a background thread when recording starts or stops.</summary>
    public event EventHandler? StateChanged;
    /// <summary>Raised on a background thread after a snapshot was appended to the session file.</summary>
    public event EventHandler? SnapshotWritten;

    /// <param name="contentOnly">When true, browser windows are reduced to the URL and the page content.</param>
    public DesktopRecorderService(string directory, Func<bool> isPaused, Func<bool> contentOnly)
    {
        _directory = directory;
        _isPaused = isPaused;
        _contentOnly = contentOnly;
    }

    public string Directory => _directory;

    public void Start()
    {
        lock (_writeLock)
        {
            if (IsRecording) return;
            System.IO.Directory.CreateDirectory(_directory);
            _start = DateTime.Now;
            _snapshotCount = 0;
            var path = Path.Combine(_directory, RecordingMarkdown.FileNameFor(_start));
            var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            _writer = new StreamWriter(fs, new UTF8Encoding(false)) { NewLine = "\n" };
            _writer.Write(RecordingMarkdown.SessionHeader(_start));
            _writer.Flush();
            CurrentSessionPath = path;
            IsRecording = true;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _thread = new Thread(() => Run(token)) { IsBackground = true, Name = "Clipwell Desktop Recorder" };
            // UI Automation client calls are meant for MTA threads.
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        Thread? thread;
        lock (_writeLock)
        {
            if (!IsRecording) return;
            _cts?.Cancel();
            thread = _thread;
        }

        // A hung target application can block a UI Automation call; do not wait forever for it.
        thread?.Join(StopJoinTimeout);

        lock (_writeLock)
        {
            try
            {
                _writer?.Write(RecordingMarkdown.SessionFooter(_start, DateTime.Now, _snapshotCount));
                _writer?.Flush();
            }
            catch (IOException ex) { App.LogCrash(ex); }
            _writer?.Dispose();
            _writer = null;
            _cts?.Dispose();
            _cts = null;
            _thread = null;
            IsRecording = false;
            CurrentSessionPath = null;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Stop();

    private sealed class WindowState
    {
        public readonly HashSet<string> Seen = new(StringComparer.Ordinal);
        public readonly List<string> SeenOrdered = [];
    }

    private void Run(CancellationToken token)
    {
        var ownPid = Environment.ProcessId;
        var windows = new Dictionary<string, WindowState>(StringComparer.Ordinal);

        string? currentKey = null;      // window whose state was last written
        string? currentLabel = null;
        var lastCapture = DateTime.MinValue;
        var lastCaptureCostMs = 0.0;
        string? pendingKey = null;      // foreground window waiting for its quiet period
        var pendingSince = DateTime.MinValue;
        var warmupRetries = 0;
        var loggedError = false;

        while (!token.IsCancellationRequested)
        {
            if (token.WaitHandle.WaitOne(PollIntervalMs)) break;
            try
            {
                if (_isPaused()) continue;

                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero || IsHungAppWindow(hwnd)) continue;
                GetWindowThreadProcessId(hwnd, out var pid);
                // Skip Clipwell itself: recording the KB window would feed the log back into itself.
                if (pid == 0 || pid == ownPid) continue;

                if (GetApp((int)pid) is not { } info) continue;
                var (app, process) = info;
                var isBrowser = RecordingLayout.IsBrowserProcess(process);
                var title = GetWindowTitle(hwnd);
                // Store apps all run inside the shared frame host process; the title names the actual app.
                if (app == "Application Frame Host" && title.Length > 0) app = RecordingMarkdown.CleanAppName(title);
                var key = $"{hwnd.ToInt64()}\u001f{title}";
                var now = DateTime.Now;

                if (key != currentKey)
                {
                    if (key != pendingKey)
                    {
                        pendingKey = key;
                        pendingSince = now;
                        warmupRetries = 0;
                        continue;
                    }
                    if ((now - pendingSince).TotalMilliseconds < QuietPeriodMs) continue;

                    var sw = Stopwatch.StartNew();
                    var lines = CaptureLines(hwnd, _contentOnly(), isBrowser);
                    lastCaptureCostMs = sw.Elapsed.TotalMilliseconds;
                    if (token.IsCancellationRequested) break;
                    if (lines.Count < MinLinesBeforeRetry && warmupRetries < MaxWarmupRetries)
                    {
                        warmupRetries++;
                        continue;
                    }

                    windows.TryGetValue(key, out var known);
                    var md = RecordingMarkdown.FormatSnapshot(now, app, title, lines, known?.SeenOrdered, currentLabel);
                    if (!Append(md, token)) break;
                    Remember(windows, key, lines);
                    currentKey = key;
                    currentLabel = $"{app}: {RecordingMarkdown.CleanTitle(title)}";
                    pendingKey = null;
                    lastCapture = now;
                    continue;
                }

                pendingKey = null;
                var recheckMs = Math.Max(RecheckIntervalMs, lastCaptureCostMs * RecheckCostFactor);
                if ((now - lastCapture).TotalMilliseconds < recheckMs) continue;
                lastCapture = now;

                var recheckWatch = Stopwatch.StartNew();
                var current = CaptureLines(hwnd, _contentOnly(), isBrowser);
                lastCaptureCostMs = recheckWatch.Elapsed.TotalMilliseconds;
                if (token.IsCancellationRequested) break;
                var state = windows[key];
                if (!current.Any(l => !state.Seen.Contains(l))) continue;

                var update = RecordingMarkdown.FormatSnapshot(now, app, title, current, state.SeenOrdered, null);
                if (!Append(update, token)) break;
                Remember(windows, key, current);
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException or TimeoutException)
            {
                // Windows close or change while being read; the next poll tries again.
                if (!loggedError) { App.LogCrash(ex); loggedError = true; }
            }
            catch (Exception ex)
            {
                App.LogCrash(ex);
            }
        }
    }

    private static void Remember(Dictionary<string, WindowState> windows, string key, List<string> lines)
    {
        if (!windows.TryGetValue(key, out var state))
        {
            state = new WindowState();
            windows[key] = state;
        }
        foreach (var line in lines)
        {
            if (state.Seen.Count >= MaxSeenLinesPerWindow) break;
            if (state.Seen.Add(line)) state.SeenOrdered.Add(line);
        }
    }

    private bool Append(string markdown, CancellationToken token)
    {
        lock (_writeLock)
        {
            if (_writer == null || token.IsCancellationRequested) return false;
            _writer.Write(markdown);
            _writer.Flush();
            _snapshotCount++;
        }
        SnapshotWritten?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Returns display and process name, or null for system processes that should not be recorded.</summary>
    private (string Display, string Process)? GetApp(int pid)
    {
        if (_apps.TryGetValue(pid, out var cached)) return cached;
        var name = "Unbekannt";
        var process = "";
        try
        {
            using var p = Process.GetProcessById(pid);
            // dwm owns the ghost window Windows shows for hung applications.
            if (string.Equals(p.ProcessName, "dwm", StringComparison.OrdinalIgnoreCase))
            {
                _apps[pid] = null;
                return null;
            }
            process = p.ProcessName;
            name = process;
            try
            {
                var description = p.MainModule?.FileVersionInfo.FileDescription;
                if (!string.IsNullOrWhiteSpace(description)) name = description;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { }
        var result = (RecordingMarkdown.CleanAppName(name), process);
        _apps[pid] = result;
        return result;
    }

    private static CacheRequest BuildCacheRequest()
    {
        // One cross-process call fetches the whole tree; walking it afterwards is local.
        var cache = new CacheRequest { AutomationElementMode = AutomationElementMode.Full, TreeScope = TreeScope.Subtree };
        cache.TreeFilter = Automation.ControlViewCondition;
        cache.Add(AutomationElement.NameProperty);
        cache.Add(AutomationElement.ControlTypeProperty);
        cache.Add(AutomationElement.LocalizedControlTypeProperty);
        cache.Add(AutomationElement.BoundingRectangleProperty);
        cache.Add(AutomationElement.FrameworkIdProperty);
        cache.Add(AutomationElement.IsOffscreenProperty);
        cache.Add(AutomationElement.IsPasswordProperty);
        cache.Add(AutomationElement.IsTextPatternAvailableProperty);
        cache.Add(AutomationElement.IsGridItemPatternAvailableProperty);
        cache.Add(ValuePattern.ValueProperty);
        cache.Add(GridItemPattern.RowProperty);
        cache.Add(GridItemPattern.ColumnProperty);
        return cache;
    }

    internal static List<string> CaptureLines(IntPtr hwnd, bool contentOnly, bool isBrowser)
    {
        var root = AutomationElement.FromHandle(hwnd).GetUpdatedCache(BuildCacheRequest());
        var visited = 0;
        var tree = BuildNode(root, 0, ref visited);
        return tree == null ? [] : RecordingMarkdown.NormalizeLines(RecordingLayout.Render(tree, contentOnly, isBrowser));
    }

    private static readonly ControlType[] SkippedTypes =
        // Window chrome and scrollbars only add noise like "Minimieren" or "Zurueck um kleine Menge".
        [ControlType.TitleBar, ControlType.ScrollBar, ControlType.MenuBar, ControlType.Thumb];

    private static UiNode? BuildNode(AutomationElement el, int depth, ref int visited)
    {
        AutomationElement.AutomationElementInformation info;
        ControlType type;
        try
        {
            info = el.Cached;
            if (info.IsPassword) return null;
            type = info.ControlType;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException)
        {
            return null;
        }
        if (Array.IndexOf(SkippedTypes, type) >= 0) return null;

        // Chromium and Firefox split their TextPattern output at every inline element; their tree reads better.
        var isWeb = info.FrameworkId is "Chrome" or "Gecko";
        string? patternText = null;
        if (!isWeb && (type == ControlType.Document || type == ControlType.Edit) && !info.IsOffscreen
            && el.GetCachedPropertyValue(AutomationElement.IsTextPatternAvailableProperty) is true)
            patternText = ReadVisibleText(el);

        var hasGridItem = el.GetCachedPropertyValue(AutomationElement.IsGridItemPatternAvailableProperty) is true;
        var node = new UiNode
        {
            Type = type,
            Name = info.Name ?? "",
            Value = type == ControlType.Edit || type == ControlType.ComboBox
                ? el.GetCachedPropertyValue(ValuePattern.ValueProperty, true) as string
                : null,
            PatternText = string.IsNullOrWhiteSpace(patternText) ? null : patternText,
            Bounds = info.BoundingRectangle,
            IsOffscreen = info.IsOffscreen,
            Landmark = RecordingLayout.ClassifyLandmark(type, info.LocalizedControlType),
            GridRow = hasGridItem && el.GetCachedPropertyValue(GridItemPattern.RowProperty, true) is int row ? row : -1,
            GridColumn = hasGridItem && el.GetCachedPropertyValue(GridItemPattern.ColumnProperty, true) is int col ? col : -1,
        };
        if (node.PatternText != null || depth >= MaxDepth) return node;

        AutomationElementCollection children;
        try { children = el.CachedChildren; }
        catch (InvalidOperationException) { return node; }
        foreach (AutomationElement child in children)
        {
            if (++visited > MaxNodes) break;
            if (BuildNode(child, depth + 1, ref visited) is { } c) node.Children.Add(c);
        }
        return node;
    }

    private static string? ReadVisibleText(AutomationElement el)
    {
        try
        {
            if (!el.TryGetCurrentPattern(TextPattern.Pattern, out var obj) || obj is not TextPattern tp) return null;
            var sb = new StringBuilder();
            foreach (var range in tp.GetVisibleRanges())
            {
                var remaining = MaxTextPatternChars - sb.Length;
                if (remaining <= 0) break;
                sb.Append(range.GetText(remaining)).Append('\n');
            }
            return sb.ToString();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ElementNotAvailableException or COMException or ArgumentException)
        {
            return null;
        }
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0) return "";
        var sb = new StringBuilder(length + 1);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsHungAppWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);
}

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
    private const int MaxNodes = 1500;
    private const int MaxDepth = 40;
    private const int MaxTextPatternChars = 6000;
    private const int MaxSeenLinesPerWindow = 5000;
    private static readonly TimeSpan CaptureBudget = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan StopJoinTimeout = TimeSpan.FromSeconds(3);

    private readonly string _directory;
    private readonly Func<bool> _isPaused;
    private readonly object _writeLock = new();
    private readonly Dictionary<int, string> _appNames = [];

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

    public DesktopRecorderService(string directory, Func<bool> isPaused)
    {
        _directory = directory;
        _isPaused = isPaused;
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

                var app = GetAppName((int)pid);
                if (app is null) continue;
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

                    var lines = CaptureLines(hwnd, token);
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
                if ((now - lastCapture).TotalMilliseconds < RecheckIntervalMs) continue;
                lastCapture = now;

                var current = CaptureLines(hwnd, token);
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

    /// <summary>Returns the display name of the process, or null for system processes that should not be recorded.</summary>
    private string? GetAppName(int pid)
    {
        if (_appNames.TryGetValue(pid, out var cached)) return cached.Length == 0 ? null : cached;
        var name = "Unbekannt";
        try
        {
            using var p = Process.GetProcessById(pid);
            // dwm owns the ghost window Windows shows for hung applications.
            if (string.Equals(p.ProcessName, "dwm", StringComparison.OrdinalIgnoreCase))
            {
                _appNames[pid] = "";
                return null;
            }
            name = p.ProcessName;
            try
            {
                var description = p.MainModule?.FileVersionInfo.FileDescription;
                if (!string.IsNullOrWhiteSpace(description)) name = description;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { }
        name = RecordingMarkdown.CleanAppName(name);
        _appNames[pid] = name;
        return name;
    }

    private static CacheRequest BuildCacheRequest()
    {
        var cache = new CacheRequest { AutomationElementMode = AutomationElementMode.Full, TreeScope = TreeScope.Element };
        cache.TreeFilter = Automation.ControlViewCondition;
        cache.Add(AutomationElement.NameProperty);
        cache.Add(AutomationElement.ControlTypeProperty);
        cache.Add(AutomationElement.IsOffscreenProperty);
        cache.Add(AutomationElement.IsPasswordProperty);
        cache.Add(AutomationElement.IsTextPatternAvailableProperty);
        cache.Add(AutomationElement.IsValuePatternAvailableProperty);
        return cache;
    }

    internal static List<string> CaptureLines(IntPtr hwnd, CancellationToken token)
    {
        var raw = new List<string>();
        var cache = BuildCacheRequest();
        var sw = Stopwatch.StartNew();
        var visited = 0;

        using (cache.Activate())
        {
            var root = AutomationElement.FromHandle(hwnd);
            var stack = new Stack<(AutomationElement Element, int Depth)>();
            stack.Push((root, 0));

            while (stack.Count > 0 && visited < MaxNodes && sw.Elapsed < CaptureBudget && !token.IsCancellationRequested)
            {
                var (el, depth) = stack.Pop();
                visited++;
                bool descend;
                try
                {
                    descend = ReadElement(el, raw);
                }
                catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException)
                {
                    continue;
                }
                if (!descend || depth >= MaxDepth) continue;

                AutomationElementCollection children;
                try
                {
                    // One cross-process call per parent instead of one per sibling.
                    children = el.FindAll(TreeScope.Children, Automation.ControlViewCondition);
                }
                catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException)
                {
                    continue;
                }
                // Reverse so the stack yields children in document order.
                for (var i = children.Count - 1; i >= 0; i--) stack.Push((children[i], depth + 1));
            }
        }

        return RecordingMarkdown.NormalizeLines(raw);
    }

    /// <summary>Collects text for one element. Returns false when its children should not be visited.</summary>
    private static bool ReadElement(AutomationElement el, List<string> raw)
    {
        var info = el.Cached;
        if (info.IsPassword) return false;
        var type = info.ControlType;
        // Window chrome and scrollbars only add noise like "Minimieren" or "Zurueck um kleine Menge".
        if (type == ControlType.TitleBar || type == ControlType.ScrollBar || type == ControlType.MenuBar
            || type == ControlType.Thumb)
            return false;

        var name = info.Name;
        var offscreen = info.IsOffscreen;

        if ((type == ControlType.Document || type == ControlType.Edit)
            && !offscreen
            && (bool)el.GetCachedPropertyValue(AutomationElement.IsTextPatternAvailableProperty))
        {
            var text = ReadVisibleText(el);
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (type == ControlType.Edit && !string.IsNullOrWhiteSpace(name)) raw.Add($"Eingabe \"{name}\":");
                raw.Add(text);
                return false;
            }
        }

        if (offscreen) return true;

        if (type == ControlType.Edit || type == ControlType.ComboBox)
        {
            var value = (bool)el.GetCachedPropertyValue(AutomationElement.IsValuePatternAvailableProperty) ? ReadValue(el) : null;
            if (!string.IsNullOrWhiteSpace(value))
                raw.Add(string.IsNullOrWhiteSpace(name) || string.Equals(name.Trim(), value.Trim(), StringComparison.Ordinal)
                    ? value
                    : $"{name}: {value}");
            else if (!string.IsNullOrWhiteSpace(name))
                raw.Add(name);
            return type == ControlType.ComboBox;
        }

        if (string.IsNullOrWhiteSpace(name)) return true;
        if (type == ControlType.Button && RecordingMarkdown.IsWindowChromeName(name)) return false;

        if (type == ControlType.Image)
            raw.Add($"[Bild] {name}");
        else if (type == ControlType.Text || type == ControlType.Hyperlink || type == ControlType.Button
            || type == ControlType.ListItem || type == ControlType.TreeItem || type == ControlType.DataItem
            || type == ControlType.HeaderItem || type == ControlType.TabItem || type == ControlType.MenuItem
            || type == ControlType.CheckBox || type == ControlType.RadioButton || type == ControlType.Header
            || type == ControlType.StatusBar || type == ControlType.ToolTip)
            raw.Add(name);
        return true;
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

    private static string? ReadValue(AutomationElement el)
    {
        try
        {
            return el.TryGetCurrentPattern(ValuePattern.Pattern, out var obj) && obj is ValuePattern vp ? vp.Current.Value : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ElementNotAvailableException or COMException)
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

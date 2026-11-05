using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;

namespace ClipwellWin.Services;

public enum UiLandmark
{
    None,
    Main,
    Navigation,
    Banner,
    Search,
    ContentInfo,
    Complementary,
}

/// <summary>
/// One UI Automation element as read from the cache. The layout works on this copy so it needs no
/// cross-process calls and can be tested without a live window.
/// </summary>
public sealed class UiNode
{
    public ControlType Type { get; init; } = ControlType.Custom;
    public string Name { get; init; } = "";
    public string? Value { get; init; }
    /// <summary>Visible text from the TextPattern; set only for non-web documents and edits.</summary>
    public string? PatternText { get; init; }
    public Rect Bounds { get; init; } = Rect.Empty;
    public bool IsOffscreen { get; init; }
    public UiLandmark Landmark { get; init; }
    public int GridRow { get; init; } = -1;
    public int GridColumn { get; init; } = -1;
    public List<UiNode> Children { get; } = [];
}

/// <summary>Turns a UI Automation tree into reading-order lines: joins fragments by screen position and renders tables.</summary>
public static partial class RecordingLayout
{
    private const int MaxTableColumns = 20;
    private const int MaxTableRows = 300;

    private static readonly HashSet<string> BrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "opera", "brave", "vivaldi", "firefox", "chromium", "arc", "thorium",
        "librewolf", "waterfox", "floorp", "zen",
    };

    [GeneratedRegex(@"^(?:[a-z][a-z0-9+.\-]*://\S+|about:\S+|[^\s/]+\.[a-z]{2,}(?::\d+)?(?:[/?#]\S*)?)$", RegexOptions.IgnoreCase)]
    private static partial Regex AddressRegex();

    public static bool IsBrowserProcess(string? processName)
        => processName != null && BrowserProcesses.Contains(processName);

    /// <summary>Maps the localized control type Chromium and Firefox report for ARIA landmarks.</summary>
    public static UiLandmark ClassifyLandmark(ControlType type, string? localizedType)
    {
        if (type != ControlType.Group && type != ControlType.Pane && type != ControlType.Custom) return UiLandmark.None;
        return (localizedType ?? "").Trim().ToLowerInvariant() switch
        {
            "main" or "primär" or "hauptinhalt" or "hauptbereich" => UiLandmark.Main,
            "navigation" => UiLandmark.Navigation,
            // German Chromium labels the page banner "Überschrift"; headings themselves are ControlType.Text.
            "banner" or "überschrift" or "kopfzeile" => UiLandmark.Banner,
            "search" or "suche" or "suchtaste" => UiLandmark.Search,
            "content information" or "contentinfo" or "inhaltsinformationen" or "fußzeile" or "footer" => UiLandmark.ContentInfo,
            "complementary" or "ergänzend" or "ergänzung" or "ergänzende informationen" => UiLandmark.Complementary,
            _ => UiLandmark.None,
        };
    }

    /// <summary>
    /// Renders the tree as lines. With <paramref name="contentOnly"/> in a browser, only the URL and the page
    /// content are kept: the main landmark if the page has one, otherwise the document without navigation,
    /// banner, search and footer areas, with complementary areas moved to the end.
    /// </summary>
    public static List<string> Render(UiNode root, bool contentOnly, bool isBrowser)
    {
        var lines = new List<string>();
        var scope = root;
        var skipLandmarks = false;

        if (contentOnly && isBrowser && FindMainDocument(root) is { } document)
        {
            var url = FindAddress(root, document);
            if (url != null) lines.Add("URL: " + url);
            var main = Find(document, n => n.Landmark == UiLandmark.Main);
            scope = main ?? document;
            skipLandmarks = main == null;
        }

        var collector = new Collector(skipLandmarks, root.Bounds);
        var items = collector.Collect(scope, scope.Bounds);
        items.AddRange(collector.Deferred);
        lines.AddRange(Flow(items));
        return lines;
    }

    private static UiNode? FindMainDocument(UiNode root)
    {
        UiNode? best = null;
        var bestArea = -1.0;
        var stack = new Stack<UiNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n.Type == ControlType.Document)
            {
                // Background tabs and collapsed side panels stay in the tree; the active page is the largest visible one.
                var area = n.IsOffscreen || n.Bounds.IsEmpty ? 0 : n.Bounds.Width * n.Bounds.Height;
                if (area > bestArea) { best = n; bestArea = area; }
                continue;
            }
            foreach (var c in n.Children) stack.Push(c);
        }
        return best;
    }

    private static string? FindAddress(UiNode root, UiNode document)
    {
        var stack = new Stack<UiNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (ReferenceEquals(n, document)) continue;
            if ((n.Type == ControlType.Edit || n.Type == ControlType.ComboBox) && n.Value is { } v
                && AddressRegex().IsMatch(v.Trim()))
                return v.Trim();
            for (var i = n.Children.Count - 1; i >= 0; i--) stack.Push(n.Children[i]);
        }
        return null;
    }

    private static UiNode? Find(UiNode root, Func<UiNode, bool> predicate)
    {
        var stack = new Stack<UiNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (predicate(n)) return n;
            for (var i = n.Children.Count - 1; i >= 0; i--) stack.Push(n.Children[i]);
        }
        return null;
    }

    private sealed record Item(string Text, Rect Bounds, Rect Container, IReadOnlyList<string>? Block = null)
    {
        public bool IsBlock => Block != null;
    }

    private sealed class Collector(bool skipLandmarks, Rect clip)
    {
        public List<Item> Deferred { get; } = [];

        public List<Item> Collect(UiNode n, Rect container)
        {
            if (skipLandmarks)
            {
                switch (n.Landmark)
                {
                    case UiLandmark.Navigation or UiLandmark.Banner or UiLandmark.Search or UiLandmark.ContentInfo:
                        return [];
                    case UiLandmark.Complementary:
                        Deferred.AddRange(CollectElement(n, container));
                        return [];
                }
            }
            return CollectElement(n, container);
        }

        private List<Item> CollectElement(UiNode n, Rect container)
        {
            var name = n.Name.Trim();
            if (n.Type == ControlType.Button && RecordingMarkdown.IsWindowChromeName(name)) return [];
            var visible = !n.IsOffscreen && (clip.IsEmpty || n.Bounds.IsEmpty || clip.IntersectsWith(n.Bounds));

            if (visible && !string.IsNullOrWhiteSpace(n.PatternText))
            {
                var block = new List<string>();
                if (n.Type == ControlType.Edit && name.Length > 0) block.Add($"Eingabe \"{name}\":");
                block.Add(n.PatternText);
                return [new Item("", n.Bounds, container, block)];
            }

            var childContainer = IsInline(n.Type) || n.Bounds.IsEmpty ? container : n.Bounds;

            if ((n.Type == ControlType.Table || n.Type == ControlType.DataGrid) && TryTable(n) is { } table)
                return table.Count == 0 ? [] : [new Item("", n.Bounds, container, table)];

            string? own = null;
            if (visible && (n.Type == ControlType.Edit || n.Type == ControlType.ComboBox) && !string.IsNullOrWhiteSpace(n.Value))
            {
                var value = n.Value.Trim();
                own = name.Length == 0 || string.Equals(name, value, StringComparison.Ordinal) ? value : $"{name}: {value}";
                // The value already is the field content; children of an edit would only repeat it.
                if (n.Type == ControlType.Edit) return [MakeItem(own, n, container)];
            }
            else if (visible && name.Length > 0)
            {
                if (n.Type == ControlType.Image) own = "[Bild] " + name;
                else if (IsContentType(n.Type)) own = name;
            }

            var children = new List<Item>();
            foreach (var c in n.Children) children.AddRange(Collect(c, childContainer));

            if (own == null) return children;
            var ownItem = MakeItem(own, n, container);
            if (children.Count == 0) return [ownItem];

            var childTexts = children.Select(c => Squash(c.IsBlock ? string.Concat(c.Block!) : c.Text)).Where(t => t.Length > 0).ToList();
            var childText = string.Concat(childTexts);
            var ownText = Squash(name);
            if (childText.Length == 0) return [ownItem, .. children];
            // Chromium names links, list items and cells after their content; keep one copy, not both.
            // A name much longer than the visible content is a screen reader summary, so the visible text wins.
            if (childTexts.All(t => ownText.Contains(t, StringComparison.Ordinal)))
                return !children.Any(c => c.IsBlock) && ownText.Length <= childText.Length * 1.5 + 10 ? [ownItem] : children;
            if (childText.Contains(ownText, StringComparison.Ordinal)) return children;
            return [ownItem, .. children];
        }

        private static Item MakeItem(string text, UiNode n, Rect container)
            => text.Contains('\n') ? new Item("", n.Bounds, container, [text]) : new Item(text, n.Bounds, container);

        private List<string>? TryTable(UiNode table)
        {
            var cells = new List<UiNode>();
            GatherCells(table, cells);
            if (cells.Count < 2) return null;
            var columns = Math.Min(cells.Max(c => c.GridColumn) + 1, MaxTableColumns);
            if (columns < 2) return null;

            var rows = new SortedDictionary<int, string[]>();
            foreach (var cell in cells)
            {
                if (cell.GridColumn >= columns) continue;
                var text = string.Join(" ", Flow(Collect(cell, cell.Bounds)))
                    .Replace('\n', ' ').Replace("|", "\\|", StringComparison.Ordinal).Trim();
                if (!rows.TryGetValue(cell.GridRow, out var row))
                {
                    row = Enumerable.Repeat("", columns).ToArray();
                    rows[cell.GridRow] = row;
                }
                row[cell.GridColumn] = row[cell.GridColumn].Length == 0 ? text : row[cell.GridColumn] + " " + text;
            }

            var lines = new List<string>();
            foreach (var row in rows.Values.Where(r => r.Any(c => c.Length > 0)).Take(MaxTableRows))
            {
                lines.Add("| " + string.Join(" | ", row) + " |");
                if (lines.Count == 1) lines.Add("|" + string.Concat(Enumerable.Repeat(" --- |", columns)));
            }
            return lines;
        }

        private static void GatherCells(UiNode n, List<UiNode> cells)
        {
            foreach (var c in n.Children)
            {
                if (c.GridRow >= 0 && c.GridColumn >= 0) { cells.Add(c); continue; }
                if (c.Type == ControlType.Table || c.Type == ControlType.DataGrid) continue;
                GatherCells(c, cells);
            }
        }
    }

    private static bool IsInline(ControlType type)
        => type == ControlType.Text || type == ControlType.Hyperlink || type == ControlType.Image || type == ControlType.Button;

    private static bool IsContentType(ControlType type)
        => type == ControlType.Text || type == ControlType.Hyperlink || type == ControlType.Button
           || type == ControlType.ListItem || type == ControlType.TreeItem || type == ControlType.DataItem
           || type == ControlType.HeaderItem || type == ControlType.TabItem || type == ControlType.MenuItem
           || type == ControlType.CheckBox || type == ControlType.RadioButton || type == ControlType.Header
           || type == ControlType.StatusBar || type == ControlType.ToolTip || type == ControlType.ComboBox
           || type == ControlType.Edit;

    private static string Squash(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    /// <summary>
    /// Joins fragments in tree order: fragments on the same visual row become one line, and a line that ran
    /// to the right edge of its container continues on the next row as long as the next fragment starts at
    /// the paragraph's left edge.
    /// </summary>
    private static List<string> Flow(IEnumerable<Item> items)
    {
        var lines = new List<string>();
        StringBuilder? para = null;
        var last = Rect.Empty;
        var container = Rect.Empty;
        var paraLeft = 0.0;

        void Flush()
        {
            if (para != null && para.Length > 0) lines.Add(para.ToString());
            para = null;
        }

        foreach (var item in items)
        {
            if (item.IsBlock)
            {
                Flush();
                lines.AddRange(item.Block!);
                continue;
            }
            var text = item.Text.Trim();
            if (text.Length == 0) continue;
            var b = item.Bounds;

            if (para != null && !b.IsEmpty && !last.IsEmpty)
            {
                if (SameRow(last, b))
                {
                    Join(para, text, wrapped: false);
                    last = b;
                    continue;
                }
                if (Wraps(last, b, paraLeft, container))
                {
                    Join(para, text, wrapped: true);
                    last = b;
                    continue;
                }
            }

            Flush();
            para = new StringBuilder(StripBullet(text));
            last = b;
            paraLeft = b.IsEmpty ? 0 : b.Left;
            container = item.Container;
        }
        Flush();
        return lines;
    }

    private static bool SameRow(Rect a, Rect b)
    {
        var h = Math.Min(a.Height, b.Height);
        if (h <= 0) return false;
        var overlap = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        if (overlap < 0.5 * h) return false;
        return b.Left >= a.Left - 2 && b.Left - a.Right <= Math.Max(2 * h, 24);
    }

    private static bool Wraps(Rect a, Rect b, double paraLeft, Rect container)
    {
        var h = b.Height;
        // A multi-line fragment hides where its last line ends, so a wrap cannot be told from a new paragraph.
        if (h <= 0 || container.IsEmpty || a.Height > 1.6 * h) return false;
        var gap = b.Top - a.Bottom;
        if (gap < -0.3 * h || gap > 0.5 * h) return false;
        if (Math.Abs(b.Left - paraLeft) > Math.Max(h, 12)) return false;
        return a.Right >= container.Right - 4 * h;
    }

    private static void Join(StringBuilder para, string text, bool wrapped)
    {
        var prev = para.Length > 0 ? para[^1] : ' ';
        var hyphenWrap = wrapped && prev == '-' && para.Length > 1 && char.IsLetter(para[^2]);
        var noSpace = hyphenWrap || prev == '(' || char.IsWhiteSpace(prev) || ",.;:!?)]".Contains(text[0]);
        if (!noSpace) para.Append(' ');
        para.Append(text);
    }

    private static string StripBullet(string text)
    {
        var i = 0;
        while (i < text.Length && (text[i] is '•' or '◦' or '▪' or '‣' or '·' || char.IsWhiteSpace(text[i]))) i++;
        return i > 0 && i < text.Length ? text[i..] : text;
    }
}

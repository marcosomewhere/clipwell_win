using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Merkautomat.Services;

/// <summary>
/// Converts clipboard HTML fragments into normalized Markdown. Tolerant of the partial, unbalanced
/// markup browsers put on the clipboard; drops scripts, styles, navigation and form residue.
/// </summary>
public static class HtmlMarkdownConverter
{
    private static readonly Regex TokenRx = new(
        @"<!--[\s\S]*?-->|<!\[CDATA\[[\s\S]*?\]\]>|<!(?:[^>]*)>|<(/?)([a-zA-Z][a-zA-Z0-9-]*)((?:[^>""']|""[^""]*""|'[^']*')*?)(/?)>|[^<]+|<",
        RegexOptions.Compiled);
    private static readonly Regex AttrRx = new(
        @"([a-zA-Z_:][-a-zA-Z0-9_:.]*)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'>]+))", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRx = new(@"[ \t\r\n\f ]+", RegexOptions.Compiled);
    private static readonly Regex BlankLinesRx = new(@"\n{3,}", RegexOptions.Compiled);
    private static readonly Regex TrailingSpaceRx = new(@"[ \t]+\n", RegexOptions.Compiled);

    private static readonly HashSet<string> SkippedElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "noscript", "template", "svg", "head", "nav", "aside", "footer",
        "form", "button", "select", "option", "iframe", "object", "canvas", "dialog", "menu",
    };

    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr",
    };

    private static readonly HashSet<string> BlockElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "div", "section", "article", "main", "header", "figure", "figcaption", "details", "summary",
        "dl", "dt", "dd", "address", "center",
    };

    public static string Convert(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var state = new State();

        foreach (Match m in TokenRx.Matches(html))
        {
            var value = m.Value;
            if (value.StartsWith("<!", StringComparison.Ordinal)) continue;

            if (!m.Groups[2].Success)
            {
                if (state.SkipDepth == 0)
                    state.Text(WebUtility.HtmlDecode(value == "<" ? "<" : value));
                continue;
            }

            var name = m.Groups[2].Value.ToLowerInvariant();
            var closing = m.Groups[1].Value == "/";
            var selfClosing = m.Groups[4].Value == "/" || VoidElements.Contains(name);

            if (SkippedElements.Contains(name))
            {
                if (closing) state.SkipDepth = Math.Max(0, state.SkipDepth - 1);
                else if (!selfClosing) state.SkipDepth++;
                continue;
            }
            if (state.SkipDepth > 0) continue;

            if (closing) state.Close(name);
            else state.Open(name, ParseAttributes(m.Groups[3].Value));
        }

        return state.Finish();
    }

    private static Dictionary<string, string> ParseAttributes(string raw)
    {
        var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match a in AttrRx.Matches(raw))
        {
            var v = a.Groups[2].Success ? a.Groups[2].Value
                  : a.Groups[3].Success ? a.Groups[3].Value
                  : a.Groups[4].Value;
            attrs[a.Groups[1].Value] = WebUtility.HtmlDecode(v);
        }
        return attrs;
    }

    private sealed class ListContext
    {
        public required bool Ordered { get; init; }
        public int Counter { get; set; }
    }

    private sealed class TableContext
    {
        public List<List<string>> Rows { get; } = [];
        public bool FirstRowIsHeader { get; set; }
    }

    private sealed class State
    {
        // Buffers stack: table cells and blockquotes render into their own buffer first.
        private readonly Stack<StringBuilder> _buffers = new([new StringBuilder()]);
        private readonly Stack<ListContext> _lists = new();
        private readonly Stack<TableContext> _tables = new();
        private readonly Stack<string?> _links = new();
        private int _preDepth;
        private int _quoteDepth;

        public int SkipDepth { get; set; }

        private StringBuilder Out => _buffers.Peek();
        private bool InCell => _tables.Count > 0 && _buffers.Count > 1 + _quoteDepth;

        public void Text(string text)
        {
            if (_preDepth > 0)
            {
                Out.Append(text);
                return;
            }
            var collapsed = WhitespaceRx.Replace(text, " ");
            if (collapsed.Length == 0) return;
            if (collapsed == " " && (Out.Length == 0 || char.IsWhiteSpace(Out[^1]))) return;
            if (collapsed[0] == ' ' && (Out.Length == 0 || char.IsWhiteSpace(Out[^1])))
                collapsed = collapsed.TrimStart();
            Out.Append(EscapeInline(collapsed));
        }

        public void Open(string name, Dictionary<string, string> attrs)
        {
            switch (name)
            {
                case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                    BlankLine();
                    Out.Append(new string('#', name[1] - '0')).Append(' ');
                    break;
                case "p":
                    BlankLine();
                    break;
                case "br":
                    if (InCell) Out.Append(' ');
                    else Out.Append('\n');
                    break;
                case "hr":
                    BlankLine();
                    Out.Append("---");
                    BlankLine();
                    break;
                case "ul":
                case "ol":
                    if (_lists.Count == 0) BlankLine(); else NewLine();
                    _lists.Push(new ListContext { Ordered = name == "ol" });
                    break;
                case "li":
                {
                    NewLine();
                    var list = _lists.Count > 0 ? _lists.Peek() : null;
                    var indent = new string(' ', Math.Max(0, _lists.Count - 1) * 2);
                    if (list is { Ordered: true })
                    {
                        list.Counter++;
                        Out.Append(indent).Append(list.Counter).Append(". ");
                    }
                    else
                    {
                        Out.Append(indent).Append("- ");
                    }
                    break;
                }
                case "strong": case "b":
                    Out.Append("**");
                    break;
                case "em": case "i":
                    Out.Append('*');
                    break;
                case "s": case "del": case "strike":
                    Out.Append("~~");
                    break;
                case "code":
                    if (_preDepth == 0) Out.Append('`');
                    break;
                case "pre":
                    BlankLine();
                    Out.Append("```\n");
                    _preDepth++;
                    break;
                case "blockquote":
                    BlankLine();
                    _quoteDepth++;
                    _buffers.Push(new StringBuilder());
                    break;
                case "a":
                {
                    attrs.TryGetValue("href", out var href);
                    var usable = !string.IsNullOrWhiteSpace(href)
                        && !href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
                        && !href.StartsWith('#');
                    _links.Push(usable ? href!.Trim() : null);
                    if (usable) Out.Append('[');
                    break;
                }
                case "img":
                {
                    attrs.TryGetValue("alt", out var alt);
                    attrs.TryGetValue("src", out var src);
                    if (!string.IsNullOrWhiteSpace(alt) && !string.IsNullOrWhiteSpace(src)
                        && !src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                        Out.Append("![").Append(EscapeInline(alt.Trim())).Append("](").Append(src.Trim()).Append(')');
                    break;
                }
                case "table":
                    BlankLine();
                    _tables.Push(new TableContext());
                    break;
                case "tr":
                    if (_tables.Count > 0) _tables.Peek().Rows.Add([]);
                    break;
                case "th":
                case "td":
                    if (_tables.Count > 0)
                    {
                        var table = _tables.Peek();
                        if (table.Rows.Count == 0) table.Rows.Add([]);
                        if (name == "th" && table.Rows.Count == 1) table.FirstRowIsHeader = true;
                        _buffers.Push(new StringBuilder());
                    }
                    break;
                default:
                    if (BlockElements.Contains(name)) NewLine();
                    break;
            }
        }

        public void Close(string name)
        {
            switch (name)
            {
                case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                case "p":
                    BlankLine();
                    break;
                case "ul":
                case "ol":
                    if (_lists.Count > 0) _lists.Pop();
                    if (_lists.Count == 0) BlankLine();
                    break;
                case "strong": case "b":
                    Out.Append("**");
                    break;
                case "em": case "i":
                    Out.Append('*');
                    break;
                case "s": case "del": case "strike":
                    Out.Append("~~");
                    break;
                case "code":
                    if (_preDepth == 0) Out.Append('`');
                    break;
                case "pre":
                    if (_preDepth > 0)
                    {
                        _preDepth--;
                        if (Out.Length > 0 && Out[^1] != '\n') Out.Append('\n');
                        Out.Append("```");
                        BlankLine();
                    }
                    break;
                case "blockquote":
                    if (_quoteDepth > 0 && _buffers.Count > 1)
                    {
                        _quoteDepth--;
                        var inner = Normalize(_buffers.Pop().ToString());
                        var quoted = string.Join("\n", inner.Split('\n').Select(l => l.Length == 0 ? ">" : "> " + l));
                        Out.Append(quoted);
                        BlankLine();
                    }
                    break;
                case "a":
                    if (_links.Count > 0 && _links.Pop() is { } href)
                        Out.Append("](").Append(href).Append(')');
                    break;
                case "th":
                case "td":
                    if (_tables.Count > 0 && _buffers.Count > 1)
                    {
                        var cell = Normalize(_buffers.Pop().ToString()).Replace("\n", " ").Replace("|", "\\|");
                        var table = _tables.Peek();
                        if (table.Rows.Count == 0) table.Rows.Add([]);
                        table.Rows[^1].Add(cell);
                    }
                    break;
                case "table":
                    if (_tables.Count > 0)
                    {
                        Out.Append(RenderTable(_tables.Pop()));
                        BlankLine();
                    }
                    break;
                default:
                    if (BlockElements.Contains(name)) NewLine();
                    break;
            }
        }

        public string Finish()
        {
            while (_buffers.Count > 1)
            {
                var inner = _buffers.Pop().ToString();
                Out.Append(inner);
            }
            while (_links.Count > 0)
                if (_links.Pop() is { } href) Out.Append("](").Append(href).Append(')');
            while (_tables.Count > 0)
                Out.Append(RenderTable(_tables.Pop()));
            return Normalize(Out.ToString());
        }

        private void NewLine()
        {
            if (Out.Length > 0 && Out[^1] != '\n') Out.Append('\n');
        }

        private void BlankLine()
        {
            if (Out.Length == 0) return;
            NewLine();
            if (Out.Length > 1 && Out[^2] != '\n') Out.Append('\n');
        }

        private static string RenderTable(TableContext table)
        {
            var rows = table.Rows.Where(r => r.Count > 0).ToList();
            if (rows.Count == 0) return "";
            var columns = rows.Max(r => r.Count);
            var sb = new StringBuilder();

            void AppendRow(List<string> row)
            {
                sb.Append('|');
                for (var i = 0; i < columns; i++)
                    sb.Append(' ').Append(i < row.Count ? row[i] : "").Append(" |");
                sb.Append('\n');
            }

            // Markdown tables require a header row; without <th> the first row takes that role.
            AppendRow(rows[0]);
            sb.Append('|');
            for (var i = 0; i < columns; i++) sb.Append(" --- |");
            sb.Append('\n');
            foreach (var row in rows.Skip(1)) AppendRow(row);
            return sb.ToString();
        }
    }

    // Only characters that would start emphasis or code spans are escaped; underscores inside
    // host names and identifiers are left alone to keep the Markdown readable.
    private static string EscapeInline(string text)
        => text.Replace("*", "\\*").Replace("`", "\\`");

    private static string Normalize(string markdown)
    {
        var text = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        text = TrailingSpaceRx.Replace(text, "\n");
        text = BlankLinesRx.Replace(text, "\n\n");
        // Remove empty emphasis/link markers left by elements that contained only skipped content.
        text = text.Replace("****", "").Replace("[]()", "");
        return text.Trim();
    }
}

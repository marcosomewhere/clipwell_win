using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;

namespace ClipwellWin.Views;

/// <summary>Native read-only Markdown subset; source text is never interpreted as HTML or XAML.</summary>
public static class MarkdownPreview
{
    private static readonly Regex Heading = new(@"^ {0,3}(#{1,6})\s+(.+?)\s*#*\s*$", RegexOptions.Compiled);
    private static readonly Regex ListLine = new(@"^\s*(?:([-+*])|\d+[.)])\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex Tokens = new(@"(`[^`\n]+`|\*\*[^*\n]+\*\*|__[^_\n]+__|\*[^*\n]+\*|~~[^~\n]+~~|!?\[[^\]\n]+\]\([^\s)]+\))", RegexOptions.Compiled);

    public static FlowDocument Build(string markdown, Brush foreground, Brush background, Brush accent, Brush border)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"), FontSize = 13, Foreground = foreground,
            Background = background, PagePadding = new Thickness(18, 12, 18, 18),
        };
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            var trim = line.TrimStart();
            if (trim.StartsWith("```") || trim.StartsWith("~~~"))
            {
                var fence = new string(trim[0], trim.TakeWhile(c => c == trim[0]).Count());
                var code = new System.Text.StringBuilder();
                while (++i < lines.Length)
                {
                    var close = lines[i].Trim();
                    if (close.Length >= fence.Length && close.All(c => c == fence[0])) break;
                    if (code.Length > 0) code.Append('\n');
                    code.Append(lines[i]);
                }
                document.Blocks.Add(new Paragraph(new Run(code.ToString()))
                {
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12,
                    BorderBrush = border, BorderThickness = new Thickness(1), Padding = new Thickness(10), Margin = new Thickness(0, 6, 0, 12),
                });
                continue;
            }
            var heading = Heading.Match(line);
            if (heading.Success)
            {
                var level = heading.Groups[1].Length;
                var paragraph = ParagraphOf(heading.Groups[2].Value, accent);
                paragraph.FontSize = level switch { 1 => 24, 2 => 19, 3 => 16, _ => 14 };
                paragraph.FontWeight = FontWeights.SemiBold;
                paragraph.Margin = new Thickness(0, level == 1 ? 4 : 16, 0, 8);
                paragraph.Tag = line;
                document.Blocks.Add(paragraph);
                continue;
            }
            if (i + 1 < lines.Length && line.Contains('|') && IsTableSeparator(lines[i + 1]))
            {
                var headers = Cells(line);
                var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 8, 0, 12) };
                foreach (var _ in headers) table.Columns.Add(new TableColumn());
                var group = new TableRowGroup();
                table.RowGroups.Add(group);
                void AddRow(string[] cells, bool header)
                {
                    var row = new TableRow();
                    for (var col = 0; col < headers.Length; col++)
                        row.Cells.Add(new TableCell(ParagraphOf(col < cells.Length ? cells[col] : "", accent))
                        {
                            Padding = new Thickness(8, 5, 8, 5), BorderBrush = border, BorderThickness = new Thickness(0.5),
                            FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal,
                        });
                    group.Rows.Add(row);
                }
                AddRow(headers, true);
                i += 2;
                while (i < lines.Length && lines[i].Contains('|') && !string.IsNullOrWhiteSpace(lines[i])) AddRow(Cells(lines[i++]), false);
                i--;
                document.Blocks.Add(table);
                continue;
            }
            if (Regex.IsMatch(trim, @"^(?:-{3,}|\*{3,}|_{3,})\s*$"))
            {
                document.Blocks.Add(new Paragraph { BorderBrush = border, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 10, 0, 10) });
                continue;
            }
            var item = ListLine.Match(line);
            if (item.Success)
            {
                var bullets = item.Groups[1].Success;
                var list = new System.Windows.Documents.List { MarkerStyle = bullets ? TextMarkerStyle.Disc : TextMarkerStyle.Decimal, Padding = new Thickness(22, 0, 0, 0), Margin = new Thickness(0, 3, 0, 8) };
                do
                {
                    list.ListItems.Add(new ListItem(ParagraphOf(item.Groups[2].Value, accent)) { Margin = new Thickness(0, 2, 0, 2) });
                    i++;
                    item = i < lines.Length ? ListLine.Match(lines[i]) : Match.Empty;
                } while (item.Success && item.Groups[1].Success == bullets);
                i--;
                document.Blocks.Add(list);
                continue;
            }
            var quote = trim.StartsWith('>');
            var p = ParagraphOf(quote ? trim[1..].TrimStart() : line, accent);
            if (quote) { p.BorderBrush = accent; p.BorderThickness = new Thickness(3, 0, 0, 0); p.Padding = new Thickness(10, 2, 0, 2); }
            document.Blocks.Add(p);
        }
        return document;
    }

    private static string[] Cells(string line) => line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
    private static bool IsTableSeparator(string line) => Cells(line).All(c => Regex.IsMatch(c, @"^:?-{3,}:?$"));

    private static Paragraph ParagraphOf(string text, Brush accent)
    {
        var p = new Paragraph { Margin = new Thickness(0, 2, 0, 7) };
        var at = 0;
        foreach (Match match in Tokens.Matches(text))
        {
            if (match.Index > at) p.Inlines.Add(new Run(text[at..match.Index]));
            var token = match.Value;
            if (token.StartsWith('`')) p.Inlines.Add(new Run(token[1..^1]) { FontFamily = new FontFamily("Cascadia Mono, Consolas") });
            else if (token.StartsWith("**") || token.StartsWith("__")) p.Inlines.Add(new Bold(new Run(token[2..^2])));
            else if (token.StartsWith("~~")) p.Inlines.Add(new Run(token[2..^2]) { TextDecorations = TextDecorations.Strikethrough });
            else if (token.StartsWith('*')) p.Inlines.Add(new Italic(new Run(token[1..^1])));
            else
            {
                var start = token.IndexOf('[') + 1;
                var close = token.IndexOf("](", StringComparison.Ordinal);
                var label = token[start..close];
                var target = token[(close + 2)..^1];
                if (!token.StartsWith('!') && Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
                {
                    var link = new Hyperlink(new Run(label)) { NavigateUri = uri, Foreground = accent, ToolTip = target };
                    link.RequestNavigate += (_, e) =>
                    {
                        Services.WebLink.TryOpen(e.Uri);
                        e.Handled = true;
                    };
                    p.Inlines.Add(link);
                }
                else p.Inlines.Add(new Run(token.StartsWith('!') ? $"Bild: {label}" : $"{label} ({target})"));
            }
            at = match.Index + match.Length;
        }
        if (at < text.Length) p.Inlines.Add(new Run(text[at..]));
        return p;
    }
}

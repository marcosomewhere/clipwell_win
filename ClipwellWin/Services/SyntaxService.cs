using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using ClipwellWin.Models;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;

namespace ClipwellWin.Services;

public static class SyntaxService
{
    private static readonly (Regex pattern, string label)[] LangPatterns =
    [
        (new(@"^#!/usr/bin/env python|^import \w|^from \w+ import|^\s*def \w+\(|^\s*class \w+:", RegexOptions.Multiline), "Python"),
        (new(@"SELECT\s+.+FROM\s+|INSERT\s+INTO\s+|UPDATE\s+\w+\s+SET\s+|DELETE\s+FROM\s+", RegexOptions.IgnoreCase), "SQL"),
        (new(@"^\s*(const|let|var)\s+\w+|=>\s*\{|console\.log\(|require\(|import\s+\{", RegexOptions.Multiline), "JavaScript"),
        (new(@"^\s*(interface|type)\s+\w+|:\s*(string|number|boolean|void)\b", RegexOptions.Multiline), "TypeScript"),
        (new(@"using\s+\w[\w.]+;|namespace\s+\w|public\s+(class|void|static|async)|\.NET", RegexOptions.Multiline), "C#"),
        (new(@"^#include\s+[<""]|int\s+main\s*\(|std::|cout\s*<<|cin\s*>>", RegexOptions.Multiline), "C++"),
        (new(@"^\s*func\s+\w+\(|:=\s*|fmt\.Println|package\s+\w+", RegexOptions.Multiline), "Go"),
        (new(@"^\s*fn\s+\w+|let\s+mut\s+|println!\(|use\s+std::", RegexOptions.Multiline), "Rust"),
        (new(@"<\?php|\$\w+\s*=|echo\s+|->|array\(", RegexOptions.Multiline), "PHP"),
        (new(@"def\s+\w+|puts\s+|\.each\s*\{|require\s+'", RegexOptions.Multiline), "Ruby"),
        (new(@"^\s*<[a-zA-Z][^>]*>|<!DOCTYPE|</\w+>", RegexOptions.Multiline), "HTML"),
        (new(@"^\s*[.#][\w-]+\s*\{|:\s*(px|em|rem|vh|vw|%)|@media\s+", RegexOptions.Multiline), "CSS"),
        (new(@"^\s*\{[\s\S]*""[\w]+"":\s*[""{\[\d]", RegexOptions.Multiline), "JSON"),
        (new(@"^#!/bin/(bash|sh)|^\s*\w+=[""]|echo\s+|grep\s+|awk\s+|sed\s+", RegexOptions.Multiline), "Bash"),
        (new(@"\$\w+\s*=|\$env:|Get-\w+|Set-\w+|Invoke-\w+|Write-Host", RegexOptions.Multiline), "PowerShell"),
        (new(@"^\s*<\?xml|xmlns[:=]|<[\w:]+\s+[\w:]+=""", RegexOptions.Multiline), "XML"),
        (new(@"^\s*\w+\s*:\s*\n\s+\w+:", RegexOptions.Multiline), "YAML"),
    ];

    public static string? DetectLanguage(string text, CodeDetectionMode mode = CodeDetectionMode.Normal)
        => Analyze(text, mode).language;

    public static (string? language, string reason) Analyze(string text, CodeDetectionMode mode = CodeDetectionMode.Normal)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (null, "Leerer Inhalt.");

        if (text.Length < 20 && mode != CodeDetectionMode.Aggressive)
            return (null, "Zu kurz fuer belastbare Code-Erkennung.");

        text = text.Trim();

        var looksLikeCode = LooksLikeCode(text);
        var strongSignal = HasStrongCodeSignal(text);
        if (!looksLikeCode)
            return (null, "Keine typischen Code-Signale gefunden.");

        foreach (var (pattern, label) in LangPatterns)
        {
            if (!pattern.IsMatch(text)) continue;
            if (mode == CodeDetectionMode.Conservative && !strongSignal && !HasLanguageAnchor(text, label))
                return (null, $"Konservativer Modus: {label}-Signal war nicht eindeutig genug.");

            return (label, $"Als {label} erkannt: Muster und Code-Zeichen passen.");
        }

        return mode == CodeDetectionMode.Aggressive
            ? ("Code", "Aggressiver Modus: Struktur wirkt wie Code, Sprache ist unklar.")
            : (null, "Code-aehnliche Struktur, aber keine passende Sprache erkannt.");
    }

    public static bool IsCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return DetectLanguage(text) != null;
    }

    private static bool LooksLikeCode(string text)
    {
        if (LooksLikePlainSentence(text) && !HasStrongCodeSignal(text))
            return false;

        if (Regex.IsMatch(text, @"^\s*[\{\[][\s\S]*[\}\]]\s*$") && text.Contains(':'))
            return true;

        if (Regex.IsMatch(text, @"^\s*<(!DOCTYPE|/?[a-zA-Z][\w:-]*)(\s|>|/>)", RegexOptions.Multiline))
            return true;

        if (Regex.IsMatch(text, @"^\s*(#!|using\s+[\w.]+;|namespace\s+\w|#include\s+[<""]|package\s+\w+|import\s+[\w.{*]|from\s+\w+\s+import|def\s+\w+\(|class\s+\w+|func\s+\w+\(|fn\s+\w+|\$env:|Get-\w+|Set-\w+)", RegexOptions.Multiline))
            return true;

        if (Regex.IsMatch(text, @"\b(SELECT\s+.+\s+FROM|INSERT\s+INTO|UPDATE\s+\w+\s+SET|DELETE\s+FROM)\b", RegexOptions.IgnoreCase))
            return true;

        var codeChars = Regex.Matches(text, @"[{}();=\[\]<>$]").Count;
        if (codeChars >= 2) return true;

        var lines = text.Split('\n');
        if (lines.Length >= 2)
        {
            var codeLikeLines = lines.Count(l =>
                Regex.IsMatch(l, @"^\s{2,}\S") ||
                Regex.IsMatch(l, @"^\s*(//|#|/\*|\*|</?\w|[}\]])") ||
                Regex.IsMatch(l, @"[{}();=]"));

            return codeLikeLines >= 2;
        }

        return false;
    }

    private static bool LooksLikePlainSentence(string text)
    {
        if (text.Contains('\n')) return false;
        if (Regex.IsMatch(text, @"[{}();=\[\]<>$]")) return false;

        var words = Regex.Matches(text, @"\p{L}+").Count;
        if (words < 5) return false;

        var lowerWords = Regex.Matches(text, @"\b\p{Ll}{2,}\b").Count;
        return lowerWords >= Math.Max(3, words / 2);
    }

    private static bool HasStrongCodeSignal(string text)
        => Regex.IsMatch(text, @"(=>|==|!=|<=|>=|::|;\s*$|^\s*(SELECT|INSERT|UPDATE|DELETE)\b)", RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static bool HasLanguageAnchor(string text, string label) => label switch
    {
        "Python" => Regex.IsMatch(text, @"^\s*(def|class|import|from)\s+", RegexOptions.Multiline),
        "SQL" => Regex.IsMatch(text, @"\b(SELECT|INSERT|UPDATE|DELETE)\b", RegexOptions.IgnoreCase),
        "JavaScript" or "TypeScript" => Regex.IsMatch(text, @"(=>|function\s+\w+|const\s+\w+|let\s+\w+|import\s+)"),
        "C#" => Regex.IsMatch(text, @"(namespace\s+\w|public\s+(class|static|void)|using\s+[\w.]+;)"),
        "JSON" => Regex.IsMatch(text, @"^\s*[\{\[][\s\S]*[\}\]]\s*$"),
        _ => HasStrongCodeSignal(text),
    };

    private static readonly Dictionary<string, (string[] keywords, Color color)> LangKeywords = new()
    {
        ["Python"] = (["def","class","import","from","return","if","else","elif","for","while","in","not","and","or","True","False","None","lambda","yield","with","as","try","except","finally","raise","pass","break","continue"], Color.FromRgb(86, 156, 214)),
        ["JavaScript"] = (["const","let","var","function","return","if","else","for","while","class","import","export","default","async","await","new","this","typeof","null","undefined","true","false","=>"], Color.FromRgb(86, 156, 214)),
        ["TypeScript"] = (["const","let","var","function","return","if","else","for","while","class","interface","type","import","export","async","await","string","number","boolean","void","null","undefined","true","false"], Color.FromRgb(86, 156, 214)),
        ["C#"] = (["using","namespace","class","public","private","protected","internal","static","void","int","string","bool","var","new","return","if","else","for","foreach","while","async","await","null","true","false","interface","enum","record","sealed","override","virtual","abstract","readonly","const"], Color.FromRgb(86, 156, 214)),
        ["SQL"] = (["SELECT","FROM","WHERE","INSERT","INTO","VALUES","UPDATE","SET","DELETE","JOIN","INNER","LEFT","RIGHT","ON","AND","OR","NOT","IN","LIKE","ORDER","BY","GROUP","HAVING","LIMIT","OFFSET","CREATE","TABLE","DROP","ALTER","INDEX","PRIMARY","KEY","FOREIGN","REFERENCES","DISTINCT","AS","NULL","IS","COUNT","SUM","MAX","MIN","AVG"], Color.FromRgb(86, 156, 214)),
        ["Go"] = (["func","package","import","return","if","else","for","range","var","const","type","struct","interface","map","chan","go","defer","select","case","default","break","continue","nil","true","false"], Color.FromRgb(86, 156, 214)),
        ["Rust"] = (["fn","let","mut","pub","use","mod","struct","enum","impl","trait","return","if","else","for","while","match","Some","None","Ok","Err","true","false","self","Self","Box","Vec","String","Option","Result"], Color.FromRgb(86, 156, 214)),
    };

    private static readonly Color StringColor = Color.FromRgb(206, 145, 120);
    private static readonly Color CommentColor = Color.FromRgb(106, 153, 85);
    private static readonly Color NumberColor = Color.FromRgb(181, 206, 168);

    private static readonly Color LineNumColor = Color.FromRgb(90, 95, 110);

    public const int LazyThreshold = 300;

    public static FlowDocument Highlight(string code, string? language, int maxLines = int.MaxValue)
    {
        var (doc, _, _) = HighlightCore(code, language, maxLines);
        return doc;
    }

    public static (FlowDocument doc, bool truncated, TableRowGroup rowGroup) HighlightCore(
        string code, string? language, int initialLines = int.MaxValue)
    {
        var doc = new FlowDocument
        {
            FontFamily  = new System.Windows.Media.FontFamily("Cascadia Code, Consolas, Courier New"),
            FontSize    = 12,
            PagePadding = new System.Windows.Thickness(0),
            PageWidth   = 10000,
        };

        HashSet<string>? keywordSet = null;
        Color keywordColor = default;
        if (language != null && LangKeywords.TryGetValue(language, out var langData))
        {
            keywordSet = new HashSet<string>(langData.keywords, StringComparer.Ordinal);
            keywordColor = langData.color;
        }

        var table = new Table { CellSpacing = 0, BorderThickness = new System.Windows.Thickness(0) };
        table.Columns.Add(new TableColumn { Width = new System.Windows.GridLength(48) });
        table.Columns.Add(new TableColumn { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
        var rowGroup = new TableRowGroup();
        table.RowGroups.Add(rowGroup);

        var lines = code.Split('\n');
        int renderCount = Math.Min(lines.Length, initialLines);
        AppendLines(rowGroup, lines, 0, renderCount, keywordSet, keywordColor);
        bool truncated = renderCount < lines.Length;

        doc.Blocks.Add(table);
        return (doc, truncated, rowGroup);
    }

    public static void AppendLines(TableRowGroup rowGroup, string[] lines,
        int from, int to, HashSet<string>? keywordSet, Color keywordColor)
    {
        for (int i = from; i < to && i < lines.Length; i++)
        {
            var lineText = lines[i].TrimEnd('\r');
            var row = new TableRow();

            var numPara = new Paragraph(new Run($"{i + 1}"))
            {
                TextAlignment = TextAlignment.Right,
                LineHeight = 18,
                Margin = new System.Windows.Thickness(0),
                Padding = new System.Windows.Thickness(0, 0, 8, 0),
            };
            numPara.Foreground = new SolidColorBrush(LineNumColor);
            row.Cells.Add(new TableCell(numPara) { Padding = new System.Windows.Thickness(2, 0, 0, 0) });

            var codePara = new Paragraph { LineHeight = 18, Margin = new System.Windows.Thickness(0) };
            if (keywordSet != null)
            {
                foreach (var (token, kind) in TokenizeSimple(lineText))
                {
                    var run = new Run(token);
                    Color? fc = kind switch
                    {
                        TokenKind.Keyword when keywordSet.Contains(token) => keywordColor,
                        TokenKind.String  => StringColor,
                        TokenKind.Comment => CommentColor,
                        TokenKind.Number  => NumberColor,
                        _ => (Color?)null,
                    };
                    if (fc.HasValue) run.Foreground = new SolidColorBrush(fc.Value);
                    codePara.Inlines.Add(run);
                }
            }
            else
            {
                codePara.Inlines.Add(new Run(lineText));
            }
            row.Cells.Add(new TableCell(codePara) { Padding = new System.Windows.Thickness(0) });

            rowGroup.Rows.Add(row);
        }
    }

    public static (HashSet<string>? keywordSet, Color keywordColor) GetKeywordSet(string? language)
    {
        if (language != null && LangKeywords.TryGetValue(language, out var langData))
            return (new HashSet<string>(langData.keywords, StringComparer.Ordinal), langData.color);
        return (null, default);
    }

    private enum TokenKind { Other, Keyword, String, Comment, Number }

    private static List<(string text, TokenKind kind)> TokenizeSimple(string src)
    {
        var result = new List<(string, TokenKind)>();
        int i = 0;
        while (i < src.Length)
        {
            // Line comment // or #
            if (i < src.Length - 1 && src[i] == '/' && src[i + 1] == '/')
            {
                int end = src.IndexOf('\n', i);
                end = end < 0 ? src.Length : end;
                result.Add((src[i..end], TokenKind.Comment));
                i = end; continue;
            }
            if (src[i] == '#' && (i == 0 || src[i - 1] == '\n' || src[i - 1] == '\r'))
            {
                int end = src.IndexOf('\n', i);
                end = end < 0 ? src.Length : end;
                result.Add((src[i..end], TokenKind.Comment));
                i = end; continue;
            }
            // Block comment /* */
            if (i < src.Length - 1 && src[i] == '/' && src[i + 1] == '*')
            {
                int end = src.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? src.Length : end + 2;
                result.Add((src[i..end], TokenKind.Comment));
                i = end; continue;
            }
            // Strings " or '
            if (src[i] == '"' || src[i] == '\'')
            {
                char q = src[i]; int j = i + 1;
                while (j < src.Length && (src[j] != q || (j > 0 && src[j - 1] == '\\'))) j++;
                if (j < src.Length) j++;
                result.Add((src[i..j], TokenKind.String));
                i = j; continue;
            }
            // Number
            if (char.IsDigit(src[i]) || (src[i] == '.' && i + 1 < src.Length && char.IsDigit(src[i + 1])))
            {
                int j = i;
                while (j < src.Length && (char.IsDigit(src[j]) || src[j] == '.' || src[j] == '_' || src[j] == 'x' || (j > i && "abcdefABCDEF".Contains(src[j])))) j++;
                result.Add((src[i..j], TokenKind.Number));
                i = j; continue;
            }
            // Word (keyword or identifier)
            if (char.IsLetter(src[i]) || src[i] == '_')
            {
                int j = i;
                while (j < src.Length && (char.IsLetterOrDigit(src[j]) || src[j] == '_')) j++;
                result.Add((src[i..j], TokenKind.Keyword));
                i = j; continue;
            }
            // Everything else: one char at a time (or runs of punctuation/whitespace)
            result.Add((src[i].ToString(), TokenKind.Other));
            i++;
        }
        return result;
    }
}

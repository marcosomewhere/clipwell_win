using System.Text.RegularExpressions;
using ClipwellWin.Models;

namespace ClipwellWin.Services;

public static class SyntaxService
{
    private static readonly Regex CodeAnchorRx = new(
        @"^\s*(#!|using\s+[\w.]+;|namespace\s+\w|#include\s+[<""]|package\s+\w+|import\s+[\w.{*]|from\s+\w+\s+import|def\s+\w+\(|class\s+\w+|func\s+\w+\(|fn\s+\w+|\$env:|Get-\w+|Set-\w+)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex StrongCodeSignalRx = new(
        @"(=>|==|!=|<=|>=|::|;\s*$|^\s*(SELECT|INSERT|UPDATE|DELETE)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

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

        if (LooksLikePlainTextBlock(text))
            return false;

        if (Regex.IsMatch(text, @"^\s*[\{\[][\s\S]*[\}\]]\s*$") && text.Contains(':'))
            return true;

        if (Regex.IsMatch(text, @"^\s*<(!DOCTYPE|/?[a-zA-Z][\w:-]*)(\s|>|/>)", RegexOptions.Multiline))
            return true;

        if (CodeAnchorRx.IsMatch(text))
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

    private static bool LooksLikePlainTextBlock(string text)
    {
        if (!text.Contains('\n') || HasStrongCodeSignal(text))
            return false;

        if (CodeAnchorRx.IsMatch(text))
            return false;

        var lines = text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
        if (lines.Count < 2) return false;

        var codePunctuationLines = lines.Count(l =>
            Regex.IsMatch(l, @"[{}=\[\]<>$]|;\s*$|^\s*(//|#|/\*)"));
        if (codePunctuationLines > 0) return false;

        var proseLines = lines.Count(IsProseLine);
        return proseLines >= Math.Max(2, (int)Math.Ceiling(lines.Count * 0.6));
    }

    private static bool IsProseLine(string line)
    {
        var words = Regex.Matches(line, @"\p{L}+").Count;
        if (words < 3) return false;

        var lowerWords = Regex.Matches(line, @"\b\p{Ll}{2,}\b").Count;
        if (lowerWords >= Math.Max(2, words / 3)) return true;

        return Regex.IsMatch(line, @"^\p{Lu}[\p{L}\s-]{2,30}:\s+\p{L}");
    }

    private static bool HasStrongCodeSignal(string text)
        => StrongCodeSignalRx.IsMatch(text);

    private static bool HasLanguageAnchor(string text, string label) => label switch
    {
        "Python" => Regex.IsMatch(text, @"^\s*(def|class|import|from)\s+", RegexOptions.Multiline),
        "SQL" => Regex.IsMatch(text, @"\b(SELECT|INSERT|UPDATE|DELETE)\b", RegexOptions.IgnoreCase),
        "JavaScript" or "TypeScript" => Regex.IsMatch(text, @"(=>|function\s+\w+|const\s+\w+|let\s+\w+|import\s+)"),
        "C#" => Regex.IsMatch(text, @"(namespace\s+\w|public\s+(class|static|void)|using\s+[\w.]+;)"),
        "JSON" => Regex.IsMatch(text, @"^\s*[\{\[][\s\S]*[\}\]]\s*$"),
        _ => HasStrongCodeSignal(text),
    };

}

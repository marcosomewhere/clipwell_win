using System.Text.RegularExpressions;
using ClipwellWin.Models;

namespace ClipwellWin.Services;

public static class SyntaxService
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly Regex CodeAnchorRx = new(
        @"^\s*(#!|using\s+[\w.]+;|namespace\s+\w|#include\s+[<""]|package\s+\w+|import\s+[\w.{*]|from\s+\w+\s+import|def\s+\w+\(|class\s+\w+|func\s+\w+\(|fn\s+\w+|\$env:|Get-\w+|Set-\w+)",
        RegexOptions.Multiline | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex StrongCodeSignalRx = new(
        @"(=>|==|!=|<=|>=|::|;\s*$|^\s*(SELECT|INSERT|UPDATE|DELETE)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly (Regex pattern, string label)[] LangPatterns =
    [
        (Rx(@"^#!/usr/bin/env python|^import \w|^from \w+ import|^\s*def \w+\(|^\s*class \w+:", RegexOptions.Multiline), "Python"),
        (Rx(@"SELECT\s+.+FROM\s+|INSERT\s+INTO\s+|UPDATE\s+\w+\s+SET\s+|DELETE\s+FROM\s+", RegexOptions.IgnoreCase), "SQL"),
        (Rx(@"^\s*(const|let|var)\s+\w+|=>\s*\{|console\.log\(|require\(|import\s+\{", RegexOptions.Multiline), "JavaScript"),
        (Rx(@"^\s*(interface|type)\s+\w+|:\s*(string|number|boolean|void)\b", RegexOptions.Multiline), "TypeScript"),
        (Rx(@"using\s+\w[\w.]+;|namespace\s+\w|public\s+(class|void|static|async)|\.NET", RegexOptions.Multiline), "C#"),
        (Rx(@"^#include\s+[<""]|int\s+main\s*\(|std::|cout\s*<<|cin\s*>>", RegexOptions.Multiline), "C++"),
        (Rx(@"^\s*func\s+\w+\(|:=\s*|fmt\.Println|package\s+\w+", RegexOptions.Multiline), "Go"),
        (Rx(@"^\s*fn\s+\w+|let\s+mut\s+|println!\(|use\s+std::", RegexOptions.Multiline), "Rust"),
        (Rx(@"<\?php|\$\w+\s*=|echo\s+|->|array\(", RegexOptions.Multiline), "PHP"),
        (Rx(@"def\s+\w+|puts\s+|\.each\s*\{|require\s+'", RegexOptions.Multiline), "Ruby"),
        (Rx(@"^\s*<[a-zA-Z][^>]*>|<!DOCTYPE|</\w+>", RegexOptions.Multiline), "HTML"),
        (Rx(@"^\s*[.#][\w-]+\s*\{|:\s*(px|em|rem|vh|vw|%)|@media\s+", RegexOptions.Multiline), "CSS"),
        (Rx(@"^\s*\{[\s\S]*""[\w]+"":\s*[""{\[\d]", RegexOptions.Multiline), "JSON"),
        (Rx(@"^#!/bin/(bash|sh)|^\s*\w+=[""]|echo\s+|grep\s+|awk\s+|sed\s+", RegexOptions.Multiline), "Bash"),
        (Rx(@"\$\w+\s*=|\$env:|Get-\w+|Set-\w+|Invoke-\w+|Write-Host", RegexOptions.Multiline), "PowerShell"),
        (Rx(@"^\s*<\?xml|xmlns[:=]|<[\w:]+\s+[\w:]+=""", RegexOptions.Multiline), "XML"),
        (Rx(@"^\s*\w+\s*:\s*\n\s+\w+:", RegexOptions.Multiline), "YAML"),
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

    private static bool LooksLikeCode(string text)
    {
        if (LooksLikePlainSentence(text) && !HasStrongCodeSignal(text))
            return false;

        if (LooksLikePlainTextBlock(text))
            return false;

        if (IsMatch(text, @"^\s*[\{\[][\s\S]*[\}\]]\s*$") && text.Contains(':'))
            return true;

        if (IsMatch(text, @"^\s*<(!DOCTYPE|/?[a-zA-Z][\w:-]*)(\s|>|/>)", RegexOptions.Multiline))
            return true;

        if (CodeAnchorRx.IsMatch(text))
            return true;

        if (IsMatch(text, @"\b(SELECT\s+.+\s+FROM|INSERT\s+INTO|UPDATE\s+\w+\s+SET|DELETE\s+FROM)\b", RegexOptions.IgnoreCase))
            return true;

        var codeChars = CountMatches(text, @"[{}();=\[\]<>$]");
        if (codeChars >= 2) return true;

        var lines = text.Split('\n');
        if (lines.Length >= 2)
        {
            var codeLikeLines = lines.Count(l =>
                IsMatch(l, @"^\s{2,}\S") ||
                IsMatch(l, @"^\s*(//|#|/\*|\*|</?\w|[}\]])") ||
                IsMatch(l, @"[{}();=]"));

            return codeLikeLines >= 2;
        }

        return false;
    }

    private static bool LooksLikePlainSentence(string text)
    {
        if (text.Contains('\n')) return false;
        if (IsMatch(text, @"[{}();=\[\]<>$]")) return false;

        var words = CountMatches(text, @"\p{L}+");
        if (words < 5) return false;

        var lowerWords = CountMatches(text, @"\b\p{Ll}{2,}\b");
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
            IsMatch(l, @"[{}=\[\]<>$]|;\s*$|^\s*(//|#|/\*)"));
        if (codePunctuationLines > 0) return false;

        var proseLines = lines.Count(IsProseLine);
        return proseLines >= Math.Max(2, (int)Math.Ceiling(lines.Count * 0.6));
    }

    private static bool IsProseLine(string line)
    {
        var words = CountMatches(line, @"\p{L}+");
        if (words < 3) return false;

        var lowerWords = CountMatches(line, @"\b\p{Ll}{2,}\b");
        if (lowerWords >= Math.Max(2, words / 3)) return true;

        return IsMatch(line, @"^\p{Lu}[\p{L}\s-]{2,30}:\s+\p{L}");
    }

    private static bool HasStrongCodeSignal(string text)
        => StrongCodeSignalRx.IsMatch(text);

    private static bool HasLanguageAnchor(string text, string label) => label switch
    {
        "Python" => IsMatch(text, @"^\s*(def|class|import|from)\s+", RegexOptions.Multiline),
        "SQL" => IsMatch(text, @"\b(SELECT|INSERT|UPDATE|DELETE)\b", RegexOptions.IgnoreCase),
        "JavaScript" or "TypeScript" => IsMatch(text, @"(=>|function\s+\w+|const\s+\w+|let\s+\w+|import\s+)"),
        "C#" => IsMatch(text, @"(namespace\s+\w|public\s+(class|static|void)|using\s+[\w.]+;)"),
        "JSON" => IsMatch(text, @"^\s*[\{\[][\s\S]*[\}\]]\s*$"),
        _ => HasStrongCodeSignal(text),
    };

    private static Regex Rx(string pattern, RegexOptions options)
        => new(pattern, options | RegexOptions.Compiled, RegexTimeout);

    private static bool IsMatch(string text, string pattern, RegexOptions options = RegexOptions.None)
    {
        try { return Regex.IsMatch(text, pattern, options, RegexTimeout); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    private static int CountMatches(string text, string pattern, RegexOptions options = RegexOptions.None)
    {
        try { return Regex.Matches(text, pattern, options, RegexTimeout).Count; }
        catch (RegexMatchTimeoutException) { return 0; }
    }
}

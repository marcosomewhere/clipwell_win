using System.IO;
using System.Text;

namespace Merkautomat.Services;

/// <summary>
/// Reads the text of files copied as file items (Explorer, Outlook drag targets, editors that put CF_HDROP on the
/// clipboard), so the history keeps their content instead of only the file name.
/// </summary>
public static class FileContentReader
{
    public const int DefaultMaxKb = 1024;
    public const int MinMaxKb = 16;
    public const int MaxMaxKb = 10240;

    // All of these are plain text formats; binary containers (docx, pdf, xlsx, zip) are deliberately absent.
    public const string DefaultExtensions =
        "txt, text, md, markdown, rst, adoc, log, csv, tsv, " +
        "eml, mht, mhtml, vcf, ics, " +
        "cfg, conf, config, ini, inf, env, properties, toml, yaml, yml, json, jsonc, json5, xml, xsd, xsl, xslt, " +
        "plist, reg, editorconfig, gitignore, gitattributes, " +
        "bat, cmd, ps1, psm1, psd1, sh, bash, zsh, fish, vbs, ahk, " +
        "js, mjs, cjs, jsx, ts, tsx, vue, svelte, html, htm, xhtml, css, scss, sass, less, svg, " +
        "cs, csx, csproj, vbproj, fsproj, props, targets, sln, slnx, resx, xaml, manifest, vb, fs, fsx, " +
        "c, h, cpp, hpp, cc, cxx, java, kt, kts, gradle, groovy, scala, go, rs, py, pyw, rb, php, pl, pm, lua, r, " +
        "swift, m, dart, sql, graphql, gql, proto, tex, bib, " +
        "dockerfile, makefile, cmake, tf, tfvars, hcl, nix, diff, patch, srt, vtt, nfo, url, desktop";

    /// <summary>Normalized extension list ("txt, .cfg;XML" -> txt, cfg, xml); duplicates and empty parts are dropped.</summary>
    public static IReadOnlyList<string> ParseExtensions(string? list)
    {
        if (string.IsNullOrWhiteSpace(list)) return [];
        return list.Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim().TrimStart('*').TrimStart('.').ToLowerInvariant())
            .Where(e => e.Length > 0)
            .Distinct()
            .ToList();
    }

    public static string FormatExtensions(IEnumerable<string> extensions) => string.Join(", ", extensions);

    /// <summary>
    /// Files without extension count by name (Dockerfile, Makefile, .gitignore, .env), so the list can name them
    /// like extensions.
    /// </summary>
    public static bool IsReadable(string path, IReadOnlyCollection<string> extensions)
    {
        if (extensions.Count == 0) return false;
        var name = Path.GetFileName(path).ToLowerInvariant();
        var ext = Path.GetExtension(name).TrimStart('.');
        if (ext.Length > 0 && extensions.Contains(ext)) return true;
        var bare = name.TrimStart('.');
        return bare.Length > 0 && !bare.Contains('.') && extensions.Contains(bare);
    }

    public sealed record FileText(string Path, string Text);

    /// <summary>
    /// Text of each readable file, at most <paramref name="maxBytes"/> per file and in total.
    /// Files that look binary (NUL bytes outside UTF-16) are skipped.
    /// </summary>
    public static List<FileText> Read(IEnumerable<string> paths, IReadOnlyCollection<string> extensions, long maxBytes)
    {
        var result = new List<FileText>();
        long total = 0;
        foreach (var path in paths)
        {
            try
            {
                if (!IsReadable(path, extensions) || !File.Exists(path)) continue;
                var info = new FileInfo(path);
                if (info.Length == 0 || info.Length > maxBytes || total + info.Length > maxBytes) continue;
                var bytes = File.ReadAllBytes(path);
                if (LooksBinary(bytes)) continue;
                var text = NoteStore.DecodeImport(bytes).Replace("\r\n", "\n").Replace('\r', '\n').Trim();
                if (text.Length == 0) continue;
                total += info.Length;
                result.Add(new FileText(path, text));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                           or NotSupportedException)
            {
            }
        }
        return result;
    }

    internal static bool LooksBinary(byte[] bytes)
    {
        if (bytes.Length >= 2 && (bytes[0] == 0xFF && bytes[1] == 0xFE || bytes[0] == 0xFE && bytes[1] == 0xFF))
            return false;
        var probe = Math.Min(bytes.Length, 8192);
        for (var i = 0; i < probe; i++)
            if (bytes[i] == 0) return true;
        return false;
    }

    /// <summary>One file: its text. Several files: each under a "--- Datei: name ---" separator.</summary>
    public static string Combine(IReadOnlyList<FileText> files)
    {
        if (files.Count == 1) return files[0].Text;
        var sb = new StringBuilder();
        foreach (var f in files)
        {
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append("--- Datei: ").Append(Path.GetFileName(f.Path)).Append(" ---\n").Append(f.Text);
        }
        return sb.ToString();
    }

    // The entry's source field holds the file paths, one per line; web sources are always http(s) URLs.
    public static string SourceOf(IEnumerable<FileText> files) => string.Join("\n", files.Select(f => f.Path));

    public static bool IsFileSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return false;
        var first = source.Split('\n')[0].Trim();
        return first.Length > 2 && Path.IsPathFullyQualified(first);
    }

    public static IReadOnlyList<string> SourcePaths(string? source)
        => IsFileSource(source)
            ? source!.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

    /// <summary>"Datei: notes.txt" or "3 Dateien: a.txt, b.cfg, c.xml".</summary>
    public static string? Headline(string? source)
    {
        var paths = SourcePaths(source);
        if (paths.Count == 0) return null;
        var names = string.Join(", ", paths.Select(Path.GetFileName));
        return paths.Count == 1 ? $"Datei: {names}" : $"{paths.Count} Dateien: {names}";
    }
}

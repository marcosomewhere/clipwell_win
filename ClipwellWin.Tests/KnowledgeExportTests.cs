using System.IO;
using System.IO.Compression;
using ClipwellWin.Models;
using ClipwellWin.Services;
using Xunit;

namespace ClipwellWin.Tests;

public class KnowledgeExportTests
{
    private static readonly DateTime Today = new(2026, 11, 5);

    [Theory]
    [InlineData(2026, 11, 5, "Heute")]
    [InlineData(2026, 11, 4, "Gestern")]
    [InlineData(2026, 11, 1, "Diese Woche")]
    [InlineData(2026, 10, 30, "Diese Woche")]
    [InlineData(2026, 10, 29, "Früher")]
    [InlineData(2025, 11, 3, "Früher")]
    public void DateGroup_UsesRelativeBuckets(int y, int m, int d, string expected)
        => Assert.Equal(expected, KnowledgeExport.DateGroup(new DateTime(y, m, d, 13, 0, 0), Today));

    [Fact]
    public void DateGroup_SameMonthOlderThanWeek_IsThisMonth()
        => Assert.Equal("Diesen Monat", KnowledgeExport.DateGroup(new DateTime(2026, 11, 20), new DateTime(2026, 11, 30)));

    [Fact]
    public void DemoteHeadings_ShiftsHeadingsButNotCodeOrHashtags()
    {
        var md = "# Titel\n## Abschnitt\n#tag\n```\n# kommentar\n```\n###### tief";
        var result = KnowledgeExport.DemoteHeadings(md);
        Assert.Equal("## Titel\n### Abschnitt\n#tag\n```\n# kommentar\n```\n###### tief", result);
    }

    [Fact]
    public void BuildCombined_ContainsTocOverviewAndAllArticles()
    {
        var articles = new List<KbArticle>
        {
            new(KbArticleKind.Note, "Einkauf", new DateTime(2026, 11, 5, 9, 0, 0), "n1.md", "# Notiz: Einkauf\n\nMilch"),
            new(KbArticleKind.Recording, "Edge", new DateTime(2026, 11, 4, 8, 30, 0), "r1.md", "# Desktop-Aufnahme\n\n## 08:30:00 - Edge: Jira"),
        };

        var md = KnowledgeExport.BuildCombined(articles, new DateTime(2026, 11, 5, 12, 0, 0), "# Übersicht\n\nText");

        Assert.StartsWith("# Ideenkombinat - Export\n", md);
        Assert.Contains("Artikel: 2", md);
        Assert.Contains("1. Sprachnotiz: Einkauf (05.11.2026 09:00)", md);
        Assert.Contains("2. Bildschirmaufnahme: Edge (04.11.2026 08:30)", md);
        Assert.Contains("## Übersicht", md);
        Assert.Contains("## Notiz: Einkauf", md);
        Assert.Contains("### 08:30:00 - Edge: Jira", md);
        Assert.True(md.IndexOf("## Übersicht", StringComparison.Ordinal) < md.IndexOf("## Notiz: Einkauf", StringComparison.Ordinal));
    }

    [Fact]
    public void WriteZip_WritesFoldersOverviewAndUniqueNames()
    {
        var dir = Path.Combine(Path.GetTempPath(), "clipwell-kbexport-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var zipPath = Path.Combine(dir, "export.zip");
            var articles = new List<KbArticle>
            {
                new(KbArticleKind.Note, "A", Today, "same.md", "Notiz A"),
                new(KbArticleKind.Note, "B", Today, "same.md", "Notiz B"),
                new(KbArticleKind.Recording, "R", Today, "rec.md", "Aufnahme\r\nZeile"),
            };

            articles.Add(new(KbArticleKind.Clipboard, "C", Today, "2026-11-05_00-00-00_7.md", "Clip"));
            KnowledgeExport.WriteZip(zipPath, articles, "Übersicht");

            using var zip = ZipFile.OpenRead(zipPath);
            var names = zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToList();
            Assert.Equal(["bildschirmaufnahmen/rec.md", "sprechwerk/same-2.md", "sprechwerk/same.md", "uebersicht.md",
                "zwischenablage/2026-11-05_00-00-00_7.md"], names);
            using var reader = new StreamReader(zip.GetEntry("bildschirmaufnahmen/rec.md")!.Open());
            Assert.Equal("Aufnahme\nZeile", reader.ReadToEnd());
            Assert.False(File.Exists(zipPath + ".tmp"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ClipboardMarkdown_RendersHeaderAndContentPerType()
    {
        var at = new DateTime(2026, 11, 5, 9, 30, 15);
        var code = new ClipboardEntry { Id = 3, Type = EntryType.Code, Language = "CSharp", Content = "var x = 1;", Timestamp = at, UseCount = 2 };
        var image = new ClipboardEntry { Id = 4, Type = EntryType.Image, OcrText = "Rechnung 4711\nZeile 2", Timestamp = at };
        var url = new ClipboardEntry { Id = 5, Type = EntryType.Url, Content = "https://example.org", UrlTitle = "Beispiel", Timestamp = at };

        var md = KnowledgeExport.ClipboardMarkdown(code);
        Assert.StartsWith("# Zwischenablage: var x = 1;\n\n- Kopiert: 2026-11-05 09:30:15\n- Typ: Code (CSharp)\n- Verwendet: 2x\n", md);
        Assert.Contains("```csharp\nvar x = 1;\n```", md);

        Assert.Equal("Bild: Rechnung 4711", KnowledgeExport.ClipboardTitle(image));
        Assert.Contains("Erkannter Text (OCR):\n\nRechnung 4711\nZeile 2", KnowledgeExport.ClipboardMarkdown(image));
        Assert.Equal("Beispiel", KnowledgeExport.ClipboardTitle(url));
        Assert.Contains("<https://example.org>", KnowledgeExport.ClipboardMarkdown(url));
        Assert.Equal("2026-11-05_09-30-15_3.md", KnowledgeExport.ClipboardFileName(code));
        Assert.Equal("Zwischenablage", KnowledgeExport.KindLabel(KbArticleKind.Clipboard));
    }
}

using System.Windows;
using Merkautomat.Models;
using Merkautomat.Services;
using Merkautomat.ViewModels;
using Merkautomat.Views;
using Xunit;

namespace Merkautomat.Tests;

public class MarkdownDetectionTests
{
    private const string ChatAnswer = """
        `.kdbx` ist das aktuelle KeePass-2.x-Datenbankformat. KDBX 4.1 unterstützt u. a. AES-256 oder ChaCha20.
        [KeePass](https://keepass.info/help/kb/kdbx.html)

        ## Ja: `.kdbx` direkt lesen und schreiben

        Deine Anwendung könnte beispielsweise:

        ```typescript
        const db = await kdbxweb.Kdbx.load(data, credentials);
        export function open(): void { console.log("x"); }
        ```

        - Passwort-Safe öffnen
        - Einträge anzeigen
        """;

    private const string PythonScript = """
        # Konfiguration laden
        import json
        def load(path):
            # - liest die Datei
            with open(path) as f:
                return json.load(f)
        print(load("[a](b)"))
        """;

    private const string TypeScriptModule = """
        import { readFile } from "fs/promises";
        export async function load(path: string): Promise<string> {
            const text = await readFile(path, "utf8");
            return text.trim();
        }
        """;

    private const string ChatAnswerMostlyBlocks = """
        Ja. Das ist technisch gut machbar, und ich würde **nicht versuchen, selbst ein neues Passwort-Datenbankformat zu erfinden**.

        Für deine Clipboard-App wäre die sauberste Lösung:

        ```text
        Clipboard-App
          └─ Passwort-Safe (.kdbx)
        ```

        ## Architektur

        ```text
        UI  ->  KdbxService  ->  Datei
        ```

        ```csharp
        public sealed class KdbxService
        {
            public void Open(string path) { }
        }
        ```
        """;

    private const string JsDocModule = """
        /**
         * Loads the configuration.
         * - reads `config.json`
         * - validates the schema
         */
        export function load(path) {
          const text = fs.readFileSync(path, "utf8");
          return JSON.parse(text);
        }
        """;

    [Fact]
    public void ChatAnswerMostlyCodeBlocks_IsMarkdown()
        => Assert.True(ContentKindService.LooksLikeMarkdownDocument(ChatAnswerMostlyBlocks));

    [Fact]
    public void TableWithProse_IsMarkdown()
        => Assert.True(ContentKindService.LooksLikeMarkdownDocument(
            "Vergleich der Formate:\n\n| Format | Verschlüsselung |\n|---|---|\n| KDBX 4 | AES-256 |\n\nFazit: **KDBX** reicht."));

    [Fact]
    public void JsDocModule_IsNotMarkdown()
        => Assert.False(ContentKindService.LooksLikeMarkdownDocument(JsDocModule));

    [Fact]
    public void FindMarkdownCandidates_SkipsManualChoicesNotesAndExistingMarkdown()
    {
        var entries = new List<ClipboardEntry>
        {
            new() { Id = 1, Type = EntryType.Code, Language = "TypeScript", Content = ChatAnswer, DetectionReason = "Als TypeScript erkannt." },
            new() { Id = 2, Type = EntryType.Code, Content = ChatAnswer, DetectionReason = "Manuell als Code behandelt." },
            new() { Id = 3, Type = EntryType.Code, Content = ChatAnswer, DetectionReason = "Code manuell bearbeitet." },
            new() { Id = 4, Type = EntryType.Text, ContentKind = "NOTE", Content = ChatAnswer },
            new() { Id = 5, Type = EntryType.Text, ContentKind = "MARKDOWN", Content = ChatAnswer },
            new() { Id = 6, Type = EntryType.Code, Content = TypeScriptModule },
            new() { Id = 7, Type = EntryType.Text, Content = ChatAnswerMostlyBlocks },
            new() { Id = 8, Type = EntryType.Url, Content = "https://example.org" },
        };

        Assert.Equal([1L, 7L], PopupViewModel.FindMarkdownCandidates(entries));
    }

    [Fact]
    public void ShowMarkdownOnly_FiltersToMdEntries()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"merkautomat-test-{Guid.NewGuid():N}.db");
        try
        {
            using (var db = new DatabaseService(dbPath))
            {
                var vm = new PopupViewModel(db, runDatabase: PopupViewModel.InlineDatabase);
                var at = new DateTime(2026, 11, 7, 9, 0, 0);
                vm.AddNow(new ClipboardEntry { Type = EntryType.Text, ContentKind = "MARKDOWN", Content = ChatAnswer, Timestamp = at });
                vm.AddNow(new ClipboardEntry { Type = EntryType.Text, Content = "nur Text", Timestamp = at.AddMinutes(1) });
                vm.AddNow(new ClipboardEntry { Type = EntryType.Code, Language = "TypeScript", Content = TypeScriptModule, Timestamp = at.AddMinutes(2) });

                vm.ShowMarkdownOnly = true;
                Assert.Equal([ChatAnswer], vm.FilteredEntries.Cast<EntryViewModel>().Select(e => e.Content));

                vm.ShowMarkdownOnly = false;
                vm.TypeFilter = EntryType.Text;
                Assert.Equal(2, vm.FilteredEntries.Cast<EntryViewModel>().Count());
            }
        }
        finally
        {
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                if (System.IO.File.Exists(p)) System.IO.File.Delete(p);
        }
    }

    [Fact]
    public void ChatAnswerWithCodeBlock_IsMarkdown()
        => Assert.True(ContentKindService.LooksLikeMarkdownDocument(ChatAnswer));

    [Fact]
    public void ProseWithHeadingsListsAndLinks_IsMarkdown()
        => Assert.True(ContentKindService.LooksLikeMarkdownDocument(
            "# Einkauf\n\n- Milch\n- Brot\n\nSiehe [Liste](https://example.org) für **Details**."));

    [Theory]
    [InlineData(PythonScript)]
    [InlineData(TypeScriptModule)]
    [InlineData("Ein ganz normaler Satz ohne Formatierung, der etwas länger ist.")]
    [InlineData("- nur\n- eine\n- Liste")]
    public void CodeAndPlainText_AreNotMarkdown(string text)
        => Assert.False(ContentKindService.LooksLikeMarkdownDocument(text));

    [Fact]
    public void BuildEntry_StoresMarkdownAsTextNotScript()
        => StaTestRunner.Run(() =>
        {
            var data = new DataObject();
            data.SetText(ChatAnswer);

            var entry = ClipboardProcessor.BuildEntry(data);

            Assert.NotNull(entry);
            Assert.Equal(EntryType.Text, entry!.Type);
            Assert.Equal("MARKDOWN", entry.ContentKind);
            Assert.Null(entry.Language);
            Assert.StartsWith("Als Markdown erkannt", entry.DetectionReason);
        });

    [Fact]
    public void BuildEntry_KeepsRealScriptsAsCode()
        => StaTestRunner.Run(() =>
        {
            var data = new DataObject();
            data.SetText(TypeScriptModule);
            Assert.Equal(EntryType.Code, ClipboardProcessor.BuildEntry(data)!.Type);
        });

    [Fact]
    public void IsMarkdownEntry_RecognizesOldScriptEntriesButRespectsManualCode()
    {
        var old = new EntryViewModel(new ClipboardEntry { Type = EntryType.Code, Language = "TypeScript", Content = ChatAnswer });
        Assert.True(DetailWindow.IsMarkdownEntry(old, ChatAnswer));

        var manual = new EntryViewModel(new ClipboardEntry
            { Type = EntryType.Code, Content = ChatAnswer, DetectionReason = "Manuell als Code behandelt." });
        Assert.False(DetailWindow.IsMarkdownEntry(manual, ChatAnswer));

        var note = new EntryViewModel(new ClipboardEntry { Type = EntryType.Text, ContentKind = "NOTE", Content = ChatAnswer });
        Assert.False(DetailWindow.IsMarkdownEntry(note, ChatAnswer));

        var stored = new EntryViewModel(new ClipboardEntry { Type = EntryType.Text, ContentKind = "MARKDOWN", Content = "kurz" });
        Assert.True(DetailWindow.IsMarkdownEntry(stored, "kurz"));
    }

    [Fact]
    public void ShouldHighlight_StopsAtSizeLimits()
    {
        Assert.True(DetailWindow.ShouldHighlight("var x = 1;\n"));
        Assert.False(DetailWindow.ShouldHighlight(new string('a', DetailWindow.HighlightMaxChars + 1)));
        Assert.True(DetailWindow.ShouldHighlight(string.Join('\n', Enumerable.Repeat("x", DetailWindow.HighlightMaxLines))));
        Assert.False(DetailWindow.ShouldHighlight(string.Join('\n', Enumerable.Repeat("x", DetailWindow.HighlightMaxLines + 1))));
    }
}

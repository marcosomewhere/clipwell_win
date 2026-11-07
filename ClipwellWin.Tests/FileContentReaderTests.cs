using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Windows;
using ClipwellWin.Models;
using ClipwellWin.Services;
using ClipwellWin.ViewModels;
using Xunit;

namespace ClipwellWin.Tests;

public sealed class FileContentReaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "clipwell-filecontent-" + Guid.NewGuid().ToString("N"));

    public FileContentReaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string WriteFile(string name, string text, Encoding? encoding = null)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text, encoding ?? new UTF8Encoding(false));
        return path;
    }

    private static FileContentOptions Options(string list = FileContentReader.DefaultExtensions, int maxKb = 1024)
        => FileContentOptions.From(new AppSettings { FileContentExtensions = list, FileContentMaxKb = maxKb })!;

    private static DataObject FileDrop(params string[] paths)
    {
        var data = new DataObject();
        var list = new StringCollection();
        list.AddRange(paths);
        data.SetFileDropList(list);
        return data;
    }

    [Fact]
    public void ParseExtensions_NormalizesSeparatorsDotsAndCase()
    {
        Assert.Equal(["txt", "cfg", "xml", "eml"], FileContentReader.ParseExtensions(" .TXT, *.cfg;xml\neml txt "));
        Assert.Empty(FileContentReader.ParseExtensions("  "));
    }

    [Fact]
    public void DefaultList_CoversCommonTextFormats()
    {
        var list = FileContentReader.ParseExtensions(FileContentReader.DefaultExtensions);
        foreach (var ext in new[] { "txt", "eml", "bat", "cfg", "config", "xml", "js", "json", "ps1", "md", "csv", "ini", "yaml" })
            Assert.Contains(ext, list);
        foreach (var binary in new[] { "exe", "dll", "docx", "pdf", "zip", "png" })
            Assert.DoesNotContain(binary, list);
    }

    [Theory]
    [InlineData(@"C:\a\notes.TXT", true)]
    [InlineData(@"C:\a\app.config", true)]
    [InlineData(@"C:\a\Dockerfile", true)]
    [InlineData(@"C:\a\.gitignore", true)]
    [InlineData(@"C:\a\setup.exe", false)]
    [InlineData(@"C:\a\README", false)]
    public void IsReadable_MatchesExtensionOrBareName(string path, bool expected)
    {
        var list = FileContentReader.ParseExtensions(FileContentReader.DefaultExtensions).ToHashSet();
        Assert.Equal(expected, FileContentReader.IsReadable(path, list));
    }

    [Fact]
    public void Read_SkipsBinaryOversizedAndUnlistedFiles()
    {
        var text = WriteFile("a.cfg", "key=value");
        var binary = Path.Combine(_dir, "b.txt");
        File.WriteAllBytes(binary, [0x41, 0x00, 0x42, 0x43]);
        var unlisted = WriteFile("c.bin", "plain");
        var big = WriteFile("d.log", new string('x', 20 * 1024));

        var files = FileContentReader.Read([text, binary, unlisted, big], Options().Extensions, 16 * 1024);

        var only = Assert.Single(files);
        Assert.Equal(text, only.Path);
        Assert.Equal("key=value", only.Text);
    }

    [Fact]
    public void Read_DecodesUtf16WithBom()
    {
        var path = WriteFile("u.txt", "Grüße", Encoding.Unicode);
        Assert.Equal("Grüße", Assert.Single(FileContentReader.Read([path], Options().Extensions, 1024)).Text);
    }

    [Fact]
    public void BuildEntry_StoresFileContentWithFileAsSource()
    {
        StaTestRunner.Run(() =>
        {
            var path = WriteFile("start.bat", "@echo off\r\necho hallo");

            var entry = ClipboardProcessor.BuildEntry(FileDrop(path), CodeDetectionMode.Normal, Options());

            Assert.NotNull(entry);
            Assert.Equal("@echo off\necho hallo", entry!.Content);
            Assert.Equal(path, entry.SourceUrl);
            Assert.True(FileContentReader.IsFileSource(entry.SourceUrl));
            Assert.StartsWith("Als Datei kopiert (start.bat)", entry.DetectionReason);
            Assert.Equal("Datei: start.bat", new EntryViewModel(entry).HeadlineText);
        });
    }

    [Fact]
    public void BuildEntry_CombinesSeveralFilesUnderSeparators()
    {
        StaTestRunner.Run(() =>
        {
            var a = WriteFile("a.txt", "Erste");
            var b = WriteFile("b.xml", "<root />");

            var entry = ClipboardProcessor.BuildEntry(FileDrop(a, b), CodeDetectionMode.Normal, Options());

            Assert.NotNull(entry);
            Assert.Equal("--- Datei: a.txt ---\nErste\n\n--- Datei: b.xml ---\n<root />", entry!.Content);
            Assert.Equal([a, b], FileContentReader.SourcePaths(entry.SourceUrl));
            Assert.Equal("2 Dateien: a.txt, b.xml", FileContentReader.Headline(entry.SourceUrl));
        });
    }

    [Fact]
    public void BuildEntry_FileWithOnlyUrlStaysText()
    {
        StaTestRunner.Run(() =>
        {
            var path = WriteFile("link.txt", "https://example.com/page");

            var entry = ClipboardProcessor.BuildEntry(FileDrop(path), CodeDetectionMode.Normal, Options());

            Assert.NotNull(entry);
            Assert.Equal(EntryType.Text, entry!.Type);
            Assert.Equal(path, entry.SourceUrl);
        });
    }

    [Fact]
    public void BuildEntry_IgnoresFilesWhenSwitchedOffOrTypeNotListed()
    {
        StaTestRunner.Run(() =>
        {
            var path = WriteFile("notes.txt", "Inhalt");

            Assert.Null(FileContentOptions.From(new AppSettings { FileContentEnabled = false }));
            Assert.Null(ClipboardProcessor.BuildEntry(FileDrop(path), CodeDetectionMode.Normal, null));
            Assert.Null(ClipboardProcessor.BuildEntry(FileDrop(path), CodeDetectionMode.Normal, Options("cfg")));
        });
    }

    [Fact]
    public void WebSourceIsNotAFileSource()
    {
        Assert.False(FileContentReader.IsFileSource("https://example.com/a"));
        Assert.Null(FileContentReader.Headline("https://example.com/a"));
        Assert.Null(FileContentReader.Headline(null));
    }
}

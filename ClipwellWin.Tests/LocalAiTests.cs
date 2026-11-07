using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClipwellWin.Models;
using ClipwellWin.Services;
using ClipwellWin.Views;
using Xunit;

namespace ClipwellWin.Tests;

public sealed class LocalAiTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "merkautomat-ai-" + Guid.NewGuid().ToString("N"));
    public LocalAiTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void Settings_DefaultOffAndRoundTripSelection()
    {
        Assert.Equal("off", new AppSettings().LocalAiModel);
        Assert.Equal("off", JsonSerializer.Deserialize<AppSettings>("{}")!.LocalAiModel);
        var json = JsonSerializer.Serialize(new AppSettings { LocalAiModel = LocalAiModel.Id });
        Assert.Equal(LocalAiModel.Id, JsonSerializer.Deserialize<AppSettings>(json)!.LocalAiModel);
        Assert.Equal("off", LocalAiModel.Normalize("unknown"));
        Assert.Equal("off", LocalAiModel.Normalize(null));
    }

    [Fact]
    public async Task DisabledServiceDoesNotStartOrCreateModelFiles()
    {
        using var ai = new LocalAiService(_root, () => "off");
        await Assert.ThrowsAsync<InvalidOperationException>(() => ai.SuggestAsync([Source("Text")], [], TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public void InputBoundsSourcesDeduplicatesAndPreservesDataBoundary()
    {
        var sources = Enumerable.Range(0, 20).Select(i => Source(i + new string('x', 2000))).ToList();
        sources.Insert(1, sources[0]);
        var input = LocalAiCategorization.BuildInput(sources, ["Arbeit", "Arbeit", "", new string('x', 81)]);
        using var data = JsonDocument.Parse(input);
        Assert.Equal(8, data.RootElement.GetProperty("sources").GetArrayLength());
        Assert.All(data.RootElement.GetProperty("sources").EnumerateArray(), s => Assert.Equal(700, s.GetProperty("text").GetString()!.Length));
        Assert.Single(data.RootElement.GetProperty("existing_categories").EnumerateArray());
        using var request = JsonDocument.Parse(LocalAiCategorization.BuildRequest(input));
        Assert.Equal(input, request.RootElement.GetProperty("messages")[1].GetProperty("content").GetString());
        Assert.False(request.RootElement.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
    }

    [Fact]
    public void EmptySourcesAreNotSent() => Assert.Throws<ArgumentException>(() => LocalAiCategorization.BuildInput([Source(" \r\n ")], []));

    [Fact]
    public void ParserAcceptsValidSuggestionAndClearsUncertainCategory()
    {
        var valid = LocalAiCategorization.Parse(Envelope(new { title = "VPN", category = "Technik", reason = "Netzwerkzugang", uncertain = false }));
        Assert.Equal("Technik", valid.Category);
        var uncertain = LocalAiCategorization.Parse(Envelope(new { title = "", category = "Vermutung", reason = "Nur Menütexte", uncertain = true }));
        Assert.True(uncertain.Uncertain);
        Assert.Empty(uncertain.Category);
    }

    [Theory]
    [InlineData("length", "Technik", "Netzwerk")]
    [InlineData("stop", "", "Netzwerk")]
    [InlineData("stop", "Technik\nFremdtext", "Netzwerk")]
    [InlineData("stop", "Technik", "")]
    public void ParserRejectsIncompleteOrMalformedSuggestions(string finish, string category, string reason)
        => Assert.Throws<InvalidDataException>(() => LocalAiCategorization.Parse(Envelope(new { title = "VPN", category, reason, uncertain = false }, finish)));

    [Fact]
    public void ParserRejectsMissingFieldsAndLongTitles()
    {
        Assert.Throws<InvalidDataException>(() => LocalAiCategorization.Parse("{}"));
        Assert.Throws<InvalidDataException>(() => LocalAiCategorization.Parse(Envelope(new { title = new string('x', 121), category = "Technik", reason = "Grund", uncertain = false })));
    }

    [Fact]
    public async Task DownloadPublishesOnlyVerifiedBytes()
    {
        var bytes = Encoding.UTF8.GetBytes("verified model fixture");
        var manifest = Manifest(bytes);
        var path = Path.Combine(_root, manifest.Name);
        using var client = new HttpClient(new FakeDownload(bytes));
        await LocalAiModel.DownloadAsync(client, manifest, path, null, TestContext.Current.CancellationToken);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(path + ".part"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptOrTruncatedDownloadKeepsExistingFile(bool truncated)
    {
        var bytes = Encoding.UTF8.GetBytes("verified fixture");
        var path = Path.Combine(_root, "model");
        await File.WriteAllTextAsync(path, "previous version", TestContext.Current.CancellationToken);
        var invalid = truncated ? bytes[..^1] : Encoding.UTF8.GetBytes("corrupt! fixture");
        using var client = new HttpClient(new FakeDownload(invalid));
        await Assert.ThrowsAsync<InvalidDataException>(() => LocalAiModel.DownloadAsync(client, Manifest(bytes), path, null, TestContext.Current.CancellationToken));
        Assert.Equal("previous version", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public async Task CancelledDownloadDoesNotPublishPartialFile()
    {
        var bytes = Encoding.UTF8.GetBytes("fixture");
        using var cancel = new CancellationTokenSource();
        using var client = new HttpClient(new FakeDownload(bytes, cancel.Cancel));
        var path = Path.Combine(_root, "model");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LocalAiModel.DownloadAsync(client, Manifest(bytes), path, null, cancel.Token));
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public void DialogDoesNotApplySuggestionsUntilClickedAndInvalidatesChangedSources()
        => StaTestRunner.Run(() =>
        {
            var choices = new List<BrainKnowledgeDialog.SourceChoice> { new() { Source = Source("VPN"), Selected = true } };
            var dialog = new BrainKnowledgeDialog("Eigener Titel", "Arbeit", ["Arbeit"], choices, [new("Neu", null, "")], false);
            void Set(string name, object value) => typeof(BrainKnowledgeDialog).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(dialog, value);
            Set("_aiSuggestion", new LocalAiSuggestion("VPN-Zugang", "Technik", "Netzwerk", false));
            Set("_aiKeys", new HashSet<string> { "c:1" });
            Assert.Equal("Arbeit", dialog.TopicCategory);
            var apply = (System.Windows.Controls.Button)dialog.FindName("AiApplyButton");
            apply.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Assert.Equal("Technik", dialog.TopicCategory);
            Assert.Equal("VPN-Zugang", dialog.TopicTitle);
            choices[0].Selected = false;
            typeof(BrainKnowledgeDialog).GetMethod("UpdateSelection", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(dialog, null);
            Assert.False(apply.IsEnabled);
            Assert.Null(typeof(BrainKnowledgeDialog).GetField("_aiSuggestion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(dialog));
            dialog.Close();
        });

    private static BrainSource Source(string text) => new("c:1", "Titel", text, "", DateTime.MinValue);
    private static LocalAiDownload Manifest(byte[] bytes) => new("model", "https://example.test/model", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
    private static string Envelope(object content, string finish = "stop") => JsonSerializer.Serialize(new
    { choices = new[] { new { finish_reason = finish, message = new { content = JsonSerializer.Serialize(content) } } } });

    private sealed class FakeDownload(byte[] bytes, Action? onSend = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onSend?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }
}

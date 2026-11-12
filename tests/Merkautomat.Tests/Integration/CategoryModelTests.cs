using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using Merkautomat.Models;
using Merkautomat.Services;
using Merkautomat.Views;
using Xunit;

namespace Merkautomat.Tests;

public sealed class CategoryModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "merkautomat-categories-" + Guid.NewGuid().ToString("N"));
    public CategoryModelTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Theory]
    [InlineData(CategoryModelFiles.MiniLmId, "MiniLM L6")]
    [InlineData(CategoryModelFiles.HorizonId, "Horizon ZeroShot Small")]
    public void SelectionRoundTripsUsesSeparateFilesAndKeepsLanguageModelFallback(string id, string name)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings { LocalAiModel = id }))!;
        using var ai = new LocalAiService(_root, () => settings.LocalAiModel);
        Assert.Equal(id, LocalAiModel.Normalize(settings.LocalAiModel));
        Assert.True(ai.Enabled);
        Assert.True(ai.UsesCategoryOnly);
        Assert.False(ai.UsesE5);
        Assert.False(ai.Ready);
        Assert.Equal(name, ai.DisplayName);
        Assert.Equal(Path.Combine(_root, id), ai.CategoryModel!.DirectoryPath);
        Assert.Equal(LocalAiModel.Id, ai.Model.ModelId);
        settings.LocalAiModel = LocalAiModel.QwenId;
        Assert.Null(ai.CategoryModel);
        Assert.False(ai.UsesCategoryOnly);
        Assert.Equal(LocalAiModel.QwenId, ai.Model.ModelId);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Theory]
    [InlineData(CategoryModelFiles.MiniLmId)]
    [InlineData(CategoryModelFiles.HorizonId)]
    public async Task MissingFilesFailForSelectedClassifierWithoutStartingOrDownloadingAnything(string id)
    {
        using var ai = new LocalAiService(_root, () => id);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => ai.SuggestAsync(
            [new BrainSource("test", "Test", "A text about travel and holidays.", "", DateTime.MinValue)],
            ["Travel", "Work"], TestContext.Current.CancellationToken));
        Assert.Contains(ai.DisplayName, error.Message);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ai.InstallSelectedAsync(null, canceled.Token));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Theory]
    [InlineData(CategoryModelFiles.MiniLmId)]
    [InlineData(CategoryModelFiles.HorizonId)]
    public void CategorizeDialogNamesTheSelectedClassifierAndDisablesItUntilInstalled(string id)
        => StaTestRunner.Run(() =>
        {
            using var ai = new LocalAiService(_root, () => id);
            var dialog = new CategorizeDialog("Test", "", new CategorizationQueue([], new Dictionary<string, string>()), ai,
                (_, _) => { }, false, false);
            var choices = ((System.Windows.Controls.ComboBox)dialog.FindName("SourceBox")).Items.OfType<ComboBoxItem>().ToArray();
            var classifier = Assert.Single(choices, c => ((string)c.Content).StartsWith(ai.DisplayName, StringComparison.Ordinal));
            Assert.False(classifier.IsEnabled);
            Assert.Contains("nicht installiert", (string)classifier.Content);
            dialog.Close();
        });
}

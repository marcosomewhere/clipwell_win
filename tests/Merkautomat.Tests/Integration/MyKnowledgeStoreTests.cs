using System.IO;
using Merkautomat.Services;
using Xunit;

namespace Merkautomat.Tests;

/// <summary>Runs with the key-file tests, so the process-wide data key cannot change in between.</summary>
[Collection(nameof(DataKeyFileTests))]
public class MyKnowledgeStoreTests
{
    [Fact]
    public void StateIsStoredEncryptedAndReadBack()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"merkautomat-myknowledge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, MyKnowledge.FileName);
        try
        {
            var state = new MyKnowledgeState { LastRun = new DateTime(2026, 11, 11, 8, 0, 0), Dismissed = ["n:weg.md"] };
            state.Entries.Add(new MyKnowledgeEntry
            {
                Key = "c:1", Source = SemanticSource.Clipboard, Title = "Geheimer Plan", Text = "Geheimer Plan für Montag",
                Weight = 0.3, Kept = true,
            });

            MyKnowledge.Save(file, state);

            Assert.True(SecureFile.IsEncryptedFile(file));
            Assert.DoesNotContain("Geheimer Plan", File.ReadAllText(file));
            var loaded = MyKnowledge.Load(file);
            var entry = Assert.Single(loaded.Entries);
            Assert.Equal("Geheimer Plan für Montag", entry.Text);
            Assert.True(entry.Kept);
            Assert.Equal(SemanticSource.Clipboard, entry.Source);
            Assert.Equal(["n:weg.md"], loaded.Dismissed);
            Assert.Equal(state.LastRun, loaded.LastRun);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void MissingFileGivesAnEmptyState()
        => Assert.Empty(MyKnowledge.Load(Path.Combine(Path.GetTempPath(), $"fehlt-{Guid.NewGuid():N}.json")).Entries);
}

using System.IO;
using System.Windows;
using Merkautomat.Services;

namespace Merkautomat.Views;

/// <summary>"Zu Mein Wissen hinzufügen" from the library, the search and the Brain.</summary>
public partial class KnowledgeBaseWindow
{
    private void FixLibraryInMyKnowledge_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedItems().ToList();
        if (selected.Count == 0) return;
        var keys = selected.Where(i => i.ArticleKind != KbArticleKind.Recording).Select(LibraryKey).ToList();
        // A recording stands for all its snapshots.
        var prefixes = selected.Where(i => i.ArticleKind == KbArticleKind.Recording)
            .Select(i => KnowledgeCategories.RecordingKey(Path.GetFileName(i.FilePath)) + "#").ToList();
        FixInMyKnowledgeAsync(keys, prefixes).Forget("Zu Mein Wissen hinzufügen");
    }

    private void FixSearchInMyKnowledge_Click(object sender, RoutedEventArgs e)
    {
        var marked = MarkedSemanticChunks().Select(c => c.ItemKey).ToList();
        var keys = marked.Count > 0 ? marked
            : SemanticResults.SelectedItem is SemanticResultItem item ? [item.Hit.Chunk.ItemKey] : [];
        FixInMyKnowledgeAsync(keys.Distinct().ToList(), null).Forget("Zu Mein Wissen hinzufügen");
    }

    private void FixBrainInMyKnowledge_Click(object sender, RoutedEventArgs e)
    {
        if (_brain is not { } graph || CurrentBrainGroup() is not { Nodes.Count: > 0 } group) return;
        var keys = group.Nodes.Distinct().Select(i => graph.Nodes[i].ItemKey).ToList();
        FixInMyKnowledgeAsync(keys, null).Forget("Zu Mein Wissen hinzufügen");
    }

    private async Task FixInMyKnowledgeAsync(IReadOnlyCollection<string> keys, IReadOnlyCollection<string>? prefixes)
    {
        if (keys.Count == 0 && (prefixes?.Count ?? 0) == 0)
        {
            StatusLabel.Text = "Bitte zuerst Einträge auswählen.";
            return;
        }
        StatusLabel.Text = $"Wird in {AppNames.MyKnowledge} aufgenommen …";
        var count = await _app.FixInMyKnowledgeAsync(keys, prefixes);
        StatusLabel.Text = count > 0
            ? $"{count} {(count == 1 ? "Eintrag" : "Einträge")} in {AppNames.MyKnowledge} aufgenommen (100 %, ohne Wartezeit, ohne Ablauf)."
            : "Kein auswertbarer Text gefunden, nichts hinzugefügt.";
    }
}

using System.IO;
using System.Windows;
using ClipwellWin.Models;
using ClipwellWin.Services;
using MessageBox = ClipwellWin.Views.AppMessageBox;

namespace ClipwellWin.Views;

public partial class KnowledgeBaseWindow
{
    private void BrainKnowledge_Click(object sender, RoutedEventArgs e)
        => OpenBrainKnowledge(summarize: false);

    private void BrainSummarize_Click(object sender, RoutedEventArgs e)
        => OpenBrainKnowledge(summarize: true);

    private void OpenBrainKnowledge(bool summarize)
    {
        if (_brain == null) return;
        var selected = BrainCanvasView.SelectedNode;
        var cluster = _brainCluster >= 0 && _brainCluster < _brain.Clusters.Count ? _brain.Clusters[_brainCluster] : null;
        if (cluster == null && (selected < 0 || selected >= _brain.Nodes.Count))
        {
            MessageBox.Show(this, "Bitte zuerst rechts ein Thema oder im Netz einen Hauptpunkt auswählen.", "Brain", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var settings = _app.CurrentSettings;
        settings.BrainAnnotations ??= [];
        settings.BrainSummaries ??= [];
        var indices = cluster != null ? cluster.Members.ToList()
            : new[] { selected }.Concat(_brain.Neighbors(selected).Select(x => x.Node)).ToList();
        var annotation = settings.BrainAnnotations.GetValueOrDefault(_brain.Nodes[indices[0]].ItemKey);
        var title = cluster?.Label ?? annotation?.Name;
        if (string.IsNullOrWhiteSpace(title)) title = _brain.Nodes[selected].Item.Title;
        var category = cluster?.Category ?? KnowledgeCategories.Of(annotation).ToString();
        var entries = _app.SnapshotEntries().ToDictionary(x => x.Id);
        var chunks = _app.Semantic.Snapshot().GroupBy(x => x.Chunk.ItemKey).ToDictionary(g => g.Key, g => g.ToList());
        var summaryIds = settings.BrainSummaries.Select(s => s.NoteId).ToHashSet();
        try
        {
            var sources = indices.Select(index =>
            {
                var node = _brain.Nodes[index];
                var c = node.Item;
                var text = chunks.TryGetValue(node.ItemKey, out var all)
                    ? string.Join("\n", all.OrderBy(x => int.TryParse(x.Chunk.Key.AsSpan(x.Chunk.Key.LastIndexOf(':') + 1), out var ordinal) ? ordinal : 0)
                        .Select(x => x.Chunk.Text)) : node.Text;
                var location = c.FilePath ?? $"Zwischenablage #{c.EntryId}";
                if (c.Source == SemanticSource.Note && c.FilePath is { } path)
                {
                    if (!File.Exists(path)) throw new IOException($"Die Quelle ist nicht mehr vorhanden: {path}");
                    text = NoteStore.KbBody(File.ReadAllText(path));
                }
                else if (c.Source == SemanticSource.Clipboard)
                {
                    if (!entries.TryGetValue(c.EntryId, out var entry)) throw new IOException("Ein Zwischenablage-Eintrag ist nicht mehr vorhanden. Bitte das Brain aktualisieren.");
                    text = entry.Type == EntryType.Image ? entry.OcrText ?? "" : entry.Content ?? "";
                    if (!string.IsNullOrWhiteSpace(entry.SourceUrl)) location += " · " + entry.SourceUrl;
                }
                var edges = cluster != null || index == selected ? [] : _brain.EdgesOf(selected).Where(edge => edge.Other(selected) == index).ToList();
                var isSummary = c.Source == SemanticSource.Note && c.FilePath != null && summaryIds.Contains(Path.GetFileNameWithoutExtension(c.FilePath));
                return new BrainKnowledgeDialog.SourceChoice
                {
                    Source = new BrainSource(node.ItemKey, c.Title, text, location, c.Date),
                    Reason = isSummary ? "Vorhandene Zusammenfassung"
                        : cluster != null ? "Teil des gewählten Themas"
                        : index == selected ? "Hauptpunkt" : string.Join(" · ", edges.Select(edge => edge.Detail)),
                    Selected = !isSummary && (cluster != null || index == selected || edges.Any(edge => edge.Kind is GraphEdgeKind.Semantic or GraphEdgeKind.Summary)),
                };
            }).ToList();
            var scope = sources.Select(s => s.Source.Key).ToHashSet();
            var saved = settings.BrainSummaries.Where(s => s.SourceKeys.Any(scope.Contains))
                .OrderByDescending(s => s.SourceKeys.Count(scope.Contains)).ToList();
            var summaries = new List<BrainKnowledgeDialog.SummaryChoice> { new("Neue Zusammenfassung anlegen", null, "") };
            foreach (var summary in saved)
            {
                var path = NoteStore.KbPathFor(AppPaths.KbNotesDir, summary.NoteId);
                summaries.Add(new($"Aktualisieren: {summary.Title}", summary.NoteId,
                    File.Exists(path) ? NoteStore.KbBody(File.ReadAllText(path)) : "", summary.SourceKeys));
            }
            var dialog = new BrainKnowledgeDialog(title ?? "Zusammenfassung", category,
                KnowledgeCategories.Paths(settings), sources, summaries, summarize, _app.LocalAi,
                KnowledgeCategories.Assignments(settings.BrainAnnotations)) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            if (dialog.TopicCategory.Length > 0) _app.RecordCategorizationFeedback(dialog.AiSuggestionForSelection, dialog.TopicCategory);
            var chosen = dialog.SelectedSources;
            string? newSummaryId = null;
            if (dialog.SaveSummary)
            {
                // The id is accepted only from the locally stored list, never from a source document.
                if (dialog.SummaryId != null && !saved.Any(s => s.NoteId == dialog.SummaryId)) throw new InvalidOperationException("Unbekannte Zusammenfassung.");
                var result = BrainKnowledge.Save(AppPaths.NotesDir, AppPaths.KbNotesDir, dialog.SummaryId, dialog.Draft, DateTime.Now);
                newSummaryId = result.NoteId;
                var link = settings.BrainSummaries.FirstOrDefault(s => s.NoteId == newSummaryId);
                if (link == null) { link = new BrainSavedSummary { NoteId = newSummaryId }; settings.BrainSummaries.Add(link); }
                link.Title = dialog.TopicTitle;
                link.SourceKeys = chosen.Select(s => s.Key).ToList();
            }
            foreach (var key in chosen.Select(s => s.Key).Concat(newSummaryId != null ? ["n:" + newSummaryId + ".md"] : []))
                settings.BrainAnnotations[key] = KnowledgeCategories.Annotation(settings.BrainAnnotations.GetValueOrDefault(key), dialog.TopicTitle, dialog.TopicCategory);
            KnowledgeCategories.Define(settings, KnowledgeCategories.Parse(dialog.TopicCategory));
            _app.SaveSettings();
            _app.NotifyKnowledgeBaseChanged();
            RebuildBrain(keepLayout: true);
            MessageBox.Show(this, dialog.SaveSummary
                ? "Zusammenfassung gespeichert. Suche und Brain werden im Hintergrund aktualisiert. Die Ausgangseinträge bleiben erhalten."
                : "Themenname und Kategorie gespeichert.", AppNames.KnowledgeBase, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            App.LogCrash(ex);
            MessageBox.Show(this, $"Die Bearbeitung konnte nicht abgeschlossen werden: {ex.Message}", AppNames.KnowledgeBase, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

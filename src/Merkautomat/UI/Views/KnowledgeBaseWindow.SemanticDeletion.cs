using System.IO;
using System.Windows;
using Merkautomat.Services;
using MessageBox = Merkautomat.Views.AppMessageBox;

namespace Merkautomat.Views;

public partial class KnowledgeBaseWindow
{
    private IEnumerable<SemanticResultItem> SemanticItems => SemanticResults.Items.OfType<SemanticResultItem>();
    private bool SemanticMarksGroups => _semanticMode == SemanticMode.Duplicates && SemanticDuplicateView.SelectedIndex == 0;
    private IEnumerable<SemanticChunk> MarkedSemanticChunks()
        => SemanticItems.Where(i => i.Marked).SelectMany(i => SemanticMarksGroups
            ? new[] { i.Hit.Chunk }.Concat(i.Hit.Duplicates) : [i.Hit.Chunk]);

    // In the group view a mark removes only the surplus copies; the newest entry of each group stays.
    private SemanticDeletePlan PlanSemanticDeletion() => SemanticMarksGroups
        ? SemanticDeletion.Plan(SemanticItems.Where(i => i.Marked).SelectMany(i => i.Hit.Duplicates), AppPaths.NotesDir, AppPaths.KbNotesDir,
            _app.Recorder.Directory, _app.Recorder.CurrentSessionPath, SemanticItems.Where(i => i.Marked).Select(i => i.Hit.Chunk))
        : SemanticDeletion.Plan(MarkedSemanticChunks(), AppPaths.NotesDir, AppPaths.KbNotesDir, _app.Recorder.Directory, _app.Recorder.CurrentSessionPath);

    private void UpdateSemanticMarks()
    {
        var count = SemanticItems.Count(i => i.Marked);
        SemanticMarkedLabel.Text = count == 0 ? "Keine Markierung" : $"{count} markiert · {MarkedSemanticChunks().Select(c => c.ItemKey).Distinct().Count()} Einträge"
            + (SemanticMarksGroups && count > 0 ? $" · {SemanticItems.Where(i => i.Marked).SelectMany(i => i.Hit.Duplicates).Select(c => c.ItemKey).Distinct().Count()} ältere Kopien" : "");
        SemanticMarkToggle.Content = count > 0 && count == SemanticResults.Items.Count ? "Auswahl aufheben" : "Alle markieren";
        SemanticMarkToggle.IsEnabled = SemanticResults.Items.Count > 0 && !_semanticBusy;
        SemanticMarkToggle.ToolTip = SemanticMarksGroups ? "Eine Markierung umfasst die ganze Gruppe. Löschen entfernt nur die älteren Kopien, der neueste Eintrag bleibt. Für einzelne Kopien: Alle Dubletten einzeln." : "Markierungen betreffen jeweils den angezeigten Eintrag.";
        SemanticMarkedLabel.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SemanticDeleteMarked.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SemanticDeleteMarked.IsEnabled = count > 0 && !_semanticBusy;
        SemanticCategorizeMarked.Visibility = SemanticDeleteMarked.Visibility;
        SemanticCategorizeMarked.IsEnabled = SemanticDeleteMarked.IsEnabled;
        SemanticSimilarMarked.Visibility = count > 0 && _semanticMode != SemanticMode.Duplicates ? Visibility.Visible : Visibility.Collapsed;
        SemanticSimilarMarked.IsEnabled = SemanticDeleteMarked.IsEnabled;
        if (_view == KbView.Semantic) DeleteButton.IsEnabled = SemanticDeleteMarked.IsEnabled;
    }

    private void SemanticMark_Click(object sender, RoutedEventArgs e) => UpdateSemanticMarks();
    private void SemanticMarkToggle_Click(object sender, RoutedEventArgs e)
    {
        if (SemanticItems.Any() && SemanticItems.All(i => i.Marked)) SemanticUnmarkAll_Click(sender, e);
        else SemanticMarkAll_Click(sender, e);
    }
    private void SemanticMarkAll_Click(object sender, RoutedEventArgs e) { foreach (var item in SemanticItems) item.Marked = true; UpdateSemanticMarks(); }
    private void SemanticUnmarkAll_Click(object sender, RoutedEventArgs e) { foreach (var item in SemanticItems) item.Marked = false; UpdateSemanticMarks(); }

    private void SemanticDeleteMarked_Click(object sender, RoutedEventArgs e)
    {
        if (_semanticBusy) return;
        try
        {
            var plan = PlanSemanticDeletion();
            if (plan.Targets.Count == 0)
            {
                MessageBox.Show(this, "Keine löschbaren Einträge markiert. Dateien aus dem Knowledgeordner werden über den Quellordner verwaltet; laufende Bildschirmaufnahmen können nicht gelöscht werden.", AppNames.KnowledgeBase);
                return;
            }
            var clips = plan.Targets.Count(t => t.Source == SemanticSource.Clipboard);
            var notes = plan.Targets.Count(t => t.Source == SemanticSource.Note);
            var recordings = plan.Targets.Count(t => t.Source == SemanticSource.Recording);
            var question = $"Markierte Inhalte endgültig löschen?\n\n{clips} Zwischenablage-Einträge\n{notes} Sprachnotizen\n{recordings} vollständige Bildschirmaufnahmen";
            if (notes > 0) question += "\n\nSprachnotizen werden einschließlich Original, Ideenkombinat-Kopie und vorhandener Sicherungsfassungen gelöscht.";
            if (recordings > 0) question += "\n\nBei Bildschirmaufnahmen wird die GESAMTE Aufnahmedatei mit allen Bildwechseln gelöscht, auch wenn nur ein Bildwechsel markiert wurde.";
            if (SemanticMarksGroups) question += "\n\nIn markierten Gruppen werden nur die älteren Kopien gelöscht, der neueste Eintrag jeder Gruppe bleibt erhalten.";
            if (plan.SkippedRecordings > 0) question += $"\n\n{plan.SkippedRecordings} laufende Aufnahme(n) werden übersprungen.";
            question += "\n\nBetroffen:\n" + string.Join("\n", plan.Targets.Take(8).Select(t => "• " + t.Title.Replace('\n', ' ')))
                + (plan.Targets.Count > 8 ? $"\n… und {plan.Targets.Count - 8} weitere." : "");
            if (MessageBox.Show(this, question, "Markierte Inhalte löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;

            var failures = new List<string>();
            var removed = new List<SemanticDeleteTarget>();
            foreach (var target in plan.Targets)
            {
                try
                {
                    // Recheck after the confirmation in case a recording started while the dialog was open.
                    if (target.Source == SemanticSource.Recording && string.Equals(target.Paths[0], _app.Recorder.CurrentSessionPath, StringComparison.OrdinalIgnoreCase)) continue;
                    if (target.Source == SemanticSource.Clipboard) _app.DeleteHistoryEntries([target.EntryId]);
                    else foreach (var path in target.Paths) File.Delete(path);
                    removed.Add(target);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
                { App.LogCrash(ex); failures.Add(target.Title + ": " + ex.Message); }
            }
            var settings = _app.CurrentSettings;
            foreach (var target in removed)
            {
                settings.BrainAnnotations?.Remove(target.Key);
                if (target.Source == SemanticSource.Note)
                    settings.BrainSummaries?.RemoveAll(s => "n:" + s.NoteId + ".md" == target.Key);
            }
            BrainCollections.Forget(settings.BrainCollections ?? [], removed.Select(t => t.Key).ToHashSet(StringComparer.Ordinal));
            _app.ForgetMyKnowledge(removed.Where(t => t.Source == SemanticSource.Note).Select(t => t.Key).ToList(),
                removed.Where(t => t.Source == SemanticSource.Recording && t.Paths.Count > 0)
                    .Select(t => KnowledgeCategories.RecordingKey(Path.GetFileName(t.Paths[0])) + "#").ToList());
            try { _app.SaveSettings(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.LogCrash(ex); failures.Add("Zuordnungen konnten nicht gespeichert werden: " + ex.Message); }
            SemanticUnmarkAll_Click(this, e);
            _app.NotifyKnowledgeBaseChanged();
            StartSemanticSync();
            if (failures.Count > 0)
                MessageBox.Show(this, "Einige Inhalte konnten nicht gelöscht werden:\n\n" + string.Join("\n", failures.Take(10)), AppNames.KnowledgeBase, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            App.LogCrash(ex);
            MessageBox.Show(this, "Löschen nicht abgeschlossen: " + ex.Message, AppNames.KnowledgeBase, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

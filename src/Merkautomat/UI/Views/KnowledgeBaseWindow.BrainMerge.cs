using System.Globalization;
using System.IO;
using System.Windows;
using Merkautomat.Models;
using Merkautomat.Services;
using MessageBox = Merkautomat.Views.AppMessageBox;

namespace Merkautomat.Views;

/// <summary>
/// Brain: merge the selected node with linked entries into one note without losing anything. Originals are removed
/// only on request, only where that is safe and only after the saved note was checked to contain their full text.
/// </summary>
public partial class KnowledgeBaseWindow
{
    /// <summary>Content links from this similarity on are preselected; weaker links and time links are offered unchecked.</summary>
    private const float MergePreselect = 0.92f;

    private void BrainMerge_Click(object sender, RoutedEventArgs e) => MergeBrainNodes();

    private void BrainNodeMenu_Opened(object sender, RoutedEventArgs e)
    {
        var node = _brain != null && BrainCanvasView.SelectedNode >= 0 && BrainCanvasView.SelectedNode < _brain.Nodes.Count;
        BrainNodeMergeItem.IsEnabled = node;
        BrainNodeFixItem.IsEnabled = node;
    }

    private void BrainNodeFix_Click(object sender, RoutedEventArgs e)
    {
        var node = BrainCanvasView.SelectedNode;
        if (_brain is not { } graph || node < 0 || node >= graph.Nodes.Count) return;
        FixInMyKnowledgeAsync([graph.Nodes[node].ItemKey], null).Forget("Zu Mein Wissen hinzufügen");
    }

    private void MergeBrainNodes()
    {
        if (_brain is not { } graph) return;
        var selected = BrainCanvasView.SelectedNode;
        // Without a selected node the highlighted group is offered, with the first entry as the main one.
        var group = CurrentBrainGroup();
        if (selected < 0 && group is not { Nodes.Count: > 1 })
        {
            MessageBox.Show(this, "Bitte im Brain einen Knoten anklicken (oder eine Gruppe hervorheben) und dann zusammenführen.", AppNames.KnowledgeBase);
            return;
        }
        var main = selected >= 0 ? selected : group!.Nodes[0];
        var candidates = new List<int> { main };
        if (selected >= 0)
            candidates.AddRange(graph.Neighbors(main).Select(n => n.Node));
        else candidates.AddRange(group!.Nodes);
        candidates = candidates.Distinct().ToList();
        if (candidates.Count < 2)
        {
            MessageBox.Show(this, "Dieser Eintrag hat im Netz keine verknüpften Einträge. Zum Zusammenführen bitte einen Knoten mit Verknüpfungen wählen.",
                AppNames.KnowledgeBase);
            return;
        }

        var entries = _app.SnapshotEntries().DistinctBy(x => x.Id).ToDictionary(x => x.Id);
        var sources = new Dictionary<int, MergeSource>();
        foreach (var index in candidates)
            if (ReadMergeSource(graph.Nodes[index], entries) is { } source) sources[index] = source;
        if (!sources.ContainsKey(main) || sources.Count < 2)
        {
            MessageBox.Show(this, "Der Text der Einträge konnte nicht vollständig gelesen werden; es wird nichts zusammengeführt.", AppNames.KnowledgeBase);
            return;
        }

        // Without a selected node the whole highlighted group is meant; otherwise close content links only.
        bool Strong(int other) => selected < 0 || other == main
            || graph.EdgesOf(main).Any(e => (e.A == other || e.B == other) && e.Kind == GraphEdgeKind.Semantic && e.Weight >= MergePreselect);
        var choices = sources.Select(p => new BrainChoiceDialog.Choice
        {
            Key = p.Key.ToString(CultureInfo.InvariantCulture),
            Title = p.Value.Title,
            Detail = $"{SemanticChunk.SourceName(p.Value.Source)} · {p.Value.Date.ToString("dd.MM.yyyy", De)} · "
                + (p.Value.Removable ? "kann danach entfernt werden" : "bleibt erhalten: " + p.Value.KeepReason),
            Initial = Strong(p.Key),
        }).ToList();
        var removeChoice = new BrainChoiceDialog.Choice
        {
            Key = "remove", Title = "Originale danach entfernen",
            Detail = "Nur Zwischenablage-Texte und Sprachnotizen, und nur wenn ihr vollständiger Text in der neuen Notiz geprüft wurde. "
                + "Kategorie, Mappen und Beziehungen gehen auf die neue Notiz über.",
            Initial = false,
        };
        var dialog = new BrainChoiceDialog("Verlustfrei zusammenführen",
            "Die markierten Einträge werden zu einer Sprachnotiz zusammengeführt. Jeder Originaltext steht darin vollständig mit seiner Herkunft; "
            + "nur wortgleiche Texte erscheinen einmal mit allen Herkünften. Kategorie, Mappen, Beziehungen und Zusammenfassungsquellen gehen auf die neue Notiz über.",
            sources[main].Title,
            [new BrainChoiceDialog.Section($"EINTRÄGE ({choices.Count})", choices), new BrainChoiceDialog.Section("DANACH", [removeChoice])],
            "Zusammenführen", nameLabel: "Titel der neuen Notiz") { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var chosen = choices.Where(c => c.IsChecked).Select(c => sources[int.Parse(c.Key, CultureInfo.InvariantCulture)]).ToList();
        if (chosen.Count < 2)
        {
            MessageBox.Show(this, "Bitte mindestens zwei Einträge markieren.", AppNames.KnowledgeBase);
            return;
        }
        var title = dialog.NameText.Length > 0 ? dialog.NameText : sources[main].Title;
        CompleteMerge(chosen, title, removeChoice.IsChecked);
    }

    private void CompleteMerge(IReadOnlyList<MergeSource> chosen, string title, bool removeOriginals)
    {
        var now = DateTime.Now;
        string kbPath;
        try
        {
            kbPath = _app.SaveQuickNote(BrainMerge.Build(chosen, title, now), new ContentMetadata
            {
                CapturedAt = now, CaptureMethod = "Zusammengeführt", ContentType = "Markdown", Application = AppNames.App,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.LogError("Einträge zusammenführen", ex);
            MessageBox.Show(this, "Die neue Notiz konnte nicht gespeichert werden; es wurde nichts verändert.\n\n" + ex.Message, AppNames.KnowledgeBase,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var newKey = "n:" + Path.GetFileName(kbPath);

        // Both copies of the new note must contain every original text before anything may go.
        List<string> missing;
        try
        {
            var original = Path.Combine(AppPaths.NotesDir, Path.GetFileName(kbPath));
            missing = BrainMerge.Missing(SecureFile.ReadAllText(kbPath), chosen)
                .Union(BrainMerge.Missing(SecureFile.ReadAllText(original), chosen)).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            App.LogError("Zusammengeführte Notiz prüfen", ex);
            missing = chosen.Select(c => c.Key).ToList();
        }

        var settings = _app.CurrentSettings;
        var keys = chosen.Select(c => c.Key).ToList();
        var category = BrainMerge.SharedCategory(keys.Select(k => KnowledgeCategories.Effective(settings.BrainAnnotations, k)).ToList(),
            KnowledgeCategories.Effective(settings.BrainAnnotations, keys[0]));
        BrainMerge.Transfer(settings, keys, newKey, category);

        var removed = new List<MergeSource>();
        var failures = new List<string>();
        if (removeOriginals)
            foreach (var source in chosen.Where(c => c.Removable && !missing.Contains(c.Key)))
            {
                try
                {
                    if (source.Source == SemanticSource.Clipboard && long.TryParse(source.Key.AsSpan(2), out var id)) _app.DeleteHistoryEntries([id]);
                    else if (source.Source == SemanticSource.Note) DeleteNoteFiles(source.Key);
                    else continue;
                    settings.BrainAnnotations?.Remove(source.Key);
                    removed.Add(source);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    App.LogError("Zusammengeführten Eintrag entfernen", ex);
                    failures.Add(source.Title);
                }
            }
        var removedKeys = removed.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        BrainCollections.Forget(settings.BrainCollections ?? [], removedKeys);
        try { _app.SaveSettings(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.LogError("Zusammenführen: Zuordnungen speichern", ex);
            failures.Add("Zuordnungen: " + ex.Message);
        }
        var notes = removed.Where(r => r.Source == SemanticSource.Note).Select(r => r.Key).ToList();
        if (notes.Count > 0) _app.ForgetMyKnowledge(notes);
        // Something the user kept for good stays kept in its merged form.
        if (_app.MyKnowledgeSnapshot().Any(e => e.Kept && keys.Contains(e.Key)))
            _app.FixInMyKnowledgeAsync([newKey]).Forget("Zu Mein Wissen hinzufügen");
        // The new note stands for the originals that stay; Mein Wissen keeps one copy of the knowledge, not two.
        var staying = keys.Where(k => !removedKeys.Contains(k)).ToList();
        if (staying.Count > 0) _app.DismissMyKnowledge(staying);

        _pendingBrainFocus = (new HashSet<string>([newKey], StringComparer.Ordinal), $"Zusammengeführt: {title}");
        _app.NotifyKnowledgeBaseChanged();
        StartSemanticSync();

        var kept = chosen.Count - removed.Count;
        var message = $"Neue Sprachnotiz „{title}“ mit dem vollständigen Text von {chosen.Count} Einträgen angelegt."
            + (removeOriginals ? $"\n\n{removed.Count} Originale entfernt, {kept} bleiben erhalten." : "\n\nDie Originale bleiben erhalten.");
        if (missing.Count > 0)
            message += $"\n\nBei {missing.Count} Einträgen war der Text in der neuen Notiz nicht vollständig auffindbar; sie wurden nicht entfernt.";
        if (failures.Count > 0) message += "\n\nNicht entfernt: " + string.Join(", ", failures.Take(5));
        StatusLabel.Text = $"{chosen.Count} Einträge zusammengeführt" + (removed.Count > 0 ? $", {removed.Count} Originale entfernt." : ".");
        MessageBox.Show(this, message, AppNames.KnowledgeBase, MessageBoxButton.OK,
            missing.Count > 0 || failures.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    /// <summary>Original, Ideenkombinat copy and their backups of a note, like deleting it in the search.</summary>
    private static void DeleteNoteFiles(string key)
    {
        var id = Path.GetFileNameWithoutExtension(key[2..]);
        if (!NoteStore.TryParseId(id, out _)) throw new ArgumentException("Ungültige Sprachnotiz-ID.");
        foreach (var path in new[] { Path.Combine(AppPaths.NotesDir, id + ".md"), Path.Combine(AppPaths.KbNotesDir, id + ".md") }
                     .SelectMany(p => new[] { p, p + ".bak" }))
            if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>
    /// The complete original text of a node (not the shortened index chunks). Null when it cannot be read; such an
    /// entry is not offered, so nothing is merged from a partial text.
    /// </summary>
    private MergeSource? ReadMergeSource(GraphNode node, IReadOnlyDictionary<long, ClipboardEntry> entries)
    {
        var item = node.Item;
        var origin = KnowledgeMetadata.Describe(item.Metadata).Replace('\n', ' ');
        try
        {
            switch (item.Source)
            {
                case SemanticSource.Clipboard:
                    if (!entries.TryGetValue(item.EntryId, out var entry)) return null;
                    var text = entry.KnowledgeText();
                    if (text.Length == 0) return null;
                    var keep = entry.Type == EntryType.Image ? "Bild, das Bild selbst steht nicht in der Notiz"
                        : entry.HasHtml ? "formatierter Text, die Formatierung ginge verloren"
                        : entry.IsPinned ? "angeheftet" : "";
                    return new MergeSource(node.ItemKey, item.Source, item.Title, entry.Timestamp, text, origin, keep.Length == 0, keep);
                case SemanticSource.Note when item.FilePath != null:
                    var body = NoteStore.KbBody(RecordingMarkdown.ReadShared(item.FilePath)).Trim();
                    return body.Length == 0 ? null : new MergeSource(node.ItemKey, item.Source, item.Title, item.Date, body, origin, true);
                case SemanticSource.Recording when item.FilePath != null:
                    var hash = node.ItemKey.LastIndexOf('#');
                    if (hash < 0 || !int.TryParse(node.ItemKey.AsSpan(hash + 1), out var index)) return null;
                    var snapshots = RecordingMarkdown.SplitSnapshots(RecordingMarkdown.ReadShared(item.FilePath));
                    if (index >= snapshots.Count) return null;
                    return new MergeSource(node.ItemKey, item.Source, item.Title, item.Date, $"{item.Title}\n{snapshots[index].Body.Trim()}", origin,
                        false, "Bildschirmaufnahme, die Datei enthält weitere Bildwechsel");
                case SemanticSource.KnowledgeFolder:
                    var file = _app.KnowledgeFolderSnapshot.Files.FirstOrDefault(f => f.ItemKey == node.ItemKey);
                    return file == null ? null : new MergeSource(node.ItemKey, item.Source, item.Title, item.Date, file.Text, file.RelativePath,
                        false, "Datei im Knowledgeordner, das Original bleibt im Ordner");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            App.LogError("Eintrag zum Zusammenführen lesen", ex);
        }
        return null;
    }
}

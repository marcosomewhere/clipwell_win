using System.IO;
using System.Windows;
using System.Windows.Input;
using ClipwellWin.Models;
using ClipwellWin.Services;
using MessageBox = ClipwellWin.Views.AppMessageBox;

namespace ClipwellWin.Views;

/// <summary>Entry points for the categorization pass: Brain node or topic, all uncategorized entries,
/// marked search results and the library selection.</summary>
public partial class KnowledgeBaseWindow
{
    private bool _categorizeOpening;

    // ---- Entry points --------------------------------------------------------------------------------------

    private async void BrainCategorize_Click(object sender, RoutedEventArgs e)
    {
        if (_brain == null) return;
        var cluster = _brainCluster >= 0 && _brainCluster < _brain.Clusters.Count ? _brain.Clusters[_brainCluster] : null;
        if (cluster != null)
        {
            var keys = cluster.Members.Select(m => _brain.Nodes[m].ItemKey).ToList();
            await OpenCategorizeAsync($"Thema einordnen: {cluster.Label}",
                $"{keys.Count} Einträge des Themas. Alle sind markiert und werden zuerst gemeinsam eingeordnet; einzeln geht es danach weiter.",
                keys, keys.ToHashSet(), onlyUncategorized: false, groupFirst: true);
            return;
        }
        if (SelectedBrainNode is not { } node) return;
        // Strongest links first: the shared suggestion evaluates the first marked entries only.
        var neighbors = _brain.Neighbors(node.Index)
            .Select(x => (x.Node, Edges: x.Edges.Where(edge => BrainCanvasView.VisibleEdgeKinds.Contains(edge.Kind)).ToList()))
            .Where(x => x.Edges.Count > 0)
            .OrderByDescending(x => x.Edges.Any(edge => edge.Kind is GraphEdgeKind.Semantic or GraphEdgeKind.Summary))
            .ThenByDescending(x => x.Edges.Max(edge => edge.Weight))
            .ToList();
        var nodeKeys = new[] { node.ItemKey }.Concat(neighbors.Select(x => _brain.Nodes[x.Node].ItemKey)).ToList();
        var marked = new[] { node.ItemKey }.Concat(neighbors
            .Where(x => x.Edges.Any(edge => edge.Kind is GraphEdgeKind.Semantic or GraphEdgeKind.Summary))
            .Select(x => _brain.Nodes[x.Node].ItemKey)).ToHashSet();
        await OpenCategorizeAsync($"Eintrag und Verbindungen einordnen",
            $"„{node.Item.Title}“ mit {neighbors.Count} sichtbaren Verbindungen. Markiert sind der Eintrag und seine inhaltlich verwandten Nachbarn; sie werden zuerst gemeinsam eingeordnet.",
            nodeKeys, marked, onlyUncategorized: false, groupFirst: true);
    }

    private async void CategorizeUncategorized_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureIndexAsync(null)) return;
        var annotations = _app.CurrentSettings.BrainAnnotations ?? [];
        var keys = _app.Semantic.Snapshot().Select(c => c.Chunk)
            .GroupBy(c => c.ItemKey).Select(g => g.First())
            .Where(c => KnowledgeCategories.Effective(annotations, c.ItemKey).IsEmpty)
            .OrderByDescending(c => c.Date).Select(c => c.ItemKey).ToList();
        if (keys.Count == 0)
        {
            MessageBox.Show(this, "Alle indizierten Einträge haben bereits eine Kategorie.", AppNames.KnowledgeBase);
            return;
        }
        await OpenCategorizeAsync("Alle Einträge ohne Kategorie", $"{keys.Count} Einträge ohne Kategorie, neueste zuerst. "
            + "Jeden Vorschlag prüfen, dann Enter zum Zuordnen oder Überspringen.", keys, null, onlyUncategorized: true, groupFirst: false);
    }

    /// <summary>Reverse direction: uncategorized entries closest to the category chosen in the library filter (Rocchio).</summary>
    private async void FindCategoryMatches_Click(object sender, RoutedEventArgs e)
    {
        if (_categoryFilter is FilterAll or FilterNone)
        {
            MessageBox.Show(this, "Bitte zuerst im Kategorienfilter der Bibliothek eine Haupt- oder Unterkategorie wählen. "
                + "Danach werden die Einträge ohne Kategorie gesucht, die am besten dazu passen.", AppNames.KnowledgeBase);
            return;
        }
        if (_categorizeOpening || !await EnsureIndexAsync(null)) return;
        var target = KnowledgeCategories.Parse(_categoryFilter);
        var snapshot = _app.Semantic.Snapshot();
        // A copy: the dictionary may change on the UI thread while the comparison runs in the background.
        var annotations = new Dictionary<string, BrainAnnotation>(_app.CurrentSettings.BrainAnnotations ?? [], StringComparer.Ordinal);
        var result = await Task.Run(() => CategoryMatching.Rank(snapshot, annotations, target));
        if (result.Matches.Count == 0)
        {
            MessageBox.Show(this, result.Message, AppNames.KnowledgeBase);
            return;
        }
        var keys = result.Matches.Select(m => m.Key).ToList();
        await OpenCategorizeAsync($"Passend zu {target}",
            $"{keys.Count} Einträge ohne Kategorie, die näher an „{target}“ liegen als an den {result.Competitors - 1} anderen Kategorien "
            + $"(Vergleich mit {result.Examples} eigenen Beispielen, deutlichste zuerst). Passende markieren und gemeinsam zuordnen.",
            keys, null, onlyUncategorized: true, groupFirst: false, presetGroupCategory: target.ToString());
    }

    private async void SemanticCategorizeMarked_Click(object sender, RoutedEventArgs e)
    {
        var keys = MarkedSemanticChunks().Select(c => c.ItemKey).Distinct().ToList();
        if (keys.Count == 0) return;
        await OpenCategorizeAsync("Markierte Suchtreffer einordnen", $"{keys.Count} markierte Einträge aus der Suche.",
            keys, keys.Count > 1 ? keys.ToHashSet() : null, onlyUncategorized: false, groupFirst: false);
    }

    private async void CategorizeSelection_Click(object sender, RoutedEventArgs e)
    {
        if (_view == KbView.Semantic && MarkedSemanticChunks().Any()) { SemanticCategorizeMarked_Click(sender, e); return; }
        if (_view == KbView.Brain && BrainManageButton.IsEnabled) { BrainCategorize_Click(sender, e); return; }
        var selected = SelectedItems().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Bitte links Einträge auswählen (Strg oder Umschalt für mehrere). "
                + "Für alle Einträge ohne Kategorie: Mehr → Alle ohne Kategorie einordnen.", AppNames.KnowledgeBase);
            return;
        }
        var direct = selected.Where(i => i.ArticleKind != KbArticleKind.Recording).Select(LibraryKey).ToList();
        var recordings = selected.Where(i => i.ArticleKind == KbArticleKind.Recording)
            .Select(i => "r:" + Path.GetFileName(i.FilePath) + "#").ToList();
        if (!await EnsureIndexAsync(recordings.Count > 0 ? null : direct)) return;
        var keys = direct.Concat(_app.Semantic.Snapshot().Select(c => c.Chunk.ItemKey).Distinct()
            .Where(k => recordings.Any(prefix => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))).ToList();
        var scope = $"{SelectionBreakdown(selected)} aus der Bibliothek."
            + (recordings.Count > 0 ? " Bildschirmaufnahmen erscheinen je Bildwechsel." : "");
        await OpenCategorizeAsync("Auswahl einordnen", scope, keys, keys.Count > 1 ? keys.ToHashSet() : null,
            onlyUncategorized: false, groupFirst: false);
    }

    private async void CategorizationReport_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureIndexAsync(null)) return;
        _app.OpenCategorizationReport(this);
    }

    /// <summary>Granite analysis: groups the newest uncategorized entries and lets Granite propose category names.</summary>
    private async void CategoryDiscovery_Click(object sender, RoutedEventArgs e)
    {
        if (_categorizeOpening || !await EnsureIndexAsync(null)) return;
        await RunDiscoveryAsync(null, this);
    }

    /// <summary>
    /// Runs the Granite analysis over the newest uncategorized entries, or over <paramref name="onlyKeys"/> (any
    /// category, for re-categorizing). Returns item key to category for everything the user saved.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> RunDiscoveryAsync(IReadOnlyCollection<string>? onlyKeys, Window owner)
    {
        if (onlyKeys != null && !await EnsureIndexAsync(onlyKeys)) return new Dictionary<string, string>();
        var settings = _app.CurrentSettings;
        settings.BrainAnnotations ??= [];
        var annotations = settings.BrainAnnotations;
        var only = onlyKeys?.ToHashSet(StringComparer.Ordinal);
        var candidates = _app.Semantic.Snapshot().GroupBy(c => c.Chunk.ItemKey)
            .Where(g => only != null ? only.Contains(g.Key) : KnowledgeCategories.Effective(annotations, g.Key).IsEmpty)
            .Select(g => (Key: g.Key, Chunk: g.First().Chunk, Date: g.Max(c => c.Chunk.Date), Vector: RocchioModel.Mean(g.Select(c => c.Vector).ToArray())))
            .OrderByDescending(x => x.Date).Take(CategoryDiscovery.MaxItems).ToList();
        var texts = _app.Semantic.ItemTexts(candidates.Select(c => c.Key).ToList());
        var items = candidates.Where(c => texts.ContainsKey(c.Key))
            .Select(c => new DiscoveryItem(c.Key, c.Chunk.Title, texts[c.Key].Text, c.Date, c.Vector)).ToList();
        var existing = KnowledgeCategories.Mains(settings);
        var categorized = items.Where(i => !KnowledgeCategories.Effective(annotations, i.Key).IsEmpty).Select(i => i.Key).ToHashSet();
        var window = new CategoryDiscoveryWindow(_app.LocalAi, items, existing, PersistCategory, _app.RecordCategorizationFeedback,
            scope: only != null ? $"{items.Count} markierte Einträge" + (categorized.Count > 0 ? $", davon {categorized.Count} schon mit Kategorie" : "") : null,
            alreadyCategorized: categorized, assignments: KnowledgeCategories.Assignments(annotations)) { Owner = owner };
        window.ShowDialog();
        if (!window.Changed) return window.Applied;
        try { _app.SaveSettings(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.LogCrash(ex); }
        // Inside the categorization window the Brain is rebuilt when that window closes.
        if (only == null && _brain != null) RebuildBrain(keepLayout: true);
        return window.Applied;
    }

    /// <summary>Opens the categorization pass for given item keys, e.g. assignments flagged in the evaluation.</summary>
    internal Task CategorizeKeysAsync(IReadOnlyList<string> keys)
        => OpenCategorizeAsync("Zuordnungen prüfen", $"{keys.Count} gespeicherte Zuordnungen, die die Auswertung als auffällig markiert hat. Kategorie prüfen und bei Bedarf ändern.",
            keys, null, onlyUncategorized: false, groupFirst: false);

    private static string LibraryKey(KbItem item) => item.ArticleKind == KbArticleKind.Clipboard && item.Entry != null
        ? $"c:{item.Entry.Id}" : "n:" + Path.GetFileName(item.FilePath);

    // ---- Shared --------------------------------------------------------------------------------------------

    /// <summary>Synchronizes the search index when it is empty or lacks one of <paramref name="keys"/>.</summary>
    private async Task<bool> EnsureIndexAsync(IReadOnlyCollection<string>? keys)
    {
        var snapshot = _app.Semantic.Snapshot();
        var present = snapshot.Select(c => c.Chunk.ItemKey).ToHashSet();
        if (snapshot.Count > 0 && (keys == null || keys.All(present.Contains))) return true;
        _semanticModelInstalled = EmbeddingModel.IsInstalled(EmbeddingModel.ResolveModelsDir(AppContext.BaseDirectory, AppPaths.ModelsDir));
        if (!_semanticModelInstalled)
        {
            MessageBox.Show(this, "Zum Einordnen wird das Modell der semantischen Suche benötigt. Bitte unter „Suche“ das Modell laden.", AppNames.KnowledgeBase);
            return false;
        }
        Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
        try
        {
            StartSemanticSync();
            if (_semanticSyncTask is { } sync) await sync;
        }
        finally { Mouse.OverrideCursor = null; }
        return !_semanticClosed;
    }

    private async Task OpenCategorizeAsync(string heading, string scope, IReadOnlyList<string> keys, ISet<string>? marked,
        bool onlyUncategorized, bool groupFirst, string? presetGroupCategory = null)
    {
        if (_categorizeOpening) return;
        _categorizeOpening = true;
        try
        {
            if (!await EnsureIndexAsync(keys)) return;
            var wanted = keys.Distinct().ToList();
            var wantedSet = wanted.ToHashSet();
            var first = _app.Semantic.Snapshot().Where(c => wantedSet.Contains(c.Chunk.ItemKey))
                .GroupBy(c => c.Chunk.ItemKey).ToDictionary(g => g.Key, g => g.First().Chunk);
            var texts = _app.Semantic.ItemTexts(wanted);
            var items = wanted.Where(k => first.ContainsKey(k) && texts.ContainsKey(k)).Select(k =>
            {
                var c = first[k];
                return new CategorizationItem
                {
                    Source = new BrainSource(k, c.Title, texts[k].Text, c.FilePath ?? $"Zwischenablage #{c.EntryId}", c.Date),
                    SourceName = SemanticChunk.SourceName(c.Source),
                    Marked = marked?.Contains(k) == true,
                };
            }).ToList();
            var missing = wanted.Count - items.Count;
            if (items.Count == 0)
            {
                MessageBox.Show(this, "Die gewählten Einträge sind nicht im Suchindex, zum Beispiel weil sie zu kurz sind oder nur aus einem einzelnen Wort bestehen.", AppNames.KnowledgeBase);
                return;
            }
            if (missing > 0) scope += $" {missing} Einträge sind nicht im Suchindex (zu kurz oder ein einzelnes Wort) und fehlen hier.";
            var settings = _app.CurrentSettings;
            settings.BrainAnnotations ??= [];
            var queue = new CategorizationQueue(items, KnowledgeCategories.Assignments(settings.BrainAnnotations),
                KnowledgeCategories.Paths(settings));
            var feedbackCount = settings.CategorizationFeedback?.Count ?? 0;
            var dialog = new CategorizeDialog(heading, scope, queue, _app.LocalAi, PersistCategory, onlyUncategorized, groupFirst,
                _app.RecordCategorizationFeedback, analyze: RunDiscoveryAsync, presetGroupCategory: presetGroupCategory) { Owner = this };
            dialog.ShowDialog();
            if ((settings.CategorizationFeedback?.Count ?? 0) != feedbackCount)
            {
                try { _app.SaveSettings(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.LogCrash(ex); }
            }
            if (dialog.Changed && _brain != null) RebuildBrain(keepLayout: true);
        }
        finally { _categorizeOpening = false; }
    }

    /// <summary>Saves a category path ("Haupt › Unter" or "Haupt") for the keys; restores the old state when saving fails.</summary>
    private void PersistCategory(IReadOnlyList<string> keys, string category)
    {
        var settings = _app.CurrentSettings;
        settings.BrainAnnotations ??= [];
        settings.KnowledgeCategories ??= [];
        var previous = keys.Distinct().ToDictionary(k => k, k => settings.BrainAnnotations.GetValueOrDefault(k));
        KnowledgeCategories.Assign(settings, keys, KnowledgeCategories.Parse(category));
        try { _app.SaveSettings(); }
        catch
        {
            foreach (var (key, old) in previous)
                if (old == null) settings.BrainAnnotations.Remove(key);
                else settings.BrainAnnotations[key] = old;
            throw;
        }
        // The categorization window rebuilds the Brain once when it closes.
        CategoriesChanged(rebuildBrain: false);
    }
}

using System.IO;
using System.Windows.Forms;
using Merkautomat.Services;

namespace Merkautomat;

/// <summary>
/// Background step after "Mein Wissen": the language model proposes finer sub- and fine categories for well filled
/// categories. One category per job; proposals wait in category-refinement.json until the user confirms them.
/// </summary>
public partial class App
{
    private static string RefinementStateFile => Path.Combine(AppPaths.DataDir, CategoryRefinement.FileName);
    private ToolStripMenuItem? _refinementReadyMenuItem;

    /// <summary>Groups with a finer category proposal that the user has not opened yet.</summary>
    internal int PreparedRefinementCount { get; private set; }

    /// <summary>Returns true when more categories wait for the next job.</summary>
    private async Task<bool> RunCategoryRefinementStepAsync(CancellationToken ct)
    {
        if (!LocalAi.Model.IsInstalled || Semantic.Count == 0) return false;
        if (_backgroundStopAfterStep || _backgroundDecision?.Allowed != true) return true;
        var settings = CurrentSettings;
        var annotations = new Dictionary<string, Models.BrainAnnotation>(settings.BrainAnnotations ?? [], StringComparer.Ordinal);
        var snapshot = Semantic.Snapshot();
        var parents = CategoryRefinement.Parents(snapshot.Select(c => c.Chunk.ItemKey), annotations);
        var state = CategoryRefinementState.Load(RefinementStateFile);
        if (state.Next(parents) is not { } next) return false;

        SetActivity($"Feinere Kategorien für „{next.Parent}“ ausdenken");
        var only = next.Keys.ToHashSet(StringComparer.Ordinal);
        var items = await Task.Run(() => CategoryDiscovery.Candidates(snapshot, annotations, Semantic.ItemTexts, only), ct);
        var groups = await Task.Run(() => CategoryDiscovery.Group(items.Select(i => i.Vector).ToArray()), ct);
        var existing = CategoryRefinement.Children(settings, next.Parent);
        var proposals = new List<StoredProposal>();
        // A single group means the entries belong together; there is nothing to split.
        if (groups.Count >= 2)
            foreach (var group in groups)
            {
                if (_backgroundStopAfterStep || _backgroundDecision?.Allowed != true) return true;
                var members = group.Select(i => items[i]).ToList();
                var sources = members.Take(CategoryDiscovery.Representatives).Select(m => new BrainSource(m.Key, m.Title, m.Text, "", m.Date)).ToList();
                RefinementSuggestion suggestion;
                try { suggestion = await LocalAi.RefineAsync(next.Parent, existing, sources, background: true, ct); }
                catch (Exception ex) when (ex is ArgumentException or InvalidDataException) { continue; }
                if (CategoryRefinement.ToProposal(next.Parent, members, suggestion, LocalAi.Model.Method) is { } proposal)
                {
                    proposals.Add(proposal);
                    if (!existing.Contains(suggestion.Name, StringComparer.OrdinalIgnoreCase)) existing.Add(suggestion.Name);
                }
            }
        state.Record(next.Parent, next.Keys, proposals);
        try { state.Save(RefinementStateFile); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            LogError("Feinere Kategorien speichern", ex);
            return false;
        }
        UpdatePreparedRefinement(state);
        if (proposals.Count > 0 && _trayIcon != null)
        {
            _balloonAction = OpenPreparedRefinement;
            _trayIcon.ShowBalloonTip(8000, "Feinere Kategorien vorgeschlagen",
                $"Für „{next.Parent}“ gibt es {proposals.Count} Vorschläge. Klicken zum Prüfen; gespeichert wird erst nach deiner Bestätigung.", ToolTipIcon.Info);
        }
        return state.Next(parents) != null;
    }

    private void StartCategoryRefinement()
    {
        if (_trayIcon?.ContextMenuStrip is { } menu)
        {
            _refinementReadyMenuItem = new ToolStripMenuItem("Feinere Kategorien prüfen") { Visible = false };
            _refinementReadyMenuItem.Click += (_, _) => OpenPreparedRefinement();
            var index = _discoveryReadyMenuItem != null ? menu.Items.IndexOf(_discoveryReadyMenuItem) + 1 : 0;
            menu.Items.Insert(index, _refinementReadyMenuItem);
        }
        RefreshPreparedRefinement();
    }

    /// <summary>Recounts the waiting proposals, e.g. after categories were assigned elsewhere.</summary>
    internal void RefreshPreparedRefinement()
    {
        try { UpdatePreparedRefinement(CategoryRefinementState.Load(RefinementStateFile)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            LogError("Feinere Kategorien laden", ex);
        }
    }

    private void UpdatePreparedRefinement(CategoryRefinementState state)
    {
        var count = state.Seen ? 0 : state.Ready(CurrentSettings.BrainAnnotations, KnowledgeCategories.Paths(CurrentSettings)).Count;
        if (_refinementReadyMenuItem != null)
        {
            _refinementReadyMenuItem.Visible = count > 0;
            _refinementReadyMenuItem.Text = $"Feinere Kategorien prüfen ({count})";
        }
        if (count == PreparedRefinementCount) return;
        PreparedRefinementCount = count;
        PreparedDiscoveryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OpenPreparedRefinement() => OpenPreparedRefinementAsync().Forget("Feinere Kategorien öffnen");

    private async Task OpenPreparedRefinementAsync()
    {
        OpenKnowledgeBase();
        if (_knowledgeBase != null) await _knowledgeBase.ShowCategoryRefinementAsync();
    }

    /// <summary>
    /// Proposals still valid for review (members not yet refined) with their proposed descriptions; marks them as seen.
    /// Empty when nothing is prepared.
    /// </summary>
    internal (List<CategoryProposal> Proposals, DateTime? Finished, Dictionary<string, string> Descriptions) TakePreparedRefinement()
    {
        CategoryRefinementState state;
        try { state = CategoryRefinementState.Load(RefinementStateFile); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            LogError("Feinere Kategorien laden", ex);
            return ([], null, []);
        }
        var ready = state.Ready(CurrentSettings.BrainAnnotations, KnowledgeCategories.Paths(CurrentSettings));
        if (!state.Seen)
        {
            state.Seen = true;
            try { state.Save(RefinementStateFile); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
            {
                LogError("Feinere Kategorien speichern", ex);
            }
            UpdatePreparedRefinement(state);
        }
        return (ready, state.Finished, state.Descriptions());
    }
}

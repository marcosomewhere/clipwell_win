using System.IO;
using System.Windows.Threading;
using ClipwellWin.Services;

namespace ClipwellWin;

/// <summary>
/// Background work with Granite: prepares the category analysis while the PC is idle (or throttled during work),
/// pauses on battery, in games and presentations and whenever the local AI is used directly. Results are proposals
/// only; nothing is saved before the user applies them.
/// </summary>
public partial class App
{
    private static readonly TimeSpan BackgroundTick = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BackgroundRecheck = TimeSpan.FromMinutes(30);
    private DispatcherTimer? _backgroundTimer;
    private CancellationTokenSource? _backgroundJob;
    private volatile BackgroundDecision? _backgroundDecision;
    private volatile bool _backgroundStopAfterStep;
    private DateTime _backgroundNextCheck = DateTime.MinValue;
    private string _backgroundActivity = "";
    private bool _backgroundMissing;
    private bool _keepingAwake;
    private ToolStripMenuItem? _discoveryReadyMenuItem;
    private Action? _balloonAction;

    /// <summary>One line for the Kontrollstation: what the background work does and why.</summary>
    internal string BackgroundStatus { get; private set; } = "";
    internal event EventHandler? BackgroundStatusChanged;
    /// <summary>Raised when prepared category proposals appear or have been opened.</summary>
    internal event EventHandler? PreparedDiscoveryChanged;
    /// <summary>Named groups of a finished background analysis that the user has not opened yet.</summary>
    internal int PreparedDiscoveryCount { get; private set; }

    private static string DiscoveryStateFile => Path.Combine(AppPaths.DataDir, BackgroundDiscoveryState.FileName);

    private void StartBackgroundWork()
    {
        if (_trayIcon?.ContextMenuStrip is { } menu)
        {
            _discoveryReadyMenuItem = new ToolStripMenuItem("Kategorievorschläge prüfen") { Visible = false };
            _discoveryReadyMenuItem.Click += (_, _) => OpenPreparedDiscovery();
            var kb = menu.Items.Cast<ToolStripItem>().FirstOrDefault(i => i.Text == AppNames.KnowledgeBase);
            menu.Items.Insert(kb != null ? menu.Items.IndexOf(kb) + 1 : 0, _discoveryReadyMenuItem);
        }
        if (_trayIcon != null)
        {
            _trayIcon.BalloonTipClicked += (_, _) => { var action = _balloonAction; _balloonAction = null; action?.Invoke(); };
            _trayIcon.BalloonTipClosed += (_, _) => _balloonAction = null;
        }
        UpdatePreparedDiscovery(BackgroundDiscoveryState.Load(DiscoveryStateFile));
        _backgroundTimer = new DispatcherTimer { Interval = BackgroundTick };
        _backgroundTimer.Tick += (_, _) => OnBackgroundTick();
        _backgroundTimer.Start();
    }

    private void StopBackgroundWork()
    {
        _backgroundTimer?.Stop();
        _backgroundJob?.Cancel();
        SetKeepAwake(false);
    }

    /// <summary>Re-evaluates at once, e.g. after the mode was changed in the Kontrollstation.</summary>
    internal void BackgroundSettingsChanged()
    {
        _backgroundNextCheck = DateTime.MinValue;
        OnBackgroundTick();
    }

    private void OnBackgroundTick()
    {
        var decision = BackgroundWork.Decide(CurrentSettings.BackgroundWorkMode, BackgroundWork.Probe(LocalAi.LastInteractiveUse));
        _backgroundDecision = decision;
        if (_backgroundJob != null)
        {
            if (!decision.Allowed && decision.Hard) _backgroundJob.Cancel();
            // Without a hard reason the running step may finish, throttled; the next one waits.
            _backgroundStopAfterStep = !decision.Allowed;
            LocalAi.SetBackgroundLoad(decision.State == BackgroundState.Fast ? AiLoad.Normal : AiLoad.Eco);
        }
        else if (!CanPrepareDiscovery(out var missing)) { _backgroundActivity = missing; _backgroundMissing = true; }
        else
        {
            if (_backgroundMissing) { _backgroundActivity = ""; _backgroundMissing = false; }
            if (decision.Allowed && DateTime.Now >= _backgroundNextCheck) _ = RunBackgroundDiscoveryAsync();
        }
        SetKeepAwake(_backgroundJob != null && CurrentSettings.BackgroundKeepAwake);
        UpdateBackgroundStatus();
    }

    private bool CanPrepareDiscovery(out string missing)
    {
        missing = "";
        if (!LocalAi.Model.IsInstalled) missing = "Granite ist nicht installiert.";
        else if (!EmbeddingModel.IsInstalled(EmbeddingModel.ResolveModelsDir(AppContext.BaseDirectory, AppPaths.ModelsDir)))
            missing = "Das Modell der semantischen Suche fehlt.";
        return missing.Length == 0;
    }

    private void UpdateBackgroundStatus()
    {
        var mode = BackgroundWork.Normalize(CurrentSettings.BackgroundWorkMode);
        var reason = _backgroundDecision?.Reason ?? "";
        var status = mode == BackgroundWork.Off ? "Hintergrundarbeit ist ausgeschaltet."
            : _backgroundJob != null ? $"{_backgroundActivity} · {reason}"
            : _backgroundActivity.Length > 0 ? $"{_backgroundActivity} · {reason}" : $"Nichts zu tun · {reason}";
        if (status == BackgroundStatus) return;
        BackgroundStatus = status;
        BackgroundStatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetActivity(string text)
    {
        _backgroundActivity = text;
        UpdateBackgroundStatus();
    }

    private void SetKeepAwake(bool awake)
    {
        if (awake == _keepingAwake) return;
        _keepingAwake = awake;
        BackgroundWork.KeepAwake(awake);
    }

    private async Task RunBackgroundDiscoveryAsync()
    {
        using var job = _backgroundJob = new CancellationTokenSource();
        var ct = job.Token;
        _backgroundStopAfterStep = false;
        _backgroundNextCheck = DateTime.Now + BackgroundRecheck;
        var file = DiscoveryStateFile;
        try
        {
            // Embedding new entries runs inside Merkautomat and cannot be throttled, so only while the PC is idle.
            if (_backgroundDecision?.State == BackgroundState.Fast || Semantic.Count == 0)
            {
                if (_backgroundDecision?.State != BackgroundState.Fast)
                {
                    SetActivity("Suchindex wird erst im Leerlauf aufgebaut");
                    _backgroundNextCheck = DateTime.Now;
                    return;
                }
                SetActivity("Suchindex wird aktualisiert");
                var entries = SnapshotEntries();
                var recordings = Recorder.Directory;
                var chunks = await Task.Run(() => Semantic.Collect(entries, AppPaths.KbNotesDir, recordings, AppPaths.NotesDir), ct);
                await Semantic.SyncAsync(chunks, ct);
            }
            var settings = CurrentSettings;
            var annotations = new Dictionary<string, Models.BrainAnnotation>(settings.BrainAnnotations ?? [], StringComparer.Ordinal);
            var existing = KnowledgeCategories.Mains(settings);
            var assignments = KnowledgeCategories.Assignments(annotations);
            var items = await Task.Run(() => CategoryDiscovery.Candidates(Semantic.Snapshot(), annotations, Semantic.ItemTexts), ct);
            var state = BackgroundDiscoveryState.Load(file);
            if (state?.CanResume(items) != true)
            {
                var due = state is { Complete: true }
                    ? state.NewItems(items.Select(i => i.Key)) >= BackgroundDiscoveryState.NewItemsForRerun
                    : items.Count >= BackgroundDiscoveryState.NewItemsForRerun;
                if (!due)
                {
                    SetActivity(state is { Complete: true } ? "Kategorieanalyse ist aktuell" : "Zu wenige Einträge ohne Kategorie für eine Analyse");
                    return;
                }
                state = new BackgroundDiscoveryState { Keys = items.Select(i => i.Key).ToList(), Started = DateTime.Now };
                state.Save(file);
                UpdatePreparedDiscovery(state);
            }

            var current = state!;
            var progress = new Progress<DiscoveryProgress>(p =>
            {
                if (p.Finished == null)
                {
                    SetActivity($"Kategorien analysieren: Gruppe {p.Done + 1} von {p.Total}");
                    return;
                }
                if (p.Resumed) return;
                current.Groups.Add(StoredProposal.From(p.Finished));
                try { current.Save(file); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LogCrash(ex); }
            });
            async Task<LocalAiSuggestion> Name(IReadOnlyList<BrainSource> sources, IReadOnlyList<string> categories, CancellationToken token)
            {
                if (_backgroundStopAfterStep || _backgroundDecision?.Allowed != true) throw new OperationCanceledException(token);
                return await LocalAi.SuggestWithGraniteAsync(sources, categories, token, assignments, e5First: true, background: true);
            }
            SetActivity("Kategorien analysieren: Gruppen werden gebildet");
            await CategoryDiscovery.RunAsync(items, existing, Name, progress, ct, current.Proposals(existing));
            current.Complete = true;
            current.Finished = DateTime.Now;
            current.Seen = false;
            current.Save(file);
            UpdatePreparedDiscovery(current);
            SetActivity(PreparedDiscoveryCount > 0 ? $"Kategorieanalyse fertig: {PreparedDiscoveryCount} Vorschläge bereit" : "Kategorieanalyse fertig, keine benannten Gruppen");
            if (PreparedDiscoveryCount > 0 && _trayIcon != null)
            {
                _balloonAction = OpenPreparedDiscovery;
                _trayIcon.ShowBalloonTip(8000, "Kategorievorschläge bereit",
                    $"{PreparedDiscoveryCount} Gruppen im Hintergrund benannt. Klicken zum Prüfen; gespeichert wird erst nach deiner Bestätigung.", ToolTipIcon.Info);
            }
        }
        catch (OperationCanceledException)
        {
            SetActivity("Kategorieanalyse pausiert, fertige Gruppen sind gesichert");
            // Continue as soon as the conditions allow it again.
            _backgroundNextCheck = DateTime.Now;
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            SetActivity("Kategorieanalyse fehlgeschlagen: " + ex.Message);
        }
        finally
        {
            _backgroundJob = null;
            _backgroundStopAfterStep = false;
            SetKeepAwake(false);
            UpdateBackgroundStatus();
        }
    }

    private void UpdatePreparedDiscovery(BackgroundDiscoveryState? state)
    {
        var count = state is { Complete: true, Seen: false } ? state.Groups.Count(g => !g.Uncertain) : 0;
        if (_discoveryReadyMenuItem != null)
        {
            _discoveryReadyMenuItem.Visible = count > 0;
            _discoveryReadyMenuItem.Text = $"Kategorievorschläge prüfen ({count})";
        }
        if (count == PreparedDiscoveryCount) return;
        PreparedDiscoveryCount = count;
        PreparedDiscoveryChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void OpenPreparedDiscovery()
    {
        OpenKnowledgeBase();
        if (_knowledgeBase != null) await _knowledgeBase.ShowCategoryDiscoveryAsync();
    }

    /// <summary>
    /// Proposals of the last finished background analysis for the review window, limited to <paramref name="items"/>
    /// (still indexed and without a category); marks them as seen. Null when there is nothing prepared.
    /// </summary>
    internal (List<CategoryProposal> Proposals, DateTime? Finished)? TakePreparedDiscovery(IReadOnlyList<DiscoveryItem> items, IReadOnlyList<string> existing)
    {
        var state = BackgroundDiscoveryState.Load(DiscoveryStateFile);
        if (state is not { Complete: true } || state.Groups.Count == 0) return null;
        var ready = state.Ready(items.Select(i => i.Key).ToHashSet(StringComparer.Ordinal), existing);
        if (!state.Seen)
        {
            state.Seen = true;
            try { state.Save(DiscoveryStateFile); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LogCrash(ex); }
            UpdatePreparedDiscovery(state);
        }
        return ready.Count == 0 ? null : (ready, state.Finished);
    }
}

using System.IO;
using System.Windows.Threading;
using Merkautomat.Models;
using Merkautomat.Services;

namespace Merkautomat;

/// <summary>
/// "Mein Wissen": loads the stored collection, runs the weighted intake once a day (and on request) in the
/// background and removes copies whose source the user deleted in Merkautomat.
/// </summary>
public partial class App
{
    private static string MyKnowledgeFile => Path.Combine(AppPaths.DataDir, MyKnowledge.FileName);
    private readonly object _myKnowledgeGate = new();
    private MyKnowledgeState _myKnowledge = new();
    /// <summary>An unreadable file is never overwritten; the collection stays read-only until the next start.</summary>
    private bool _myKnowledgeBroken;
    private DispatcherTimer? _myKnowledgeTimer;
    private Task? _myKnowledgeRun;
    private readonly CancellationTokenSource _myKnowledgeCts = new();
    private readonly List<long> _deletedClips = [];

    /// <summary>Raised on the UI thread after a run or a change by the user.</summary>
    internal event EventHandler? MyKnowledgeChanged;

    internal string MyKnowledgeStatus { get; private set; } = "Noch nicht abgeglichen.";
    private bool _myKnowledgeBusy;
    internal bool MyKnowledgeRunning => _myKnowledgeBusy;

    internal DateTime? MyKnowledgeLastRun
    {
        get { lock (_myKnowledgeGate) return _myKnowledge.LastRun; }
    }

    internal int MyKnowledgeCount
    {
        get { lock (_myKnowledgeGate) return _myKnowledge.Entries.Count; }
    }

    /// <summary>Weight per item key, without copying texts; for the Brain filter.</summary>
    internal Dictionary<string, double> MyKnowledgeWeights()
    {
        lock (_myKnowledgeGate)
        {
            var weights = new Dictionary<string, double>(_myKnowledge.Entries.Count, StringComparer.Ordinal);
            foreach (var e in _myKnowledge.Entries) weights[e.Key] = e.Weight;
            return weights;
        }
    }

    /// <summary>Copies of the entries, safe to read while a run is going on.</summary>
    internal List<MyKnowledgeEntry> MyKnowledgeSnapshot()
    {
        lock (_myKnowledgeGate) return _myKnowledge.Entries.Select(e => e.Clone()).ToList();
    }


    internal MyKnowledgeContext MyKnowledgeContext()
    {
        var s = CurrentSettings;
        return new MyKnowledgeContext(new Dictionary<string, BrainAnnotation>(s.BrainAnnotations ?? []),
            (s.BrainCollections ?? []).SelectMany(c => c.Keys).ToHashSet(StringComparer.Ordinal),
            Math.Clamp(s.MyKnowledgeRetentionDays, 0, 3650), Math.Clamp(s.MyKnowledgeClipboardHalfLifeDays, 1, 365), s.MyKnowledgeCondense,
            Math.Clamp(s.MyKnowledgeMaturityHours, 0, AppSettings.MyKnowledgeMaturityMax));
    }

    private void StartMyKnowledge()
    {
        try { _myKnowledge = MyKnowledge.Load(MyKnowledgeFile); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException
            or System.Security.Cryptography.CryptographicException)
        {
            LogError("Mein Wissen laden", ex);
            _myKnowledgeBroken = true;
            MyKnowledgeStatus = "Gespeichertes Wissen konnte nicht gelesen werden; es wird nicht überschrieben.";
        }
        if (_myKnowledge.LastRun is { } last) MyKnowledgeStatus = $"Stand {last:dd.MM.yyyy HH:mm}.";
        _popupVm!.EntryDeleted += id =>
        {
            // A bulk delete raises one event per entry; the copies go together in one save.
            if (_deletedClips.Count == 0) Dispatcher.BeginInvoke(() =>
            {
                var keys = _deletedClips.Select(i => $"c:{i}").ToList();
                _deletedClips.Clear();
                ForgetMyKnowledge(keys);
            });
            _deletedClips.Add(id);
        };
        // The first check waits a little so the start stays quick; afterwards the timer catches the change of day.
        _myKnowledgeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        _myKnowledgeTimer.Tick += (_, _) =>
        {
            _myKnowledgeTimer.Interval = TimeSpan.FromMinutes(20);
            if (MyKnowledgeLastRun?.Date != DateTime.Today) RunMyKnowledge();
        };
        _myKnowledgeTimer.Start();
    }

    private void StopMyKnowledge()
    {
        _myKnowledgeTimer?.Stop();
        _myKnowledgeCts.Cancel();
    }

    /// <summary>Starts a run unless one is going on or the collection is switched off.</summary>
    internal void RunMyKnowledge()
    {
        if (!CurrentSettings.MyKnowledgeEnabled || _myKnowledgeBroken || MyKnowledgeRunning || _myKnowledgeCts.IsCancellationRequested) return;
        // Set before the first notification, so listeners already see the run as going on.
        _myKnowledgeBusy = true;
        _myKnowledgeRun = RunMyKnowledgeAsync();
        _myKnowledgeRun.Forget("Mein Wissen aktualisieren");
    }

    private async Task RunMyKnowledgeAsync()
    {
        try { await RunMyKnowledgeCoreAsync(); }
        finally
        {
            _myKnowledgeBusy = false;
            MyKnowledgeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task RunMyKnowledgeCoreAsync()
    {
        MyKnowledgeStatus = "Wird aktualisiert …";
        MyKnowledgeChanged?.Invoke(this, EventArgs.Empty);
        var entries = SnapshotEntries();
        var context = MyKnowledgeContext();
        var recordings = Recorder.Directory;
        var ct = _myKnowledgeCts.Token;
        try
        {
            var run = await Task.Run(() =>
            {
                var chunks = Semantic.Collect(entries, AppPaths.KbNotesDir, recordings, AppPaths.NotesDir);
                ct.ThrowIfCancellationRequested();
                var candidates = MyKnowledge.Candidates(chunks, entries.DistinctBy(e => e.Id).ToDictionary(e => e.Id));
                HashSet<string> core, targets;
                lock (_myKnowledgeGate)
                {
                    core = _myKnowledge.Entries.Where(e => !e.Digest && (e.Kept || e.Source == SemanticSource.KnowledgeFolder))
                        .Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
                    targets = _myKnowledge.Entries.Where(e => !e.Digest).Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
                }
                core.UnionWith(candidates.Where(c => c.Source == SemanticSource.KnowledgeFolder).Select(c => c.Key));
                targets.UnionWith(candidates.Select(c => c.Key));
                // Needs the search index; without it (model missing, nothing indexed yet) there is simply no boost.
                var affinity = MyKnowledgeAi.Affinity(Semantic.Snapshot(), core, targets);
                ct.ThrowIfCancellationRequested();
                lock (_myKnowledgeGate)
                {
                    var result = MyKnowledge.Update(_myKnowledge, candidates, context with { Affinity = affinity }, DateTime.Now);
                    MyKnowledge.Save(MyKnowledgeFile, _myKnowledge);
                    return result;
                }
            }, ct);
            MyKnowledgeStatus = $"Stand {DateTime.Now:dd.MM.yyyy HH:mm}: {run.Total:N0} Einträge, {run.Added:N0} neu, "
                + $"{run.Consolidated:N0} gefestigt, {run.Condensed:N0} zusammengefasst, {run.Expired:N0} entfernt, {run.Waiting:N0} warten noch.";
            MyKnowledgeChanged?.Invoke(this, EventArgs.Empty);
            await SuggestMyKnowledgeCategoriesAsync(ct);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            LogError("Mein Wissen aktualisieren", ex);
            MyKnowledgeStatus = "Aktualisierung fehlgeschlagen: " + ex.Message;
        }
    }

    /// <summary>Keeps or releases items; kept ones neither decay nor expire.</summary>
    internal void KeepMyKnowledge(IReadOnlyCollection<string> keys, bool keep)
        => ChangeMyKnowledge("Mein Wissen: dauerhaft behalten speichern", (state, context) =>
        {
            var set = keys.ToHashSet(StringComparer.Ordinal);
            foreach (var e in state.Entries.Where(e => set.Contains(e.Key)))
            {
                e.Kept = keep;
                e.Weight = MyKnowledge.Weight(e, DateTime.Now, context);
            }
        });

    /// <summary>Removed by the user in "Mein Wissen"; the source itself stays untouched.</summary>
    internal void DismissMyKnowledge(IReadOnlyCollection<string> keys)
        => ChangeMyKnowledge("Mein Wissen: Einträge entfernen", (state, _) => MyKnowledge.Dismiss(state, keys));

    /// <summary>
    /// "Zu Mein Wissen hinzufügen" from any window: fixes the items (100 %, no waiting, never condensed), taking
    /// them in from their source first when needed. <paramref name="prefixes"/> cover all snapshots of a recording.
    /// With <paramref name="notify"/> the tray reports the result (for windows without a status line).
    /// Returns the number of fixed entries; 0 when nothing usable was found.
    /// </summary>
    internal async Task<int> FixInMyKnowledgeAsync(IReadOnlyCollection<string> keys, IReadOnlyCollection<string>? prefixes = null, bool notify = false)
    {
        if (_myKnowledgeBroken || (keys.Count == 0 && (prefixes?.Count ?? 0) == 0)) return 0;
        var entries = SnapshotEntries();
        var recordings = Recorder.Directory;
        var set = keys.ToHashSet(StringComparer.Ordinal);
        bool Match(string key) => set.Contains(key) || (prefixes ?? []).Any(p => key.StartsWith(p, StringComparison.Ordinal));
        int count;
        try
        {
            count = await Task.Run(() =>
            {
                var byId = entries.DistinctBy(e => e.Id).ToDictionary(e => e.Id);
                var chunks = Semantic.Collect(entries, AppPaths.KbNotesDir, recordings, AppPaths.NotesDir).Where(c => Match(c.ItemKey));
                var candidates = MyKnowledge.Candidates(chunks, byId);
                // The search index leaves out very short entries; a deliberate choice by the user still counts.
                foreach (var key in set.Where(k => k.StartsWith("c:", StringComparison.Ordinal) && candidates.All(c => c.Key != k)))
                    if (long.TryParse(key.AsSpan(2), out var id) && byId.TryGetValue(id, out var entry) && MyKnowledge.Candidate(entry) is { } extra)
                        candidates.Add(extra);
                lock (_myKnowledgeGate)
                {
                    var n = MyKnowledge.Fix(_myKnowledge, keys, prefixes, candidates, DateTime.Now);
                    MyKnowledge.Save(MyKnowledgeFile, _myKnowledge);
                    return n;
                }
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            LogError("Mein Wissen: fest hinzufügen", ex);
            if (notify) _trayIcon?.ShowBalloonTip(3000, AppNames.MyKnowledge, "Konnte nicht gespeichert werden.", System.Windows.Forms.ToolTipIcon.Warning);
            return 0;
        }
        MyKnowledgeChanged?.Invoke(this, EventArgs.Empty);
        if (notify)
            _trayIcon?.ShowBalloonTip(2500, AppNames.MyKnowledge, count > 0
                ? $"{count} {(count == 1 ? "Eintrag" : "Einträge")} in {AppNames.MyKnowledge} aufgenommen (100 %)."
                : "Kein auswertbarer Text, nichts hinzugefügt.", count > 0 ? System.Windows.Forms.ToolTipIcon.Info : System.Windows.Forms.ToolTipIcon.Warning);
        return count;
    }

    /// <summary>Condenses the entries into one digest; returns its key, or null when fewer than two could be used.</summary>
    internal string? SummarizeMyKnowledge(IReadOnlyCollection<string> keys)
    {
        string? created = null;
        ChangeMyKnowledge("Mein Wissen: Einträge zusammenfassen",
            (state, context) => created = MyKnowledge.Summarize(state, keys, context, DateTime.Now)?.Key);
        return created;
    }

    /// <summary>The source was deleted in Merkautomat, so its copy goes as well.</summary>
    internal void ForgetMyKnowledge(IReadOnlyCollection<string> keys, IReadOnlyCollection<string>? prefixes = null)
        => ChangeMyKnowledge("Mein Wissen: gelöschte Quellen entfernen", (state, _) => MyKnowledge.Forget(state, keys, prefixes));

    private void ChangeMyKnowledge(string attempted, Action<MyKnowledgeState, MyKnowledgeContext> change)
    {
        if (_myKnowledgeBroken) return;
        var context = MyKnowledgeContext();
        try
        {
            lock (_myKnowledgeGate)
            {
                change(_myKnowledge, context);
                MyKnowledge.Save(MyKnowledgeFile, _myKnowledge);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            LogError(attempted, ex);
        }
        MyKnowledgeChanged?.Invoke(this, EventArgs.Empty);
    }
}

using System.IO;
using System.Windows.Threading;
using Merkautomat.Services;

namespace Merkautomat;

public partial class App
{
    private KnowledgeFolder? _knowledgeFolder;
    private DispatcherTimer? _knowledgeFolderTimer;
    private readonly CancellationTokenSource _knowledgeFolderCts = new();
    private Task? _knowledgeFolderTask;
    private bool _knowledgeFolderAgain;
    private bool _folderIndexPending;

    internal KnowledgeFolderState KnowledgeFolderSnapshot => _knowledgeFolder?.Snapshot ?? new("", []);
    internal string KnowledgeFolderStatus => _knowledgeFolder?.Status ?? "Noch nicht abgeglichen.";

    private void StartKnowledgeFolder()
    {
        _knowledgeFolder = new KnowledgeFolder(Path.Combine(AppPaths.DataDir, KnowledgeFolder.FileName),
            AppPaths.DataDir, AppPaths.ModelsDir, Path.Combine(AppContext.BaseDirectory, "models"));
        Semantic.AdditionalSources = _knowledgeFolder.Chunks;
        _knowledgeFolderTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _knowledgeFolderTimer.Tick += (_, _) => RefreshKnowledgeFolder();
        _knowledgeFolderTimer.Start();
        RefreshKnowledgeFolder();
    }

    internal void RefreshKnowledgeFolder()
    {
        if (_knowledgeFolder == null || _knowledgeFolderCts.IsCancellationRequested) return;
        if (_knowledgeFolderTask is { IsCompleted: false }) { _knowledgeFolderAgain = true; return; }
        _knowledgeFolderTask = RefreshKnowledgeFolderAsync();
        _knowledgeFolderTask.Forget("Knowledgeordner synchronisieren");
    }

    private async Task RefreshKnowledgeFolderAsync()
    {
        do
        {
            _knowledgeFolderAgain = false;
            var settings = CurrentSettings;
            var root = settings.KnowledgeFolderPath;
            var enabled = settings.KnowledgeFolderEnabled;
            var extensions = settings.FileContentExtensions;
            var maxKb = settings.FileContentMaxKb;
            var ct = _knowledgeFolderCts.Token;
            try
            {
                var changed = await Task.Run(() => _knowledgeFolder!.Scan(root, enabled, extensions, maxKb, ct), ct);
                _folderIndexPending |= changed || (enabled && _knowledgeFolder!.Snapshot.Files.Length > 0 && Semantic.Count == 0);
                if (changed) NotifyKnowledgeBaseChanged();
                if (_folderIndexPending && EmbeddingModel.IsInstalled(EmbeddingModel.ResolveModelsDir(AppContext.BaseDirectory, AppPaths.ModelsDir)))
                {
                    var entries = SnapshotEntries();
                    var recordings = Recorder.Directory;
                    var chunks = await Task.Run(() => Semantic.Collect(entries, AppPaths.KbNotesDir, recordings, AppPaths.NotesDir), ct);
                    await Semantic.SyncAsync(chunks, ct);
                    _folderIndexPending = false;
                    NotifyKnowledgeBaseChanged();
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or System.Security.Cryptography.CryptographicException)
            {
                LogError("Knowledgeordner abgleichen", ex);
                // A failed scan keeps the previous snapshot and is retried by the timer.
            }
        } while (_knowledgeFolderAgain && !_knowledgeFolderCts.IsCancellationRequested);
    }

    private void StopKnowledgeFolder()
    {
        _knowledgeFolderTimer?.Stop();
        _knowledgeFolderCts.Cancel();
    }
}

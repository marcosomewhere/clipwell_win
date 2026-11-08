using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace ClipwellWin.Services;

public sealed class LocalAiService : IDisposable
{
    private readonly Func<string> _selection;
    private readonly E5Categorization? _e5;
    private readonly string _modelsDirectory;
    private readonly SemaphoreSlim _requests = new(1, 1);
    private readonly object _sync = new();
    private CancellationTokenSource _session = new();
    private Process? _process;
    private HttpClient? _client;
    private bool _disposed;
    private readonly Dictionary<string, LocalAiSuggestion> _cache = [];
    private AiLoad _backgroundLoad = AiLoad.Eco;
    private AiLoad _appliedLoad = AiLoad.Normal;
    private CancellationTokenSource? _backgroundCall;
    private int _interactiveWaiting;
    private DateTime _lastInteractiveUse = DateTime.MinValue;
    /// <summary>Last direct (non-background) model call; background work stays away for a while after it.</summary>
    public DateTime LastInteractiveUse { get { lock (_sync) return _lastInteractiveUse; } }
    public LocalAiModel Model { get; }
    public E5Categorization? E5 => _e5;
    public bool UsesE5 => LocalAiModel.Normalize(_selection()) == E5Categorization.Id;
    public string DisplayName => UsesE5 ? "E5" : "Granite";
    public bool Enabled => LocalAiModel.Normalize(_selection()) != "off";
    public bool Ready => Enabled && (UsesE5 ? _e5?.Ready == true : Model.IsInstalled);

    public LocalAiService(string modelsDirectory, Func<string> selection, E5Categorization? e5 = null)
    {
        Model = new(modelsDirectory);
        _selection = selection;
        _modelsDirectory = modelsDirectory;
        _e5 = e5;
    }

    public Task InstallSelectedAsync(IProgress<LocalAiProgress>? progress, CancellationToken ct)
    {
        if (!UsesE5) return Model.InstallAsync(progress, ct);
        var total = EmbeddingModel.Files.Sum(f => f.Size);
        return EmbeddingModel.DownloadAsync(_modelsDirectory,
            new Progress<long>(bytes => progress?.Report(new("E5-Suchmodell wird geladen …", 100.0 * bytes / total))), ct);
    }

    /// <param name="assignments">Saved manual assignments (item key to category); only E5 learns from them.</param>
    public Task<LocalAiSuggestion> SuggestAsync(IEnumerable<BrainSource> sources, IEnumerable<string> categories,
        CancellationToken cancellationToken = default, IReadOnlyDictionary<string, string>? assignments = null)
    {
        if (!Enabled) throw new InvalidOperationException("Die lokale KI ist ausgeschaltet.");
        if (UsesE5) return SuggestCoreAsync(sources, categories, cancellationToken, assignments, useE5: true);
        // Granite also proposes a title here, so E5 only contributes hints and never replaces the Granite answer.
        return SuggestWithGraniteAsync(sources, categories, cancellationToken, assignments, e5First: false);
    }

    /// <summary>
    /// Granite regardless of the selection in the Kontrollstation; needs the installed model. With saved assignments
    /// and the search model, E5 runs first: <paramref name="e5First"/> lets a confident E5 answer from own examples
    /// replace the slow Granite call, otherwise E5's best candidates are passed to Granite as hints.
    /// </summary>
    /// <param name="background">
    /// Background work: runs with the load set by <see cref="SetBackgroundLoad"/> and a longer limit, and gives way to
    /// every direct call (then throws <see cref="OperationCanceledException"/>).
    /// </param>
    public async Task<LocalAiSuggestion> SuggestWithGraniteAsync(IEnumerable<BrainSource> sources, IEnumerable<string> categories,
        CancellationToken cancellationToken = default, IReadOnlyDictionary<string, string>? assignments = null, bool e5First = false,
        bool background = false)
    {
        if (!Model.IsInstalled) throw new InvalidOperationException("Granite ist nicht installiert. Bitte zuerst herunterladen.");
        var sourceList = sources.ToArray();
        var categoryList = categories.ToArray();
        var e5 = await TryE5Async(sourceList, categoryList, cancellationToken, assignments).ConfigureAwait(false);
        if (e5First && CategorizationCascade.Settles(e5)) return CategorizationCascade.Shortcut(e5!);
        return await SuggestCoreAsync(sourceList, categoryList, cancellationToken, null, useE5: false,
            CategorizationCascade.Hints(e5), background).ConfigureAwait(false);
    }

    /// <summary>E5 as a cheap first step; any failure just means Granite works without hints.</summary>
    private async Task<LocalAiSuggestion?> TryE5Async(BrainSource[] sources, string[] categories, CancellationToken ct,
        IReadOnlyDictionary<string, string>? assignments)
    {
        if (assignments is not { Count: > 0 } || _e5?.Ready != true) return null;
        var copy = assignments.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal);
        try { return await _e5.SuggestAsync(sources, categories, ct, copy).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return null; }
    }

    /// <summary>E5 regardless of the selection in the Kontrollstation (categorization window); needs the search model.</summary>
    public Task<LocalAiSuggestion> SuggestWithE5Async(IEnumerable<BrainSource> sources, IEnumerable<string> categories,
        CancellationToken cancellationToken = default, IReadOnlyDictionary<string, string>? assignments = null)
    {
        if (_e5?.Ready != true) throw new InvalidOperationException("Das E5-Suchmodell fehlt. Bitte unter Suche das Modell laden.");
        return SuggestCoreAsync(sources, categories, cancellationToken, assignments, useE5: true);
    }

    public bool E5Ready => _e5?.Ready == true;

    private Task<LocalAiSuggestion> SuggestCoreAsync(IEnumerable<BrainSource> sources, IEnumerable<string> categories,
        CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? assignments, bool useE5, IReadOnlyList<string>? hints = null,
        bool background = false)
    {
        var sourceList = sources.ToArray();
        var categoryList = categories.ToArray();
        var assignmentCopy = assignments?.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal);
        return ExecuteAsync(async ct =>
        {
            if (useE5)
                return await (_e5 ?? throw new InvalidOperationException("E5 ist nicht verfügbar."))
                    .SuggestAsync(sourceList, categoryList, ct, assignmentCopy).ConfigureAwait(false);
            var input = LocalAiCategorization.BuildInput(sourceList, categoryList, hints);
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
            if (_cache.TryGetValue(key, out var cached)) return cached;
            var json = await PostChatAsync(LocalAiCategorization.BuildRequest(input), "den Vorschlag", ct).ConfigureAwait(false);
            var result = LocalAiCategorization.Parse(json) with { Method = ClassifierMethods.Granite };
            ct.ThrowIfCancellationRequested();
            if (_cache.Count >= 64) _cache.Clear();
            _cache[key] = result;
            return result;
        }, "Bitte weniger Quellen auswählen und erneut versuchen.", cancellationToken, background);
    }

    /// <summary>
    /// Answers <paramref name="question"/> only from the numbered sources with Granite, regardless of the selection in
    /// the Kontrollstation; needs the installed model.
    /// </summary>
    public Task<KnowledgeAnswerResult> AnswerAsync(string question, IReadOnlyList<AnswerSource> sources, CancellationToken cancellationToken = default)
    {
        if (!Model.IsInstalled) throw new InvalidOperationException("Granite ist nicht installiert. Bitte zuerst herunterladen.");
        var input = KnowledgeAnswer.BuildInput(question, sources);
        var count = sources.Count;
        return ExecuteAsync(async ct =>
        {
            var json = await PostChatAsync(KnowledgeAnswer.BuildRequest(input), "die Antwort", ct).ConfigureAwait(false);
            return KnowledgeAnswer.Parse(json, count);
        }, "Bitte die Frage enger stellen und erneut versuchen.", cancellationToken);
    }

    private async Task<string> PostChatAsync(string request, string what, CancellationToken ct)
    {
        var client = await StartAsync(ct).ConfigureAwait(false);
        using var body = new StringContent(request, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("v1/chat/completions", body, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Die lokale KI konnte {what} nicht erstellen (HTTP {(int)response.StatusCode}).");
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// One model call at a time, bound to the current session and a three-minute limit (ten minutes in the background,
    /// where the process may run throttled). A direct call cancels a running background call instead of queueing behind it.
    /// </summary>
    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, string timeoutHint, CancellationToken cancellationToken,
        bool background = false)
    {
        CancellationToken session;
        using var preempt = background ? new CancellationTokenSource() : null;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            session = _session.Token;
            if (!background)
            {
                _lastInteractiveUse = DateTime.Now;
                _interactiveWaiting++;
                _backgroundCall?.Cancel();
            }
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(session, cancellationToken, preempt?.Token ?? CancellationToken.None);
        linked.CancelAfter(background ? TimeSpan.FromMinutes(10) : TimeSpan.FromMinutes(3));
        var ct = linked.Token;
        try { await _requests.WaitAsync(ct).ConfigureAwait(false); }
        finally
        {
            if (!background) lock (_sync) _interactiveWaiting--;
        }
        try
        {
            lock (_sync)
            {
                if (!background) _lastInteractiveUse = DateTime.Now;
                else if (_interactiveWaiting > 0) preempt!.Cancel();
                else _backgroundCall = preempt;
            }
            ct.ThrowIfCancellationRequested();
            ApplyLoad(background ? BackgroundLoad : AiLoad.Normal);
            return await work(ct).ConfigureAwait(false);
        }
        // Giving way keeps the server running: llama.cpp drops a request whose connection was closed.
        catch (Exception) when (preempt?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested && !session.IsCancellationRequested)
        {
            throw new OperationCanceledException("Die Hintergrundarbeit hat einer direkten Anfrage Platz gemacht.", preempt.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !session.IsCancellationRequested)
        {
            Stop();
            throw new TimeoutException($"Die lokale KI hat nach {(background ? "zehn" : "drei")} Minuten nicht geantwortet. " + timeoutHint);
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            Stop();
            throw new OperationCanceledException(ct);
        }
        catch
        {
            // An interrupted generation must not keep occupying the single local slot.
            Stop();
            throw;
        }
        finally
        {
            lock (_sync) if (_backgroundCall == preempt) _backgroundCall = null;
            _requests.Release();
        }
    }

    private AiLoad BackgroundLoad { get { lock (_sync) return _backgroundLoad; } }

    /// <summary>Load for background calls; applied at once when a background call is running.</summary>
    public void SetBackgroundLoad(AiLoad load)
    {
        bool running;
        lock (_sync)
        {
            if (_backgroundLoad == load && (_backgroundCall == null || _appliedLoad == load)) return;
            _backgroundLoad = load;
            running = _backgroundCall != null;
        }
        if (running) ApplyLoad(load);
    }

    private void ApplyLoad(AiLoad load)
    {
        Process? process;
        lock (_sync)
        {
            _appliedLoad = load;
            process = _process;
        }
        if (process != null) ProcessLoad.Apply(process, load);
    }

    private async Task<HttpClient> StartAsync(CancellationToken ct)
    {
        lock (_sync)
        {
            if (_process is { HasExited: false } && _client != null) return _client;
        }
        // A server left behind by a crash or forced exit would lock the runtime files that are restored below.
        ChildProcessJob.KillOrphans(Model.ServerPath);
        await Model.PrepareRuntimeAsync(ct).ConfigureAwait(false);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var start = new ProcessStartInfo(Model.ServerPath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = System.IO.Path.GetDirectoryName(Model.ServerPath)!,
            RedirectStandardError = true, RedirectStandardOutput = true
        };
        // Ignore inherited llama settings: no remote model, tool, proxy or listener configuration.
        foreach (var name in start.Environment.Keys.Where(k => k.StartsWith("LLAMA_", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(name);
        // Through the environment, not the command line: command lines end up in process lists and EDR/audit logs.
        start.Environment["LLAMA_API_KEY"] = key;
        foreach (var argument in new[]
        {
            "--model", Model.ModelPath, "--host", "127.0.0.1", "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--ctx-size", "4096", "--parallel", "1", "--threads", Math.Clamp(Environment.ProcessorCount / 2, 1, 4).ToString(),
            "--gpu-layers", "0", "--no-webui", "--no-webui-mcp-proxy", "--reasoning", "off", "--log-disable",
            "--sleep-idle-seconds", "180"
        }) start.ArgumentList.Add(argument);
        var client = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = 65536
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        lock (_sync)
        {
            ct.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            _client?.Dispose();
            _process?.Dispose();
            _client = client;
            _process = Process.Start(start) ?? throw new InvalidOperationException("Die lokale KI konnte nicht gestartet werden.");
            ChildProcessJob.Assign(_process);
            ProcessLoad.Apply(_process, _appliedLoad);
            _process.OutputDataReceived += (_, _) => { };
            _process.ErrorDataReceived += (_, _) => { };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            lock (_sync)
                if (_process == null || _process.HasExited) throw new InvalidOperationException("Granite wurde beendet. Bitte verfügbaren Arbeitsspeicher und den Download prüfen.");
            try
            {
                using var health = await client.GetAsync("health", ct).ConfigureAwait(false);
                if (health.IsSuccessStatusCode) return client;
            }
            catch (HttpRequestException) { }
            await Task.Delay(250, ct).ConfigureAwait(false);
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            _session.Cancel();
            _session = new();
            if (_process != null)
            {
                try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                _process.Dispose();
                _process = null;
            }
            _client?.Dispose();
            _client = null;
        }
    }

    public void Dispose()
    {
        lock (_sync) { _disposed = true; Stop(); }
    }
}

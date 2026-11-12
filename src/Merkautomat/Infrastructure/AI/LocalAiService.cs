using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Merkautomat.Services;

public sealed partial class LocalAiService : IDisposable
{
    private readonly Func<string> _selection;
    private readonly Func<string?> _languageModelSelection;
    /// <summary>The user's prompt text by id (see <see cref="AiPrompts"/>); empty means the default.</summary>
    public Func<string, string> Prompt { get; set; } = _ => "";
    private readonly E5Categorization? _e5;
    private readonly string _modelsDirectory;
    private readonly SemaphoreSlim _requests = new(1, 1);
    private readonly SemaphoreSlim _cpuRequests = new(1, 1);
    public AiExecutionManager? Execution { get; }
    private AiExecutionPlan? _activePlan;
    private bool _lastAttemptUsedGpu;
    private DateTime _lastGeneration = DateTime.UtcNow;
    public bool Benchmarking { get; private set; }
    public bool GpuActive { get { lock (_sync) return _activePlan?.UsesGpu == true; } }
    private readonly object _sync = new();
    private CancellationTokenSource _session = new();
    private Process? _process;
    private HttpClient? _client;
    private string? _processModelId;
    private bool _disposed;
    private readonly Dictionary<string, LocalAiSuggestion> _cache = [];
    private AiLoad _backgroundLoad = AiLoad.Eco;
    private AiLoad _appliedLoad = AiLoad.Normal;
    private CancellationTokenSource? _backgroundCall;
    private int _interactiveWaiting;
    private DateTime _lastInteractiveUse = DateTime.MinValue;
    /// <summary>Last direct (non-background) model call; background work stays away for a while after it.</summary>
    public DateTime LastInteractiveUse { get { lock (_sync) return _lastInteractiveUse; } }
    private readonly LocalAiModel _granite;
    private readonly LocalAiModel _qwen;
    private readonly CategoryModelFiles _miniLm;
    private readonly CategoryModelFiles _horizon;
    public LocalAiModel Model => LocalAiModel.ResolveLanguageModelId(_selection(), _languageModelSelection()) == LocalAiModel.QwenId ? _qwen : _granite;
    public LocalAiModel SuggestionModel => LocalAiModel.Normalize(_selection()) == LocalAiModel.QwenId ? _qwen : _granite;
    public E5Categorization? E5 => _e5;
    public bool UsesE5 => LocalAiModel.Normalize(_selection()) == E5Categorization.Id;
    public CategoryModelFiles? CategoryModel => _selection() switch
    {
        CategoryModelFiles.MiniLmId => _miniLm,
        CategoryModelFiles.HorizonId => _horizon,
        _ => null,
    };
    public bool UsesCategoryOnly => UsesE5 || CategoryModel != null;
    public string DisplayName => UsesE5 ? "E5" : CategoryModel?.Name ?? SuggestionModel.Name;
    public bool Enabled => LocalAiModel.Normalize(_selection()) != "off";
    public bool Ready => Enabled && (UsesE5 ? _e5?.Ready == true : CategoryModel?.IsInstalled ?? SuggestionModel.IsInstalled);

    public LocalAiService(string modelsDirectory, Func<string> selection, E5Categorization? e5 = null, Func<string?>? languageModelSelection = null, AiExecutionManager? execution = null)
    {
        _granite = new(modelsDirectory);
        _qwen = new(modelsDirectory, LocalAiModel.QwenId);
        _miniLm = new(modelsDirectory, CategoryModelFiles.MiniLmId);
        _horizon = new(modelsDirectory, CategoryModelFiles.HorizonId);
        _selection = selection;
        _languageModelSelection = languageModelSelection ?? (() => null);
        _modelsDirectory = modelsDirectory;
        _e5 = e5;
        Execution = execution;
        if (Execution != null) Execution.Resources.Changed += ResourcesChanged;
    }

    public Task InstallSelectedAsync(IProgress<LocalAiProgress>? progress, CancellationToken ct)
    {
        if (CategoryModel is { } categoryModel) return categoryModel.InstallAsync(progress, ct);
        if (!UsesE5) return SuggestionModel.InstallAsync(progress, ct);
        var total = EmbeddingModel.Files.Sum(f => f.Size);
        return EmbeddingModel.DownloadAsync(_modelsDirectory,
            new Progress<long>(bytes => progress?.Report(new("E5-Suchmodell wird geladen …", 100.0 * bytes / total))), ct);
    }

    /// <param name="assignments">Saved manual assignments (item key to category); only E5 learns from them.</param>
    public Task<LocalAiSuggestion> SuggestAsync(IEnumerable<BrainSource> sources, IEnumerable<string> categories,
        CancellationToken cancellationToken = default, IReadOnlyDictionary<string, string>? assignments = null)
    {
        if (!Enabled) throw new InvalidOperationException("Die lokale KI ist ausgeschaltet.");
        if (CategoryModel is { } categoryModel)
        {
            var sourceList = sources.ToArray();
            var labels = categories.ToArray();
            return ExecuteAsync(async ct =>
            {
                await categoryModel.VerifyAsync(ct).ConfigureAwait(false);
                return await Task.Run(() =>
                {
                    using var classifier = new OnnxCategorization(categoryModel);
                    return classifier.Suggest(sourceList, labels, ct);
                }, ct).ConfigureAwait(false);
            }, "Bitte weniger Kategorien auswählen und erneut versuchen.", cancellationToken, cpuWork: true);
        }
        if (UsesE5) return SuggestCoreAsync(sources, categories, cancellationToken, assignments, useE5: true);
        // The language model also proposes a title here, so E5 only contributes hints.
        return SuggestWithModelAsync(SuggestionModel, sources, categories, cancellationToken, assignments, e5First: false, background: false);
    }

    /// <summary>
    /// The independently selected language model; needs the installed model. With saved assignments
    /// and the search model, E5 runs first: <paramref name="e5First"/> lets a confident E5 answer from own examples
    /// replace the language-model call, otherwise E5's best candidates are passed as hints.
    /// </summary>
    /// <param name="background">
    /// Background work: runs with the load set by <see cref="SetBackgroundLoad"/> and a longer limit, and gives way to
    /// every direct call (then throws <see cref="OperationCanceledException"/>).
    /// </param>
    public Task<LocalAiSuggestion> SuggestWithLanguageModelAsync(IEnumerable<BrainSource> sources, IEnumerable<string> categories,
        CancellationToken cancellationToken = default, IReadOnlyDictionary<string, string>? assignments = null, bool e5First = false,
        bool background = false)
        => SuggestWithModelAsync(Model, sources, categories, cancellationToken, assignments, e5First, background);

    private async Task<LocalAiSuggestion> SuggestWithModelAsync(LocalAiModel model, IEnumerable<BrainSource> sources, IEnumerable<string> categories,
        CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? assignments, bool e5First, bool background)
    {
        if (!model.IsInstalled) throw new InvalidOperationException($"{model.Name} ist nicht installiert. Bitte zuerst herunterladen.");
        var sourceList = sources.ToArray();
        var categoryList = categories.ToArray();
        var e5 = await TryE5Async(sourceList, categoryList, cancellationToken, assignments).ConfigureAwait(false);
        if (e5First && CategorizationCascade.Settles(e5)) return CategorizationCascade.Shortcut(e5!);
        return await SuggestCoreAsync(sourceList, categoryList, cancellationToken, null, useE5: false,
            CategorizationCascade.Hints(e5), background, model).ConfigureAwait(false);
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
        bool background = false, LocalAiModel? languageModel = null)
    {
        var sourceList = sources.ToArray();
        var categoryList = categories.ToArray();
        var assignmentCopy = assignments?.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal);
        var model = languageModel ?? Model;
        return ExecuteAsync(async ct =>
        {
            if (useE5)
                return await (_e5 ?? throw new InvalidOperationException("E5 ist nicht verfügbar."))
                    .SuggestAsync(sourceList, categoryList, ct, assignmentCopy).ConfigureAwait(false);
            var input = LocalAiCategorization.BuildInput(sourceList, categoryList, hints);
            var prompt = AiPrompts.Effective(AiPrompts.Categorize, Prompt(AiPrompts.Categorize));
            var key = model.ModelId + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prompt + "\n" + input)));
            if (_cache.TryGetValue(key, out var cached)) return cached;
            var json = await PostChatAsync(model, LocalAiCategorization.BuildRequest(input, prompt), "den Vorschlag", ct).ConfigureAwait(false);
            var result = LocalAiCategorization.Parse(json) with { Method = model.Method };
            ct.ThrowIfCancellationRequested();
            if (_cache.Count >= 64) _cache.Clear();
            _cache[key] = result;
            return result;
        }, "Bitte weniger Quellen auswählen und erneut versuchen.", cancellationToken, background, cpuWork: useE5);
    }

    /// <summary>
    /// Answers <paramref name="question"/> only from numbered sources with the selected language model.
    /// Needs the installed language model, independently of the categorization selection.
    /// </summary>
    public Task<KnowledgeAnswerResult> AnswerAsync(string question, IReadOnlyList<AnswerSource> sources, CancellationToken cancellationToken = default)
    {
        var model = Model;
        if (!model.IsInstalled) throw new InvalidOperationException($"{model.Name} ist nicht installiert. Bitte zuerst herunterladen.");
        var input = KnowledgeAnswer.BuildInput(question, sources);
        var count = sources.Count;
        return ExecuteAsync(async ct =>
        {
            var json = await PostChatAsync(model, KnowledgeAnswer.BuildRequest(input, AiPrompts.Effective(AiPrompts.Answer, Prompt(AiPrompts.Answer))), "die Antwort", ct).ConfigureAwait(false);
            return KnowledgeAnswer.Parse(json, count);
        }, "Bitte die Frage enger stellen und erneut versuchen.", cancellationToken);
    }

    /// <summary>
    /// Short summary of numbered sources with the selected language model; every sentence cites its sources
    /// (<see cref="MyKnowledgeAi.ParseSummary"/>). Used in the background for digests and the weekly review.
    /// </summary>
    public Task<List<MyKnowledgeSummarySentence>> SummarizeAsync(string promptId, IReadOnlyList<string> sources, int maxSentences, string task,
        bool background, CancellationToken cancellationToken = default)
    {
        var model = Model;
        if (!model.IsInstalled) throw new InvalidOperationException($"{model.Name} ist nicht installiert. Bitte zuerst herunterladen.");
        if (sources.Count == 0) throw new ArgumentException("Keine Quellen für die Zusammenfassung.");
        var request = MyKnowledgeAi.BuildSummaryRequest(sources, maxSentences, task, AiPrompts.Effective(promptId, Prompt(promptId)));
        var count = sources.Count;
        return ExecuteAsync(async ct =>
        {
            var json = await PostChatAsync(model, request, "die Zusammenfassung", ct).ConfigureAwait(false);
            return MyKnowledgeAi.ParseSummary(json, count);
        }, "Bitte weniger Quellen verwenden.", cancellationToken, background);
    }

    /// <summary>Name for a finer level below <paramref name="parent"/> for a group of similar entries (background work).</summary>
    public Task<RefinementSuggestion> RefineAsync(CategoryPath parent, IReadOnlyList<string> existing, IReadOnlyList<BrainSource> sources,
        bool background, CancellationToken cancellationToken = default)
    {
        var model = Model;
        if (!model.IsInstalled) throw new InvalidOperationException($"{model.Name} ist nicht installiert. Bitte zuerst herunterladen.");
        var request = CategoryRefinement.BuildRequest(CategoryRefinement.BuildInput(parent, existing, sources),
            AiPrompts.Effective(AiPrompts.Refine, Prompt(AiPrompts.Refine)));
        return ExecuteAsync(async ct =>
        {
            var json = await PostChatAsync(model, request, "den Vorschlag", ct).ConfigureAwait(false);
            return CategoryRefinement.Parse(json);
        }, "Bitte später erneut versuchen.", cancellationToken, background);
    }

    private async Task<string> PostChatAsync(LocalAiModel model, string request, string what, CancellationToken ct)
    {
        if (Execution?.NeedsBenchmark(model) == true && Execution.CanBenchmark(model))
            await CompareAsync(model, ct, automatic: true).ConfigureAwait(false);
        try { return await SendChatAsync(model, request, what, ct).ConfigureAwait(false); }
        catch (Exception ex) when (_lastAttemptUsedGpu && !ct.IsCancellationRequested
            && ex is HttpRequestException or InvalidOperationException or ObjectDisposedException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            App.LogError("GPU-Anfrage auf CPU wiederholen", ex);
            Execution!.BlockGpu("GPU-Ausführung fehlgeschlagen.");
            lock (_sync) StopProcessCore();
            return await SendChatAsync(model, request, what, ct, AiExecutionPolicy.CpuDefault(Environment.ProcessorCount)).ConfigureAwait(false);
        }
    }

    private async Task<string> SendChatAsync(LocalAiModel model, string request, string what, CancellationToken ct, AiExecutionPlan? forced = null)
    {
        var client = await StartAsync(model, ct, forced).ConfigureAwait(false);
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
        bool background = false, bool cpuWork = false, bool benchmark = false)
    {
        var gate = cpuWork ? _cpuRequests : _requests;
        CancellationToken session;
        using var preempt = background ? new CancellationTokenSource() : null;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            session = _session.Token;
            if (!background)
            {
                _lastInteractiveUse = DateTime.Now;
                if (!cpuWork)
                {
                    _interactiveWaiting++;
                    _backgroundCall?.Cancel();
                }
            }
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(session, cancellationToken, preempt?.Token ?? CancellationToken.None);
        linked.CancelAfter(background || benchmark ? TimeSpan.FromMinutes(10) : TimeSpan.FromMinutes(3));
        var ct = linked.Token;
        try { await gate.WaitAsync(ct).ConfigureAwait(false); }
        finally
        {
            if (!background && !cpuWork) lock (_sync) _interactiveWaiting--;
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
            if (!cpuWork) ApplyLoad(background ? BackgroundLoad : AiLoad.Normal);
            return await work(ct).ConfigureAwait(false);
        }
        // Giving way keeps the server running: llama.cpp drops a request whose connection was closed.
        catch (Exception) when (preempt?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested && !session.IsCancellationRequested)
        {
            throw new OperationCanceledException("Die Hintergrundarbeit hat einer direkten Anfrage Platz gemacht.", preempt.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !session.IsCancellationRequested)
        {
            if (!cpuWork) Stop();
            throw new TimeoutException($"Die lokale KI hat nach {(background || benchmark ? "zehn" : "drei")} Minuten nicht geantwortet. " + timeoutHint);
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            if (!cpuWork) Stop();
            throw new OperationCanceledException(ct);
        }
        catch
        {
            // An interrupted generation must not keep occupying the single local slot.
            if (!cpuWork) Stop();
            throw;
        }
        finally
        {
            lock (_sync) if (_backgroundCall == preempt) _backgroundCall = null;
            if (!cpuWork) _lastGeneration = DateTime.UtcNow;
            gate.Release();
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

    private async Task<HttpClient> StartAsync(LocalAiModel model, CancellationToken ct, AiExecutionPlan? forced = null)
    {
        _lastAttemptUsedGpu = false;
        if (Execution != null && !Execution.Resources.Latest.Fresh)
            await Execution.Resources.RefreshAsync(ct).ConfigureAwait(false);
        if (forced != null && Execution != null)
        {
            var reason = _activePlan == null ? AiExecutionPolicy.StartBlockReason(Execution.Resources.Latest, model)
                : AiExecutionPolicy.CriticalReason(Execution.Resources.Latest, false);
            if (reason != null) throw new InvalidOperationException(reason);
        }
        var plan = forced ?? Execution?.Select(model, _activePlan) ?? AiExecutionPolicy.CpuDefault(Environment.ProcessorCount);
        _lastAttemptUsedGpu = plan.UsesGpu;
        var serverPath = plan.UsesGpu ? Execution!.Runtime.ServerPath : model.ServerPath;
        lock (_sync)
        {
            if (_process is { HasExited: false } && _client != null)
            {
                if (_processModelId == model.ModelId && _activePlan?.Key == plan.Key) return _client;
                StopProcessCore();
            }
        }
        // A server left behind by a crash or forced exit would lock the runtime files that are restored below.
        ChildProcessJob.KillOrphans(serverPath);
        if (plan.UsesGpu) await Execution!.Runtime.PrepareAsync(model, ct).ConfigureAwait(false);
        else await model.PrepareRuntimeAsync(ct).ConfigureAwait(false);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var start = new ProcessStartInfo(serverPath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = System.IO.Path.GetDirectoryName(serverPath)!,
            RedirectStandardError = true, RedirectStandardOutput = true
        };
        // Ignore inherited llama settings: no remote model, tool, proxy or listener configuration.
        foreach (var name in start.Environment.Keys.Where(k => k.StartsWith("LLAMA_", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(name);
        // Through the environment, not the command line: command lines end up in process lists and EDR/audit logs.
        start.Environment["LLAMA_API_KEY"] = key;
        foreach (var argument in new[]
        {
            "--model", model.ModelPath, "--host", "127.0.0.1", "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--ctx-size", "4096", "--parallel", "1", "--threads", plan.Threads.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--gpu-layers", plan.GpuLayers < 0 ? "all" : plan.GpuLayers.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--batch-size", "128", "--ubatch-size", "128", "--poll", "0", "--poll-batch", "0", "--threads-http", "2", "--fit", "off",
            "--no-webui", "--no-webui-mcp-proxy", "--reasoning", "off", "--log-disable",
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
            _processModelId = model.ModelId;
            _activePlan = plan;
            _lastGeneration = DateTime.UtcNow;
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
                if (_process == null || _process.HasExited) throw new InvalidOperationException($"{model.Name} wurde beendet. Bitte verfügbaren Arbeitsspeicher und den Download prüfen.");
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
            StopProcessCore();
        }
    }

    // Called under _sync; switching roles must not cancel the request that needs the other model.
    private void StopProcessCore()
    {
        if (_process != null)
        {
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception ex) { App.LogError("Lokales Sprachmodell beenden", ex); }
            _process.Dispose();
            _process = null;
            _processModelId = null;
            _activePlan = null;
        }
        _client?.Dispose();
        _client = null;
    }

    public void Dispose()
    {
        if (Execution != null) Execution.Resources.Changed -= ResourcesChanged;
        lock (_sync) { _disposed = true; Stop(); }
    }
}

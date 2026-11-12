using System.IO;
using Merkautomat.Models;
using System.Text;
using System.Text.RegularExpressions;
using Whisper.net;
using Whisper.net.Ggml;

namespace Merkautomat.Services;

public enum DictationState { Idle, Downloading, Recording, Transcribing }

/// <summary>
/// Local speech-to-text: microphone capture plus Whisper (ggml model under data\models).
/// Only one dictation runs at a time; <see cref="Owner"/> tells the windows which of them started it.
/// </summary>
public sealed partial class DictationService : IDisposable
{
    public static readonly string[] ModelNames = ["base", "small", "medium"];
    private const int MinSamples = MicrophoneRecorder.SampleRate / 2;

    private readonly string _modelsDir;
    private readonly Func<(string Model, string Language)> _settings;
    private readonly MicrophoneRecorder _mic = new();
    private readonly SemaphoreSlim _factoryLock = new(1, 1);
    private WhisperFactory? _factory;
    private string? _factoryPath;

    public DictationService(string modelsDir, Func<(string Model, string Language)> settings)
    {
        _modelsDir = modelsDir;
        _settings = settings;
    }

    public event EventHandler? StateChanged;

    public DictationState State { get; private set; }
    public object? Owner { get; private set; }
    public string StatusText { get; private set; } = "";
    public MicrophoneRecorder Microphone => _mic;

    public ContentMetadata? LastTranscriptionMetadata { get; private set; }
    private DateTime _recordingStarted;

    public string ModelName => NormalizeModel(_settings().Model);

    /// <summary>Bundled model next to the EXE (base ships with Merkautomat), otherwise the downloaded copy.</summary>
    public string ModelPath => ResolveModelPath(AppContext.BaseDirectory, _modelsDir, ModelName);
    public bool IsModelAvailable => File.Exists(ModelPath);

    public static string ResolveModelPath(string appDir, string modelsDir, string model)
    {
        var fileName = $"ggml-{NormalizeModel(model)}.bin";
        var bundled = Path.Combine(appDir, "models", fileName);
        return File.Exists(bundled) ? bundled : Path.Combine(modelsDir, fileName);
    }

    public static string NormalizeModel(string? name)
        => ModelNames.Contains(name ?? "", StringComparer.OrdinalIgnoreCase) ? name!.ToLowerInvariant() : "base";

    public static int ApproxModelSizeMb(string name) => NormalizeModel(name) switch
    {
        "small" => 470,
        "medium" => 1500,
        _ => 145,
    };

    public bool IsBusyFor(object owner) => State != DictationState.Idle && !ReferenceEquals(Owner, owner);

    public async Task DownloadModelAsync(object owner, CancellationToken ct = default)
    {
        if (State != DictationState.Idle) throw new InvalidOperationException("Diktat läuft bereits.");
        var model = ModelName;
        var target = Path.Combine(_modelsDir, $"ggml-{model}.bin");
        SetState(DictationState.Downloading, owner, $"Lade Sprachmodell '{model}' ...");
        var tmp = target + ".part";
        try
        {
            Directory.CreateDirectory(_modelsDir);
            var type = model switch { "small" => GgmlType.Small, "medium" => GgmlType.Medium, _ => GgmlType.Base };
            await using (var source = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(type, QuantizationType.NoQuantization, ct))
            await using (var file = File.Create(tmp))
            {
                var buffer = new byte[1 << 16];
                long total = 0;
                long lastReport = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                    total += read;
                    if (total - lastReport >= 4 << 20)
                    {
                        lastReport = total;
                        SetState(DictationState.Downloading, owner,
                            $"Lade Sprachmodell '{model}' ... {total >> 20} / ~{ApproxModelSizeMb(model)} MB");
                    }
                }
            }
            File.Move(tmp, target, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { }
            SetState(DictationState.Idle, null, "");
        }
    }

    public void StartRecording(object owner)
    {
        if (State != DictationState.Idle) throw new InvalidOperationException("Diktat läuft bereits.");
        if (!MicrophoneRecorder.HasInputDevice())
            throw new InvalidOperationException("Kein Mikrofon gefunden.");
        _mic.Start();
        _recordingStarted = DateTime.Now;
        LastTranscriptionMetadata = null;
        SetState(DictationState.Recording, owner, "Aufnahme läuft ...");
        // Warm the model while the user is still speaking.
        _ = Task.Run(async () =>
        {
            try { await GetFactoryAsync(); }
            catch (Exception ex) { App.LogCrash(ex); }
        });
    }

    public void Cancel()
    {
        if (State != DictationState.Recording) return;
        _mic.Stop();
        SetState(DictationState.Idle, null, "");
    }

    /// <summary>Stops recording and returns the transcribed text (empty when nothing was understood).</summary>
    public async Task<string> StopAndTranscribeAsync(CancellationToken ct = default)
    {
        if (State != DictationState.Recording) return "";
        var owner = Owner;
        var samples = _mic.Stop();
        if (samples.Length < MinSamples)
        {
            SetState(DictationState.Idle, null, "");
            return "";
        }

        SetState(DictationState.Transcribing, owner, "Transkribiere ...");
        try
        {
            var language = NormalizeLanguage(_settings().Language);
            return await Task.Run(async () =>
            {
                var factory = await GetFactoryAsync();
                LastTranscriptionMetadata = new ContentMetadata
                {
                    CapturedAt = _recordingStarted, CaptureMethod = "Diktat", ContentType = "Transkript", Application = AppNames.App,
                    SpeechModel = "Whisper " + Path.GetFileNameWithoutExtension(_factoryPath)!.Replace("ggml-", "", StringComparison.Ordinal),
                    SpeechLanguage = language,
                };
                var builder = factory.CreateBuilder()
                    .WithThreads(Math.Max(1, Math.Min(8, Environment.ProcessorCount - 1)));
                builder = language == "auto" ? builder.WithLanguageDetection() : builder.WithLanguage(language);
                await using var processor = builder.Build();

                var sb = new StringBuilder();
                await foreach (var segment in processor.ProcessAsync(PadToMinimum(samples), ct))
                {
                    var text = segment.Text.Trim();
                    if (IsNoise(text)) continue;
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(text);
                }
                return sb.ToString().Trim();
            }, ct);
        }
        finally
        {
            SetState(DictationState.Idle, null, "");
        }
    }

    public static bool IsNoise(string text)
        => text.Length == 0 || NoiseRegex().IsMatch(text);

    public static string NormalizeLanguage(string? language)
    {
        var l = (language ?? "").Trim().ToLowerInvariant();
        return l.Length == 0 ? "de" : l;
    }

    private static float[] PadToMinimum(float[] samples)
    {
        // Whisper discards input shorter than one second; pad with silence instead.
        var min = MicrophoneRecorder.SampleRate + MicrophoneRecorder.SampleRate / 10;
        if (samples.Length >= min) return samples;
        var padded = new float[min];
        Array.Copy(samples, padded, samples.Length);
        return padded;
    }

    private async Task<WhisperFactory> GetFactoryAsync()
    {
        await _factoryLock.WaitAsync();
        try
        {
            var path = ModelPath;
            if (_factory != null && _factoryPath == path) return _factory;
            if (!File.Exists(path)) throw new FileNotFoundException("Sprachmodell fehlt.", path);
            _factory?.Dispose();
            _factory = WhisperFactory.FromPath(path);
            _factoryPath = path;
            return _factory;
        }
        finally
        {
            _factoryLock.Release();
        }
    }

    private void SetState(DictationState state, object? owner, string status)
    {
        State = state;
        Owner = owner;
        StatusText = status;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _mic.Dispose();
        _factory?.Dispose();
        _factoryLock.Dispose();
    }

    // Whisper emits bracketed annotations such as [Musik] or (Stille) for non-speech input.
    [GeneratedRegex(@"^[\[\(\*].*[\]\)\*]$")]
    private static partial Regex NoiseRegex();
}

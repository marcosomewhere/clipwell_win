using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Merkautomat.Services;
using MessageBox = Merkautomat.Views.AppMessageBox;

namespace Merkautomat.Views;

/// <summary>Suche "Fragen": answers a question from the best search hits with Granite and lists the cited sources.</summary>
public partial class KnowledgeBaseWindow
{
    public sealed record AnswerSourceChip(AnswerSource Source)
    {
        public string Label => $"[{Source.Number}] {(Source.Chunk.Title.Length == 0 ? "(ohne Titel)" : Source.Chunk.Title)}";
        public string Tooltip => $"{SemanticChunk.SourceName(Source.Chunk.Source)} · {FormatHitDate(Source.Chunk)}\n{Source.Text}";
    }

    private CancellationTokenSource? _askCts;
    private DispatcherTimer? _askTicker;
    private DateTime _askStarted;
    private string _askStage = "";
    private string _askQuestion = "";
    private KnowledgeAnswerResult? _answer;
    private List<AnswerSource> _answerSources = [];
    private bool _askRunning;

    private void CloseAsk()
    {
        _askCts?.Cancel();
        _askTicker?.Stop();
    }

    private void SemanticAsk_Click(object sender, RoutedEventArgs e) => _ = AskAsync();

    private async Task AskAsync()
    {
        var question = SemanticQuery.Text.Trim();
        if (!_semanticModelInstalled || _semanticClosed) return;
        if (question.Length == 0)
        {
            FocusSemanticQuery();
            ShowAnswerState("Erst oben eine Frage eingeben, z. B. wie habe ich das VPN-Zertifikat erneuert.", running: false);
            return;
        }
        if (_semanticMode == SemanticMode.Duplicates) _semanticMode = SemanticMode.Search;

        _askCts?.Cancel();
        _askCts?.Dispose();
        var cts = _askCts = new CancellationTokenSource();
        var ct = cts.Token;
        _askQuestion = question;
        _answer = null;
        _answerSources = [];
        SemanticAnswerSources.ItemsSource = null;
        SemanticAnswerText.Text = "";

        var ai = _app.LocalAi;
        if (!ai.Model.IsInstalled && !await InstallLanguageModelForAskAsync(ai, ct)) return;

        _semanticDebounce?.Stop();
        StartSemanticSync();
        StartAskTicker("Suche passende Einträge");
        var source = SelectedScope();
        var index = _app.Semantic;
        _semanticMode = SemanticMode.Search;
        _similarOrigin = null;
        _similarMarked = null;
        await RunSemanticQuery(() => index.Search(question, source), question, keepSelection: false,
            index.Count == 0 ? "Der Index wird noch aufgebaut, bitte gleich erneut fragen." : "Keine passenden Einträge.");
        if (_semanticClosed) return;
        if (ct.IsCancellationRequested)
        {
            if (_askCts == cts) { StopAskTicker(); ShowAnswerState("Abgebrochen.", running: false); }
            return;
        }

        var sources = KnowledgeAnswer.SelectSources(SemanticItems.Select(i => i.Hit));
        if (sources.Count == 0)
        {
            StopAskTicker();
            ShowAnswerState("Keine passenden Einträge gefunden, daher keine Antwort. Andere Begriffe oder eine andere Quelle versuchen.", running: false);
            return;
        }
        _answerSources = sources;
        _askStage = $"{ai.Model.Name} liest {sources.Count} Quellen";
        try
        {
            var result = await ai.AnswerAsync(question, sources, ct);
            if (ct.IsCancellationRequested || _semanticClosed) return;
            _answer = result;
            ShowAnswer(result);
        }
        catch (OperationCanceledException)
        {
            if (!_semanticClosed && _askCts == cts) ShowAnswerState("Abgebrochen.", running: false);
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or InvalidDataException
                                       or HttpRequestException or IOException or ArgumentException
                                       or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            App.LogCrash(ex);
            if (!_semanticClosed && _askCts == cts) ShowAnswerState("Keine Antwort: " + ex.Message, running: false);
        }
        finally
        {
            if (_askCts == cts) StopAskTicker();
        }
    }

    private async Task<bool> InstallLanguageModelForAskAsync(LocalAiService ai, CancellationToken ct)
    {
        var answer = MessageBox.Show(this,
            $"Für Antworten wird das lokale Sprachmodell {ai.Model.Name} gebraucht. Es ist noch nicht installiert.\n\n" +
            $"Einmaliger Download (ca. {ai.Model.DownloadSize}) aus festen Quellen mit Prüfsumme; danach läuft alles ohne Internet " +
            "auf diesem Rechner. 8 GB RAM oder mehr empfohlen.\n\nJetzt herunterladen?",
            AppNames.App, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return false;
        ShowAnswerState($"{ai.Model.Name} wird geladen …", running: true);
        try
        {
            var progress = new Progress<LocalAiProgress>(p =>
            {
                if (!ct.IsCancellationRequested) SemanticAnswerHeader.Text = $"{p.Stage} {p.Percent:0} %";
            });
            await ai.Model.InstallAsync(progress, ct);
            return !ct.IsCancellationRequested;
        }
        catch (OperationCanceledException)
        {
            if (!_semanticClosed) ShowAnswerState("Download abgebrochen. Vollständig geprüfte Dateien bleiben erhalten.", running: false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            App.LogCrash(ex);
            if (!_semanticClosed) ShowAnswerState("Download fehlgeschlagen: " + ex.Message, running: false);
        }
        return false;
    }

    private void StartAskTicker(string stage)
    {
        _askStage = stage;
        _askStarted = DateTime.Now;
        _askTicker ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => UpdateAskProgress(), Dispatcher);
        _askTicker.Start();
        ShowAnswerState("", running: true);
        UpdateAskProgress();
    }

    private void StopAskTicker() => _askTicker?.Stop();

    private void UpdateAskProgress()
    {
        var seconds = (int)(DateTime.Now - _askStarted).TotalSeconds;
        SemanticAnswerHeader.Text = $"{_askStage} … {seconds} s";
        SemanticAnswerText.Text = seconds < 20 || _answerSources.Count == 0 ? ""
            : "Das Sprachmodell läuft auf dem Prozessor; der erste Start und lange Quellen brauchen etwas länger.";
    }

    private void ShowAnswerState(string text, bool running)
    {
        SemanticAnswerPanel.Visibility = Visibility.Visible;
        SemanticAnswerCancel.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        SemanticAnswerCopy.Visibility = Visibility.Collapsed;
        _askRunning = running;
        SemanticAskButton.IsEnabled = _semanticModelInstalled && !running;
        if (!running)
        {
            SemanticAnswerHeader.Text = "Fragen";
            SemanticAnswerText.Text = text;
            SemanticAnswerSources.ItemsSource = null;
        }
    }

    private void ShowAnswer(KnowledgeAnswerResult result)
    {
        SemanticAnswerPanel.Visibility = Visibility.Visible;
        SemanticAnswerCancel.Visibility = Visibility.Collapsed;
        SemanticAnswerCopy.Visibility = Visibility.Visible;
        _askRunning = false;
        SemanticAskButton.IsEnabled = _semanticModelInstalled;
        SemanticAnswerHeader.Text = result.Found
            ? $"Antwort aus {result.Cited.Count} {(result.Cited.Count == 1 ? "Quelle" : "Quellen")} · lokal mit {_app.LocalAi.Model.Name} · bitte an den Quellen prüfen"
            : "Keine belegte Antwort in deinen Einträgen";
        SemanticAnswerText.Text = result.Answer;
        var cited = result.Cited.ToHashSet();
        SemanticAnswerSources.ItemsSource = _answerSources.Where(s => cited.Contains(s.Number)).Select(s => new AnswerSourceChip(s)).ToList();
    }

    private void SemanticAnswerSource_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AnswerSourceChip chip) return;
        var item = SemanticItems.FirstOrDefault(i => i.Hit.Chunk.ItemKey == chip.Source.Chunk.ItemKey);
        if (item == null)
        {
            OpenChunkInKb(chip.Source.Chunk);
            return;
        }
        SemanticResults.SelectedItem = item;
        SemanticResults.ScrollIntoView(item);
    }

    private void SemanticAnswerCancel_Click(object sender, RoutedEventArgs e) => _askCts?.Cancel();

    private void SemanticAnswerClose_Click(object sender, RoutedEventArgs e)
    {
        _askCts?.Cancel();
        StopAskTicker();
        _askRunning = false;
        SemanticAskButton.IsEnabled = _semanticModelInstalled;
        SemanticAnswerPanel.Visibility = Visibility.Collapsed;
    }

    private void SemanticAnswerCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_answer == null) return;
        var cited = _answer.Cited.ToHashSet();
        var sb = new StringBuilder();
        sb.Append("Frage: ").Append(_askQuestion).Append("\n\n").Append(_answer.Answer).Append('\n');
        var used = _answerSources.Where(s => cited.Contains(s.Number)).ToList();
        if (used.Count > 0)
        {
            sb.Append("\nQuellen:\n");
            foreach (var s in used)
                sb.Append('[').Append(s.Number).Append("] ").Append(s.Chunk.Title).Append(" (")
                  .Append(SemanticChunk.SourceName(s.Chunk.Source)).Append(", ").Append(FormatHitDate(s.Chunk)).Append(")\n");
        }
        StatusLabel.Text = _app.CopyTextWithoutHistory(sb.ToString())
            ? "Antwort in die Zwischenablage kopiert."
            : "Zwischenablage ist gerade belegt.";
    }
}

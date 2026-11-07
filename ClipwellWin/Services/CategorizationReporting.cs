using System.Globalization;
using System.Text;
using ClipwellWin.Models;

namespace ClipwellWin.Services;

public sealed record FeedbackSummary(string Method, int Suggestions, int Accepted, int Corrected, int NoSuggestion, int InTopThree)
{
    public double AcceptedShare => Suggestions == 0 ? 0 : (double)Accepted / Suggestions;
    /// <summary>Accepted among the cases where a category was proposed.</summary>
    public double Precision => Accepted + Corrected == 0 ? 0 : (double)Accepted / (Accepted + Corrected);
    public double TopThreeShare => Suggestions == 0 ? 0 : (double)InTopThree / Suggestions;
}

/// <summary>Usage feedback, run history and the copyable report for the categorization evaluation.</summary>
public static class CategorizationReporting
{
    public const int MaxFeedback = 5000;
    public const int MaxRuns = 50;
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Classifies what the user did with a shown suggestion; null when no model suggestion was shown.</summary>
    public static CategorizationFeedback? Feedback(LocalAiSuggestion? suggestion, string applied, DateTime now)
    {
        if (suggestion == null || suggestion.Method.Length == 0 || applied.Trim().Length == 0) return null;
        // Practice values compare main categories, as the evaluation does.
        var category = KnowledgeCategories.MainOf(applied);
        var suggested = KnowledgeCategories.MainOf(suggestion.Category);
        var outcome = suggestion.Uncertain ? SuggestionOutcome.NoSuggestion
            : string.Equals(suggested, category, StringComparison.OrdinalIgnoreCase) ? SuggestionOutcome.Accepted
            : SuggestionOutcome.Corrected;
        var inTop = (!suggestion.Uncertain && string.Equals(suggested, category, StringComparison.OrdinalIgnoreCase))
            || suggestion.Candidates?.Any(c => string.Equals(c.Category, category, StringComparison.OrdinalIgnoreCase)) == true;
        return new CategorizationFeedback(now, suggestion.Method, outcome, inTop);
    }

    public static void Append<T>(List<T> list, T item, int max)
    {
        list.Add(item);
        if (list.Count > max) list.RemoveRange(0, list.Count - max);
    }

    public static List<FeedbackSummary> Summarize(IEnumerable<CategorizationFeedback> feedback, DateTime? since = null)
        => feedback.Where(f => since == null || f.Date >= since)
            .GroupBy(f => f.Method)
            .Select(g => new FeedbackSummary(g.Key, g.Count(), g.Count(f => f.Outcome == SuggestionOutcome.Accepted),
                g.Count(f => f.Outcome == SuggestionOutcome.Corrected), g.Count(f => f.Outcome == SuggestionOutcome.NoSuggestion),
                g.Count(f => f.InTopThree)))
            .OrderBy(s => { var i = Array.IndexOf(ClassifierMethods.All, s.Method); return i < 0 ? int.MaxValue : i; })
            .ToList();

    public static CategorizationRun ToRun(BenchmarkReport report) => new(report.Date, report.Examples, report.Categories, report.Recommended,
        report.Methods.Select(m => new CategorizationRunResult(m.Method, m.MacroF1, m.Coverage, m.SelectiveAccuracy)).ToList());

    public static string Percent(double value) => value.ToString("P0", De);

    /// <summary>Metric cells of one method in the order of the comparison table (without the method name).</summary>
    public static string[] Row(MethodReport m) =>
    [
        $"{Number(m.MacroF1)} ± {Number(m.MacroF1Spread)}", Percent(m.TopOne), Percent(m.Coverage),
        $"{Percent(m.SelectiveAccuracy)} ({Percent(m.SelectiveLow)}–{Percent(m.SelectiveHigh)})", Percent(m.TopThree),
        Percent(m.CoverageAtTenPercentError), m.UnknownRejection is { } u ? Percent(u) : "–",
        m.CalibratedThreshold is { } t ? $"{Number(t)} ({Percent(m.CalibratedCoverage)})" : "–",
        m.CalibrationError is { } e ? Number(e) : "–",
        $"{m.TrainingMilliseconds.ToString("0.0", De)} ms / {m.MicrosecondsPerDecision.ToString("0", De)} µs",
    ];
    public static string Number(double value) => value.ToString("0.00", De);

    public static string ToMarkdown(BenchmarkReport? report, string activeMethod, IReadOnlyList<FeedbackSummary> feedback, IReadOnlyList<CategorizationRun> runs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Einordnung: Auswertung der Verfahren").AppendLine();
        if (report == null) sb.AppendLine("Keine aktuelle Auswertung vorhanden.").AppendLine();
        else
        {
            sb.AppendLine($"Stand {report.Date.ToString("dd.MM.yyyy HH:mm", De)}: {report.Examples} eigene Beispiele in {report.Categories} Kategorien, "
                + $"{report.Folds}-fache Kreuzvalidierung, {report.Repeats} Wiederholungen, nahezu gleiche Kopien gruppiert.").AppendLine();
            sb.AppendLine($"Empfohlen: {ClassifierMethods.DisplayName(report.Recommended)}. Für Vorschläge aktiv: {ClassifierMethods.DisplayName(activeMethod)}.").AppendLine();
            sb.AppendLine("| Verfahren | Macro-F1 | Top-1 | beantwortet | davon richtig (95 %-Intervall) | Top-3 | Abdeckung bei max. 10 % Fehler | Unbekanntes abgewiesen | Schwelle kalibriert | ECE | Lernen / Vorschlag |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (var m in report.Methods)
                sb.AppendLine($"| {ClassifierMethods.DisplayName(m.Method)} | {string.Join(" | ", Row(m))} |");
            sb.AppendLine();
            if (report.Get(activeMethod) is { } active)
            {
                sb.AppendLine($"## Kategorien ({ClassifierMethods.DisplayName(activeMethod)})").AppendLine();
                sb.AppendLine("| Kategorie | Beispiele | Precision | Recall | F1 | häufigste Verwechslung |");
                sb.AppendLine("| --- | --- | --- | --- | --- | --- |");
                foreach (var c in active.PerCategory)
                    sb.AppendLine($"| {c.Category} | {c.Examples} | {Percent(c.Precision)} | {Percent(c.Recall)} | {Number(c.F1)} | {c.MostConfusedWith ?? "–"} |");
                sb.AppendLine();
            }
        }
        if (report?.Suspicious is { Count: > 0 } suspicious)
        {
            sb.AppendLine("## Zuordnungen zur Prüfung").AppendLine();
            sb.AppendLine("| Eintrag (Auszug) | gespeichert | Rocchio sagt |");
            sb.AppendLine("| --- | --- | --- |");
            foreach (var s in suspicious) sb.AppendLine($"| {s.Excerpt.Replace("|", "/")} | {s.Category} | {s.Suggested} |");
            sb.AppendLine();
        }
        sb.AppendLine("## Praxis: bestätigte Vorschläge").AppendLine();
        if (feedback.Count == 0) sb.AppendLine("Noch keine Vorschläge bestätigt oder korrigiert.").AppendLine();
        else
        {
            sb.AppendLine("| Verfahren | Vorschläge | angenommen | korrigiert | ohne Empfehlung | richtige unter Top-3 |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- |");
            foreach (var f in feedback)
                sb.AppendLine($"| {ClassifierMethods.DisplayName(f.Method)} | {f.Suggestions} | {f.Accepted} ({Percent(f.AcceptedShare)}) | {f.Corrected} | {f.NoSuggestion} | {Percent(f.TopThreeShare)} |");
            sb.AppendLine();
        }
        if (runs.Count > 0)
        {
            sb.AppendLine("## Verlauf").AppendLine();
            sb.AppendLine("| Datum | Beispiele | Kategorien | empfohlen | Macro-F1 Rocchio | Macro-F1 empfohlen |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- |");
            foreach (var r in runs.OrderByDescending(r => r.Date).Take(20))
                sb.AppendLine($"| {r.Date.ToString("dd.MM.yyyy HH:mm", De)} | {r.Examples} | {r.Categories} | {ClassifierMethods.DisplayName(r.Recommended)} | "
                    + $"{Number(r.Results.FirstOrDefault(x => x.Method == ClassifierMethods.Rocchio)?.MacroF1 ?? 0)} | "
                    + $"{Number(r.Results.FirstOrDefault(x => x.Method == r.Recommended)?.MacroF1 ?? 0)} |");
            sb.AppendLine();
        }
        sb.AppendLine("Macro-F1 wertet jede Kategorie gleich; nicht beantwortete Fälle zählen als verpasst. Ähnlichkeiten und Scores sind keine Wahrscheinlichkeiten; ECE nur für die logistische Regression. Verfahren, Gegenbeispiel-Gewicht und Schwelle werden auf denselben Kreuzvalidierungsdaten gewählt, die Werte des gewählten Verfahrens sind daher eher etwas zu optimistisch. Kleine Stichproben schwanken stark.");
        return sb.ToString();
    }
}

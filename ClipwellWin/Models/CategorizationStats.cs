namespace ClipwellWin.Models;

public enum SuggestionOutcome { Accepted, Corrected, NoSuggestion }

/// <summary>What happened to one shown suggestion when the user saved a category.</summary>
public sealed record CategorizationFeedback(DateTime Date, string Method, SuggestionOutcome Outcome, bool InTopThree);

public sealed record CategorizationRunResult(string Method, double MacroF1, double Coverage, double SelectiveAccuracy);

/// <summary>Summary of one benchmark run, kept for the history in the report.</summary>
public sealed record CategorizationRun(DateTime Date, int Examples, int Categories, string Recommended, List<CategorizationRunResult> Results);

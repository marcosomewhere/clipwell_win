using System.Globalization;
using System.Text;
using ClipwellWin.Models;

namespace ClipwellWin.Services;

/// <summary>Builds the Markdown overview shown on the KB page: clipboard history, exported notes and desktop recordings.</summary>
public static class KnowledgeSummaryBuilder
{
    private const int TopCount = 10;
    private const int PreviewLength = 90;
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public static string Build(IReadOnlyList<ClipboardEntry> entries, IReadOnlyList<RecordingSession> sessions, DateTime now,
        IReadOnlyList<KbNote>? notes = null)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(AppNames.KnowledgeBase).Append(" - Gesamtübersicht\n\n");
        sb.Append("Stand: ").Append(now.ToString("dd.MM.yyyy HH:mm", De)).Append("\n\n");

        AppendClipboard(sb, entries, now);
        AppendNotes(sb, notes ?? []);
        AppendRecordings(sb, sessions);
        return sb.ToString().TrimEnd() + "\n";
    }

    private static void AppendClipboard(StringBuilder sb, IReadOnlyList<ClipboardEntry> entries, DateTime now)
    {
        sb.Append("## Zwischenablage\n\n");
        if (entries.Count == 0)
        {
            sb.Append("Keine Einträge in der History.\n\n");
            return;
        }

        var pinned = entries.Count(e => e.IsPinned);
        var oldest = entries.Min(e => e.Timestamp);
        var newest = entries.Max(e => e.Timestamp);
        sb.Append("- Einträge: ").Append(entries.Count).Append(" (gepinnt: ").Append(pinned).Append(")\n");
        sb.Append("- Zeitraum: ").Append(oldest.ToString("dd.MM.yyyy", De)).Append(" bis ").Append(newest.ToString("dd.MM.yyyy", De)).Append('\n');
        sb.Append("- Nach Typ: ").Append(string.Join(", ", entries
            .GroupBy(e => e.Type)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{TypeLabel(g.Key)} {g.Count()}"))).Append('\n');

        var lastWeek = Enumerable.Range(0, 7)
            .Select(i => now.Date.AddDays(-i))
            .Select(d => (Day: d, Count: entries.Count(e => e.Timestamp.Date == d)))
            .ToList();
        sb.Append("- Letzte 7 Tage: ").Append(string.Join(", ", lastWeek.Select(d => $"{d.Day:dd.MM.} {d.Count}"))).Append("\n\n");

        var domains = entries
            .Select(DomainOf)
            .Where(d => d != null)
            .GroupBy(d => d!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Take(TopCount)
            .ToList();
        if (domains.Count > 0)
        {
            sb.Append("### Häufigste Domains\n\n");
            foreach (var g in domains) sb.Append("- ").Append(g.Key).Append(" (").Append(g.Count()).Append(")\n");
            sb.Append('\n');
        }

        // URL entries store their host as ContentKind; those are already listed under domains.
        var kinds = entries
            .Where(e => e.Type != EntryType.Url && !string.IsNullOrWhiteSpace(e.ContentKind))
            .GroupBy(e => e.ContentKind!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Take(TopCount)
            .ToList();
        if (kinds.Count > 0)
        {
            sb.Append("### Inhaltsarten\n\n");
            foreach (var g in kinds) sb.Append("- ").Append(g.Key).Append(" (").Append(g.Count()).Append(")\n");
            sb.Append('\n');
        }

        var mostUsed = entries.Where(e => e.UseCount > 0)
            .OrderByDescending(e => e.UseCount).ThenByDescending(e => e.Timestamp)
            .Take(TopCount).ToList();
        if (mostUsed.Count > 0)
        {
            sb.Append("### Meistgenutzt\n\n");
            foreach (var e in mostUsed) sb.Append("- ").Append(Preview(e)).Append(" (").Append(e.UseCount).Append("x)\n");
            sb.Append('\n');
        }

        var pinnedEntries = entries.Where(e => e.IsPinned).OrderByDescending(e => e.PinOrder).Take(TopCount * 2).ToList();
        if (pinnedEntries.Count > 0)
        {
            sb.Append("### Gepinnt\n\n");
            foreach (var e in pinnedEntries) sb.Append("- ").Append(Preview(e)).Append('\n');
            sb.Append('\n');
        }

        var recent = entries.OrderByDescending(e => e.Timestamp).Take(TopCount).ToList();
        sb.Append("### Zuletzt kopiert\n\n");
        foreach (var e in recent)
            sb.Append("- ").Append(e.Timestamp.ToString("dd.MM. HH:mm", De)).Append(" · ").Append(TypeLabel(e.Type)).Append(" · ").Append(Preview(e)).Append('\n');
        sb.Append('\n');
    }

    private static void AppendNotes(StringBuilder sb, IReadOnlyList<KbNote> notes)
    {
        sb.Append("## ").Append(AppNames.VoiceNotes).Append("\n\n");
        if (notes.Count == 0)
        {
            sb.Append($"Noch keine Sprachnotizen im {AppNames.KnowledgeBase}. Im {AppNames.VoiceNotes} über \"Ins {AppNames.KnowledgeBase}\" übernehmen.\n\n");
            return;
        }

        sb.Append("- Exportierte Notizen: ").Append(notes.Count).Append("\n\n");
        foreach (var n in notes.OrderByDescending(n => n.Created))
            sb.Append("- ").Append(n.Created.ToString("dd.MM.yyyy HH:mm", De)).Append(" · ").Append(n.Title).Append('\n');
        sb.Append('\n');
    }

    private static void AppendRecordings(StringBuilder sb, IReadOnlyList<RecordingSession> sessions)
    {
        sb.Append("## Desktop-Aufnahmen\n\n");
        if (sessions.Count == 0)
        {
            sb.Append($"Noch keine Aufnahmen. Aufnahme über den Record-Button im Popup, im {AppNames.KnowledgeBase} oder im Tray starten.\n");
            return;
        }

        var totalSnapshots = sessions.Sum(s => s.Snapshots.Count);
        var totalDuration = TimeSpan.FromTicks(sessions.Sum(s => s.Duration.Ticks));
        sb.Append("- Aufnahmen: ").Append(sessions.Count).Append('\n');
        sb.Append("- Erfasste Bildwechsel: ").Append(totalSnapshots).Append('\n');
        sb.Append("- Gesamtdauer: ").Append(RecordingMarkdown.FormatDuration(totalDuration)).Append("\n\n");

        var apps = sessions.SelectMany(s => s.Snapshots)
            .GroupBy(s => s.App, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Take(TopCount).ToList();
        if (apps.Count > 0)
        {
            sb.Append("### Häufigste Anwendungen\n\n");
            foreach (var g in apps) sb.Append("- ").Append(g.Key).Append(" (").Append(g.Count()).Append(")\n");
            sb.Append('\n');
        }

        sb.Append("### Aufnahmen im Einzelnen\n\n");
        foreach (var s in sessions.OrderByDescending(s => s.Start))
        {
            sb.Append("#### ").Append(s.Start.ToString("dd.MM.yyyy HH:mm", De))
              .Append(" · ").Append(RecordingMarkdown.FormatDuration(s.Duration))
              .Append(" · ").Append(s.Snapshots.Count).Append(" Einträge");
            if (s.IsOpen) sb.Append(" · nicht abgeschlossen");
            sb.Append("\n\n");

            var windows = s.Snapshots
                .Select(x => $"{x.App}: {x.Title}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(TopCount).ToList();
            if (windows.Count == 0) sb.Append("- (keine Bildwechsel erfasst)\n");
            foreach (var w in windows) sb.Append("- ").Append(w).Append('\n');
            var more = s.Snapshots.Select(x => $"{x.App}: {x.Title}").Distinct(StringComparer.OrdinalIgnoreCase).Count() - windows.Count;
            if (more > 0) sb.Append("- ... und ").Append(more).Append(" weitere Fenster\n");
            sb.Append('\n');
        }
    }

    internal static string? DomainOf(ClipboardEntry e)
    {
        var candidate = e.Type == EntryType.Url ? e.Content?.Trim() : e.SourceUrl;
        if (string.IsNullOrWhiteSpace(candidate)) return null;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        var host = uri.Host;
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    private static string Preview(ClipboardEntry e)
    {
        var text = e.Type == EntryType.Color ? e.HexColor ?? e.Content ?? "" : e.PreviewText;
        text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length == 0) text = "(leer)";
        return text.Length > PreviewLength ? text[..PreviewLength] + "…" : text;
    }

    private static string TypeLabel(EntryType type) => type switch
    {
        EntryType.Text => "Text",
        EntryType.Image => "Bild",
        EntryType.Url => "URL",
        EntryType.Code => "Code",
        EntryType.Color => "Farbe",
        _ => type.ToString(),
    };
}

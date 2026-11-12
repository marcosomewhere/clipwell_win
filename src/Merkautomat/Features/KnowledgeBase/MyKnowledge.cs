using System.IO;
using System.Text.Json;
using Merkautomat.Models;

namespace Merkautomat.Services;

/// <summary>One item of "Mein Wissen" with its own copy of the text, so it outlives the clipboard history limit.</summary>
public sealed class MyKnowledgeEntry
{
    /// <summary>Item key of the search index ("c:12", "n:datei.md", "r:datei.md#3", "f:id").</summary>
    public string Key { get; set; } = "";
    public SemanticSource Source { get; set; }
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    /// <summary>Latest activity of the source: last copy, capture or file change. The weight decays from here.</summary>
    public DateTime Date { get; set; }
    /// <summary>Earliest known capture; together with <see cref="Added"/> it decides how long the item has been around.</summary>
    public DateTime FirstCaptured { get; set; }
    public DateTime Added { get; set; }
    /// <summary>Last daily run in which the source still existed.</summary>
    public DateTime LastSeen { get; set; }
    public double Weight { get; set; }
    /// <summary>Was around long enough with enough weight; stays until the user removes it.</summary>
    public bool Consolidated { get; set; }
    /// <summary>Fixed by the user ("Dauerhaft behalten", added via "Zu Mein Wissen hinzufügen"): counts 100 %, no waiting time, never expires, decays or condenses.</summary>
    public bool Kept { get; set; }
    public bool Pinned { get; set; }
    public int UseCount { get; set; }
    public string Origin { get; set; } = "";

    /// <summary>A summary of condensed entries instead of a single source item; key "d:…". Never expires.</summary>
    public bool Digest { get; set; }
    /// <summary>Digest only: main category of its members, empty when they had none.</summary>
    public string Category { get; set; } = "";
    /// <summary>Digest only: how many entries went into it over time.</summary>
    public int Members { get; set; }
    /// <summary>Digest only: kept original statements with the entry they came from.</summary>
    public List<MyKnowledgeStatement> Statements { get; set; } = [];
    /// <summary>Digest only: the newest condensed entries (titles only, no content).</summary>
    public List<MyKnowledgeDigestSource> Sources { get; set; } = [];
    /// <summary>Digest only: number of condensed entries per source name.</summary>
    public Dictionary<string, int> SourceCounts { get; set; } = [];

    /// <summary>Category proposed by E5 or the language model ("Haupt › Unter"); never protects, only the user's choice does.</summary>
    public string SuggestedCategory { get; set; } = "";
    /// <summary>E5 was clear with the user's own examples; such proposals can be taken over together.</summary>
    public bool SuggestionSure { get; set; }
    public string SuggestionMethod { get; set; } = "";
    public string SuggestionReason { get; set; } = "";
    /// <summary>E5 has looked at this entry (with or without a proposal).</summary>
    public bool SuggestionChecked { get; set; }
    /// <summary>The language model has looked at this entry (title and, if needed, category).</summary>
    public bool ModelChecked { get; set; }
    /// <summary>Short title proposed by the language model; the original title stays in <see cref="Title"/>.</summary>
    public string AiTitle { get; set; } = "";
    /// <summary>0 to 1: how close the entry is in meaning to the core knowledge (Knowledgeordner, kept entries).</summary>
    public double Affinity { get; set; }
    /// <summary>Digest only: readable summary by the language model, each sentence with its statement numbers.</summary>
    public List<MyKnowledgeSummarySentence> AiSummary { get; set; } = [];
    /// <summary>Digest only: fingerprint of the statements <see cref="AiSummary"/> was written for.</summary>
    public string AiSummaryFor { get; set; } = "";

    /// <summary>An independent copy (lists included), safe to read while a background run changes the original.</summary>
    public MyKnowledgeEntry Clone()
    {
        var copy = (MyKnowledgeEntry)MemberwiseClone();
        copy.Statements = [.. Statements];
        copy.Sources = [.. Sources];
        copy.SourceCounts = new(SourceCounts);
        copy.AiSummary = [.. AiSummary];
        return copy;
    }
}

public sealed record MyKnowledgeSummarySentence(string Text, IReadOnlyList<int> Sources);

public sealed record MyKnowledgeStatement(string Text, string Key);

public sealed record MyKnowledgeDigestSource(string Key, string Title, SemanticSource Source, DateTime Date);

public sealed class MyKnowledgeState
{
    public List<MyKnowledgeEntry> Entries { get; set; } = [];
    /// <summary>Removed by the user; not taken in again while the source exists.</summary>
    public List<string> Dismissed { get; set; } = [];
    public DateTime? LastRun { get; set; }
    /// <summary>Sources still in their waiting time, with the moment Mein Wissen first saw them.</summary>
    public Dictionary<string, DateTime> Pending { get; set; } = [];
    /// <summary>One sentence per weekly review group, by group fingerprint, written by the language model.</summary>
    public Dictionary<string, MyKnowledgeSummarySentence> ReviewSentences { get; set; } = [];
    /// <summary>Fingerprint of the category list the open proposals were made for.</summary>
    public string SuggestionTaxonomy { get; set; } = "";
}

/// <summary>A source item offered to the daily run.</summary>
public sealed record MyKnowledgeCandidate(string Key, SemanticSource Source, string Title, string Text, DateTime Date,
    DateTime FirstCaptured, bool Pinned = false, int UseCount = 0, string Origin = "");

/// <summary>What protects an item from expiring, looked up per item key.</summary>
/// <param name="Condense">Expiring entries are condensed into digests instead of being dropped.</param>
/// <param name="MaturityHours">Waiting time: an item shows up only after this long in the knowledge base.</param>
/// <param name="Affinity">Closeness to the core knowledge per item key (0 to 1), from <see cref="MyKnowledgeAi.Affinity"/>.</param>
public sealed record MyKnowledgeContext(IReadOnlyDictionary<string, BrainAnnotation>? Annotations,
    IReadOnlySet<string> CollectionKeys, int RetentionDays, int ClipboardHalfLifeDays, bool Condense = true, int MaturityHours = 0,
    IReadOnlyDictionary<string, double>? Affinity = null)
{
    public bool HasCategory(string key) => !KnowledgeCategories.Effective(Annotations, key).IsEmpty;
    public string Category(string key) => KnowledgeCategories.Effective(Annotations, key).ToString();
    public bool InCollection(string key) => CollectionKeys.Contains(key);
    /// <summary>Saved category; a digest without one keeps the category its members shared.</summary>
    public string CategoryOf(MyKnowledgeEntry e) => e.Digest && !HasCategory(e.Key) ? e.Category : Category(e.Key);
    public string MainOf(MyKnowledgeEntry e) => e.Digest && !HasCategory(e.Key) ? e.Category : KnowledgeCategories.Effective(Annotations, e.Key).Main;
}

public sealed record MyKnowledgeRun(int Added, int Updated, int Consolidated, int Expired, int Total, int Condensed = 0, int Waiting = 0);

public sealed record MyKnowledgeProfileRow(string Category, int Count, double Weight, double Share, DateTime Latest);

/// <summary>
/// "Mein Wissen": once a day every source item is weighted and taken in with a copy of its text. The source decides
/// the base weight (Knowledgeordner, Sprachnotizen, Bildschirmaufnahmen, Zwischenablage); clipboard entries and
/// recordings lose weight with age. Items that stay relevant for <see cref="ConsolidateDays"/> days are consolidated
/// and kept; everything else without category, collection or manual keep is condensed after the retention period
/// into a digest per month and category (or source): selected original statements survive, details are lost.
/// Knowledgeordner files have no retention and are never condensed; they follow the folder.
/// </summary>
public static class MyKnowledge
{
    public const string FileName = "my-knowledge.json";
    public const int MaxTextChars = 12000;
    /// <summary>Below this weight a new item is not taken in.</summary>
    public const double MinWeight = 0.05;
    /// <summary>From this weight on an item does not expire, whatever its age.</summary>
    public const double ProtectWeight = 0.25;
    public const double ConsolidateWeight = 0.4;
    public const int ConsolidateDays = 14;
    public const int RecordingHalfLifeDays = 30;

    public static double BaseWeight(SemanticSource source) => source switch
    {
        SemanticSource.KnowledgeFolder => 1.0,
        SemanticSource.Note => 0.8,
        SemanticSource.Recording => 0.5,
        _ => 0.35,
    };

    /// <summary>Days after which the weight halves; null for sources that do not decay.</summary>
    public static double? HalfLifeDays(SemanticSource source, int clipboardHalfLifeDays) => source switch
    {
        SemanticSource.Clipboard => Math.Clamp(clipboardHalfLifeDays, 1, 365),
        SemanticSource.Recording => RecordingHalfLifeDays,
        _ => null,
    };

    public const int MaxDigestStatements = 15;
    public const int MaxDigestSources = 200;

    public static double Weight(MyKnowledgeEntry e, DateTime now, MyKnowledgeContext context)
    {
        if (e.Kept) return 1;
        // A digest stands for many entries but only keeps a selection: modest, steady and growing with its size.
        if (e.Digest) return Math.Min(0.6, 0.3 + 0.05 * Math.Log2(1 + e.Members));
        var weight = BaseWeight(e.Source);
        if (context.HasCategory(e.Key)) weight *= 1.25;
        if (context.InCollection(e.Key)) weight *= 1.25;
        // Copying an entry again is the clearest sign that it is worth something.
        if (e.Source == SemanticSource.Clipboard && e.UseCount > 0) weight *= 1 + 0.2 * Math.Min(e.UseCount, 5);
        if (!e.Pinned && HalfLifeDays(e.Source, context.ClipboardHalfLifeDays) is { } halfLife)
            weight *= Math.Pow(0.5, Math.Max(0, (now - e.Date).TotalDays) / halfLife);
        weight *= Maturity(e, now, context);
        // Close in meaning to the core knowledge: what belongs to it rises, noise sinks.
        if (context.Affinity != null && context.Affinity.TryGetValue(e.Key, out var near)) weight *= 1 + MyKnowledgeAi.AffinityBoost * near;
        return Math.Clamp(weight, 0, 1);
    }

    /// <summary>Weight share of a young item right after its waiting time.</summary>
    public const double YoungFactor = 0.3;
    /// <summary>Days after the waiting time until an item counts fully.</summary>
    public const int RampDays = 14;

    /// <summary>
    /// How much an item already counts for its time in the knowledge base: <see cref="YoungFactor"/> right after the
    /// waiting time, rising evenly to 1 over <see cref="RampDays"/> days. Time runs from the first capture or the first
    /// sighting, whichever is earlier, so later copies or file edits do not make an item young again.
    /// </summary>
    public static double Maturity(MyKnowledgeEntry e, DateTime now, MyKnowledgeContext context)
    {
        var since = Earlier(e.FirstCaptured, e.Added);
        if (since == default) return 1;
        var days = (now - since).TotalDays - Math.Max(0, context.MaturityHours) / 24.0;
        return YoungFactor + (1 - YoungFactor) * Math.Clamp(days / RampDays, 0, 1);
    }

    /// <summary>Still in the waiting time: not shown in Mein Wissen yet.</summary>
    private static bool IsYoung(DateTime since, DateTime now, MyKnowledgeContext context)
        => context.MaturityHours > 0 && since != default && (now - since).TotalHours < context.MaturityHours;

    /// <summary>Never expires: digests, Knowledgeordner files (no retention; they follow the folder), kept, consolidated, categorized and collected items.</summary>
    public static bool IsProtected(MyKnowledgeEntry e, MyKnowledgeContext context)
        => e.Digest || e.Source == SemanticSource.KnowledgeFolder || e.Kept || e.Consolidated
           || context.HasCategory(e.Key) || context.InCollection(e.Key);

    public static bool IsExpired(MyKnowledgeEntry e, double weight, DateTime now, MyKnowledgeContext context)
        => context.RetentionDays > 0 && !IsProtected(e, context) && weight < ProtectWeight
           && (now - e.Date).TotalDays > context.RetentionDays;

    /// <summary>Days until an unprotected item leaves; null when it is protected or retention is off.</summary>
    public static int? DaysLeft(MyKnowledgeEntry e, DateTime now, MyKnowledgeContext context)
    {
        if (context.RetentionDays <= 0 || IsProtected(e, context) || e.Weight >= ProtectWeight) return null;
        return Math.Max(0, (int)Math.Ceiling(context.RetentionDays - (now - e.Date).TotalDays));
    }

    // ---- Daily run -------------------------------------------------------------------------------------------

    /// <summary>
    /// Takes in new items, refreshes text and weight of known ones, consolidates and expires. Knowledgeordner files
    /// follow the folder: a file that is gone leaves. Copies of other sources stay when the source disappears.
    /// </summary>
    public static MyKnowledgeRun Update(MyKnowledgeState state, IEnumerable<MyKnowledgeCandidate> candidates,
        MyKnowledgeContext context, DateTime now)
    {
        var byKey = new Dictionary<string, MyKnowledgeEntry>(StringComparer.Ordinal);
        foreach (var e in state.Entries) byKey.TryAdd(e.Key, e);
        var dismissed = state.Dismissed.ToHashSet(StringComparer.Ordinal);
        var all = candidates.ToList();
        // Every existing source counts as present, also one skipped below as a repeated text; otherwise its
        // dismissal would be forgotten and the item could come back.
        var present = all.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        var pending = new Dictionary<string, DateTime>(state.Pending, StringComparer.Ordinal);
        int added = 0, updated = 0, consolidated = 0, expired = 0;

        foreach (var c in Distinct(all))
        {
            if (dismissed.Contains(c.Key)) continue;
            if (byKey.TryGetValue(c.Key, out var known))
            {
                Apply(known, c, now);
                updated++;
                continue;
            }
            // New items wait until they have been in the knowledge base long enough.
            var seen = pending.TryGetValue(c.Key, out var first) ? first : now;
            var since = Earlier(Earlier(c.FirstCaptured, c.Date), seen);
            if (IsYoung(since, now, context))
            {
                pending[c.Key] = seen;
                continue;
            }
            pending.Remove(c.Key);
            var entry = new MyKnowledgeEntry { Key = c.Key, Source = c.Source, Added = now, FirstCaptured = since };
            Apply(entry, c, now);
            var weight = Weight(entry, now, context);
            if (!context.HasCategory(c.Key) && !context.InCollection(c.Key)
                && (weight < MinWeight || IsExpired(entry, weight, now, context))) continue;
            byKey[c.Key] = entry;
            added++;
        }

        var kept = new List<MyKnowledgeEntry>(byKey.Count);
        var condense = new List<MyKnowledgeEntry>();
        foreach (var e in byKey.Values)
        {
            if (e.Source == SemanticSource.KnowledgeFolder && !e.Digest && !present.Contains(e.Key)) { expired++; continue; }
            // Taken in before a waiting time was set: back to waiting, unless the user kept it.
            if (!e.Digest && !e.Kept && IsYoung(Earlier(e.FirstCaptured, e.Added), now, context))
            {
                if (present.Contains(e.Key)) pending[e.Key] = Earlier(e.FirstCaptured, e.Added);
                continue;
            }
            if (context.Affinity != null) e.Affinity = context.Affinity.GetValueOrDefault(e.Key);
            e.Weight = Weight(e, now, context);
            var around = now - Earlier(e.FirstCaptured, e.Added);
            if (!e.Digest && !e.Consolidated && e.Weight >= ConsolidateWeight && around.TotalDays >= ConsolidateDays)
            {
                e.Consolidated = true;
                consolidated++;
            }
            if (IsExpired(e, e.Weight, now, context))
            {
                if (context.Condense && e.Source != SemanticSource.KnowledgeFolder) condense.Add(e);
                else expired++;
                continue;
            }
            kept.Add(e);
        }

        // Expiring entries of one month and one category (or, without category, one source) share a digest.
        foreach (var group in condense.GroupBy(e => (Key: DigestKey(e, context), Month: new DateTime(e.Date.Year, e.Date.Month, 1))))
        {
            var main = context.MainOf(group.First());
            var label = main.Length > 0 ? main : SemanticChunk.SourceName(group.First().Source);
            var target = kept.FirstOrDefault(k => k.Key == group.Key.Key);
            var digest = Condense(target, group.ToList(), group.Key.Key,
                $"Zusammenfassung {label} · {group.Key.Month.ToString("MMMM yyyy", De)}", main, now);
            digest.Weight = Weight(digest, now, context);
            if (target == null) kept.Add(digest);
        }

        state.Entries = kept.OrderByDescending(e => e.Weight).ThenByDescending(e => e.Date).ThenBy(e => e.Key, StringComparer.Ordinal).ToList();
        // A dismissed key whose source is gone cannot come back; forgetting it keeps the list small.
        state.Dismissed = state.Dismissed.Where(present.Contains).Distinct(StringComparer.Ordinal).ToList();
        // Waiting sources that disappeared before their time are forgotten.
        state.Pending = pending.Where(p => present.Contains(p.Key) && !dismissed.Contains(p.Key))
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        state.LastRun = now;
        return new MyKnowledgeRun(added, updated, consolidated, expired, kept.Count, condense.Count, state.Pending.Count);
    }

    private static readonly System.Globalization.CultureInfo De = System.Globalization.CultureInfo.GetCultureInfo("de-DE");

    private static string DigestKey(MyKnowledgeEntry e, MyKnowledgeContext context)
    {
        var main = context.MainOf(e);
        return $"d:{e.Date:yyyy-MM}:" + (main.Length > 0 ? "k:" + main.ToLowerInvariant() : "s:" + e.Source);
    }

    // ---- Condensing ------------------------------------------------------------------------------------------

    /// <summary>
    /// Merges entries (and digests) into <paramref name="target"/> or a new digest: only selected original statements
    /// and the titles of the newest entries remain. Earlier statements of the target compete with the new texts, so
    /// each merge may drop more detail.
    /// </summary>
    public static MyKnowledgeEntry Condense(MyKnowledgeEntry? target, IReadOnlyList<MyKnowledgeEntry> members, string key,
        string title, string category, DateTime now)
    {
        var digest = target ?? new MyKnowledgeEntry { Key = key, Digest = true, Added = now, Category = category };
        var texts = new List<(string Text, string Key)>(digest.Statements.Select(s => (s.Text, s.Key)));
        var sources = new List<MyKnowledgeDigestSource>(digest.Sources);
        foreach (var m in members)
        {
            if (m.Digest)
            {
                texts.AddRange(m.Statements.Select(s => (s.Text, s.Key)));
                sources.AddRange(m.Sources);
                foreach (var (name, count) in m.SourceCounts) digest.SourceCounts[name] = digest.SourceCounts.GetValueOrDefault(name) + count;
                digest.Members += m.Members;
            }
            else
            {
                texts.Add((m.Text, m.Key));
                sources.Add(new MyKnowledgeDigestSource(m.Key, m.Title, m.Source, m.Date));
                var name = SemanticChunk.SourceName(m.Source);
                digest.SourceCounts[name] = digest.SourceCounts.GetValueOrDefault(name) + 1;
                digest.Members++;
            }
            digest.Date = m.Date > digest.Date ? m.Date : digest.Date;
            digest.FirstCaptured = Earlier(digest.FirstCaptured, Earlier(m.FirstCaptured, m.Date));
        }
        digest.Statements = BrainKnowledge.KeyStatements(texts.Select(t => t.Text).ToList(), MaxDigestStatements, 3)
            .Select(p => new MyKnowledgeStatement(p.Text, texts[p.Source].Key)).ToList();
        digest.Sources = sources.DistinctBy(s => s.Key).OrderByDescending(s => s.Date).Take(MaxDigestSources).ToList();
        digest.Source = digest.Sources.GroupBy(s => s.Source).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        digest.Title = title;
        digest.AiSummary = [];
        digest.AiSummaryFor = "";
        digest.LastSeen = now;
        Render(digest);
        return digest;
    }

    /// <summary>Writes the digest text; a summary by the language model appears only while it fits the current statements.</summary>
    public static void Render(MyKnowledgeEntry digest)
    {
        var sb = new System.Text.StringBuilder();
        var counts = string.Join(", ", digest.SourceCounts.OrderByDescending(c => c.Value).Select(c => $"{c.Key} {c.Value}"));
        sb.Append($"Verdichtet aus {digest.Members} Einträgen ({counts}), {digest.FirstCaptured.ToString("dd.MM.yyyy", De)} bis ")
            .AppendLine($"{digest.Date.ToString("dd.MM.yyyy", De)}. Ausgewählte Originalaussagen; die Einzelheiten sind nicht mehr gespeichert.")
            .AppendLine();
        if (digest.AiSummary.Count > 0 && digest.AiSummaryFor == MyKnowledgeAi.Fingerprint(digest.Statements))
        {
            sb.AppendLine("## Zusammenfassung").AppendLine()
              .AppendLine("Lokal vom Sprachmodell formuliert; jede Aussage belegt mit den Nummern der Kernaussagen.").AppendLine();
            foreach (var s in digest.AiSummary) sb.Append(s.Text).Append(" [").Append(string.Join(", ", s.Sources)).AppendLine("]");
            sb.AppendLine();
        }
        sb.AppendLine("## Kernaussagen").AppendLine();
        if (digest.Statements.Count == 0) sb.AppendLine("Keine ausreichend langen Aussagen; nur die Titel sind erhalten.");
        for (var i = 0; i < digest.Statements.Count; i++) sb.Append(i + 1).Append(". ").AppendLine(digest.Statements[i].Text);
        sb.AppendLine().AppendLine("## Enthaltene Einträge").AppendLine();
        foreach (var s in digest.Sources.Take(40))
            sb.AppendLine($"- {s.Title} · {SemanticChunk.SourceName(s.Source)} · {s.Date.ToString("dd.MM.yyyy", De)}");
        var more = digest.Members - Math.Min(40, digest.Sources.Count);
        if (more > 0) sb.AppendLine($"- … und {more} weitere");
        digest.Text = sb.ToString().TrimEnd() + "\n";
    }

    /// <summary>
    /// The user condenses the selection into one digest. Knowledgeordner files are left out. Condensed single entries
    /// are dismissed, so they do not come back while their source exists.
    /// </summary>
    public static MyKnowledgeEntry? Summarize(MyKnowledgeState state, IReadOnlyCollection<string> keys, MyKnowledgeContext context, DateTime now)
    {
        var set = keys.ToHashSet(StringComparer.Ordinal);
        var members = state.Entries.Where(e => set.Contains(e.Key) && e.Source != SemanticSource.KnowledgeFolder).ToList();
        if (members.Count < 2) return null;
        var mains = members.Select(context.MainOf).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var main = mains.Count == 1 ? mains[0] : "";
        var heaviest = members.OrderByDescending(m => m.Weight).First();
        var title = main.Length > 0 ? $"Zusammenfassung {main}" : $"Zusammenfassung: {heaviest.Title}";
        if (title.Length > 120) title = title[..120];
        var digest = Condense(null, members, "d:m:" + Guid.NewGuid().ToString("N"), title, main, now);
        digest.Weight = Weight(digest, now, context);
        state.Entries.RemoveAll(e => members.Contains(e));
        state.Entries.Add(digest);
        state.Dismissed = state.Dismissed.Union(members.Where(m => !m.Digest).Select(m => m.Key), StringComparer.Ordinal).ToList();
        return digest;
    }

    private static DateTime Earlier(DateTime a, DateTime b) => a == default ? b : b == default ? a : a < b ? a : b;

    private static void Apply(MyKnowledgeEntry e, MyKnowledgeCandidate c, DateTime now)
    {
        e.Source = c.Source;
        e.Title = c.Title.Length > 200 ? c.Title[..200] : c.Title;
        e.Text = c.Text.Length > MaxTextChars ? c.Text[..MaxTextChars] : c.Text;
        if (c.Date > e.Date) e.Date = c.Date;
        e.FirstCaptured = Earlier(e.FirstCaptured, Earlier(c.FirstCaptured, c.Date));
        e.Pinned = c.Pinned;
        e.UseCount = c.UseCount;
        e.Origin = c.Origin;
        e.LastSeen = now;
    }

    /// <summary>Recordings and the clipboard repeat the same text often; only the newest of identical texts counts.</summary>
    private static IEnumerable<MyKnowledgeCandidate> Distinct(IEnumerable<MyKnowledgeCandidate> candidates)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in candidates.OrderByDescending(c => c.Date).ThenBy(c => c.Key, StringComparer.Ordinal))
        {
            if ((c.Source is SemanticSource.Clipboard or SemanticSource.Recording) && !seen.Add(c.Source + "\n" + Normalize(c.Text)))
                continue;
            yield return c;
        }
    }

    private static string Normalize(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>One candidate per item key from the chunks of the search index sources, in chunk order.</summary>
    public static List<MyKnowledgeCandidate> Candidates(IEnumerable<SemanticChunk> chunks, IReadOnlyDictionary<long, ClipboardEntry> entries)
        => chunks.GroupBy(c => c.ItemKey, StringComparer.Ordinal).Select(g =>
        {
            var first = g.First();
            var text = string.Join("\n\n", g.Select(c => c.Text));
            entries.TryGetValue(first.EntryId, out var entry);
            var date = first.Date;
            if (entry?.LastUsedAt is { } used && used > date) date = used;
            var captured = first.Metadata?.CapturedAt is { } at && at < first.Date ? at : first.Date;
            var origin = first.Metadata?.Application ?? first.Domain ?? "";
            if (first.Source == SemanticSource.KnowledgeFolder) origin = first.FilePath ?? origin;
            return new MyKnowledgeCandidate(g.Key, first.Source, first.Title, text, date, captured,
                entry?.IsPinned == true, entry?.UseCount ?? 0, origin);
        }).ToList();

    /// <summary>
    /// A clipboard entry directly, for entries the search index leaves out as too short; a deliberate choice of the
    /// user still counts. Null without text (an image without recognized text).
    /// </summary>
    public static MyKnowledgeCandidate? Candidate(ClipboardEntry e)
    {
        var text = e.KnowledgeText();
        if (text.Length == 0) return null;
        var date = e.LastUsedAt is { } used && used > e.Timestamp ? used : e.Timestamp;
        var captured = e.Metadata?.CapturedAt is { } at && at < e.Timestamp ? at : e.Timestamp;
        return new MyKnowledgeCandidate($"c:{e.Id}", SemanticSource.Clipboard, KnowledgeExport.ClipboardTitle(e), text, date, captured,
            e.IsPinned, e.UseCount, e.Metadata?.Application ?? "");
    }

    // ---- User actions ----------------------------------------------------------------------------------------

    /// <summary>
    /// "Zu Mein Wissen hinzufügen": known entries and digests matching the keys or prefixes become fixed; sources
    /// not yet in Mein Wissen (waiting, dismissed or too light) are taken in at once from <paramref name="candidates"/>.
    /// Returns how many entries are fixed afterwards.
    /// </summary>
    public static int Fix(MyKnowledgeState state, IReadOnlyCollection<string> keys, IReadOnlyCollection<string>? prefixes,
        IEnumerable<MyKnowledgeCandidate> candidates, DateTime now)
    {
        var set = keys.ToHashSet(StringComparer.Ordinal);
        bool Match(string key) => set.Contains(key) || (prefixes ?? []).Any(p => key.StartsWith(p, StringComparison.Ordinal));
        var byKey = new Dictionary<string, MyKnowledgeEntry>(StringComparer.Ordinal);
        foreach (var e in state.Entries) byKey.TryAdd(e.Key, e);
        var fixedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in candidates.Where(c => Match(c.Key)))
        {
            if (!byKey.TryGetValue(c.Key, out var entry))
            {
                var since = state.Pending.TryGetValue(c.Key, out var seen) ? Earlier(Earlier(c.FirstCaptured, c.Date), seen) : Earlier(c.FirstCaptured, c.Date);
                entry = new MyKnowledgeEntry { Key = c.Key, Source = c.Source, Added = now, FirstCaptured = since };
                byKey[c.Key] = entry;
                state.Entries.Add(entry);
            }
            Apply(entry, c, now);
            fixedKeys.Add(c.Key);
        }
        foreach (var e in state.Entries.Where(e => Match(e.Key))) fixedKeys.Add(e.Key);
        foreach (var key in fixedKeys)
        {
            var e = byKey[key];
            e.Kept = true;
            e.Weight = 1;
        }
        state.Dismissed.RemoveAll(fixedKeys.Contains);
        foreach (var key in fixedKeys) state.Pending.Remove(key);
        state.Entries = state.Entries.OrderByDescending(e => e.Weight).ThenByDescending(e => e.Date).ThenBy(e => e.Key, StringComparer.Ordinal).ToList();
        return fixedKeys.Count;
    }

    /// <summary>Removes items and remembers them so the next run does not take them in again.</summary>
    public static int Dismiss(MyKnowledgeState state, IReadOnlyCollection<string> keys)
    {
        var set = keys.ToHashSet(StringComparer.Ordinal);
        var removed = state.Entries.RemoveAll(e => set.Contains(e.Key));
        state.Dismissed = state.Dismissed.Union(set, StringComparer.Ordinal).ToList();
        return removed;
    }

    /// <summary>The source was deleted in Merkautomat: the copy goes too. Prefixes cover all snapshots of a recording.</summary>
    public static int Forget(MyKnowledgeState state, IReadOnlyCollection<string> keys, IReadOnlyCollection<string>? prefixes = null)
    {
        var set = keys.ToHashSet(StringComparer.Ordinal);
        bool Match(string key) => set.Contains(key) || (prefixes ?? []).Any(p => key.StartsWith(p, StringComparison.Ordinal));
        state.Dismissed.RemoveAll(Match);
        var removed = state.Entries.RemoveAll(e => !e.Digest && Match(e.Key));
        // Statements and titles from a deleted source leave the digests too; an emptied digest goes.
        foreach (var digest in state.Entries.Where(e => e.Digest).ToList())
        {
            var statements = digest.Statements.RemoveAll(s => Match(s.Key));
            var sources = digest.Sources.RemoveAll(s => Match(s.Key));
            if (statements + sources == 0) continue;
            // The model's summary may repeat the deleted content; it is written again from the remaining statements.
            digest.AiSummary = [];
            digest.AiSummaryFor = "";
            removed++;
            if (digest.Statements.Count == 0 && digest.Sources.Count == 0) state.Entries.Remove(digest);
            else Render(digest);
        }
        // Review sentences may quote deleted entries; they are written again on the next background run.
        if (removed > 0) state.ReviewSentences.Clear();
        return removed;
    }

    // ---- Reading ---------------------------------------------------------------------------------------------

    /// <summary>Main categories by summed weight: what the knowledge is mostly about.</summary>
    public static List<MyKnowledgeProfileRow> Profile(IReadOnlyList<MyKnowledgeEntry> entries, MyKnowledgeContext context)
    {
        var total = entries.Sum(e => e.Weight);
        return entries.GroupBy(context.MainOf, StringComparer.OrdinalIgnoreCase)
            .Select(g => new MyKnowledgeProfileRow(g.Key.Length == 0 ? "Ohne Kategorie" : g.Key, g.Count(), g.Sum(e => e.Weight),
                total > 0 ? g.Sum(e => e.Weight) / total : 0, g.Max(e => e.Date)))
            .OrderByDescending(r => r.Weight).ThenBy(r => r.Category, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>All words must appear in title or text; title hits and heavier items rank first.</summary>
    public static List<MyKnowledgeEntry> Search(IEnumerable<MyKnowledgeEntry> entries, string query)
    {
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return entries.ToList();
        return entries
            .Select(e => (Entry: e, TitleHits: words.Count(w => e.Title.Contains(w, StringComparison.OrdinalIgnoreCase)),
                All: words.All(w => e.Title.Contains(w, StringComparison.OrdinalIgnoreCase) || e.Text.Contains(w, StringComparison.OrdinalIgnoreCase))))
            .Where(x => x.All)
            .OrderByDescending(x => x.TitleHits).ThenByDescending(x => x.Entry.Weight).ThenByDescending(x => x.Entry.Date)
            .Select(x => x.Entry).ToList();
    }

    // ---- Storage ---------------------------------------------------------------------------------------------

    public static MyKnowledgeState Load(string path)
    {
        if (!File.Exists(path)) return new();
        return JsonSerializer.Deserialize<MyKnowledgeState>(SecureFile.ReadAllText(path)) ?? new();
    }

    public static void Save(string path, MyKnowledgeState state)
        => SecureFile.WriteAllTextAtomic(path, JsonSerializer.Serialize(state));
}

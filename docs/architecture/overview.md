# Merkautomat - Architecture

## Stack

| Layer | Technology |
| --- | --- |
| Framework | .NET 10, WPF, `net10.0-windows10.0.19041.0` |
| UI library | WPF-UI 4.3.0 |
| Tray | WinForms `NotifyIcon` |
| Database | `Microsoft.Data.Sqlite.Core 10.0.14`, SQLite WAL with FTS5, encrypted with SQLite3 Multiple Ciphers 2.6.1 (`SQLite3MC.PCLRaw.bundle`, ChaCha20-Poly1305) |
| OCR | `Windows.Media.Ocr.OcrEngine` |
| YAML | `YamlDotNet 18.1.0` (detection and validation) |
| Dictation | `Whisper.net 1.9.1` with bundled `ggml-base.bin` |
| Embeddings | `Microsoft.ML.OnnxRuntime 1.31.1`, `Microsoft.ML.Tokenizers 2.0.1`, multilingual-e5-small bundled as `models\e5-small` |
| Local categorization | Bundled E5 for existing categories, or optional IBM Granite 4.2 3B Q4_K_M via pinned llama.cpp b12110 Windows CPU server; no new NuGet package |
| Tests | xUnit v3 4.0.2 on Microsoft Testing Platform, 402 tests |

## Project Layout

`E5Categorization` compares category names (`Thema: <category>`) and up to eight unique 700-character excerpts using query-prefixed vectors on both sides, as recommended by E5 for classification. `SemanticIndex.EmbedForCategorizationAsync` uses the existing lazy model and model lock; passage vectors from the search index are not mixed into this query/query comparison. Up to 4096 vectors are cached in memory by text hash. Category changes are evaluated anew; no predicted labels or graph edges become training examples. Saved manual assignments (`AppSettings.BrainAnnotations`, snapshot passed into `BrainKnowledgeDialog`) are the only learning data: with at least three categories, each category with two or more saved examples gets a Rocchio prototype (normalized name vector plus the mean of up to eight newest example texts from `SemanticIndex.ItemTexts`, embedded with the query prefix, at most 400 examples). Prototypes and excerpts are compared after subtracting the prototype mean; a suggestion needs a centered margin >= 0.08 and raw cosine >= 0.80. Selected sources are excluded from the examples. The math lives in `RocchioModel`: up to 16 newest examples per category; broad categories (6+ examples) may get up to three sub-prototypes (spherical k-means, farthest-point seeding, clusters of 3+) scored by their best sub-prototype, while the centering mean still uses one prototype per category. Sub-prototypes are enabled only when `RocchioModel.Evaluate` (leave-one-out over all saved assignments, near copies with cosine >= 0.98 excluded from training) shows macro-F1 above single-prototype Rocchio by more than 0.01 with no lower selective accuracy; the result is cached per example set. Suggestions carry the top three candidates (relative scores, also on abstention) and name the nearest own example. Classifiers share `ICategoryClassifier` (`CategoryClassifiers.cs`): `RocchioModel` (names only, name + examples, examples only, sub-prototypes), `KnnClassifier` and `LogisticClassifier` (centered features, L2, class weights, gradient descent). `ClassifierBenchmark` runs grouped, stratified 5-fold cross-validation with 3 seeded repeats (near copies in one fold) and reports macro-F1 with spread, coverage, selective accuracy, top-3, coverage at 10 % error, ECE for probabilistic output, latency, per-category scores and a confusion matrix; `Recommend` keeps Rocchio unless another method gains at least 0.02 macro-F1 without losing more than 0.02 selective accuracy. Further metrics: top-1, a 95 % Wilson interval for selective accuracy (first repetition), unknown-topic rejection (with four or more categories, up to ten categories are removed completely in turn and the share of their texts without a suggestion is counted) and `Calibrate` (40+ examples: smallest margin/probability threshold whose accepted cases reach a lower Wilson bound of 0.90, clamped to 0.04-0.25 for Rocchio margins and 0.35-0.95 for probabilities). `E5Categorization` applies the calibrated threshold of the active method whenever a benchmark for the current examples exists. Rocchio variants with hard negatives subtract gamma (0.1/0.2) times the mean of the five most similar foreign examples from the positive mean. Ablations: `EuclideanRocchio` (nearest unnormalized centroid by squared Euclidean distance, no name prior, no centering) and the robust variant (`RocchioModel.TrimmedMean`: from five examples on, the 20 % least similar to the plain mean are dropped). `BenchmarkReport.Suspicious` lists cases that Rocchio confidently assigned to another category in every repetition; `E5Categorization.BenchmarkAsync` maps them back to item keys and excerpts and records embedding time separately from the per-method training time. `E5Categorization` applies `AppSettings.CategorizationMethod` ("auto" uses the cached benchmark winner; up to 150 examples inline, else in the background with Rocchio meanwhile). `CategorizationReporting` turns saved suggestions into usage feedback (accepted/corrected/no suggestion, top-3) and builds the Markdown report; `ClassifierReportWindow` shows chart, tables, confusion heatmap, usage and history and stores the chosen method.

Category hierarchy: `BrainAnnotation` carries `Category` (main) and optional `Subcategory`; `AppSettings.KnowledgeCategories` defines main categories with descriptions and subcategories. `KnowledgeCategories` parses and combines paths ("Haupt › Unter"), assigns, renames/merges, moves and deletes, and resolves recording snapshots (`r:file#n`) to the recording key (`r:file`) when they have no own category. Dialogs, `CategorizationQueue` and classifiers pass categories as path strings. `E5Categorization.SuggestAsync` is hierarchical: main categories first (benchmark-selected method; benchmark and practice feedback compare main categories only), then `SuggestSubcategoryAsync`/`RankSubcategories` compare only the subcategories and saved examples of the chosen main category (centered Rocchio from three subcategories, agreement of the 2 or 3 nearest own examples for two, names only otherwise; a single subcategory is offered but never chosen). Category descriptions are appended to the name text ("Thema: Name. Beschreibung"). `LocalAiCategorization` passes `existing_subcategories` to Granite and reads an optional `subcategory`. Library UI in `KnowledgeBaseWindow.Categories.cs` with `AssignCategoryDialog` and `CategoryManagerDialog`; in the Brain, `BrainOptions.GroupByCategory` replaces topic clusters with main-category clusters plus `GraphCluster.Subgroups` and adds `GraphEdgeKind.Category` chains (not used for topic clustering).

`CategoryDiscovery` groups up to 600 newest uncategorized items (mean of their passage vectors, centered and normalized) with spherical k-means (k = sqrt(n/2), 1-15, farthest-point seeding, groups of 3+), asks `LocalAiService.SuggestWithGraniteAsync` (independent of the E5/Granite selection) to name each group from its eight most central members while passing existing and already proposed names, and merges equally named groups. With saved assignments and the search model, `SuggestWithGraniteAsync(..., e5First: true)` asks E5 first (`CategorizationCascade`): a confident E5 answer from own examples (`LocalAiSuggestion.Learned`) settles the group without starting Granite, otherwise the top three E5 candidates go to Granite as `likely_categories`; `CategoryProposal.Method` records which one named the group. The categorization window uses the same cascade (explicit re-suggest forces Granite); `BrainKnowledgeDialog` only passes the hints, because it needs Granite's title. `CategoryMatching` ranks, for one category, the uncategorized items whose centered Rocchio score (examples-only prototypes on the mean passage vectors of the index, no new embeddings, three or more categories with two examples each) puts that category first. `CategoryDiscoveryWindow` offers the Granite download when missing, shows progress, lets the user rename or deselect proposals and applies them through `PersistCategory` after confirmation.

`CategorizeDialog` runs a categorization pass over a `CategorizationQueue` (items with saved category, state and last suggestion; `Version` grows with every saved assignment so E5 suggestions are recomputed with the new examples, Granite suggestions are kept). Entry points live in `KnowledgeBaseWindow.Categorize.cs`: Brain node (node plus visible neighbours, semantic/summary neighbours marked, strongest first) or topic (all members), all uncategorized indexed items, marked search results, and the library selection (recordings expand to their indexed snapshots). Texts come from `SemanticIndex.ItemTexts`; missing keys trigger one index sync first. A single semaphore serializes model calls; requests for entries the user already left are dropped instead of cancelled, so Granite is not restarted. Each confirmation goes through `PersistCategory` (keeps the annotation name, rolls back on save errors); the Brain is rebuilt after the dialog when anything changed. At least two and at most 200 categories are required. Every evaluated excerpt must agree on the best category with cosine >= 0.80 and a >= 0.025 margin. Otherwise the service abstains; these thresholds are heuristics, not probabilities. E5 suggestions leave the title empty so the dialog preserves the user's title. Selection uses `AppSettings.LocalAiModel = e5-categories`; `LocalAiService` routes to E5 before any Granite setup or inference.

`LocalAiModel` downloads pinned files into `data\models\granite-4.2-3b`, verifies length/SHA256 and only publishes complete files. Model and archive are verified again before process start; runtime files are restored from the verified archive. `LocalAiService` owns one hidden llama-server process bound to 127.0.0.1 on an ephemeral port, authenticated with a random session key; its HTTP client bypasses proxies. No web UI or tools are enabled. Inherited LLAMA_* settings are removed. Requests are serialized, bounded to three minutes, cancellable and cached in memory (64 entries); idle model unload after 180 seconds, process stopped on disable/exit/cancellation.

`ProcessLoad` sets the llama-server's priority and EcoQoS (`SetProcessInformation`, `ProcessPowerThrottling`) on the running process: direct calls use below-normal priority with EcoQoS explicitly off; background calls (`background: true`) use the load from `SetBackgroundLoad` (normal when the PC is idle, idle priority plus EcoQoS otherwise) and a ten-minute limit. A direct call cancels a running background call without restarting the server and records `LastInteractiveUse`. `App.Background.cs` runs a 10-second tick: `BackgroundWork.Decide` combines the mode (`AppSettings.BackgroundWorkMode`), input idle time (`GetLastInputInfo`), `SHQueryUserNotificationState` (full screen, D3D game, presentation), `GetSystemPowerStatus` and recent direct use into Paused/Eco/Fast; hard reasons (battery, game, presentation, off) cancel the running call, soft ones let the current group finish throttled. The background job syncs the search index only while idle, builds candidates with `CategoryDiscovery.Candidates`, and runs `CategoryDiscovery.RunAsync` with Granite in background mode, saving each named group to `BackgroundDiscoveryState` (`background-discovery.json`); an interrupted run over the same item keys resumes, reusing groups with identical keys. Finished proposals are announced in the tray and the KB and handed to `CategoryDiscoveryWindow` via `App.TakePreparedDiscovery`, filtered to items still uncategorized. Optional `SetThreadExecutionState` keeps the PC awake while the job runs.

`LocalAiCategorization` limits source data to eight distinct excerpts of 700 characters and uses a non-thinking, schema-constrained JSON response. Source text is treated as data. Title/category/reason lengths and completion status are checked; uncertain responses cannot assign a category. `BrainKnowledgeDialog.LocalAi` invalidates outstanding suggestions when the selected item keys change; only the explicit apply action updates the editable fields. Existing annotation persistence remains unchanged. `SettingsWindow.LocalAi` handles model selection, installation progress, cancellation and a synthetic end-to-end test. `AppSettings.LocalAiModel` defaults to `off`; unknown values are treated as off.

```text
Merkautomat/
  App.xaml.cs                 lifecycle, tray, hotkeys, clipboard chain
  NativeMethods.cs            Win32 interop
  Models/                     persisted settings and entries
  Services/                   storage, processing, detection, image helpers, OCR, URL preview,
                              recording, notes, dictation, KB summary, semantic search
  ViewModels/                 popup and entry state
  Views/                      WPF windows plus DragGhost (drag preview) and KbInstruments (KB gauges)
Merkautomat.Tests/            unit, integration and smoke tests
```

## Main Windows

| Window | Purpose |
| --- | --- |
| `PopupWindow` | Main history popup, search, filters, bulk actions, quick note, pinboard and eyedropper entry points |
| `DetailWindow` | Text/code editor, image editor, OCR view and color details |
| `SettingsWindow` | Hotkey, theme, limits, export/import, privacy, backup and shortcuts |
| `OnboardingWindow` | First-run flow with hotkey test |
| `PinboardWindow` | Always-on-top list of pinned entries |
| `EyedropperWindow` | Transparent full-screen color picker |
| `KnowledgeBaseWindow` | KB page in the detail-window chrome: dashboard with counters, gauges and weekly review, overall Markdown summary, "Mein Wissen" section (UserControl `MyKnowledgePanel`, wired up in `KnowledgeBaseWindow.MyKnowledge.cs`), semantic search section (partial class `KnowledgeBaseWindow.Semantic.cs`: query, source chips, results, preview, similar, duplicates, model download; `KnowledgeBaseWindow.Ask.cs`: answers with sources), tasks section (UserControl `TasksPanel` with `TasksPanel.Calendar.cs`, wired up in `KnowledgeBaseWindow.Tasks.cs`; shared KB styles in `KnowledgeBaseStyles.xaml`), Brain section (partial class `KnowledgeBaseWindow.Brain.cs`), four source lists ordered by durability (Knowledgeordner, voice notes, recordings, clipboard history) with a category filter and multi-selection across lists; combined preview, record/stop, copy, export, bulk delete |
| `BrainCanvas` | Self-drawn knowledge network for the KB "Brain" section (partial `KnowledgeBaseWindow.Brain.cs`): bucketed edge geometry, shapes per source, topic labels, zoom at cursor, pan, node drag, highlight and focus |
| `KbExportDialog` | Export choice for the KB: combined Markdown or ZIP, optional overview |
| `InlineDictation` | Dictation straight into a text box (popup quick note, KB "Neue Notiz"); owns the dictation while active, inserts at the caret |
| `EditorChrome` | Shared header/button styles (`EditorChrome.xaml`) and light/dark brushes plus frame colors for KB window and export dialog |

## Services

| Service | Responsibility |
| --- | --- |
| `DatabaseService` | SQLite schema, CRUD, export/import, URL cache, encrypted auto-backups, purge and secure delete |
| `DataProtection`, `DataKey`, `RecoveryCode` | Data key in `datakey.json` (DPAPI) plus recovery-key wrap; never replaced while encrypted data exists |
| `SecureFile`, `DataMigration` | AES-256-GCM record format for notes, recordings, tasks, settings and backups; one-time encryption of plain files with crash-safe resume |
| `EncryptedDatabase` | Per-database passwords, encryption of plain databases via `PRAGMA rekey`, plain copy for the explicit SQLite export |
| `WebLink` | Opens only http(s) links, escaped, in the default browser |
| `ClipboardProcessor` | Converts current clipboard data into `ClipboardEntry` |
| `SyntaxService` | Code-language detection |
| `ContentKindService` | User-facing badges and broad content-kind detection; code entries use the generic `CODE` badge while `Language` stores the detected/editor language |
| `OcrService` | Async WinRT OCR for images |
| `UrlPreviewService` | Title/favicon fetch with cache and private-target guard |
| `ImageUtils` | Shared frozen bitmap loading, image alpha repair, opaque clipboard image conversion and DIB/PNG helpers |
| `HtmlMarkdownConverter` | Dependency-free CF_HTML fragment to Markdown conversion (headings, lists, tables, links, code), strips scripts, navigation and forms |
| `SettingsService` | JSON settings load/save |
| `MessageWindowService` | Hidden window for `WM_CLIPBOARDUPDATE` |
| `AppPaths` | Data directory path resolution (`data`) |
| `DesktopRecorderService` | Record mode: MTA background thread polls the foreground window via UI Automation (text only, no pixels), waits for a quiet period and appends Markdown snapshots |
| `RecordingLayout` | Turns the cached UI Automation tree (`UiNode`) into lines: joins fragments on the same visual row, joins wrapped lines into paragraphs, drops container names that repeat their content, renders grid cells as Markdown tables; browser content-only mode keeps URL plus main landmark or document without navigation/banner/search/footer |
| `RecordingMarkdown` | Recording file format: header, snapshot sections with full content or newly visible lines, footer; parser and directory loader |
| `KnowledgeSummaryBuilder` | Builds the KB overview Markdown from clipboard entries, exported notes and parsed recordings |
| `KnowledgeDashboard` | Counters and per-application/per-site statistics for the KB dashboard |
| `KnowledgeExport` | KB date groups, clipboard entries as Markdown articles, combined Markdown (TOC, demoted headings) and ZIP export (sprachnotizen/, bildschirmaufnahmen/, zwischenablage/, uebersicht.md) |
| `NoteStore` | Notes as Markdown files (`notes\yyyy-MM-dd_HH-mm-ss.md`), load/create/save/delete, KB export as snapshot copy (`kb\notes`) |
| `MicrophoneRecorder` | waveIn capture of the default microphone as 16 kHz mono floats (event notification plus capture thread) |
| `DictationService` | Whisper.net speech-to-text: on-demand ggml model download to `models`, single active dictation with owner, cached `WhisperFactory` |
| `EmbeddingModel` | multilingual-e5-small (int8 ONNX) via ONNX Runtime plus SentencePiece tokenizer (XLM-R id offset), mean pooling and L2 normalization, e5 query/passage prefixes; download of pinned files to `models\e5-small` with SHA256 check |
| `SemanticSearch` | `SemanticChunker` (clipboard, KB notes and recording snapshots into chunks of up to 1000 chars, skips short single tokens) and `SemanticRanking` (cosine plus word matches fused by reciprocal rank, grouping per item, near-duplicate folding, similar items, Rocchio relevance feedback `SimilarToMarked`, IDF-weighted word matches with word-start matching for terms up to three characters, duplicate groups with canonical links in the text key) |
| `SemanticIndex` | Vector store in `semantic.db` (key, content hash, vector; no text), incremental background sync with file cache, query embedding, prune of deleted clipboard entries, model release |
| `KnowledgeGraph` | Knowledge network for the KB "Brain": item nodes (mean vector), semantic edges (raw cosine threshold, neighbors ranked on centered vectors, loosely mutual k-nearest), time/session/domain edges, topics by label propagation with TF-IDF keyword labels (`BrainGraph`, `GraphNode`, `GraphEdge`, `GraphCluster`), deterministic Fruchterman-Reingold `ForceLayout` with warm start |
| `KnowledgeCategories`, `CategoryPath` | Category hierarchy with up to three levels (main › sub › fine): parsing, definitions with descriptions, assign, rename/move and delete with the whole subtree, counts per level |
| `AiPrompts` | Every language-model prompt (categorize, refine, answer, digest, review) with use, sent data and default; user text in settings, empty means default; answer format fixed by JSON schema |
| `CategoryRefinement`, `CategoryRefinementState` | Background proposals for finer sub/fine categories of well filled categories; reviewed in `CategoryDiscoveryWindow`, state in `category-refinement.json` |
| `MyKnowledge` | "Mein Wissen": daily weighted intake of all sources with own encrypted text copies (`my-knowledge.json`), waiting time and ramp, consolidation, condensing into monthly digests, fixed ("dauerhaft") entries, forget/dismiss, profile and word search |
| `MyKnowledgeAi` | Closeness to the core knowledge (E5 vectors), category and title proposals, summary requests with mandatory citations, weekly review, source selection for questions |
| `BrainMerge` | Lossless merge of Brain entries into one note (full texts with origin), verification before removing originals, transfer of category, collections, relations and summary sources |

## Recording Flow

```text
Record button (popup, KB window, tray)
  -> App.ToggleRecording -> DesktopRecorderService.Start (new recordings\yyyy-MM-dd_HH-mm-ss.md)
  -> every 500 ms: GetForegroundWindow + title (skip own process, hung windows, dwm ghosts, paused monitoring)
     -> new window/title stable >= 500 ms: one GetUpdatedCache(TreeScope.Subtree) call, copied into UiNode tree
        (TextPattern only for non-web documents/edits), RecordingLayout.Render (position-based lines, tables,
        browser content-only filter; no password fields, no title/scroll/menu bars), retry up to 2x while < 5 lines (Chromium warm-up)
        -> RecordingMarkdown.FormatSnapshot (full content, or only lines new for this window) -> append + flush
     -> same window: re-read every 2.5 s (at least 4x the last capture time), append only lines never logged for this window
  -> SnapshotWritten -> KnowledgeBaseWindow refresh (debounced 1 s)
Stop -> footer (end, duration, count) -> App.RecordingStateChanged (UI thread)
```

## Notes and Dictation Flow

```text
Ideenkombinat voice-note composer / popup quick-note panel
  -> InlineDictation -> text at the caret
  -> SaveQuickNote -> NoteStore.CreateInKb (notes\<id>.md plus kb\notes\<id>.md)
Popup window button -> App.OpenVoiceNotes -> Ideenkombinat composer (preserves existing draft)
NoteStore.LoadKnowledgeNotes -> KB snapshots plus legacy notes without export (no file migration)
Diktieren -> App.ToggleDictationAsync
  -> model missing: confirm -> DictationService.DownloadModelAsync (models\ggml-<model>.bin)
  -> DictationService.StartRecording -> MicrophoneRecorder.Start (waveIn, 16 kHz mono); WhisperFactory warmed in background
  -> second click: MicrophoneRecorder.Stop -> Whisper ProcessAsync (language from settings) -> drop [..]/(..) segments
  -> text inserted at the caret -> save via composer
Save -> App.KnowledgeBaseChanged -> KnowledgeBaseWindow reload
Semantic search / Brain -> OpenChunkInKb -> article in the Ideenkombinat
```

## Brain Flow

```text
KB "Brain" (Strg+B) -> semantic sync as for the search
  -> index signature changed: KnowledgeGraph.Build (worker; configurable node limit, with "Nur Mein Wissen" the heaviest entries, else the newest; edges, topics)
  -> ForceLayout on a worker, positions published every ~45 ms -> BrainCanvas.UpdatePositions
     (warm start keeps known nodes; threshold or source changes rebuild with warm start)
  -> click: side panel with links and reasons | double click / Oeffnen: OpenChunkInKb
  -> right click: select node, context menu "Verlustfrei zusammenfuehren" (BrainMerge, KnowledgeBaseWindow.BrainMerge.cs) and "Zu Mein Wissen hinzufuegen"
  -> search: words highlight locally, Enter -> SemanticIndex.Search -> highlight and zoom
```

## Mein Wissen Flow

```text
App start, then every 20 min on a new day (or "Jetzt aktualisieren")
  -> SemanticIndex.Collect (all sources, no model needed) -> MyKnowledge.Candidates
  -> MyKnowledgeAi.Affinity (index vectors, core = Knowledgeordner and fixed entries)
  -> MyKnowledge.Update: waiting time, weight, consolidate, condense expiring entries into digests -> my-knowledge.json
  -> E5 category proposals for entries without category (never saved as category)
Background work (idle, after the category analysis): language model for titles, unclear categories,
  digest summaries, weekly review sentences, then CategoryRefinement for one full category per job
"Zu Mein Wissen hinzufuegen" (any context menu) -> App.FixInMyKnowledgeAsync -> MyKnowledge.Fix (100 %)
Brain filter "Nur Mein Wissen" -> BrainOptions.Weights (heaviest first before the node limit)
```

## Semantic Search Flow

```text
KB "Semantische Suche" (Strg+K)
  -> model: bundled models\e5-small next to the EXE (EmbeddingModel.ResolveModelsDir); only if it is missing:
     "Modell laden" -> EmbeddingModel.DownloadAsync (data\models\e5-small, SHA256)
  -> SemanticIndex.Collect (clipboard snapshot, kb\notes, recordings; files cached by write time)
  -> SemanticIndex.SyncAsync: reuse vectors with matching hash, embed new chunks newest first in batches of 16,
     publish after each batch, delete vectors of vanished keys
  -> query (debounce 350 ms, Enter also re-syncs): EmbedQuery -> SemanticRanking.Search (cosine >= 0.80 and/or word match, RRF)
  -> "Aehnliche": SemanticRanking.Similar (>= 0.83) | "Aehnliche zu Markierten": SemanticRanking.SimilarToMarked (q + 0.75 * mean of marked) | "Dubletten": SemanticRanking.DuplicateGroups (>= 0.97)
  -> "Oeffnen": clipboard -> App.OpenEntryDetails; note/recording -> KB article (recording scrolled to the snapshot)
  -> "Fragen" (Strg+Enter): SemanticRanking.Search -> KnowledgeAnswer.SelectSources (6 items, 800 chars, numbered)
     -> LocalAiService.AnswerAsync (Granite, strict JSON schema answer/found/sources, sources are data)
     -> KnowledgeAnswer.Parse (only valid source numbers; "found" without citation counts as unsupported)
     -> answer panel with source buttons (select the hit in the result list)
KB window closed -> sync cancelled -> SemanticIndex.ReleaseModel
App start -> SemanticIndex.PruneClipboard (vectors of deleted history entries)
```

## Tasks Flow

```text
Popup context menu "Zu Aufgaben hinzufuegen" -> App.AddTaskFromEntry
  -> TaskStore.FromEntry (title from first line, copy of the text up to 4000 chars, SourceKey "clip:<id>")
  -> TaskStore.Save (data\tasks.json via .tmp + move) -> App.TasksChanged
  -> KnowledgeBaseWindow.OpenTasks (section "Aufgaben", new task selected, title ready to rename)
KB "Aufgaben" (TasksPanel, window side in KnowledgeBaseWindow.Tasks.cs): month calendar with ISO weeks (TaskCalendar, TasksPanel.Calendar.cs; day or week click = due-date filter), add, edit title/due/notes (saved after 600 ms), done, filter, delete,
  TasksChanged reloads the file and keeps unsaved edits of the open task

Outlook (classic, 64 bit) context menu "Aufgabe in Merkautomat erstellen"
  -> MerkautomatOutlook.Connect.OnCreateTask (COM add-in, net48, late-bound Outlook objects, max. 20 mails)
  -> MailHandover.Write (data\inbox\mail-*.json via .tmp + move), AllowSetForegroundWindow,
     start Merkautomat via MerkautomatPath when it is not running
  -> App.MailInbox: FileSystemWatcher (and app start) -> MailInbox.Parse -> MailInbox.ToTask (SourceKey "mail:<EntryID>")
  -> TaskStore.Save -> delete files -> App.TasksChanged -> KnowledgeBaseWindow.OpenTasks
Kontrollstation "Outlook" -> OutlookAddin.Install: copy DLL to data\outlook, HKCU COM class + Outlook Addins key
```

## Clipboard Flow

```text
Windows clipboard change
  -> MessageWindowService.WM_CLIPBOARDUPDATE
  -> App.OnClipboardChanged
     -> ClipboardProcessor.BuildEntry (Text plus CF_HTML-Fragment und SourceURL)
     -> PopupViewModel.AddEntry
        -> duplicate latest image suppression
        -> purge by count, age and optional DB size
     -> image OCR async
     -> URL preview async only when enabled and safe to fetch
```

## Popup Filtering

Filter order in `PopupViewModel.Filter`:

1. `TypeFilter`
2. `ShowPinnedOnly`
3. Prefix parsing: `type:`, `kind:`, `domain:`, `pinned:true`/`pinned:false`
4. FTS match set (`DatabaseService.SearchFts`) when the list has more than 1000 entries and neither regex nor a prefix is active
5. Regex mode when enabled (50 ms timeout)
6. Linear case-insensitive full-text match across content, OCR text, URL title and language

List groups are calculated by `EntryViewModel.GroupLabel`: pinned, today, yesterday, this week and older.

## Database

```sql
CREATE TABLE History (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    Type            INTEGER NOT NULL,
    Content         TEXT,
    ImageData       BLOB,
    OcrText         TEXT,
    Language        TEXT,
    UrlTitle        TEXT,
    UrlFavicon      BLOB,
    HexColor        TEXT,
    ContentKind     TEXT,
    DetectionReason TEXT,
    IsPinned        INTEGER NOT NULL DEFAULT 0,
    PinOrder        INTEGER NOT NULL DEFAULT 0,
    ThumbnailData   BLOB,
    UseCount        INTEGER NOT NULL DEFAULT 0,
    LastUsedAt      TEXT,
    HtmlContent     TEXT,
    SourceUrl       TEXT,
    Timestamp       TEXT NOT NULL
);

CREATE TABLE UrlCache (
    Url      TEXT PRIMARY KEY,
    Title    TEXT,
    Favicon  BLOB,
    CachedAt TEXT NOT NULL
);
```

Useful index:

```sql
CREATE INDEX IF NOT EXISTS IX_History_Pinned_Timestamp
ON History(IsPinned, Timestamp);
```

FTS:

```sql
CREATE VIRTUAL TABLE IF NOT EXISTS HistoryFts
USING fts5(content, ocrtext, urltitle, language, tokenize='unicode61');
```

`HistoryFts` is maintained by insert/update/delete triggers and is used for prefix full-text search once the in-memory list is large enough.

## WPF Notes

- Do not use `StackPanel.Spacing` unless the current WPF build accepts it; use child margins otherwise.
- Use aliases or fully qualified names where WPF and WinForms types collide.
- Keep code-behind where the existing windows already use it.
- Avoid screenshot automation through PowerShell/GDI/WinAPI.
- Local editor theme resources in `DetailWindow.xaml` must stay dynamic where the light/dark theme is switched at runtime.

## Brain topic curation and summaries

`BrainKnowledgeDialog` edits a selected topic or a node and its direct neighbors. `AppSettings.BrainAnnotations` stores names/categories by stable item key; `BrainSummaries` links an owned note id to source keys. `KnowledgeGraph` restores labels/categories, suppresses UI noise and repeated text during naming, and adds explicit `Summary` edges independently of cosine similarity.

`BrainKnowledge.Summarize` selects up to twelve original statements (at most three per source), deduplicates and adds source references. The editable draft is generated on demand off the UI thread. `BrainKnowledge.Save` creates or updates the linked note/KB snapshot, keeping previous versions as `.bak`. Original source files are not changed. `KnowledgeBaseChanged` triggers the existing semantic sync and rebuild path. No added dependencies or generative model.
## Local directed relationships and traversal

BrainRelation records live in AppSettings.BrainRelations with stable item keys, relation type, evidence, optional task and work steps. BrainRelationsDialog edits an isolated list and saves only on explicit acceptance. Missing endpoints remain stored for review but do not create graph nodes. KnowledgeGraph adds directed Manual edges without semantic thresholding or automatic topic merging. BrainCanvas renders arrowheads.

BrainTraversal.Find uses breadth-first search over allowed edge kinds, respects directed edges, terminates on cycles, and returns individual explained steps. Default traversal uses manual and summary edges; automatic hints are opt-in and intersect current edge filters. Results cover only the loaded graph, not the full database. No new dependency, model, inference service or network call.

# Clipwell Windows - Architecture

## Stack

| Layer | Technology |
| --- | --- |
| Framework | .NET 10, WPF, `net10.0-windows10.0.19041.0` |
| UI library | WPF-UI 4.3.0 |
| Tray | WinForms `NotifyIcon` |
| Database | `Microsoft.Data.Sqlite 10.0.14`, SQLite WAL with FTS5, `SQLitePCLRaw.bundle_e_sqlite3 3.0.5` override |
| OCR | `Windows.Media.Ocr.OcrEngine` |
| YAML | `YamlDotNet 18.1.0` (detection and validation) |
| Dictation | `Whisper.net 1.9.1` with bundled `ggml-base.bin` |
| Tests | xUnit v3 4.0.2 on Microsoft Testing Platform, 182 tests |

## Project Layout

```text
ClipwellWin/
  App.xaml.cs                 lifecycle, tray, hotkeys, clipboard chain
  NativeMethods.cs            Win32 interop
  Models/                     persisted settings and entries
  Services/                   storage, processing, detection, image helpers, OCR, URL preview,
                              recording, notes, dictation, KB summary
  ViewModels/                 popup and entry state
  Views/                      WPF windows plus DragGhost (drag preview) and KbInstruments (KB gauges)
ClipwellWin.Tests/            unit, integration and smoke tests
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
| `KnowledgeBaseWindow` | KB page in the detail-window chrome: dashboard with counters and gauges, overall Markdown summary, article list (exported notes and recordings) with search, type filter, grouping by date/type and multi-selection; combined preview, record/stop, copy, export, bulk delete |
| `KbExportDialog` | Export choice for the KB: combined Markdown or ZIP, optional overview |
| `InlineDictation` | Dictation straight into a text box (popup quick note, KB "Neue Notiz"); owns the dictation while active, inserts at the caret |
| `EditorChrome` | Shared header/button styles (`EditorChrome.xaml`) and light/dark brushes plus frame colors for KB window and export dialog |
| `NotesWindow` | Standalone notes: list, search, autosaving editor, Whisper dictation at the caret, export to KB |

## Services

| Service | Responsibility |
| --- | --- |
| `DatabaseService` | SQLite schema, CRUD, export/import, URL cache, backups, purge and secure delete |
| `ClipboardProcessor` | Converts current clipboard data into `ClipboardEntry` |
| `SyntaxService` | Code-language detection |
| `ContentKindService` | User-facing badges and broad content-kind detection; code entries use the generic `CODE` badge while `Language` stores the detected/editor language |
| `OcrService` | Async WinRT OCR for images |
| `UrlPreviewService` | Title/favicon fetch with cache and private-target guard |
| `ImageUtils` | Shared frozen bitmap loading, image alpha repair, opaque clipboard image conversion and DIB/PNG helpers |
| `HtmlMarkdownConverter` | Dependency-free CF_HTML fragment to Markdown conversion (headings, lists, tables, links, code), strips scripts, navigation and forms |
| `SettingsService` | JSON settings load/save |
| `MessageWindowService` | Hidden window for `WM_CLIPBOARDUPDATE` |
| `AppPaths` | Data directory path resolution (`%APPDATA%\Clipwell`) |
| `DesktopRecorderService` | Record mode: MTA background thread polls the foreground window via UI Automation (text only, no pixels), waits for a quiet period and appends Markdown snapshots |
| `RecordingLayout` | Turns the cached UI Automation tree (`UiNode`) into lines: joins fragments on the same visual row, joins wrapped lines into paragraphs, drops container names that repeat their content, renders grid cells as Markdown tables; browser content-only mode keeps URL plus main landmark or document without navigation/banner/search/footer |
| `RecordingMarkdown` | Recording file format: header, snapshot sections with full content or newly visible lines, footer; parser and directory loader |
| `KnowledgeSummaryBuilder` | Builds the KB overview Markdown from clipboard entries, exported notes and parsed recordings |
| `KnowledgeDashboard` | Counters and per-application/per-site statistics for the KB dashboard |
| `KnowledgeExport` | KB date groups, combined Markdown (TOC, demoted headings) and ZIP export (notizen/, aufnahmen/, uebersicht.md) |
| `NoteStore` | Notes as Markdown files (`notes\yyyy-MM-dd_HH-mm-ss.md`), load/create/save/delete, KB export as snapshot copy (`kb\notes`) |
| `MicrophoneRecorder` | waveIn capture of the default microphone as 16 kHz mono floats (event notification plus capture thread) |
| `DictationService` | Whisper.net speech-to-text: on-demand ggml model download to `models`, single active dictation with owner, cached `WhisperFactory` |

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
Tray "Notizen" submenu / popup quick-note panel
  -> App.OpenNotes(action, initialText) -> NotesWindow.Run (new note, optionally start dictation)
Editor change -> 800 ms debounce -> NoteStore.Create/Save (notes\<id>.md)
Diktieren -> App.ToggleDictationAsync
  -> model missing: confirm -> DictationService.DownloadModelAsync (models\ggml-<model>.bin)
  -> DictationService.StartRecording -> MicrophoneRecorder.Start (waveIn, 16 kHz mono); WhisperFactory warmed in background
  -> second click: MicrophoneRecorder.Stop -> Whisper ProcessAsync (language from settings) -> drop [..]/(..) segments
  -> text inserted at the editor caret -> autosave
In KB exportieren -> NoteStore.ExportToKb (kb\notes\<id>.md snapshot) -> App.KnowledgeBaseChanged -> KnowledgeBaseWindow reload
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

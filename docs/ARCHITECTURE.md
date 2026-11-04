# Clipwell Windows - Architecture

## Stack

| Layer | Technology |
| --- | --- |
| Framework | .NET 10, WPF, `net10.0-windows10.0.19041.0` |
| UI library | WPF-UI 4.3.0 |
| Tray | WinForms `NotifyIcon` |
| Database | `Microsoft.Data.Sqlite 10.0.9`, SQLite WAL, `SQLitePCLRaw.bundle_e_sqlite3 3.0.3` override |
| OCR | `Windows.Media.Ocr.OcrEngine` |
| Tests | xUnit 2.9.3, 92 Tests |

## Project Layout

```text
ClipwellWin/
  App.xaml.cs                 lifecycle, tray, hotkeys, clipboard chain
  NativeMethods.cs            Win32 interop
  Models/                     persisted settings and entries
  Services/                   storage, processing, detection, image helpers, OCR, URL preview
  ViewModels/                 popup and entry state
  Views/                      WPF windows
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

## Services

| Service | Responsibility |
| --- | --- |
| `DatabaseService` | SQLite schema, CRUD, export/import, URL cache, backups, purge and secure delete |
| `ClipboardProcessor` | Converts current clipboard data into `ClipboardEntry` |
| `SyntaxService` | Code-language detection |
| `ContentKindService` | User-facing badges and broad content-kind detection; code entries use the generic `CODE` badge while `Language` stores the detected/editor language |
| `OcrService` | Async WinRT OCR for images |
| `UrlPreviewService` | Title/favicon fetch with cache and private-target guard |
| `ImageUtils` | Image alpha repair, opaque clipboard image conversion and DIB/PNG helpers |
| `SettingsService` | JSON settings load/save |
| `MessageWindowService` | Hidden window for `WM_CLIPBOARDUPDATE` |
| `AppPaths` | Data directory path resolution (`%APPDATA%\Clipwell`) |

## Clipboard Flow

```text
Windows clipboard change
  -> MessageWindowService.WM_CLIPBOARDUPDATE
  -> App.OnClipboardChanged
     -> ClipboardProcessor.BuildEntry
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
3. Prefix parsing: `type:`, `kind:`, `domain:`, `pinned:`
4. Regex mode when enabled
5. FTS prefix search for large lists, with linear full-text fallback across content, OCR text, URL title and language

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

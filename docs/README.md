# Merkautomat for Windows

Merkautomat (code name and binary: Clipwell) is a local-first Windows clipboard manager built with WPF, .NET 10 and SQLite. It runs in the tray, records clipboard history and opens a keyboard-first popup for search, copy and paste.

User-facing names: Merkautomat (app), Ideenkombinat (knowledge base), Pinstation (pinboard), Sprechwerk (voice notes), Kontrollstation (settings). Executable, data folder (`%APPDATA%\Clipwell`), namespaces and project files keep the code name.

## Features

- Clipboard history for text, notes, URLs, code/config snippets (including YAML detection and validation), colors and images.
- Global hotkey with automatic fallback combinations when the requested shortcut is blocked.
- Keyboard-first popup with type chips, pinned filter, grouped entries, search prefixes, regex search, search history and sorting (newest, oldest, most/least used).
- Bulk actions for pin, unpin, export and delete.
- Detail window with editable text/code minieditor, image editor, OCR view and color values.
- Image support with thumbnail, zoom/pan, precise annotations, crop/resize/expand and duplicate-image suppression.
- URL previews with title/favicon cache, opt-out setting and loopback/private-network protection.
- URL quick links open entries in the default browser from popup or details.
- Pinstation: always-on-top window with the pinned entries.
- Eyedropper overlay for picking screen colors into history and clipboard.
- Desktop recording: after pressing Record, the visible text of the foreground window is read through UI Automation (no screenshots) and every stable change is appended as Markdown.
- Sprechwerk: voice notes window with search and autosave, local Whisper dictation into the note, export to the Ideenkombinat; tray submenu "Sprechwerk".
- Ideenkombinat (knowledge base window): overall Markdown summary of clipboard history, exported notes and all recordings, per-item view, copy and export.
- Ideenkombinat semantic search: local embedding model (multilingual-e5-small, bundled) finds clipboard entries, voice notes and recordings by meaning; hybrid ranking with word matches, similar entries and near-duplicate detection.
- Ideenkombinat "Brain": knowledge network of all indexed items with links by meaning, time, recording and website, automatic topics with keyword names, interactive force layout, focus and search.
- Export/import as JSON, SQLite backup export, auto-backup rotation and secure delete via VACUUM.
- Privacy controls: monitoring pause via tray menu.
- SQLite recovery prompt when `quick_check` fails.

## Keyboard

Default hotkey: `Win+Shift+V`

Popup shortcuts:

- `Enter`: paste selected entry
- `Ctrl+Enter`: paste selected entry as plain text
- `Up` / `Down`: move selection
- `Ctrl+P`: pin selected entry
- `Delete`: delete selected entry
- `Ctrl+B`: toggle pinboard
- `F2`: open details
- `Esc`: close popup

## Search

Supported prefixes:

- `type:url`, `type:text`, `type:code`, `type:image`, `type:color`
- `kind:code`, `kind:note`, `kind:png`
- `domain:github.com`
- `pinned:true`, `pinned:false`

## Data Locations

- Settings: `%APPDATA%\Clipwell\settings.json`
- History database: `%APPDATA%\Clipwell\history.db`
- Log: `%APPDATA%\Clipwell\clipwell.log`
- Image editor style memory: `%APPDATA%\Clipwell\editor-styles.json`
- Desktop recordings: `%APPDATA%\Clipwell\recordings\*.md`
- Notes: `%APPDATA%\Clipwell\notes\*.md`; notes exported to the KB: `%APPDATA%\Clipwell\kb\notes\*.md`
- Whisper base model: bundled as `models\ggml-base.bin` next to `Clipwell.exe` (MIT, see `THIRD-PARTY-NOTICES.txt`); the build fetches it into `Tools\whisper` if missing. Larger models (small/medium) download on demand to `%APPDATA%\Clipwell\models`.
- Semantic search: model bundled as `models\e5-small\` next to `Clipwell.exe` (MIT, see `THIRD-PARTY-NOTICES.txt`; the build fetches it into `Tools\e5-small` if missing), vector index (no text) in `%APPDATA%\Clipwell\semantic.db`
- Recovery backups: next to `history.db` with `.broken-YYYYMMDD-HHMMSS` suffix

## Build

Requirements:

- Windows 10 version 2004 (build 19041) or newer
- .NET 10 Windows Desktop SDK

Build Release:

```powershell
taskkill /IM Clipwell.exe /F
dotnet build ClipwellWin\ClipwellWin.csproj -c Release --nologo -v q
```

Run Release:

```powershell
Start-Process ClipwellWin\bin\Release\net10.0-windows10.0.19041.0\Clipwell.exe
```

Test:

```powershell
dotnet test --project ClipwellWin.Tests\ClipwellWin.Tests.csproj -c Release
```

Current suite: 234 xUnit v3 tests (one network test for the fallback model download runs only with `CLIPWELL_E5_DOWNLOAD=1`).

Portable single-file build:

```powershell
dotnet publish ClipwellWin\ClipwellWin.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist\Clipwell-win-x64
```

Dependency status: the previous `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 `NU1903` warning is resolved through `Microsoft.Data.Sqlite` 10.0.14 plus a direct `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5 override.

## Project Structure

- `ClipwellWin\Models`: persisted settings and clipboard entries
- `ClipwellWin\Services`: clipboard processing, database, settings, content detection, OCR, URL preview, HTML-to-Markdown, desktop recording, notes, dictation, KB summary and semantic search
- `ClipwellWin\ViewModels`: popup and entry presentation state
- `ClipwellWin\Views`: popup, details, settings, onboarding, pinboard, eyedropper, KB and notes windows
- `ClipwellWin.Tests`: service, view-model and smoke tests

## Documentation

- [STATUS.md](STATUS.md): current state, latest review, verification and known warnings
- [ROADMAP.md](ROADMAP.md): consolidated feature gaps and product roadmap
- [ARCHITECTURE.md](ARCHITECTURE.md): stable technical map for Codex, Claude and maintainers
- [handbuch.md](handbuch.md): user-facing manual

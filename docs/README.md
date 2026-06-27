# Clipwell for Windows

Clipwell is a local-first Windows clipboard manager built with WPF, .NET 10 and SQLite. It runs in the tray, records clipboard history and opens a keyboard-first popup for search, copy and paste.

## Features

- Clipboard history for text, notes, URLs, code/config snippets, colors and images.
- Global hotkey with automatic fallback combinations when the requested shortcut is blocked.
- Keyboard-first popup with type chips, pinned filter, grouped entries and search prefixes.
- Bulk actions for pin, unpin, export and delete.
- Detail window with editable text/code minieditor, image editor, OCR view and color values.
- Image support with thumbnail, zoom/pan, precise annotations, crop/resize/expand and duplicate-image suppression.
- URL previews with title/favicon cache, opt-out setting and loopback/private-network protection.
- URL quick links open entries in the default browser from popup or details.
- Pinboard window for always-on-top pinned entries.
- Eyedropper overlay for picking screen colors into history and clipboard.
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
- Recovery backups: next to `history.db` with `.broken-YYYYMMDD-HHMMSS` suffix

## Build

Requirements:

- Windows 10 or newer
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
dotnet test ClipwellWin.Tests\ClipwellWin.Tests.csproj --nologo -v q
```

Current suite: 92 xUnit tests.

Dependency status: the previous `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 `NU1903` warning is resolved through `Microsoft.Data.Sqlite` 10.0.9 plus a direct `SQLitePCLRaw.bundle_e_sqlite3` 3.0.3 override.

## Project Structure

- `ClipwellWin\Models`: persisted settings and clipboard entries
- `ClipwellWin\Services`: clipboard processing, database, settings, content detection, OCR and URL preview
- `ClipwellWin\ViewModels`: popup and entry presentation state
- `ClipwellWin\Views`: popup, details, settings, onboarding, pinboard and eyedropper windows
- `ClipwellWin.Tests`: service, view-model and smoke tests

## Documentation

- [STATUS.md](STATUS.md): current state, latest review, verification and known warnings
- [ROADMAP.md](ROADMAP.md): consolidated feature gaps and product roadmap
- [ARCHITECTURE.md](ARCHITECTURE.md): stable technical map for Codex, Claude and maintainers
- [handbuch.md](handbuch.md): user-facing manual

# Clipwell Windows - Status

Last update: 2026-06-06

## Current State

- Build target: .NET 8 WPF, `net8.0-windows10.0.19041.0`
- UI: WPF-UI 4.3.0 plus WinForms `NotifyIcon`
- Storage: SQLite WAL in `%APPDATA%\Clipwell\history.db`
- Tests: 55 xUnit tests (all passing)
- Solution: `ClipwellWin.slnx`

## Implemented

### Core

- Tray app with single-instance handling.
- Global hotkey registration with fallback combinations.
- Clipboard monitoring through hidden message window.
- Paste selected entry or latest entry.
- Text, note, URL, code, color and image entries.

### Popup

- Searchable grouped history list.
- Type filter chips and pinned-only filter.
- Search prefixes: `type:`, `kind:`, `domain:`, `pinned:`.
- Bulk selection with pin, unpin, export and delete.
- Hover action buttons and context menu actions.
- Quick note creation.
- Live relative-time refresh.
- Pinboard toggle.
- Eyedropper launch.

### Detail Window

- Editable text and code minieditor.
- Save with button or `Ctrl+S`.
- Line numbers, counters, undo and wrap toggle.
- Inline code highlight overlay.
- Image zoom/pan and image annotations.
- Image crop, resize and canvas expansion.
- OCR text view for images.
- Color swatch with HEX, RGB, HSL, CSS and Tailwind values.

### Data

- SQLite CRUD with lock gate.
- WAL mode and `quick_check` recovery path.
- JSON export/import with dedupe.
- SQLite backup export.
- Auto-backup rotation.
- Secure delete via VACUUM.
- Purge by count, age and optional DB size.
- URL title/favicon cache with 7-day TTL.

### Privacy

- Global incognito mode.
- App and domain exclusions.
- Sensitive-content filter for copied text and OCR text.
- URL preview toggle.
- URL preview skips loopback, private and link-local targets.

## Known Limitations / Technical Debt

- Selection export (`ExportEntriesToJson` in `PopupWindow`) uses `File.WriteAllText` directly; the full-history export uses the safer atomic write path.
- `DetailWindow` Blur annotation renders a gray overlay, not a real pixel blur.

## Current Backlog

The single backlog source is `next-feature.md`.

## Verification Command

```powershell
taskkill /IM Clipwell.exe /F
dotnet build ClipwellWin\ClipwellWin.csproj -c Release --nologo -v q
dotnet test ClipwellWin.Tests\ClipwellWin.Tests.csproj --nologo -v q
```

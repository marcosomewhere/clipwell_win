# Clipwell for Windows

Clipwell is a local-first Windows clipboard history tool built with WPF, .NET 8 and SQLite. It runs from the tray, watches clipboard changes and opens a keyboard-first popup for search, copy and paste.

## Features

- Clipboard history for text, URLs, code/config snippets, colors and images.
- Global hotkey with status in the tray menu.
- System tray icon rendered from a clipboard-with-heart motif for small Windows tray sizes.
- Hotkey registration falls back to usable combinations if the requested global shortcut is blocked.
- Single-instance mode: launching a second instance opens the running app instead of starting another watcher.
- Searchable popup with keyboard navigation.
- Popup follows the Windows light/dark app theme by default and uses a larger, resizable layout.
- Context menu per entry: copy, plain copy, pin, delete, details and manual type correction.
- Detail view for long text, code and images.
- Clipboard writes for text and images are retried and logged instead of crashing the app when Windows temporarily locks the clipboard.
- Duplicate image clipboard notifications are deduplicated before they reach history.
- URL previews with persisted title and favicon.
- Badges for entry type and detected detail type:
  - `URL`, `IMG`, `CODE`, `COLOR`, `TEXT`
  - Domains for URLs
  - Image formats such as `PNG`, `JPG`, `GIF`, `BMP`, `WEBP`, `TIFF`
  - Code/config types such as `PS1`, `SH`, `SCRIPT`, `JSON`, `XML`, `YAML`, `SQL`, `TOML`, `INI`, `PROPS`, `ENV`, `DOCKER`, `CONFIG`
- Configurable code detection: conservative, normal or aggressive.
- Paste test mode in settings for focus restore, `Ctrl+V` and `Win+V`.
- Crash logging to `%APPDATA%\Clipwell\clipwell.log`.
- SQLite recovery prompt when the history database is corrupt or locked.

## Keyboard

Default hotkey: `Win+Shift+V`

Popup shortcuts:

- `Enter`: paste selected entry
- `Ctrl+Enter`: paste selected entry as plain text
- `Up` / `Down`: move selection
- `Ctrl+P`: pin selected entry
- `Delete`: delete selected entry
- `Esc`: close popup

## Settings

The settings window includes:

- Hotkey recorder and manual hotkey entry
- Hotkey action: open menu or paste latest entry immediately
- Code detection mode
- Maximum history size
- Pause monitoring toggle
- Paste test field
- Full keyboard help

Note: Windows does not provide a permission prompt to force blocked global hotkeys. If `RegisterHotKey` fails, Clipwell shows the Windows error code and the user must free the combination or choose another one.

## Data Locations

- Settings: `%APPDATA%\Clipwell\settings.json`
- History database: `%APPDATA%\Clipwell\history.db`
- Crash log: `%APPDATA%\Clipwell\clipwell.log`
- Database recovery backups: next to `history.db` with `.broken-YYYYMMDD-HHMMSS` suffixes

## Build

Requirements:

- Windows 10 or newer
- .NET 8 Windows Desktop runtime / SDK

Build:

```powershell
dotnet build ClipwellWin\ClipwellWin.csproj
```

Run:

```powershell
dotnet run --project ClipwellWin\ClipwellWin.csproj
```

Test:

```powershell
dotnet test ClipwellWin.Tests\ClipwellWin.Tests.csproj
```

## Project Structure

- `ClipwellWin\Models`: persisted app and clipboard data
- `ClipwellWin\Services`: clipboard processing, database, settings, hotkey window, URL preview, syntax/content detection
- `ClipwellWin\ViewModels`: popup and entry presentation state
- `ClipwellWin\Views`: popup, settings and detail windows
- `ClipwellWin.Tests`: syntax, clipboard pipeline, badges and popup smoke tests

## Current Verification

The current implementation has been checked with:

- Unit and integration tests: 33 passing tests
- Debug build
- Real app start without new crash log entries
- Tray icon creation during real app start
- Clipboard samples for plain text, URL, color, SQL, PowerShell, XML, properties, TOML/INI-style config, ENV, JSON, Dockerfile and PNG image
- Single-instance popup trigger through a second app launch
- History inspection confirming type detection, URL cache writes, image storage and detail badges

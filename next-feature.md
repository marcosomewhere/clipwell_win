# Next Features

Stand: 2026-06-06

## Code Quality / Dead Code

- **Atomic write for selection export**: `PopupWindow.ExportEntriesToJson` uses `File.WriteAllText` directly. The full-history export path uses the safe `WriteFileAtomic` helper in `DatabaseService`. Deduplicate into a shared utility or move the selection export through `DatabaseService`.

## Tests / Quality

- Add DB recovery tests for failed `quick_check` and `BackupAndReset`.
- Add schema migration tests for `EnsureColumn`.
- Add tests for hotkey fallback selection.
- Add tests for clipboard write retry behavior.
- Put `UrlPreviewService.FetchAsync` behind an interface for deterministic title/favicon tests.
- Add a concurrency test for the `DatabaseService` lock gate.

## Release

- Decide release format: MSIX, NSIS or portable ZIP.
- Add versioned release notes.
- Add code signing for binary and installer.

## Not Planned

- Cloud sync through OneDrive/iCloud for the live SQLite database.

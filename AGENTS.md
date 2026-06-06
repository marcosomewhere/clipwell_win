# Clipwell Windows - Agent Instructions

Gilt fuer Claude Code und Codex. AGENTS.md und CLAUDE.md muessen inhaltlich identisch bleiben.

## Projekt

WPF Clipboard-Manager fuer Windows. .NET 8, WPF-UI 4.3.0 (Lepo), SQLite WAL.
Laeuft im Tray und oeffnet per Hotkey ein tastaturgesteuertes Popup.

## Build & Start

```powershell
taskkill /IM Clipwell.exe /F
dotnet build ClipwellWin\ClipwellWin.csproj -c Release --nologo -v q
Start-Process ClipwellWin\bin\Release\net8.0-windows10.0.19041.0\Clipwell.exe
```

## Tests

```powershell
dotnet test ClipwellWin.Tests\ClipwellWin.Tests.csproj --nologo -v q
```

Aktuell: 55 Tests (xUnit 2.9.2). Release-Build und Tests sind Pflicht nach jeder Codeaenderung.

## Wichtige Pfade

| Was | Pfad |
| --- | --- |
| Hauptprojekt | `ClipwellWin\ClipwellWin.csproj` |
| Tests | `ClipwellWin.Tests\ClipwellWin.Tests.csproj` |
| Release-Binary | `ClipwellWin\bin\Release\net8.0-windows10.0.19041.0\Clipwell.exe` |
| History-DB | `%APPDATA%\Clipwell\history.db` |
| Einstellungen | `%APPDATA%\Clipwell\settings.json` |
| Log | `%APPDATA%\Clipwell\clipwell.log` |

## Architektur

| Bereich | Dateien |
| --- | --- |
| Views | `PopupWindow`, `DetailWindow`, `SettingsWindow`, `OnboardingWindow`, `PinboardWindow`, `EyedropperWindow` |
| ViewModels | `PopupViewModel`, `EntryViewModel`, `ViewModelBase` |
| Services | `DatabaseService`, `ClipboardProcessor`, `SyntaxService`, `ContentKindService`, `SensitiveContentService`, `OcrService`, `UrlPreviewService`, `SettingsService`, `MessageWindowService` |
| Models | `ClipboardEntry`, `AppSettings`, `EntryType`, `ThemeMode`, `HotkeyAction`, `CodeDetectionMode` |
| Infrastruktur | `App.xaml.cs`, `NativeMethods.cs`, `Converters/Converters.cs` |

## Aktuelle Funktionen

- Tray-App mit Single-Instance-Mutex und Hotkey-Fallbacks.
- Popup mit Filter-Chips, Suche, Gruppen, Bulk-Aktionen, Schnellnotiz, Pinboard und Eyedropper.
- Detailfenster mit editierbarem Text-/Code-Minieditor, Bildeditor, OCR-Ansicht und Farbdetails.
- SQLite-History mit WAL, URL-Cache, Export/Import, Auto-Backup, SecureDelete und Speicherlimits.
- Datenschutz: Inkognito-Regeln, Sensitive-Content-Filter, OCR-Sensitivfilter, URL-Preview-Opt-out und Private-/Loopback-Schutz.

## Clipboard-Event-Kette

```text
Clipboard-Aenderung
  -> MessageWindowService.WM_CLIPBOARDUPDATE
  -> App.OnClipboardChanged
  -> Inkognito-Check
  -> ClipboardProcessor.BuildEntry
  -> PopupViewModel.AddEntry
     -> Bild-Dedupe
     -> Purge nach Anzahl, Alter und optional DB-Groesse
  -> Bild: OCR async, danach erneuter Sensitivfilter
  -> URL: Preview nur wenn aktiviert und UrlPreviewService.ShouldFetch erlaubt
```

## Filter-Logik

Reihenfolge in `PopupViewModel.Filter`:

1. `TypeFilter`
2. `ShowPinnedOnly`
3. Suchpraefixe `type:`, `kind:`, `domain:`, `pinned:`
4. Volltextsuche in Content, OCR-Text, URL-Titel und Sprache

Gruppen: Gepinnt, Heute, Gestern, Diese Woche, Frueher.

## Datenbankschema

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
    Timestamp       TEXT NOT NULL
);

CREATE TABLE UrlCache (
    Url      TEXT PRIMARY KEY,
    Title    TEXT,
    Favicon  BLOB,
    CachedAt TEXT NOT NULL
);
```

## Coding-Regeln

- Kein MVVM-Refactoring ohne expliziten Auftrag; Code-behind ist hier bewusste Wahl.
- Keine neuen Abhaengigkeiten ohne Rueckfrage.
- Kommentare nur fuer nicht offensichtliches Warum.
- Keine Emojis in Code, Docs oder Ausgaben.
- Keine Herkunfts-, Tool- oder Arbeitsprosa in Projektdateien.
- Bei veraenderter Arbeitskopie: knapp den relevanten Status nennen, nicht pauschal warnen.
- Bestehende User-Aenderungen nicht revertieren. Bei Konflikten mit ihnen arbeiten.
- Vor Build `taskkill /IM Clipwell.exe /F` ausfuehren.
- Immer Release-Konfiguration bauen und Tests ausfuehren.
- Kein Screen-Capture per PowerShell/GDI/WinAPI.

## Bekannte WPF-Fallstricke

| Problem | Loesung |
| --- | --- |
| `TextBlock.MaxLines` existiert nicht in WPF | `MaxHeight` und `ClipToBounds=True` auf Container |
| `StackPanel.Spacing` erst ab .NET 9 | `Margin` auf Child-Elementen |
| Namespace-Konflikte bei WPF + WinForms | using-Aliases oder vollqualifizierte Typen |
| `DataTemplate.Triggers` mit externen Namen | Named Elements innerhalb des Templates verwenden |
| Hover-Zustand in DataTemplate | `IsHovered` im ViewModel plus `DataTrigger` |

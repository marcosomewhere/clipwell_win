# Clipwell Windows - Agent Instructions

Gilt fuer Claude Code und Codex. AGENTS.md und CLAUDE.md muessen inhaltlich identisch bleiben.

## Projekt

WPF Clipboard-Manager fuer Windows. .NET 10, WPF-UI 4.3.0 (Lepo), SQLite WAL.
Laeuft im Tray und oeffnet per Hotkey ein tastaturgesteuertes Popup.

## Build & Start

```powershell
taskkill /IM Clipwell.exe /F
dotnet build ClipwellWin\ClipwellWin.csproj -c Release --nologo -v q
Start-Process ClipwellWin\bin\Release\net10.0-windows10.0.19041.0\Clipwell.exe
```

## Tests

```powershell
dotnet test ClipwellWin.Tests\ClipwellWin.Tests.csproj --nologo -v q
```

Aktuell: 92 Tests (xUnit 2.9.3). Release-Build und Tests sind Pflicht nach jeder Codeaenderung.

Dependency-Sicherheitsstatus: `SQLitePCLRaw.lib.e_sqlite3` 2.1.11/`NU1903` wurde per `Microsoft.Data.Sqlite` 10.0.9 plus direktem `SQLitePCLRaw.bundle_e_sqlite3` 3.0.3 Override bereinigt. Bei erneuten NuGet-Warnungen Status dokumentieren und separat per Dependency-Update bereinigen.

## Wichtige Pfade

| Was | Pfad |
| --- | --- |
| Hauptprojekt | `ClipwellWin\ClipwellWin.csproj` |
| Tests | `ClipwellWin.Tests\ClipwellWin.Tests.csproj` |
| Release-Binary | `ClipwellWin\bin\Release\net10.0-windows10.0.19041.0\Clipwell.exe` |
| History-DB | `%APPDATA%\Clipwell\history.db` |
| Einstellungen | `%APPDATA%\Clipwell\settings.json` |
| Log | `%APPDATA%\Clipwell\clipwell.log` |

## Dokumentation

| Was | Pfad |
| --- | --- |
| Aktueller Status, Review, Warnungen | `docs\STATUS.md` |
| Feature-Luecken und Roadmap | `docs\ROADMAP.md` |
| Architekturkarte | `docs\ARCHITECTURE.md` |
| Benutzerhandbuch | `docs\handbuch.md` |

## Architektur

| Bereich | Dateien |
| --- | --- |
| Views | `PopupWindow`, `DetailWindow`, `SettingsWindow`, `OnboardingWindow`, `PinboardWindow`, `EyedropperWindow` |
| ViewModels | `PopupViewModel`, `EntryViewModel`, `ViewModelBase` |
| Services | `DatabaseService`, `ClipboardProcessor`, `SyntaxService`, `ContentKindService`, `OcrService`, `UrlPreviewService`, `ImageUtils`, `SettingsService`, `MessageWindowService` |
| Models | `ClipboardEntry`, `AppSettings`, `EntryType`, `ThemeMode`, `HotkeyAction`, `CodeDetectionMode` |
| Infrastruktur | `App.xaml.cs`, `NativeMethods.cs`, `Converters/Converters.cs`, `Services/AppPaths.cs` |

## Aktuelle Funktionen

- Tray-App mit Single-Instance-Mutex und Hotkey-Fallbacks.
- Popup mit Filter-Chips, Suche, Gruppen, Bulk-Aktionen, Schnellnotiz, Pinboard und Eyedropper.
- Detailfenster mit editierbarem Text-/Code-Minieditor, Bildeditor, OCR-Ansicht und Farbdetails.
- URL-Eintraege mit Browser-Sprung im Popup, Kontextmenue und Detailfenster.
- Code-Eintraege zeigen als Badge nur `CODE`; die konkrete Sprache bleibt separat fuer Highlighting und Export.
- SQLite-History mit WAL, URL-Cache, Export/Import, Auto-Backup, SecureDelete und Speicherlimits.
- Datenschutz: Ueberwachung pausieren (Tray), URL-Preview-Opt-out und Private-/Loopback-Schutz.

## Clipboard-Event-Kette

```text
Clipboard-Aenderung
  -> MessageWindowService.WM_CLIPBOARDUPDATE
  -> App.OnClipboardChanged
  -> ClipboardProcessor.BuildEntry
  -> PopupViewModel.AddEntry
     -> Bild-Dedupe
     -> Purge nach Anzahl, Alter und optional DB-Groesse
  -> Bild: OCR async
  -> URL: Preview nur wenn aktiviert und UrlPreviewService.ShouldFetch erlaubt
```

## Filter-Logik

Reihenfolge in `PopupViewModel.Filter`:

1. `TypeFilter`
2. `ShowPinnedOnly`
3. Suchpraefixe `type:`, `kind:`, `domain:`, `pinned:`
4. Volltextsuche in Content, OCR-Text, URL-Titel und Sprache
5. Regex-Modus (Toggle-Button `.*`); Suchhistorie (letzte 5 Suchen als Dropdown)

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

## Coding-Regeln

- Kein MVVM-Refactoring ohne expliziten Auftrag; Code-behind ist hier bewusste Wahl.
- Keine neuen Abhaengigkeiten ohne Rueckfrage.
- Kommentare nur fuer nicht offensichtliches Warum.
- Keine Emojis in Code, Docs oder Ausgaben.
- Keine Herkunfts-, Tool- oder Arbeitsprosa in Projektdateien.
- Bei veraenderter Arbeitskopie: knapp den relevanten Status nennen, nicht pauschal warnen.
- Bestehende User-Aenderungen nicht revertieren. Bei Konflikten mit ihnen arbeiten.
- Dokumentation konsolidiert halten: keine neuen Tages-MD-Dateien fuer Fixes/Reviews; in `STATUS.md` oder `ROADMAP.md` einarbeiten.
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

# Clipwell Windows - Agent Instructions

Gilt fuer Claude Code und Codex. AGENTS.md und CLAUDE.md muessen inhaltlich identisch bleiben.

## Projekt

WPF Clipboard-Manager fuer Windows. .NET 10, WPF-UI 4.3.0 (Lepo), SQLite WAL mit FTS5, YamlDotNet fuer YAML-Erkennung, Whisper.net 1.9.1 (lokales Diktat).
Laeuft im Tray und oeffnet per Hotkey ein tastaturgesteuertes Popup.

## Build & Start

```powershell
taskkill /IM Clipwell.exe /F
dotnet build ClipwellWin\ClipwellWin.csproj -c Release --nologo -v q
Start-Process ClipwellWin\bin\Release\net10.0-windows10.0.19041.0\Clipwell.exe
```

Portable Single-File-Build (nur win-x64, damit keine fremden Runtime-Ordner entstehen; das Target `RemoveForeignWhisperRuntimes` laesst nur `runtimes\win-x64` der Whisper-DLLs neben der EXE; dazu kommen `models\ggml-base.bin` und `THIRD-PARTY-NOTICES.txt`):

```powershell
dotnet publish ClipwellWin\ClipwellWin.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist\Clipwell-win-x64
```

`dist\` und `Tools\` sind in `.gitignore`. SDK-Version ist per `global.json` auf 10.0.4xx (`latestFeature`) festgelegt.

## Tests

```powershell
dotnet test --project ClipwellWin.Tests\ClipwellWin.Tests.csproj -c Release
```

Aktuell: 148 Tests (xUnit v3 4.0.2 auf Microsoft Testing Platform, per `global.json` aktiviert). Release-Build und Tests sind Pflicht nach jeder Codeaenderung.

Dependency-Sicherheitsstatus: `SQLitePCLRaw.lib.e_sqlite3` 2.1.11/`NU1903` wurde per `Microsoft.Data.Sqlite` 10.0.14 plus direktem `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5 Override bereinigt; keine verwundbaren Pakete (auch transitiv, inkl. Whisper.net). Bei erneuten NuGet-Warnungen Status dokumentieren und separat per Dependency-Update bereinigen.

## Wichtige Pfade

| Was | Pfad |
| --- | --- |
| Hauptprojekt | `ClipwellWin\ClipwellWin.csproj` |
| Tests | `ClipwellWin.Tests\ClipwellWin.Tests.csproj` |
| Release-Binary | `ClipwellWin\bin\Release\net10.0-windows10.0.19041.0\Clipwell.exe` |
| History-DB | `%APPDATA%\Clipwell\history.db` |
| Einstellungen | `%APPDATA%\Clipwell\settings.json` |
| Log | `%APPDATA%\Clipwell\clipwell.log` |
| Bildeditor-Stilspeicher | `%APPDATA%\Clipwell\editor-styles.json` |
| Onboarding-Marker | `%APPDATA%\Clipwell\onboarding.seen` |
| Desktop-Aufnahmen (Markdown) | `%APPDATA%\Clipwell\recordings\*.md` |
| Notizen (Markdown) | `%APPDATA%\Clipwell\notes\*.md` |
| In die KB exportierte Notizen | `%APPDATA%\Clipwell\kb\notes\*.md` |
| Whisper-Basismodell (mitgeliefert, MIT) | Quelle `Tools\whisper\ggml-base.bin` (gitignored, Build laedt es bei Fehlen und prueft SHA256), Ausgabe `models\ggml-base.bin` neben der EXE |
| Whisper-Modelle Small/Medium (Download bei Bedarf) | `%APPDATA%\Clipwell\models\ggml-small.bin` bzw. `ggml-medium.bin` |

## Dokumentation

| Was | Pfad |
| --- | --- |
| Aktueller Status, Review, Warnungen | `docs\STATUS.md` |
| Feature-Luecken und Roadmap | `docs\ROADMAP.md` |
| Architekturkarte | `docs\ARCHITECTURE.md` |
| Benutzerhandbuch | `docs\handbuch.md` |
| Kurzuebersicht | `docs\README.md` |

## Architektur

| Bereich | Dateien |
| --- | --- |
| Views | `PopupWindow`, `DetailWindow`, `SettingsWindow`, `OnboardingWindow`, `PinboardWindow`, `EyedropperWindow`, `KnowledgeBaseWindow`, `NotesWindow`, Helfer `DragGhost` und `KbInstruments` |
| ViewModels | `PopupViewModel`, `EntryViewModel`, `ViewModelBase` |
| Services | `DatabaseService`, `ClipboardProcessor`, `SyntaxService`, `ContentKindService`, `OcrService`, `UrlPreviewService`, `HtmlMarkdownConverter`, `ImageUtils`, `SettingsService`, `MessageWindowService`, `DesktopRecorderService`, `RecordingMarkdown`, `KnowledgeSummaryBuilder`, `KnowledgeDashboard`, `NoteStore`, `DictationService`, `MicrophoneRecorder` |
| Models | `ClipboardEntry`, `AppSettings`, `EntryType`, `ThemeMode`, `HotkeyAction`, `CodeDetectionMode` |
| Infrastruktur | `App.xaml.cs`, `NativeMethods.cs`, `Converters/Converters.cs`, `Services/AppPaths.cs` |

## Aktuelle Funktionen

- Tray-App mit Single-Instance-Mutex und Hotkey-Fallbacks.
- Popup mit Filter-Chips, Suche, Gruppen, Bulk-Aktionen, Schnellnotiz (speichert in die Notizen, nicht in die History), Pinboard und Eyedropper.
- Detailfenster mit editierbarem Text-/Code-Minieditor, Bildeditor, OCR-Ansicht und Farbdetails.
- URL-Eintraege mit Browser-Sprung im Popup, Kontextmenue und Detailfenster.
- Code-Eintraege zeigen als Badge nur `CODE`; die konkrete Sprache bleibt separat fuer Highlighting und Export.
- SQLite-History mit WAL, URL-Cache, Export/Import, Auto-Backup, SecureDelete und Speicherlimits.
- Datenschutz: Ueberwachung pausieren (Tray), URL-Preview-Opt-out und Private-/Loopback-Schutz.
- Desktop-Aufnahme (Record-Button im Popup, KB-Fenster, Tray): liest sichtbaren Text des Vordergrundfensters per UI Automation (keine Pixel, kein Screen-Capture), schreibt jeden stabilen Wechsel als Markdown nach `recordings`. Clipwell-eigene Fenster und Passwortfelder werden uebersprungen, Monitoring-Pause gilt auch hier.
- Notizen als eigene Funktion (`NotesWindow`, Tray-Untermenue "Notizen", Schnellnotiz-Panel im Popup): Markdown-Dateien mit Autosave, Suche, Diktat per Whisper (lokal, Mikrofon per waveIn, Base-Modell mitgeliefert, Small/Medium nach Rueckfrage geladen) und "In KB exportieren" (Schnappschuss nach `kb\notes`).
- KB-Fenster (`KB`-Button im Popup, Tray "KB"): Dashboard mit Sieben-Segment-Zaehlern, Analog-Instrumenten zur aktuellen Aufnahme, Bibliotheksstatistik nach Anwendung/Webseite und Artikelliste (`KnowledgeDashboard`, `Views\KbInstruments.cs`), dazu Gesamtuebersicht ueber Clipboard-History, exportierte Notizen und alle Aufnahmen plus Einzelansicht je Notiz/Aufnahme, kopier- und exportierbar.

## Clipboard-Event-Kette

```text
Clipboard-Aenderung
  -> MessageWindowService.WM_CLIPBOARDUPDATE
  -> App.OnClipboardChanged
  -> ClipboardProcessor.BuildEntry (Text plus CF_HTML-Fragment und SourceURL)
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
3. Suchpraefixe `type:`, `kind:`, `domain:`, `pinned:true`/`pinned:false`
4. FTS5-Treffer (`DatabaseService.SearchFts`), nur bei mehr als 1000 Eintraegen, ohne Regex und ohne Praefix
5. Regex-Modus (Toggle-Button `.*`, Timeout 50 ms) ueber Content, OCR-Text, URL-Titel und Sprache
6. Lineare Volltextsuche (case-insensitive) ueber Content, OCR-Text, URL-Titel und Sprache

Suchhistorie: letzte 5 Suchen als Dropdown. Sortierung im Popup: Neueste, Aelteste, Haeufig genutzt.

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

CREATE INDEX IX_History_Pinned_Timestamp ON History (IsPinned, Timestamp);

CREATE VIRTUAL TABLE HistoryFts
    USING fts5(content, ocrtext, urltitle, language, tokenize='unicode61');
-- Trigger HistoryFts_ai/_ad/_au halten HistoryFts synchron zu History.
```

`ThumbnailData`, `UseCount`, `LastUsedAt`, `HtmlContent` und `SourceUrl` kommen per `EnsureColumn`-Migration dazu. `LoadAllLite` laedt `HtmlContent` nicht mit (nur `HasHtml`); `DatabaseService.LoadHtml` holt es bei Bedarf. Ist FTS5 nicht verfuegbar, faellt die Suche auf den linearen Scan zurueck.

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

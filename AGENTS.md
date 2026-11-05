# Clipwell Windows - Agent Instructions

Gilt fuer Claude Code und Codex. AGENTS.md und CLAUDE.md muessen inhaltlich identisch bleiben.

## Projekt

WPF Clipboard-Manager fuer Windows. .NET 10, WPF-UI 4.3.0 (Lepo), SQLite WAL mit FTS5, YamlDotNet fuer YAML-Erkennung, Whisper.net 1.9.1 (lokales Diktat), Microsoft.ML.OnnxRuntime 1.31.1 plus Microsoft.ML.Tokenizers 2.0.1 (lokale Embeddings fuer die semantische KB-Suche).
Laeuft im Tray und oeffnet per Hotkey ein tastaturgesteuertes Popup.

## Build & Start

```powershell
taskkill /IM Clipwell.exe /F
dotnet build ClipwellWin\ClipwellWin.csproj -c Release --nologo -v q
Start-Process ClipwellWin\bin\Release\net10.0-windows10.0.19041.0\Clipwell.exe
```

Portable Single-File-Build (nur win-x64, damit keine fremden Runtime-Ordner entstehen; das Target `RemoveForeignWhisperRuntimes` laesst nur `runtimes\win-x64` der Whisper-DLLs neben der EXE; dazu kommen `models\ggml-base.bin`, `models\e5-small\`, `THIRD-PARTY-NOTICES.txt` und `onnxruntime*.dll`; `RemoveOnnxImportLibraries` laesst die `.lib`-Dateien von ONNX Runtime weg):

```powershell
dotnet publish ClipwellWin\ClipwellWin.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist\Clipwell-win-x64
```

`dist\` und `Tools\` sind in `.gitignore`. SDK-Version ist per `global.json` auf 10.0.4xx (`latestFeature`) festgelegt.

## Tests

```powershell
dotnet test --project ClipwellWin.Tests\ClipwellWin.Tests.csproj -c Release
```

Aktuell: 234 Tests (xUnit v3 4.0.2 auf Microsoft Testing Platform, per `global.json` aktiviert). Ein Test laeuft nur mit `CLIPWELL_E5_DOWNLOAD=1` (Fallback-Download des Embedding-Modells, Netzwerk), sonst uebersprungen; der Test mit dem echten Embedding-Modell nutzt die mitgelieferte Kopie in der Build-Ausgabe. Release-Build und Tests sind Pflicht nach jeder Codeaenderung.

Dependency-Sicherheitsstatus: `SQLitePCLRaw.lib.e_sqlite3` 2.1.11/`NU1903` wurde per `Microsoft.Data.Sqlite` 10.0.14 plus direktem `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5 Override bereinigt; keine verwundbaren Pakete (auch transitiv, inkl. Whisper.net, ONNX Runtime, Microsoft.ML.Tokenizers, Google.Protobuf). Bei erneuten NuGet-Warnungen Status dokumentieren und separat per Dependency-Update bereinigen.

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
| Embedding-Modell multilingual-e5-small (mitgeliefert, MIT) | Quelle `Tools\e5-small\` (`model_quantized.onnx`, `sentencepiece.bpe.model`; gitignored, Build laedt bei Fehlen von gepinnten Hugging-Face-Revisionen und prueft SHA256), Ausgabe `models\e5-small\` neben der EXE; Fallback-Download nur bei fehlender Kopie nach `%APPDATA%\Clipwell\models\e5-small\` |
| Vektorindex der semantischen Suche (kein Text) | `%APPDATA%\Clipwell\semantic.db` |

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
| Views | `PopupWindow`, `DetailWindow`, `SettingsWindow`, `OnboardingWindow`, `PinboardWindow`, `EyedropperWindow`, `KnowledgeBaseWindow` (Teile `KnowledgeBaseWindow.Semantic.cs` fuer die semantische Suche und `KnowledgeBaseWindow.Brain.cs` fuer das Brain), `BrainCanvas` (selbst gezeichnetes Wissensnetz), `KbExportDialog`, `NotesWindow`, Helfer `DragGhost`, `KbInstruments`, `InlineDictation` (Diktat direkt in ein Eingabefeld) und `EditorChrome` (gemeinsame Kopfleisten-Stile, `EditorChrome.xaml`) |
| ViewModels | `PopupViewModel`, `EntryViewModel`, `ViewModelBase` |
| Services | `DatabaseService`, `ClipboardProcessor`, `SyntaxService`, `ContentKindService`, `OcrService`, `UrlPreviewService`, `HtmlMarkdownConverter`, `ImageUtils`, `SettingsService`, `MessageWindowService`, `DesktopRecorderService`, `RecordingLayout`, `RecordingMarkdown`, `KnowledgeSummaryBuilder`, `KnowledgeDashboard`, `KnowledgeExport`, `NoteStore`, `DictationService`, `MicrophoneRecorder`, `EmbeddingModel`, `SemanticSearch` (`SemanticChunker`, `SemanticRanking`), `SemanticIndex`, `KnowledgeGraph` (`BrainGraph`, `ForceLayout`) |
| Models | `ClipboardEntry`, `AppSettings`, `EntryType`, `ThemeMode`, `HotkeyAction`, `CodeDetectionMode` |
| Infrastruktur | `App.xaml.cs`, `NativeMethods.cs`, `Converters/Converters.cs`, `Services/AppPaths.cs` |

## Aktuelle Funktionen

- Tray-App mit Single-Instance-Mutex und Hotkey-Fallbacks.
- Popup mit Filter-Chips, Sortier-Chip, Suche, Gruppen, Bulk-Aktionen, Schnellnotiz mit direktem Diktat (speichert in die Notizen und sofort in die KB, nicht in die History), Pinboard und Eyedropper. Kopf-Aktionen als gerahmte Leiste (Stil `HeaderToolButton`, Zustand ueber `Tag` "recording"/"active"); Bilder mit Titel "Bild BxH" (Groesse aus dem Bildkopf, `ImageUtils.TryReadImageSize`) und OCR als Unterzeile; Zeitangaben je Gruppe (`EntryViewModel.FormatTime`).
- Eigene Grafiken in `ClipwellWin\Assets` als WPF-Resource: `kb-icon-16.png` (KB-Symbol in Popup, KB-Fenster, Tray) und `kb-rec-logo.png` ("KB REC" auf dem Aufnahme-Button der KB), Quelle `D:\dev\Browser-Addons\KnowledgeAssistant\KnowledgebaseRecorder\assets`.
- Detailfenster mit editierbarem Text-/Code-Minieditor, Bildeditor, OCR-Ansicht und Farbdetails.
- URL-Eintraege mit Browser-Sprung im Popup, Kontextmenue und Detailfenster.
- Code-Eintraege zeigen als Badge nur `CODE`; die konkrete Sprache bleibt separat fuer Highlighting und Export.
- SQLite-History mit WAL, URL-Cache, Export/Import, Auto-Backup, SecureDelete und Speicherlimits.
- Datenschutz: Ueberwachung pausieren (Tray), URL-Preview-Opt-out und Private-/Loopback-Schutz.
- Desktop-Aufnahme (Record-Button im Popup, KB-Fenster, Tray): liest sichtbaren Text des Vordergrundfensters per UI Automation (keine Pixel, kein Screen-Capture), setzt Fragmente nach Bildschirmposition zu Zeilen und Absaetzen zusammen, Tabellen mit Zellpositionen werden Markdown-Tabellen (`RecordingLayout`), und schreibt jeden stabilen Wechsel als Markdown nach `recordings`. Browser standardmaessig nur URL plus Seiteninhalt (Einstellung "Desktop-Aufnahme: nur Seiteninhalt"). Clipwell-eigene Fenster und Passwortfelder werden uebersprungen, Monitoring-Pause gilt auch hier.
- Notizen als eigene Funktion (`NotesWindow`, Tray-Untermenue "Notizen", Schnellnotiz-Panel im Popup): Markdown-Dateien mit Autosave, Suche, Diktat per Whisper (lokal, Mikrofon per waveIn, Base-Modell mitgeliefert, Small/Medium nach Rueckfrage geladen) und "In KB exportieren" (Schnappschuss nach `kb\notes`).
- KB-Fenster (`KB`-Button im Popup, Tray "KB"): Dashboard mit Sieben-Segment-Zaehlern, Analog-Instrumenten zur aktuellen Aufnahme, Bibliotheksstatistik nach Anwendung/Webseite (`KnowledgeDashboard`, `ViewsKbInstruments.cs`), Gesamtuebersicht ueber Clipboard-History, Sprachnotizen und Bildschirmaufnahmen, links drei getrennte Bereiche "Sprachnotizen", "Bildschirmaufnahmen" und "Zwischenablage" (die ganze Clipwell-History, je Eintrag als Markdown ueber `KnowledgeExport.ClipboardMarkdown`; Doppelklick oeffnet das Detailfenster, Loeschen entfernt aus der History) mit Suche und bereichsuebergreifender Mehrfachauswahl; unten erscheint nur fuer den aktiven Bereich "Neue Sprachnotiz" (Diktat, `NoteStore.CreateInKb`) bzw. "Neue Bildschirmaufnahme". Export der Auswahl als zusammengefasstes Markdown oder ZIP (`KnowledgeExport`, `KbExportDialog`). Kopfleiste und Buttons wie im Detailfenster (`EditorChrome`). In der KB heissen Notizen "Sprachnotizen" und Desktop-Aufnahmen "Bildschirmaufnahmen".
- Semantische Suche als KB-Abschnitt (Navigation "Semantische Suche", Strg+K): multilingual-e5-small lokal per ONNX Runtime, Modell (ca. 123 MB) mitgeliefert unter `models\e5-small` (fehlt es, bietet die KB "Modell laden" mit SHA256-Pruefung an). Index ueber Zwischenablage, KB-Sprachnotizen und Bildschirmaufnahmen (je Bildwechsel) in `semantic.db`, nur Schluessel, Hash und Vektor, kein Text; inkrementeller Sync im Hintergrund, solange die Suche offen ist. Hybride Suche (Kosinus plus Worttreffer per Reciprocal Rank Fusion), "Aehnliche", "Dubletten", Quellen-Chips, "Oeffnen" ins Detailfenster bzw. den KB-Artikel. Kurze Einzel-Token (Passwoerter, IDs) und Farben werden nicht indiziert.
- Brain als KB-Abschnitt (Navigation "Brain", Strg+B): Wissensnetz der neuesten 1000 indizierten Eintraege. Bezuege inhaltlich (Kosinus ab Regler-Schwelle, Standard 0,86), zeitlich (5 min), gleiche Aufnahme und gleiche Webseite (`SemanticChunk.Domain`); Themen per Label Propagation mit Stichwort-Namen; Kraft-Layout im Hintergrund mit warmem Start; Zoom, Verschieben, Knoten ziehen, Seitenleiste mit Themen bzw. Verknuepfungen samt Begruendung, Wort- und Bedeutungssuche, Quellen-/Bezugs-Filter, Fokus. Kanten werden live aus dem Index berechnet, nicht gespeichert. Schwelle, Quellen, Bezugsarten und Fokus stehen in `settings.json` (`AppSettings.Brain*`, Schwelle beim Laden auf 0,80 bis 0,95 begrenzt).

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

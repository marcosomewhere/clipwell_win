# Clipwell Windows - Status und Review

Stand: 2026-06-27

## Aktueller Zustand

- Zielplattform: .NET 10 WPF, `net10.0-windows10.0.19041.0`
- UI: WPF-UI 4.3.0 plus WinForms `NotifyIcon`
- Speicher: SQLite WAL in `%APPDATA%\Clipwell\history.db`
- Tests: 92 xUnit-Tests
- Solution: `ClipwellWin.slnx`
- Architekturstatus: Code-behind in den bestehenden WPF-Fenstern bleibt bewusst bestehen; keine MVVM- oder Architektur-Umbauten geplant.

## Implementiert

- Tray-App mit Single-Instance-Schutz und globalem Hotkey inklusive Fallbacks.
- Clipboard-History fuer Text, Schnellnotizen, URLs, Code, Farben und Bilder.
- Popup mit Suche, Typ-Filtern, Gruppen, Hover-Aktionen, Kontextmenue, Bulk-Aktionen, Pinboard und Eyedropper.
- URL-Eintraege mit Titel/Favicon-Cache, privatem Fetch-Schutz und Schnelllink zum Standardbrowser.
- Detailfenster mit editierbarem Text-/Code-Editor, Live-Syntax-Highlighting, Bildeditor, OCR-Ansicht und Farbwerten.
- Bildeditor mit Zoom/Pan, punktgenauem Zeichnen bei Zoom/Scroll, Annotationen, modernisierten Pfeilen/Rechtecken, Crop, Resize und Canvas-Erweiterung.
- Code-Erkennung mit einfachem UI-Badge `CODE`; konkrete Sprache bleibt separat fuer Highlighting und Export erhalten.
- SQLite-CRUD mit WAL, `quick_check`-Recovery, Export/Import, URL-Cache, Auto-Backups, Speicherlimits und Secure Delete via VACUUM.
- Datenschutz: Monitoring-Pause, URL-Preview-Opt-out, Schutz vor Loopback/private/link-local URL-Fetches.

## Letzte Aenderungen

- Bild-Clipboard fuer Paint/Office kompatibel gemacht: Clipwell repariert voll transparente Bilddaten mit versteckten RGB-Farben, schreibt Bilder als voll opakes GDI+-Bitmap und rendert den Bildeditor-Export ueber eine explizite Export-Canvas statt ueber den UI-Viewport.
- SQLite FTS5-Volltext-Index (`HistoryFts`) mit Triggern; Suche per Praefix-MATCH ab 1000 Eintraegen automatisch aktiv.
- FTS-Initialisierung erhaelt den Index ueber App-Starts hinweg; Rebuild nur bei fehlender/inkonsistenter Backfill.
- YAML-Erkennung in ContentKindService (LooksLikeYaml, ContentKind=`YAML`); YAML-Validierung mit YamlDotNet 18.0.0 im Detailfenster.
- Bestehende Text-Eintraege mit ContentKind `YAML` werden beim Schema-Start auf Typ `Code` migriert.
- Shape-Memory im Bildeditor: letzte 3 Farb-/Dicke-Kombinationen werden in `editor-styles.json` persistiert und als Schnellzugriffs-Buttons angezeigt.
- Helles Layout im Detail-, Text- und Bildeditor korrigiert.
- Bildeditor-Koordinaten gegen Zoom, Scroll und Layout-Transform korrigiert.
- Rechtecke und Pfeile im Bildeditor mit Schatten und moderner Darstellung versehen.
- Bildeditor: Schatten auf alle Annotationstypen ausgeweitet (Ellipse, Linie, Stift, Text, Marker).
- Bildeditor: Ctrl+Z/Ctrl+Y per Window.PreviewKeyDown; Mausrad scrollt, Ctrl+Mausrad zoomt.
- DetailWindow: Standardgroesse 1250x1020, Maximiert-Zustand wird in AppSettings persistiert.
- Browser-Sprung fuer URL-Eintraege im Popup, Kontextmenue und Detailfenster ergaenzt.
- Code-ContentKind auf generisches `CODE` konsolidiert, ohne Sprachdaten zu verlieren.
- Code-Editor aktualisiert Highlighting direkt beim Sprachwechsel.
- Stift-Zeichnen im Bildeditor korrigiert: Live-Draft verwendet absolute Canvas-Punkte ohne doppelten Startpunkt-Offset.
- Nutzung wird beim Kopieren und Einfuegen gezaehlt; Sortierung nach `Haeufig`/`Selten` aktualisiert im laufenden Popup.
- JSON-Export, JSON-Import und Auto-Backup erhalten Pin-Reihenfolge, UseCount, LastUsedAt und ThumbnailData.
- Ungebundener Bildeditor-Skaliercode und ungenutzte `AllowUnsafeBlocks`-Buildoption entfernt.
- Kurzlebige Tagesdokumente in `STATUS.md` und `ROADMAP.md` konsolidiert; offene Punkte stehen zentral in `ROADMAP.md`.

## Verifikation

Zuletzt ausgefuehrt am 2026-06-27:

```powershell
taskkill /IM Clipwell.exe /F
dotnet build ClipwellWin\ClipwellWin.csproj -c Release --nologo -v q
dotnet test ClipwellWin.Tests\ClipwellWin.Tests.csproj --nologo -v q
```

Ergebnis:

- Tests: 92/92 bestanden.
- Release-Build: erfolgreich, 0 Warnungen.
- Dependency-Warnung bereinigt: `SQLitePCLRaw.lib.e_sqlite3` 2.1.11/`NU1903` wird durch `Microsoft.Data.Sqlite` 10.0.9 plus direktem `SQLitePCLRaw.bundle_e_sqlite3` 3.0.3 Override nicht mehr aufgeloest.

## Bekannte Grenzen

- Bildeditor-Mausgenauigkeit ist automatisiert nur ueber Smoke-Tests abgedeckt; DPI-/Zoom-/Multi-Monitor-Verhalten sollte manuell geprueft werden.
- Browser-Oeffnen aus dem laufenden Tray-Popup ist implementiert, aber nicht automatisiert getestet.
- Es gibt noch keine Installer-/Signatur-/Release-Pipeline.
- Offene Produkt- und Technikpunkte werden in `docs/ROADMAP.md` gepflegt.

## Review

Keine kritischen Race Conditions bekannt:

- Clipboard-, Hotkey- und UI-State laufen ueber den UI-Thread.
- Datenbankzugriff wird ueber `_gate` serialisiert.
- OCR- und URL-Preview-Tasks aktualisieren UI-State ueber den Dispatcher.
- Settings-Saves sind gelockt.

Technische Schulden:

- `App.OnClipboardChanged` und `App.ReloadUrlPreview` enthalten aehnliche async URL-Preview-Koordination. Nicht extrahiert, weil State- und UI-Koordination dort sensibel ist.
- `UrlPreviewService` begrenzt Favicons per `FetchLimitedBytesAsync` auf `MaxFaviconBytes` und per Timeout.
- Einige UI-Smoke-Tests decken Renderbarkeit ab, aber keine echten Interaktionspfade per Maus/Tastatur.

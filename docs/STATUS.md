# Clipwell Windows - Status und Review

Stand: 2026-11-05

## Aktueller Zustand

- Zielplattform: .NET 10 WPF, `net10.0-windows10.0.19041.0`
- UI: WPF-UI 4.3.0 plus WinForms `NotifyIcon`
- Speicher: SQLite WAL in `%APPDATA%\Clipwell\history.db`
- Tests: 182 xUnit-Tests
- Solution: `ClipwellWin.slnx`
- Architekturstatus: Code-behind in den bestehenden WPF-Fenstern bleibt bewusst bestehen; keine MVVM- oder Architektur-Umbauten geplant.

## Implementiert

- Tray-App mit Single-Instance-Schutz und globalem Hotkey inklusive Fallbacks.
- Clipboard-History fuer Text, URLs, Code, Farben und Bilder; Notizen separat als Markdown mit lokalem Whisper-Diktat und KB-Export.
- Popup mit Suche, Typ-Filtern, Gruppen, Hover-Aktionen, Kontextmenue, Bulk-Aktionen, Pinboard und Eyedropper.
- URL-Eintraege mit Titel/Favicon-Cache, privatem Fetch-Schutz und Schnelllink zum Standardbrowser.
- Detailfenster mit editierbarem Text-/Code-Editor, Live-Syntax-Highlighting, Bildeditor, OCR-Ansicht und Farbwerten.
- Bildeditor mit Zoom/Pan, punktgenauem Zeichnen bei Zoom/Scroll, Annotationen, modernisierten Pfeilen/Rechtecken, Crop, Resize und Canvas-Erweiterung.
- Code-Erkennung mit einfachem UI-Badge `CODE`; konkrete Sprache bleibt separat fuer Highlighting und Export erhalten.
- SQLite-CRUD mit WAL, `quick_check`-Recovery, Export/Import, URL-Cache, Auto-Backups, Speicherlimits und Secure Delete via VACUUM.
- Datenschutz: Monitoring-Pause, URL-Preview-Opt-out, Schutz vor Loopback/private/link-local URL-Fetches.

## Letzte Aenderungen

- Version 1.1.0.
- Aufraeumen: tote Member entfernt (ungenutzte HTML-Regexe in `ContentKindService`, `BytesToImageConverter`, invertierte Converter-Varianten, `EntryViewModel.HexBrush`, `App.PreviousForeground`, nie gelesener Bildeditor-Pinsel, ungenutzter `selfClosing`-Parameter), sechs gleiche Bitmap-Loader zu `ImageUtils.LoadBitmap` zusammengefasst, ueberfluessige `using`-Direktiven entfernt. NuGet-Pakete sind aktuell (`dotnet list package --outdated` ohne Treffer).
- Notizen als eigenstaendige Funktion: `NotesWindow` (Tray-Untermenue "Notizen" mit "Notizen oeffnen", "Neue Notiz", "Neue Sprachnotiz (diktieren)"; Pfeil-Button im Schnellnotiz-Panel des Popups) mit Liste, Suche und Editor. Speicherung als Markdown in `%APPDATA%\Clipwell\notes\yyyy-MM-dd_HH-mm-ss.md` (`NoteStore`), Autosave nach 800 ms, leer gewordene Notizen werden beim Wechseln entfernt. Schnellnotizen aus dem Popup landen dort statt als gepinnter `NOTE`-Eintrag in der History (bestehende `NOTE`-Eintraege bleiben unveraendert).
- Diktat: Button "Diktieren" (Strg+D) im Notizfenster, Mikrofon-Button im Schnellnotiz-Panel des Popups und unter "Neue Notiz" im KB-Fenster (diktiert direkt ins Eingabefeld, `ViewsInlineDictation.cs`; die Instanz ist selbst Diktat-Owner, Speichern beendet ein laufendes Diktat vorher). `MicrophoneRecorder` nimmt per waveIn 16 kHz mono auf (Event-Callback plus Capture-Thread, max. 30 min), `DictationService` transkribiert lokal mit Whisper.net 1.9.1 und fuegt den Text an der Cursorposition ein; Whisper-Annotationen wie `[Musik]` werden verworfen. Das Base-Modell (~141 MB, MIT-Lizenz von OpenAI/whisper.cpp) wird mitgeliefert: Quelle `Tools\whisper\ggml-base.bin` (gitignored; der Build laedt es bei Fehlen von Hugging Face und prueft SHA256), Ausgabe `models\ggml-base.bin` neben der EXE plus `THIRD-PARTY-NOTICES.txt`. `DictationService` nimmt zuerst das mitgelieferte Modell; Small/Medium (Einstellungen > Allgemein > Diktat, dort auch Sprache Deutsch/Englisch/Automatisch) werden nach Rueckfrage nach `%APPDATA%\Clipwell\models` geladen. Pegelanzeige und Aufnahmedauer in der Statusleiste.
- "In KB exportieren" (Strg+E) schreibt einen Schnappschuss nach `%APPDATA%\Clipwell\kb\notes\<id>.md` (Kopf mit Titel, Erstellt, Exportiert). Die Notizliste zeigt "in KB" bzw. "KB veraltet", der Button wechselt zu "KB aktualisieren". Das KB-Fenster listet exportierte Notizen zwischen Gesamtuebersicht und Aufnahmen; Loeschen dort entfernt nur die KB-Fassung. Die Gesamtuebersicht hat einen Abschnitt "Notizen".
- Portable Publish: Target `RemoveForeignWhisperRuntimes` laesst bei RID-Builds nur `runtimes\win-x64\*whisper*.dll` neben der EXE (die Whisper-Natives werden nicht in die Single-File eingebettet), dazu `models\ggml-base.bin` und `THIRD-PARTY-NOTICES.txt`.
- Popup: Tastenhinweis-Leiste am unteren Rand entfernt; Schnellnotiz-Panel und Bulk-Leiste haben jetzt die abgerundeten unteren Ecken.
- Desktop-Aufnahme: Record-Button im Popup-Kopf, "Neue Aufnahme" links im KB-Fenster und das Tray-Menue startet/stoppt `DesktopRecorderService`. Ein MTA-Hintergrundthread prueft alle 500 ms Vordergrundfenster und Titel. Ist ein neues Fenster bzw. ein neuer Titel 500 ms stabil, wird sein sichtbarer Text per UI Automation gelesen und sofort geschrieben: ein einziger `GetUpdatedCache`-Aufruf mit `TreeScope.Subtree` holt den ganzen Baum (Opera/Jira ca. 110 ms statt eines prozessuebergreifenden `FindAll` je Elternknoten; max. 20000 Knoten), danach arbeitet `RecordingLayout` lokal auf einer `UiNode`-Kopie. TextPattern-Sichtbereich nur noch fuer Nicht-Web-Dokumente/-Editoren (Chromium/Firefox zerlegen ihn an jedem Inline-Element, das war die Ursache fuer "jedes Wort untereinander" und fuer abgeschnittenen Text bei 6000 Zeichen). Fragmente in derselben Bildschirmzeile werden zu einer Zeile verbunden, umbrochene Zeilen (Zeile reicht bis an den rechten Rand des Containers, naechste beginnt am linken Absatzrand) zu einem Absatz; Container, deren Name nur ihren Inhalt wiederholt, werden einmal geschrieben (bei langen Vorleser-Zusammenfassungen gewinnt der sichtbare Text); Zellen mit GridItem-Position werden Markdown-Tabellen. Browser (Prozessname) im Modus "nur Seiteninhalt" (Standard, `RecordingContentOnly`): URL aus der Adressleiste plus groesstes sichtbares Dokument, davon nur die Main-Landmark, ohne Main ohne Navigation/Banner/Suche/Fusszeile und Seitenleisten am Ende; Landmarken werden ueber den lokalisierten Steuerelementtyp erkannt (Chromium deutsch: "Primaer", "Navigation", "Ueberschrift" als Gruppe, "Suchtaste"). Teure Fenster werden hoechstens alle 4x Lesedauer nachgelesen; liefert der erste Durchgang unter 5 Zeilen (Chromium/Electron bauen den Baum erst auf Anfrage), wird bis zu zweimal nachgelesen. Im selben Fenster wird alle 2,5 s neu gelesen und nur geschrieben, was fuer dieses Fenster noch nie protokolliert wurde. Rauschfilter: Titel-/Scroll-/Menueleisten, Fensterknoepfe (auch `"App" minimieren` von Store-Apps), Objekt-Platzhalter U+FFFC und Zeilen ohne Buchstaben/Ziffern. Uebersprungen werden Clipwell-eigene Fenster, haengende Fenster (`IsHungAppWindow`) samt dwm-Geisterfenster und Passwortfelder; Store-Apps im Application Frame Host werden nach Fenstertitel benannt. Keine Pixelaufnahme, also kein Screen-Capture-Alarm und keine OCR. Monitoring-Pause pausiert auch die Aufnahme, Tray-Hinweis beim Start. Der erste Entwurf verlangte zwei identische Lesedurchgaenge und schrieb deshalb bei grossen oder sich staendig aendernden Fenstern (Teams, VS Code) keine Eintraege; das ist behoben, ebenso Domains unter "Inhaltsarten" der KB-Uebersicht (URL-Eintraege sind dort ausgenommen).
- Markdown pro Aufnahme in `%APPDATA%\Clipwell\recordings\yyyy-MM-dd_HH-mm-ss.md` (`RecordingMarkdown`): Kopf mit Start, je Wechsel `## HH:mm:ss - App: Fenstertitel` mit "Sichtbarer Inhalt" (erstes Mal oder weitgehend neuer Inhalt) bzw. "Neu sichtbar" (nur hinzugekommene Zeilen) und "Aenderung" (Fensterwechsel, Anzahl neuer Zeilen), Fusszeile mit Ende, Dauer und Anzahl.
- KB-Fenster (`KnowledgeBaseWindow`, `KB`-Button im Popup, Tray "KB"): Standard-Fensterrahmen, Kopfleiste und Buttons wie im Detailfenster (gemeinsame Stile in `Views\EditorChrome.xaml`, Hell/Dunkel ueber `EditorChrome`; Kopfleiste ohne Aufnahme-Button, nur Kopieren, Exportieren, Loeschen, Aktualisieren, Ordner, Schliessen). Links Dashboard und Gesamtuebersicht als Navigation, Suche ueber Titel, Fenstertitel und Inhalt, dann zwei getrennte Bereiche mit eigener Liste: "Sprachnotizen" (in die KB uebernommene Notizen) und "Bildschirmaufnahmen" (Desktop-Aufnahmen, Titel aus den erfassten Anwendungen). Die Zeilenhoehen teilen sich nach Anzahl der Eintraege. Ein Klick auf Bereichskopf oder Eintrag macht den Bereich aktiv; nur fuer ihn erscheint unten "Neue Sprachnotiz" (Diktat ueber `InlineDictation`, speichert ueber `NoteStore.CreateInKb` direkt in Notizen und KB) bzw. "Neue Bildschirmaufnahme" (Start/Stopp, Laufzeit, Eintraege; eine dort gestartete Aufnahme wird sofort ausgewaehlt und laeuft rechts live mit). Auswahl ueber beide Listen: einfacher Klick ersetzt die Auswahl, Strg/Umschalt erweitern sie bereichsuebergreifend, "Alle auswaehlen" je Bereich, "Alle"/"Keine" fuer beide. Filter- und Gruppierungs-Chips sind vorerst entfernt (`KnowledgeExport.DateGroup` bleibt fuer eine spaetere Gruppierung). Rechts Dashboard, Markdown eines Artikels oder zusammengefasste Vorschau mehrerer. Exportieren (`KbExportDialog`, `KnowledgeExport`) gilt fuer die Auswahl, sonst fuer alle angezeigten Artikel: zusammengefasstes Markdown mit Inhaltsverzeichnis und um eine Ebene verschobenen Ueberschriften oder ZIP mit `sprachnotizen\`, `bildschirmaufnahmen\` und optional `uebersicht.md`. Loeschen wirkt auf die ganze Auswahl (laufende Aufnahme ausgenommen). Tasten: Strg+F, Strg+E, Entf, F5, Esc. Das Dashboard (`KnowledgeDashboard` fuer die Zahlen, `Views\KbInstruments.cs` mit `SevenSegmentDisplay` und `AnalogGauge` mit animierter Nadel und automatischer Skala 5/10/25/50/...) zeigt Artikel-, Sprachnotiz-, Bildschirmaufnahme- und Zwischenablagezaehler, Instrumente zur laufenden bzw. letzten Aufnahme, Kacheln nach Anwendung und Webseite (kompakt, passt ohne Scrollen in die Standardgroesse 1400x940, begrenzt auf den Arbeitsbereich). Die Gesamtuebersicht (`KnowledgeSummaryBuilder`) fasst Clipboard-History (Anzahl, Zeitraum, Typen, letzte 7 Tage, Domains, Inhaltsarten, Meistgenutzt, Gepinnt, Zuletzt kopiert) und Aufnahmen (Anzahl, Bildwechsel, Gesamtdauer, haeufigste Anwendungen, Fenster je Aufnahme) zusammen. Laufende Aufnahmen aktualisieren die Ansicht im Sekundentakt.
- Webinhalte: Beim Kopieren aus dem Browser speichert Clipwell das CF_HTML-Fragment (`HtmlContent`, bis 1 Mio. Zeichen, nur mit Struktur wie Tabellen, Listen, Ueberschriften, Links) und die Quell-URL (`SourceUrl`, nur http/https). Einfuegen per Enter schreibt Text plus HTML, Ctrl+Enter weiterhin nur Text. Erneutes Kopieren desselben Textes aktualisiert HTML und Quelle.
- `HtmlMarkdownConverter` normalisiert das HTML ohne externe Abhaengigkeit zu Markdown (Ueberschriften, Absaetze, verschachtelte Listen, Tabellen, Links, Fett/Kursiv, Code, Zitate, Bilder mit Alt-Text) und entfernt Skripte, Styles, Navigation, Seitenleisten, Fusszeilen und Formulare.
- Detailfenster: Quell-URL als Link neben dem Titel, Browser-Button oeffnet sie; Umschalter "Webinhalt" zeigt das aufbereitete Markdown (schreibgeschuetzt, kopierbar) mit gerenderter Vorschau. Markdown-Vorschau stellt jetzt Pipe-Tabellen dar und kursiviert keine Unterstriche in Bezeichnern mehr.
- Testprojekt auf `xunit.v3` 4.0.2 und `xunit.runner.visualstudio` 4.0.1 umgestellt (Microsoft Testing Platform, `OutputType` Exe, Parallelisierung per `[assembly: Parallelization(Mode = None)]` aus).
- Pinboard-Umsortieren bei aktiver Suche nummeriert alle gepinnten Eintraege neu (`PopupViewModel.MovePinned`), nicht nur die sichtbaren; Test `MovePinned_RenumbersHiddenPinsToo`.
- Ziehen aus dem Popup in andere Programme zeigt einen Akzentrahmen am gezogenen Eintrag und eine halbtransparente Vorschau, die dem Cursor auch außerhalb von Clipwell folgt (`Views/DragGhost.cs`, klick-durchlässiges Fenster); dieselbe Vorschau beim Umsortieren im Pinboard.
- Eyedropper deckt den gesamten virtuellen Bildschirm ab (alle Monitore) statt nur den maximierten Primaermonitor; Farbwerte und Lupenposition nutzen physische Cursor-Koordinaten, damit gemischte Skalierung stimmt. Lupe mit Akzentrahmen, Umlaut-Kodierung im Hinweistext korrigiert.
- Popup oeffnet ohne Zucken: Fenster wird im Leerlauf nach dem Start vorbereitet, nur einmal vor dem Anzeigen positioniert und blendet von Deckkraft 0 ein.
- Zeitstempel-Aktualisierung baut die gruppierte Liste nur noch bei Tageswechsel neu auf statt bei jedem Oeffnen und jede Minute.
- Pin loesen im Popup und im Pinboard behaelt die Scrollposition; der geloeste Eintrag zieht die Liste nicht mehr in seine Datumsgruppe.
- Design "Blacked Out": dunkles Theme in Schwarztönen (`#0A0A0A`/`#121212`/`#262626`), Akzent `#E88832` zentral in `App.AccentHex` mit `AccentBrush`, `AccentTextBrush` (dunkler Text auf Orange) und `AccentStripeBrush`; dünner Akzentstrich oben in Popup, Detailfenster und Pinboard; Pins, Auswahlrahmen, Primärbuttons und WPF-UI-Akzent nutzen das Orange. Das bisherige Limettengrün ist entfernt, das helle Theme nutzt denselben Akzent.
- Gruppe "Gepinnt" im Popup sortiert nach Pin-Reihenfolge (zuletzt gepinnt oben, Pinboard-Reihenfolge gilt auch im Popup) statt nach Kopierzeitpunkt; Test `TogglePin_PutsLatestPinnedEntryFirstEvenWhenItIsOlder`.
- Pinboard-Drag & Drop: gezogener Eintrag wird umrandet, Einfuegelinie oben/unten am Ziel, Ablegen nach dem Ziel und am Listenende moeglich.
- Detailfenster-Kopfzeile: Aktionsbuttons bekommen ihre volle Breite, die Infozeile links wird bei Platzmangel abgeschnitten.
- Abhaengigkeiten aktualisiert: Microsoft.Data.Sqlite 10.0.14, SQLitePCLRaw.bundle_e_sqlite3 3.0.5, YamlDotNet 18.1.0, Microsoft.NET.Test.Sdk 18.11.0.
- `global.json` fixiert das SDK auf 10.0.4xx; `dist/` und `Tools/` sind in `.gitignore`.
- Ungenutzte ViewModel-Methoden entfernt; Migration des Alt-Themes `Compact` erfolgt jetzt beim Laden der Settings.
- Bild-Clipboard fuer Paint/Office kompatibel gemacht: Clipwell repariert voll transparente Bilddaten mit versteckten RGB-Farben, schreibt Bilder als voll opakes GDI+-Bitmap und rendert den Bildeditor-Export ueber eine explizite Export-Canvas statt ueber den UI-Viewport.
- SQLite FTS5-Volltext-Index (`HistoryFts`) mit Triggern; Suche per Praefix-MATCH ab 1000 Eintraegen automatisch aktiv.
- FTS-Initialisierung erhaelt den Index ueber App-Starts hinweg; Rebuild nur bei fehlender/inkonsistenter Backfill.
- YAML-Erkennung in ContentKindService (LooksLikeYaml, ContentKind=`YAML`); YAML-Validierung mit YamlDotNet im Detailfenster.
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

Zuletzt ausgefuehrt am 2026-11-05:

```powershell
taskkill /IM Clipwell.exe /F
dotnet build ClipwellWin\ClipwellWin.csproj -c Release --nologo -v q
dotnet test --project ClipwellWin.Tests\ClipwellWin.Tests.csproj -c Release
```

Ergebnis:

- Tests: 182/182 bestanden.
- Release-Build: erfolgreich, 0 Warnungen.
- Dependency-Warnung bereinigt: `SQLitePCLRaw.lib.e_sqlite3` 2.1.11/`NU1903` wird durch `Microsoft.Data.Sqlite` 10.0.14 plus direktem `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5 Override nicht mehr aufgeloest. `dotnet list package --vulnerable --include-transitive`: keine Treffer.
- Testprojekt laeuft auf `xunit.v3` 4.0.2 mit `xunit.runner.visualstudio` 4.0.1 auf der Microsoft Testing Platform (`global.json` `test.runner`); `dotnet test` braucht deshalb `--project`.

## Bekannte Grenzen

- Bildeditor-Mausgenauigkeit ist automatisiert nur ueber Smoke-Tests abgedeckt; DPI-/Zoom-/Multi-Monitor-Verhalten sollte manuell geprueft werden.
- Browser-Oeffnen aus dem laufenden Tray-Popup ist implementiert, aber nicht automatisiert getestet.
- Es gibt noch keine Installer-/Signatur-/Release-Pipeline.
- Desktop-Aufnahme erfasst nur, was Anwendungen per UI Automation bereitstellen: Bilder, Videos, Canvas-/Spiele-Oberflaechen und manche Electron-/Java-Apps liefern wenig oder keinen Text. Fenster mit laufend wechselndem Text (Uhren, Logs) erzeugen viele kleine Eintraege. Layout und Inhaltsfilter sind per `RecordingLayoutTests` mit synthetischen Baeumen abgedeckt; das Einlesen echter Fenster nicht. Landmarken-Namen anderer Browser-Sprachen und Firefox sind nicht verifiziert; ohne erkannte Landmarke bleibt Navigation im Text. Absatz-Erkennung ist heuristisch: Text-Knoten, die selbst mehrere Zeilen umfassen, werden nicht mit dem Folgeknoten verbunden.
- Diktat: Mikrofonaufnahme und Whisper-Transkription sind nur ueber Hilfsfunktionen getestet (Rauschfilter, Modellnamen); der Ende-zu-Ende-Pfad wurde ausserhalb der App gegen das Base-Modell geprueft. Transkription laeuft nach dem Stoppen in einem Stueck (kein Live-Text); bei langen Diktaten auf schwacher CPU dauert das spuerbar. Mikrofonzugriff muss in den Windows-Datenschutzeinstellungen fuer Desktop-Apps erlaubt sein.
- Auto-Backup und JSON-Export sichern Metadaten, Bilder und Thumbnails; SQLite-Export bleibt die vollstaendige 1:1-Sicherung.
- Der Weichzeichnen-Effekt im Bildeditor ist eine visuelle Ueberlagerung, kein echter Gaussian Blur.
- Offene Produkt- und Technikpunkte werden in `docs/ROADMAP.md` gepflegt.

## Review

Keine kritischen Race Conditions bekannt:

- Clipboard-, Hotkey- und UI-State laufen ueber den UI-Thread.
- Datenbankzugriff wird ueber `_gate` serialisiert.
- OCR- und URL-Preview-Tasks aktualisieren UI-State ueber den Dispatcher.
- Settings-Saves sind gelockt.
- Desktop-Recorder schreibt unter `_writeLock`; `Stop` wartet hoechstens 3 s auf den Lesethread (haengende Ziel-App) und schreibt danach die Fusszeile, spaete Schreibversuche des Threads werden verworfen. Zustandswechsel gehen per Dispatcher an UI-Thread.

Technische Schulden:

- `App.OnClipboardChanged` und `App.ReloadUrlPreview` enthalten aehnliche async URL-Preview-Koordination. Nicht extrahiert, weil State- und UI-Koordination dort sensibel ist.
- Einige UI-Smoke-Tests decken Renderbarkeit ab, aber keine echten Interaktionspfade per Maus/Tastatur.

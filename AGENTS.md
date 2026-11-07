# Clipwell Windows - Agent Instructions

Gilt fuer Claude Code und Codex. AGENTS.md und CLAUDE.md muessen inhaltlich identisch bleiben.

## Projekt

WPF Clipboard-Manager fuer Windows. .NET 10, WPF-UI 4.3.0 (Lepo), SQLite WAL mit FTS5, YamlDotNet fuer YAML-Erkennung, Whisper.net 1.9.1 (lokales Diktat), Microsoft.ML.OnnxRuntime 1.31.1 plus Microsoft.ML.Tokenizers 2.0.1 (lokale Embeddings fuer die semantische KB-Suche).
Laeuft im Tray und oeffnet per Hotkey ein tastaturgesteuertes Popup.

## Namen in der Oberflaeche

| Bereich | Name fuer Nutzer | Code-Name |
| --- | --- | --- |
| Hauptanwendung | Merkautomat | Clipwell |
| Knowledge Base | Ideenkombinat | KB, `KnowledgeBaseWindow` |
| Pinboard | Pinstation | `PinboardWindow` |
| Sprachnotizen (Whisper) | Sprechwerk | Notes, Sprachnotizen im `KnowledgeBaseWindow` |
| Einstellungen | Kontrollstation | `SettingsWindow` |

- Sichtbare Texte nutzen die Konstanten in `Services\AppNames.cs` (in XAML stehen die Namen direkt). Neue Texte nie mit "Clipwell", "KB", "Pinboard" oder "Notizen" als Bereichsname schreiben.
- Ein einzelner Eintrag bleibt "Sprachnotiz"/"Notiz"; "Einstellungen" bleibt fuer die Einstellungswerte selbst (z. B. "Einstellungen zurücksetzen").
- Programmdatei, Assembly, Prozess und Autostart-Eintrag heissen `Merkautomat` (`Merkautomat.exe`). Der alte Autostart-Eintrag Clipwell wird beim Abgleich entfernt. Datenpfad `%APPDATA%\Clipwell`, Namespaces, Projektdateien, Mutex/Fensterklassen, Drag-Format, User-Agent und Auto-Backup-Dateien (`clipwell-autobackup-*`) bleiben aus Kompatibilitaetsgruenden erhalten. Produktname und AssemblyTitle sind Merkautomat.

## Build & Start

```powershell
taskkill /IM Merkautomat.exe /F
dotnet build ClipwellWin\ClipwellWin.csproj -c Release --nologo -v q
Start-Process ClipwellWin\bin\Release\net10.0-windows10.0.19041.0\Merkautomat.exe
```

Portable Single-File-Build (nur win-x64, damit keine fremden Runtime-Ordner entstehen; das Target `RemoveForeignWhisperRuntimes` laesst nur `runtimes\win-x64` der Whisper-DLLs neben der EXE; dazu kommen `models\ggml-base.bin`, `models\e5-small\`, `THIRD-PARTY-NOTICES.txt` und `onnxruntime*.dll`; `RemoveOnnxImportLibraries` laesst die `.lib`-Dateien von ONNX Runtime weg):

```powershell
dotnet publish ClipwellWin\ClipwellWin.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist\Clipwell-win-x64
```

`dist\` und `Tools\` sind in `.gitignore`. SDK-Version ist per `global.json` auf 10.0.4xx (`latestFeature`) festgelegt.

`Directory.Build.props` laesst Build- und Publish-Ausgaben Dateien per Hardlink statt Kopie anlegen (gleiches Laufwerk), damit die Modelle nur einmal auf der Platte liegen; `Directory.Build.targets` (`RemoveNonWindowsRuntimes`) laesst native Bibliotheken fuer andere Betriebssysteme (Android, iOS, Linux, macOS, WebAssembly) aus allen Ausgaben weg, nur `runtimes\win-*` bleibt. Eine Datei in der Build-Ausgabe nie direkt aendern, sie ist dieselbe wie die Quelle unter `Tools\`.

## Tests

```powershell
dotnet test --project ClipwellWin.Tests\ClipwellWin.Tests.csproj -c Release
```

Aktuell: 498 Tests (xUnit v3 4.0.2 auf Microsoft Testing Platform, per `global.json` aktiviert). Ein Test laeuft nur mit `CLIPWELL_E5_DOWNLOAD=1` (Fallback-Download des Embedding-Modells, Netzwerk), sonst uebersprungen; der Test mit dem echten Embedding-Modell nutzt die mitgelieferte Kopie in der Build-Ausgabe. Release-Build und Tests sind Pflicht nach jeder Codeaenderung.

Dependency-Sicherheitsstatus: `SQLitePCLRaw.lib.e_sqlite3` 2.1.11/`NU1903` wurde per `Microsoft.Data.Sqlite` 10.0.14 plus direktem `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5 Override bereinigt; keine verwundbaren Pakete (auch transitiv, inkl. Whisper.net, ONNX Runtime, Microsoft.ML.Tokenizers, Google.Protobuf). Bei erneuten NuGet-Warnungen Status dokumentieren und separat per Dependency-Update bereinigen.

## Wichtige Pfade

| Was | Pfad |
| --- | --- |
| Hauptprojekt | `ClipwellWin\ClipwellWin.csproj` |
| Tests | `ClipwellWin.Tests\ClipwellWin.Tests.csproj` |
| Outlook-Add-in (net48, COM, wird vom Hauptprojekt mitgebaut) | `MerkautomatOutlook\MerkautomatOutlook.csproj`, Ausgabe `outlook\MerkautomatOutlook.dll` neben der EXE |
| Release-Binary | `ClipwellWin\bin\Release\net10.0-windows10.0.19041.0\Merkautomat.exe` |
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
| Aufgaben | `%APPDATA%\Clipwell\tasks.json` |
| Uebergabeordner des Outlook-Add-ins | `%APPDATA%\Clipwell\inbox\*.json` (unlesbare Dateien nach `inbox\fehler`) |
| Installierte Kopie des Outlook-Add-ins | `%LOCALAPPDATA%\Clipwell\outlook\MerkautomatOutlook.dll` |

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
| Views | `PopupWindow`, `DetailWindow`, `SettingsWindow`, `OnboardingWindow`, `PinboardWindow`, `EyedropperWindow`, `KnowledgeBaseWindow` (Teile `KnowledgeBaseWindow.Semantic.cs` fuer die semantische Suche, `KnowledgeBaseWindow.Ask.cs` fuer "Fragen", `KnowledgeBaseWindow.Tasks.cs` und `KnowledgeBaseWindow.TaskCalendar.cs` fuer Aufgaben und `KnowledgeBaseWindow.Brain.cs` fuer das Brain und `KnowledgeBaseWindow.BrainKnowledge.cs` fuer Themenbearbeitung/Zusammenfassungen, `KnowledgeBaseWindow.BrainRelations.cs` fuer Beziehungen/Wege, `KnowledgeBaseWindow.SemanticDeletion.cs` fuer Markieren und Loeschen in der Suche, `KnowledgeBaseWindow.Markdown.cs` fuer die Markdown-Ansicht; `SettingsWindow` mit Teilen `SettingsWindow.LocalAi.cs` und `SettingsWindow.Outlook.cs`), `BrainKnowledgeDialog` (Teil `BrainKnowledgeDialog.LocalAi.cs`), `BrainRelationsDialog`, `EntryHotkeyDialog`, `MarkdownPreview` (native Markdown-Vorschau), `BrainCanvas` (selbst gezeichnetes Wissensnetz), `KbExportDialog`, `CategorizeDialog` (Einordnungsdurchgang, Einstiege in `KnowledgeBaseWindow.Categorize.cs`), `AssignCategoryDialog` und `CategoryManagerDialog` (manuelle Haupt-/Unterkategorien, Bibliotheksanbindung in `KnowledgeBaseWindow.Categories.cs`), `ClassifierReportWindow` (Auswertung der Einordnungsverfahren), `CategoryDiscoveryWindow` (Kategorien analysieren mit Granite), `AppMessageBox` (Meldungen im App-Design), Helfer `DragGhost`, `KbInstruments`, `InlineDictation` (Diktat direkt in ein Eingabefeld) und `EditorChrome` (gemeinsame Kopfleisten-Stile, `EditorChrome.xaml`) |
| ViewModels | `PopupViewModel`, `EntryViewModel`, `ViewModelBase` |
| Services | `DatabaseService`, `ClipboardProcessor`, `SyntaxService`, `ContentKindService`, `OcrService`, `UrlPreviewService`, `HtmlMarkdownConverter`, `ImageUtils`, `SettingsService`, `MessageWindowService`, `DesktopRecorderService`, `RecordingLayout`, `RecordingMarkdown`, `KnowledgeSummaryBuilder`, `KnowledgeDashboard`, `KnowledgeExport`, `NoteStore`, `DictationService`, `MicrophoneRecorder`, `EmbeddingModel`, `SemanticSearch` (`SemanticChunker`, `SemanticRanking`), `SemanticIndex`, `KnowledgeGraph` (`BrainGraph`, `ForceLayout`), `CategorizationQueue`, `RocchioModel`, `CategoryClassifiers` (`ClassifierMethods`, `KnnClassifier`, `LogisticClassifier`), `ClassifierBenchmark`, `CategorizationReporting`, `CategoryDiscovery`, `CategoryMatching`, `KnowledgeCategories` (`CategoryPath`, Kategorienhierarchie), `FileContentReader`, `KnowledgeAnswer` (Fragen mit Quellen), `TaskStore` (`KbTask`), `TaskCalendar`, `MailInbox`, `OutlookAddin`, `BrainTraversal` (Wege im Brain), `SemanticDeletion`, `LinkText`, `E5Categorization`, `LocalAiModel`, `LocalAiService`, `LocalAiCategorization`, `ChildProcessJob`, `TrayMonogram`, `AppNames` |
| Models | `ClipboardEntry`, `AppSettings`, `BrainKnowledge` (`BrainAnnotation`, `KnowledgeCategory`), `CategorizationStats`, `EntryType`, `ThemeMode`, `HotkeyAction`, `CodeDetectionMode` |
| Infrastruktur | `App.xaml.cs` (Teile `App.EntryHotkeys.cs`, `App.MailInbox.cs`, `App.Markdown.cs`), `NativeMethods.cs`, `Converters/Converters.cs`, `Services/AppPaths.cs` |

## Aktuelle Funktionen

- Tray-App mit Single-Instance-Mutex und Hotkey-Fallbacks.
- Globale Hotkeys fuer einzelne Eintraege: Rechtsklick im Popup "Hotkey zuweisen ..." (`EntryHotkeyDialog`), Verwaltung in Kontrollstation → Tastenkuerzel "Hotkeys fuer Eintraege" (Liste, Aendern, Entfernen, Alle entfernen, Hauptschalter). Gespeichert in `AppSettings.EntryHotkeys`/`EntryHotkeysEnabled`, Regeln in `Services\EntryHotkeys.cs`, Registrierung und Einfuegen in `App.EntryHotkeys.cs` (`RegisterHotKey` mit IDs ab 9100, Einfuegen ins Vordergrundfenster nach Loslassen der Modifier). Zuweisen pinnt den Eintrag; die Kombination steht in der Liste neben der Zeit (`EntryViewModel.HotkeyText`). Keine Nummern/Strg+Ziffer im Popup.
- Popup mit Filter-Chips, Sortier-Chip, Suche, Gruppen, Bulk-Aktionen, Schnellnotiz mit direktem Diktat (speichert in die Notizen und sofort in die KB, nicht in die History), Pinboard und Eyedropper. Kopf-Aktionen als orange gerahmte Leiste (Stil `HeaderToolButton`, Zustand ueber `Tag` "recording"/"active"); Bilder mit Titel "Bild BxH" (Groesse aus dem Bildkopf, `ImageUtils.TryReadImageSize`) und OCR als Unterzeile; Zeitangaben je Gruppe (`EntryViewModel.FormatTime`).
- Eigene Grafiken in `ClipwellWin\Assets` als WPF-Resource: `kb-icon-16.png` (Symbol des Ideenkombinats in Popup, Fenster und Tray-Menue), `kb-rec-logo.png` (Bildschirmaufnahmen und Aufnahme-Button), `mic-rec-logo.png` (Sprechwerk und Diktat-Button) und `cb-logo.png` (vollstaendiges "clip snip"-Logo fuer die Zwischenablage). Die drei Bereichslogos stehen links in den Bereichsueberschriften des Ideenkombinats. `navigation-icons.png` enthaelt die vier orangefarbenen Navigationssymbole fuer Dashboard, Gesamtuebersicht, Suche und Brain; die Ausschnitte sind als CroppedBitmap-Ressourcen definiert. `popup-toolbar-icons.png` enthaelt Aufnahme, Schnellnotiz, Farbpalette und Mehrfachauswahl fuer die Popup-Kopfleiste. `about-cover.jpg` (640x1360, Titelbild mit Schriftzug) fuellt per ImageBrush die linke Spalte von Kontrollstation → Ueber; rechts stehen Nutzentext, vier Vorteile, Version und "From Germany by Marco Seefeldt" (kein Build-Stempel, kein GitHub-Link).
- Detailfenster mit editierbarem Text-/Code-Minieditor, Bildeditor, OCR-Ansicht und Farbdetails. Skripteditor: Hervorhebung/Zeilennummern gebuendelt ueber `QueueTextEditorSync` statt je Tastendruck, ab 40.000 Zeichen oder 1.500 Zeilen ohne Hervorhebung (`DetailWindow.ShouldHighlight`). Markdown wird vor Code erkannt (`ContentKindService.LooksLikeMarkdownDocument` in `ClipboardProcessor.BuildEntry`, Ergebnis Text/MARKDOWN) und oeffnet im Markdown-Editor mit nativer Vorschau (`MarkdownPreview`, kein WebBrowser), "Bearbeiten" zeigt den Quelltext; aeltere Skript-Eintraege mit Markdown-Inhalt (`DetailWindow.IsMarkdownEntry`) ebenso, Speichern macht sie zu Text/MARKDOWN; beim Start stuft `App.RecheckMarkdownEntries` (`PopupViewModel.FindMarkdownCandidates`) sie im Hintergrund um, manuelle Entscheidungen und Notizen bleiben. Erkennung: Codebloecke beiseite, mindestens zwei Markdown-Signale, hoechstens ein Viertel codeartiger Zeilen. Badge "MD", Filter-Chip "MD" nach "Code" (`PopupViewModel.ShowMarkdownOnly`, Markdown bleibt auch unter "Text"), Suche `kind:md`, Detailtitel "Markdown". Popup "Typ aendern" bietet "Als Markdown behandeln".
- URL-Eintraege mit Browser-Sprung im Popup, Kontextmenue und Detailfenster.
- Code-Eintraege zeigen als Badge nur `CODE`; die konkrete Sprache bleibt separat fuer Highlighting und Export.
- Kopierte Dateien (CF_HDROP, z. B. Explorer) lesbarer Typen werden mit Textinhalt gespeichert (`FileContentReader`, `ClipboardProcessor.TryBuildFileEntry`): Liste der Endungen, Schalter und Hoechstgroesse (Standard 1024 KB je Kopie) in der Kontrollstation (`AppSettings.FileContent*`), Binaerdateien mit NUL-Bytes uebersprungen, mehrere Dateien mit Trennzeilen "--- Datei: name ---". Die Dateipfade stehen zeilenweise in `SourceUrl` (Web-Quellen sind immer http(s)); Popup-Titel "Datei: name", Detailfenster-Quelle oeffnet den Explorer an der Datei. Die Pinstation nutzt dieselbe Liste fuer abgelegte Dateien.
- SQLite-History mit WAL, URL-Cache, Export/Import, Auto-Backup, SecureDelete und Speicherlimits.
- Datenschutz: Ueberwachung pausieren (Tray), URL-Preview-Opt-out und Private-/Loopback-Schutz. Inhalte mit `ExcludeClipboardContentFromMonitorProcessing`, `Clipboard Viewer Ignore` oder `CanIncludeInClipboardHistory` = 0 (Passwortmanager) speichert `ClipboardProcessor.BuildEntry` nie (`IsExcludedFromHistory`, unlesbare Marker gelten als privat).
- Desktop-Aufnahme (Record-Button im Popup, KB-Fenster, Tray): liest sichtbaren Text des Vordergrundfensters per UI Automation (keine Pixel, kein Screen-Capture), setzt Fragmente nach Bildschirmposition zu Zeilen und Absaetzen zusammen, Tabellen mit Zellpositionen werden Markdown-Tabellen (`RecordingLayout`), und schreibt jeden stabilen Wechsel als Markdown nach `recordings`. Browser standardmaessig nur URL plus Seiteninhalt (Einstellung "Desktop-Aufnahme: nur Seiteninhalt"). Clipwell-eigene Fenster und Passwortfelder werden uebersprungen, Monitoring-Pause gilt auch hier.
- Sprachnotizen als Bereich im Ideenkombinat und Schnellnotiz-Panel im Popup: Markdown-Dateien und lokales Diktat per Whisper. Kein separates Sprechwerk-Fenster und kein Sprechwerk-Tray-Untermenue. Aeltere, nicht exportierte Dateien aus `notes` erscheinen ueber `NoteStore.LoadKnowledgeNotes` ebenfalls im Ideenkombinat; vorhandene KB-Schnappschuesse haben Vorrang.
- KB-Fenster (`KB`-Button im Popup, Tray "KB"): Dashboard mit Sieben-Segment-Zaehlern, Analog-Instrumenten zur aktuellen Aufnahme, Bibliotheksstatistik nach Anwendung/Webseite (`KnowledgeDashboard`, `Views\KbInstruments.cs`), Gesamtuebersicht ueber Clipboard-History, Sprachnotizen und Bildschirmaufnahmen, links drei getrennte Bereiche "Sprachnotizen", "Bildschirmaufnahmen" und "Zwischenablage" (die ganze Clipwell-History, je Eintrag als Markdown ueber `KnowledgeExport.ClipboardMarkdown`; Doppelklick oeffnet das Detailfenster, Loeschen entfernt aus der History) mit Suche und bereichsuebergreifender Mehrfachauswahl; unten erscheint nur fuer den aktiven Bereich "Neue Sprachnotiz" (Diktat, `NoteStore.CreateInKb`) bzw. "Neue Bildschirmaufnahme". Export der Auswahl als zusammengefasstes Markdown oder ZIP (`KnowledgeExport`, `KbExportDialog`). Artikel mit Kategorie bekommen die Zeile "Kategorie: Haupt › Unter" unter der Ueberschrift (`KbArticle.Category`, `KnowledgeExport.WithCategoryLine`), das ZIP ordnet dann nach `Haupt/Unter/art/` bzw. `ohne kategorie/art/`, ohne Kategorien wie bisher nach Art. Import von `.md`/`.txt` (Button "Importieren", Strg+O oder Dateien ins Fenster ziehen, max. 2 MB je Datei, UTF-8/UTF-16 per BOM, sonst Latin-1) als Sprachnotiz plus KB-Kopie (`NoteStore.ImportText`, `DecodeImport`); ohne eigene Ueberschrift wird der Dateiname zur Ueberschrift. Kopfleiste und Buttons wie im Detailfenster (`EditorChrome`). In der KB heissen Notizen "Sprachnotizen" und Desktop-Aufnahmen "Bildschirmaufnahmen".
- Semantische Suche als KB-Abschnitt (Navigation "Suche", Strg+K): multilingual-e5-small lokal per ONNX Runtime, Modell (ca. 123 MB) mitgeliefert unter `models\e5-small` (fehlt es, bietet die KB "Modell laden" mit SHA256-Pruefung an). Index ueber Zwischenablage, alle Sprachnotizen (exportierte aus der KB-Kopie, nicht exportierte direkt aus `notes`, "Oeffnen" zeigt sie ebenfalls im Ideenkombinat) und Bildschirmaufnahmen (je Bildwechsel) in `semantic.db`, nur Schluessel, Hash und Vektor, kein Text; inkrementeller Sync im Hintergrund, solange die Suche offen ist. Hybride Suche (Kosinus plus Worttreffer per Reciprocal Rank Fusion), "Aehnliche", "Dubletten", Quellen-Chips, "Oeffnen" ins Detailfenster bzw. den KB-Artikel. Kurze Einzel-Token (Passwoerter, IDs) und Farben werden nicht indiziert.
- Fragen in der Suche (Knopf "Fragen", Strg+Enter im Suchfeld): hybride Suche wie oben, die besten 6 Treffer (je Eintrag einer, je 800 Zeichen, nummeriert, `KnowledgeAnswer.SelectSources`) gehen mit der Frage an Granite (`LocalAiService.AnswerAsync`, unabhaengig von der Auswahl E5/Granite in der Kontrollstation; gemeinsamer Ablauf `ExecuteAsync`/`PostChatAsync` mit der Einordnung: ein Aufruf gleichzeitig, 3 Minuten Limit). Strenges JSON-Schema `answer`/`found`/`sources`; Quellen sind Daten, keine Anweisungen; Belege als [n] im Text. `KnowledgeAnswer.Parse` nimmt nur Quellennummern im gueltigen Bereich, "gefunden" ohne gueltigen Beleg gilt als nicht belegt. Antwortfeld ueber der Trefferliste mit Quellen-Knoepfen (waehlen den Treffer), Abbrechen, Kopieren; fehlt Granite, Download-Angebot. Antworten werden nicht gespeichert.
- Aufgaben als KB-Abschnitt zwischen Gesamtuebersicht und Suche (Navigation "Aufgaben", Strg+T, Zahl offener Aufgaben neben dem Menuepunkt): Popup-Kontextmenue "Zu Aufgaben hinzufuegen" (`App.AddTaskFromEntry`) legt eine Aufgabe mit Titel aus der ersten Zeile und Kopie des Eintragstexts (max. 4000 Zeichen, `SourceKey` "clip:id") an und oeffnet sie markiert zum Umbenennen; eigene Aufgaben per Eingabefeld. Titel, Faelligkeit (TT.MM.JJJJ/TT.MM. oder Kalender-Picker als Popup beim Klick ins Feld, mit KW, Heute/Morgen/Naechster Montag/Kein Datum, Alt+Pfeil runter oder F4; gleiche Darstellung ueber `RenderCalendar`), Notizen, Erledigt; Speichern automatisch (600 ms). Filter Offen/Erledigt/Alle, Monatskalender mit ISO-Kalenderwochen ueber der Liste (`TaskCalendar`, `KnowledgeBaseWindow.TaskCalendar.cs`: Tag oder KW anklicken filtert nach Faelligkeit, erneuter Klick hebt auf, Punkt je Tag orange offen/rot ueberfaellig/grau erledigt; beim Filtern springt "Offen" auf "Alle"; neue Aufgabe bei Tagesfilter bekommt dieses Datum), Sortierung nach Faelligkeit, ueberfaellige rot, Loeschen mit Rueckfrage (nie der Eintrag; Knopf `TaskDeleteButton` in der Kopfleiste vor "Kopieren", nur in diesem Abschnitt sichtbar), "Heute erledigt" in der Kopfleiste (`TaskStore.CompleteDueOn`: alle offenen heute faelligen nach Rueckfrage, ueberfaellige bleiben offen), Kopieren als Markdown-Checkliste (kein "Quelle oeffnen", der Quelltext steht bei der Aufgabe; neue Aufgaben nur per Enter). Notizen und Quelle teilen sich die restliche Hoehe (1:3, GridSplitter dazwischen, ohne Quelle nur Notizen); die Quelle steht als FlowDocument mit klickbaren http(s)-Links (`LinkText`: auch Outlooks "<url>"-Form, lange Tracking-Links als "host/…", volle Adresse im Tooltip). Speicher `TaskStore` in `tasks.json` (atomar ueber .tmp; unlesbare Datei wird als `tasks.json.defekt-*` beiseitegelegt); `App.TasksChanged` laesst ein offenes KB-Fenster neu laden und behaelt dort ungespeicherte Aenderungen.
- Outlook-Add-in (nur klassisches Outlook fuer Microsoft 365, 64 Bit; Projekt `MerkautomatOutlook`, net48): COM-Add-in ohne Interop-Pakete (`IDTExtensibility2`, `IRibbonExtensibility`, `IRibbonControl` selbst deklariert in `ComInterfaces.cs`, Outlook-Objekte per `dynamic`). Die `custom`-Parameter von `IDTExtensibility2` (Interface und `Connect`) brauchen zwingend `[MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)]`; ohne das stuerzt Outlook beim Start in `clr.dll` ab (0xC0000005), Test `AddinInterfacesMarshalOutlookArraysAsSafeArrays`. Aenderungen an `ComInterfaces.cs` immer per Vtable-Aufruf in Windows PowerShell 5.1 pruefen, bevor das Add-in in Outlook geladen wird. Ribbon-XML nur fuer `Microsoft.Outlook.Explorer`: Kontextmenues `ContextMenuMailItem`/`ContextMenuMultipleItems` mit "Aufgabe(n) in Merkautomat erstellen" direkt nach "Kopieren" (`insertAfterMso="Copy"`). Liest je Mail (max. 20, nur Klasse 43) Betreff, Absendername, Empfangszeit, Text (max. 20.000 Zeichen), EntryID/StoreID und schreibt sie per `MailHandover` als JSON (`kind` "outlook-mail") ueber .tmp nach `%APPDATA%\Clipwell\inbox`; ruft `AllowSetForegroundWindow` und startet Merkautomat ueber `MerkautomatPath` aus dem Add-in-Registry-Schluessel, falls er nicht laeuft. Merkautomat (`App.MailInbox.cs`, `MailInbox`) beobachtet den Ordner per `FileSystemWatcher` (und beim Start), macht daraus Aufgaben (Titel Betreff ohne AW:/WG:/RE:/FW:, `SourceKey` "mail:EntryID", Quelle mit Von/Empfangen/Betreff plus Text, max. 4000 Zeichen; `MailInbox.CleanBody` entfernt unsichtbare Fuellzeichen aus Newsletter-Vorschauzeilen, Einrueckung und Leerzeilenfolgen, auch bei der Anzeige aelterer Mail-Aufgaben), loescht die Dateien und oeffnet die Aufgaben; ungueltige Dateien nach `inbox\fehler`. Einrichtung in Kontrollstation → Outlook (`OutlookAddin`, `SettingsWindow.Outlook.cs`): kopiert die DLL nach `%LOCALAPPDATA%\Clipwell\outlook` (Outlook sperrt so nie die Build-Ausgabe) und schreibt nur HKCU (64-Bit-Ansicht): `Software\Classes\CLSID\{6E4B2D7A-...}` (mscoree, CodeBase), ProgID `Merkautomat.OutlookAddin`, `Software\Microsoft\Office\Outlook\Addins\Merkautomat.OutlookAddin` (LoadBehavior 3, MerkautomatPath). Status erkennt LoadBehavior ohne Bit 1 als "von Outlook abgeschaltet" ("Wieder aktivieren"); Entfernen loescht die Schluessel. Outlook nie automatisch starten oder beenden; Tests schreiben nur unter `HKCU\Software\ClipwellTests\<guid>` und raeumen auf.
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

1. `TypeFilter` (Chip "MD": `ShowMarkdownOnly`, Badge MD)
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
- Vor Build `taskkill /IM Merkautomat.exe /F` ausfuehren.
- Immer Release-Konfiguration bauen und Tests ausfuehren.
- Kein Screen-Capture per PowerShell/GDI/WinAPI.
- Meldungen und Rueckfragen nie mit `System.Windows.MessageBox`, sondern mit `AppMessageBox.Show` (gleiche Signatur; App-Farben, Akzentlinie, deutsche Knoepfe, Primaerstil fuer den Standardknopf). In Dateien per `using MessageBox = ClipwellWin.Views.AppMessageBox;`.

## Bekannte WPF-Fallstricke

| Problem | Loesung |
| --- | --- |
| `TextBlock.MaxLines` existiert nicht in WPF | `MaxHeight` und `ClipToBounds=True` auf Container |
| `StackPanel.Spacing` erst ab .NET 9 | `Margin` auf Child-Elementen |
| Namespace-Konflikte bei WPF + WinForms | using-Aliases oder vollqualifizierte Typen |
| `DataTemplate.Triggers` mit externen Namen | Named Elements innerhalb des Templates verwenden |
| Hover-Zustand in DataTemplate | `IsHovered` im ViewModel plus `DataTrigger` |

## Brain-Themen und Zusammenfassungen

- Themenbegriffe filtern Oberflaechentexte und zaehlen identische Texte nur einmal; Aufnahme-Fenstertitel werden fuer Themenbegriffe ausgelassen.
- Dauerhafte Namen/Kategorien pro ItemKey in AppSettings.BrainAnnotations; gespeicherte Zusammenfassungen mit NoteId und SourceKeys in BrainSummaries. Keine instabilen Cluster-Nummern als Speicherschluessel verwenden.
- BrainKnowledgeDialog: getrennte Einstiege Thema bearbeiten / Zusammenfassen und Reiter fuer Zuordnung bzw. Entwurf; Quellenfilter, Markierungszaehler und Markdown-Vorschau. Thema oder Hauptpunkt waehlen, Quellen markieren, zuordnen oder lokal extraktiv zusammenfassen. Bearbeitete Entwuerfe bleiben beim Speicherzielwechsel im Dialog erhalten. Entwurf vor Speichern bearbeitbar. BrainKnowledge.Save erstellt/aktualisiert nur die ausgewaehlte Zusammenfassungsnotiz und deren KB-Kopie; vorherige Fassungen als .bak. Ausgangseintraege bleiben erhalten.
- GraphEdgeKind.Summary verbindet die indizierte Zusammenfassung explizit mit vorhandenen Quellen, separat filterbar. Index-Sync nach KnowledgeBaseChanged aktualisiert das Brain. Kein zusaetzliches Modell und keine neue Abhaengigkeit.

## Markdown-Vorschau im Ideenkombinat

- Gesamtuebersicht und Artikel bieten Markdown/Text als Umschalter. Standard ist die native formatierte FlowDocument-Ansicht, Text zeigt unveraenderten Quelltext. Kopieren/Export bleiben am Quelltext.
- MarkdownPreview rendert einen Markdown-Teilumfang (Ueberschriften, Listen, Code, Tabellen, Inline-Formatierung, Zitate und Weblinks), ohne HTML/XAML aus Inhalten auszufuehren oder Bilder nachzuladen. Keine neue Abhaengigkeit. KnowledgeBaseWindow.Markdown.cs koordiniert Ansicht und Cache.

## Dublettenfilter

- Suche: Dubletten ist ein abschaltbarer Filter mit Gruppen, allen Kopien einzeln oder je Gruppe einem Eintrag plus Einzelstuecken. Quellen und Suchtext bleiben im Filter wirksam; Index-Sync aktualisiert ihn.
- Gleicher Text vergleicht alle indizierten Abschnitte je Eintrag mit vereinheitlichtem Leerraum (Gross-/Kleinschreibung bleibt relevant). Aehnlicher Inhalt nutzt gemittelte normalisierte Vektoren, Schwelle 0,90 bis 1,00 (Standard 0,97). Maximal neueste 6000 indizierte Eintraege; keine Loeschung. Nicht automatisch als Freigabe fuer Loeschungen verwenden.

## Markieren und Loeschen in der Suche

- Checkboxen in SemanticResults markieren unabhaengig von der Vorschau; Alle markieren, Auswahl aufheben und Markierte loeschen. Gruppenansicht: Einordnen umfasst alle Gruppenmitglieder, Loeschen nur die aelteren Kopien (der neueste Eintrag je Gruppe bleibt, `SemanticDeletion.Plan` mit `keep`, auch keine Aufnahmedatei, die ihn enthaelt); sonst nur den sichtbaren Treffer. Bestaetigung mit Typ-/Dateizaehlung und standardmaessig Nein.
- SemanticDeletion.Plan dedupliziert IDs/Dateien und begrenzt Dateiziele auf direkte Notiz-/KB-/Aufnahmeordner. Sprachnotizen: Original, KB-Kopie und zugehoerige .bak. Aufnahme-Treffer: vollstaendige Datei nach ausdruecklichem Hinweis, aktive Aufnahme ausgeschlossen und nach Bestaetigung erneut geprueft.
- Ergebnisabgleich erhaelt Markierungen nur bei unveraenderter Gruppenmitgliedschaft. Aenderung von Gruppierung/Schwelle verwirft sie. Nach Loeschen KnowledgeBaseChanged plus Index-Sync, auch in Aehnliche/Dubletten; Brain-Zuordnungen geloeschter Ziele bereinigen.

## Lokale Brain-Beziehungen und Wege

- AppSettings.BrainRelations speichert gerichtete Beziehungen mit stabilen FromKey/ToKey, frei benennbarem Typ, Pflicht-Begruendung sowie optionaler Aufgabe und Arbeitsschritten. Keine automatische Ausfuehrung, keine externe KI, keine neuen Abhaengigkeiten.
- BrainRelationsDialog ueber die getrennten Aktionen Weg finden / Beziehungen: kuerzester Weg per Breitensuche, standardmaessig manuelle Beziehungen plus Quellen von Zusammenfassungen. Automatische Hinweise nur optional und unter Beachtung aktiver Bezugsfilter. Quellenfilter und 1000-Knoten-Grenze gelten; kein Weg ist keine Aussage ueber den Gesamtbestand.
- GraphEdgeKind.Manual mit Directed=true, Pfeile im Canvas und eigener gespeicherter Bezugsfilter BrainEdgeManual. Manuelle Beziehungen beeinflussen nicht automatisch Themencluster (z. B. widerspricht ist keine Themenzuordnung). Fehlende Endpunkte werden im Graph ausgelassen, gespeicherte Beziehungen bleiben bearbeitbar. Speichern aktualisiert das Brain; Abbrechen verwirft Dialogaenderungen.

- Das App-Tray-Symbol ist ein MA-Monogramm mit grau-beigem Oberteil, orangem Unterteil und feinem dunklem Bengal-Muster, als skalierbarer Zeichenpfad in Services/TrayMonogram.cs gerendert. Das Ideenkombinat behaelt sein eigenes Logo.

## Optionale lokale KI

- Kontrollstation → Lokale KI: AppSettings.LocalAiModel (Standard off), optional Granite 4.2 3B Q4_K_M mit llama.cpp b12110 Windows CPU. Keine neue NuGet-Abhaengigkeit. Expliziter Download von gepinnten offiziellen URLs, Groesse und SHA256 pruefen; kein Download beim Build oder Start. Lizenztexte unter Licenses mitliefern.
- LocalAiModel verwaltet Dateien unter AppPaths.ModelsDir/granite-4.2-3b. LocalAiService verwaltet den versteckten Prozess, ausschliesslich 127.0.0.1, zufaelliger Port/API-Schluessel, kein Proxy/Web-UI/Toolzugriff, nicht denkender Modus, JSON-Schema, Abbruch/Timeout und Beenden beim Ausschalten/App-Ende. Der Serverprozess haengt an einem Windows-Jobobjekt mit Kill-on-close (`ChildProcessJob`), endet also auch bei Absturz/erzwungenem Beenden von Merkautomat; vor jedem Start werden verwaiste llama-server aus genau diesem Modellordner beendet (sonst sperren sie die Laufzeitdateien).
- BrainKnowledgeDialog erstellt bei aktivierter und installierter KI Vorschlaege fuer Titel/Kategorie. Bis zu acht unterschiedliche Quellen mit je 700 Zeichen; unsichere Antworten bleiben ohne Kategorie. Uebernehmen und Speichern explizit, keine automatische Ueberschreibung vorhandener Zuordnungen. Auswahlwechsel entwertet Vorschlaege. Zusammenfassungen bleiben extraktiv ohne Modellpflicht.

- E5-Kategorisierung: LocalAiModel=e5-categories, vorhandenes Suchmodell ueber SemanticIndex.EmbedForCategorizationAsync gemeinsam nutzen (query-Praefix auf beiden Seiten, Modell-Sperre). E5Categorization vergleicht 2 bis 200 Kategorienamen mit bis zu acht Quellenauszuegen zu 700 Zeichen; nur bei uebereinstimmenden eindeutigen Treffern vorschlagen. Keine neuen Kategorien/Titel, Titelfeld bei Uebernahme erhalten. Vektoren nur begrenzt im Speicher cachen, Kategorien nach Aenderungen neu vergleichen. Lerndaten nur aus gespeicherten manuellen Zuordnungen (AppSettings.BrainAnnotations, Uebergabe beim Oeffnen des Dialogs): ab drei Kategorien bekommt jede Kategorie mit mindestens zwei Beispielen einen Rocchio-Prototyp (Name plus Mittel der neuesten bis zu 16 Beispieltexte aus dem Index, query-Praefix, hoechstens 400 Beispiele; Mathematik in RocchioModel); Vergleich nach Abzug des Prototyp-Mittels mit Abstand >= 0,08. Ausgewaehlte Quellen sind nie ihr eigenes Beispiel. Breite Kategorien (ab 6 Beispielen) koennen bis zu drei Unterprototypen (spherisches k-Means, je mindestens 3 Beispiele, Score = bester Unterprototyp) bekommen, aber nur wenn eine Leave-one-out-Auswertung ueber alle gespeicherten Zuordnungen (nahezu gleiche Kopien ab Kosinus 0,98 ausgeschlossen) fuer Unterprototypen Macro-F1 > einfacher Rocchio + 0,01 bei mindestens gleicher Trefferquote zeigt; Ergebnis je Beispielstand im Speicher gecacht. Vorschlaege liefern die besten drei Kandidaten (relative Werte, auch bei Enthaltung; im Einordnungsfenster als Knoepfe) und das aehnlichste eigene Beispiel. Kontrollstation "Qualitaet pruefen" (nur E5) zeigt die Leave-one-out-Auswertung: nur Namen, Rocchio mit einem und mehreren Prototypen, k-NN (3 Nachbarn) mit Beantwortungsquote, Trefferquote und Macro-F1. Keine negativen Beispiele, keine Multi-Label- oder Bildmodelle. Keine Lerndaten aus automatischen Brain-Beziehungen, keine gespeicherten Modelle, kein Selbsttraining aus ungespeicherten Vorschlaegen.

## Einordnen (Kategorien vergeben)

- CategorizeDialog vergibt Kategorien mit waehlbarer Quelle "Vorschlaege von" (nur fuer dieses Fenster): E5 (LocalAiService.SuggestWithE5Async), Granite (SuggestWithGraniteAsync) oder Aus; vorbelegt mit der Kontrollstation, bei "Aus" E5, falls das Suchmodell da ist; nicht verfuegbare Quellen sind deaktiviert. Quellenwechsel verwirft vorhandene Vorschlaege. "Markierte mit Granite analysieren ..." startet die Granite-Analyse nur fuer die markierten Eintraege (auch bereits zugeordnete, Rueckfrage nennt Ersetzungen) und zeigt uebernommene Zuordnungen sofort als zugeordnet. Einstiege: Brain-Seitenleiste "Mit lokaler KI einordnen ..." (Knoten plus sichtbare Verbindungen, inhaltlich verwandte vormarkiert; bzw. alle Mitglieder des gewaehlten Themas), Brain-Werkzeugleiste und Mehr-Menue "Alle ohne Kategorie einordnen ...", Suche "Markierte einordnen ...", Mehr-Menue "Auswahl einordnen ..." (Bibliotheksauswahl, in Suche/Brain deren Auswahl).
- Markierte gemeinsam: ein gemeinsamer Vorschlag (bis zu acht Quellen) und Zuordnung an alle Markierten nach Rueckfrage; bei Brain-Knoten/Themen laeuft der gemeinsame Vorschlag zuerst. Einzeln: Vorschlag je Eintrag, Enter ordnet zu und springt zum naechsten offenen, Ueberspringen, Neu vorschlagen; "Nur ohne Kategorie" steuert die Navigation.
- Jede Bestaetigung schreibt sofort BrainAnnotations (Name bleibt erhalten) und zaehlt fuer E5 sofort als Beispiel (CategorizationQueue.Version); Speicherfehler setzen die Zuordnung zurueck. Ein Modellaufruf gleichzeitig; Anfragen fuer verlassene Eintraege entfallen. Texte aus SemanticIndex.ItemTexts; fehlt ein Eintrag im Index, wird vorher synchronisiert, nicht indizierte (zu kurze) Eintraege werden genannt und ausgelassen. Nach Aenderungen wird das Brain neu aufgebaut.

## Einordnungsverfahren und Auswertung

- E5-Vorschlaege laufen ueber einen waehlbaren Klassifikator (AppSettings.CategorizationMethod, Standard "auto"): nur Kategorienamen, Rocchio (Name + Beispiele), Rocchio nur Beispiele, Rocchio klassisch (euklidischer Abstand zum unnormierten Schwerpunkt, ohne Namen und Zentrierung, Abstand 0,05), Rocchio robust (ab 5 Beispielen die unaehnlichsten 20 % vor der Mittelung weglassen), Rocchio mit Gegenbeispielen (gamma 0,1 bzw. 0,2: p = norm(mu+ - gamma * Mittel der 5 aehnlichsten fremden Beispiele)), Rocchio mit Unterprototypen, k-NN (3 Nachbarn, 2 Stimmen), logistische Regression (zentriert/normiert, L2, klassengewichtet, Gradientenabstieg; Vorschlag ab p >= 0,5 und Abstand 0,2). Alle in CategoryClassifiers/RocchioModel, ohne neue Abhaengigkeit.
- ClassifierBenchmark: gruppierte, nach Kategorie geschichtete 5-fache Kreuzvalidierung mit 3 Wiederholungen (feste Seeds), nahezu gleiche Kopien (Kosinus >= 0,98) bleiben in einem Teil. Kennzahlen: Macro-F1 mit Streuung, Top-1, beantwortet, davon richtig mit 95-%-Wilson-Intervall, Top-3, Abweisung unbekannter Themen (ab 4 Kategorien: bis zu 10 Kategorien einzeln ganz weglassen, Anteil ohne Vorschlag), kalibrierte Enthaltungsschwelle (ab 40 Beispielen; kleinste Schwelle, deren untere Wilson-Grenze >= 90 % richtig ergibt; Rocchio-Abstand 0,04 bis 0,25, Wahrscheinlichkeit 0,35 bis 0,95; wird fuer Vorschlaege verwendet, auch bei fest eingestelltem Verfahren), Abdeckung bei max. 10 % Fehler, ECE (nur logistische Regression), Zeit je Vorschlag, je Kategorie Precision/Recall/F1 und Verwechslungsmatrix. "auto" nimmt Rocchio, ausser ein anderes Verfahren ist um mindestens 0,02 Macro-F1 besser bei hoechstens 0,02 weniger Trefferquote; Namen allein nie. Bis 150 Beispiele wird vor dem Vorschlag ausgewertet, darueber im Hintergrund (bis dahin Rocchio); Ergebnis je Beispielstand gecacht.
- ClassifierReportWindow (Kontrollstation "Auswertung oeffnen ...", Ideenkombinat Mehr "Einordnung auswerten ..."): Verfahrenswahl, Balkendiagramm Macro-F1, Kennzahlentabelle (inkl. Lernzeit je Verfahren; Einbettzeit separat), Kategorien und Verwechslungsmatrix, "Zuordnungen zur Pruefung" (gespeicherte Zuordnungen, die Rocchio in allen Wiederholungen sicher einer anderen Kategorie zuordnet; Knopf oeffnet sie im Einordnungsfenster ueber KnowledgeBaseWindow.CategorizeKeysAsync), Praxiswerte, Verlauf, "Bericht kopieren" als Markdown. Praxis: CategorizationFeedback (angenommen/korrigiert/ohne Empfehlung, richtige unter Top-3, je Verfahren inkl. Granite) beim Speichern im Einordnungsfenster und Brain-Dialog, max. 5000; CategorizationRuns max. 50.

## Kategorien analysieren (Granite)

- Ideenkombinat Mehr "Kategorien analysieren (Granite) ..." bzw. Brain-Werkzeugleiste: neueste bis 600 Eintraege ohne Kategorie (gemittelte Index-Vektoren je Eintrag) werden per spherischem k-Means auf zentrierten Vektoren gruppiert (k = Wurzel(n/2), 1 bis 15, Gruppen ab 3 Eintraegen). Granite benennt jede Gruppe aus bis zu 8 typischsten Eintraegen (LocalAiService.SuggestWithGraniteAsync, unabhaengig von der Auswahl E5/Granite; vorhandene und bereits vorgeschlagene Namen werden mitgegeben); gleich benannte Gruppen werden zusammengefuehrt; nur ungueltige Antworten (InvalidDataException) fuer eine Gruppe bleiben unbenannt und abgewaehlt, Start- und Laufzeitfehler brechen die Analyse mit Meldung ab (fertige Gruppen bleiben). Jede Zeile erklaert sich selbst: Feldbeschriftung und Platzhalter, "N aehnliche Eintraege", Granites Begruendung bzw. Handlungshinweis, Beispiele untereinander, "Alle Eintraege anzeigen".
- CategoryDiscoveryWindow: Download-Angebot, falls Granite fehlt (nur auf Klick); Fortschritt laeuft innerhalb jeder Gruppe zeitbasiert weiter (erste Gruppe 30 s angenommen, danach Mittel der gemessenen Gruppen, max. 95 % je Gruppe) mit Laufzeit, Restzeit und gerade gelesener Gruppe; fertige Gruppen erscheinen sofort, am Ende zusammengefuehrt; Abbrechen behaelt fertige Gruppen; Vorschlaege mit editierbarem Namen, neu/vorhanden, Anzahl, Beispieltiteln und Begruendung. "Ausgewaehlte uebernehmen" nach Rueckfrage speichert per PersistCategory (nur Eintraege ohne Kategorie, nichts wird ueberschrieben) und zaehlt Praxis-Feedback fuer Granite; danach Brain neu aufbauen. Nichts wird ohne Bestaetigung gespeichert.

## Haupt- und Unterkategorien

- `BrainAnnotation(Name, Category, Subcategory)`: Category ist die Hauptkategorie, Subcategory optional und gehoert immer zu ihr. Definitionen mit optionaler Beschreibung (max. 300 Zeichen) in `AppSettings.KnowledgeCategories`; benutzte Kategorien ohne Definition zaehlen ebenfalls. Alte settings.json ohne Subcategory bleiben lesbar.
- In Dialogen, Warteschlange und Klassifikatoren wird ein Pfad als ein String "Haupt › Unter" (Trenner ` › `, Eingabe auch `>`) uebergeben; `KnowledgeCategories.Parse/Combine` wandeln, `Assign` speichert (Titel bleiben, leere Eintraege werden entfernt, Kategorie wird definiert), `Annotation` behaelt die Unterkategorie, wenn ein Dialog nur die unveraenderte Hauptkategorie kennt. Umbenennen auf vorhandenen Namen fuehrt zusammen; Unterkategorien lassen sich zu einer anderen Hauptkategorie verschieben; Loeschen einer Unterkategorie behaelt die Hauptkategorie.
- Bildschirmaufnahmen: Kategorie unter `r:datei.md` plus allen indizierten Bildwechseln `r:datei.md#n`; `KnowledgeCategories.Effective` faellt fuer Bildwechsel ohne eigene Kategorie auf die Aufnahme zurueck. `r:datei.md` ist kein Lernbeispiel und wird nicht doppelt gezaehlt.
- Ideenkombinat-Bibliothek: Kategoriezeile je Eintrag, Filter unter der Suche (Alle, Ohne Kategorie, Hauptkategorie inkl. Unterkategorien, einzelne Unterkategorie), "Kategorie festlegen ..." (Strg+Umschalt+K, Kontextmenue, Mehr-Menue) und "Kategorien verwalten ..." (anlegen, beschreiben, umbenennen/zusammenfuehren, verschieben, loeschen mit Rueckfrage; Speicherfehler stellen den alten Stand her).
- E5 hierarchisch: zuerst die Hauptkategorie mit dem Verfahren aus der Auswertung (Auswertung und Praxiswerte vergleichen nur Hauptkategorien), danach lokal innerhalb dieser Hauptkategorie die Unterkategorie (`E5Categorization.SuggestSubcategoryAsync`/`RankSubcategories`): ab drei Unterkategorien zentrierter Rocchio aus Name plus eigenen Beispielen dieser Hauptkategorie, bei genau zwei mit Beispielen muessen die 2 bzw. 3 naechsten eigenen Beispiele uebereinstimmen, sonst nur Namen; eine einzige Unterkategorie wird nur angeboten. Beschreibungen gehen als "Thema: Name. Beschreibung" in die Namensvektoren ein. Im Einordnungsfenster Felder Haupt-/Unterkategorie, Unterkategorie-Kandidaten als Knoepfe; waehlt der Nutzer eine andere Hauptkategorie, wird die Unterkategorie dafuer neu vorgeschlagen.
- Granite bekommt `existing_subcategories` je Hauptkategorie und ein Feld `subcategory` (leer erlaubt); Antworten ohne das Feld bleiben gueltig.
- Brain: "Gruppieren nach Themen | Kategorien" (`AppSettings.BrainGroupByCategory`). Kategorien: Cluster = Hauptkategorien (Farbe), Unterkategorien als `GraphCluster.Subgroups` mit eigener Beschriftung im Netz und eigenen Zeilen in der Seitenleiste (markieren nur), Eintraege ohne Kategorie grau. `GraphEdgeKind.Category` (Filter "Gleiche Kategorie", `BrainEdgeCategory`) nur in dieser Ansicht: Kette je Pfad in Zeitfolge plus Verbindung der Unterkategorien zur Hauptkategorie; beeinflusst keine Themencluster.

## Rocchio ausserhalb der Einordnung und E5-vor-Granite

- Granite nur, wenn es noetig ist: `LocalAiService.SuggestWithGraniteAsync(..., assignments, e5First)` fragt bei vorhandenen Zuordnungen und Suchmodell zuerst E5 (`CategorizationCascade`). `e5First` und E5 sicher mit eigenen Beispielen (`LocalAiSuggestion.Learned`, nicht nur Namen) ersetzt den Granite-Aufruf (Begruendung "Granite nicht gestartet ..."); sonst gehen die besten drei E5-Kandidaten als `likely_categories` (nur vorhandene Hauptkategorien) unverbindlich an Granite. Einordnungsfenster und "Kategorien analysieren" nutzen `e5First`; "Neu vorschlagen" und ein erneuter gemeinsamer Vorschlag nach E5-Abkuerzung fragen Granite ausdruecklich. Brain-Dialog (`SuggestAsync` mit Granite) nur Hinweise, weil Granites Titel gebraucht wird. `CategoryProposal.Method` haelt das tatsaechliche Verfahren fuers Praxis-Feedback fest. Keine Few-Shot-Beispiele an Granite (laengerer Prompt verlaengert die Wartezeit).
- Suche "Aehnliche zu Markierten" (`SemanticRanking.SimilarToMarked`/`RocchioQuery`): q' = norm(q + 0,75 * Mittel der Markierten je Eintrag), ohne Suchtext nur das Mittel (Schwelle 0,83); mit Suchtext ueber die hybride Suche. Markierte bleiben in der Liste und markiert, erneutes Klicken verfeinert. Keine Gegenbeispiele.
- Bibliothek Mehr "Passende ohne Kategorie finden ..." (`CategoryMatching.Rank`): Kategorie aus dem Kategorienfilter (Haupt- oder Unterkategorie), zentrierter Rocchio nur aus Beispielen auf den gemittelten Indexvektoren (keine neue Einbettung), ab drei Kategorien mit je zwei Beispielen, neueste 32 je Kategorie. Kandidat nur ohne Kategorie, Zielkategorie vor allen anderen und Rohkosinus >= 0,80; sortiert nach Abstand, max. 100. Oeffnet das Einordnungsfenster mit vorbelegter gemeinsamer Kategorie; nichts wird ohne Bestaetigung gespeichert.
- Wortsuche der hybriden Suche: IDF-Gewicht ln(1 + N/df) je Begriff; Begriffe bis 3 Zeichen nur am Wortanfang, laengere auch in Komposita. Dubletten "Gleicher Text" ueber `SemanticRanking.DuplicateKey`: Leerraum vereinheitlicht, Links kanonisch (Schema/Host klein, ohne Standardport, ohne Tracking-Parameter wie utm_*/fbclid/gclid, ohne Schraegstrich am Ende, Fragment bleibt).

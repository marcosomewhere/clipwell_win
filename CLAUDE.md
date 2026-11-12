# Merkautomat - Agent Instructions

Gilt fuer Claude Code und Codex. AGENTS.md und CLAUDE.md muessen inhaltlich identisch bleiben.

## Projekt

WPF-Clipboard-Manager fuer Windows: laeuft im Tray und oeffnet per Hotkey ein tastaturgesteuertes Popup; dazu Wissensbasis (Ideenkombinat) mit lokaler semantischer Suche, Sprachnotizen, Bildschirmaufnahmen als Text, Aufgaben und Outlook-Add-in.

.NET 10, WPF-UI 4.3.0 (Lepo), SQLite WAL mit FTS5, verschluesselt per SQLite3 Multiple Ciphers 2.6.1 (`SQLite3MC.PCLRaw.bundle`, MIT, mit `Microsoft.Data.Sqlite.Core` 10.0.14), YamlDotNet, Whisper.net 1.9.1 (Diktat), Microsoft.ML.OnnxRuntime 1.31.1 plus Microsoft.ML.Tokenizers 2.0.1 (Embeddings fuer die Suche). Grundsatzentscheidungen: `docs/decisions/`.

## Namen in der Oberflaeche

| Bereich | Name fuer Nutzer | Code-Name |
| --- | --- | --- |
| Hauptanwendung | Merkautomat | Merkautomat |
| Knowledge Base | Ideenkombinat | KB, `KnowledgeBaseWindow` |
| Pinboard | Pinstation | `PinboardWindow` |
| Sprachnotizen (Whisper) | Sprechwerk | Notes, Sprachnotizen im `KnowledgeBaseWindow` |
| Einstellungen | Kontrollstation | `SettingsWindow` |
| Gewichtetes Tageswissen | Mein Wissen | `MyKnowledge`, `MyKnowledgePanel` |

- Sichtbare Texte nutzen die Konstanten in `src\Merkautomat\Core\AppNames.cs` (in XAML stehen die Namen direkt). Neue Texte nie mit "Clipwell", "KB", "Pinboard" oder "Notizen" als Bereichsname schreiben.
- Ein einzelner Eintrag bleibt "Sprachnotiz"/"Notiz"; "Einstellungen" bleibt fuer die Einstellungswerte selbst (z. B. "Einstellungen zurücksetzen").
- Programmdatei, Assembly, Prozess und Autostart-Eintrag heissen `Merkautomat` (`Merkautomat.exe`). Der alte Autostart-Eintrag Clipwell wird beim Abgleich entfernt. Benutzerdaten liegen unter `data\` im Tool-Stamm (Entwicklungsbuilds: Repository-Stamm; verteilte EXE: daneben). Auch Namespaces, Projektdateien, Mutex/Fensterklassen, Drag-Format, User-Agent, Logs und neue Auto-Backups verwenden Merkautomat. Alte Namen sind nur als gezielte Migrations-/Uebergangskennung erlaubt (AppData, Autostart, Single-Instance-Bruecke und historische Inhalte; Entscheidung 0008). Bestehende Sicherungen und Logs bleiben erhalten. Produktname und AssemblyTitle sind Merkautomat.

## Build, Start, Tests

```powershell
taskkill /IM Merkautomat.exe /F
dotnet build src\Merkautomat\Merkautomat.csproj -c Release --nologo -v q
Start-Process src\Merkautomat\bin\Release\net10.0-windows10.0.19041.0\Merkautomat.exe
dotnet test --project tests\Merkautomat.Tests\Merkautomat.Tests.csproj -c Release
```

Portable Single-File-Build (nur win-x64; `RemoveForeignWhisperRuntimes` laesst nur `runtimes\win-x64` der Whisper-DLLs neben der EXE, dazu `models\ggml-base.bin`, `models\e5-small\`, `THIRD-PARTY-NOTICES.txt`, `sqlite3mc.dll` und `onnxruntime*.dll`; `RemoveOnnxImportLibraries` laesst die `.lib`-Dateien weg):

```powershell
dotnet publish src\Merkautomat\Merkautomat.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist\Merkautomat-win-x64
```

- Release-Build und Tests sind Pflicht nach jeder Codeaenderung. Immer Release, nie Debug. Vor dem Build `taskkill /IM Merkautomat.exe /F`.
- SDK per `global.json` auf 10.0.4xx (`latestFeature`). `Directory.Build.props` legt Ausgaben per Hardlink an, damit die Modelle nur einmal auf der Platte liegen; eine Datei in der Build-Ausgabe nie direkt aendern, sie ist dieselbe wie die Quelle unter `models\`. `Directory.Build.targets` (`RemoveNonWindowsRuntimes`) laesst native Bibliotheken anderer Betriebssysteme weg.
- Modelle laedt der Build bei Fehlen nach `models\whisper` und `models\e5-small` (gepinnte Revisionen, SHA256-Pruefung); `models\`, `dist\` und der Inhalt von `tools\` sind in `.gitignore`.
- Tests: 776 (xUnit v3 4.0.2 auf Microsoft Testing Platform), in `Unit/`, `Integration/` (Dateien, Datenbank, Registry, Windows) und `UI/`; Helfer in `Support/`. `TestDataKey` setzt per Modulinitialisierer einen zufaelligen Datenschluessel; Tests, die den prozessweiten Schluessel wechseln, laufen in der Sammlung `DataKeyFileTests` ohne Parallelisierung. Dateien aus Stores mit `SecureFile.ReadAllText` lesen. Fenster-Tests laufen in `StaTestRunner.Run` und holen die Application ueber `TestApplication.Ensure()` (nie `new Application()`: beim Standard-ShutdownMode ignoriert WPF nach dem ersten geschlossenen Fenster jedes weitere `Show()`). Ein Test laeuft nur mit `MERKAUTOMAT_E5_DOWNLOAD=1` (Netzwerk).
- Abhaengigkeiten: keine verwundbaren Pakete (auch transitiv). Bei neuen NuGet-Warnungen Status in `docs/STATUS.md` dokumentieren und separat per Update bereinigen.

## Repository

```text
src/Merkautomat/            App: App.xaml(.cs) und App/ (Start, Tray, Hotkeys, Hintergrundarbeit, UiTasks),
                            Features/ (Clipboard, Search, KnowledgeBase, Categories, Notes, Voice, Recordings,
                            Tasks, Settings), Infrastructure/ (Persistence, Security, AI, Windows),
                            Core/ (Models, AppNames), UI/ (Views, ViewModels, Converters), Assets/, Licenses/
src/MerkautomatOutlook/     Outlook-COM-Add-in (net48), wird vom Hauptprojekt mitgebaut
tests/Merkautomat.Tests/    Unit/, Integration/, UI/, Support/
docs/                       siehe docs/README.md
tools/, packaging/, .github/workflows/   lokale Werkzeuge bzw. Platzhalter fuer spaeter
models/, dist/              vom Build geladen bzw. erzeugt, nicht versioniert
data/                      lokale Benutzerdaten und nachgeladene Modelle, nicht versioniert
```

Neue Dateien in den passenden Bereich legen. Namespaces folgen den Ordnern bewusst nicht (`Merkautomat.Services` fuer `Features/` und `Infrastructure/`, `Merkautomat.Views` fuer `UI/Views`, `Merkautomat.Models` fuer `Core/Models`).

## Dokumentation

Wegweiser: `docs/README.md`. Vor Aenderungen an einem Bereich die passende Datei unter `docs/features/` lesen und danach mitpflegen; dort stehen die fachlichen Regeln (Popup, Ideenkombinat, Brain, Kategorien, lokale KI, Aufgaben, Outlook). Ergebnisse und manuelle Pruefungen in `docs/STATUS.md`, offene Punkte in `docs/ROADMAP.md`, neue Grundsatzentscheidungen als Datei in `docs/decisions/`. Keine neuen Tagesdateien fuer Fixes oder Reviews.

## Wichtige Pfade (Daten)

| Was | Pfad |
| --- | --- |
| History-DB (verschluesselt) | `data\history.db` |
| Vektorindex der Suche (kein Text) | `data\semantic.db` |
| Datenschluessel (DPAPI plus Notschluessel-Verpackung) | `data\datakey.json` |
| Einstellungen | `data\settings.json` |
| Log | `data\merkautomat.log` |
| Bildeditor-Stilspeicher | `data\editor-styles.json` |
| Onboarding-Marker | `data\onboarding.seen` |
| Bildschirmaufnahmen (Markdown) | `data\recordings\*.md` |
| Sprachnotizen (Markdown) und KB-Kopien | `data\notes\*.md`, `data\kb\notes\*.md` |
| Aufgaben | `data\tasks.json` |
| Uebergabeordner des Outlook-Add-ins | `data\inbox\*.json` (unlesbare nach `inbox\fehler`), Format `docs/api/outlook-uebergabe.md` |
| Verschluesselte CPU-/GPU-Messprofile | `data\ai-execution-benchmarks.json` |
| Zwischenstand der Hintergrund-Kategorieanalyse | `data\background-discovery.json` |
| Mein Wissen (verschluesselte Textkopien) | `data\my-knowledge.json` |
| Vorschlaege feinerer Kategorien aus der Hintergrundarbeit | `data\category-refinement.json` |
| Whisper Small/Medium, E5-Fallback, MiniLM, Horizon, Granite, Qwen (Download bei Bedarf) | `data\models\` |
| Mitgelieferte Modelle neben der EXE | `models\ggml-base.bin`, `models\e5-small\` |
| Installierte Kopie des Outlook-Add-ins | `data\outlook\MerkautomatOutlook.dll` |

## Regeln

Allgemein:

- Kein MVVM-Refactoring ohne expliziten Auftrag; Code-behind ist bewusste Wahl (`docs/decisions/0002-code-behind-und-teilansichten.md`). Fachlogik gehoert nach `Features/` bzw. `Infrastructure/`, nicht ins Fenster.
- Keine neuen Abhaengigkeiten ohne Rueckfrage.
- Kommentare nur fuer nicht offensichtliches Warum. Keine Emojis in Code, Docs oder Ausgaben. Keine Herkunfts-, Tool- oder Arbeitsprosa in Projektdateien.
- Bestehende User-Aenderungen nicht revertieren; bei Konflikten mit ihnen arbeiten. Bei veraenderter Arbeitskopie knapp den relevanten Status nennen.

Windows und Sicherheit:

- Kein Screen-Capture per PowerShell, GDI, WinAPI oder UI Automation; UI-Pruefung als Checkliste fuer den User.
- Keine Administratorrechte, nur HKCU und Benutzerordner. Packaging-Modell, Zielframework und Laufzeitversion nicht nebenbei aendern. Signatur-, Hash- und Download-Pruefungen nie abschwaechen.
- Inhalte immer verschluesselt speichern: neue Lese-/Schreibstellen ueber `SecureFile` bzw. `EncryptedDatabase`, nie direkt `File.ReadAllText`/`WriteAllText` oder eine `SqliteConnection` ohne Passwort. Details `docs/security/verschluesselung.md`.
- Links nur ueber `WebLink.TryOpen` (http(s)). Nie Inhalte ins Log schreiben.
- Datenbank und Dateiformate wachsen nur: neue Spalten mit Standardwert, neue Tabellen, neue optionale Felder; nichts umbenennen oder entfernen, keine Versionsnummer (`docs/decisions/0004-datenbank-waechst-nur.md`). So oeffnet jede Version die Daten jeder anderen.

Code:

- Fehlerbehandlung: kein leeres oder untypisiertes `catch`. Erwartete Fehler (Zwischenablage belegt `ExternalException`, kaputte Bilder `ImageUtils.IsDecodeFailure`, Netzwerk `UrlPreviewService.IsFetchFailure`, Datei gesperrt, ungueltige Eingabe) typisiert ohne Log abfangen; alles andere, von dem sich der Aufrufer erholt, mit `App.LogError("was versucht wurde", ex)` protokollieren. Nur enger fangen ohne Fallback ist keine Loesung: `DispatcherUnhandledException` beendet die App.
- `async void` nur in `UiTasks.Forget`, auch keine `async`-Lambdas an Ereignissen. Event-Handler und angestossene UI-Arbeit als `async Task ...Async` schreiben und per `XAsync().Forget("Was der Nutzer versucht hat")` starten.
- Datenbankarbeit nicht auf dem UI-Thread, wenn ein Hintergrund-Thread das Gate von `DatabaseService` halten kann (Backup, Import): `PopupViewModel` fuehrt Hinzufuegen, Purge und FTS-Suche ueber `_runDb` aus (in Tests `PopupViewModel.InlineDatabase`, Hinzufuegen per `vm.AddNow(...)`).
- Grosse Fenster abschnittsweise als UserControl mit kleiner Schnittstelle (Muster `TasksPanel`). Gemeinsame Stile in ein ResourceDictionary ohne Event-Handler (`KnowledgeBaseStyles.xaml`); ein UserControl findet `StaticResource` aus den Fensterressourcen beim Laden nicht.
- Barrierefreiheit: Bedienelemente ohne sichtbaren Text brauchen `AutomationProperties.Name` (der Tooltip zaehlt nur als Hilfetext); aendert der Code den Tooltip, auch den Namen setzen. Selbst gezeichnete Steuerelemente brauchen einen AutomationPeer und Tastaturbedienung; `FocusVisualStyle` nicht abschalten.
- Meldungen und Rueckfragen nie mit `System.Windows.MessageBox`, sondern mit `AppMessageBox.Show` (gleiche Signatur), per `using MessageBox = Merkautomat.Views.AppMessageBox;`.

## Bekannte WPF-Fallstricke

| Problem | Loesung |
| --- | --- |
| `TextBlock.MaxLines` existiert nicht in WPF | `MaxHeight` und `ClipToBounds=True` auf Container |
| `StackPanel.Spacing` erst ab .NET 9 | `Margin` auf Child-Elementen |
| Namespace-Konflikte bei WPF + WinForms (`KeyEventArgs`, `MessageBox`, `UserControl` ...) | using-Aliases oder vollqualifizierte Typen |
| `DataTemplate.Triggers` mit externen Namen | Named Elements innerhalb des Templates verwenden |
| Hover-Zustand in DataTemplate | `IsHovered` im ViewModel plus `DataTrigger` |
| `App.xaml` ausserhalb des Projektstamms | Wird nicht als Anwendungsdefinition erkannt; bleibt im Stamm von `src\Merkautomat` |

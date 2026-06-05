# Clipwell Windows – Projektstatus

Letztes Update: 2026-06-05 (Runde 6)

## Abgeschlossene Features (Runde 1)

### P1 – Qualität und Bedienung
- [x] **History-Filter-Chips** – Filter nach Typ (Text, Code, URL, Bild, Farbe, Gepinnt) direkt im Popup
- [x] **Bulk-Aktionen** – Mehrfachauswahl per Checkbox, Pinnen/Lösen/Exportieren/Löschen
- [x] **Bessere Detailansicht** – Copy-Button, Zeilenumbruch-Toggle, OCR-Text-Toggle für Bilder
- [x] **URL-Preview-Zustände** – Loading/Loaded/Failed-Indikator in der Liste, manueller Reload im Kontextmenü
- [x] **Bilddetails** – Auflösung, Format, Dateigrösse und OCR-Status im DetailWindow

### P2 – Daten und Export
- [x] **Export als JSON** – komplette History exportieren (Popup: nur Auswahl; Einstellungen: alles)
- [x] **Export als SQLite-Backup** – DB-Datei kopieren via Einstellungen
- [x] **Import aus JSON** – History aus Backup wiederherstellen
- [x] **Reset-Dialoge** – History löschen (opt. gepinnte behalten), URL-Cache löschen, Einstellungen zurücksetzen
- [x] **Inkognito-Modus** – global, per App (Prozessname) und per Domain
- [x] **Sensible Inhalte filtern** – Erkennt API-Keys, Tokens, Passwörter, Private Keys (15+ Muster)
- [x] **Erweiterte Speicherlimits** – Maximales Alter (Tage) und maximale DB-Grösse (MB)

### P3 – UI und Workflow
- [x] **Popup-Grösse und -Position merken** – Resize-Griff, Speichern beim Schliessen, optional Position merken
- [x] **Erweiterte Suche** – Präfixe `type:`, `kind:`, `domain:`, `pinned:`
- [x] **Hover-Aktionsleiste** – Icon-Buttons (Kopieren, Pinnen, Löschen, Details) pro Eintrag
- [x] **Theme-Optionen** – System / Dunkel / Hell / Kompakt (hell)
- [x] **Onboarding** – Willkommensfenster beim ersten Start mit Hotkey-Test und Feature-Überblick
- [x] **Onboarding-Hotkey verdrahtet** – `_onboarding`-Instanz in App gespeichert; `NotifyHotkeyTriggered()` wird in `OnHotkeyPressed` aufgerufen
- [x] **Compact-Theme Paddings** – DynamicResource-Schlüssel für `EntryItemMargin`, `EntryContentMargin`, `EntryMinHeight`, FontSizes, `FilterChipPadding`; `ApplyTheme()` schaltet die Werte um
- [x] **Bulk-Export gesamte History** – Wenn keine Einträge ausgewählt: Dialog zum Exportieren der gesamten History via `DatabaseService.ExportAsJson`
- [x] **Edge-Resize PopupWindow** – `ResizeMode="CanResize"` + WndProc-Hook (WM_NCHITTEST) für Links/Rechts/Unten-Kanten und -Ecken
- [x] **Run.Text-Binding-Fix** – `Mode=OneWay` auf `SelectedCount`-Binding (verhindert TwoWay-Fehler bei read-only Property)
- [x] **Solution-Datei** – `ClipwellWin.sln` erstellt (behebt VS Code NuGet-Restore-Warnung)

### P1 – Qualitätsstufe
- [x] **Zeilennummern im Code-Viewer** – `SyntaxService.Highlight()` nutzt FlowDocument-Table: linke Spalte Zeilennummern (grau), rechte Spalte Syntax-Highlighting; `PageWidth=10000` für horizontales Scrollen
- [x] **Search-Autocomplete** – Popup unter Suchfeld mit Vorschlägen für `type:`, `kind:`, `domain:`, `pinned:`; dynamische kind:/domain:-Werte aus vorhandenen Einträgen; ↓ wechselt in die Vorschlagsliste, Esc schliesst
- [x] **Timestamp-Gruppierung** – Eintrags-Liste gliedert sich in Gruppen: Gepinnt / Heute / Gestern / Diese Woche / Früher (via `EntryViewModel.GroupLabel` + WPF `PropertyGroupDescription`)
- [x] **Farb-Eintrag Details** – DetailWindow zeigt grosser Farbswatch + tabellarische Werte (HEX, RGB, HSL, CSS, Tailwind) je mit eigenem Kopieren-Button
- [x] **Bild-Zoom und -Pan** – DetailWindow: Bild in `ScrollViewer + ScaleTransform`, Mausrad-Zoom (0.1×–8×), Zoom-Reset-Button, Zoom-%-Anzeige

### P2 – Daten und Sync
- [x] **Automatische JSON-Backups** – `AppSettings.AutoBackupEnabled/AutoBackupDirectory/LastAutoBackupDate`; App prüft beim Start ob Backup fällig; `DatabaseService.AutoBackupIfNeeded()` rotiert auf 7 Dateien
- [x] **Import-Deduplizierung** – `ImportFromJson` prüft vor jedem Insert ob Timestamp + Content bereits existiert und überspringt Duplikate
- [x] **Sicheres Löschen (VACUUM)** – `DatabaseService.SecureDelete(id)` + Kontextmenü-Eintrag „Sicher löschen (VACUUM)" im Popup; VACUUM läuft im Hintergrund-Thread

### P3 – UI und Workflow
- [x] **Tab-Tastatur-Navigation** – Tab im Suchfeld: Fokus auf ersten Filter-Chip; Tab im Chip: Fokus auf Liste; Esc in Chip: zurück zum Suchfeld
- [x] **Drag & Drop** – ListBox-Einträge können per Maus in andere Apps gezogen werden (Text-DnD mit `DataFormats.UnicodeText`)
- [x] **Schnellnotiz** – „+"-Button im Popup-Header öffnet Schnellnotiz-Panel; Eingabe wird als gepinnter Text-Eintrag mit ContentKind „NOTE" gespeichert; Enter bestätigt, Esc verwirft


- [x] **Compact-Theme Typo-Tuning** – `EntryFontWeightMain/Meta`, `BadgeFontSize`, `BadgePadding` als DynamicResources in `ApplyCompactResources()`
- [x] **Code-Viewer Lazy-Highlighting** – `SyntaxService.LazyThreshold = 300`; `HighlightCore()` rendert initial 300 Zeilen; „Restliche X Zeilen laden"-Button in DetailWindow ergänzt Rest via `Dispatcher.InvokeAsync(Background)`
- [x] **Auto-Backup Einstellungs-UI** – CardExpander „Automatisches Backup" im Tab „Daten"; ToggleSwitch + `OpenFolderDialog` + Status-Label mit letztem Backup-Datum
- [x] **Popup-Drag Edge-Cases** – `e.Handled = true`, `MouseButtonState.Pressed`-Check, Position nach Drag in Settings speichern

### P1 – Qualitätsstufe
- [x] **Farb-Picker Eyedropper** – `EyedropperWindow`: vollbild-transparentes WPF-Overlay; GDI `GetDC/GetPixel` liest Bildschirmpixel; Lupe mit Hex+RGB-Vorschau nahe Cursor; Linksklick = auswählen, Esc = abbrechen; Ergebnis als neuer Color-Eintrag in History + in Clipboard; Eyedropper-Button (&#xE790;) im Popup-Header

### P2 – Daten und Sync
- [x] **Auto-Backup Einstellungs-UI** – (siehe Fixes oben)

### P3 – UI und Workflow
- [x] **Pinboard-Ansicht** – `PinboardWindow.xaml/.cs`: kompaktes Always-on-Top-Fenster; aktualisiert sich live über `Entries.CollectionChanged`; Eintrag einfügen per Klick; toggle via Tray-Menü „Pinboard" oder `Ctrl+B` im Popup
- [x] **Tastenkürzel konfigurierbar** – Neuer Tab „Tastenkürzel" in SettingsWindow; Pin, Details (F2), Schnellnotiz, Pinboard jeweils eigene Taste + Ctrl-Modifier; `MatchShortcut()` in PopupWindow; gespeichert in `AppSettings`
- [x] **App-Icon** – Tray-Icon wird per GDI+ aus einem Clipboard-mit-Herz-Motiv erzeugt; optimiert für kleine Windows-Tray-Größen.

### Technische Schulden (bereinigt)
- [x] **`ContentKindService.PrimaryBadge`** – gibt jetzt `entry.ContentKind ?? "TEXT"` zurück; NOTE, JSON, XML, YAML etc. erscheinen als Badge statt pauschal „TEXT"; IMG-Einträge zeigen Format (PNG, JPG …)
- [x] **`SyntaxService.Highlight` zu langsam bei langen Dateien** – behoben via Lazy-Threshold (siehe oben)
- [x] **Auto-Backup ohne UI** – behoben (siehe oben)


### Fixes aus manuellem App-Test
- [x] **System-Theme beim Start und bei Windows-Themewechseln** – Standard bleibt `System`; Light/Dark wird aus Windows gelesen, dynamische Popup-Ressourcen werden aktualisiert und offene Popup-Fenster bekommen den passenden Window-Tint.
- [x] **Größeres Popup ohne sofortiges Scrollen** – Standardgröße auf 560 × 720 erhöht, Mindestgröße auf 500 × 620 gesetzt und Position/Größe an den Arbeitsbereich des Monitors geklemmt.
- [x] **Hotkey-Registrierung robuster** – Modifiers werden normalisiert, Hotkeys ohne Modifier werden verhindert, blockierte Kombinationen bekommen automatische Fallbacks (`Win+Shift+V`, `Ctrl+Shift+V`, `Ctrl+Alt+V`, `Win+Alt+V`), Recorder speichert erst beim Übernehmen.
- [x] **Stabiles Scrollen im Popup** – gruppierte Listbox virtualisiert mit Recycling, Pixel-Scrolling und eigener Wheel-Behandlung; Hover-Aktionsspalte hat feste Breite und ändert beim Scrollen nicht mehr das Layout.
- [x] **Bild-Clipboard-Crash behoben** – Clipboard-Schreibzugriffe für Text und Bilder laufen mit Retry und Fehlerlogging; UI-Dispatcher-Fehler werden geloggt und beenden die Tray-App nicht mehr.
- [x] **Bild-Badge-Regression behoben** – Hauptbadge für Bilder ist wieder `IMG`, Detailbadge zeigt das konkrete Format (`PNG`, `JPG` usw.).


- [x] `dotnet build ClipwellWin\ClipwellWin.csproj` erfolgreich (0 Warnungen, 0 Fehler)
- [x] `dotnet test ClipwellWin.Tests\ClipwellWin.Tests.csproj` erfolgreich (19/19 grün)
- [x] Gebaute App kurz gestartet; Prozess lief und es wurden keine neuen Crashlog-Bytes geschrieben.


### Ausgiebiger Funktionstest
- [x] **Echter App-Lauf mit frischer Test-History** – Text, URL, Farbe, SQL, PowerShell, XML, Properties, ENV, JSON, Dockerfile und PNG-Bild über die echte Windows-Clipboard-API geschrieben und anschließend direkt in SQLite geprüft.
- [x] **Single-Instance und Popup-Pfad** – zweiter App-Start beendet sich wieder; ein sichtbares `Clipwell`-Popup-Fenster bleibt im primären Prozess.
- [x] **Bild-Dedupe nach Runtime-Befund** – ein einzelnes Bild-Clipboard-Set erzeugte zwei PNG-Einträge; `PopupViewModel.AddEntry` dedupliziert jetzt identische neueste Bilddaten.
- [x] **Detailfenster-Smoke-Tests** – Text-, Code-, Farb- und Bilddetails werden konstruiert und gelayoutet.
- [x] **Datenbanktests erweitert** – JSON-Export/Import mit Dedupe, `ClearHistory(keepPinned)`, URL-Cache und `SecureDelete`.
- [x] **Datenschutztests ergänzt** – API-Key, Bearer Token, Passwortzuweisung und Private-Key-Block werden erkannt; normaler Text wird nicht gefiltert.
- [x] **Echte AppData wiederhergestellt** – `%APPDATA%\Clipwell` wurde vor dem Runtime-Test gesichert und danach zurückgestellt.


- [x] `dotnet build ClipwellWin\ClipwellWin.csproj` erfolgreich (0 Warnungen, 0 Fehler)
- [x] `dotnet test ClipwellWin.Tests\ClipwellWin.Tests.csproj` erfolgreich (33/33 grün)
- [x] Runtime-History nach Clipboard-Test: 11 erwartete Einträge, Typen `Text`, `Url`, `Color`, `Code`, `Image`; genau ein PNG-Bildeintrag; kein Crashlog.

---

## Abgeschlossene Tray-Icon-Anpassung (Runde 6)

### Icon
- [x] **Systemtray-Icon ersetzt** – `CreateTrayIcon()` rendert ein kleines Clipboard-mit-Herz-Motiv mit dunklem Board, weißem Blatt, Metallclip, grauen Zeilen, rotem Herz und Papier-Ecke.
- [x] **Icon-Konvertierung für Tray-Größen** – Motiv wird als 64×64 ARGB-Bitmap sauber gezeichnet und über HICON an `NotifyIcon` übergeben; native Icon-Handles werden wieder freigegeben.

### Verifikation Runde 6
- [x] `dotnet build ClipwellWin\ClipwellWin.csproj` erfolgreich (0 Warnungen, 0 Fehler)
- [x] `dotnet test ClipwellWin.Tests\ClipwellWin.Tests.csproj` erfolgreich (33/33 grün)
- [x] Gebaute App kurz gestartet; Tray-Icon-Pfad lief durch und es wurden keine neuen Crashlog-Bytes geschrieben.

---

## Nicht umgesetzt (nächste Runden)

### P2
- [ ] Cloud-Sync (OneDrive/iCloud als SQLite-Sync-Ziel)

### P4
- [ ] End-to-end UI-Tests
- [ ] Datenbank-Recovery- und Migrations-Tests
- [ ] Mockbarer URL-Preview-Service für Tests
- [ ] Installer / portable Release-Paket
- [ ] Versionierte Release Notes
- [ ] Unit-Test-Projekt (minimal: SensitiveContentService, SyntaxService.DetectLanguage)

## Tech-Stack (unverändert)
- .NET 8 / WPF + WinForms (NotifyIcon)
- WPF-UI 4.3.0 (Lepo)
- Microsoft.Data.Sqlite 8.0.10
- WinRT OCR (Windows.Media.Ocr)
- SQLite WAL-Mode, lokale DB unter `%APPDATA%\Clipwell\history.db`
- Testsuite: xUnit 2.9.2 (33 Tests, alle grün)

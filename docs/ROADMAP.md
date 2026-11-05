# Clipwell Windows - Roadmap und offene Punkte

Stand: 2026-11-05

Diese Datei ist die zentrale Ablage fuer offene Produkt-, Technik- und Release-Punkte. Kurzlebige Review-, Bug- und Feature-MDs werden hier konsolidiert und nicht separat weitergefuehrt.

## Naechste sinnvolle Arbeit

| Prioritaet | Thema | Stand |
| --- | --- | --- |
| Hoch | Installer-/Release-Format festlegen: MSIX, NSIS oder portable ZIP | Offen |
| Hoch | Release-Pipeline mit versionierten Artefakten und Release Notes | Offen; portabler Single-File-Build per `dotnet publish -r win-x64` dokumentiert in `CLAUDE.md` |
| Mittel | Code-Signing fuer Binary und Installer | Offen |
| Mittel | Bildeditor manuell auf DPI-, Zoom- und Multi-Monitor-Setups pruefen | Offen |
| Mittel | UI-Interaktionstests fuer Maus/Tastaturpfade ergaenzen | Offen |
| Niedrig | URL-Preview-Koordination in `App.OnClipboardChanged` und `App.ReloadUrlPreview` ggf. extrahieren | Beobachten |

## Feature-Luecken

Abgeleitet aus Code und Handbuch (Stand 2026-11-05), nicht aus Nutzertests. Prioritaet nach Nutzen fuer einen Clipboard-Manager im Alltag.

### Datenschutz und Sicherheit

| Prioritaet | Feature | Ist-Zustand | Hinweis |
| --- | --- | --- | --- |
| Hoch | Clipboard-Ausschlussformate respektieren (`ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory`=0, `Clipboard Viewer Ignore`) | Nicht vorhanden; `ClipboardProcessor.BuildEntry` liest jeden Inhalt. Passwortmanager (KeePass, Bitwarden, 1Password) setzen diese Formate, Passwoerter landen sonst in der History. | Pruefung vor `BuildEntry` in `App.OnClipboardChanged`, kleiner Eingriff |
| Hoch | App-Ausschlussliste (Prozessname des Clipboard-Besitzers via `GetClipboardOwner`/`GetWindowThreadProcessId`) | Nicht vorhanden; nur globale Pause | Einstellung "Diese Apps nie aufzeichnen" |
| Mittel | Sensible Inhalte erkennen (Kreditkarte mit Luhn-Pruefung, IBAN, API-Keys/Tokens, JWT) | Nicht vorhanden | Optionen: nicht speichern, maskiert anzeigen oder nach N Minuten loeschen |
| Mittel | Verschluesselung der History-DB im Ruhezustand | Klartext-SQLite in `%APPDATA%` | DPAPI fuer Inhaltsspalten oder SQLCipher (neue Abhaengigkeit, Rueckfrage noetig) |
| Niedrig | Inkognito-Timer ("Pause fuer 15 Minuten") | Pause nur manuell an/aus | Tray-Untermenue |

### Erfassung

| Prioritaet | Feature | Ist-Zustand | Hinweis |
| --- | --- | --- | --- |
| Mittel | RTF-Formatierung erhalten (z. B. aus Word) | HTML ist umgesetzt (`HtmlContent`, Einfuegen mit Formatierung, Markdown-Aufbereitung); RTF wird weiterhin zu Klartext reduziert | Eigene Spalte oder RTF nach HTML wandeln |
| Mittel | Bibliotheksansicht fuer Webinhalte, gruppiert nach Webseite (`SourceUrl`) | Webinhalte sind pro Eintrag im Detailfenster sichtbar, aber nicht gesammelt | Liste nach Host, Suche, Markdown-Export pro Seite; optional Anbindung der Chrome-Erweiterung per Native Messaging |
| Mittel | Dateien und Ordner aus dem Explorer erfassen (`FileDrop`) | Nur per Drop ins Pinboard, nicht beim Kopieren | Neuer `EntryType` oder ContentKind mit Pfadliste; Einfuegen als `FileDrop` |
| Mittel | Quell-App und Fenstertitel pro Eintrag speichern | Nicht vorhanden | Ermoeglicht Suchpraefix `app:` und App-Ausschlussliste |

### Einfuegen und Arbeitsablauf

| Prioritaet | Feature | Ist-Zustand | Hinweis |
| --- | --- | --- | --- |
| Hoch | Schnell-Einfuegen per Nummer (Ctrl+1 bis Ctrl+9 bzw. Ziffern im Popup) | Nicht vorhanden; nur Pfeiltasten plus Enter | Nummern in der Liste anzeigen |
| Mittel | Einfuegen mit Umwandlung (GROSS/klein, Trim, JSON formatieren, URL-/Base64-Kodierung, Zeilen sortieren) | Nicht vorhanden | Untermenue im Kontextmenue plus Shortcut |
| Mittel | Paste-Stapel (mehrere Eintraege nacheinander einfuegen) | Bulk-"Kopieren" verbindet Texte mit `---`; kein sequenzielles Einfuegen | Eigener Modus mit Hotkey |
| Mittel | Snippets/Vorlagen mit Platzhaltern (Datum, Cursorposition) | Notizen als Markdown-Dateien, keine Platzhalter | Kann auf `NoteStore` aufbauen |
| Niedrig | Weitere globale Hotkeys (Pinboard, Eyedropper, Paste-Stapel) | Ein globaler Hotkey; Pinboard nur ueber Tray oder Popup | Hotkey-Recorder kann zusaetzlich keine F-Tasten aufnehmen |

### Organisation und Suche

| Prioritaet | Feature | Ist-Zustand | Hinweis |
| --- | --- | --- | --- |
| Mittel | Loeschen rueckgaengig machen (Undo-Toast oder Papierkorb) | Loeschen sofort endgueltig | Kurzer Undo-Hinweis im Popup reicht |
| Mittel | Tags oder Ordner fuer gepinnte Eintraege | Nur Pin plus manuelle Reihenfolge im Pinboard | Filter-Chip und Suchpraefix `tag:` |
| Niedrig | Suchpraefix fuer Zeitraum (`date:today`, `date:>2026-10-01`) und Fuzzy-Suche | Nur Typ, Kind, Domain, Pinned, Volltext, Regex | Erweiterung von `PopupViewModel.Filter` |
| Niedrig | Onboarding erneut aufrufbar | Erscheint nur einmal | Button in Einstellungen, "Ueber" |

### Plattform und Qualitaet

| Prioritaet | Feature | Ist-Zustand | Hinweis |
| --- | --- | --- | --- |
| Mittel | Update-Pruefung gegen GitHub-Releases | Nicht vorhanden; "Ueber" verlinkt nur GitHub | Erst sinnvoll mit Release-Pipeline |
| Mittel | Barrierefreiheit: `AutomationProperties.Name` fuer Icon-Buttons, Kontrastmodus | Keine `AutomationProperties` im Code | Screenreader liest Icon-Buttons sonst ohne Namen |
| Niedrig | Mehrsprachige UI (mindestens Englisch) | UI nur Deutsch, README Englisch | Ressourcen-Dateien statt fester Strings |
| Niedrig | Echter Gaussian Blur im Bildeditor | Graue Ueberlagerung | Siehe Bekannte Grenzen in `STATUS.md` |
| Mittel | Desktop-Aufnahme: Ausschlussliste fuer Apps/Fenstertitel und einstellbare Ruhezeit | Feste Werte (500 ms Takt, 500 ms Ruhe, 2,5 s Nachlesen), nur Clipwell und Passwortfelder ausgeschlossen | Settings-Seite "Aufnahme" |
| Niedrig | Desktop-Aufnahme: optionaler OCR-Snapshot fuer textlose Inhalte | Nur UI Automation | Pixelaufnahme kann Corporate-Screen-Capture-Alarme ausloesen; nur als expliziter Einzelklick |
| Niedrig | KB-Fenster: gerenderte Markdown-Vorschau | Rohes Markdown (Suche, Filter, Gruppierung und Mehrfachexport vorhanden) | Renderer aus dem Detailfenster wiederverwenden |
| Mittel | Diktat: Live-Text waehrend des Sprechens und globaler Diktat-Hotkey | Transkription erst nach dem Stoppen, Start per Button/Strg+D/Tray | Whisper in Abschnitten (z. B. 5 s mit Ueberlappung) verarbeiten |
| Niedrig | Notizen: Markdown-Vorschau, Tags und Uebernahme alter `NOTE`-History-Eintraege | Rohtext-Editor, alte Schnellnotizen bleiben in der History | Migration nur auf Wunsch |

Bekannte Grenzen stehen zentral in `STATUS.md`.

## Erledigt und konsolidiert

- Alte Tagesdateien `docs/next-feature.md`, `docs/refactoring-2026-06-07.md` und `docs/review-2026-06-07.md` wurden in `STATUS.md` und diese Roadmap ueberfuehrt.
- Fruehere Refactoring-Punkte zu Regex-Caching, Clipboard-Schreibpfad, Thumbnail/Favicon-Laden und Monitor/DPI-Helfern sind umgesetzt.
- Favicon-Fetches sind groessenlimitiert und URL-Preview-Fetches blockieren private, Loopback- und Link-Local-Ziele.
- JSON-Export/Import erhaelt Nutzung, Pin-Reihenfolge, LastUsedAt und ThumbnailData.

## Nicht geplant

- Cloud-Sync direkt auf der Live-SQLite-Datenbank ueber OneDrive/iCloud.

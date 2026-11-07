# Clipwell Windows - Roadmap und offene Punkte

Stand: 2026-11-07

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
| Mittel | Semantische Suche: Schwellen (0,80/0,83/0,97) mit echten Daten pruefen und ggf. nachjustieren | Offen; Stufe 1 (Embeddings, hybride Suche, Aehnliche, Dubletten) umgesetzt |
| Mittel | Brain: Schwelle (0,86), Themenbildung und Stichworte mit echten Daten pruefen | Offen; Stufe 2 (Wissensnetz aus Inhalt, Zeit, Aufnahme und Webseite, Themen, Darstellung) umgesetzt, Kanten werden live aus dem Index berechnet statt in SQLite gespeichert |
| Niedrig | Brain: Zeitachse als zweite Ansicht, Export des Netzes (z. B. Obsidian-Links) | Offen; manuelle gerichtete Beziehungen mit Begruendung sind umgesetzt (`BrainRelations`) |
| Niedrig | Brain: mehr als 1000 Knoten (Barnes-Hut statt O(n^2)-Abstossung, Kanten-Bucketing bleibt) | Offen; 1000 Knoten: Aufbau 0,1 s, Layout 0,4 s |
| Niedrig | KB Stufe 3: Entitaeten per lokalem LLM | Zurueckgestellt; erst wenn Stufe 1 und 2 nicht reichen (LLM-Groesse, Qualitaet bei kurzen Clips) |
| Niedrig | Themen aus dem Brain im Dashboard | Offen |
| Mittel | Fragen in der Suche: Antwortqualitaet und Wartezeit von Granite 3B mit echten Fragen pruefen (Anzahl Quellen 6, 800 Zeichen je Quelle ggf. anpassen) | Offen; umgesetzt mit Belegpflicht und Quellen-Knoepfen |
| Niedrig | Aufgaben auch aus Sprachnotizen, Bildschirmaufnahmen und Suchtreffern anlegen; Aufgaben aus Diktaten per Granite vorschlagen (nur nach Bestaetigung) | Offen; heute aus Zwischenablage-Eintraegen (Popup-Kontextmenue) und manuell |
| Niedrig | Aufgaben: Erinnerung bei Faelligkeit (Tray-Hinweis), offene Aufgaben im Dashboard | Offen |
| Niedrig | Outlook-Add-in: Mail aus der Aufgabe wieder in Outlook oeffnen (EntryID/StoreID werden schon uebergeben), optional Kontextmenue auch fuer Termine | Offen; erst nach Praxistest des Add-ins in Outlook |
| Mittel | Kategorien: Auswertung der Unterkategorien je Hauptkategorie (Kreuzvalidierung wie fuer Hauptkategorien, Kalibrierung der Unterkategorie-Schwelle) | Offen; Hauptkategorien werden ausgewertet, Unterkategorien nutzen feste Regeln (Rocchio ab 3, Nachbarn-Einigkeit bei 2) |
| Niedrig | Einordnung: Kalibrierte Schwelle in eigener Validierungsmenge statt in derselben Kreuzvalidierung bestimmen (geschachtelte Kreuzvalidierung) | Offen; derzeit leicht optimistische Schaetzung |
| Niedrig | Einordnung: korrigierte Vorschlaege als explizite Gegenbeispiele der falschen Kategorie (Rocchio gamma) und nur nach Auswertung uebernehmen | Offen; Gegenbeispiele heute aus den aehnlichsten fremden Beispielen |
| Niedrig | Einordnung: gewichtetes k-NN (Kosinus-Gewichte, k validiert) und lineare SVM als weitere Vergleichsverfahren | Offen; logistische Regression deckt lineare Grenzen ab |
| Niedrig | Einordnung: Domain/Quelle als Prior (P(Kategorie gegeben Domain) aus gespeicherten Zuordnungen) in Rocchio bzw. logistischer Regression, nur nach Kreuzvalidierung im `ClassifierBenchmark` | Offen; Domain wird heute fuer die Einordnung nicht genutzt |
| Niedrig | Suche/Brain: Zentrierung wie in Rocchio fuer "Aehnliche" und Brain-Kanten; Schwellen 0,80/0,83/0,86 muessten neu bestimmt werden | Offen; nur mit Vorher-Nachher-Messung |
| Niedrig | Suche: "Passt nicht" als Gegenbeispiele in "Aehnliche zu Markierten" (Rocchio gamma) | Offen; braucht eine zweite Markierungsart in der Oberflaeche |
| Zurueckgestellt | Weitere Encoder (Qwen3-Embedding-0.6B, BGE-M3, Granite Embedding), SetFit, Mehrfach-Kategorien | Neue Modelle/Abhaengigkeiten noetig; erst wenn die Auswertung zeigt, dass E5 die Grenze ist |

## Feature-Luecken

Abgeleitet aus Code und Handbuch (Stand 2026-11-05), nicht aus Nutzertests. Prioritaet nach Nutzen fuer einen Clipboard-Manager im Alltag.

### Datenschutz und Sicherheit

| Prioritaet | Feature | Ist-Zustand | Hinweis |
| --- | --- | --- | --- |
| Hoch | App-Ausschlussliste (Prozessname des Clipboard-Besitzers via `GetClipboardOwner`/`GetWindowThreadProcessId`) | Nicht vorhanden; nur globale Pause | Einstellung "Diese Apps nie aufzeichnen" |
| Mittel | Sensible Inhalte erkennen (Kreditkarte mit Luhn-Pruefung, IBAN, API-Keys/Tokens, JWT) | Nicht vorhanden | Optionen: nicht speichern, maskiert anzeigen oder nach N Minuten loeschen |
| Mittel | Verschluesselung der History-DB im Ruhezustand | Klartext-SQLite in `%APPDATA%` | DPAPI fuer Inhaltsspalten oder SQLCipher (neue Abhaengigkeit, Rueckfrage noetig) |
| Niedrig | Inkognito-Timer ("Pause fuer 15 Minuten") | Pause nur manuell an/aus | Tray-Untermenue |

### Erfassung

| Prioritaet | Feature | Ist-Zustand | Hinweis |
| --- | --- | --- | --- |
| Mittel | RTF-Formatierung erhalten (z. B. aus Word) | HTML ist umgesetzt (`HtmlContent`, Einfuegen mit Formatierung, Markdown-Aufbereitung); RTF wird weiterhin zu Klartext reduziert | Eigene Spalte oder RTF nach HTML wandeln |
| Mittel | Bibliotheksansicht fuer Webinhalte, gruppiert nach Webseite (`SourceUrl`) | Webinhalte sind pro Eintrag im Detailfenster sichtbar, aber nicht gesammelt | Liste nach Host, Suche, Markdown-Export pro Seite; optional Anbindung der Chrome-Erweiterung per Native Messaging |
| Niedrig | Dateien und Ordner aus dem Explorer erfassen (`FileDrop`) | Lesbare Textdateien werden mit Inhalt gespeichert (Liste in der Kontrollstation); andere Dateien und Ordner nicht | Pfadliste als Eintrag; Einfuegen als `FileDrop` |
| Mittel | Quell-App und Fenstertitel pro Eintrag speichern | Nicht vorhanden | Ermoeglicht Suchpraefix `app:` und App-Ausschlussliste |

### Einfuegen und Arbeitsablauf

| Prioritaet | Feature | Ist-Zustand | Hinweis |
| --- | --- | --- | --- |
| Mittel | Einfuegen mit Umwandlung (GROSS/klein, Trim, JSON formatieren, URL-/Base64-Kodierung, Zeilen sortieren) | Nicht vorhanden | Untermenue im Kontextmenue plus Shortcut |
| Niedrig | Eintrags-Hotkeys auch fuer Sprachnotizen und Pinstation-Gruppen, Hinweis beim Entpinnen eines Eintrags mit Hotkey | Offen; heute nur History-Eintraege, Zuweisung bleibt nach Loeschen als "Eintrag geloescht" sichtbar |
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
| Mittel | Diktat: Live-Text waehrend des Sprechens und globaler Diktat-Hotkey | Transkription erst nach dem Stoppen, Start per Button/Strg+D/Tray | Whisper in Abschnitten (z. B. 5 s mit Ueberlappung) verarbeiten |
| Niedrig | Notizen: Markdown-Vorschau, Tags und Uebernahme alter `NOTE`-History-Eintraege | Rohtext-Editor, alte Schnellnotizen bleiben in der History | Migration nur auf Wunsch |

Bekannte Grenzen stehen zentral in `STATUS.md`.

## Erledigt und konsolidiert

- Inhalte mit Ausschlussformaten (`ExcludeClipboardContentFromMonitorProcessing`, `Clipboard Viewer Ignore`, `CanIncludeInClipboardHistory` = 0) werden nie gespeichert (`ClipboardProcessor.IsExcludedFromHistory`), auch nicht beim Ablegen in der Pinstation.
- Globale Hotkeys fuer einzelne Eintraege: Rechtsklick im Popup "Hotkey zuweisen ..." (`EntryHotkeyDialog`), Verwaltung in Kontrollstation → Tastenkuerzel; fuegt in jedem Programm ein, Eintrag wird dabei gepinnt.
- Kategorien im Export: Zeile "Kategorie: Haupt › Unter" unter der Artikelueberschrift, Kategorie im Inhaltsverzeichnis, ZIP-Ordner je Haupt-/Unterkategorie (sobald ein Artikel eine Kategorie hat, sonst wie bisher nach Art).
- Gerenderte Markdown-Vorschau im Ideenkombinat (`MarkdownPreview`).
- Alte Tagesdateien `docs/next-feature.md`, `docs/refactoring-2026-06-07.md` und `docs/review-2026-06-07.md` wurden in `STATUS.md` und diese Roadmap ueberfuehrt.
- Fruehere Refactoring-Punkte zu Regex-Caching, Clipboard-Schreibpfad, Thumbnail/Favicon-Laden und Monitor/DPI-Helfern sind umgesetzt.
- Favicon-Fetches sind groessenlimitiert und URL-Preview-Fetches blockieren private, Loopback- und Link-Local-Ziele.
- JSON-Export/Import erhaelt Nutzung, Pin-Reihenfolge, LastUsedAt und ThumbnailData.

## Nicht geplant

- Cloud-Sync direkt auf der Live-SQLite-Datenbank ueber OneDrive/iCloud.

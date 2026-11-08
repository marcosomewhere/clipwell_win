# Clipwell Windows - Roadmap und offene Punkte

Stand: 2026-11-08

Diese Datei ist die zentrale Ablage fuer offene Produkt-, Technik- und Release-Punkte. Kurzlebige Review-, Bug- und Feature-MDs werden hier konsolidiert und nicht separat weitergefuehrt.

## Naechste sinnvolle Arbeit

| Prioritaet | Thema | Stand |
| --- | --- | --- |
| Hoch | Installer-/Release-Format festlegen: MSIX, NSIS oder portable ZIP | Offen |
| Hoch | Release-Pipeline mit versionierten Artefakten und Release Notes | Offen; portabler Single-File-Build per `dotnet publish -r win-x64` dokumentiert in `CLAUDE.md` |
| Mittel | Weitere Hintergrundaufgaben ueber `App.Background.cs`: Granite-Vorschlaege je Eintrag fuer das Einordnungsfenster vorab, Auswertung der Einordnungsverfahren, Zusammenfassungsentwuerfe je Brain-Thema; im Leerlauf mehr Threads fuer Granite (Neustart noetig) | Offen; "Kategorien analysieren" laeuft bereits im Hintergrund |
| Hoch | Verschluesselung in der Oberflaeche pruefen: Notschluessel-Dialog beim Erststart, Notschluessel anzeigen/kopieren, Start auf einem anderen Windows-Konto mit Notschluessel, Import eines Auto-Backups von einem anderen PC | Offen; nur per Tests und Migration am echten Bestand geprueft |
| Mittel | Outlook-Add-in neu einrichten und in Outlook testen (schreibt erst danach DPAPI-geschuetzte Uebergabedateien) | Offen; die installierte Kopie schreibt noch Klartext-JSON, Merkautomat liest beides |
| Niedrig | Notschluessel erneuern (neuer Notschluessel, alle Dateikoepfe neu schreiben, alte Auto-Backups brauchen weiter den alten) | Offen; heute nur anzeigen |
| Mittel | Code-Signing Stufe 1: selbst signieren fuer den Eigengebrauch (siehe "Code-Signing" unten) | Offen; vor dem Eintragen des Zertifikats auf dem Firmen-PC mit der IT abstimmen |
| Niedrig | Code-Signing Stufe 2: Signatur einer Zertifizierungsstelle fuer die Weitergabe (siehe "Code-Signing" unten) | Offen; erst noetig, wenn Merkautomat PCs ausserhalb der eigenen Kontrolle erreicht |
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
| Niedrig | Suche: Zentrierung wie in Rocchio fuer "Aehnliche"; Schwellen 0,80/0,83 muessten neu bestimmt werden | Offen; nur mit Vorher-Nachher-Messung. Brain-Kanten: Rangfolge zentriert mit gegenseitigen Nachbarn umgesetzt, Schwelle bleibt auf dem Roh-Kosinus |
| Niedrig | Suche: "Passt nicht" als Gegenbeispiele in "Aehnliche zu Markierten" (Rocchio gamma) | Offen; braucht eine zweite Markierungsart in der Oberflaeche |
| Zurueckgestellt | Weitere Encoder (Qwen3-Embedding-0.6B, BGE-M3, Granite Embedding), SetFit, Mehrfach-Kategorien | Neue Modelle/Abhaengigkeiten noetig; erst wenn die Auswertung zeigt, dass E5 die Grenze ist |

## Feature-Luecken

Abgeleitet aus Code und Handbuch (Stand 2026-11-05, am 2026-11-08 gegen den Code geprueft), nicht aus Nutzertests. Prioritaet nach Nutzen fuer einen Clipboard-Manager im Alltag.

### Datenschutz und Sicherheit

| Prioritaet | Feature | Ist-Zustand | Hinweis |
| --- | --- | --- | --- |
| Hoch | App-Ausschlussliste (Prozessname des Clipboard-Besitzers via `GetClipboardOwner`/`GetWindowThreadProcessId`) | Nicht vorhanden; nur globale Pause | Einstellung "Diese Apps nie aufzeichnen" |
| Mittel | Sensible Inhalte erkennen (Kreditkarte mit Luhn-Pruefung, IBAN, API-Keys/Tokens, JWT) | Nicht vorhanden | Optionen: nicht speichern, maskiert anzeigen oder nach N Minuten loeschen |
| Niedrig | Inkognito-Timer ("Pause fuer 15 Minuten") | Pause nur manuell an/aus | Tray-Untermenue |

### Code-Signing

Ziel: Windows (SmartScreen, Defender, AppLocker/EDR) und Outlook (Richtlinie "nur signierte Add-ins") sollen `Merkautomat.exe` und `MerkautomatOutlook.dll` als Programm eines bekannten Herausgebers sehen. Eine Signatur belegt immer, dass die Datei unveraendert ist; vertraut wird ihr aber nur, wenn das Zertifikat auf eine Stelle zurueckgeht, die der jeweilige PC kennt.

Gemeinsamer Projektaufwand fuer beide Stufen (etwa ein halber Tag): Signierschritt nach Build/Publish fuer `Merkautomat.exe` (Single-File nach dem Publish) und `MerkautomatOutlook.dll`, SHA-256 mit Zeitstempel (Signatur bleibt nach Ablauf des Zertifikats gueltig), Pruefung per `signtool verify`. Mitgelieferte Fremd-DLLs nicht selbst signieren (ONNX Runtime ist von Microsoft signiert; `sqlite3mc.dll` und die Whisper-Laufzeit pruefen).

**Stufe 1: selbst signieren fuer den Eigengebrauch**

- Kosten 0 €, etwa 1 Stunde.
- Zertifikat per `New-SelfSignedCertificate -Type CodeSigningCert`, nur fuer Code-Signierung (EKU), privater Schluessel nicht exportierbar bzw. mit Passwort, nur im Benutzerspeicher.
- Auf dem eigenen PC als vertrauenswuerdiger Stamm und Herausgeber eintragen. Dann gilt die Signatur dort, Outlook akzeptiert das Add-in auch bei Signaturpflicht, Veraenderungen an EXE/DLL fallen auf.
- Grenzen: SmartScreen wird dadurch nicht ruhiger. Das Eintragen eines eigenen Stammzertifikats ist eine typische Angreifertechnik und kann auf dem Firmen-PC EDR-Alarme ausloesen, deshalb vorher mit der IT abstimmen. Wer den privaten Schluessel stiehlt, kann Code signieren, dem dieser PC vertraut.
- Variante in der Firma: Die IT signiert mit dem internen Firmenzertifikat oder verteilt das eigene Zertifikat per Gruppenrichtlinie an die Firmen-PCs. Kostet nichts und ist der sauberste Weg gegen Sicherheitsalarme im Firmennetz.

**Wie weit selbst signiert reicht**

Selbst signiert genuegt, solange jeder PC, auf dem Merkautomat laeuft, unter eigener Kontrolle oder der Kontrolle der Firmen-IT steht und das Zertifikat dort eingetragen werden kann: eigener PC, einzelne Kollegen-PCs (manuell eintragen) oder Firmen-PCs (Verteilung per Gruppenrichtlinie). Nicht mehr genuegt es, sobald Merkautomat zum Download angeboten oder an PCs weitergegeben wird, auf denen niemand das Zertifikat eintraegt: Dort gilt die Signatur als nicht vertrauenswuerdig, und SmartScreen warnt wie bei einer unsignierten Datei.

**Stufe 2: Signatur einer Zertifizierungsstelle fuer die Weitergabe**

- Microsoft Artifact Signing (frueher Trusted Signing): etwa 10 $ im Monat, Einrichtung etwa 1 Tag nach der Identitaetspruefung. Verfuegbarkeit fuer Einzelpersonen bzw. Firmen in Deutschland vor der Entscheidung pruefen (zuletzt bekannt: Einzelpersonen nur USA/Kanada, Firmen mit nachweisbarer Geschichte).
- Klassisches OV-Zertifikat (z. B. Certum, SSL.com) mit Schluessel in der Cloud oder auf einem USB-Token: etwa 100–400 € im Jahr, Pruefung 1–3 Wochen. Funktioniert auch als Privatperson in Deutschland; fuer quelloffene Projekte gibt es guenstigere Angebote.
- EV-Zertifikat (etwa 300–700 € im Jahr): seit 2024 kein Sofort-Vertrauen bei SmartScreen mehr, daher nicht lohnend.
- SmartScreen-Warnungen verschwinden auch mit gekauftem Zertifikat erst, wenn die signierte App ueber einige Wochen genug Downloads gesammelt hat.
- Preise und Bedingungen vor dem Kauf aktuell pruefen.

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
| Niedrig | Weitere globale Hotkeys (Pinboard, Eyedropper, Paste-Stapel) | Ein globaler Popup-Hotkey plus Hotkeys fuer einzelne Eintraege; Pinboard nur ueber Tray oder Popup | Der Recorder fuer den Popup-Hotkey nimmt nur Buchstaben und Ziffern auf (der Dialog fuer Eintrags-Hotkeys kann F-Tasten) |

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
| Mittel | Update-Pruefung gegen GitHub-Releases | Nicht vorhanden; "Ueber" enthaelt keinen GitHub-Link mehr | Erst sinnvoll mit Release-Pipeline; Downloads nur mit Signatur- und Hash-Pruefung |
| Mittel | Barrierefreiheit: `AutomationProperties.Name` fuer Icon-Buttons, Kontrastmodus | Vereinzelt gesetzt, nicht systematisch fuer alle Icon-Buttons; kein Kontrastmodus | Screenreader liest Icon-Buttons sonst ohne Namen |
| Niedrig | Mehrsprachige UI (mindestens Englisch) | UI nur Deutsch, README Englisch | Ressourcen-Dateien statt fester Strings |
| Niedrig | Echter Gaussian Blur im Bildeditor | Graue Ueberlagerung | Siehe Bekannte Grenzen in `STATUS.md` |
| Mittel | Desktop-Aufnahme: Ausschlussliste fuer Apps/Fenstertitel und einstellbare Ruhezeit | Feste Werte (500 ms Takt, 500 ms Ruhe, 2,5 s Nachlesen), nur Clipwell und Passwortfelder ausgeschlossen | Settings-Seite "Aufnahme" |
| Niedrig | Desktop-Aufnahme: optionaler OCR-Snapshot fuer textlose Inhalte | Nur UI Automation | Pixelaufnahme kann Corporate-Screen-Capture-Alarme ausloesen; nur als expliziter Einzelklick |
| Mittel | Diktat: Live-Text waehrend des Sprechens und globaler Diktat-Hotkey | Transkription erst nach dem Stoppen; Start nur per Button (Schnellnotiz im Popup, "Neue Sprachnotiz", Diktat in Eingabefeldern), kein Tastenkuerzel, kein Tray-Eintrag | Whisper in Abschnitten (z. B. 5 s mit Ueberlappung) verarbeiten |
| Niedrig | Notizen: Uebernahme alter `NOTE`-History-Eintraege in das Sprechwerk | Alte Schnellnotizen bleiben in der History; Markdown-Vorschau und Kategorien gibt es fuer Notizen im Ideenkombinat bereits | Migration nur auf Wunsch |

Bekannte Grenzen stehen zentral in `STATUS.md`.

## Erledigt und konsolidiert

- Verschluesselung im Ruhezustand: History und Suchindex per SQLite3 Multiple Ciphers, Notizen, Aufnahmen, Aufgaben, Einstellungen und Auto-Backups per AES-256-GCM, Schluessel per DPAPI plus Notschluessel (Details in `STATUS.md`, Regeln in AGENTS.md/CLAUDE.md).
- Sicherheitsreview 2026-11-08: Links nur ueber `WebLink` (http(s), escapt), URL-Vorschau mit erweitertem Adressschutz, gepruefter Weiterleitung und ohne Proxy, `secure_delete` fuer History und FTS, API-Schluessel des llama-server per Umgebungsvariable, Log-Rotation (Details in `STATUS.md`).
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

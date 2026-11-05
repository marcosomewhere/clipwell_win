# Clipwell für Windows — Benutzerhandbuch

## Inhalt

1. [Überblick](#1-überblick)
2. [Systemvoraussetzungen und Installation](#2-systemvoraussetzungen-und-installation)
3. [Erststart und Onboarding](#3-erststart-und-onboarding)
4. [Tray-Icon und Grundbedienung](#4-tray-icon-und-grundbedienung)
5. [Das Popup-Fenster](#5-das-popup-fenster)
6. [Suchen und Filtern](#6-suchen-und-filtern)
7. [Einträge einfügen und kopieren](#7-einträge-einfügen-und-kopieren)
8. [Einträge pinnen, löschen, organisieren](#8-einträge-pinnen-löschen-organisieren)
9. [Bulk-Aktionen](#9-bulk-aktionen)
10. [Notizen und Diktat](#10-notizen-und-diktat)
11. [Pinboard](#11-pinboard)
12. [Eyedropper — Farben vom Bildschirm](#12-eyedropper--farben-vom-bildschirm)
13. [Desktop-Aufnahme und KB](#13-desktop-aufnahme-und-kb)
14. [Detailfenster](#14-detailfenster)
15. [Einstellungen](#15-einstellungen)
16. [Export, Import und Backup](#16-export-import-und-backup)
17. [Datenschutz und Privatsphäre](#17-datenschutz-und-privatsphäre)
18. [Datenpfade](#18-datenpfade)
19. [Fehlerbehebung](#19-fehlerbehebung)

---

## 1. Überblick

Clipwell ist ein lokaler Windows-Clipboard-Manager, der im Hintergrund als Tray-App läuft. Er zeichnet automatisch auf, was du in die Zwischenablage kopierst, und öffnet auf Tastendruck ein tastaturgesteuertes Popup, über das du jeden früheren Eintrag gezielt einfügen, suchen oder verwalten kannst.

**Was Clipwell aufzeichnet:**

| Typ | Beschreibung |
|-----|-------------|
| Text | Beliebiger Unicode-Text |
| Code | Erkannter Quellcode (15+ Sprachen) oder Konfigurationsdateien |
| URL | Webadressen mit optionalem Titel und Favicon |
| Farbe | Hex-Farbcodes, die automatisch erkannt werden |
| Bild | Kopierte Bilder aus Browser, Office, Screenshot-Tool etc. |

Alle Daten verbleiben lokal auf deinem Gerät in einer SQLite-Datenbank.

---

## 2. Systemvoraussetzungen und Installation

**Voraussetzungen:**
- Windows 10 Version 2004 (Build 19041) oder neuer
- .NET 10 Windows Desktop Runtime

**Starten:**
```
Clipwell.exe
```

Beim ersten Start legt Clipwell automatisch seinen Datenpfad unter `%APPDATA%\Clipwell\` an, registriert sich optional im Autostart und öffnet das Onboarding.

---

## 3. Erststart und Onboarding

Beim allerersten Start öffnet sich das Onboarding-Fenster. Es führt durch:

1. **Hotkey-Test** — Du kannst die konfigurierte Tastenkombination ausprobieren, bevor du fortfährst.
2. **Abschluss** — Das Fenster schließt sich und das Tray-Icon erscheint in der Taskleiste.

Das Onboarding erscheint nur einmal. Es kann nicht manuell erneut geöffnet werden.

---

## 4. Tray-Icon und Grundbedienung

Clipwell lebt als Icon in der Windows-Taskleiste (Systembereich). Ein Linksklick auf das Icon öffnet oder schließt das Popup. Ein Rechtsklick öffnet das Kontextmenü:

| Menüeintrag | Funktion |
|-------------|----------|
| Öffnen | Popup öffnen |
| Pinboard | Pinboard-Fenster ein-/ausblenden |
| Einstellungen | Einstellungsfenster öffnen |
| Beenden | Clipwell vollständig beenden |

**Überwachung pausieren:** Über das Tray-Menü kann die Clipboard-Überwachung temporär angehalten werden. Solange die Überwachung pausiert ist, werden keine neuen Einträge aufgezeichnet.

---

## 5. Das Popup-Fenster

### Öffnen

Der Standard-Hotkey ist **Win+Shift+V**. Dieser kann in den Einstellungen geändert werden.

Das Popup öffnet sich am Mauszeiger. Per Linksklick auf das Tray-Icon öffnet es sich zentriert über der Taskleiste.

### Aufbau

```
┌──────────────────────────────────────┐
│  Suchfeld                            │
│──────────────────────────────────────│
│  [Alle] [Text] [Code] [URL] [Bild] [Farbe] [Gepinnt]  │
│──────────────────────────────────────│
│  ▼ Gepinnt                           │
│    Eintrag A (gepinnt)               │
│  ▼ Heute                             │
│    Eintrag B                         │
│    Eintrag C                         │
│  ▼ Gestern                           │
│    ...                               │
└──────────────────────────────────────┘
```

**Gruppen:** Einträge werden automatisch gruppiert in: Gepinnt, Heute, Gestern, Diese Woche, Früher.

### Größe und Position

Das Popup lässt sich per Drag am oberen Rand verschieben. Die rechte, linke und untere Kante können zum Vergrößern gezogen werden. Größe und Position werden beim Schließen gespeichert.

### Popup schließen

- **Esc** oder Klick außerhalb des Fensters
- Beim Schließen werden Suche, Filter und Bulk-Modus automatisch zurückgesetzt

---

## 6. Suchen und Filtern

### Filter-Chips

Die Chips unterhalb des Suchfelds filtern nach Eintragstyp:

| Chip | Zeigt |
|------|-------|
| Alle | Gesamte History |
| Text | Nur Texteinträge |
| Code | Code und Konfigurationsdateien (z. B. JSON, YAML; YAML wird beim Kopieren auf Gültigkeit geprüft) |
| URL | Webadressen |
| Bild | Kopierte Bilder |
| Farbe | Erkannte Hex-Farbcodes |
| Gepinnt | Nur angepinnte Einträge |

### Volltextsuche

Tippen im Suchfeld durchsucht Inhalt, OCR-Text (bei Bildern), URL-Titel und erkannte Sprache gleichzeitig. Groß-/Kleinschreibung spielt keine Rolle. Bei sehr großer History (mehr als 1000 Einträge) nutzt Clipwell einen Suchindex, damit die Suche schnell bleibt.

### Regex-Suche

Der Button `.*` rechts im Suchfeld schaltet auf reguläre Ausdrücke um, z. B. `^https?://` oder `\d{4}-\d{2}`. Ungültige Ausdrücke liefern keine Treffer.

### Suchhistorie

Clipwell merkt sich die letzten 5 Suchen. Sie erscheinen als Liste, wenn das Suchfeld leer ist und den Fokus hat.

### Sortierung

Über den Sortier-Button in der Chip-Leiste lässt sich die Reihenfolge wählen: Neueste zuerst, Älteste zuerst, Häufig verwendet oder Selten verwendet. Gepinnte Einträge bleiben immer oben.

### Suchpräfixe

Für präzisere Suchen stehen folgende Präfixe zur Verfügung:

| Präfix | Beispiel | Funktion |
|--------|----------|----------|
| `type:` | `type:code` | Suche nach Eintragstyp |
| `kind:` | `kind:code` | Suche nach Content-Kind (Badge) |
| `domain:` | `domain:github.com` | URL-Einträge nach Domain filtern |
| `pinned:` | `pinned:true` | Nur angepinnte / nicht angepinnte |

Beim Eingeben erscheinen Vorschläge für Präfixe und vorhandene Werte.

**Tastaturnavigation im Popup:**

| Taste | Funktion |
|-------|----------|
| Pfeil hoch/runter | Auswahl bewegen |
| Enter | Ausgewählten Eintrag einfügen und Ziel in Vordergrund bringen |
| Ctrl+Enter | Als Plaintext einfügen (ohne Formatierung) |
| Tab | Fokus zu den Filter-Chips wechseln |
| Esc | Popup schließen |
| Delete | Ausgewählten Eintrag löschen |

---

## 7. Einträge einfügen und kopieren

### Einfügen (Paste)

**Enter** oder **Doppelklick** fügt den ausgewählten Eintrag ein:
1. Clipwell schreibt den Inhalt in die Zwischenablage.
2. Das Popup schließt sich.
3. Das vorher aktive Fenster wird in den Vordergrund gebracht.
4. Clipwell sendet automatisch Ctrl+V.

**Ctrl+Enter** fügt als reinen Text ein. Mit **Enter** bleibt bei Inhalten aus dem Browser die Formatierung erhalten (Tabellen, Listen, Überschriften, Links), sofern das Zielprogramm HTML annimmt, etwa Word, Outlook oder Confluence. Rich-Text aus Word (RTF) wird weiterhin als reiner Text gespeichert.

**Drag & Drop:** Ein Eintrag kann aus der Liste direkt in ein anderes Fenster gezogen werden.

### Nur in die Zwischenablage kopieren

Über den Hover-Button (erscheint beim Überfahren eines Eintrags mit der Maus) oder das Rechtsklick-Kontextmenü kann ein Eintrag ohne Einfügen in die Zwischenablage kopiert werden. Das Popup bleibt dabei offen.

### URL-Vorschau nachladen

Im Kontextmenü (Rechtsklick) steht bei URL-Einträgen "URL-Vorschau neu laden". Dies ruft Titel und Favicon erneut ab.

### URL im Browser öffnen

Bei URL-Einträgen kann der Link über Hover-Aktion, Kontextmenü oder Detailfenster direkt im Standardbrowser geöffnet werden.

---

## 8. Einträge pinnen, löschen, organisieren

### Pinnen

Angepinnte Einträge erscheinen immer in der Gruppe "Gepinnt" ganz oben und werden beim Bereinigen (Purge nach Anzahl, Alter oder Größe) nicht gelöscht.

- Hover-Button (Pin-Symbol) beim Überfahren
- Rechtsklick → Pinnen/Lösen
- **Tastenkürzel:** Standard Ctrl+P (konfigurierbar in Einstellungen)

### Löschen

- Hover-Button (Papierkorb) beim Überfahren
- Rechtsklick → Löschen
- **Delete**-Taste bei ausgewähltem Eintrag

### Sicheres Löschen

Rechtsklick → "Sicher löschen" entfernt den Eintrag physisch aus der Datenbank via SQLite VACUUM. Dies ist nicht rückgängig zu machen und löscht alle Spuren des Inhalts.

### Typ manuell überschreiben

Rechtsklick → "Als Text behandeln" oder "Als Code behandeln" überschreibt die automatische Erkennung des Eintragstyps.

---

## 9. Bulk-Aktionen

Der Mehrfachauswahlmodus ermöglicht das gleichzeitige Bearbeiten mehrerer Einträge.

**Aktivieren:** Klick auf das Mehrfachauswahl-Symbol in der Toolbar oben.

Im Bulk-Modus erscheinen Checkboxen links neben jedem Eintrag. Folgende Aktionen stehen zur Verfügung:

| Aktion | Funktion |
|--------|----------|
| Alle auswählen | Alle sichtbaren (gefilterten) Einträge auswählen/abwählen |
| Pinnen | Alle ausgewählten Einträge pinnen |
| Lösen | Alle ausgewählten Einträge lösen |
| Kopieren | Texte der ausgewählten Einträge verbunden (getrennt durch `---`) in die Zwischenablage kopieren |
| Exportieren | Ausgewählte Einträge als JSON exportieren |
| Löschen | Ausgewählte Einträge löschen (mit Bestätigungsdialog) |
| Abbrechen | Bulk-Modus beenden |

Ohne Auswahl beim Export: Clipwell fragt, ob die gesamte History exportiert werden soll.

---

## 10. Notizen und Diktat

Notizen sind eine eigene Funktion neben der Zwischenablage-History. Jede Notiz ist eine Markdown-Datei in `%APPDATA%\Clipwell\notes\`.

### Schnellnotiz im Popup

Über das Notizblock-Symbol in der Toolbar öffnet sich ein kleines Eingabefeld am unteren Rand des Popups.

- **Enter** speichert die Notiz in die Notizen.
- **Shift+Enter** fügt einen Zeilenumbruch ein.
- **Esc** schließt das Eingabefeld ohne zu speichern.
- **Mikrofon-Symbol:** übergibt den bisherigen Text ans Notizfenster und startet dort sofort das Diktat.
- **Fenster-Symbol:** öffnet das Notizfenster (mit dem bisherigen Text als neue Notiz, falls etwas eingegeben ist).
- **Tastenkürzel:** Standard Ctrl+N (konfigurierbar)

Ältere Schnellnotizen, die noch als gepinnte Einträge mit dem Badge "NOTE" in der History stehen, bleiben dort unverändert.

### Notizfenster

**Öffnen:** Tray-Menü → "Notizen" → "Notizen öffnen", "Neue Notiz" oder "Neue Sprachnotiz (diktieren)".

Links stehen alle Notizen (neueste zuerst) mit Suchfeld, rechts der Editor. Änderungen werden nach kurzer Pause automatisch gespeichert; eine leer gewordene Notiz wird beim Wechseln entfernt. Der Titel einer Notiz ist ihre erste Zeile.

| Button | Kürzel | Funktion |
| --- | --- | --- |
| Neu | Strg+N | Neue, leere Notiz |
| Diktieren | Strg+D | Aufnahme starten; erneut klicken beendet sie und fügt den erkannten Text an der Cursorposition ein |
| In KB exportieren | Strg+E | Übernimmt die Notiz in die KB |
| Kopieren | | Notiztext in die Zwischenablage (ohne History-Eintrag) |
| Ordner öffnen, Löschen | | Notizordner im Explorer, Notiz löschen |

### Diktat

Das Diktat läuft komplett lokal mit Whisper; Audio verlässt den Rechner nicht und wird nicht gespeichert. Das Sprachmodell "Base" ist bereits enthalten (Ordner `models` neben `Clipwell.exe`), Diktieren funktioniert also sofort und offline. Die größeren Modelle "Small" und "Medium" lädt Clipwell erst nach Rückfrage einmalig nach `%APPDATA%\Clipwell\models\`. Modellgröße und Sprache (Deutsch, Englisch, Automatisch) stehen unter Einstellungen → Allgemein → "Diktat (Whisper, lokal)"; größere Modelle erkennen genauer, brauchen aber länger.

Während der Aufnahme ist der Button rot, unten rechts laufen Pegel und Dauer mit. Nach dem Beenden steht "Transkribiere ..." in der Statusleiste, bis der Text eingefügt ist. Man kann jederzeit zwischendurch diktieren und dazwischen tippen. Es läuft immer nur ein Diktat gleichzeitig.

Voraussetzung: ein Mikrofon, und unter Windows-Einstellungen → Datenschutz → Mikrofon muss der Zugriff für Desktop-Apps erlaubt sein.

### Export in die KB

"In KB exportieren" legt eine Kopie der Notiz in `%APPDATA%\Clipwell\kb\notes\` ab. Die Kopie bleibt so, wie sie beim Export war: Wird die Notiz später geändert, zeigt die Liste "KB veraltet" und der Button heißt "KB aktualisieren". Wird die Notiz gelöscht, bleibt die KB-Fassung erhalten.

---

## 11. Pinboard

Das Pinboard ist ein separates, immer-im-Vordergrund-Fenster, das alle angepinnten Einträge anzeigt.

**Öffnen:** Tray-Menü → Pinboard oder Tastenkürzel Ctrl+B (konfigurierbar) im Popup.

Die Reihenfolge lässt sich per Drag & Drop ändern: Der gezogene Eintrag wird farbig umrandet, eine Linie oben oder unten am Zieleintrag zeigt, wo er landet. Ein Ablegen im leeren Bereich unter der Liste verschiebt ihn ans Ende. Dieselbe Reihenfolge gilt für die Gruppe "Gepinnt" im Popup; neu gepinnte Einträge stehen dort oben.

Im Pinboard können Einträge ebenfalls per Klick oder Tastatur eingefügt werden. Das Pinboard bleibt offen, während das Popup geöffnet und geschlossen wird.

---

## 12. Eyedropper — Farben vom Bildschirm

Der Eyedropper ermöglicht es, eine Farbe direkt vom Bildschirm zu entnehmen.

**Öffnen:** Pipetten-Symbol in der Toolbar des Popups.

Das Popup schließt sich, und ein transparentes Vollbild-Overlay erscheint. Mit einem Klick auf einen beliebigen Bildschirmpunkt wird die Farbe an dieser Stelle als Hex-Wert aufgezeichnet, in die Zwischenablage kopiert und als Farbeintrag in die History gespeichert.

---

## 13. Desktop-Aufnahme und KB

### Aufnahme

Die Desktop-Aufnahme protokolliert, was im jeweils aktiven Fenster zu sehen ist, und speichert es fortlaufend als Markdown.

**Starten und beenden:** Record-Symbol (Kreis) im Popup-Kopf, Button "Aufnahme starten" im KB-Fenster oder Tray-Menü → "Desktop aufzeichnen". Während der Aufnahme ist das Symbol rot (Stopp-Quadrat), das Tray-Icon zeigt "Aufnahme läuft". Derselbe Button beendet die Aufnahme.

**Was erfasst wird:** Clipwell liest den sichtbaren Text des Vordergrundfensters über die Windows-Bedienungshilfen-Schnittstelle (UI Automation): Fenstertitel, Anwendung, sichtbarer Dokumenttext, Beschriftungen, Links, Listen, Eingabewerte und Alternativtexte von Bildern. Es werden keine Screenshots aufgenommen.

**Wann ein Eintrag entsteht:** Clipwell prüft das aktive Fenster alle 0,5 Sekunden. Ein Fenster- oder Titelwechsel (z. B. neuer Browser-Tab) wird erfasst, sobald er 0,5 Sekunden stabil ist. Kurzes Durchklicken erzeugt dadurch keine Einträge. Bleibst du im selben Fenster, liest Clipwell alle 2,5 Sekunden neu und schreibt nur Zeilen, die für dieses Fenster noch nicht protokolliert wurden.

**Aufbau eines Eintrags:**

```markdown
## 14:32:08 - Google Chrome: Kundenportal

### Sichtbarer Inhalt

- Kunde: Muster GmbH
- Vertragsstatus: Aktiv

### Änderung

Fensterwechsel von "Explorer: Downloads".
```

Bleibt man im selben Fenster und ändert sich nur ein Teil (z. B. Scrollen), steht unter "Neu sichtbar" nur das Hinzugekommene, unter "Änderung" die Anzahl neuer Zeilen. Fenster-, Scroll- und Menüleisten werden ausgelassen.

**Was nicht erfasst wird:**

- Clipwell-eigene Fenster (Popup, KB-Fenster usw.).
- Passwortfelder.
- Alles, solange "Überwachung pausieren" aktiv ist.
- Hängende Fenster ("Keine Rückmeldung").
- Inhalte ohne Textinformation: Bilder, Videos, Diagramme in Canvas-Grafik, Spiele, manche Java-Anwendungen und Editoren ohne Bedienungshilfen-Text (z. B. der Textbereich von Notepad++).

Jede Aufnahme landet als eigene Datei in `%APPDATA%\Clipwell\recordings\` (Name `JJJJ-MM-TT_hh-mm-ss.md`) und endet mit Ende-Zeit, Dauer und Anzahl der Einträge.

### KB

**Öffnen:** `KB`-Button im Popup-Kopf oder Tray-Menü → "KB".

Links steht oben das **Dashboard**, darunter die **Gesamtübersicht**, dann die exportierten Notizen ("Notiz: ...") und alle Aufnahmen (neueste zuerst, mit Anzahl der Einträge und Dauer). Rechts erscheint das Markdown des gewählten Eintrags.

Das Dashboard zeigt die KB als Instrumententafel: Sieben-Segment-Zähler für Artikel in der KB (exportierte Notizen plus Aufnahmen), Notizen, Aufnahmen und Zwischenablage-Einträge; vier Analog-Instrumente für die laufende bzw. letzte Aufnahme (erfasste Bildwechsel, Fenster, Anwendungen, Minuten) samt Statusanzeige "AUFNAHME LÄUFT" oder "INAKTIV"; Kacheln mit erfassten Inhalten je Anwendung und kopierten Inhalten je Webseite; darunter alle gespeicherten Artikel, ein Klick öffnet den Artikel. Kopieren und Exportieren liefern auf dem Dashboard das Markdown der Gesamtübersicht.

Die Gesamtübersicht fasst alles zusammen:

- **Zwischenablage:** Anzahl Einträge und gepinnte, Zeitraum, Verteilung nach Typ, Einträge der letzten 7 Tage, häufigste Domains, Inhaltsarten, meistgenutzte, gepinnte und zuletzt kopierte Einträge.
- **Notizen:** Anzahl und Liste der in die KB exportierten Notizen.
- **Desktop-Aufnahmen:** Anzahl, erfasste Bildwechsel, Gesamtdauer, häufigste Anwendungen und je Aufnahme die besuchten Fenster.

Buttons: Aufnahme starten/beenden, Aktualisieren, Kopieren (landet nicht in der History), Exportieren als `.md`, Ordner öffnen (Clipwell-Datenordner) und Löschen des gewählten Eintrags: bei einer Aufnahme die Datei (nicht während sie läuft), bei einer Notiz nur die KB-Fassung. Eine laufende Aufnahme und neue Notiz-Exporte aktualisieren das KB-Fenster automatisch.

---

## 14. Detailfenster

Das Detailfenster öffnet sich per Doppelklick auf einen Eintrag, über den Hover-Button oder per Tastenkürzel (Standard F2).

### Text- und Code-Editor

Für Text- und Code-Einträge zeigt das Detailfenster einen vollständigen Editor:

- **Bearbeiten:** Text kann direkt bearbeitet werden.
- **Speichern:** Ctrl+S oder der Speichern-Button übernimmt Änderungen dauerhaft in die Datenbank.
- **Zeilenumbruch:** Umschalter für weichen Zeilenumbruch.
- **Zeilennummern:** Bei Code-Einträgen.
- **Zeichenzähler:** Zeigt Zeichen- und Zeilenanzahl.
- **Undo/Redo:** Standard Ctrl+Z / Ctrl+Y.
- **Sprache:** Dropdown zur manuellen Auswahl der Code-Sprache (C#, JavaScript, Python, SQL usw.) mit Syntax-Highlight-Overlay.
- **OCR-Ansicht:** Bei Bildern mit erkanntem Text kann zwischen Bild und OCR-Text umgeschaltet werden.
- **Quelle:** Wurde der Inhalt aus dem Browser kopiert, steht die Adresse der Webseite als Link neben dem Titel. Der Button "Browser" öffnet sie.
- **Webinhalt:** Bei Inhalten aus dem Browser schaltet der Umschalter "Webinhalt" auf eine aufbereitete Fassung als Markdown um: Überschriften, Listen, Tabellen und Links bleiben erhalten, Navigation, Werbung, Skripte und Formulare werden entfernt. "Vorschau" zeigt sie gerendert, "Kopieren" übernimmt das Markdown. Die Ansicht ist schreibgeschützt; der ursprüngliche Text bleibt unverändert.

### Bild-Editor

Für Bildeinträge bietet das Detailfenster einen vollständigen Bild-Editor:

**Ansicht:**
- Zoom mit Mausrad oder Zoom-Buttons
- Pan per Drag

**Annotations-Werkzeuge:**

| Werkzeug | Funktion |
|----------|----------|
| Auswahl | Annotation auswählen, verschieben, löschen |
| Pfeil | Pfeil zeichnen |
| Linie | Gerade Linie |
| Rechteck | Rechteckige Umrahmung |
| Ellipse | Ellipse/Kreis |
| Stift | Freihandzeichnen |
| Text | Textanmerkung hinzufügen |
| Nummer | Nummeriertes Marker-Symbol (1, 2, 3, ...) |
| Hervorheben | Halbtransparente Hervorhebungsfläche |
| Schwärzen | Bereich schwarz überdecken (Redaktion) |
| Pixelieren | Bereich verpixeln |
| Weichzeichnen | Bereich grau überlagern |
| Zuschneiden | Bild auf markierten Bereich zuschneiden |

**Bild-Operationen:**
- Drehen um 90° nach links oder rechts
- Spiegeln (horizontal, vertikal)
- Größe ändern (Pixelgenaue Eingabe)
- Canvas erweitern (Rand hinzufügen)
- Als JPEG oder PNG speichern (exportieren)
- Undo/Redo für alle Änderungen

### Farb-Details

Für Farbeinträge zeigt das Detailfenster die Farbe als Vorschau und listet alle gängigen Farbformate:

- **HEX:** `#4A90E2`
- **RGB:** `rgb(74, 144, 226)`
- **HSL:** `hsl(211, 71%, 59%)`
- **CSS-Name:** falls vorhanden
- **Tailwind-Klasse:** falls vorhanden

Jeder Wert kann einzeln in die Zwischenablage kopiert werden.

---

## 15. Einstellungen

Öffnen über Tray-Menü → Einstellungen.

### Hotkey

| Option | Beschreibung |
|--------|-------------|
| Hotkey aufnehmen | Gewünschte Tastenkombination direkt eintippen |
| Manuelle Eingabe | Modifier-Checkboxen + Buchstabe |
| Hotkey-Aktion | "Popup öffnen" oder "Letzten Eintrag einfügen" |

Der Hotkey wird sofort registriert. Bei Konflikten zeigt Clipwell den Windows-Fehlercode und versucht automatisch Fallback-Kombinationen (Win+Shift, Ctrl+Shift, Ctrl+Alt, Win+Alt).

### Allgemein

| Option | Beschreibung |
|--------|-------------|
| Design | System / Dunkel / Hell |
| Autostart | Clipwell beim Windows-Login starten |
| Maximale Einträge | Älteste unpinnierte Einträge werden überschrieben (50–5000, Standard 500) |
| Maximales Alter | Einträge älter als N Tage automatisch löschen (0 = deaktiviert) |
| Max. DB-Größe | Datenbank auf N MB beschränken (0 = deaktiviert) |
| Code-Erkennung | Konservativ / Normal / Aggressiv |

### Datenschutz

| Option | Beschreibung |
|--------|-------------|
| Überwachung pausieren | Kein Aufzeichnen neuer Einträge |
| URL-Vorschau | Automatischen Abruf von Titel und Favicon aktivieren/deaktivieren |

### Auto-Backup

| Option | Beschreibung |
|--------|-------------|
| Auto-Backup aktivieren | Tägliches JSON-Backup beim Start |
| Backup-Verzeichnis | Zielordner für automatische Backups |

Clipwell behält die letzten 7 automatischen Backups. Ältere werden gelöscht.

### Shortcuts (Popup-Tastenkürzel)

Vier Aktionen im Popup können mit eigenen Tastenkombinationen belegt werden:

| Aktion | Standard |
|--------|---------|
| Eintrag pinnen | Ctrl+P |
| Details öffnen | F2 |
| Schnellnotiz | Ctrl+N |
| Pinboard | Ctrl+B |

Jeder Shortcut kann mit beliebiger Kombination aus Ctrl, Alt, Shift, Win und einem Buchstaben (A–Z) oder F1–F12 belegt werden.

### Über

Zeigt Version und Build sowie einen Link zum Quellcode auf GitHub.

---

## 16. Export, Import und Backup

### JSON-Export

Popup → Bulk-Modus → Exportieren, oder Einstellungen → Daten.

Exportiert die History als lesbares JSON-Format inklusive Bilder, Thumbnails, Pin-Reihenfolge und Nutzungsmetadaten.

### JSON-Import

Einstellungen → Daten → Importieren. Doppelte Einträge (gleicher Timestamp und Inhalt) werden beim Import übersprungen.

### SQLite-Backup

Einstellungen → Daten → SQLite-Kopie exportieren. Erstellt eine vollständige Kopie der `history.db` — ideal als Backup oder für die Übertragung auf einen anderen PC.

### Sicheres Löschen

Rechtsklick auf einen Eintrag → "Sicher löschen" entfernt den Eintrag und führt VACUUM aus, um den Speicherplatz physisch freizugeben. Damit werden alle Spuren des Inhalts aus der Datenbankdatei entfernt.

### Gesamte History löschen

Einstellungen → Daten → "History löschen". Optional können gepinnte Einträge beibehalten werden.

---

## 17. Datenschutz und Privatsphäre

- **Lokal only:** Alle Daten bleiben auf deinem Gerät. Clipwell sendet keine Daten ins Internet, außer für URL-Vorschauen.
- **URL-Vorschauen:** Nur aktiv, wenn in den Einstellungen aktiviert. Clipwell ruft Titel und Favicon der kopierten URL ab. Private, Loopback- und Link-Local-Adressen (127.x, 10.x, 192.168.x, 172.16–31.x, 169.254.x, fc00::/7) werden nie abgerufen.
- **Überwachung pausieren:** Über Tray-Menü jederzeit möglich. Clipwell zeichnet dann nichts auf.
- **URL-Cache:** Abgerufene Titel und Favicons werden 7 Tage in der Datenbank gecacht. Der Cache kann in den Einstellungen geleert werden.
- **Desktop-Aufnahme:** Läuft nur nach ausdrücklichem Start und bleibt lokal. Sie speichert sichtbaren Text als unverschlüsseltes Markdown, auch vertrauliche Inhalte wie Chats oder Kundendaten. Vertrauliches nur bei pausierter Überwachung öffnen oder die Aufnahme danach im KB-Fenster löschen.
- **Diktat:** Spracherkennung läuft lokal (Whisper). Audio wird nur im Arbeitsspeicher gehalten und nach der Transkription verworfen. Das Base-Modell wird mitgeliefert; Netzwerkzugriff gibt es nur, wenn ein größeres Modell gewählt und dessen Download bestätigt wird.

---

## 18. Datenpfade

| Was | Pfad |
|-----|------|
| Einstellungen | `%APPDATA%\Clipwell\settings.json` |
| History-Datenbank | `%APPDATA%\Clipwell\history.db` |
| Fehlerprotokoll | `%APPDATA%\Clipwell\clipwell.log` |
| Onboarding-Marker | `%APPDATA%\Clipwell\onboarding.seen` |
| Bildeditor-Stilspeicher | `%APPDATA%\Clipwell\editor-styles.json` |
| Notizen | `%APPDATA%\Clipwell\notes\*.md` |
| In die KB exportierte Notizen | `%APPDATA%\Clipwell\kb\notes\*.md` |
| Whisper-Sprachmodell Base (mitgeliefert) | `models\ggml-base.bin` neben `Clipwell.exe` |
| Whisper-Sprachmodelle Small/Medium | `%APPDATA%\Clipwell\models\` |
| Desktop-Aufnahmen | `%APPDATA%\Clipwell\recordings\*.md` |
| WAL-Datei (SQLite) | `%APPDATA%\Clipwell\history.db-wal` |
| Wiederherstellungs-Backup | `%APPDATA%\Clipwell\history.db.broken-YYYYMMDD-HHMMSS` |
| Auto-Backups | Konfigurierbarer Ordner, `clipwell-autobackup-YYYYMMDD-HHmmss.json` |

---

## 19. Fehlerbehebung

### Popup öffnet sich nicht

1. Prüfe, ob Clipwell als Tray-Icon sichtbar ist.
2. Öffne die Einstellungen und prüfe, ob der Hotkey registriert wurde (grüner Status).
3. Falls der Hotkey blockiert ist: andere Anwendung verwendet dieselbe Kombination. Hotkey in den Einstellungen ändern.
4. Prüfe das Fehlerprotokoll unter `%APPDATA%\Clipwell\clipwell.log`.

### Clipboard wird nicht aufgezeichnet

1. Prüfe, ob "Überwachung pausieren" im Tray-Menü aktiv ist.
2. Starte Clipwell neu (Tray → Beenden, dann `Clipwell.exe` erneut starten).

### Datenbank-Fehler beim Start

Clipwell erkennt beschädigte Datenbanken über `quick_check` und bietet an, die Datei zu sichern und mit einer neuen, leeren History zu starten. Das kaputte File wird als `history.db.broken-...` archiviert.

### Kein Autostart

Prüfe, ob Clipwell Schreibzugriff auf den Registry-Schlüssel hat:
`HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`

### URL-Vorschau fehlt

- Prüfe Internetverbindung.
- Prüfe, ob URL-Vorschau in den Einstellungen aktiviert ist.
- Private/lokale Adressen werden grundsätzlich nicht abgerufen.
- Rechtsklick → "URL-Vorschau neu laden" erzwingt einen neuen Abruf.

### Bekannte Einschränkungen

- Die Desktop-Aufnahme sieht nur Text, den Anwendungen über UI Automation bereitstellen; Bilder und Videos werden nicht beschrieben. Fenster mit ständig wechselndem Text (Uhren, Logs) erzeugen viele kleine Einträge.
- Der "Weichzeichnen"-Effekt im Bild-Editor zeigt eine graue Überlagerung, keinen echten Gauß-Blur.
- Hotkey-Einschränkung: Nur Buchstaben A–Z und Ziffern 0–9 werden als Tastenanteil unterstützt. Funktionstasten können im Hotkey-Recorder nicht aufgenommen werden, sind aber als Popup-Shortcuts möglich.

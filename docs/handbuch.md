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
10. [Schnellnotiz](#10-schnellnotiz)
11. [Pinboard](#11-pinboard)
12. [Eyedropper — Farben vom Bildschirm](#12-eyedropper--farben-vom-bildschirm)
13. [Detailfenster](#13-detailfenster)
14. [Einstellungen](#14-einstellungen)
15. [Export, Import und Backup](#15-export-import-und-backup)
16. [Datenschutz und Privatsphäre](#16-datenschutz-und-privatsphäre)
17. [Datenpfade](#17-datenpfade)
18. [Fehlerbehebung](#18-fehlerbehebung)

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
- Windows 10 Version 1903 oder neuer
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
| Code | Code und Konfigurationsdateien |
| URL | Webadressen |
| Bild | Kopierte Bilder |
| Farbe | Erkannte Hex-Farbcodes |
| Gepinnt | Nur angepinnte Einträge |

### Volltextsuche

Tippen im Suchfeld durchsucht Inhalt, OCR-Text (bei Bildern), URL-Titel und erkannte Sprache gleichzeitig.

### Suchpräfixe

Für präzisere Suchen stehen folgende Präfixe zur Verfügung:

| Präfix | Beispiel | Funktion |
|--------|----------|----------|
| `type:` | `type:code` | Suche nach Eintragstyp |
| `kind:` | `kind:json` | Suche nach Content-Kind (Badge) |
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

**Ctrl+Enter** fügt als reinen Text ein — HTML-Formatierung und Rich-Text-Informationen werden entfernt.

**Drag & Drop:** Ein Eintrag kann aus der Liste direkt in ein anderes Fenster gezogen werden.

### Nur in die Zwischenablage kopieren

Über den Hover-Button (erscheint beim Überfahren eines Eintrags mit der Maus) oder das Rechtsklick-Kontextmenü kann ein Eintrag ohne Einfügen in die Zwischenablage kopiert werden. Das Popup bleibt dabei offen.

### URL-Vorschau nachladen

Im Kontextmenü (Rechtsklick) steht bei URL-Einträgen "URL-Vorschau neu laden". Dies ruft Titel und Favicon erneut ab.

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
| Exportieren | Ausgewählte Einträge als JSON exportieren |
| Löschen | Ausgewählte Einträge löschen (mit Bestätigungsdialog) |
| Abbrechen | Bulk-Modus beenden |

Ohne Auswahl beim Export: Clipwell fragt, ob die gesamte History exportiert werden soll.

---

## 10. Schnellnotiz

Über das Notizblock-Symbol in der Toolbar öffnet sich ein kleines Eingabefeld am oberen Rand des Popups. Damit kann schnell eine eigene Notiz erstellt werden, ohne die Zwischenablage zu verwenden.

- **Enter** speichert die Notiz.
- **Shift+Enter** fügt einen Zeilenumbruch ein.
- **Esc** schließt das Eingabefeld ohne zu speichern.
- **Tastenkürzel:** Standard Ctrl+N (konfigurierbar)

Schnellnotizen werden als angepinnte Texteinträge mit dem Badge "NOTE" gespeichert.

---

## 11. Pinboard

Das Pinboard ist ein separates, immer-im-Vordergrund-Fenster, das alle angepinnten Einträge anzeigt.

**Öffnen:** Tray-Menü → Pinboard oder Tastenkürzel Ctrl+B (konfigurierbar) im Popup.

Im Pinboard können Einträge ebenfalls per Klick oder Tastatur eingefügt werden. Das Pinboard bleibt offen, während das Popup geöffnet und geschlossen wird.

---

## 12. Eyedropper — Farben vom Bildschirm

Der Eyedropper ermöglicht es, eine Farbe direkt vom Bildschirm zu entnehmen.

**Öffnen:** Pipetten-Symbol in der Toolbar des Popups.

Das Popup schließt sich, und ein transparentes Vollbild-Overlay erscheint. Mit einem Klick auf einen beliebigen Bildschirmpunkt wird die Farbe an dieser Stelle als Hex-Wert aufgezeichnet, in die Zwischenablage kopiert und als Farbeintrag in die History gespeichert.

---

## 13. Detailfenster

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
- Drehen (90°, 180°, 270°)
- Spiegeln (horizontal, vertikal)
- Größe ändern (Pixelgenaue Eingabe)
- Canvas erweitern (Rand hinzufügen)
- Als PNG speichern (exportieren)
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

## 14. Einstellungen

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

Zeigt Version, Build-Datum und Lizenzen der verwendeten Bibliotheken.

---

## 15. Export, Import und Backup

### JSON-Export

Popup → Bulk-Modus → Exportieren, oder Einstellungen → Daten.

Exportiert die History als lesbares JSON-Format, das alle Felder enthält (inkl. Bilder als Base64).

### JSON-Import

Einstellungen → Daten → Importieren. Doppelte Einträge (gleicher Timestamp und Inhalt) werden beim Import übersprungen.

### SQLite-Backup

Einstellungen → Daten → SQLite-Kopie exportieren. Erstellt eine vollständige Kopie der `history.db` — ideal als Backup oder für die Übertragung auf einen anderen PC.

### Sicheres Löschen

Rechtsklick auf einen Eintrag → "Sicher löschen" entfernt den Eintrag und führt VACUUM aus, um den Speicherplatz physisch freizugeben. Damit werden alle Spuren des Inhalts aus der Datenbankdatei entfernt.

### Gesamte History löschen

Einstellungen → Daten → "History löschen". Optional können gepinnte Einträge beibehalten werden.

---

## 16. Datenschutz und Privatsphäre

- **Lokal only:** Alle Daten bleiben auf deinem Gerät. Clipwell sendet keine Daten ins Internet, außer für URL-Vorschauen.
- **URL-Vorschauen:** Nur aktiv, wenn in den Einstellungen aktiviert. Clipwell ruft Titel und Favicon der kopierten URL ab. Private, Loopback- und Link-Local-Adressen (127.x, 10.x, 192.168.x, 172.16–31.x, 169.254.x, fc00::/7) werden nie abgerufen.
- **Überwachung pausieren:** Über Tray-Menü jederzeit möglich. Clipwell zeichnet dann nichts auf.
- **URL-Cache:** Abgerufene Titel und Favicons werden 7 Tage in der Datenbank gecacht. Der Cache kann in den Einstellungen geleert werden.

---

## 17. Datenpfade

| Was | Pfad |
|-----|------|
| Einstellungen | `%APPDATA%\Clipwell\settings.json` |
| History-Datenbank | `%APPDATA%\Clipwell\history.db` |
| Fehlerprotokoll | `%APPDATA%\Clipwell\clipwell.log` |
| Onboarding-Marker | `%APPDATA%\Clipwell\onboarding.seen` |
| WAL-Datei (SQLite) | `%APPDATA%\Clipwell\history.db-wal` |
| Wiederherstellungs-Backup | `%APPDATA%\Clipwell\history.db.broken-YYYYMMDD-HHMMSS` |
| Auto-Backups | Konfigurierbarer Ordner, `clipwell-autobackup-YYYYMMDD-HHmmss.json` |

---

## 18. Fehlerbehebung

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

- Der "Weichzeichnen"-Effekt im Bild-Editor zeigt eine graue Überlagerung, keinen echten Gauß-Blur.
- Hotkey-Einschränkung: Nur Buchstaben A–Z und Ziffern 0–9 werden als Tastenanteil unterstützt. Funktionstasten können im Hotkey-Recorder nicht aufgenommen werden, sind aber als Popup-Shortcuts möglich.

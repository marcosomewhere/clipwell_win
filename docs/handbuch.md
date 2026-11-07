# Merkautomat für Windows — Benutzerhandbuch

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
10. [Sprechwerk: Sprachnotizen und Diktat](#10-sprechwerk-sprachnotizen-und-diktat)
11. [Pinstation](#11-pinstation)
12. [Eyedropper — Farben vom Bildschirm](#12-eyedropper--farben-vom-bildschirm)
13. [Desktop-Aufnahme und Ideenkombinat](#13-desktop-aufnahme-und-ideenkombinat)
14. [Detailfenster](#14-detailfenster)
15. [Kontrollstation](#15-kontrollstation)
16. [Export, Import und Backup](#16-export-import-und-backup)
17. [Datenschutz und Privatsphäre](#17-datenschutz-und-privatsphäre)
18. [Datenpfade](#18-datenpfade)
19. [Fehlerbehebung](#19-fehlerbehebung)

---

## 1. Überblick

Merkautomat ist ein lokaler Windows-Clipboard-Manager, der im Hintergrund als Tray-App läuft. Er zeichnet automatisch auf, was du in die Zwischenablage kopierst, und öffnet auf Tastendruck ein tastaturgesteuertes Popup, über das du jeden früheren Eintrag gezielt einfügen, suchen oder verwalten kannst.

**Was Merkautomat aufzeichnet:**

| Typ | Beschreibung |
|-----|-------------|
| Text | Beliebiger Unicode-Text |
| Code | Erkannter Quellcode (15+ Sprachen) oder Konfigurationsdateien |
| URL | Webadressen mit optionalem Titel und Favicon |
| Farbe | Hex-Farbcodes, die automatisch erkannt werden |
| Bild | Kopierte Bilder aus Browser, Office, Screenshot-Tool etc. |

Alle Daten verbleiben lokal auf deinem Gerät in einer SQLite-Datenbank.

**Die Bereiche:**

| Name | Bereich |
|------|---------|
| Merkautomat | Die Anwendung selbst: Tray, Popup und Zwischenablage-History |
| Ideenkombinat | Wissensbasis mit Dashboard, Suche, Brain, Sprachnotizen, Bildschirmaufnahmen und Zwischenablage |
| Pinstation | Fenster mit allen angepinnten Einträgen |
| Sprechwerk | Sprachnotizen mit lokalem Diktat (Whisper) |
| Kontrollstation | Einstellungen |

Programmdatei und Prozess heissen `Merkautomat.exe`, der Autostart-Eintrag heisst Merkautomat. Der Datenordner bleibt `%APPDATA%\Clipwell`, damit vorhandene Inhalte und Einstellungen weiter genutzt werden.

---

## 2. Systemvoraussetzungen und Installation

**Voraussetzungen:**
- Windows 10 Version 2004 (Build 19041) oder neuer
- .NET 10 Windows Desktop Runtime

**Starten:**
```
Merkautomat.exe
```

Beim ersten Start legt Merkautomat automatisch seinen Datenpfad unter `%APPDATA%\Clipwell\` an, registriert sich optional im Autostart und öffnet das Onboarding.

---

## 3. Erststart und Onboarding

Beim allerersten Start öffnet sich das Onboarding-Fenster. Es führt durch:

1. **Hotkey-Test** — Du kannst die konfigurierte Tastenkombination ausprobieren, bevor du fortfährst.
2. **Abschluss** — Das Fenster schließt sich und das Tray-Icon erscheint in der Taskleiste.

Das Onboarding erscheint nur einmal. Es kann nicht manuell erneut geöffnet werden.

---

## 4. Tray-Icon und Grundbedienung

Merkautomat lebt als Icon in der Windows-Taskleiste (Systembereich). Ein Linksklick auf das Icon öffnet oder schließt das Popup. Ein Rechtsklick öffnet das Kontextmenü:

| Menüeintrag | Funktion |
|-------------|----------|
| Öffnen | Popup öffnen |
| Pinstation | Pinstation-Fenster ein-/ausblenden |
| Kontrollstation | Einstellungen öffnen |
| Beenden | Merkautomat vollständig beenden |

**Überwachung pausieren:** Über das Tray-Menü kann die Clipboard-Überwachung temporär angehalten werden. Solange die Überwachung pausiert ist, werden keine neuen Einträge aufgezeichnet.

---

## 5. Das Popup-Fenster

### Öffnen

Der Standard-Hotkey ist **Win+Shift+V**. Dieser kann in der Kontrollstation geändert werden.

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

**Zeitangaben** passen zu den Gruppen: Heutiges erscheint relativ ("vor 6 Min.", "vor 3 Std."), Gestriges mit Uhrzeit ("Gestern, 23:37"), Einträge dieser Woche mit Wochentag ("Di, 14:05"), ältere mit Datum.

**Bilder** zeigen als Titel ihre Größe ("Bild 1280×720") und darunter den erkannten Text in Anführungszeichen, oder "Kein Text erkannt".

**Aktionsleiste** rechts neben dem Suchfeld, in zwei Gruppen:

| Symbol | Aktion |
|--------|--------|
| Kreis | Desktop-Aufnahme starten/beenden; während der Aufnahme rot hinterlegt |
| Stift | Schnellnotiz öffnen/schließen; geöffnet grau hinterlegt (Tooltip zeigt das Tastenkürzel) |
| Pipette | Farbe vom Bildschirm wählen |
| Ideenkombinat-Symbol | Ideenkombinat öffnen |
| Liste | Mehrfachauswahl ein/aus; aktiv grau hinterlegt |

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
| MD | Nur Markdown-Einträge (Label MD); sie erscheinen auch unter "Text" |
| URL | Webadressen |
| Bild | Kopierte Bilder |
| Farbe | Erkannte Hex-Farbcodes |
| Gepinnt | Nur angepinnte Einträge |

### Volltextsuche

Tippen im Suchfeld durchsucht Inhalt, OCR-Text (bei Bildern), URL-Titel und erkannte Sprache gleichzeitig. Groß-/Kleinschreibung spielt keine Rolle. Bei sehr großer History (mehr als 1000 Einträge) nutzt Merkautomat einen Suchindex, damit die Suche schnell bleibt.

### Regex-Suche

Der Button `.*` rechts im Suchfeld schaltet auf reguläre Ausdrücke um, z. B. `^https?://` oder `\d{4}-\d{2}`. Ungültige Ausdrücke liefern keine Treffer.

### Suchhistorie

Merkautomat merkt sich die letzten 5 Suchen. Sie erscheinen als Liste, wenn das Suchfeld leer ist und den Fokus hat.

### Sortierung

Der Sortier-Chip rechts in der Chip-Leiste zeigt die aktuelle Reihenfolge ("Neueste", "Älteste", "Häufig genutzt", "Selten genutzt") und öffnet per Klick die Auswahl. Weicht die Sortierung vom Standard ab, ist der Chip hervorgehoben. Gepinnte Einträge bleiben immer oben.

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
1. Merkautomat schreibt den Inhalt in die Zwischenablage.
2. Das Popup schließt sich.
3. Das vorher aktive Fenster wird in den Vordergrund gebracht.
4. Merkautomat sendet automatisch Ctrl+V.

**Hotkey für einen Eintrag:** Rechtsklick auf einen Eintrag → **Hotkey zuweisen …**, in das Feld klicken und die Kombination drücken (z. B. Ctrl+Alt+1; Buchstaben und Ziffern brauchen Ctrl, Alt oder Win, F-Tasten gehen auch allein). Optional "Als reinen Text einfügen". Der Hotkey gilt global: In jedem Programm fügt er diesen Eintrag sofort ein, ohne das Popup zu öffnen. Der Eintrag wird dabei gepinnt, damit die automatische Bereinigung ihn nicht löscht; die Kombination steht in der Liste neben der Zeit. Ist eine Kombination schon vom Popup-Hotkey, einem anderen Eintrag, Windows oder einem anderen Programm belegt, sagt der Dialog das. Verwalten, ändern, entfernen und alle auf einmal ausschalten: Kontrollstation → Tastenkürzel → Hotkeys für Einträge.

**Ctrl+Enter** fügt als reinen Text ein. Mit **Enter** bleibt bei Inhalten aus dem Browser die Formatierung erhalten (Tabellen, Listen, Überschriften, Links), sofern das Zielprogramm HTML annimmt, etwa Word, Outlook oder Confluence. Rich-Text aus Word (RTF) wird weiterhin als reiner Text gespeichert.

**Drag & Drop:** Ein Eintrag kann aus der Liste direkt in ein anderes Fenster gezogen werden.

### Nur in die Zwischenablage kopieren

Über den Hover-Button (erscheint beim Überfahren eines Eintrags mit der Maus) oder das Rechtsklick-Kontextmenü kann ein Eintrag ohne Einfügen in die Zwischenablage kopiert werden. Das Popup bleibt dabei offen.

### Kopierte Dateien

Kopierst du eine Datei (z. B. im Explorer mit Strg+C), speichert Merkautomat bei lesbaren Dateitypen den Textinhalt als Eintrag, nicht nur das Dateielement. Der Eintrag trägt als Titel "Datei: name.txt", der Erkennungsgrund nennt "Als Datei kopiert". Im Detailfenster steht die Datei als Quelle; ein Klick darauf zeigt sie im Explorer. Bei mehreren Dateien landen alle Inhalte in einem Eintrag, getrennt durch Zeilen "--- Datei: name ---".

Welche Endungen gelesen werden, legst du in der Kontrollstation fest (Standard: über 120 Textformate wie txt, eml, bat, cfg, config, xml, js, json, ps1, yaml, md, csv). Binärdateien werden auch bei passender Endung übersprungen.

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
- **Tastenkürzel:** Standard Ctrl+P (konfigurierbar in der Kontrollstation)

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

Ohne Auswahl beim Export: Merkautomat fragt, ob die gesamte History exportiert werden soll.

---

## 10. Sprechwerk: Sprachnotizen und Diktat

Das Sprechwerk ist der Bereich fuer Sprachnotizen im Ideenkombinat, getrennt von der Zwischenablage-History. Jede Notiz ist eine Markdown-Datei in `%APPDATA%\Clipwell\notes\`.

### Schnellnotiz im Popup

Über das Notizblock-Symbol in der Toolbar öffnet sich ein kleines Eingabefeld am unteren Rand des Popups.

- **Enter** oder **Speichern** legt die Notiz im Sprechwerk an und übernimmt sie sofort ins Ideenkombinat.
- **Shift+Enter** fügt einen Zeilenumbruch ein.
- **Esc** schließt das Eingabefeld ohne zu speichern und bricht ein laufendes Diktat ab.
- **Mikrofon-Symbol:** startet das Diktat direkt im Eingabefeld, das Symbol wird zum roten Stopp-Quadrat. Erneut klicken beendet die Aufnahme, der erkannte Text wird an der Cursorposition eingefügt. Speichern während der Aufnahme beendet sie vorher. Unter dem Feld steht die Aufnahmedauer bzw. "Wird transkribiert".
- **Fenster-Symbol:** oeffnet die Sprachnotizen im Ideenkombinat und uebernimmt den bisherigen Text ins Eingabefeld. Ein vorhandener Entwurf bleibt erhalten.
- **Tastenkürzel:** Standard Ctrl+N (konfigurierbar)

Ältere Schnellnotizen, die noch als gepinnte Einträge mit dem Badge "NOTE" in der History stehen, bleiben dort unverändert.

### Sprachnotizen im Ideenkombinat

Die drei Bereiche links lassen sich an ihrer Ueberschrift auf- und zuklappen (auch mit Enter oder Leertaste). Der Pfeil zeigt den Zustand an. Jeweils ein Bereich nutzt den verfuegbaren Listenplatz; die Auswahl in anderen Bereichen bleibt erhalten. Der Scrollbalken ist breiter und bleibt bei ueberlaufenden Listen sichtbar. Im geoeffneten Sprechwerk steht das kompakte Formular **Neue Sprachnotiz** direkt bereit. Das Textfeld waechst mit dem Inhalt bis 160 Pixel Hoehe; danach scrollt es. Das mic-rec-Symbol startet das Diktat, die orange Diskette speichert die Sprachnotiz.

Im Tray das Ideenkombinat oeffnen und links den Bereich Sprechwerk waehlen. Das separate Sprechwerk-Fenster und das alte Tray-Untermenue entfallen. Hier stehen auch aeltere, nicht exportierte Sprachnotizen; bestehende exportierte Fassungen haben Vorrang. Neue Sprachnotizen entstehen im Eingabefeld mit Diktat und Speichern.

### Diktat

Das Diktat läuft komplett lokal mit Whisper; Audio verlässt den Rechner nicht und wird nicht gespeichert. Das Sprachmodell "Base" ist bereits enthalten (Ordner `models` neben `Merkautomat.exe`), Diktieren funktioniert also sofort und offline. Die größeren Modelle "Small" und "Medium" lädt Merkautomat erst nach Rückfrage einmalig nach `%APPDATA%\Clipwell\models\`. Modellgröße und Sprache (Deutsch, Englisch, Automatisch) stehen unter Einstellungen → Allgemein → "Diktat (Whisper, lokal)"; größere Modelle erkennen genauer, brauchen aber länger.

Während der Aufnahme ist der Button rot, unten rechts läuft die Dauer mit. Nach dem Beenden steht "Transkribiere ..." in der Statusleiste, bis der Text eingefügt ist. Man kann jederzeit zwischendurch diktieren und dazwischen tippen. Es läuft immer nur ein Diktat gleichzeitig.

Voraussetzung: ein Mikrofon, und unter Windows-Einstellungen → Datenschutz → Mikrofon muss der Zugriff für Desktop-Apps erlaubt sein.

### Speicherung

Neue Sprachnotizen werden beim Speichern im Notizordner und als Fassung im Ideenkombinat abgelegt. Aeltere, nicht exportierte Dateien erscheinen direkt aus dem Notizordner; sie werden weder verschoben noch ueberschrieben.

---

## 11. Pinstation

Die Pinstation ist ein separates, immer-im-Vordergrund-Fenster, das alle angepinnten Einträge anzeigt.

**Öffnen:** Tray-Menü → Pinstation oder Tastenkürzel Ctrl+B (konfigurierbar) im Popup.

Die Reihenfolge lässt sich per Drag & Drop ändern: Der gezogene Eintrag wird farbig umrandet, eine Linie oben oder unten am Zieleintrag zeigt, wo er landet. Ein Ablegen im leeren Bereich unter der Liste verschiebt ihn ans Ende. Dieselbe Reihenfolge gilt für die Gruppe "Gepinnt" im Popup; neu gepinnte Einträge stehen dort oben.

In der Pinstation können Einträge ebenfalls per Klick oder Tastatur eingefügt werden. Die Pinstation bleibt offen, während das Popup geöffnet und geschlossen wird.

---

## 12. Eyedropper — Farben vom Bildschirm

Der Eyedropper ermöglicht es, eine Farbe direkt vom Bildschirm zu entnehmen.

**Öffnen:** Pipetten-Symbol in der Toolbar des Popups.

Das Popup schließt sich, und ein transparentes Vollbild-Overlay erscheint. Mit einem Klick auf einen beliebigen Bildschirmpunkt wird die Farbe an dieser Stelle als Hex-Wert aufgezeichnet, in die Zwischenablage kopiert und als Farbeintrag in die History gespeichert.

---

## 13. Desktop-Aufnahme und Ideenkombinat

### Aufnahme

Die Desktop-Aufnahme protokolliert, was im jeweils aktiven Fenster zu sehen ist, und speichert es fortlaufend als Markdown.

**Starten und beenden:** Record-Symbol (Kreis) im Popup-Kopf, "Aufnahme starten" unter "Neue Aufnahme" links im Ideenkombinat oder Tray-Menü → "Desktop aufzeichnen". Während der Aufnahme ist das Symbol rot (Stopp-Quadrat), das Tray-Icon zeigt "Aufnahme läuft". Derselbe Button beendet die Aufnahme.

**Was erfasst wird:** Merkautomat liest den sichtbaren Text des Vordergrundfensters über die Windows-Bedienungshilfen-Schnittstelle (UI Automation): Fenstertitel, Anwendung, sichtbarer Dokumenttext, Beschriftungen, Links, Listen, Eingabewerte und Alternativtexte von Bildern. Es werden keine Screenshots aufgenommen.

**Zeilen und Tabellen:** Textstücke, die nebeneinander in einer Zeile stehen, werden zu einer Zeile zusammengesetzt; umbrochene Zeilen eines Absatzes werden wieder zu einem Absatz verbunden. Tabellen, deren Zellen ihre Position melden (z. B. HTML-Tabellen im Browser), erscheinen als Markdown-Tabelle.

**Nur Seiteninhalt (Browser):** Standardmäßig schreibt Merkautomat bei Browsern (Chrome, Edge, Opera, Firefox, Brave, Vivaldi u. a.) nur die URL aus der Adressleiste und den Inhalt der Webseite. Tabs, Symbolleisten und Erweiterungen entfallen; Navigations-, Kopf-, Such- und Fußbereiche der Seite werden übersprungen, Seitenleisten ans Ende gestellt. Hat die Seite einen gekennzeichneten Hauptbereich, wird nur dieser geschrieben. Einstellungen → "Desktop-Aufnahme: nur Seiteninhalt" ausschalten, um wieder alles Sichtbare zu erfassen.

**Wann ein Eintrag entsteht:** Merkautomat prüft das aktive Fenster alle 0,5 Sekunden. Ein Fenster- oder Titelwechsel (z. B. neuer Browser-Tab) wird erfasst, sobald er 0,5 Sekunden stabil ist. Kurzes Durchklicken erzeugt dadurch keine Einträge. Bleibst du im selben Fenster, liest Merkautomat alle 2,5 Sekunden neu und schreibt nur Zeilen, die für dieses Fenster noch nicht protokolliert wurden.

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

- Merkautomat-eigene Fenster (Popup, Ideenkombinat usw.).
- Passwortfelder.
- Alles, solange "Überwachung pausieren" aktiv ist.
- Hängende Fenster ("Keine Rückmeldung").
- Inhalte ohne Textinformation: Bilder, Videos, Diagramme in Canvas-Grafik, Spiele, manche Java-Anwendungen und Editoren ohne Bedienungshilfen-Text (z. B. der Textbereich von Notepad++).

Jede Aufnahme landet als eigene Datei in `%APPDATA%\Clipwell\recordings\` (Name `JJJJ-MM-TT_hh-mm-ss.md`) und endet mit Ende-Zeit, Dauer und Anzahl der Einträge.

### Ideenkombinat

**Öffnen:** Ideenkombinat-Symbol in der Aktionsleiste des Popups oder Tray-Menü → "Ideenkombinat" (mit demselben Symbol).

Das Fenster nutzt dieselbe Kopfleiste und dieselben Buttons wie das Detailfenster. Links stehen oben **Dashboard** und **Gesamtübersicht**, darunter die Suche (Strg+F, über Titel, Anwendungen, Fenstertitel und Inhalt) und drei getrennte Bereiche mit eigener Liste:

- **Sprechwerk:** alle Sprachnotizen inklusive aelterer, nicht exportierter Dateien, neueste zuerst.
- **Bildschirmaufnahmen:** Desktop-Aufnahmen, benannt nach den erfassten Anwendungen, mit Startzeit, Anzahl der Einträge und Dauer.
- **Zwischenablage:** alle Einträge der Merkautomat-History, neueste zuerst, mit Typ und Zeitpunkt. Rechts erscheint der Eintrag als Markdown (Code als Codeblock, Bilder mit erkanntem Text); Doppelklick öffnet das Detailfenster. Neue Kopien erscheinen automatisch.

Ein Klick auf einen Bereichskopf oder einen Eintrag macht den Bereich aktiv (orange umrandet). Nur für den aktiven Bereich erscheint unten das passende Eingabefeld.

**Auswahl:** Ein Klick wählt einen Artikel, Strg+Klick und Umschalt+Klick erweitern die Auswahl, auch über die Bereiche hinweg. Ein Klick auf das Kästchen links fügt den Artikel ohne Tastatur hinzu oder entfernt ihn wieder. "Alle auswählen" im Bereichskopf fügt alle Artikel des Bereichs hinzu, "Alle"/"Keine" unten gelten für alle Bereiche. Rechts erscheint das Markdown des gewählten Artikels, bei mehreren eine zusammengefasste Vorschau.

**Neue Bildschirmaufnahme** (Bereich Bildschirmaufnahmen aktiv): Der Button mit dem "KB REC"-Logo beginnt die Aufnahme (während sie läuft, zeigt er ein rotes Stopp-Symbol) und wählt sie aus, sodass neue Einträge rechts live mitlaufen. Darunter stehen Startzeit, Laufzeit und Anzahl der Einträge; derselbe Button beendet die Aufnahme.

**Neue Sprachnotiz** (Bereich Sprechwerk aktiv): mit "Diktieren" sprechen oder tippen (erneut klicken beendet das Diktat, der Text landet an der Cursorposition). "Speichern" oder Strg+Enter legt die Notiz in den Notizen an, übernimmt sie sofort ins Ideenkombinat und wählt sie aus. Esc bricht ein laufendes Diktat ab.

Das Dashboard zeigt das Ideenkombinat als Instrumententafel: Sieben-Segment-Zähler für Artikel im Ideenkombinat, Sprachnotizen, Bildschirmaufnahmen und Zwischenablage-Einträge; vier Analog-Instrumente für die laufende bzw. letzte Bildschirmaufnahme (erfasste Bildwechsel, Fenster, Anwendungen, Minuten); Kacheln mit erfassten Inhalten je Anwendung und kopierten Inhalten je Webseite. Der Aufnahmestatus steht in der Kopfleiste. Kopieren liefert auf dem Dashboard das Markdown der Gesamtübersicht.

Die Gesamtübersicht fasst alles zusammen:

- **Zwischenablage:** Anzahl Einträge und gepinnte, Zeitraum, Verteilung nach Typ, Einträge der letzten 7 Tage, häufigste Domains, Inhaltsarten, meistgenutzte, gepinnte und zuletzt kopierte Einträge.
- **Sprechwerk:** Anzahl und Liste aller Sprachnotizen.
- **Desktop-Aufnahmen:** Anzahl, erfasste Bildwechsel, Gesamtdauer, häufigste Anwendungen und je Aufnahme die besuchten Fenster.

Buttons oben: Kopieren (angezeigtes Markdown, landet nicht in der History), Exportieren (Strg+E), Löschen (Entf), Aktualisieren (F5), Datenordner öffnen und Schließen (Esc).

**Importieren** (oben, Strg+O) übernimmt Markdown- und Textdateien (`.md`, `.txt`) als Sprachnotizen ins Ideenkombinat; sie erscheinen auch im Sprechwerk, in der semantischen Suche und im Brain. Mehrere Dateien auf einmal sind möglich, ebenso Dateien einfach ins Fenster ziehen. Hat eine Datei keine eigene Überschrift, wird der Dateiname zur Überschrift. Leere Dateien, andere Dateitypen und Dateien über 2 MB werden übersprungen und gemeldet.

**Exportieren** gilt für die ausgewählten Artikel, ohne Auswahl für alle angezeigten. Der Dialog bietet:

- **Zusammengefasstes Markdown:** eine `.md`-Datei mit Inhaltsverzeichnis und allen Artikeln hintereinander (Überschriften eine Ebene tiefer).
- **Alles als ZIP:** jeder Artikel als eigene `.md`-Datei in den Ordnern `sprechwerk`, `bildschirmaufnahmen` und `zwischenablage`. Hat mindestens ein Artikel eine Kategorie, ordnet das ZIP zuerst nach Kategorie: `Hauptkategorie/Unterkategorie/sprechwerk/…`, Artikel ohne Kategorie unter `ohne kategorie/…`.
- **Kategorien:** Jeder Artikel mit Kategorie bekommt unter seiner Überschrift die Zeile "Kategorie: Haupt › Unter"; im zusammengefassten Markdown steht sie zusätzlich im Inhaltsverzeichnis. Die Originaldateien bleiben unverändert.
- **Gesamtübersicht beilegen:** als eigener Abschnitt bzw. als `uebersicht.md` im ZIP.

**Löschen** wirkt auf alle ausgewählten Artikel: Bildschirmaufnahmen werden als Datei gelöscht (nicht die laufende), Sprachnotizen nur in ihrer Fassung im Ideenkombinat, Zwischenablage-Einträge aus der Merkautomat-History (wie Löschen im Popup). Eine laufende Aufnahme und neue Notiz-Exporte aktualisieren das Ideenkombinat automatisch.

### Aufgaben

**Öffnen:** "Aufgaben" links im Ideenkombinat (zwischen Gesamtübersicht und Suche) oder Strg+T. Neben dem Menüpunkt steht die Zahl der offenen Aufgaben.

**Aus einem Eintrag:** Im Popup Rechtsklick auf einen Eintrag → **Zu Aufgaben hinzufügen**. Das Ideenkombinat öffnet sich mit der neuen Aufgabe; der Titel ist die erste Zeile des Eintrags und gleich zum Umbenennen markiert. Der Text des Eintrags wird bei der Aufgabe mitgespeichert (bis 4000 Zeichen), damit sie auch nach dem Löschen oder Bereinigen des Eintrags verständlich bleibt.

**Eigene Aufgabe:** Oben eintippen und Enter drücken. Ist im Kalender ein Tag gewählt, bekommt die neue Aufgabe dieses Fälligkeitsdatum.

**Kalender:** Über der Liste steht der aktuelle Monat mit Kalenderwochen (KW, Woche beginnt am Montag). Ein Punkt unter dem Tag zeigt fällige Aufgaben: orange offen, rot überfällig, grau nur erledigte; der Tooltip nennt die Anzahl. Ein Klick auf einen Tag zeigt nur die an diesem Tag fälligen Aufgaben, ein Klick auf die KW die der ganzen Woche. Ein zweiter Klick oder **Filter aufheben** zeigt wieder alle. Beim Filtern springt "Offen" auf "Alle", damit auch erledigte Aufgaben des Tages sichtbar sind. Die Pfeile blättern den Monat, **Heute** springt zum aktuellen Monat und zeigt die heute fälligen Aufgaben. Aufgaben ohne Fälligkeit erscheinen nur ohne Kalenderfilter.

**Bearbeiten:** Rechts Titel, Fälligkeit und Notizen ändern. Ein Klick in das Feld "Fällig am" öffnet einen Kalender (auch Alt+Pfeil runter bzw. F4): Tag anklicken, oder schnell **Heute**, **Morgen**, **Nächster Montag**, **Kein Datum**; eintippen geht weiterhin (TT.MM.JJJJ oder TT.MM., leer für kein Datum); Änderungen werden automatisch gespeichert. Das Kästchen vor einer Aufgabe oder **Erledigt** rechts hakt sie ab. Die Chips **Offen**, **Erledigt** und **Alle** filtern. Offene Aufgaben stehen nach Fälligkeit sortiert oben, überfällige sind rot markiert. **Heute erledigt** oben rechts hakt nach Rückfrage alle offenen Aufgaben ab, die heute fällig sind; überfällige von früheren Tagen bleiben offen. **Löschen** daneben (oder Entf) entfernt nur die Aufgabe, nie den Eintrag. **Kopieren** oben kopiert die angezeigten Aufgaben als Markdown-Checkliste.

**Aus Outlook:** Mit dem Outlook-Add-in (einrichten unter Kontrollstation → Outlook) Rechtsklick auf eine Mail → **Aufgabe in Merkautomat erstellen**, bei mehreren markierten Mails **Aufgaben in Merkautomat erstellen**. Titel ist der Betreff ohne "AW:", "WG:" usw.; Absender, Empfangszeit, Betreff und Mailtext stehen als Quelle bei der Aufgabe. Die Quelle nutzt die ganze restliche Höhe; der Trenner zwischen Notizen und Quelle lässt sich ziehen. Links darin sind anklickbar und öffnen im Browser; lange Tracking-Links werden auf ihre Domain gekürzt angezeigt, die volle Adresse steht im Tooltip. Unsichtbare Füllzeichen und überzählige Leerzeilen aus Newslettern werden entfernt. Die Mail selbst bleibt in Outlook.

Gespeichert wird in `%APPDATA%\Clipwell\tasks.json`.

### Suche

**Öffnen:** "Suche" links im Ideenkombinat oder Strg+K.

Die semantische Suche findet Inhalte nach ihrer Bedeutung, nicht nur nach gleichen Wörtern. "Wie habe ich das VPN-Zertifikat erneuert" findet auch einen Eintrag, in dem "OpenVPN-Cert" und "Firmenportal" stehen, auch über Sprachen hinweg. Durchsucht werden Zwischenablage, alle Sprachnotizen aus dem Sprechwerk (auch nicht ins Ideenkombinat exportierte; "Öffnen" zeigt sie im Ideenkombinat) und Bildschirmaufnahmen. Bilder ohne erkannten Text und sehr kurze Einträge werden nicht aufgenommen.

**Modell:** Das Sprachmodell multilingual-e5-small (MIT-Lizenz, ca. 123 MB) wird mit Merkautomat ausgeliefert (`models\e5-small\` neben `Merkautomat.exe`). Die Suche läuft komplett lokal, es wird nichts gesendet. Nur wenn der Ordner fehlt oder beschädigt ist, erscheint ein Hinweis mit "Modell laden"; das holt das Modell von Hugging Face nach `%APPDATA%\Clipwell\models\e5-small\` und prüft die Prüfsumme.

**Index:** Beim Öffnen der Suche wandelt Merkautomat neue oder geänderte Inhalte im Hintergrund in Vektoren um, neueste zuerst. Der Fortschritt steht unter der Überschrift; die Suche liefert schon während des Aufbaus Treffer. Der erste Aufbau einer großen History dauert einige Minuten und belastet die CPU spürbar; danach kommen nur neue Inhalte dazu. Gespeichert werden nur Vektoren in `semantic.db`, kein Text.

Nicht indiziert werden Farben, Bilder ohne OCR-Text und kurze Einträge ohne Leerzeichen (z. B. Passwörter, IDs, Codes).

**Übersicht:** Solange das Suchfeld leer ist, listet die Suche alles, was im Index steht, neueste Einträge zuerst; die Chips filtern nach Quelle. Die Zeile unter der Überschrift nennt die Anzahl je Quelle (Zwischenablage-Einträge, Sprachnotizen, Bildwechsel aus Bildschirmaufnahmen).

**Suchen:** Frage oder Stichworte eintippen, die Treffer erscheinen nach kurzer Pause. Enter sucht sofort und nimmt dabei auch Einträge auf, die seit dem Öffnen dazugekommen sind. Die Chips darunter schränken auf eine Quelle ein. Jeder Treffer zeigt Quelle, Datum, Ähnlichkeit in Prozent und "Worttreffer", wenn ein Suchwort wörtlich vorkommt. Seltene Suchwörter zählen dabei mehr als Wörter, die fast überall stehen. Kurze Wörter bis drei Buchstaben treffen nur am Wortanfang ("art" findet "Art", nicht "Wartezeit"); längere auch in zusammengesetzten Wörtern ("rechnung" in "Stromrechnung"). Fast gleiche Treffer, etwa derselbe Bildschirminhalt mehrmals aufgenommen, werden zusammengefasst ("+3 fast gleich"); die Vorschau rechts listet sie auf.

**Fragen:** Eine Frage eintippen und **Fragen** klicken (oder Strg+Enter). Merkautomat sucht wie oben die besten Treffer, gibt die sechs passendsten mit Nummer an das lokale Sprachmodell Granite und zeigt über der Trefferliste eine kurze Antwort mit Belegen wie [2]. Darunter stehen die zitierten Quellen als Knöpfe; ein Klick wählt den Treffer in der Liste und zeigt ihn in der Vorschau. Granite antwortet nur aus diesen Quellen. Stehen sie für die Frage nicht drin, heißt es "Keine belegte Antwort in deinen Einträgen" mit einem Hinweis, was fehlt. Eine Antwort ohne gültigen Beleg gilt ebenfalls als nicht belegt. Die Quellen-Chips schränken auch das Fragen ein. Granite läuft auf dem Prozessor: Die erste Antwort nach dem Start dauert länger, danach meist einige Sekunden bis eine halbe Minute. **Abbrechen** stoppt, **Kopieren** kopiert Frage, Antwort und Quellenliste. Fehlt Granite, bietet Merkautomat den einmaligen Download an (ca. 2,27 GB, Prüfsumme, danach ohne Internet). Die Antwort kann trotz Belegen falsch zusammengefasst sein; im Zweifel die zitierte Quelle lesen.

| Aktion | Wirkung |
|--------|---------|
| Fragen / Strg+Enter | Beantwortet die Frage lokal mit Granite aus den besten Treffern, mit Quellenangaben |
| Ähnliche | Sucht Einträge, die inhaltlich dem gewählten Treffer ähneln |
| Ähnliche zu Markierten | Sucht Einträge, die allen markierten Treffern zusammen ähneln, zusammen mit dem Suchtext; die Markierten bleiben in der Liste. Weitere markieren und erneut klicken schärft das Ergebnis |
| Öffnen / Doppelklick | Zwischenablage: Detailfenster. Sprachnotiz und Bildschirmaufnahme: Artikel im Ideenkombinat, Aufnahmen an der Stelle des Bildwechsels |
| Dubletten | Zeigt Gruppen fast gleicher Abschnitte der gewählten Quelle, größte zuerst |
| Kopieren (oben) | Kopiert die Vorschau |
| Index löschen | Entfernt alle gespeicherten Vektoren; der Index wird bei der nächsten Suche neu aufgebaut |

Das Modell wird nur geladen, solange das Ideenkombinat offen ist, und danach wieder freigegeben.

### Brain (Wissensnetz)

**Öffnen:** "Brain" links im Ideenkombinat oder Strg+B. Die Bibliothek ist hier zunächst eingeklappt, damit das Netz mehr Platz hat; **Bibliothek einblenden** zeigt die Eintragslisten wieder. Strg+F springt direkt in die Netzsuche. **Nach Bedeutung** (oder Enter) sucht semantisch, **Zurücksetzen** entfernt die Suchhervorhebung. Oben stehen die Netzansicht, **Weg finden …** und **Beziehungen …**; rechts die Aktionen zur gewählten Quelle oder zum Thema. Der Filter **Manuell** steuert die selbst gepflegten Beziehungen. Die Verbindungsliste folgt den eingeschalteten Bezugsarten.

Das Brain zeigt alle indizierten Einträge als Netz: jeder Zwischenablage-Eintrag, jede Sprachnotiz und jeder Bildwechsel einer Bildschirmaufnahme ist ein Knoten, Bezüge zwischen ihnen sind Linien. Merkautomat ermittelt die Bezüge selbst, lokal und ohne Netzwerk:

| Bezug | Bedeutung | Linie |
|-------|-----------|-------|
| Inhaltlich | Bedeutung ähnlich (über der eingestellten Schwelle; jeder Eintrag verknüpft sich mit seinen bis zu 5 ähnlichsten) | Farbe des Themas, grau zwischen Themen |
| Zeitlich | innerhalb von 5 Minuten entstanden (bis zu 3 nächste je Eintrag) | blau |
| Gleiche Aufnahme | aufeinanderfolgende Bildwechsel derselben Bildschirmaufnahme | hellgrau |
| Gleiche Webseite | gleiche Domain (kopierte URL, Quelle der Kopie oder Adresse in der Aufnahme), zeitlich verkettet | grün |

**Themen:** Inhaltlich eng verknüpfte Einträge bilden ein Thema. Jedes Thema hat eine Farbe und einen Namen aus seinen typischen Stichworten (z. B. "Zertifikat · Vpn · Portal"). Einzelne Einträge ohne inhaltliche Nachbarn bleiben grau.

**Darstellung:** Kreis = Zwischenablage, Raute = Sprachnotiz, Quadrat = Bildwechsel einer Aufnahme. Je mehr Verknüpfungen, desto größer der Knoten. Das Netz ordnet sich beim Öffnen selbst an (Verknüpftes rückt zusammen).

| Bedienung | Wirkung |
|-----------|---------|
| Mausrad | Zoomt an der Mausposition |
| Ziehen auf freier Fläche | Verschiebt die Ansicht |
| Knoten ziehen | Verschiebt den Knoten (nach dem Anordnen) |
| Klick auf Knoten | Zeigt rechts Details und alle Verknüpfungen mit Begründung, z. B. "inhaltlich 91 % · zeitlich nah (2 min)" |
| Klick auf freie Fläche oder Esc | Zurück zur Themenliste (Esc leert vorher ein gefülltes Suchfeld) |
| Doppelklick auf Knoten / Öffnen | Öffnet den Artikel im Ideenkombinat |
| Ähnliche | Wechselt in die semantische Suche mit ähnlichen Einträgen |
| Pos1 / Einpassen | Zeigt das ganze Netz |
| Neu anordnen | Berechnet das Layout neu |

Rechts steht ohne Auswahl die **Themenliste**; ein Klick hebt das Thema hervor und zoomt darauf. Mit Auswahl stehen dort die **Verknüpfungen**; ein Klick springt zum verknüpften Eintrag.

**Werkzeugleiste:**

- **Suchfeld:** Getippte Wörter heben passende Knoten hervor (alle Wörter müssen vorkommen). Enter sucht nach Bedeutung und zoomt auf die Treffer.
- **Quellen:** blendet Zwischenablage, Sprachnotizen oder Bildschirmaufnahmen aus dem Netz aus.
- **Bezüge:** blendet Linienarten aus (die Themen bleiben).
- **Ähnlichkeit ab:** wie ähnlich zwei Einträge sein müssen, damit eine inhaltliche Linie entsteht (80 bis 95 %, Standard 86 %). Niedriger gibt mehr Verknüpfungen und größere Themen, höher nur sehr enge Bezüge.
- **Fokus:** hebt nur die Umgebung des gewählten Eintrags hervor (zwei Schritte weit).

**Kopieren** (oben) liefert im Brain Markdown: mit Auswahl den Eintrag samt Verknüpfungen, ohne Auswahl alle Themen mit ihren Einträgen.

Das Netz zeigt die neuesten 1000 Einträge. Es wird aus dem Index der semantischen Suche berechnet; neue Einträge kommen beim Öffnen und bei laufender Aufnahme automatisch dazu, bereits angeordnete Knoten behalten ihre Position.

Die Einstellungen der Werkzeugleiste (Ähnlichkeit, Quellen, Bezüge, Fokus) merkt sich Merkautomat in den Einstellungen und stellt sie beim nächsten Öffnen wieder her. Die Position verschobener Knoten wird nicht gespeichert.

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
- **Sprache:** Dropdown zur manuellen Auswahl der Code-Sprache (C#, JavaScript, Python, SQL usw.) mit Syntax-Highlight-Overlay. Bei sehr großen Skripten (über 40.000 Zeichen oder 1.500 Zeilen) schaltet der Skripteditor die Farbhervorhebung ab, damit Tippen und Scrollen flüssig bleiben; unten steht dann "ohne Hervorhebung (großer Text)".
- **Markdown:** Markdown-Texte (z. B. Antworten aus einem Chat mit Überschriften, Listen, Links und Codeblöcken) erkennt Merkautomat beim Kopieren als Markdown, nicht als Skript, auch wenn Codebeispiele darin stehen. Sie öffnen im **Markdown-Editor** zuerst formatiert; **Bearbeiten** zeigt den Quelltext, **Vorschau** wieder die formatierte Ansicht. Die Vorschau zeigt Überschriften, Listen, Hervorhebungen, Codeblöcke, Tabellen, Zitate und Links, führt aber kein HTML aus und lädt keine Bilder nach. Ältere Einträge, die noch als Skript gespeichert sind, öffnen ebenfalls als Markdown und werden beim Speichern zu Markdown. Markdown-Einträge tragen in der Liste das Label **MD**, die Suche findet sie mit `kind:md`. Beim Start prüft Merkautomat ältere Einträge einmal nach und stuft Markdown-Dokumente, die noch als Skript gespeichert waren, um. Im Popup stellt Rechtsklick → Typ ändern → **Als Markdown behandeln** einen Eintrag direkt um; **Als Code behandeln** hat Vorrang vor der Erkennung.
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

## 15. Kontrollstation

Öffnen über Tray-Menü → Kontrollstation. Hier liegen alle Einstellungen.

### Hotkey

| Option | Beschreibung |
|--------|-------------|
| Hotkey aufnehmen | Gewünschte Tastenkombination direkt eintippen |
| Manuelle Eingabe | Modifier-Checkboxen + Buchstabe |
| Hotkey-Aktion | "Popup öffnen" oder "Letzten Eintrag einfügen" |

Der Hotkey wird sofort registriert. Bei Konflikten zeigt Merkautomat den Windows-Fehlercode und versucht automatisch Fallback-Kombinationen (Win+Shift, Ctrl+Shift, Ctrl+Alt, Win+Alt).

### Allgemein

| Option | Beschreibung |
|--------|-------------|
| Design | System / Dunkel / Hell |
| Autostart | Merkautomat beim Windows-Login starten |
| Maximale Einträge | Älteste unpinnierte Einträge werden überschrieben (50–5000, Standard 500) |
| Maximales Alter | Einträge älter als N Tage automatisch löschen (0 = deaktiviert) |
| Max. DB-Größe | Datenbank auf N MB beschränken (0 = deaktiviert) |
| Code-Erkennung | Konservativ / Normal / Aggressiv |

### Lokale KI: automatische Kategorien

Im Reiter **Lokale KI** stehen **Aus**, **E5 · bestehende Kategorien (schnell, mitgeliefert)** und **Granite 4.2 3B · lokal (CPU)** zur Wahl. Standard ist Aus. **Funktion testen** prüft die gewählte Variante mit einem mitgelieferten Reisebeispiel.

**E5** verwendet dasselbe lokale Modell wie die semantische Suche. Normalerweise ist kein Download nötig; fehlt das Suchmodell, bietet die Kontrollstation einen Download an. E5 vergleicht Texte mit den Namen bereits gespeicherter Kategorien. Dafür müssen mindestens zwei Kategorien durch manuelle Zuordnung angelegt sein. Es erfindet keine Kategorie und verändert deinen Titel nicht. Verständliche Kategorien wie „Reisen“ oder „Softwareentwicklung“ sind hilfreicher als Kürzel. Bei ähnlichen Kategorien oder gemischten Quellen bleibt die Zuordnung offen. Die Ähnlichkeit ist keine Wahrscheinlichkeit für eine richtige Kategorie.

Im Funktionstest mit drei Kategorien dauerte E5 rund 2,4 Sekunden einschließlich Modellstart; weitere kurze Texte benötigten etwa 8–14 Millisekunden. Wiederholte Vergleiche nutzen zwischengespeicherte Vektoren. Das ist eine kleine Stichprobe; Dauer und Qualität hängen von Texten, Kategorien und Rechner ab. Neue oder umbenannte Kategorien werden beim nächsten Vorschlag berücksichtigt. Es gibt keinen Trainingslauf und kein automatisches Lernen aus Brain-Verbindungen.

**E5 lernt aus deinen Zuordnungen:** Sobald mindestens drei Kategorien existieren und eine Kategorie zwei oder mehr gespeicherte Zuordnungen hat, vergleicht E5 neue Texte zusätzlich mit diesen Beispielen (bis zu acht der neuesten je Kategorie). Dadurch werden auch Kategorien unterschieden, deren Namen allein zu ähnlich klingen. Du musst dafür nichts pflegen: Jede gespeicherte Zuordnung zählt automatisch als Beispiel, eine geänderte oder gelöschte Zuordnung fällt beim nächsten Vorschlag heraus. Vorschläge, die du nicht speicherst, werden nie zu Beispielen. Die gerade markierten Quellen zählen nicht als ihr eigenes Beispiel. Passt ein Text zu keiner Kategorie eindeutig, bleibt die Zuordnung weiterhin offen. E5 berücksichtigt höchstens 200 Kategorien und nur Quellenauszüge mit mindestens 20 Zeichen.

### Kategorien analysieren (Granite)

Wenn du noch keine Kategorien hast oder viele Einträge ohne Kategorie, kann Granite welche vorschlagen: im Ideenkombinat **Mehr → Kategorien analysieren (Granite) …** oder im Brain **Kategorien analysieren …**. Das funktioniert auch, wenn in der Kontrollstation E5 gewählt ist.

1. Fehlt Granite, bietet das Fenster den einmaligen Download an (etwa 2,27 GB). Danach läuft alles lokal.
2. **Analyse starten:** Merkautomat teilt die Einträge ohne Kategorie nach Inhalt in Gruppen. Granite liest je Gruppe die typischsten Einträge und schlägt einen Namen vor; vorhandene Kategorien werden bevorzugt. Je Gruppe dauert das etwa 10 bis 20 Sekunden, der erste Aufruf etwas länger. Gruppen, die E5 anhand deiner eigenen Zuordnungen eindeutig einer vorhandenen Kategorie zuordnet, sind ohne Granite sofort fertig; die Begründung nennt das („Granite nicht gestartet …“). **Abbrechen** ist jederzeit möglich.
3. Prüfe die Vorschläge: Namen ändern, unpassende Gruppen abwählen, unbenannte Gruppen selbst benennen. Mit der Maus über den Beispieltiteln siehst du alle Einträge einer Gruppe.
4. **Ausgewählte übernehmen** speichert nach Rückfrage. Bereits vorhandene Kategorien werden nicht überschrieben.

Danach schlägt E5 für neue Einträge passende Kategorien vor und lernt aus diesen Zuordnungen. Einzelne Einträge kannst du jederzeit im Einordnungsfenster ändern.

### Einordnen: Kategorien gezielt oder für alles vergeben

Das Einordnen nutzt das Modell, das unter **Kontrollstation → Lokale KI** gewählt ist. Ist die lokale KI aus, lassen sich Kategorien im selben Fenster von Hand vergeben.

- **Ein Knoten im Brain:** Knoten anklicken, rechts unten **Mit lokaler KI einordnen …**. Der Eintrag und seine inhaltlich verwandten Nachbarn sind markiert und bekommen zuerst einen gemeinsamen Vorschlag. **Allen Markierten zuordnen** ordnet sie zusammen ein. Danach kannst du jeden Eintrag einzeln prüfen.
- **Ein Thema im Brain:** Thema rechts in der Liste wählen, dann **Mit lokaler KI einordnen …**. Alle Einträge des Themas sind markiert.
- **Alles ohne Kategorie:** **Ohne Kategorie einordnen …** in der Brain-Werkzeugleiste oder **Mehr → Alle ohne Kategorie einordnen …**. Die Einträge kommen nacheinander, neueste zuerst.
- **Suche:** Treffer markieren, dann **Markierte einordnen …**.
- **Gesamtübersicht:** Einträge links auswählen (Strg oder Umschalt für mehrere), dann **Mehr → Auswahl einordnen …**. Bildschirmaufnahmen erscheinen je Bildwechsel.

Oben wählst du unter **Vorschläge von**, wer vorschlägt: **E5** (schnell, wählt unter vorhandenen Kategorien und lernt aus jeder Zuordnung), **Granite** (langsamer, kann auch neue Kategorien vorschlagen) oder **Aus** (nur manuell). Auch bei Granite prüft E5 zuerst: Ist E5 mit deinen eigenen Beispielen eindeutig, kommt sein Vorschlag sofort und Granite wird übersprungen. **Neu vorschlagen** (bzw. ein zweiter Klick auf den gemeinsamen Vorschlag) fragt Granite trotzdem. Die Auswahl gilt nur für dieses Fenster. Mit **Markierte mit Granite analysieren …** lässt du die markierten Einträge gruppieren und von Granite benennen, auch um schon zugeordnete Einträge neu einzuordnen. Im Fenster zeigt rechts unten der Vorschlag mit Begründung. **Enter** oder **Zuordnen und weiter** speichert die Kategorie sofort und springt zum nächsten offenen Eintrag. **Überspringen** lässt ihn unverändert. **Nur ohne Kategorie** springt nur zu Einträgen ohne Kategorie. Mit E5 zählt jede Bestätigung gleich als Beispiel für die nächsten Vorschläge. Die ersten zwei, drei Kategorien vergibst du am besten von Hand; danach werden die Vorschläge besser.
Unter dem Vorschlag stehen die **besten Kandidaten** als Knöpfe, auch wenn E5 sich nicht festlegt. Ein Klick übernimmt die Kategorie ins Feld. Die Begründung nennt das ähnlichste deiner eigenen Beispiele. Breite Kategorien mit vielen unterschiedlichen Beispielen kann E5 über bis zu drei Schwerpunkte vergleichen. Das passiert automatisch und nur, wenn es bei deinen Zuordnungen nachweislich besser trifft.

### Haupt- und Unterkategorien selbst pflegen

Jeder Eintrag (Sprachnotiz, Bildschirmaufnahme, Zwischenablage) kann eine **Hauptkategorie** und optional eine **Unterkategorie** dieser Hauptkategorie haben, zum Beispiel „Reisen › Strand“.

- **Kategorie festlegen:** Einträge links in der Bibliothek auswählen (Strg oder Umschalt für mehrere), dann **Strg+Umschalt+K**, Rechtsklick → **Kategorie festlegen …** oder **Mehr → Kategorie festlegen …**. Hauptkategorie wählen oder neu eingeben, Unterkategorie optional. **Kategorie entfernen** nimmt sie wieder weg; Titel bleiben erhalten. Eine Bildschirmaufnahme bekommt die Kategorie samt allen Bildwechseln.
- **Anzeige und Filter:** Unter jedem Eintrag steht seine Kategorie. Unter dem Suchfeld filterst du nach *Ohne Kategorie*, einer Hauptkategorie (mit allen Unterkategorien) oder einer einzelnen Unterkategorie.
- **Kategorien verwalten:** **Mehr → Kategorien verwalten …** zeigt alle Haupt- und Unterkategorien mit Anzahl. Dort legst du Kategorien an, gibst ihnen eine kurze **Beschreibung** (was gehört dazu, was nicht), benennst sie um, verschiebst eine Unterkategorie zu einer anderen Hauptkategorie oder löschst sie. Umbenennen auf einen vorhandenen Namen führt beide zusammen. Löschen einer Unterkategorie behält die Hauptkategorie der Einträge.
- **Vorschläge:** E5 schlägt zuerst die Hauptkategorie vor und danach, nur unter deren Unterkategorien, die Unterkategorie. Dafür zählen nur Beispiele dieser Hauptkategorie. Gibt es dort erst eine Unterkategorie, wird sie nur als Knopf angeboten, nicht vorgeschlagen. Wählst du im Einordnungsfenster eine andere Hauptkategorie, schlägt E5 die Unterkategorie dafür neu vor. Beschreibungen fließen in den Vergleich ein und helfen bei ähnlich klingenden Kategorien. Granite kennt die vorhandenen Unterkategorien und darf eine wählen.
- **Brain:** **Gruppieren nach → Kategorien** färbt das Netz nach Hauptkategorie, beschriftet Unterkategorien im Netz und listet rechts jede Hauptkategorie mit ihren Unterkategorien (Klick hebt die Einträge hervor). Einträge ohne Kategorie bleiben grau. Die Bezugsart **Gleiche Kategorie** zieht Einträge einer Kategorie zusammen und lässt sich im Filter ausblenden. **Themen** zeigt wieder die automatisch erkannten Themen.

### Wie Rocchio im Merkautomat eingesetzt wird

Rocchio ist das Standardverfahren hinter den **E5-Vorschlägen** für Kategorien und wird an drei weiteren Stellen genutzt:

- **Suche → Ähnliche zu Markierten:** Der Schwerpunkt der markierten Treffer wird mit dem Suchtext verbunden (Rocchio-Rückmeldung). Weitere passende Treffer markieren und erneut klicken schärft das Ergebnis Schritt für Schritt.
- **Mehr → Passende ohne Kategorie finden …:** Umgekehrte Richtung. Für die im Kategorienfilter gewählte Kategorie werden die Einträge ohne Kategorie gesucht, die näher an ihr liegen als an jeder anderen Kategorie. Dafür braucht es mindestens drei Kategorien mit je zwei Zuordnungen. Die Treffer öffnen sich im Einordnungsfenster mit der Kategorie vorbelegt. Gespeichert wird erst, wenn du zuordnest.
- **E5 vor Granite:** Ist Granite gewählt, prüft E5 zuerst mit deinen eigenen Beispielen. Ist E5 eindeutig, wird Granite nicht gestartet. Andernfalls bekommt Granite die drei besten E5-Kandidaten als unverbindlichen Hinweis.

Im Brain-Netz wird Rocchio nicht eingesetzt. Jede Kategorie bekommt einen Schwerpunkt aus ihrem Namen (plus Beschreibung) und den Vektoren deiner gespeicherten Zuordnungen; ein neuer Text bekommt die Kategorie mit dem ähnlichsten Schwerpunkt, aber nur bei deutlichem Abstand zur zweitbesten. Es wird nichts trainiert oder gespeichert, die Schwerpunkte werden bei jedem Vorschlag aus deinen aktuellen Zuordnungen gebildet. Die Auswertung vergleicht Rocchio mit k-NN, logistischer Regression und Rocchio-Varianten; unter *Automatisch* wird ein anderes Verfahren nur genommen, wenn es auf deinen Daten messbar besser ist. Für Unterkategorien gilt Rocchio innerhalb der jeweiligen Hauptkategorie.

### Einordnung auswerten

**Kontrollstation → Lokale KI → Auswertung öffnen …** oder im Ideenkombinat **Mehr → Einordnung auswerten …** öffnet die Auswertung. Sie prüft alle Verfahren gleich auf deinen gespeicherten Zuordnungen: Die Beispiele werden in fünf Teile geteilt, jeder Teil wird mit den übrigen vier eingeordnet, das Ganze dreimal mit anderer Aufteilung. Fast gleiche Kopien bleiben dabei zusammen, damit sie das Ergebnis nicht schönen.

- **Verfahren für Vorschläge:** *Automatisch* nimmt Rocchio (Name + Beispiele), außer ein anderes Verfahren ist messbar besser. Du kannst auch ein Verfahren fest einstellen, etwa um es im Alltag auszuprobieren.
- **Vergleich:** Balken mit dem Macro-F1-Wert je Verfahren (jede Kategorie zählt gleich, 1 ist perfekt) und eine Tabelle: wie oft ein Vorschlag kam, wie viele davon stimmten, ob die richtige Kategorie unter den drei besten war, wie viel sich mit höchstens 10 % Fehlern zuordnen ließe und wie lange ein Vorschlag dauert.
- **Weitere Spalten:** *Top-1* (bester Kandidat richtig, auch ohne Vorschlag), das *95-%-Intervall* für „davon richtig“ (bei wenigen Beispielen breit), *Unbekanntes abgewiesen* (ab vier Kategorien wird jeweils eine Kategorie ganz weggelassen; gezählt wird, wie oft ihre Texte korrekt keinen Vorschlag bekommen) und *Schwelle kalibriert* (ab 40 Beispielen bestimmt Merkautomat aus deinen Daten, ab welchem Abstand ein Vorschlag kommt, sodass mindestens 90 % stimmen; diese Schwelle wird dann für Vorschläge verwendet).
- **Rocchio mit Gegenbeispielen:** Varianten, die die ähnlichsten Beispiele anderer Kategorien abziehen. Sie helfen bei leicht verwechselbaren Kategorien, können aber auch schaden; die Auswertung entscheidet.
- **Rocchio klassisch und robust:** Vergleichsvarianten. *Klassisch* misst den euklidischen Abstand zum Schwerpunkt wie im Lehrbuch, *robust* lässt die untypischsten Beispiele einer Kategorie weg. Sie zeigen, ob die Standardvariante gut gewählt ist.
- **Zuordnungen zur Prüfung:** Einträge, die Rocchio in jeder Wiederholung sicher einer anderen Kategorie zuordnet als gespeichert. Oft ist die gespeicherte Kategorie falsch. **Diese Einträge einordnen …** öffnet sie im Einordnungsfenster.
- **Kategorien und Verwechslungen:** Je Kategorie Precision, Recall und F1 sowie eine Matrix, welche Kategorie mit welcher verwechselt wird.
- **Praxis:** Bei jedem Speichern wird gezählt, ob der Vorschlag angenommen, korrigiert oder keiner gemacht wurde. So siehst du, wie gut ein Verfahren (auch Granite) im Alltag trifft.
- **Verlauf:** Jede Auswertung wird gespeichert; so siehst du, ob mehr Zuordnungen helfen.
- **Bericht kopieren:** alles als Markdown-Tabelle in die Zwischenablage.

Bei wenigen Beispielen schwanken die Werte stark. Die Ähnlichkeitswerte sind keine Wahrscheinlichkeiten; nur die logistische Regression gibt Wahrscheinlichkeiten aus, deren Verlässlichkeit die Spalte ECE zeigt (0 wäre perfekt).

**Granite** kann zusätzlich neue Kategorien und Thementitel formulieren. Nach der Auswahl einmal **Modell herunterladen** anklicken (ca. 2,27 GB). Modell und Laufzeit stammen von Hugging Face beziehungsweise GitHub und werden vor Verwendung per SHA256 geprüft. **Abbrechen** stoppt den Download; **Download prüfen / reparieren** prüft vorhandene Dateien. Es werden keine Konten oder API-Schlüssel benötigt.

Für Granite sind mindestens 8 GB RAM empfohlen; der erste Aufruf lädt das Modell. Auf dem Entwicklungsrechner benötigten kurze Beispiele etwa 17–26 Sekunden beim ersten Aufruf und 9–17 Sekunden danach. Die Dauer hängt von Rechner und Text ab. Nach drei Minuten Leerlauf wird der Granite-Modellspeicher freigegeben; **Aus** beendet den Granite-Prozess. Das gemeinsam genutzte E5-Suchmodell bleibt davon unabhängig.

Im Brain bei **Thema zuordnen** oder **Zusammenfassen** entsteht bei aktivierter und installierter KI automatisch ein Vorschlag. **Kategorie vorschlagen** berechnet ihn erneut. **KI-Vorschlag übernehmen** setzt die Kategorie; bei Granite auch den Titel. Erst das anschließende Speichern übernimmt die Zuordnung. Eigene Titel, Kategorien und Originaltexte werden nicht ungefragt überschrieben. Nach einer Änderung der Quellenauswahl muss der Vorschlag neu erstellt werden.

Die Auswertung verwendet maximal acht unterschiedliche Quellen mit jeweils den ersten 700 Zeichen. Bei großen oder gemischten Auswahlen deshalb passende Quellen gezielt markieren und den Vorschlag prüfen. Die KI kategorisiert nicht automatisch den gesamten Bestand. Die Zusammenfassungsfunktion bleibt die lokale Auswahl von Originalaussagen.

Nach dem Download bleibt die Verarbeitung vollständig auf dem Rechner. Dateien liegen unter `%APPDATA%\Clipwell\models\granite-4.2-3b`. Granite verwendet Apache 2.0, llama.cpp MIT; es fallen keine Lizenzgebühren an. Die Lizenztexte werden im Ordner `Licenses` neben der Anwendung mitgeliefert.

### Datenschutz

| Option | Beschreibung |
|--------|-------------|
| Überwachung pausieren | Kein Aufzeichnen neuer Einträge |
| Passwortmanager (immer aktiv) | Inhalte, die die Quell-App als privat markiert (Formate `ExcludeClipboardContentFromMonitorProcessing`, `Clipboard Viewer Ignore` oder `CanIncludeInClipboardHistory` = 0, z. B. KeePass, Bitwarden, 1Password), werden nie gespeichert; sie landen damit auch nicht in Suche, Brain oder Export |
| URL-Vorschau | Automatischen Abruf von Titel und Favicon aktivieren/deaktivieren |
| Kopierte Dateien mit Inhalt speichern | Textinhalt kopierter Dateien übernehmen; Liste der lesbaren Dateitypen (Endungen durch Komma, "Standardliste" stellt sie wieder her) und Höchstgröße je Kopie in KB (Standard 1024) |

### Auto-Backup

| Option | Beschreibung |
|--------|-------------|
| Auto-Backup aktivieren | Tägliches JSON-Backup beim Start |
| Backup-Verzeichnis | Zielordner für automatische Backups |

Merkautomat behält die letzten 7 automatischen Backups. Ältere werden gelöscht.

### Shortcuts (Popup-Tastenkürzel)

Vier Aktionen im Popup können mit eigenen Tastenkombinationen belegt werden:

| Aktion | Standard |
|--------|---------|
| Eintrag pinnen | Ctrl+P |
| Details öffnen | F2 |
| Schnellnotiz | Ctrl+N |
| Pinstation | Ctrl+B |

Jeder Shortcut kann mit beliebiger Kombination aus Ctrl, Alt, Shift, Win und einem Buchstaben (A–Z) oder F1–F12 belegt werden.

**Hotkeys für Einträge:** Liste aller Einträge mit eigenem globalem Hotkey (Kombination, Eintrag, Zustand: aktiv, belegt durch ein anderes Programm oder Eintrag gelöscht; ggf. "als reiner Text"). Der Schalter "Eintrags-Hotkeys aktiv" schaltet alle auf einmal ab und wieder an, ohne die Zuweisungen zu verlieren. "Ändern …" öffnet den Aufnahmedialog, "Entfernen" löscht eine Zuweisung, "Alle entfernen" nach Rückfrage alle. Zugewiesen wird per Rechtsklick im Popup (siehe Kapitel 7). Wird ein Eintrag gelöscht, bleibt seine Zuweisung als "Eintrag gelöscht" sichtbar, bis sie entfernt wird; der Hotkey ist dann nicht mehr belegt.

### Outlook

Richtet ein kleines Add-in für das **klassische Outlook** (Microsoft 365, Desktop) ein. Es ergänzt das Kontextmenü einer Mail um **Aufgabe in Merkautomat erstellen** (siehe Aufgaben in Kapitel 13).

1. **Add-in einrichten** klicken. Eingerichtet wird nur für deinen Windows-Benutzer, ohne Adminrechte; die Datei wird nach `%LOCALAPPDATA%\Clipwell\outlook` kopiert.
2. Outlook beenden und neu starten.
3. Rechtsklick auf eine Mail (oder bis zu 20 markierte) → **Aufgabe in Merkautomat erstellen**. Läuft Merkautomat nicht, startet ihn das Add-in.

Der Status zeigt, ob das Add-in eingerichtet ist. Hat Outlook es nach einem Fehler oder einem langsamen Start abgeschaltet, steht dort "von Outlook abgeschaltet"; **Wieder aktivieren** und Outlook neu starten. Nach einem Merkautomat-Update **Erneut einrichten** bei geschlossenem Outlook. **Add-in entfernen** löscht die Einträge wieder; angelegte Aufgaben bleiben.

Das Add-in liest nur die Mails, die du übergibst, und nur Betreff, Absendername, Empfangszeit und Text, keine Anhänge. Die Übergabe läuft über eine Datei in `%APPDATA%\Clipwell\inbox`, die Merkautomat sofort wieder löscht; nichts verlässt den Rechner. Das neue Outlook unterstützt solche Add-ins nicht. Erlaubt eine Firmenrichtlinie nur freigegebene Add-ins, lädt Outlook es nicht; dann erscheint es in Outlook unter Datei → Optionen → Add-Ins als inaktiv.

### Über

Zeigt Version und Build sowie einen Link zum Quellcode auf GitHub.

---

## 16. Export, Import und Backup

### JSON-Export

Popup → Bulk-Modus → Exportieren, oder Kontrollstation → Daten.

Exportiert die History als lesbares JSON-Format inklusive Bilder, Thumbnails, Pin-Reihenfolge und Nutzungsmetadaten.

### JSON-Import

Kontrollstation → Daten → Importieren. Doppelte Einträge (gleicher Timestamp und Inhalt) werden beim Import übersprungen.

### SQLite-Backup

Kontrollstation → Daten → SQLite-Kopie exportieren. Erstellt eine vollständige Kopie der `history.db` — ideal als Backup oder für die Übertragung auf einen anderen PC.

### Sicheres Löschen

Rechtsklick auf einen Eintrag → "Sicher löschen" entfernt den Eintrag und führt VACUUM aus, um den Speicherplatz physisch freizugeben. Damit werden alle Spuren des Inhalts aus der Datenbankdatei entfernt.

### Gesamte History löschen

Kontrollstation → Daten → "History löschen". Optional können gepinnte Einträge beibehalten werden.

---

## 17. Datenschutz und Privatsphäre

- **Lokal only:** Alle Daten bleiben auf deinem Gerät. Merkautomat sendet keine Daten ins Internet, außer für URL-Vorschauen. Modell-Downloads (Diktat, semantische Suche) holen nur Dateien und senden keine Inhalte.
- **URL-Vorschauen:** Nur aktiv, wenn in der Kontrollstation aktiviert. Merkautomat ruft Titel und Favicon der kopierten URL ab. Private, Loopback- und Link-Local-Adressen (127.x, 10.x, 192.168.x, 172.16–31.x, 169.254.x, fc00::/7) werden nie abgerufen.
- **Überwachung pausieren:** Über Tray-Menü jederzeit möglich. Merkautomat zeichnet dann nichts auf.
- **URL-Cache:** Abgerufene Titel und Favicons werden 7 Tage in der Datenbank gecacht. Der Cache kann in der Kontrollstation geleert werden.
- **Desktop-Aufnahme:** Läuft nur nach ausdrücklichem Start und bleibt lokal. Sie speichert sichtbaren Text als unverschlüsseltes Markdown, auch vertrauliche Inhalte wie Chats oder Kundendaten. Vertrauliches nur bei pausierter Überwachung öffnen oder die Aufnahme danach im Ideenkombinat löschen.
- **Diktat:** Spracherkennung läuft lokal (Whisper). Audio wird nur im Arbeitsspeicher gehalten und nach der Transkription verworfen. Das Base-Modell wird mitgeliefert; Netzwerkzugriff gibt es nur, wenn ein größeres Modell gewählt und dessen Download bestätigt wird.
- **Semantische Suche:** Läuft lokal mit dem mitgelieferten Modell, ohne Netzwerkzugriff. Nur wenn das Modell fehlt, kann es per Klick auf "Modell laden" geholt werden. Der Index `semantic.db` enthält nur Vektoren und Prüfsummen, keinen Text; aus Vektoren lassen sich Inhalte aber grob ableiten. Vektoren gelöschter Zwischenablage-Einträge werden beim nächsten Indexabgleich oder App-Start entfernt, "Index löschen" entfernt alle sofort.
- **Fragen in der Suche:** Frage und bis zu sechs Trefferauszüge gehen nur an das lokale Granite auf 127.0.0.1, nichts verlässt den Rechner. Antworten werden nicht gespeichert.
- **Aufgaben:** Eine Aufgabe aus einem Eintrag speichert dessen Text (bis 4000 Zeichen) in `tasks.json`, auch wenn der Eintrag später gelöscht wird. Wer das nicht will, löscht die Aufgabe mit.

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
| Ins Ideenkombinat exportierte Notizen | `%APPDATA%\Clipwell\kb\notes\*.md` |
| Whisper-Sprachmodell Base (mitgeliefert) | `models\ggml-base.bin` neben `Merkautomat.exe` |
| Whisper-Sprachmodelle Small/Medium | `%APPDATA%\Clipwell\models\` |
| Desktop-Aufnahmen | `%APPDATA%\Clipwell\recordings\*.md` |
| Modell der semantischen Suche (mitgeliefert) | `models\e5-small\` neben `Merkautomat.exe`; Ersatzkopie nur bei Bedarf in `%APPDATA%\Clipwell\models\e5-small\` |
| Index der semantischen Suche (nur Vektoren) | `%APPDATA%\Clipwell\semantic.db` |
| Aufgaben (mit Kopie des Quelltexts) | `%APPDATA%\Clipwell\tasks.json` |
| Übergabe aus Outlook (wird sofort geleert) | `%APPDATA%\Clipwell\inbox\` |
| Outlook-Add-in (eingerichtete Kopie) | `%LOCALAPPDATA%\Clipwell\outlook\MerkautomatOutlook.dll` |
| WAL-Datei (SQLite) | `%APPDATA%\Clipwell\history.db-wal` |
| Wiederherstellungs-Backup | `%APPDATA%\Clipwell\history.db.broken-YYYYMMDD-HHMMSS` |
| Auto-Backups | Konfigurierbarer Ordner, `clipwell-autobackup-YYYYMMDD-HHmmss.json` |

---

## 19. Fehlerbehebung

### Popup öffnet sich nicht

1. Prüfe, ob Merkautomat als Tray-Icon sichtbar ist.
2. Öffne die Kontrollstation und prüfe, ob der Hotkey registriert wurde (grüner Status).
3. Falls der Hotkey blockiert ist: andere Anwendung verwendet dieselbe Kombination. Hotkey in der Kontrollstation ändern.
4. Prüfe das Fehlerprotokoll unter `%APPDATA%\Clipwell\clipwell.log`.

### Clipboard wird nicht aufgezeichnet

1. Prüfe, ob "Überwachung pausieren" im Tray-Menü aktiv ist.
2. Starte Merkautomat neu (Tray → Beenden, dann `Merkautomat.exe` erneut starten).

### Datenbank-Fehler beim Start

Merkautomat erkennt beschädigte Datenbanken über `quick_check` und bietet an, die Datei zu sichern und mit einer neuen, leeren History zu starten. Das kaputte File wird als `history.db.broken-...` archiviert.

### Kein Autostart

Prüfe, ob Merkautomat Schreibzugriff auf den Registry-Schlüssel hat:
`HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`

### URL-Vorschau fehlt

- Prüfe Internetverbindung.
- Prüfe, ob URL-Vorschau in der Kontrollstation aktiviert ist.
- Private/lokale Adressen werden grundsätzlich nicht abgerufen.
- Rechtsklick → "URL-Vorschau neu laden" erzwingt einen neuen Abruf.

### Bekannte Einschränkungen

- Die Desktop-Aufnahme sieht nur Text, den Anwendungen über UI Automation bereitstellen; Bilder und Videos werden nicht beschrieben. Fenster mit ständig wechselndem Text (Uhren, Logs) erzeugen viele kleine Einträge.
- Der "Weichzeichnen"-Effekt im Bild-Editor zeigt eine graue Überlagerung, keinen echten Gauß-Blur.
- Hotkey-Einschränkung: Nur Buchstaben A–Z und Ziffern 0–9 werden als Tastenanteil unterstützt. Funktionstasten können im Hotkey-Recorder nicht aufgenommen werden, sind aber als Popup-Shortcuts möglich.

### Themen bearbeiten, kategorisieren und zusammenfassen

Im Brain ein Thema in der rechten Liste oder einen Hauptpunkt im Netz waehlen. Unten rechts oeffnen **Thema bearbeiten …** und **Zusammenfassen …** denselben Dialog mit der jeweils passenden Aktion. Bei einem Thema werden dessen Eintraege angeboten, bei einem Hauptpunkt dessen direkte Verbindungen. Inhaltliche Verbindungen sind vorgewaehlt; rein zeitliche oder Webseiten-Bezuege lassen sich bei Bedarf markieren. Bereits gespeicherte Zusammenfassungen sind nicht als Quelle vorgewaehlt.

- Themenname und Kategorie eingeben oder eine vorhandene Kategorie waehlen. Im Reiter **Thema zuordnen**: **Zuordnung speichern** merkt sich die Angaben fuer die markierten Eintraege, unabhaengig von wechselnden Cluster-Nummern. Kategorien stehen in der Themenliste, manuelle Namen auch im Netz.
- Im Reiter **Zusammenfassen**: **Entwurf aus Auswahl erstellen** waehlt lokal bis zu zwoelf Originalaussagen aus, entfernt Wiederholungen und fuegt Quellenverweise hinzu. Es entsteht keine frei formulierte KI-Zusammenfassung. Der Text rechts ist editierbar; keine automatische Fakten- oder Widerspruchserkennung.
- Im Auswahlfeld rechts **Neue Zusammenfassung anlegen** oder **Aktualisieren: ...** waehlen. Eine vorhandene Fassung wird geladen, nicht automatisch durch einen neuen Entwurf ersetzt. Nach geaenderter Quellenauswahl muss der Entwurf neu erstellt werden.
- **Zusammenfassung speichern** bzw. **Zusammenfassung aktualisieren** speichert eine Sprachnotiz im Notizordner und die Fassung im Ideenkombinat. Aktualisieren nutzt dieselbe Notiz-ID; die vorherige Fassung bleibt jeweils als `.bak`. Die ausgewaehlten Ausgangseintraege werden nicht geloescht oder ueberschrieben.
- Suche und Brain aktualisieren sich anschliessend im Hintergrund. Der Bezugsfilter **Zusammenfassung** zeigt die expliziten Verbindungen zu den Quellen, soweit diese im aktuellen Netz enthalten sind. Quellenfilter und die Grenze von 1000 Knoten gelten weiterhin.

Die Quellenliste zeigt die Anzahl markierter Eintraege und laesst sich nach Titel oder Inhalt filtern. **Sichtbare markieren** betrifft nur die gefilterten Treffer, **Auswahl aufheben** alle Quellen. Ein Klick zeigt die Quellenvorschau. **Vorschau** rendert den Entwurf als Markdown; der Text bleibt bearbeitbar. Beim Wechsel des Speicherziels bleiben bearbeitete Entwuerfe samt Auswahl innerhalb des Dialogs erhalten. Vor dem Ersetzen eines bearbeiteten Entwurfs wird nachgefragt.

Die Berechnung nutzt lokale Textauswahl und den vorhandenen Index. Ein zusaetzliches Sprachmodell oder eine Netzwerkverbindung ist dafuer nicht erforderlich.

### Markdown-Ansicht und Text

In Gesamtuebersicht und Artikeln schaltet oben rechts **Markdown / Text** zwischen formatierter Vorschau und unveraendertem Markdown-Quelltext um. Markdown ist standardmaessig aktiv. Die Vorschau zeigt Ueberschriften, Listen, Hervorhebungen, Codebloecke, einfache Tabellen, Zitate und Weblinks. Bilder werden als beschriftete Platzhalter angezeigt. Kopieren und Exportieren verwenden in beiden Ansichten den Originalinhalt.

### Dubletten gezielt filtern

In Suche schaltet **Dubletten** den Dublettenfilter ein. Die orange Umrandung und die zusaetzliche Filterleiste zeigen den aktiven Zustand. Erneutes Klicken oder **Filter beenden** kehrt zur normalen Suche zurueck.

- **Dubletten-Gruppen:** zeigt nur Gruppen wiederholter Inhalte, mit der Zahl der zugehoerigen Eintraege. Rechts stehen die weiteren Fundstellen.
- **Alle Dubletten einzeln:** zeigt jede betroffene Kopie einzeln, damit sie separat angesehen und geoeffnet werden kann. Einzelstuecke werden ausgeblendet.
- **Je Gruppe ein Eintrag:** behaelt den neuesten Eintrag jeder Gruppe und alle Einzelstuecke in der Ansicht; die weiteren Kopien werden ausgeblendet.
- **Aehnlicher Inhalt:** vergleicht die Bedeutung mit einer einstellbaren Schwelle zwischen 90 und 100 Prozent. **Gleicher Text** vergleicht stattdessen den indizierten Text mit vereinheitlichten Leerzeichen/Zeilenumbruechen; Gross-/Kleinschreibung bleibt relevant. Links zaehlen ohne Tracking-Anhaenge (z. B. `utm_source`, `fbclid`) und unabhaengig von der Schreibweise der Domain als gleich.

Quellen-Chips und Eingabe im Suchfeld grenzen die Ergebnisse weiter ein. Texteingabe beendet den Filter nicht. Der Vergleich verwendet alle vorhandenen indizierten Abschnitte eines Eintrags, hoechstens die neuesten 6000 Eintraege der gewaehlten Quelle. Die Grenzen der Indizierung gelten weiterhin; der Filter ist keine Pruefung aller Originaldateien auf Bytegleichheit. Es wird nichts geloescht.
### Suchtreffer markieren und loeschen

Checkboxen links an den Suchtreffern markieren Inhalte zum Loeschen. Anklicken eines Treffers zeigt weiterhin seine Vorschau. **Alle markieren** umfasst die aktuell angezeigten Treffer; **Auswahl aufheben** entfernt die Markierungen. **Markierte loeschen** (oder Entf bei Fokus in der Trefferliste) zeigt vor der Loeschung eine Bestaetigung mit dem Umfang.

In **Dubletten-Gruppen** betrifft eine Checkbox die gesamte Gruppe inklusive des neuesten Eintrags. Um einzelne Kopien auszuwaehlen, vorher **Alle Dubletten einzeln** einstellen. Gruppen- und Schwellenwechsel heben Markierungen auf; beim Hintergrund-Abgleich bleiben sie nur bei unveraenderter Mitgliedschaft erhalten.

- Zwischenablage-Eintraege werden aus der History geloescht.
- Sprachnotizen werden samt Original, Ideenkombinat-Kopie und vorhandenen .bak-Fassungen entfernt, damit sie nicht erneut erscheinen.
- Bei Bildschirmaufnahmen wird die gesamte Datei mit allen Bildwechseln geloescht, auch wenn nur ein einzelner Treffer daraus markiert ist. Die Bestaetigung nennt dies ausdruecklich. Laufende Aufnahmen werden uebersprungen.

Nach der Loeschung aktualisieren sich die Treffer und das Brain ueber den lokalen Index-Abgleich. Fehler werden gemeldet; moeglicherweise geschuetzte Dateien bleiben erhalten.

### Kompakte Aktionsleisten

Oben stehen **Kopieren** und **Mehr**. Unter Mehr liegen Importieren, Exportieren, Aktualisieren (F5), Datenordner oeffnen und Loeschen. In der Suche ist dort zusaetzlich Suchindex zuruecksetzen erreichbar. Die bisherigen Tastenkuerzel bleiben erhalten; das Fenster schliesst ueber das normale Fenster-X.

Die Dubletteneinstellungen sind aufklappbar; ihre Zusammenfassung zeigt weiterhin Ansicht und Vergleich an. Der Link Alle markieren wechselt bei vollstaendiger Markierung zu Auswahl aufheben. Anzahl und Markierte loeschen erscheinen erst nach einer Markierung. In der Gruppenansicht loescht Markierte loeschen nur die aelteren Kopien einer Gruppe; der neueste Eintrag bleibt erhalten (Einordnen gilt fuer alle Gruppenmitglieder). Die Anzeige nennt die Zahl der aelteren Kopien.

### Wege und eigene Beziehungen im Brain

Oben **Weg finden …** oder **Beziehungen …** oeffnen. Ein zuvor gewaehlter Knoten wird als Start uebernommen. Unter **Weg finden** Start und Ziel waehlen und den kuerzesten Weg berechnen. Die Auswahlfelder erlauben die Eingabe eines Titels; in der Liste stehen Quelle, Datum und Kennung zur Unterscheidung. **Tauschen** vertauscht Start und Ziel. Das Ergebnis zeigt jeden Uebergang mit Grund und Eintragstitel. Die Anzahl bezeichnet Verbindungen, keine Dauer. Der Ergebnistext ist markierbar und kopierbar.

Standardmaessig gelten manuelle Beziehungen in Pfeilrichtung und Quellenverknuepfungen von Zusammenfassungen in beiden Richtungen. **Automatische Hinweise** schaltet die im Brain aktiven automatischen Bezugsarten zu; solche Wege sind keine belegte fachliche Schlussfolgerung. Gesucht wird im aktuellen Brain-Ausschnitt mit dessen Quellenfiltern und maximal 1000 Knoten.

Unter **Beziehungen bearbeiten** eine neue Beziehung anlegen oder links eine vorhandene waehlen: Von-Eintrag, Beziehungstyp, Ziel-Eintrag und Begruendung/Quellenstelle sind erforderlich. Eigene Typen sind moeglich. Aufgabe und Arbeitsschritte sind optionale Beschreibungen und werden nicht ausgefuehrt. Die Liste ist nach Titel, Typ oder Begruendung filterbar. **Zur Liste hinzufuegen** bzw. **Aenderung uebernehmen** aktualisiert den Dialogentwurf; Wege werden anhand dieser Liste berechnet. **Aenderungen speichern** uebernimmt auch die aktuelle Eingabe, sichert die Beziehungen dauerhaft und aktualisiert das Netz. Ohne Aenderungen ist Speichern deaktiviert. **Abbrechen** verwirft die Dialogaenderungen. Bereits gespeicherte Beziehungen mit Endpunkten ausserhalb des aktuellen Netzes bleiben bearbeitbar. Entfernen loescht nur die Beziehung, keine Inhalte. Violette Pfeile zeigen manuelle Beziehungen; bei Auswahl werden sie orange hervorgehoben.

Alle Berechnungen und Daten bleiben lokal. Es wird kein zusaetzliches Modell geladen und kein externer KI-Dienst verwendet.

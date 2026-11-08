# Merkautomat für Windows — Handbuch

Merkautomat merkt sich, was du kopierst, sprichst und auf dem Bildschirm liest, und macht daraus ein durchsuchbares Wissen auf deinem eigenen Rechner. Alles läuft lokal.

Das Handbuch hat zwei Teile:

- **Teil A — Für Nutzer:** Wie du Merkautomat im Alltag benutzt. Nach Aufgaben geordnet: "Ich will …".
- **Teil B — Für Admins:** Installation, Einstellungen, lokale KI, Outlook-Add-in, Datenschutz, Datenablage, Sicherung und Fehlerbehebung.

## Die Bereiche

| Name | Was es ist | So öffnest du es |
|------|-----------|------------------|
| **Merkautomat** | Die Anwendung im Tray mit dem Popup für deine Zwischenablage | Win+Shift+V oder Linksklick auf das Tray-Symbol |
| **Pinstation** | Immer sichtbares Fenster mit deinen angepinnten Einträgen | Tray → Pinstation, im Popup Strg+B |
| **Ideenkombinat** | Deine Wissensbasis: Sprachnotizen, Bildschirmaufnahmen, Zwischenablage, Aufgaben, Suche und Brain | Ideenkombinat-Symbol im Popup oder Tray → Ideenkombinat |
| **Sprechwerk** | Sprachnotizen mit lokalem Diktat, als Bereich im Ideenkombinat | Stift-Symbol im Popup oder Ideenkombinat → Sprechwerk |
| **Kontrollstation** | Alle Einstellungen | Tray → Kontrollstation |

---

## Inhalt

**Teil A — Für Nutzer**

1. [Erste Schritte](#1-erste-schritte)
2. [Kopiertes wiederfinden und einfügen](#2-kopiertes-wiederfinden-und-einfügen)
3. [Einträge ordnen: pinnen, löschen, Mehrfachauswahl](#3-einträge-ordnen-pinnen-löschen-mehrfachauswahl)
4. [Einträge ansehen und bearbeiten](#4-einträge-ansehen-und-bearbeiten)
5. [Pinstation und Farbpipette](#5-pinstation-und-farbpipette)
6. [Sprechwerk: Notizen schreiben und diktieren](#6-sprechwerk-notizen-schreiben-und-diktieren)
7. [Bildschirmaufnahmen](#7-bildschirmaufnahmen)
8. [Das Ideenkombinat](#8-das-ideenkombinat)
9. [Aufgaben](#9-aufgaben)
10. [Suche und Fragen](#10-suche-und-fragen)
11. [Brain: das Wissensnetz](#11-brain-das-wissensnetz)
12. [Kategorien vergeben](#12-kategorien-vergeben)
13. [Tastenkürzel auf einen Blick](#13-tastenkürzel-auf-einen-blick)
14. [Wenn etwas nicht klappt](#14-wenn-etwas-nicht-klappt)

**Teil B — Für Admins**

15. [Systemvoraussetzungen und Installation](#15-systemvoraussetzungen-und-installation)
16. [Kontrollstation: alle Einstellungen](#16-kontrollstation-alle-einstellungen)
17. [Lokale KI einrichten und betreiben](#17-lokale-ki-einrichten-und-betreiben)
18. [Outlook-Add-in](#18-outlook-add-in)
19. [Datenschutz und Netzwerkzugriffe](#19-datenschutz-und-netzwerkzugriffe)
20. [Sichern, Wiederherstellen, Löschen](#20-sichern-wiederherstellen-löschen)
21. [Datenpfade](#21-datenpfade)
22. [Fehlerbehebung](#22-fehlerbehebung)
23. [Bekannte Einschränkungen](#23-bekannte-einschränkungen)

---

# Teil A — Für Nutzer

## 1. Erste Schritte

**Merkautomat läuft im Hintergrund.** Nach dem Start siehst du rechts unten in der Taskleiste das MA-Symbol. Ab jetzt landet alles, was du kopierst, in deiner History.

**Beim ersten Start** zeigt dir ein kurzes Onboarding den Hotkey. Probier ihn gleich aus; danach erscheint das Onboarding nicht mehr.

**Die drei Handgriffe, die du ab sofort brauchst:**

1. **Win+Shift+V** öffnet das Popup am Mauszeiger.
2. Tippen sucht, **Pfeil hoch/runter** wählt aus.
3. **Enter** fügt den Eintrag dort ein, wo du vorher warst.

**Tray-Menü (Rechtsklick auf das Symbol):**

| Eintrag | Funktion |
|---------|----------|
| Öffnen | Popup öffnen |
| Pinstation | Pinstation ein- oder ausblenden |
| Desktop aufzeichnen | Bildschirmaufnahme starten oder beenden |
| Ideenkombinat | Wissensbasis öffnen |
| Kategorievorschläge prüfen (N) | Erscheint, wenn die Hintergrundanalyse Vorschläge vorbereitet hat |
| Kontrollstation | Einstellungen |
| Beenden | Merkautomat beenden |

Ein Linksklick auf das Tray-Symbol öffnet oder schließt das Popup.

---

## 2. Kopiertes wiederfinden und einfügen

### Das Popup

```
┌──────────────────────────────────────────────┐
│  Suchfeld                 [Aktionsleiste]    │
│──────────────────────────────────────────────│
│  [Alle] [Text] [Code] [MD] [URL] [Bild]      │
│  [Farbe] [Gepinnt]                [Neueste]  │
│──────────────────────────────────────────────│
│  ▼ Gepinnt                                   │
│  ▼ Heute                                     │
│  ▼ Gestern                                   │
│  ▼ Diese Woche                               │
│  ▼ Früher                                    │
└──────────────────────────────────────────────┘
```

- **Gruppen:** Gepinnt, Heute, Gestern, Diese Woche, Früher. Die Zeit passt zur Gruppe: "vor 6 Min.", "Gestern, 23:37", "Di, 14:05" oder ein Datum.
- **Bilder** heißen "Bild 1280×720"; darunter steht der erkannte Text oder "Kein Text erkannt".
- **Aktionsleiste** rechts oben: Kreis (Bildschirmaufnahme), Stift (Schnellnotiz), Pipette (Farbe vom Bildschirm), Ideenkombinat-Symbol, Liste (Mehrfachauswahl).
- **Größe und Position:** Oben ziehen verschiebt, an den Kanten ziehen vergrößert. Merkautomat merkt sich beides.
- **Schließen:** Esc oder ein Klick daneben. Suche, Filter und Mehrfachauswahl werden dabei zurückgesetzt.

### Einfügen

| So | Ergebnis |
|----|----------|
| **Enter** oder Doppelklick | Fügt ein. Inhalte aus dem Browser behalten ihre Formatierung (Tabellen, Listen, Links), wenn das Ziel sie annimmt, etwa Word oder Outlook. |
| **Strg+Enter** | Fügt als reinen Text ein |
| Kopieren-Knopf beim Überfahren oder Rechtsklick → Kopieren | Nur in die Zwischenablage, das Popup bleibt offen |
| Eintrag in ein anderes Fenster ziehen | Drag & Drop |

### Einen Eintrag auf eine Taste legen

Für Textbausteine, die du oft brauchst: Rechtsklick auf den Eintrag → **Hotkey zuweisen …**, ins Feld klicken und die Kombination drücken, z. B. Strg+Alt+1. Buchstaben und Ziffern brauchen Strg, Alt oder Win; F-Tasten gehen auch allein. Optional **Als reinen Text einfügen**.

Der Hotkey wirkt in jedem Programm, ohne das Popup zu öffnen. Der Eintrag wird dabei gepinnt, damit er nicht weggeräumt wird; die Kombination steht in der Liste neben der Zeit. Ist die Kombination schon belegt, sagt dir das der Dialog. Alle Eintrags-Hotkeys verwaltest du unter Kontrollstation → Tastenkürzel.

### Suchen und filtern

- **Tippen** durchsucht Inhalt, erkannten Bildtext, Seitentitel und Sprache, ohne auf Groß-/Kleinschreibung zu achten.
- **Filter-Chips:** Alle, Text, Code, MD (Markdown, erscheint auch unter Text), URL, Bild, Farbe, Gepinnt.
- **Sortier-Chip** rechts: Neueste, Älteste, Häufig genutzt, Selten genutzt. Gepinnte bleiben oben.
- **`.*`** rechts im Suchfeld schaltet auf reguläre Ausdrücke um, z. B. `^https?://`.
- **Letzte Suchen:** Die letzten fünf erscheinen, wenn das Suchfeld leer ist und den Fokus hat.
- **Suchpräfixe** (Vorschläge erscheinen beim Tippen):

| Präfix | Beispiel | Findet |
|--------|----------|--------|
| `type:` | `type:code` | Eintragstyp |
| `kind:` | `kind:md` | Inhaltsart (Label in der Liste) |
| `domain:` | `domain:github.com` | Links einer Webseite |
| `pinned:` | `pinned:true` | Gepinnte bzw. nicht gepinnte |

### Kopierte Dateien

Kopierst du im Explorer eine Textdatei (z. B. `.txt`, `.json`, `.ps1`, `.md`, `.csv`), speichert Merkautomat ihren Inhalt als Eintrag "Datei: name.txt". Im Detailfenster öffnet ein Klick auf die Quelle den Explorer an der Datei. Mehrere Dateien landen in einem Eintrag, getrennt durch "--- Datei: name ---". Binärdateien werden übersprungen. Welche Endungen gelesen werden, stellt dein Admin in der Kontrollstation ein.

### Links

Bei Link-Einträgen öffnen der Knopf beim Überfahren, das Kontextmenü oder das Detailfenster die Seite im Browser. Rechtsklick → **URL-Vorschau neu laden** holt Titel und Symbol neu.

---

## 3. Einträge ordnen: pinnen, löschen, Mehrfachauswahl

**Pinnen** hält einen Eintrag dauerhaft oben in "Gepinnt"; er wird beim automatischen Aufräumen nie gelöscht. Pin-Knopf beim Überfahren, Rechtsklick → Pinnen oder **Strg+P**.

**Löschen:** Papierkorb beim Überfahren, Rechtsklick → Löschen oder **Entf**.

**Sicher löschen** (Rechtsklick) entfernt den Eintrag zusätzlich restlos aus der Datenbankdatei. Das lässt sich nicht rückgängig machen.

**Typ ändern:** Rechtsklick → Typ ändern → **Als Text**, **Als Code** oder **Als Markdown behandeln**, wenn die automatische Erkennung danebenliegt.

**Zu Aufgaben hinzufügen:** Rechtsklick → **Zu Aufgaben hinzufügen** macht aus dem Eintrag eine Aufgabe (siehe [Kapitel 9](#9-aufgaben)).

**Mehrfachauswahl:** Das Listen-Symbol oben schaltet Kästchen vor jedem Eintrag ein.

| Aktion | Wirkung |
|--------|---------|
| Alle auswählen | Alle sichtbaren (gefilterten) Einträge |
| Pinnen / Lösen | Für alle ausgewählten |
| Kopieren | Texte verbunden, getrennt durch `---` |
| Exportieren | Als JSON; ohne Auswahl fragt Merkautomat nach der ganzen History |
| Löschen | Nach Rückfrage |
| Abbrechen | Mehrfachauswahl beenden |

---

## 4. Einträge ansehen und bearbeiten

Das **Detailfenster** öffnest du per Doppelklick auf den Eintrag im Ideenkombinat, über den Knopf beim Überfahren oder mit **F2**.

### Text, Code und Markdown

- Direkt bearbeiten, **Strg+S** speichert. Rückgängig mit Strg+Z, wiederholen mit Strg+Y.
- Zeilenumbruch umschaltbar, Zeichen- und Zeilenzähler unten.
- **Code:** Zeilennummern und Farbhervorhebung; die Sprache wählst du oben. Bei sehr großen Skripten (über 40.000 Zeichen oder 1.500 Zeilen) bleibt die Hervorhebung aus, damit alles flüssig bleibt.
- **Markdown** (z. B. Antworten aus einem Chat) öffnet formatiert. **Bearbeiten** zeigt den Quelltext, **Vorschau** wieder die formatierte Ansicht. Die Vorschau zeigt Überschriften, Listen, Codeblöcke, Tabellen, Zitate und Links; sie führt kein HTML aus und lädt keine Bilder.
- **Quelle:** Kam der Inhalt aus dem Browser, steht die Adresse als Link neben dem Titel. **Webinhalt** zeigt eine aufgeräumte Markdown-Fassung der Seite ohne Navigation und Werbung.

### Bilder

Zoom mit dem Mausrad, Verschieben per Ziehen. Werkzeuge: Auswahl, Pfeil, Linie, Rechteck, Ellipse, Stift, Text, Nummer, Hervorheben, Schwärzen, Pixelieren, Weichzeichnen, Zuschneiden. Dazu Drehen, Spiegeln, Größe ändern, Rand erweitern, als PNG oder JPEG speichern; alles mit Rückgängig. Bei Bildern mit Text schaltest du zwischen Bild und erkanntem Text um.

### Farben

Vorschau plus HEX, RGB, HSL, CSS-Name und Tailwind-Klasse, jeder Wert einzeln kopierbar.

---

## 5. Pinstation und Farbpipette

**Pinstation:** Ein Fenster, das immer im Vordergrund bleibt und alle gepinnten Einträge zeigt. Tray → Pinstation oder Strg+B im Popup. Einträge per Klick oder Tastatur einfügen, per Ziehen umsortieren; eine Linie zeigt, wo der Eintrag landet. Die Reihenfolge gilt auch für "Gepinnt" im Popup. Dateien, die du hineinziehst, werden wie kopierte Dateien gelesen.

**Farbpipette:** Pipette im Popup anklicken, dann irgendwo auf den Bildschirm klicken. Der Farbwert landet als HEX in der Zwischenablage und als Farbeintrag in der History.

---

## 6. Sprechwerk: Notizen schreiben und diktieren

Sprachnotizen sind eigene Markdown-Dateien, getrennt von der Zwischenablage. Sie landen sofort im Ideenkombinat und sind dort durchsuchbar.

### Schnellnotiz im Popup

Stift-Symbol oder **Strg+N** öffnet ein Eingabefeld unten im Popup.

- **Enter** oder **Speichern** legt die Notiz an. **Shift+Enter** macht eine neue Zeile.
- **Mikrofon** startet das Diktat, das Symbol wird zum roten Stopp-Quadrat. Erneut klicken beendet; der Text landet an der Cursorposition.
- **Fenster-Symbol** öffnet die Notiz im Ideenkombinat und nimmt den Text mit.
- **Esc** schließt ohne Speichern und bricht ein Diktat ab.

### Im Ideenkombinat

Links den Bereich **Sprechwerk** wählen; unten steht **Neue Sprachnotiz** bereit. **Diktieren** oder tippen, **Speichern** oder **Strg+Enter** legt die Notiz an und wählt sie aus.

### Gut zu wissen

- Das Diktat läuft komplett auf deinem Rechner. Die Aufnahme wird nicht gespeichert.
- Sprache und Genauigkeit stellst du unter Kontrollstation → Allgemein → "Diktat (Whisper, lokal)" ein.
- Du kannst diktieren und tippen im Wechsel. Es läuft immer nur ein Diktat.
- Geht das Mikrofon nicht: Windows-Einstellungen → Datenschutz → Mikrofon → Zugriff für Desktop-Apps erlauben.

---

## 7. Bildschirmaufnahmen

Eine Bildschirmaufnahme schreibt mit, was im aktiven Fenster an **Text** zu sehen ist, und legt es als Markdown ab. Es werden keine Bilder des Bildschirms gemacht.

**Starten und beenden:** Kreis im Popup, Tray → Desktop aufzeichnen oder im Ideenkombinat Bereich Bildschirmaufnahmen → **Aufnahme starten**. Während der Aufnahme ist der Knopf rot.

**Was mitgeschrieben wird:** Fenstertitel, Anwendung, sichtbarer Text, Beschriftungen, Links, Listen, Eingabewerte. Zeilen und Absätze werden sauber zusammengesetzt, Tabellen (z. B. im Browser) werden Markdown-Tabellen.

**Im Browser** schreibt Merkautomat standardmäßig nur die Adresse und den eigentlichen Seiteninhalt, ohne Tabs, Menüs und Fußzeilen.

**Wann ein Eintrag entsteht:** bei jedem Fenster- oder Tabwechsel, der eine halbe Sekunde stehen bleibt. Bleibst du im selben Fenster, kommen nur neu sichtbare Zeilen dazu. Kurzes Durchklicken erzeugt nichts.

```markdown
## 14:32:08 - Google Chrome: Kundenportal

### Sichtbarer Inhalt

- Kunde: Muster GmbH
- Vertragsstatus: Aktiv

### Änderung

Fensterwechsel von "Explorer: Downloads".
```

**Nicht mitgeschrieben werden:** Merkautomats eigene Fenster, Passwortfelder, hängende Fenster und Inhalte ohne Text (Bilder, Videos, Spiele, manche Editoren). Ist die Überwachung pausiert, entsteht gar nichts.

> **Achtung:** Die Aufnahme speichert auch Vertrauliches, das gerade sichtbar ist, etwa Chats oder Kundendaten. Solche Fenster erst nach dem Beenden öffnen oder die Aufnahme danach im Ideenkombinat löschen.

---

## 8. Das Ideenkombinat

Das Ideenkombinat ist deine Wissensbasis. Hier kommen Sprachnotizen, Bildschirmaufnahmen und die ganze Zwischenablage zusammen.

### Aufbau

**Links oben** die Navigation:

| Abschnitt | Kürzel | Wofür |
|-----------|--------|-------|
| **Dashboard** | | Zähler, Instrumente zur laufenden Aufnahme, Herkunft nach Anwendung und Webseite |
| **Gesamtübersicht** | | Alles zusammengefasst als Markdown |
| **Aufgaben** | Strg+T | Deine Aufgaben, die Zahl daneben zeigt offene ([Kapitel 9](#9-aufgaben)) |
| **Suche** | Strg+K oder Strg+F | Suche nach Bedeutung und Fragen ([Kapitel 10](#10-suche-und-fragen)) |
| **Brain** | Strg+B | Wissensnetz ([Kapitel 11](#11-brain-das-wissensnetz)) |

**Darunter** der Kategorienfilter (Alle, Ohne Kategorie, eine Haupt- oder Unterkategorie) und drei aufklappbare Bereiche:

- **Sprechwerk:** alle Sprachnotizen, neueste zuerst.
- **Bildschirmaufnahmen:** benannt nach den erfassten Anwendungen, mit Startzeit, Einträgen und Dauer.
- **Zwischenablage:** die ganze History. Doppelklick öffnet das Detailfenster.

Gesucht wird oben über **Suche**; ein eigenes Suchfeld in der Bibliothek gibt es nicht.

**Rechts** steht der gewählte Artikel. **Markdown / Text** schaltet zwischen formatierter Ansicht und Quelltext um. Kopieren und Export nehmen immer den Quelltext.

### Auswählen

Klick wählt, **Strg+Klick** und **Umschalt+Klick** erweitern die Auswahl, auch über Bereiche hinweg. Das Kästchen vor einem Artikel geht ohne Tastatur. "Alle auswählen" im Bereichskopf nimmt den ganzen Bereich, **Alle**/**Keine** unten gelten für alles. Bei mehreren Artikeln zeigt rechts eine gemeinsame Vorschau.

### Oben rechts

**Kopieren** kopiert das angezeigte Markdown (es landet nicht in der History). **Mehr …** enthält:

| Aktion | Kürzel | Wirkung |
|--------|--------|---------|
| Importieren … | Strg+O | `.md`- und `.txt`-Dateien als Sprachnotizen übernehmen; geht auch per Ziehen ins Fenster. Ohne eigene Überschrift wird der Dateiname zur Überschrift. Bis 2 MB je Datei. |
| Exportieren … | Strg+E | Auswahl (sonst alles Angezeigte) als ein Markdown-Dokument oder als ZIP |
| Kategorie festlegen … und weitere Kategorie-Aktionen | Strg+Umschalt+K | Siehe [Kapitel 12](#12-kategorien-vergeben) |
| Aktualisieren | F5 | Neu einlesen |
| Datenordner öffnen | | Öffnet den Ordner mit deinen Daten |

**Export im Detail:**
- **Zusammengefasstes Markdown:** eine Datei mit Inhaltsverzeichnis.
- **Alles als ZIP:** jeder Artikel als eigene Datei in `sprechwerk`, `bildschirmaufnahmen`, `zwischenablage`. Gibt es Kategorien, ordnet das ZIP zuerst danach: `Hauptkategorie/Unterkategorie/…`, der Rest unter `ohne kategorie/…`.
- Artikel mit Kategorie bekommen unter der Überschrift die Zeile "Kategorie: Haupt › Unter".
- Optional mit Gesamtübersicht.

**Löschen** (Entf) wirkt auf alle ausgewählten Artikel: Bildschirmaufnahmen als Datei (nie die laufende), Sprachnotizen in ihrer Fassung im Ideenkombinat, Zwischenablage-Einträge aus der History.

---

## 9. Aufgaben

**Öffnen:** Ideenkombinat → **Aufgaben** oder Strg+T.

**Aufgabe anlegen:**
- **Aus einem Eintrag:** Im Popup Rechtsklick → **Zu Aufgaben hinzufügen**. Der Titel ist die erste Zeile und gleich zum Umbenennen markiert. Der Text des Eintrags bleibt bei der Aufgabe, auch wenn der Eintrag später gelöscht wird.
- **Aus Outlook:** Rechtsklick auf eine oder mehrere Mails → **Aufgabe(n) in Merkautomat erstellen**. Titel ist der Betreff ohne "AW:"/"WG:", darunter stehen Absender, Zeit und Mailtext. Die Mail bleibt in Outlook. (Einrichtung: [Kapitel 18](#18-outlook-add-in).)
- **Eigene:** oben eintippen, Enter.

**Bearbeiten:** Rechts Titel, Fälligkeit und Notizen. Ein Klick ins Feld **Fällig am** öffnet einen Kalender mit **Heute**, **Morgen**, **Nächster Montag** und **Kein Datum**; Tippen geht auch (TT.MM.JJJJ oder TT.MM.). Alles wird automatisch gespeichert. Die Quelle (Eintrag oder Mail) steht darunter, Links darin sind anklickbar; den Trenner zwischen Notizen und Quelle kannst du ziehen.

**Erledigen:** Kästchen vor der Aufgabe oder **Erledigt**. **Heute erledigt** hakt nach Rückfrage alle heute fälligen ab; überfällige von früher bleiben offen.

**Überblick behalten:**
- Chips **Offen**, **Erledigt**, **Alle**. Überfällige sind rot.
- Der **Kalender** über der Liste zeigt Punkte an Tagen mit Aufgaben: orange offen, rot überfällig, grau erledigt. Klick auf einen Tag oder eine Kalenderwoche filtert, nochmal klicken hebt den Filter auf. Neue Aufgaben bekommen dann dieses Datum.
- **Kopieren** liefert die angezeigten Aufgaben als Markdown-Checkliste.

**Löschen** (Knopf oben oder Entf) entfernt nur die Aufgabe, nie den Eintrag oder die Mail.

---

## 10. Suche und Fragen

**Öffnen:** Ideenkombinat → **Suche**, Strg+K oder Strg+F.

Die Suche findet Inhalte **nach ihrer Bedeutung**, nicht nur nach gleichen Wörtern, auch über Sprachen hinweg. "Wie habe ich das VPN-Zertifikat erneuert" findet auch "OpenVPN-Cert im Firmenportal". Durchsucht werden Zwischenablage, Sprachnotizen und Bildschirmaufnahmen.

### Suchen

- Frage oder Stichworte eintippen; Treffer kommen nach kurzer Pause, **Enter** sofort.
- **Quellen-Chips** schränken auf Zwischenablage, Sprachnotizen oder Bildschirmaufnahmen ein.
- Jeder Treffer zeigt Quelle, Datum, Ähnlichkeit und "Worttreffer", wenn ein Suchwort wörtlich vorkommt. Fast gleiche Treffer werden zusammengefasst ("+3 fast gleich").
- Ohne Suchtext listet die Suche alles, neueste zuerst.
- Beim ersten Öffnen baut Merkautomat den Suchindex auf. Das dauert bei großer History einige Minuten; Treffer kommen schon währenddessen.

| Knopf | Wirkung |
|-------|---------|
| **Ähnliche** | Einträge, die dem gewählten Treffer inhaltlich ähneln |
| **Ähnliche zu Markierten** | Einträge, die allen markierten zusammen ähneln. Weitere markieren und erneut klicken macht das Ergebnis genauer. |
| **Öffnen** / Doppelklick | Detailfenster bzw. Artikel im Ideenkombinat, Aufnahmen an der passenden Stelle |
| **Markierte einordnen …** | Kategorie für die markierten Treffer vergeben |

### Fragen

Frage eintippen, **Fragen** klicken oder **Strg+Enter**. Merkautomat nimmt die sechs besten Treffer und lässt das lokale Sprachmodell Granite daraus antworten, mit Belegen wie [2]. Die zitierten Quellen stehen als Knöpfe darunter; ein Klick zeigt sie.

- Steht die Antwort nicht in deinen Einträgen, sagt Merkautomat das ("Keine belegte Antwort").
- Die erste Antwort dauert länger, danach meist einige Sekunden bis eine halbe Minute.
- **Abbrechen** stoppt, **Kopieren** kopiert Frage, Antwort und Quellen. Antworten werden nicht gespeichert.
- Im Zweifel die zitierte Quelle lesen; auch eine belegte Antwort kann falsch zusammengefasst sein.
- Fehlt Granite, bietet Merkautomat den Download an.

### Dubletten finden

**Dubletten** schaltet den Dublettenfilter ein (orange umrandet). Ansichten:

- **Dubletten-Gruppen:** nur wiederholte Inhalte, mit Anzahl.
- **Alle Dubletten einzeln:** jede Kopie einzeln.
- **Je Gruppe ein Eintrag:** der neueste je Gruppe plus alle Einzelstücke.

Vergleich: **Ähnlicher Inhalt** (Schwelle 90 bis 100 %) oder **Gleicher Text** (Leerzeichen und Zeilenumbrüche egal, Links ohne Tracking-Anhänge wie `utm_source`). Der Filter selbst löscht nichts. **Filter beenden** kehrt zur normalen Suche zurück.

### Markieren und löschen

Kästchen an den Treffern markieren, **Alle markieren** nimmt alle angezeigten. **Markierte löschen** (oder Entf in der Trefferliste) zeigt vorher genau, was wegfällt; Standard ist "Nein".

- **Zwischenablage:** aus der History.
- **Sprachnotiz:** mit Original, Fassung im Ideenkombinat und Sicherungskopien.
- **Bildschirmaufnahme:** die **ganze Datei**, auch wenn nur ein Bildwechsel markiert ist. Die laufende Aufnahme bleibt.
- **In der Gruppenansicht** werden nur die älteren Kopien gelöscht; der neueste Eintrag jeder Gruppe bleibt.

---

## 11. Brain: das Wissensnetz

**Öffnen:** Ideenkombinat → **Brain** oder Strg+B.

Das Brain zeigt deine neuesten 1000 Einträge als Netz. Jeder Eintrag ist ein Punkt, Bezüge sind Linien. Merkautomat findet die Bezüge selbst:

| Bezug | Bedeutung | Linie |
|-------|-----------|-------|
| Inhaltlich | ähnliche Bedeutung | Farbe des Themas |
| Zeitlich | innerhalb von 5 Minuten entstanden | blau |
| Gleiche Aufnahme | aufeinanderfolgende Bildwechsel | hellgrau |
| Gleiche Webseite | gleiche Domain | grün |
| Manuell | von dir angelegte Beziehung, mit Pfeil | violett |
| Zusammenfassung | gespeicherte Zusammenfassung zu ihren Quellen | orange |
| Gleiche Kategorie | nur bei "Gruppieren nach Kategorien" | türkis |

**Formen:** Kreis = Zwischenablage, Raute = Sprachnotiz, Quadrat = Bildwechsel. Je mehr Verbindungen, desto größer.

**Themen:** Eng verknüpfte Einträge bilden ein Thema mit Farbe und Stichwort-Namen, z. B. "Zertifikat · Vpn · Portal". **Gruppieren nach → Kategorien** färbt stattdessen nach deinen Kategorien.

### Bedienung

| Aktion | Wirkung |
|--------|---------|
| Mausrad | Zoomen |
| Ziehen auf freier Fläche | Ansicht verschieben |
| Punkt ziehen | Punkt verschieben |
| Klick auf Punkt | Rechts Details und alle Verbindungen mit Begründung, z. B. "inhaltlich 91 % · zeitlich nah (2 min)" |
| Klick daneben oder Esc | Zurück zur Themenliste |
| Doppelklick / Öffnen | Artikel im Ideenkombinat |
| Ähnliche | In der Suche ähnliche Einträge zeigen |
| Pos1 / Netz einpassen | Ganzes Netz zeigen |
| Neu anordnen | Layout neu berechnen |

**Werkzeugleiste:**
- **Suchfeld** (Strg+F): Wörter heben Punkte hervor; **Nach Bedeutung** oder Enter sucht semantisch.
- **Quellen** und **Bezüge** blenden Arten aus.
- **Ähnlichkeit ab** (80 bis 95 %, Standard 86 %): niedriger gibt mehr Verbindungen und größere Themen.
- **Fokus** hebt nur die nähere Umgebung des gewählten Punkts hervor.
- **Bibliothek einblenden** zeigt die Listen links wieder.

Merkautomat merkt sich diese Einstellungen. **Kopieren** liefert den gewählten Eintrag mit Verbindungen bzw. alle Themen als Markdown.

### Thema benennen und zusammenfassen

Thema rechts oder einen Punkt wählen, dann **Thema bearbeiten …** oder **Zusammenfassen …**.

- **Thema zuordnen:** Name und Kategorie für die markierten Einträge festlegen, **Zuordnung speichern**.
- **Zusammenfassen:** **Entwurf aus Auswahl erstellen** sucht bis zu zwölf Kernaussagen aus deinen Einträgen heraus, entfernt Wiederholungen und setzt Quellenverweise. Es wird nichts frei erfunden. Den Entwurf kannst du bearbeiten und als Markdown ansehen.
- **Zusammenfassung speichern** legt eine Sprachnotiz an; **aktualisieren** überschreibt sie und hält die alte Fassung als Sicherung. Die Ausgangseinträge bleiben unverändert.
- Mit eingeschalteter lokaler KI schlägt Merkautomat Titel und Kategorie vor; **KI-Vorschlag übernehmen** und danach Speichern übernimmt ihn.

### Wege und eigene Beziehungen

- **Weg finden …:** Start und Ziel wählen, Merkautomat zeigt den kürzesten Weg mit dem Grund jedes Schritts. Standardmäßig über deine eigenen Beziehungen und Zusammenfassungen; **Automatische Hinweise** nimmt die automatischen Bezüge dazu.
- **Beziehungen …:** Eigene Beziehung anlegen: Von, Typ (frei wählbar, z. B. "widerspricht"), Nach und Begründung. Aufgabe und Arbeitsschritte sind optional. **Änderungen speichern** übernimmt alles, **Abbrechen** verwirft. Entfernen löscht nur die Beziehung, nie Inhalte.

---

## 12. Kategorien vergeben

Jeder Eintrag kann eine **Hauptkategorie** und optional eine **Unterkategorie** haben, z. B. "Reisen › Strand". Kategorien helfen beim Filtern, Exportieren und im Brain.

### Von Hand

- **Kategorie festlegen:** Einträge in der Bibliothek auswählen, dann **Strg+Umschalt+K**, Rechtsklick oder **Mehr → Kategorie festlegen …**. **Kategorie entfernen** nimmt sie weg. Bei einer Bildschirmaufnahme gilt sie für alle Bildwechsel.
- **Kategorien verwalten** (Mehr): anlegen, kurz beschreiben (was gehört dazu, was nicht), umbenennen, verschieben, löschen. Umbenennen auf einen vorhandenen Namen führt beide zusammen.

> **Tipp:** Vergib die ersten zwei, drei Kategorien von Hand, mit verständlichen Namen wie "Reisen" statt Kürzeln. Danach werden die Vorschläge deutlich besser.

### Mit Vorschlägen: das Einordnungsfenster

Einstiege:
- **Brain:** Punkt oder Thema wählen → **Mit lokaler KI einordnen …**
- **Alles ohne Kategorie:** Brain-Werkzeugleiste oder **Mehr → Alle ohne Kategorie einordnen …**
- **Suche:** Treffer markieren → **Markierte einordnen …**
- **Bibliothek:** Einträge wählen → **Mehr → Auswahl einordnen …**

Oben wählst du unter **Vorschläge von**:
- **E5:** schnell, wählt nur unter deinen vorhandenen Kategorien und lernt aus jeder Zuordnung.
- **Granite:** langsamer, kann auch neue Kategorien vorschlagen.
- **Aus:** nur von Hand.

Bedienung:
- **Enter** oder **Zuordnen und weiter** speichert sofort und springt zum nächsten Eintrag. **Überspringen** lässt ihn, wie er ist.
- Die **besten Kandidaten** stehen als Knöpfe unter dem Vorschlag, ein Klick übernimmt.
- **Allen Markierten zuordnen** gibt mehreren Einträgen dieselbe Kategorie.
- **Neu vorschlagen** fragt Granite ausdrücklich.
- **Nur ohne Kategorie** springt nur zu offenen Einträgen.

### Kategorien vorschlagen lassen (Granite)

Hast du noch keine Kategorien oder viele Einträge ohne: **Mehr → Kategorien analysieren (Granite) …** oder im Brain **Kategorien analysieren …**.

1. **Analyse starten:** Merkautomat bildet Gruppen ähnlicher Einträge, Granite schlägt je Gruppe einen Namen vor (etwa 10 bis 20 Sekunden je Gruppe).
2. Vorschläge prüfen: Namen ändern, Unpassendes abwählen.
3. **Ausgewählte übernehmen** speichert nach Rückfrage. Vorhandene Kategorien werden nie überschrieben.

Meist musst du nicht warten: Merkautomat bereitet die Analyse in Arbeitspausen vor. Sind Vorschläge fertig, meldet sich das Tray-Symbol mit **Kategorievorschläge bereit**.

### Passende Einträge zu einer Kategorie finden

Kategorie im Kategorienfilter wählen, dann **Mehr → Passende ohne Kategorie finden …**. Merkautomat sucht Einträge ohne Kategorie, die gut dazu passen, und öffnet sie im Einordnungsfenster. Gespeichert wird erst, wenn du zuordnest. Voraussetzung: mindestens drei Kategorien mit je zwei Einträgen.

### Wie gut treffen die Vorschläge?

**Mehr → Einordnung auswerten …** zeigt, wie gut die Verfahren auf deinen Zuordnungen treffen, und listet **Zuordnungen zur Prüfung**: Einträge, die vermutlich falsch eingeordnet sind. **Diese Einträge einordnen …** öffnet sie direkt. Details für Admins: [Kapitel 17](#17-lokale-ki-einrichten-und-betreiben).

---

## 13. Tastenkürzel auf einen Blick

**Überall**

| Taste | Wirkung |
|-------|---------|
| Win+Shift+V | Popup öffnen (änderbar) |
| Eigene Eintrags-Hotkeys | Eintrag direkt einfügen |

**Popup**

| Taste | Wirkung |
|-------|---------|
| Pfeil hoch/runter | Auswahl bewegen |
| Enter | Einfügen |
| Strg+Enter | Als reinen Text einfügen |
| Tab | Zu den Filter-Chips |
| Entf | Löschen |
| Strg+P | Pinnen |
| F2 | Detailfenster |
| Strg+N | Schnellnotiz |
| Strg+B | Pinstation |
| Esc | Schließen |

**Ideenkombinat**

| Taste | Wirkung |
|-------|---------|
| Strg+T | Aufgaben |
| Strg+K | Suche |
| Strg+F | Suche, im Brain die Netzsuche |
| Strg+B | Brain |
| Strg+Enter | In der Suche: Fragen; bei Sprachnotizen: Speichern |
| Strg+Umschalt+K | Kategorie festlegen |
| Strg+E | Exportieren |
| Strg+O | Importieren |
| F5 | Aktualisieren |
| Entf | Löschen |
| Esc | Eingabe leeren bzw. zurück |

**Detailfenster:** Strg+S speichern, Strg+Z/Strg+Y rückgängig/wiederholen.

---

## 14. Wenn etwas nicht klappt

| Problem | Was du tun kannst |
|---------|-------------------|
| Popup geht nicht auf | Ist das MA-Symbol im Tray? Kontrollstation → Allgemein → Globaler Hotkey prüfen; ein anderes Programm nutzt vielleicht dieselbe Kombination. |
| Kopiertes erscheint nicht | Ist "Überwachung pausieren" in der Kontrollstation an? Inhalte aus Passwortmanagern werden absichtlich nie gespeichert. |
| Diktat bleibt stumm | Mikrofonzugriff für Desktop-Apps in Windows erlauben. |
| Suche findet einen Eintrag nicht | Sehr kurze Einträge ohne Leerzeichen (Passwörter, IDs), Farben und Bilder ohne Text werden nicht in die Bedeutungssuche aufgenommen. Im Popup findest du sie trotzdem. |
| Fragen/Analyse dauert lange | Granite rechnet auf dem Prozessor; der erste Aufruf lädt das Modell. |
| Outlook-Menüeintrag fehlt | Admin fragen bzw. [Kapitel 18](#18-outlook-add-in). |

Mehr in [Kapitel 22](#22-fehlerbehebung).

---

# Teil B — Für Admins

## 15. Systemvoraussetzungen und Installation

**Voraussetzungen**
- Windows 10 Version 2004 (Build 19041) oder neuer, 64 Bit.
- Normale Ausgabe: .NET 10 Windows Desktop Runtime. Portable Ausgabe: keine Runtime nötig.
- Für Granite (optional): mindestens 8 GB RAM empfohlen, etwa 2,3 GB Plattenplatz.
- Für das Diktat: Mikrofon und Mikrofonzugriff für Desktop-Apps.

**Installation:** Ordner kopieren und `Merkautomat.exe` starten. Es gibt keinen Installer und keine Adminrechte; alles wird für den angemeldeten Windows-Benutzer eingerichtet. Neben der EXE müssen liegen:

| Datei/Ordner | Inhalt |
|--------------|--------|
| `models\ggml-base.bin` | Whisper-Modell Base für das Diktat |
| `models\e5-small\` | Modell für Bedeutungssuche und Kategorievorschläge |
| `runtimes\win-x64\` | Laufzeitbibliotheken für Whisper |
| `onnxruntime*.dll` | Laufzeit für das Suchmodell |
| `outlook\MerkautomatOutlook.dll` | Outlook-Add-in (optional) |
| `Licenses\`, `THIRD-PARTY-NOTICES.txt` | Lizenztexte |

**Beim ersten Start** legt Merkautomat `%APPDATA%\Clipwell\` an und zeigt das Onboarding. Der Datenordner heißt aus Kompatibilitätsgründen weiter `Clipwell`.

**Autostart:** Kontrollstation → Allgemein → Windows-Autostart. Eintrag `Merkautomat` unter `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Ein alter Eintrag `Clipwell` wird dabei entfernt.

**Einzelinstanz:** Es läuft immer nur eine Instanz je Benutzer. Ein zweiter Start öffnet das Popup der laufenden Instanz.

**Update:** Merkautomat beenden (Tray → Beenden), Programmordner ersetzen, neu starten. Daten unter `%APPDATA%\Clipwell` bleiben. Bei eingerichtetem Outlook-Add-in danach bei geschlossenem Outlook **Erneut einrichten** ([Kapitel 18](#18-outlook-add-in)).

---

## 16. Kontrollstation: alle Einstellungen

Tray → Kontrollstation. Fünf Reiter: **Allgemein**, **Tastenkürzel**, **Lokale KI**, **Outlook**, **Über**.

### Allgemein

| Einstellung | Wirkung |
|-------------|---------|
| Globaler Hotkey | Kombination aufnehmen oder per Kästchen und Taste setzen; Aktion "Nur Menü öffnen" oder "Neuesten Eintrag sofort einfügen". Bei Konflikten zeigt Merkautomat den Windows-Fehlercode und weicht auf Win+Shift, Strg+Shift, Strg+Alt oder Win+Alt aus. |
| Farbschema | System, Dunkel, Hell |
| Erkennung | Code-Erkennung Konservativ, Normal oder Aggressiv |
| History-Limit | Höchstzahl Einträge (50 bis 5000, Standard 500); die ältesten nicht gepinnten fallen weg |
| Überwachung pausieren | Es wird nichts aufgezeichnet, auch keine Bildschirmaufnahme |
| Windows-Autostart | Beim Anmelden starten |
| URL-Vorschau laden | Titel und Symbol kopierter Links aus dem Netz holen. Verbindet direkt, nicht über einen Proxy; in Netzen, die nur über einen Proxy ins Internet kommen, bleibt die Vorschau leer. |
| Verschlüsselung und Notschlüssel | "Notschlüssel anzeigen …" zeigt den Notschlüssel zum Aufschreiben ([Kapitel 19](#19-datenschutz-und-netzwerkzugriffe)) |
| Kopierte Dateien mit Inhalt speichern | Schalter, Liste der Endungen (durch Komma, "Standardliste" setzt zurück), Höchstgröße je Kopie (Standard 1024 KB) |
| Desktop-Aufnahme: nur Seiteninhalt | Bei Browsern nur Adresse und Seiteninhalt; aus = alles Sichtbare |
| URL-Cache-Dauer | Wie lange Titel und Symbole zwischengespeichert werden (Standard 7 Tage); Cache leeren |
| Diktat (Whisper, lokal) | Modell Base (ca. 145 MB, mitgeliefert), Small (ca. 470 MB) oder Medium (ca. 1,5 GB), beide als Download nach Rückfrage; Sprache Deutsch, Englisch oder Automatisch. Größer ist genauer, aber langsamer. |
| Speicherlimits | Maximales Alter in Tagen, maximale Datenbankgröße in MB (0 = aus) |
| Sichern & Wiederherstellen | Export als JSON, SQLite, CSV oder Markdown, Import aus JSON, automatisches Backup ([Kapitel 20](#20-sichern-wiederherstellen-löschen)) |
| Zurücksetzen | Ungepinnte History löschen, Gesamte History löschen, URL-Cache löschen, Einstellungen zurücksetzen |

### Tastenkürzel

- **Popup-Aktionen:** Eintrag pinnen (Strg+P), Details öffnen (F2), Schnellnotiz (Strg+N), Pinstation (Strg+B). Belegbar mit Strg, Alt, Shift, Win plus A–Z oder F1–F12.
- **Hotkeys für Einträge:** Liste aller Eintrags-Hotkeys mit Zustand (aktiv, von einem anderen Programm belegt, Eintrag gelöscht). **Eintrags-Hotkeys aktiv** schaltet alle ab und an, ohne Zuweisungen zu verlieren. **Ändern …**, **Entfernen**, **Alle entfernen**.

### Lokale KI

Siehe [Kapitel 17](#17-lokale-ki-einrichten-und-betreiben).

### Outlook

Siehe [Kapitel 18](#18-outlook-add-in).

### Über

Kurzbeschreibung, Version und eine Bildergeschichte zur Entstehung (Klick auf das Titelbild oder **Zur Entstehung**; Blättern mit Pfeiltasten, Esc schließt).

---

## 17. Lokale KI einrichten und betreiben

Merkautomat bringt zwei lokale Modelle mit bzw. kann eines nachladen. Nichts davon braucht ein Konto, einen API-Schlüssel oder eine Internetverbindung im Betrieb.

| Modell | Wofür | Größe | Herkunft |
|--------|-------|-------|----------|
| **multilingual-e5-small** (MIT) | Bedeutungssuche, Brain, Kategorievorschläge (E5) | ca. 123 MB | mitgeliefert |
| **Granite 4.2 3B** (Apache 2.0) mit llama.cpp (MIT) | Fragen, Kategorien benennen, Titelvorschläge | ca. 2,27 GB | Download auf Klick |
| **Whisper Base/Small/Medium** (MIT) | Diktat | Base mitgeliefert | Small/Medium auf Klick |

### Auswahl im Reiter Lokale KI

- **Aus** (Standard): keine automatischen Kategorievorschläge.
- **E5 · bestehende Kategorien:** schnell, kein Download, wählt nur unter vorhandenen Kategorien. Braucht mindestens zwei von Hand angelegte Kategorien.
- **Granite 4.2 3B · lokal (CPU):** kann neue Kategorien und Titel formulieren. **Modell herunterladen** holt Modell und Laufzeit von gepinnten Adressen (Hugging Face, GitHub) und prüft Größe und SHA256. **Download prüfen / reparieren** prüft vorhandene Dateien.
- **Funktion testen** prüft die gewählte Variante mit einem Beispiel.

Fragen in der Suche und "Kategorien analysieren" nutzen Granite unabhängig von dieser Auswahl, wenn es installiert ist.

### Betrieb von Granite

- Läuft als versteckter Prozess nur auf 127.0.0.1 mit zufälligem Port und Schlüssel; kein Web-UI, kein Proxy, kein Werkzeugzugriff.
- Der Prozess endet mit Merkautomat, auch bei Absturz. Nach drei Minuten ohne Nutzung wird der Speicher freigegeben; **Aus** beendet ihn sofort.
- Direkte Aufrufe laufen mit leicht abgesenkter Priorität; ein Aufruf darf höchstens 3 Minuten dauern.
- Richtwerte: erster Aufruf 17 bis 26 Sekunden, danach 9 bis 17 Sekunden. Abhängig von Rechner und Text.

### Hintergrundarbeit

**Lokale KI → Hintergrundarbeit** steuert, ob Merkautomat die Kategorieanalyse in Pausen vorbereitet:

| Modus | Verhalten |
|-------|-----------|
| **Nur im Leerlauf** (Standard) | Start nach 5 Minuten ohne Eingabe; bei Rückkehr wird die laufende Gruppe sparsam beendet, dann Pause |
| **Auch während der Arbeit, sparsam** | Läuft nebenher mit niedrigster Priorität und Windows-Sparmodus (EcoQoS) |
| **Aus** | Keine Hintergrundarbeit |

- **Sofortige Pause** bei Akkubetrieb, Vollbildspiel und Präsentationsmodus. Bei Videos im Vollbild nur sparsam.
- Nutzt jemand die KI direkt, hat das Vorrang; die Hintergrundarbeit wartet danach drei Minuten.
- Start ab 10 Einträgen ohne Kategorie, Wiederholung nach 10 neuen. Zwischenstände bleiben erhalten (`background-discovery.json`).
- Optional **PC nicht in den Energiesparmodus wechseln lassen**, solange gerechnet wird.
- Voraussetzung: Granite und Suchmodell installiert.

### Wie die Vorschläge entstehen

- **E5** vergleicht einen Text mit Schwerpunkten je Kategorie (Rocchio): Name plus Beschreibung plus die gespeicherten Zuordnungen. Vorgeschlagen wird nur bei deutlichem Abstand zur zweitbesten Kategorie, sonst bleibt der Eintrag offen. Unterkategorien werden danach nur innerhalb der Hauptkategorie bestimmt.
- **Gelernt wird nur aus gespeicherten Zuordnungen.** Nicht gespeicherte Vorschläge und automatische Brain-Bezüge zählen nie. Es wird kein Modell trainiert oder abgelegt; die Schwerpunkte entstehen bei jedem Vorschlag neu.
- **E5 vor Granite:** Ist Granite gewählt, prüft E5 zuerst. Ist E5 mit eigenen Beispielen eindeutig, startet Granite gar nicht; sonst bekommt Granite die drei besten E5-Kandidaten als Hinweis.
- Die Ähnlichkeitswerte sind keine Wahrscheinlichkeiten.

### Einordnung auswerten

**Lokale KI → Auswertung öffnen …** (oder im Ideenkombinat Mehr → Einordnung auswerten …) vergleicht alle Verfahren auf den gespeicherten Zuordnungen: fünffache Kreuzvalidierung, dreimal wiederholt, fast gleiche Kopien bleiben zusammen.

- **Verfahren für Vorschläge:** *Automatisch* nimmt Rocchio, außer ein anderes Verfahren (k-NN, logistische Regression, Rocchio-Varianten) ist messbar besser. Ein Verfahren lässt sich auch fest einstellen.
- **Kennzahlen:** Macro-F1, Top-1, Top-3, beantwortet und davon richtig (mit 95-%-Intervall), Abdeckung bei höchstens 10 % Fehlern, Abweisung unbekannter Themen, Zeit je Vorschlag, ECE (nur logistische Regression).
- **Schwelle kalibriert:** Ab 40 Beispielen bestimmt Merkautomat die Vorschlagsschwelle so, dass mindestens 90 % stimmen, und nutzt sie.
- **Kategorien und Verwechslungen:** Precision, Recall, F1 und Verwechslungsmatrix.
- **Zuordnungen zur Prüfung**, **Praxis** (angenommen/korrigiert je Verfahren, auch Granite), **Verlauf**, **Bericht kopieren** (Markdown).

Bei wenigen Beispielen schwanken die Werte stark.

### Lizenzen

Granite Apache 2.0, llama.cpp, Whisper und e5-small MIT. Keine Lizenzgebühren. Lizenztexte liegen in `Licenses` neben der Anwendung.

---

## 18. Outlook-Add-in

Ergänzt im **klassischen Outlook für Microsoft 365 (64 Bit)** das Kontextmenü einer Mail um **Aufgabe(n) in Merkautomat erstellen**, direkt nach "Kopieren". Das neue Outlook unterstützt solche Add-ins nicht.

### Einrichten

1. Kontrollstation → Outlook → **Add-in einrichten**.
2. Outlook beenden und neu starten.
3. Rechtsklick auf eine Mail (bis zu 20 markierte) → **Aufgabe in Merkautomat erstellen**. Läuft Merkautomat nicht, startet das Add-in ihn.

Keine Adminrechte nötig. Die DLL wird nach `%LOCALAPPDATA%\Clipwell\outlook\` kopiert, damit Outlook nie den Programmordner sperrt. Geschrieben wird nur unter HKCU:

| Schlüssel | Inhalt |
|-----------|--------|
| `HKCU\Software\Classes\CLSID\{6E4B2D7A-...}` | COM-Registrierung der DLL |
| `HKCU\Software\Classes\Merkautomat.OutlookAddin` | ProgID |
| `HKCU\Software\Microsoft\Office\Outlook\Addins\Merkautomat.OutlookAddin` | `LoadBehavior` = 3, `MerkautomatPath` |

### Status und Pflege

- **"von Outlook abgeschaltet":** Outlook hat das Add-in nach einem Fehler oder langsamen Start deaktiviert. **Wieder aktivieren**, Outlook neu starten.
- **Nach einem Update:** bei geschlossenem Outlook **Erneut einrichten**.
- **Add-in entfernen** löscht die Registry-Einträge; angelegte Aufgaben bleiben.
- Merkautomat startet oder beendet Outlook nie selbst.

### Was übergeben wird

Nur die Mails, die der Nutzer auswählt, und davon nur Betreff, Absendername, Empfangszeit, Text (bis 20.000 Zeichen) und die Outlook-Kennung. Keine Anhänge. Die Übergabe läuft über eine Datei in `%APPDATA%\Clipwell\inbox\`, die Merkautomat sofort verarbeitet und löscht; unlesbare Dateien landen in `inbox\fehler`. Nichts verlässt den Rechner.

### Firmenumgebungen

Erlaubt eine Richtlinie nur freigegebene Add-ins, lädt Outlook dieses nicht; es steht dann unter Datei → Optionen → Add-Ins als inaktiv. In dem Fall muss die IT das Add-in freigeben.

---

## 19. Datenschutz und Netzwerkzugriffe

**Grundsatz:** Alle Inhalte bleiben auf dem Rechner. Es gibt keine Telemetrie und keinen Cloud-Dienst.

### Was nie gespeichert wird

- Inhalte, die die Quell-App als privat markiert (Passwortmanager wie KeePass, Bitwarden, 1Password; technisch die Formate `ExcludeClipboardContentFromMonitorProcessing`, `Clipboard Viewer Ignore` und `CanIncludeInClipboardHistory` = 0). Immer aktiv, nicht abschaltbar.
- Bei Bildschirmaufnahmen: Passwortfelder und Merkautomats eigene Fenster.
- Alles, solange die Überwachung pausiert ist.

### Wann Merkautomat ins Netz geht

| Anlass | Ziel | Was gesendet wird |
|--------|------|-------------------|
| URL-Vorschau (abschaltbar) | Die kopierte Webseite | Ein Abruf von Titel und Symbol, direkt ohne Proxy; nie Adressen im eigenen Netz (127.x, 10.x, 192.168.x, 172.16–31.x, 169.254.x, 100.64–127.x, fc00::/7 und weitere reservierte Bereiche), auch nicht über Weiterleitungen |
| Granite-Download (auf Klick) | Hugging Face, GitHub | Nur Dateiabruf |
| Whisper Small/Medium (auf Klick) | Hugging Face | Nur Dateiabruf |
| Suchmodell fehlt, "Modell laden" (auf Klick) | Hugging Face | Nur Dateiabruf |

Sonst gibt es keinen Netzwerkzugriff. Granite läuft ausschließlich auf 127.0.0.1.

### Verschlüsselung und Notschlüssel

History, Suchindex, Sprachnotizen, Bildschirmaufnahmen, Aufgaben, Einstellungen und Auto-Backups liegen **verschlüsselt** auf der Platte. Der Schlüssel ist an dein Windows-Konto gebunden. Du merkst davon im Alltag nichts. Die Dateien im Datenordner lassen sich aber nicht mehr in einem Editor öffnen.

Beim ersten Start mit Verschlüsselung zeigt Merkautomat einmal den **Notschlüssel** (acht Vierergruppen, z. B. `7K2Q-…`). Den brauchst du, wenn dein Windows-Konto die Daten nicht mehr öffnen kann: neuer PC, neues Windows-Profil oder ein vom Administrator zurückgesetztes Passwort. Merkautomat fragt dann beim Start danach. Auch ein Auto-Backup vom alten PC lässt sich damit importieren.

- Bewahre den Notschlüssel im **Passwortmanager oder auf Papier** auf, nicht als Datei auf demselben PC.
- Wer den Notschlüssel **und** ein Backup hat, kann die Daten lesen.
- Ohne Windows-Konto **und** ohne Notschlüssel sind die Daten verloren.
- Später anzeigen: Kontrollstation → Allgemein → **Notschlüssel anzeigen …**. "Kopieren" hält ihn aus Merkautomats History und dem Windows-Zwischenablageverlauf heraus.

Die Verschlüsselung schützt vor kopierten oder gestohlenen Dateien, Backups auf NAS oder USB-Stick, anderen Windows-Konten und einer ausgebauten Platte. Sie schützt **nicht** vor Schadsoftware, die unter deinem Konto läuft, und auf Firmen-PCs in einer Domäne nicht vor den Domänen-Admins. BitLocker zusätzlich eingeschaltet lassen.

**Unverschlüsselt** bleiben bewusst: manuelle Exporte (JSON, CSV, Markdown, SQLite-Kopie, Ideenkombinat-Export), das Fehlerprotokoll `clipwell.log` und die Modelle.

### Was wo gespeichert wird

- **History** in `history.db` (SQLite, verschlüsselt). "Sicher löschen" entfernt einen Eintrag auch aus den freien Bereichen der Datei.
- **Bildschirmaufnahmen** als verschlüsseltes Markdown, auch mit vertraulichen Inhalten, die zu sehen waren.
- **Diktat:** Audio nur im Arbeitsspeicher, nach der Erkennung verworfen.
- **Suchindex** `semantic.db`: nur Vektoren und Prüfsummen, kein Text. Aus Vektoren lassen sich Inhalte aber grob ableiten. "Index löschen" in der Suche entfernt alle.
- **Fragen:** Frage und Trefferauszüge gehen nur an das lokale Granite; Antworten werden nicht gespeichert.
- **Aufgaben** speichern eine Kopie des Quelltexts (bis 4000 Zeichen) in `tasks.json`, auch wenn der Eintrag später gelöscht wird.
- **URL-Cache:** Titel und Symbole, Dauer einstellbar, leerbar.

---

## 20. Sichern, Wiederherstellen, Löschen

| Aufgabe | Wo | Hinweis |
|---------|----|---------|
| History exportieren | Kontrollstation → Allgemein → Sichern & Wiederherstellen → Als JSON / CSV / Markdown exportieren, oder Popup → Mehrfachauswahl → Exportieren | Unverschlüsselt. JSON enthält Bilder, Pin-Reihenfolge und Nutzungsdaten und lässt sich wieder importieren |
| JSON importieren | Kontrollstation → Aus JSON importieren | Auch verschlüsselte Auto-Backups; stammt eins von einem anderen PC, fragt Merkautomat nach dessen Notschlüssel. Doppelte Einträge (gleiche Zeit und gleicher Inhalt) werden übersprungen |
| SQLite-Kopie | Kontrollstation → SQLite-Backup exportieren | Unverschlüsselte Kopie der `history.db` für andere Programme |
| Automatisches Backup | Kontrollstation → Automatisches tägliches Backup | Verschlüsseltes JSON beim Start in einen wählbaren Ordner, die letzten 7 bleiben |
| Ideenkombinat exportieren | Ideenkombinat → Mehr → Exportieren | Markdown oder ZIP, siehe [Kapitel 8](#8-das-ideenkombinat) |
| History löschen | Kontrollstation → Zurücksetzen → Ungepinnte bzw. Gesamte History löschen | "Ungepinnte" behält die gepinnten Einträge |
| Einzelnen Eintrag restlos löschen | Popup → Rechtsklick → Sicher löschen | Nicht rückgängig zu machen |

**Vollständige Sicherung:** Merkautomat beenden und den ganzen Ordner `%APPDATA%\Clipwell` kopieren. Er enthält History, Einstellungen, Notizen, Aufnahmen, Aufgaben, Kategorien und die Schlüsseldatei `datakey.json`, alles verschlüsselt. Auf einem anderen PC öffnet Merkautomat die Kopie nach Eingabe des Notschlüssels. Die Modelle unter `models\` lassen sich neu laden und müssen nicht gesichert werden.

---

## 21. Datenpfade

| Was | Pfad |
|-----|------|
| Einstellungen (inkl. Kategorien, Beziehungen, Eintrags-Hotkeys) | `%APPDATA%\Clipwell\settings.json` |
| History-Datenbank | `%APPDATA%\Clipwell\history.db` (plus `history.db-wal`) |
| Fehlerprotokoll | `%APPDATA%\Clipwell\clipwell.log` |
| Sprachnotizen | `%APPDATA%\Clipwell\notes\*.md` |
| Sprachnotizen im Ideenkombinat | `%APPDATA%\Clipwell\kb\notes\*.md` |
| Bildschirmaufnahmen | `%APPDATA%\Clipwell\recordings\*.md` |
| Aufgaben | `%APPDATA%\Clipwell\tasks.json` |
| Suchindex (nur Vektoren) | `%APPDATA%\Clipwell\semantic.db` |
| Zwischenstand der Hintergrundanalyse | `%APPDATA%\Clipwell\background-discovery.json` |
| Übergabe aus Outlook | `%APPDATA%\Clipwell\inbox\` (unlesbare in `inbox\fehler`) |
| Bildeditor-Stile | `%APPDATA%\Clipwell\editor-styles.json` |
| Onboarding-Marker | `%APPDATA%\Clipwell\onboarding.seen` |
| Nachgeladene Modelle (Whisper Small/Medium, Granite, Ersatz für e5-small) | `%APPDATA%\Clipwell\models\` |
| Mitgelieferte Modelle | `models\` neben `Merkautomat.exe` |
| Outlook-Add-in (eingerichtete Kopie) | `%LOCALAPPDATA%\Clipwell\outlook\MerkautomatOutlook.dll` |
| Auto-Backups | Wählbarer Ordner, `clipwell-autobackup-JJJJMMTT-hhmmss.json` |
| Gesicherte defekte Datenbank | `%APPDATA%\Clipwell\history.db.broken-JJJJMMTT-hhmmss` |
| Gesicherte defekte Aufgabendatei | `%APPDATA%\Clipwell\tasks.json.defekt-*` |
| Schlüsseldatei der Verschlüsselung | `%APPDATA%\Clipwell\datakey.json` (an das Windows-Konto gebunden; nicht löschen) |

---

## 22. Fehlerbehebung

Erste Anlaufstelle ist immer das Protokoll `%APPDATA%\Clipwell\clipwell.log`.

### Popup öffnet sich nicht

1. Läuft `Merkautomat.exe` (Tray-Symbol, Task-Manager)?
2. Kontrollstation → Allgemein → Globaler Hotkey: Ist er registriert? Bei Konflikt eine andere Kombination wählen. Beim Start meldet eine Tray-Sprechblase, wenn der Hotkey nicht registriert werden konnte.

### Zwischenablage wird nicht aufgezeichnet

1. Überwachung pausiert?
2. Kommt der Inhalt aus einem Passwortmanager oder einer App, die ihn als privat markiert? Dann ist das gewollt.
3. Merkautomat neu starten (Tray → Beenden, dann neu starten).

### Datenbank-Fehler beim Start

Merkautomat prüft die Datenbank beim Start. Ist sie beschädigt, bietet er an, sie als `history.db.broken-…` beiseitezulegen und mit einer leeren History zu starten. Danach lässt sich ein Backup importieren.

### Aufgaben sind weg

War `tasks.json` oder `settings.json` unlesbar, liegt sie als `*.defekt-*` daneben. Datei prüfen, reparieren und zurückbenennen, während Merkautomat beendet ist. Fragt Merkautomat beim Start nach dem Notschlüssel, kann das Windows-Konto den Schlüssel nicht öffnen (siehe [Kapitel 19](#19-datenschutz-und-netzwerkzugriffe)); Abbrechen ändert nichts an den Daten.

### Kein Autostart

Schreibrechte auf `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` prüfen; manche Richtlinien sperren diesen Schlüssel.

### URL-Vorschau fehlt

Internetverbindung, Schalter "URL-Vorschau laden", private Adresse? Rechtsklick → URL-Vorschau neu laden erzwingt einen neuen Abruf.

### Suche oder Brain leer

- Fehlt `models\e5-small\` neben der EXE, bietet die Suche "Modell laden" an.
- Der erste Indexaufbau dauert bei großer History einige Minuten.
- "Index löschen" in der Suche baut den Index neu auf.

### Granite startet nicht

- Kontrollstation → Lokale KI → **Download prüfen / reparieren**.
- Verwaiste `llama-server`-Prozesse aus dem Granite-Ordner beendet Merkautomat vor jedem Start selbst.
- Zu wenig Arbeitsspeicher: andere Anwendungen schließen.

### Outlook-Menüeintrag fehlt

1. Klassisches Outlook, 64 Bit? Das neue Outlook wird nicht unterstützt.
2. Kontrollstation → Outlook → Status. Bei "von Outlook abgeschaltet": **Wieder aktivieren**, Outlook neu starten.
3. Nach einem Update: Outlook schließen, **Erneut einrichten**.
4. Outlook → Datei → Optionen → Add-Ins: Steht "Merkautomat" unter inaktiv oder deaktiviert, ist eine Richtlinie oder ein Fehler die Ursache.

---

## 23. Bekannte Einschränkungen

- Bildschirmaufnahmen sehen nur Text, den Anwendungen über die Bedienungshilfen bereitstellen. Fenster mit ständig wechselndem Text (Uhren, Logs) erzeugen viele kleine Einträge.
- "Weichzeichnen" im Bildeditor ist eine graue Überlagerung, kein echter Weichzeichner.
- Der globale Popup-Hotkey nimmt als Taste nur A–Z und 0–9 an; F-Tasten gehen für Popup-Aktionen und Eintrags-Hotkeys.
- Das Brain zeigt höchstens die neuesten 1000 Einträge; die Dublettensuche betrachtet höchstens die neuesten 6000.
- Ein gefundener Weg im Brain ist keine Aussage über den Gesamtbestand, sondern gilt nur für den angezeigten Ausschnitt.
- Granite rechnet auf dem Prozessor; auf schwächeren Rechnern dauern Antworten deutlich länger.
- Das Outlook-Add-in funktioniert nur im klassischen Outlook.

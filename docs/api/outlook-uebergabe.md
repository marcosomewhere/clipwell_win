# Schnittstelle: Uebergabe vom Outlook-Add-in an Merkautomat

Die einzige Schnittstelle zwischen zwei Programmen. Das Add-in (`src/MerkautomatOutlook`, net48, laeuft in Outlook) und Merkautomat sprechen nicht direkt miteinander, sondern ueber Dateien in einem Ordner des eigenen Windows-Kontos. Beide Seiten werden zusammen getestet (`OutlookMailTests`; das Testprojekt bindet `MailHandover.cs` des Add-ins direkt ein).

## Ablauf

1. Rechtsklick in Outlook "Aufgabe(n) in Merkautomat erstellen": Das Add-in liest je Mail (hoechstens 20, nur Mails) die Felder unten.
2. Es schreibt je Mail eine Datei `mail-JJJJMMTT-HHMMSS-<guid>.tmp` nach `data\inbox` und benennt sie danach in `.json` um. Merkautomat sieht so nie eine halb geschriebene Datei.
3. Laeuft Merkautomat nicht, startet das Add-in die EXE aus `MerkautomatPath` im Registry-Schluessel des Add-ins (nur ein absoluter Pfad auf `Merkautomat.exe`).
4. Merkautomat (`MailInbox`, `App.MailInbox.cs`) beobachtet den Ordner, macht aus jeder Datei eine Aufgabe, loescht die Datei und oeffnet die Aufgaben. Unlesbare Dateien kommen nach `inbox\fehler` und werden nicht erneut versucht.

## Dateiformat

- Inhalt: die Bytes `MKD1`, danach das JSON als UTF-8, verschluesselt per DPAPI fuer den aktuellen Windows-Benutzer mit der Entropie `Merkautomat.Inbox.v1`.
- Merkautomat liest zusaetzlich unverschluesselte JSON-Dateien aelterer Add-in-Versionen.
- Hoechstens 512 KB je Datei (`MailInbox.MaxFileBytes`).

```json
{
  "kind": "outlook-mail",
  "subject": "Betreff",
  "sender": "Anzeigename des Absenders",
  "received": "2026-11-08T09:30:00",
  "entryId": "Outlook-EntryID",
  "storeId": "Outlook-StoreID oder leer",
  "body": "Text der Mail, hoechstens 20.000 Zeichen"
}
```

## Vertrauensgrenze

Jedes Programm unter demselben Windows-Konto kann Dateien in den Ordner legen. Merkautomat behandelt den Inhalt deshalb wie Benutzereingaben: Groessenlimit, Pruefung von `kind`, nur Text in Aufgaben, Links nur ueber `WebLink` (http(s)). Aenderungen am Format immer auf beiden Seiten und rueckwaertskompatibel, weil ein installiertes Add-in nicht automatisch mit aktualisiert wird.

## Datenordner

Der Add-in-Registry-Eintrag unter HKCU enthaelt `DataDirectory`. Fehlt der optionale Wert, wird der interne Datenordner aus `MerkautomatPath` mit demselben Resolver wie in der Hauptanwendung bestimmt. Bei fehlender/ungueltiger Konfiguration wird ein Hinweis auf erneute Einrichtung angezeigt; es gibt keinen stillen AppData-Rueckfall. `data\outlook` enthaelt die installierte DLL, getrennt von der mitgelieferten Kopie neben der EXE.

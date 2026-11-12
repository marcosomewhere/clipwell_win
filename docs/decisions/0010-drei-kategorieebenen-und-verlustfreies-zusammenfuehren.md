# 0010: Drei Kategorieebenen, einstellbare Prompts und verlustfreies Zusammenfuehren

Stand: gilt, 2026-11-12

## Kontext

Zwei Kategorieebenen reichten fuer eine fein gegliederte Wissensbasis nicht. Die Anweisungen an das Sprachmodell waren fest im Code und fuer den Nutzer nicht sichtbar. Doppelte und zusammengehoerige Eintraege machten die Wissensbasis unuebersichtlich, ohne dass Wissen verloren gehen darf.

## Entscheidung

- Hoechstens drei Ebenen: Haupt-, Unter- und Feinkategorie. Gespeichert als zusaetzliches optionales Feld, Definitionen eine Ebene tiefer; alte Einstellungen laden unveraendert. Ein Pfad bleibt intern ein String "Haupt › Unter › Fein".
- Jede Aufgabe des Sprachmodells hat einen sichtbaren, bearbeitbaren Prompt mit Einsatzort und mitgesendeten Daten. Das Antwortformat bleibt per JSON-Schema fest; Inhalte gehen als Daten mit, nie als Anweisung.
- Die KI darf im Leerlauf feinere Kategorien fuer gut gefuellte Kategorien vorschlagen. Gespeichert wird nur nach Bestaetigung im Pruefenster.
- Zusammenfuehren erzeugt immer eine neue Notiz mit dem vollstaendigen Text jeder Quelle; nur wortgleiche Texte erscheinen einmal. Originale werden nur auf Wunsch entfernt, nur wenn beide gespeicherten Fassungen der Notiz ihren vollstaendigen Text enthalten, und nie bei Bildern, formatierten Texten, angehefteten Eintraegen, Bildschirmaufnahmen oder Knowledgeordner-Dateien.

## Folgen

- Dialoge nehmen im zweiten Feld "Unter" oder "Unter › Fein"; tiefere Ebenen gibt es nicht.
- Ein schlecht formulierter eigener Prompt kann die Qualitaet senken, aber kein ungueltiges Ergebnis speichern; "Standard wiederherstellen" setzt zurueck.
- Zusammengefuehrte Notizen koennen laenger sein als die Summe der Einzelteile wirkt, weil Herkunftsangaben hinzukommen; Verschlanken heisst weniger Eintraege, nicht weniger Text.

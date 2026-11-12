# 0009: Mein Wissen mit eigenen Textkopien, Gewichtung und Verdichtung

Stand: gilt, 2026-11-11

## Kontext

Die Zwischenablage-History ist begrenzt (Anzahl, Alter, Groesse), Aufnahmen und Kopien wachsen schnell. Gewuenscht ist ein taeglich aktuelles, gewichtetes Wissen, das Wertvolles behaelt, Fluechtiges verdichtet und durchsuchbar bleibt. Der Knowledgeordner ist die bevorzugte, dauerhafte Quelle.

## Entscheidung

- "Mein Wissen" speichert je Eintrag eine eigene, verschluesselte Textkopie (`data\my-knowledge.json`). So ueberlebt Wissen das History-Limit; der Suchindex bleibt textfrei.
- Gewicht aus Quelle (Knowledgeordner vor Sprachnotizen vor Aufnahmen vor Zwischenablage), Alter (Zerfall nur fuer Zwischenablage und Aufnahmen) und Nutzersignalen (Kategorie, Mappe, dauerhaftes Behalten, erneutes Kopieren). Feste Konstanten im Code, einstellbar sind Aufbewahrung, Halbwertszeit der Zwischenablage und das Verdichten.
- Knowledgeordner: keine Fristen, kein Verdichten; die Eintraege folgen nur dem Ordner.
- Neues erscheint erst nach einer einstellbaren Wartezeit (Standard 48 Stunden, bis eine Woche) und zaehlt anfangs weniger (30 %, voll nach 14 Tagen), damit Frisches nicht vor Bewaehrtes rueckt.
- Was lange relevant bleibt, wird gefestigt; Eingeordnetes, dauerhaft Behaltenes und Mappen-Mitglieder bleiben. Alles andere wird nach der Frist verlustbehaftet zu Monatszusammenfassungen verdichtet: lokal und extraktiv (ausgewaehlte Originalaussagen und Titel), ohne Sprachmodell, damit der Lauf taeglich, schnell und nachvollziehbar bleibt. Ausgeschaltet werden faellige Eintraege entfernt. Manuelles Zusammenfassen folgt denselben Regeln.
- Lokale KI (E5, Granite/Qwen) schlaegt Kategorien und Titel vor, formuliert Zusammenfassungen und den Wochenrueckblick nur mit Quellenbeleg und beantwortet Fragen aus Mein Wissen. Sie speichert nie selbst Kategorien: Vorschlaege schuetzen nicht vor dem Verdichten, erst die Zustimmung des Nutzers zaehlt. Sprachmodell nur ueber die bestehende Hintergrundarbeit.
- Loeschen durch den Nutzer entfernt auch die Kopie und ihre Aussagen in Zusammenfassungen; Bereinigung durch Limits nicht. Merkautomat veraendert nie die Quellen.

## Folgen

- Zusaetzlicher Speicherbedarf (hoechstens 12.000 Zeichen je Eintrag, gleiche Texte nur einmal); Zusammenfassungen begrenzen das Wachstum alter Eintraege.
- Verdichtete Inhalte sind unwiederbringlich verloren, soweit die Quelle selbst nicht mehr existiert.
- Kopien und Zusammenfassungen, deren Quelle nicht mehr indiziert ist, sind nur ueber die Wortsuche in Mein Wissen auffindbar, nicht im Brain oder in der Bedeutungssuche.
- Neue Datei wurde in die Notschluessel-Erkennung aufgenommen; Format waechst nur (neue Felder optional).

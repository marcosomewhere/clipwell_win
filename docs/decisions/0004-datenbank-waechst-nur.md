# 0004: Datenbankschema waechst nur, keine Versionsnummer

Stand: gilt, 2026-11-08

## Kontext

Verschiedene Versionen von Merkautomat (Build-Ausgabe, portable Kopie, aeltere Sicherung) sollen dieselbe `history.db` oeffnen koennen. Eine Versionsnummer mit Sperre fuer neuere Datenbanken wurde erprobt und wieder verworfen: Sie haette eine aeltere EXE ausgesperrt, obwohl sie die Daten problemlos lesen kann.

## Entscheidung

- Das Schema waechst nur: neue Spalten mit Standardwert (`EnsureColumn`), neue Tabellen per `CREATE TABLE IF NOT EXISTS`.
- Nichts wird umbenannt, entfernt oder in der Bedeutung geaendert.
- Keine Versionsnummer, keine Sperre.

## Folgen

- Aeltere Programme ignorieren neue Spalten, neuere ergaenzen fehlende. Abgesichert durch `OlderDatabase_GetsMissingColumnsAndKeepsEntries` und `DatabaseWithColumnsOfANewerVersion_StillOpensAndWorks`.
- Eine Aenderung, die sich so nicht ausdruecken laesst, braucht eine neue Entscheidung.
- Dasselbe gilt sinngemaess fuer Dateiformate (`settings.json`, `tasks.json`, Markdown-Dateien): neue Felder optional, alte bleiben lesbar.

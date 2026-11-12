# 0008: Auch technische Produktnamen verwenden Merkautomat

Stand: gilt, 2026-11-08

## Kontext

Nach der sichtbaren Umbenennung enthielten Projekte, Namespaces und Laufzeitkennungen noch den frueheren Produktnamen. Eine pauschale Beibehaltung ist nicht erforderlich: diese Kennungen sind keine gespeicherten Inhaltsformate.

## Entscheidung

- Solution `Merkautomat.slnx`, App `src/Merkautomat/Merkautomat.csproj`, Tests `tests/Merkautomat.Tests/Merkautomat.Tests.csproj`, Namespaces `Merkautomat.*`. Das Outlook-Projekt behaelt seinen bereits passenden Namen.
- Aktive Mutex-/Eventnamen, Fensterkennung, Drag-Format, User-Agent, Testkennungen, neue Logs (`merkautomat.log`) und neue Sicherungen (`merkautomat-autobackup-*`) verwenden Merkautomat.
- Alte Namen bleiben nur an echten Uebergangsstellen: `%APPDATA%\Clipwell` als Quelle der vorhandenen Datenuebernahme, Entfernen des alten Autostartwerts, Stopwort fuer historische Inhalte und Mutex/Event-Bruecke zu Altversionen. Beide Programmversionen erkennen damit eine laufende andere Instanz und signalisieren das Oeffnen des Popups statt dieselben Daten parallel zu verwenden.
- Bestehende Logs und Sicherungen werden weder umbenannt noch geloescht. Rotation begrenzt die neuen Sicherungen auf sieben Dateien; alte Sicherungen bleiben manuell importierbar. Der neue Testschalter heisst `MERKAUTOMAT_E5_DOWNLOAD`.
- `data/`, Verschluesselungskennungen, Schluesselableitung, DPAPI-Entropie, Dateiinhalte und Datenbankschemata bleiben unveraendert. Entscheidung 0004 gilt weiter; der Entwicklungsresolver aus 0006 nutzt die neuen Projektpfade.

## Folgen

Build-, Start- und Testbefehle nutzen die neuen Pfade. Die Anwendung wird fuer den Umbau beendet und anschliessend aus dem neuen Ausgabepfad gestartet; Autostart und ein bereits eingerichtetes Outlook-Add-in erhalten dabei den aktuellen Programmpfad. Laufendes Outlook muss fuer die aktualisierte Add-in-DLL spaeter neu gestartet werden.

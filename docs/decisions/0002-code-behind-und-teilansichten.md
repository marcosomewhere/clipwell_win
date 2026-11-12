# 0002: Code-behind statt MVVM, grosse Fenster als UserControls

Stand: gilt, 2026-11-08

## Kontext

Die Oberflaeche ist ueberwiegend in Code-behind geschrieben. Ein vollstaendiger MVVM-Umbau waere gross und braechte fuer eine Einzelentwicklung wenig. Gleichzeitig sind einzelne Fenster sehr gross geworden (`DetailWindow` rund 3350 Zeilen, `KnowledgeBaseWindow` ueber 4000 Zeilen in Teildateien), und die Logik darin ist schwer zu testen.

## Entscheidung

- Kein MVVM-Umbau. Code-behind bleibt.
- Fachlogik gehoert in `Features/` bzw. `Infrastructure/` und wird dort getestet, nicht im Fenster.
- Grosse Fenster werden abschnittsweise in UserControls mit kleiner Schnittstelle zum Fenster aufgeteilt. Muster: `TasksPanel` (Ereignisse `StateChanged`/`StatusMessage`; das Fenster besitzt Navigation, Kopfleiste und Statuszeile). Gemeinsame Stile liegen in einem ResourceDictionary ohne Event-Handler (`KnowledgeBaseStyles.xaml`).

## Folgen

- Ein Abschnitt je Schritt, jeweils mit Smoke-Test und manueller Pruefung, weil sich Oberflaechen hier nicht automatisch pruefen lassen (keine Bildschirmaufnahmen).
- Offene Abschnitte stehen in `docs/ROADMAP.md`.

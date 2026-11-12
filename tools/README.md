# tools

Lokale Entwicklerwerkzeuge, die nicht zur App gehoeren: kleine Vorschau- und Testprogramme (z. B. `BrainPreview`, `LocalAiSmoke`, `TrayPreview`), Skripte und Diagnosehilfen. Der Inhalt ist nicht versioniert; nur diese Datei liegt in Git.

Ein Werkzeug, das andere brauchen, bekommt einen eigenen Unterordner und wird in `.gitignore` freigegeben (`!tools/<name>/`).

Nicht hierher gehoeren:

- Modelle, die der Build laedt: `models/`
- Datenmigrationen der App: im Code (`src/Merkautomat/Infrastructure/Persistence/DataMigration.cs`), sie laufen beim Start

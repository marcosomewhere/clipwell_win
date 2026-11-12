# 0006: Benutzerdaten im Tool-Verzeichnis

Stand: gilt, 2026-11-08

## Kontext

Einstellungen, verschluesselte Inhalte und nachgeladene Modelle sollen zusammen mit Merkautomat in einem eigenen Tool-Verzeichnis liegen. Build-Ausgaben werden beim Entwickeln bereinigt und eignen sich daher nicht als dauerhafter Datenordner.

## Entscheidung

- Benutzerdaten liegen unter `data\`: bei Entwicklungsbuilds unter `src\Merkautomat\bin\...` im Repository-Stamm, bei einer verteilten Anwendung direkt neben `Merkautomat.exe`.
- Die Erkennung des Entwicklungsstamms verlangt `Merkautomat.slnx`, das App-Projekt und den erwarteten Build-Unterpfad. Eine unter `dist\` erzeugte Kopie verwendet ihren eigenen `data\`-Ordner.
- Alle Speicherorte leiten sich von `AppPaths.DataDir` ab, auch nachgeladene Modelle und die installierte Outlook-Add-in-Kopie (`data\outlook`). Mitgelieferte Modelle und Build-Caches bleiben Programmressourcen unter `models\`.
- Vor dem ersten Oeffnen werden bestehende Dateien aus `%APPDATA%\Clipwell` bytegetreu in einen benachbarten Zwischenordner kopiert. Erst nach vollstaendiger Kopie wird dieser zu `data` umbenannt. Vorhandene lokale Profile werden nie mit einem anderen Profil vermischt; die alte Kopie bleibt erhalten.
- An Verschluesselung, Datenschluessel und Dateiformaten aendert sich nichts. Symlinks/Junctions werden bei der Uebernahme nicht verfolgt. Ein Fehler bricht den Start vor dem Oeffnen der Stores ab; kein stiller Rueckfall nach AppData und kein neuer Schluessel in einem teilweise kopierten Profil.
- Das Outlook-Add-in bekommt `DataDirectory` im bestehenden HKCU-Eintrag und verwendet denselben Pfadresolver. Ein vorhandenes Add-in wird auf die lokale Kopie umregistriert, ohne einen durch Outlook deaktivierten Zustand zu aktivieren. Ein bereits laufendes Outlook muss danach neu gestartet werden.

## Folgen

- Der Tool-Ordner muss beschreibbar sein; geschuetzte Programmordner sind ohne zusaetzliche Rechte ungeeignet.
- Beim Update nur Programmdateien ersetzen und `data\` erhalten; fuer eine vollstaendige Sicherung die Anwendung beenden und `data\` kopieren.
- Eine Kopie auf einen anderen PC bzw. ein anderes Windows-Konto braucht weiter den Notschluessel, weil DPAPI an das Konto gebunden ist.
- `data/` und Zwischenordner sind aus Git ausgeschlossen. Die bisherige AppData-Kopie wird nicht mehr als aktiver Speicher verwendet und erst nach gesonderter Entscheidung geloescht.

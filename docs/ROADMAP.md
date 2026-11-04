# Clipwell Windows - Roadmap und offene Punkte

Stand: 2026-06-27

Diese Datei ist die zentrale Ablage fuer offene Produkt-, Technik- und Release-Punkte. Kurzlebige Review-, Bug- und Feature-MDs werden hier konsolidiert und nicht separat weitergefuehrt.

## Naechste sinnvolle Arbeit

| Prioritaet | Thema | Stand |
| --- | --- | --- |
| Hoch | Installer-/Release-Format festlegen: MSIX, NSIS oder portable ZIP | Offen |
| Hoch | Release-Pipeline mit versionierten Artefakten und Release Notes | Offen |
| Mittel | Code-Signing fuer Binary und Installer | Offen |
| Mittel | Bildeditor manuell auf DPI-, Zoom- und Multi-Monitor-Setups pruefen | Offen |
| Mittel | UI-Interaktionstests fuer Maus/Tastaturpfade ergaenzen | Offen |
| Niedrig | URL-Preview-Koordination in `App.OnClipboardChanged` und `App.ReloadUrlPreview` ggf. extrahieren | Beobachten |

## Bekannte Grenzen

- Bildeditor-Mausgenauigkeit ist automatisiert nur ueber Smoke-Tests und gezielte Utility-Tests abgedeckt.
- Browser-Oeffnen aus dem laufenden Tray-Popup ist implementiert, aber nicht automatisiert als End-to-End-Pfad getestet.
- Auto-Backup und JSON-Export sichern Metadaten, Bilder und Thumbnails; SQLite-Export bleibt die vollstaendige 1:1-Sicherung.
- Der Weichzeichnen-Effekt im Bildeditor ist aktuell eine visuelle Ueberlagerung, kein echter Gaussian Blur.

## Erledigt und konsolidiert

- Alte Tagesdateien `docs/next-feature.md`, `docs/refactoring-2026-06-07.md` und `docs/review-2026-06-07.md` wurden in `STATUS.md` und diese Roadmap ueberfuehrt.
- Fruehere Refactoring-Punkte zu Regex-Caching, Clipboard-Schreibpfad, Thumbnail/Favicon-Laden und Monitor/DPI-Helfern sind umgesetzt.
- Favicon-Fetches sind groessenlimitiert und URL-Preview-Fetches blockieren private, Loopback- und Link-Local-Ziele.
- JSON-Export/Import erhaelt Nutzung, Pin-Reihenfolge, LastUsedAt und ThumbnailData.

## Nicht geplant

- Cloud-Sync direkt auf der Live-SQLite-Datenbank ueber OneDrive/iCloud.

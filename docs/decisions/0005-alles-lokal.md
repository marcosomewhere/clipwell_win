# 0005: Alles lokal: keine Cloud, lokale Modelle, Verschluesselung

Stand: gilt, 2026-11-08

## Kontext

Die Zwischenablage enthaelt Passwoerter, Kundendaten und Mails. Die App laeuft auch auf Firmen-PCs.

## Entscheidung

- Keine Cloud-Dienste, kein Konto, keine Telemetrie. Netzwerk nur fuer die URL-Vorschau (abschaltbar, private Adressen gesperrt) und fuer ausdruecklich angestossene Modell-Downloads mit SHA256-Pruefung.
- KI nur lokal: E5 (Suche, Einordnung) per ONNX Runtime, Granite per llama.cpp nur auf 127.0.0.1.
- Alle Inhalte verschluesselt auf der Platte; Schluessel per DPAPI an das Windows-Konto gebunden, dazu ein Notschluessel. Details: `docs/security/verschluesselung.md`.
- Kein automatisierter Screen-Capture (loest Sicherheitsalarme aus); Aufnahmen lesen Text per UI Automation.

## Folgen

- Funktionen, die nur mit Cloud-Modellen gut gingen, entfallen oder bleiben schwaecher.
- Neue Datei- und Datenbankzugriffe immer ueber `SecureFile` bzw. `EncryptedDatabase`.

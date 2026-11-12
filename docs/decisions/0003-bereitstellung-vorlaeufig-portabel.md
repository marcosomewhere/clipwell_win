# 0003: Vorlaeufig portabel, self-contained, nur x64, unsigniert

Stand: gilt bis zur ersten Weitergabe an fremde PCs, 2026-11-08

## Kontext

Merkautomat laeuft derzeit nur im Eigengebrauch. Installer, Signatur und Updates kosten Zeit und teils Geld (Zertifikat). Die App liest allerdings Fenstertexte, ueberwacht die Zwischenablage, registriert ein Outlook-Add-in und startet `llama-server.exe`; unsigniert sieht das fuer Sicherheitssoftware verdaechtig aus.

## Entscheidung

- Auslieferung als portabler Single-File-Build (`dotnet publish -r win-x64 --self-contained`), Ordner `dist/`.
- Self-contained, damit keine .NET-Laufzeit installiert sein muss.
- Nur x64. ONNX Runtime, Whisper und llama.cpp liefern wir nur fuer x64 mit; auf ARM64 liefe die App emuliert.
- Keine Signatur, kein Installer, keine Updates.

## Folgen

- Vor jeder Weitergabe an PCs ausserhalb der eigenen Kontrolle muss der Abschnitt "Bereitstellung, Packaging und Signatur" in `docs/ROADMAP.md` erledigt sein; dann ersetzt eine neue Entscheidung diese hier.
- Sicherheitsfixes der Abhaengigkeiten erreichen verteilte Kopien nur durch einen neuen Build.
- Platzhalter fuer spaeter: `packaging/`.

# packaging

Installation, Verteilung und Signatur. Noch nicht umgesetzt: derzeit gibt es nur den portablen Single-File-Build (`dotnet publish`, siehe `README.md`; Entscheidung in `docs/decisions/0003-bereitstellung-vorlaeufig-portabel.md`).

Geplante Inhalte, Reihenfolge und Begruendung stehen in `docs/ROADMAP.md`, Abschnitt "Bereitstellung, Packaging und Signatur":

- `MSIX/` oder `Installer/`: Paket bzw. Installer, je nach Entscheidung
- `Signing/`: Signierschritt fuer `Merkautomat.exe` und `MerkautomatOutlook.dll` (Schluessel nie im Repository)
- Update-Weg: nur signierte Pakete mit Hash-Pruefung

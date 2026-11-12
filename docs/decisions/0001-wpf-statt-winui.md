# 0001: WPF statt WinUI 3

Stand: gilt, 2026-11-08

## Kontext

Fuer neue native Windows-Apps ist WinUI 3 mit Windows App SDK der Standard. Merkautomat existiert bereits als umfangreiche WPF-App (eigene Zeichenflaechen, FlowDocument-Vorschau, WPF-UI, Tray per WinForms `NotifyIcon`) und laeuft nur unter Windows.

## Entscheidung

Merkautomat bleibt bei WPF auf .NET 10 LTS. Kein Umbau auf WinUI 3.

## Folgen

- Kein Windows App SDK; Windows-Funktionen ueber WinAPI und WinRT-Projektionen (z. B. OCR).
- Ein Wechsel lohnt sich erst, wenn eine benoetigte Funktion nur mit WinUI geht. Dann neue Entscheidung.

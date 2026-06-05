# Clipwell Windows – Architektur

## Tech-Stack

| Schicht | Technologie |
|---|---|
| Framework | .NET 8, WPF (`net8.0-windows10.0.19041.0`) |
| UI-Bibliothek | WPF-UI 4.3.0 (NuGet-ID: `WPF-UI` mit Dash, Owner: Lepo) |
| Tray | WinForms `NotifyIcon` (`UseWindowsForms=true`) |
| Datenbank | SQLite via `Microsoft.Data.Sqlite 8.0.10`, WAL-Mode |
| OCR | `Windows.Media.Ocr.OcrEngine` (WinRT, requires Windows 10+) |
| Datenpfade | DB: `%APPDATA%\Clipwell\history.db` – Settings: `%APPDATA%\Clipwell\settings.json` |

## Schichten

```
┌──────────────────────────────────────────────────┐
│  Views  (WPF XAML)                               │
│  PopupWindow · DetailWindow · SettingsWindow     │
│  OnboardingWindow · PinboardWindow               │
│  EyedropperWindow                                │
├──────────────────────────────────────────────────┤
│  ViewModels  (MVVM)                              │
│  PopupViewModel · EntryViewModel · ViewModelBase │
├──────────────────────────────────────────────────┤
│  Services                                        │
│  DatabaseService · ClipboardProcessor            │
│  SyntaxService · ContentKindService              │
│  SensitiveContentService · OcrService            │
│  UrlPreviewService · SettingsService             │
│  MessageWindowService                            │
├──────────────────────────────────────────────────┤
│  Models                                          │
│  ClipboardEntry · AppSettings · EntryType        │
│  ThemeMode · HotkeyAction · CodeDetectionMode    │
│  (AppSettings: KeyPin/Details/QuickNote/Pinboard)│
├──────────────────────────────────────────────────┤
│  App.xaml.cs  (Anwendungssteuerung)              │
│  Lifecycle, Tray, Hotkey, Clipboard-Event-Chain  │
├──────────────────────────────────────────────────┤
│  NativeMethods.cs  (P/Invoke)                    │
│  Clipboard-Hook, Hotkey, DWM, SendInput          │
└──────────────────────────────────────────────────┘
```

## Bekannte WPF-Fallstricke

| Problem | Loesung |
|---|---|
| `TextBlock.MaxLines` existiert nicht in WPF | `MaxHeight + ClipToBounds="True"` auf umschliessender `Border` |
| `StackPanel.Spacing` erst ab .NET 9 | `Margin` auf Child-Elementen |
| Namespace-Konflikte: `Brush`, `Color`, `Point`, `BitmapDecoder` | `using`-Aliases in betroffenen Dateien |
| `DataTemplate.Triggers` kann kein `TargetName` | `DataTrigger` mit `TargetName` in `DataTemplate.Triggers` – funktioniert nur mit Named Elements |
| Hover-Zustand in DataTemplate | `IsHovered` Property im ViewModel + `DataTrigger` |
| `MouseEventArgs`/`KeyEventArgs` in XAML-Code-behind (WPF + WinForms aktiv) | Vollqualifizierte Typen in Methodensignaturen: `System.Windows.Input.MouseEventArgs` |
| `FlowDocument` rendert grosse Dateien langsam (kein Virtualisieren) | `SyntaxService.HighlightCore(maxLines)` + `AppendLines` via `Dispatcher.InvokeAsync(Background)` |

## Clipboard-Event-Kette

```
Clipboard-Aenderung (OS)
  └─> MessageWindowService.WM_CLIPBOARDUPDATE
        └─> App.OnClipboardChanged
              ├─> Inkognito-Check (global / App / Domain)
              ├─> SensitiveContentService.IsSensitive
              ├─> ClipboardProcessor.BuildEntry
              ├─> PopupViewModel.AddEntry
              │     ├─> Purge nach Anzahl
              │     └─> Purge nach Alter
              ├─> [Bild] OcrService.RecognizeAsync (async)
              └─> [URL]  UrlPreviewService.FetchAsync (async)
                          └─> vm.UrlState = Loading -> Loaded/Failed
```

## Eyedropper-Kette

```
PopupWindow „Eyedropper"-Button
  └─> EyedropperWindow (vollbild-transparent, Topmost)
        ├─> GDI GetPixel bei MouseMove → Farbvorschau
        └─> Linksklick → PickedHex gesetzt, DialogResult=true
              └─> App.CopyColorToClipboard(hex)    // in Clipboard
              └─> PopupViewModel.AddEntry(Color)   // in History
```

## Pinboard-Kette

```
Tray „Pinboard" / Ctrl+B im Popup
  └─> App.TogglePinboard()
        └─> PinboardWindow.Show() / Hide()
              ├─> Entries.CollectionChanged → Refresh()  // live
              └─> PasteBtn → App.PasteEntry(vm, false)
```

## Filter-Logik (PopupViewModel.Filter)

Reihenfolge:
1. `TypeFilter` (Chip-Auswahl: null = Alle)
2. `ShowPinnedOnly` (Gepinnt-Chip)
3. Such-Praefix-Parsing: `type:`, `kind:`, `domain:`, `pinned:`
4. Volltextsuche: Content, OcrText, UrlTitle, Language

## Datenbankschema (History-Tabelle)

```sql
CREATE TABLE History (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    Type            INTEGER NOT NULL,   -- EntryType enum
    Content         TEXT,
    ImageData       BLOB,
    OcrText         TEXT,
    Language        TEXT,
    UrlTitle        TEXT,
    UrlFavicon      BLOB,
    HexColor        TEXT,
    ContentKind     TEXT,
    DetectionReason TEXT,
    IsPinned        INTEGER NOT NULL DEFAULT 0,
    Timestamp       TEXT NOT NULL      -- ISO 8601
);

CREATE TABLE UrlCache (
    Url      TEXT PRIMARY KEY,
    Title    TEXT,
    Favicon  BLOB,
    CachedAt TEXT NOT NULL             -- 7-Tage-TTL
);
```

# Refactoring-Zusammenfassung — 2026-06-07

Build: **Release, erfolgreich** | Tests: **56/56 bestanden**

Alle Änderungen sind rein strukturell — kein Verhalten geändert, keine Features entfernt.

---

## Geänderte Dateien

### ClipwellWin/Services/OcrService.cs

- `2 * 1024 * 1024` durch benannte Konstante `MaxImageSizeBytes` ersetzt.

---

### ClipwellWin/App.xaml.cs

| Änderung | Details |
|----------|---------|
| `LogCrash`: `DatabaseService.DataDir` → `AppPaths.DataDir` | Entfernt unnötige Kopplung des Crash-Loggers an DatabaseService |
| Neue Konstanten `PasteDelayMs = 140`, `ClipboardBusyMessage` | Magic Number und Literalstring benannt |
| `FillRoundedRect` via `RoundedRect` implementiert | 7 duplizierte Pfad-Konstruktions-Zeilen entfernt |
| Neuer Helper `TryWriteEntryToClipboard` | Identischer 10-Zeilen-Block aus `PasteEntry` und `CopyEntryToClipboard` extrahiert |

---

### ClipwellWin/Services/ClipboardProcessor.cs

- `HtmlTagRx` und `MultiSpaceRx` als `static readonly Regex`-Felder mit `RegexOptions.Compiled` hinzugefügt.
- `StripHtml` verwendet jetzt diese Felder statt inline `Regex.Replace`.
- `ScriptStyleRx` um `RegexOptions.Compiled` ergänzt.

---

### ClipwellWin/Services/SyntaxService.cs

- Neues statisches Feld `CodeAnchorRx`: Das identische Code-Anchor-Pattern aus `LooksLikeCode` (Z. 81) und `LooksLikePlainTextBlock` (Z. 121) wurde extrahiert.
- Neues statisches Feld `StrongCodeSignalRx`: `HasStrongCodeSignal` erzeugt keinen Regex mehr bei jedem Aufruf.
- Beide Felder kompiliert (`RegexOptions.Compiled`).

---

### ClipwellWin/Services/ContentKindService.cs

14 neue `static readonly Regex`-Felder mit `RegexOptions.Compiled` für alle `LooksLike*`-Methoden und `AnalyzeKeyValueLines`:

| Feld | Methode |
|------|---------|
| `TomlSectionRx`, `TomlKeyValueRx` | `LooksLikeToml` |
| `XmlDeclRx`, `XmlOpenTagRx`, `XmlCloseTagRx` | `LooksLikeXml` |
| `HtmlDoctypeRx`, `HtmlRootRx`, `HtmlTagRx` | `LooksLikeHtml` |
| `IniSectionRx`, `IniKeyValueRx` | `LooksLikeIni` |
| `EnvLineRx` | `LooksLikeEnv` |
| `DockerfileRx` | `LooksLikeDockerfile` |
| `ConfigXmlRx` | `LooksLikeConfig` |
| `KeyValueLineRx` | `AnalyzeKeyValueLines` |

---

### ClipwellWin/ViewModels/EntryViewModel.cs

- `LoadThumbnail` und `LoadImage` zu `LoadBitmapImage(byte[] data, int decodeWidth)` zusammengeführt.
- Aufrufe: `LoadBitmapImage(data, 80)` (Thumbnail) und `LoadBitmapImage(data, 16)` (Favicon).

---

### ClipwellWin/ViewModels/PopupViewModel.cs

- `SecureDelete(EntryViewModel vm, DatabaseService db)` → `SecureDelete(EntryViewModel vm)`.
- Intern wird `_db` verwendet, das bereits die korrekte Instanz enthält. Der externe Parameter war redundant.

---

### ClipwellWin/Views/PopupWindow.xaml.cs

- Neuer Helper `GetMonitorAndScale(out POINT cursor, out MONITORINFO mi, out double scaleX, out double scaleY)`.
- Ersetzt den identischen 10-Zeilen-Block in `PositionAtCursor`, `PositionAboveTaskbar` und `ClampSizeToCurrentMonitor`.
- `_vm.SecureDelete(vm, _app.Database)` → `_vm.SecureDelete(vm)` (siehe PopupViewModel).

---

## Nicht geänderte Dateien (mit Begründung)

| Datei | Warum nicht geändert |
|-------|----------------------|
| `DatabaseService.cs` | `DataDir`-Duplizierung bleibt (public API, könnte extern referenziert werden); `SecureDelete`-Reentrant-Lock ist korrekt dokumentiert |
| `App.xaml.cs` `OnClipboardChanged`/`ReloadUrlPreview` | Async-Koordination und State-Management zu komplex für sicheres Extrahieren |
| `AppSettings.cs` | Shortcut-Flachstruktur und `NativeMethods`-Kopplung würden JSON-Format brechen |
| `PopupWindow.xaml.cs` Keyboard-Shortcut-Matching | Korrekt und vollständig, kein Duplikat |
| `NativeMethods.cs`, `Converters.cs`, `ViewModelBase.cs` | Keine Befunde |
| Alle Test-Dateien | Unberührt; 56/56 Tests bestehen nach Refactoring |

---

## Analyse-Dokument

Vollständige Befundliste mit Risikobewertung: [review-2026-06-07.md](review-2026-06-07.md)

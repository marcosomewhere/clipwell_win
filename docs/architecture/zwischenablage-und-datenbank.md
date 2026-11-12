# Zwischenablage-Ablauf, Filter und Datenbank

## Clipboard-Event-Kette

```text
Clipboard-Aenderung
  -> MessageWindowService.WM_CLIPBOARDUPDATE
  -> App.OnClipboardChanged
  -> ClipboardProcessor.BuildEntry (Text plus CF_HTML-Fragment und SourceURL)
  -> PopupViewModel.AddEntry
     -> Bild-Dedupe
     -> Purge nach Anzahl, Alter und optional DB-Groesse
  -> Bild: OCR async
  -> URL: Preview nur wenn aktiviert und UrlPreviewService.ShouldFetch erlaubt
```

## Filter-Logik

Reihenfolge in `PopupViewModel.Filter`:

1. `TypeFilter` (Chip "MD": `ShowMarkdownOnly`, Badge MD)
2. `ShowPinnedOnly`
3. Suchpraefixe `type:`, `kind:`, `domain:`, `pinned:true`/`pinned:false`
4. FTS5-Treffer (`DatabaseService.SearchFts`), nur bei mehr als 1000 Eintraegen, ohne Regex und ohne Praefix
5. Regex-Modus (Toggle-Button `.*`, Timeout 50 ms) ueber Content, OCR-Text, URL-Titel und Sprache
6. Lineare Volltextsuche (case-insensitive) ueber Content, OCR-Text, URL-Titel und Sprache

Suchhistorie: letzte 5 Suchen als Dropdown. Sortierung im Popup: Neueste, Aelteste, Haeufig genutzt.

Gruppen: Gepinnt, Heute, Gestern, Diese Woche, Frueher.

## Datenbankschema

```sql
CREATE TABLE History (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    Type            INTEGER NOT NULL,
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
    PinOrder        INTEGER NOT NULL DEFAULT 0,
    ThumbnailData   BLOB,
    UseCount        INTEGER NOT NULL DEFAULT 0,
    LastUsedAt      TEXT,
    HtmlContent     TEXT,
    SourceUrl       TEXT,
    Timestamp       TEXT NOT NULL
);

CREATE TABLE UrlCache (
    Url      TEXT PRIMARY KEY,
    Title    TEXT,
    Favicon  BLOB,
    CachedAt TEXT NOT NULL
);

CREATE INDEX IX_History_Pinned_Timestamp ON History (IsPinned, Timestamp);

CREATE VIRTUAL TABLE HistoryFts
    USING fts5(content, ocrtext, urltitle, language, tokenize='unicode61');
-- Trigger HistoryFts_ai/_ad/_au halten HistoryFts synchron zu History.
```

Kompatibilitaet: Das Schema waechst nur. Neue Spalten kommen per `EnsureColumn` mit Standardwert dazu, neue Tabellen per `CREATE TABLE IF NOT EXISTS`; nichts wird umbenannt oder entfernt, und es gibt keine Versionsnummer. Damit oeffnet jede Version von Merkautomat eine `history.db` jeder anderen Version: aeltere Programme ignorieren neue Spalten, neuere ergaenzen fehlende. Tests `OlderDatabase_GetsMissingColumnsAndKeepsEntries` und `DatabaseWithColumnsOfANewerVersion_StillOpensAndWorks`. Dasselbe gilt fuer `semantic.db` (Vektoren tragen die Modellkennung; passt sie nicht, wird der Eintrag neu berechnet). Die YAML-Korrektur (`MigrateYamlTextEntries`) ist eine Datenkorrektur und laeuft bei jedem Oeffnen.

`ThumbnailData`, `UseCount`, `LastUsedAt`, `HtmlContent` und `SourceUrl` sind so nachtraeglich dazugekommen. `LoadAllLite` laedt `HtmlContent` nicht mit (nur `HasHtml`); `DatabaseService.LoadHtml` holt es bei Bedarf. Ist FTS5 nicht verfuegbar, faellt die Suche auf den linearen Scan zurueck.

## Gemeinsame Metadaten

`History.MetadataJson TEXT` ergaenzt das Schema mit Standard NULL. `KnowledgeMetadata` serialisiert den gemeinsamen Quellenkontext und behandelt optionale Markdown-Metadatenbloecke in Notizen/Aufnahmen. Metadaten werden im Suchindex-Snapshot mitgefuehrt, bleiben aber ausserhalb der Embeddings und deren Texthashes. Unbekannte JSON-Felder bleiben beim erneuten Schreiben erhalten.

# Merkautomat for Windows

Merkautomat is a local-first Windows clipboard manager built with WPF, .NET 10 and SQLite. It runs in the tray, records clipboard history and opens a keyboard-first popup for search, copy and paste.

User-facing names: Merkautomat (app), Ideenkombinat (knowledge base), Pinstation (pinboard), Sprechwerk (voice notes), Kontrollstation (settings). The executable is `Merkautomat.exe`; user data lives under `data` in the tool folder (repository root for development builds, next to the executable for distributed copies). Projects, namespaces and runtime identifiers also use Merkautomat. Legacy names remain only for migration and coordination with older installations.

## Features

- Clipboard history for text, notes, URLs, code/config snippets (including YAML detection and validation), colors and images.
- Global hotkey with automatic fallback combinations when the requested shortcut is blocked.
- Keyboard-first popup with type chips, pinned filter, grouped entries, search prefixes, regex search, search history and sorting (newest, oldest, most/least used).
- Bulk actions for pin, unpin, export and delete.
- Detail window with editable text/code minieditor, image editor, OCR view and color values.
- Image support with thumbnail, zoom/pan, precise annotations, crop/resize/expand and duplicate-image suppression.
- URL previews with title/favicon cache, opt-out setting and loopback/private-network protection.
- URL quick links open entries in the default browser from popup or details.
- Pinstation: always-on-top window with the pinned entries.
- Eyedropper overlay for picking screen colors into history and clipboard.
- Desktop recording: after pressing Record, the visible text of the foreground window is read through UI Automation (no screenshots) and every stable change is appended as Markdown.
- Sprechwerk: voice notes within the Ideenkombinat with local Whisper dictation and popup quick notes; legacy notes without an export are included. No separate window or tray submenu.
- Ideenkombinat (knowledge base window): overall Markdown summary of clipboard history, exported notes and all recordings, per-item view, copy and export.
- Ideenkombinat semantic search: local embedding model (multilingual-e5-small, bundled) finds clipboard entries, voice notes and recordings by meaning; hybrid ranking with word matches, similar entries and near-duplicate detection.
- Ideenkombinat "Fragen": the semantic search answers questions with the selected local language model from the best hits, citing them as [n]; "Aufgaben": tasks from clipboard entries (popup context menu) or typed in, with due date, notes and done state.
- Ideenkombinat "Brain": knowledge network of all indexed items with links by meaning, time, recording and website, automatic topics with keyword names, interactive force layout, focus and search.
- Optional category-only models MiniLM L6 (23.3 MB, mainly English) and Horizon ZeroShot Small (302.8 MB, multilingual) in Kontrollstation → Lokale KI. Downloads with SHA256 verification into `data\models`. They select existing categories and keep titles; E5 remains the search model.
- Optional local Granite 4.2 3B or Qwen3.5-2B for title/category suggestions, category analysis and source-based answers. Choose the categorization model and the language model for topic names/answers independently in Kontrollstation → Lokale KI, and download once (Granite 2.27 GB, Qwen 1.31 GB including runtime) into `data\models`; inference stays on the CPU, with explicit suggestion review before saving. Apache 2.0 / MIT, no API account required.
- Fast E5 categorization in the same dropdown reuses the bundled search model to compare existing category names. Ambiguous or mixed excerpts remain unassigned; titles are preserved. Saved manual assignments act as examples per category (Rocchio prototype); no extra download, no training run and no self-training from graph edges or unsaved suggestions.
- Categorization pass (`CategorizeDialog`) from a Brain node or topic, all uncategorized entries, marked search results or the library selection: shared suggestion for marked entries plus one-by-one confirmation; each confirmation is saved and learned immediately.
- Rocchio details (`RocchioModel`): up to three sub-prototypes for broad categories, enabled only when a leave-one-out check on the saved assignments shows a benefit; top-3 candidates and the nearest own example in each suggestion; "Qualität prüfen" in the Kontrollstation opens the evaluation window.
- Category analysis with the selected language model (`CategoryDiscoveryWindow`): uncategorized entries are grouped by their search vectors, the model names each group, the user reviews and applies the proposals.
- Classifier choice and report (`ClassifierReportWindow`): names only, Rocchio with/without names, sub-prototypes, k-NN and logistic regression compared by grouped 5-fold cross-validation (3 repeats); Rocchio with hard negatives (gamma 0.1/0.2), textbook Euclidean and robust (trimmed) Rocchio as ablations; flagged assignments that look mislabeled, with a jump into the categorization window; top-1, Wilson interval, unknown-topic rejection and a calibrated abstention threshold (40+ examples) per method; automatic choice of the simplest method unless another is clearly better; chart, metrics table, per-category results with confusion matrix, real-use feedback, history and Markdown export.
- Export/import as JSON, SQLite backup export, auto-backup rotation and secure delete via VACUUM.
- Privacy controls: monitoring pause via tray menu.
- SQLite recovery prompt when `quick_check` fails.

## Keyboard

Default hotkey: `Win+Shift+V`

Popup shortcuts:

- `Enter`: paste selected entry
- `Ctrl+Enter`: paste selected entry as plain text
- `Up` / `Down`: move selection
- `Ctrl+P`: pin selected entry
- `Delete`: delete selected entry
- `Ctrl+B`: toggle pinboard
- `F2`: open details
- `Esc`: close popup

## Search

Supported prefixes:

- `type:url`, `type:text`, `type:code`, `type:image`, `type:color`
- `kind:code`, `kind:note`, `kind:png`
- `domain:github.com`
- `pinned:true`, `pinned:false`

## Data Locations

- Settings: `data\settings.json`
- Data root: `data\` in the tool folder, independent of the working directory. Existing AppData data is copied together with its encryption key before first use; the old copy is retained. Preserve `data\` when replacing program files.
- History database: `data\history.db`
- Log: `data\merkautomat.log`
- Image editor style memory: `data\editor-styles.json`
- Desktop recordings: `data\recordings\*.md`
- Notes: `data\notes\*.md`; notes exported to the KB: `data\kb\notes\*.md`
- Whisper base model: bundled as `models\ggml-base.bin` next to `Merkautomat.exe` (MIT, see `THIRD-PARTY-NOTICES.txt`); the build fetches it into `models\whisper` at the repository root if missing. Larger models (small/medium) download on demand to `data\models`.
- Semantic search: model bundled as `models\e5-small\` next to `Merkautomat.exe` (MIT, see `THIRD-PARTY-NOTICES.txt`; the build fetches it into `models\e5-small` at the repository root if missing), vector index (no text) in `data\semantic.db`
- Recovery backups: next to `history.db` with `.broken-YYYYMMDD-HHMMSS` suffix

## Build

Requirements: Windows 10 version 2004 (build 19041) or newer, .NET 10 SDK (pinned in `global.json`).

```powershell
taskkill /IM Merkautomat.exe /F
dotnet build src\Merkautomat\Merkautomat.csproj -c Release --nologo -v q
Start-Process src\Merkautomat\bin\Release\net10.0-windows10.0.19041.0\Merkautomat.exe
dotnet test --project tests\Merkautomat.Tests\Merkautomat.Tests.csproj -c Release
```

One network test (fallback download of the embedding model) runs only with `MERKAUTOMAT_E5_DOWNLOAD=1`.

Portable single-file build (the only distribution format for now, see `docs/decisions/0003-bereitstellung-vorlaeufig-portabel.md`):

```powershell
dotnet publish src\Merkautomat\Merkautomat.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist\Merkautomat-win-x64
```

## Repository layout

```text
README.md, AGENTS.md, CLAUDE.md   overview and rules for AI-assisted development (AGENTS.md = CLAUDE.md)
.editorconfig, .gitattributes     code style and line endings
global.json, Directory.Build.*    SDK pin and build settings for all projects (must stay at the root)
Merkautomat.slnx                  solution

docs/
  STATUS.md, ROADMAP.md           current state with manual checks, open points
  handbuch.md                     manual (German): part A users, part B admins
  architecture/                   how the code fits together
  features/                       what each area does, with its rules
  security/                       encryption and privacy
  api/                            interfaces between programs (Outlook hand-over)
  decisions/                      architecture decision records

src/
  Merkautomat/                    the app (Merkautomat.exe)
    App.xaml(.cs), App/           start-up, tray, hotkeys, background work, UiTasks
    Features/                     Clipboard, Search, KnowledgeBase, Categories, Notes, Voice,
                                  Recordings, Tasks, Settings
    Infrastructure/               Persistence, Security, AI, Windows
    Core/                         models and names shared by all areas
    UI/                           Views (windows, controls, styles), ViewModels, Converters
    Assets/, Licenses/            images compiled into the EXE, third-party licenses
  MerkautomatOutlook/             Outlook COM add-in (net48)

tests/Merkautomat.Tests/          Unit/, Integration/ (files, database, registry, Windows), UI/, Support/
tools/                            local developer tools (not versioned, see tools/README.md)
packaging/                        installer, MSIX and signing (planned)
models/                           models fetched by the build (not versioned)
dist/                             portable builds (not versioned)
```

Folders group files by area; namespaces use `Merkautomat.Services`, `Merkautomat.Views`, etc. and do not follow the folders.

## Documentation

Start with [docs/README.md](docs/README.md).

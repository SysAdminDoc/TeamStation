# Changelog

All notable changes to TeamStation are documented here. Format loosely follows [Keep a Changelog](https://keepachangelog.com/) and [Semantic Versioning](https://semver.org/).

## [0.0.6] - 2026-04-23

### Added
- **Runtime inheritance cascade.** `ConnectionEntry.Mode`, `Quality`, `AccessControl`, and `Password` are now nullable. A null value means "inherit from the folder chain" and is resolved at launch time by a new `InheritanceResolver` service in `TeamStation.Core/Services/`. The resolver walks up `ParentFolderId` pointers, pulling the first non-null default from each ancestor, without mutating the source entry.
- **(inherit from folder)** option added to the mode / quality / access-control combos in `EntryEditorWindow`. New entries created inside a folder default to all-null so they literally defer to the folder until the user chooses otherwise.
- **Folder accent colors now render.** Each folder's tree dot uses its `AccentColor` (validated `#RRGGBB`); fallback to Catppuccin Mauve when unset. Parsing failures gracefully fall back rather than throwing.
- **Schema v2 migration.** `Database.MigrateIfNeeded` rebuilds the `entries` table with nullable `mode` / `quality` / `access_control` columns, preserving all existing data via a `CREATE TABLE entries_v2 + INSERT SELECT + DROP + RENAME` transaction. Fresh installs go straight to v2 via an `isFresh` path that stamps the schema version without running migrations.

### Changed
- `TeamViewerLauncher` defaults a null mode to `RemoteControl` for transport selection (CLI vs URI). Callers wanting concrete values should pass the resolver's output.
- `CliArgvBuilder` emits no `--mode` / `--quality` / `--ac` flag when the corresponding field is null, deferring to TeamViewer's own defaults.
- `EntryNode.Summary` renders "inherit" instead of an enum name when the entry's mode is null.
- `MainViewModel.AddEntry` no longer pre-copies folder defaults into the draft. New entries start fully inheriting from their parent folder; the user can override any field in the editor.

### Verified
- Clean Release build, 0 warnings / 0 errors across all five projects.
- **v1 → v2 migration:** seeded a simulated v0.0.5 database with `entries.mode INTEGER NOT NULL` and a legacy row, launched the app, confirmed the post-launch schema shows `mode INTEGER` (nullable), `schema_version` is hex `02000000`, and the legacy row's data is intact.
- **Fresh install:** deleted the database, launched the app, confirmed the new DB lands on schema v2 directly with a populated DEK.
- **Inheritance load:** seeded a folder with a yellow accent (`#F9E2AF`) and an entry with all three enum columns NULL. App rendered the tree cleanly without exceptions.

### Notes
- `v0.1.0` gating features still outstanding: search/filter, CSV import, JSON export, tray with recent connections, embedded log panel, and running `TvLaunchSpike` against a real peer.

## [0.0.5] - 2026-04-23

### Added
- **Drag-and-drop in the tree.** New `TreeDragDrop` service hooks the TreeView's mouse and drag events.
  - Drag an entry onto a folder to reparent it; drop on a sibling entry to join that entry's folder; drop on empty space to move it to root.
  - Drag a folder to reparent it. Own-subtree drops are rejected so a folder can never be moved inside itself or a descendant.
  - No-op drops (target is already the current parent) are silently ignored instead of triggering a save.
  - Drop is committed via a single `MainViewModel.Reparent(source, newParent)` entry point, which updates the model, persists through the appropriate repository, reloads the tree, and reselects the moved node.
- **Folder editor.** New `FolderEditorWindow` with fields for folder name, optional hex accent color (`#RRGGBB`, validated), default password (stored encrypted), and per-folder default mode / quality / access-control.
- **Inheritance on new-entry creation.** When a new entry is added inside a folder, the draft now pre-fills `Password`, `Mode`, `Quality`, and `AccessControl` from the nearest ancestor folder that carries that default. Entries retain their own values once saved — nothing is overwritten on load.
- **Unified `EditCommand`.** One command now edits whichever kind of node is selected (folder → folder editor, entry → entry editor). Replaces the prior `EditEntryCommand`.

### Changed
- Removed `Folder.DefaultProxy` — it was never persisted (no DB columns for it) and exposed a latent data-loss shape. If proxy inheritance becomes a feature, it will be added with an explicit schema migration.

### Verified
- Clean Release build, 0 warnings / 0 errors across all five projects.
- Seeded a 3-folder / 3-entry hierarchy (including a nested subfolder and a root-level entry) via `sqlite3`, launched the app, and confirmed the full tree renders without exception.

### Notes
- Runtime inheritance (where a launched entry *reads* its parent folder's defaults at launch time rather than only at creation) is the v0.0.6 target.
- `v0.1.0` gating work: search/filter, CSV import, JSON export, tray with recent connections, embedded log panel, and running `TvLaunchSpike` against a real peer.

## [0.0.4] - 2026-04-23

### Added
- **Folder tree.** Flat `ListView` replaced with a hierarchical `TreeView`.
  - New VM hierarchy: `TreeNode` (abstract) with `FolderNode` and `EntryNode` leaves. `EntryViewModel` retired — the `EntryNode` takes over the same role with a parent reference.
  - `MainViewModel` now builds the tree from flat `EntryRepository` + `FolderRepository` snapshots, wires folder–folder parent links, attaches entries to their declared folder (or root), and sorts folders-first then case-insensitive by name.
  - `IsExpanded` / `IsSelected` state lives on each node and two-way-binds to the `TreeViewItem` style so selection survives reloads.
- **Folder CRUD.** `AddFolderCommand` creates a root folder, `AddSubfolderCommand` creates one beneath the selected folder.
- **Unified Rename, Move, Delete commands** dispatch by selected node type:
  - `InputDialog` — styled input prompt used for new-folder names and renames.
  - `FolderPickerDialog` — tree picker for "Move to…", with "Root" escape hatch and automatic exclusion of the moved folder's own subtree.
  - `DeleteCommand` warns when deleting a non-empty folder and notes that enclosed entries become unassigned (ON DELETE SET NULL on the foreign key).
- **Context menu on the tree** binds to `MainViewModel` via `PlacementTarget.DataContext`. Also in the top toolbar.
- **Double-click to launch** now only fires when a `TreeViewItem` is under the click (not the empty tree background).

### Verified
- Clean Release build, 0 warnings / 0 errors across all five projects.
- Programmatically seeded a folder + an entry via `sqlite3`, relaunched the app, confirmed both materialize into the tree and the UI renders without exception.

### Notes
- Drag-and-drop reorder and folder-level inheritance cascade (mode / quality / access-control / proxy / password defaults pushed to descendants) are queued for `v0.0.5`.
- Search / filter, CSV import, JSON export, tray with recent connections, and embedded log panel remain the gating features for `v0.1.0`.

## [0.0.3] - 2026-04-23

### Added
- New `TeamStation.Data` project (SQLite + DPAPI + AES-256-GCM):
  - `StoragePaths` — portable-mode detection (marker file next to exe) or `%LocalAppData%\TeamStation\teamstation.db` default.
  - `Database` — schema bootstrap with WAL mode, foreign keys, indexed `folders` + `entries` tables, and a `_meta` key/value store. Implements `IKeyStore` for the crypto service.
  - `CryptoService` — generates a 256-bit DEK on first run, DPAPI-wraps it under `DataProtectionScope.CurrentUser`, and AES-256-GCM encrypts each password field with a fresh 96-bit nonce. Wire format: `nonce(12) | tag(16) | ciphertext(n)`.
  - `EntryRepository`, `FolderRepository` — full CRUD, transparent field encryption, `TouchLastConnected` for post-launch bookkeeping.
- MVVM in the App project:
  - `ViewModelBase`, `RelayCommand`, `EntryViewModel`, `MainViewModel`.
  - `EntryEditorWindow` — styled Catppuccin Mocha add/edit dialog with inline validation via `LaunchInputValidator`.
  - `MainWindow` refactored to a toolbar + `ListView` bound to entries, with Launch / Add / Edit / Delete commands. Double-clicking an entry launches it.
- `App.xaml.cs` now wires the full object graph at startup (DB, crypto, repos, launcher, view model) with a top-level exception handler that surfaces to a message box.

### Verified
- Clean Release build, 0 warnings / 0 errors across all five projects.
- Startup creates `teamstation.db` with the expected schema (foreign keys, WAL, `_meta` holding a 262-byte DPAPI-wrapped DEK and a 4-byte `schema_version`).
- App launches and terminates cleanly.

### Notes
- Launch from the UI uses `--PasswordB64` by default. Whether TeamViewer actually honors this silently on 15.58+ remains an open question until `TvLaunchSpike` is run against a real peer.
- Folder tree, drag-reorder, CSV import, JSON export, tray, and log panel are deferred to `v0.0.4` and `v0.1.0` per `ROADMAP.md`.

## [0.0.2] - 2026-04-23

### Added
- Solution scaffold (`TeamStation.sln`) with four projects:
  - `TeamStation.Core` — domain models (`ConnectionEntry`, `Folder`, `ConnectionMode`, `ConnectionQuality`, `AccessControl`, `ProxySettings`)
  - `TeamStation.Launcher` — `LaunchInputValidator` (CVE-2020-13699 hardening), `CliArgvBuilder`, `UriSchemeBuilder`, `TeamViewerPathResolver`, `TeamViewerLauncher`
  - `TeamStation.App` — minimal WPF shell with Catppuccin Mocha palette, app manifest with per-monitor DPI-V2 awareness
  - `tools/TvLaunchSpike` — interactive console harness for the two feasibility tests queued in `ROADMAP.md`
- `.NET 9` SDK pinned via `global.json`. Common settings in `Directory.Build.props` (nullable, warnings-as-errors, deterministic, version metadata).

### Verified
- Clean Release build, 0 warnings / 0 errors.
- WPF window opens and reports detected `TeamViewer.exe` path.
- Spike CLI usage screen prints without launching TeamViewer when args are missing.

### Notes
- No persistence layer, no launch wiring from the UI, no crypto implementation yet — those land in `v0.0.3`+ per `ROADMAP.md`.
- The two feasibility questions (silent launch behavior, URI param survival) remain open until the spike is run against a real peer.

## [0.0.1] - 2026-04-23

### Added
- Repository scaffold: `.gitignore`, MIT `LICENSE`, `README.md`.
- `ROADMAP.md` populated with P0 / P1 / P2 feature prioritization from deep research.
- `docs/teamviewer-reference.md` — CLI parameter table, URI handler matrix, CVE-2020-13699 hardening rules, mode-selection decision table, Web API sync endpoints, install-path discovery order.
- `CLAUDE.md` (local-only) — working notes, corrected CLI reference, must-spike risks.

### Notes
- No runtime code yet. First working build will land as `v0.1.0`.
- Two feasibility spikes queued before architecture is committed: silent-launch behavior of `--PasswordB64` on TV 15.58+, and URI-handler param survival post-CVE-2020-13699.

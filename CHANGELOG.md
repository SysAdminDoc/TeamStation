# Changelog

All notable changes to TeamStation are documented here. Format loosely follows [Keep a Changelog](https://keepachangelog.com/) and [Semantic Versioning](https://semver.org/).

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

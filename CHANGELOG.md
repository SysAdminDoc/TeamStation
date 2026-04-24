# Changelog

All notable changes to TeamStation are documented here. Format loosely follows [Keep a Changelog](https://keepachangelog.com/) and [Semantic Versioning](https://semver.org/).

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

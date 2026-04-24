# Changelog

All notable changes to TeamStation are documented here. Format loosely follows [Keep a Changelog](https://keepachangelog.com/) and [Semantic Versioning](https://semver.org/).

## [0.3.0] - 2026-04-24

First public release cut. The advanced workflow content from the v0.1.2 internal / v0.2.0 / v0.2.1 waves landed on `main` but never shipped as a tagged GitHub release — v0.3.0 is that release.

Iteration 1 of the v0.3.0 cycle adds a hardening-and-coverage pass focused on security regression vectors, crypto edge cases, schema migrations, and randomized validator fuzz.

### Security

- **Device-ID validation is now ASCII-only.** `LaunchInputValidator.IdPattern` matched `\d{8,12}` which, by default in .NET, accepts every Unicode decimal digit category character — Arabic-Indic, full-width, Bengali, and others. A device ID written in those scripts would pass the validator even though the TeamViewer CLI only accepts ASCII decimal IDs, giving homograph-style obfuscation headroom. Pattern tightened to `[0-9]{8,12}`.
- **Proxy-endpoint validator now rejects argv-injection shapes in the host.** `ValidateProxyEndpoint` previously only checked that the port parsed in range, so hosts like `--ProxyIP 1.2.3.4` (embedded flag + space) or `\\attacker\share` were accepted. Host part is now subject to the same forbidden-character and forbidden-substring rules as passwords plus the leading-dash guard.
- **CVE-2020-13699 regression vectors.** New pinned test suite (`CveRegressionTests`) drives curated argv-injection shapes against `LaunchInputValidator`, `CliArgvBuilder`, and `UriSchemeBuilder`: `--play` smuggling, `\\UNC` prefixes, colon/slash/backslash splitters, whitespace-encoded exploits, leading-hyphen probes, mixed-case variants, and non-ASCII digit ID homographs. Any regression fails CI before code review.

### Tests

- **`LaunchInputValidator` fuzz.** 10k randomized-draw fuzz run per test invocation asserting the contract invariant: every rejection surfaces as `LaunchValidationException`, never an unhandled framework exception (`IndexOutOfRange`, `NullReference`, `Regex` engine quirks).
- **Crypto edge cases.** Empty-string, null-byte-embedded, maximum-length (512 KiB), surrogate-pair, and invariant-culture plaintexts round-trip intact. Tamper-probe on each ciphertext byte independently still trips `CryptographicException`.
- **Schema migration with malformed rows.** Synthetic v1 database whose rows carry out-of-range enum integers and trailing-whitespace IDs; v1 → v3 migration rescues every recoverable row without a silent data drop.

### Changed

- Version cut from `0.2.1` to `0.3.0`. No code path regresses; this is the first external GitHub release of the cumulative workflow feature set.

## [0.2.1] - 2026-04-24

### Fixed
- **JSON backup was lossy.** Export/import silently dropped `ProfileName`, `IsPinned`, `TeamViewerPathOverride`, Wake-on-LAN fields, and pre/post-launch scripts on entries, plus `DefaultTeamViewerPath`, `DefaultWakeBroadcastAddress`, and launch scripts on folders. Backup format bumped to `2`; v1 files still parse with sane defaults.
- **Cloud mirror could produce corrupt SQLite copies.** Replaced `File.Copy`-then-copy-sidecars with a read-only `VACUUM INTO` snapshot written to a staging path and atomically renamed over the mirror. The mirror is now a single self-consistent file with no WAL/SHM residue.
- **Sub-VM split left proxy properties stale.** `LogSummary`, `ActivityButtonText`, `SavedSearches`, `HasSavedSearches`, and quick-connect fields were bound to proxies on `MainViewModel` that never rebroadcast sub-VM `PropertyChanged`. Added explicit forwarding for every proxy.
- **`AutoScrollBehavior` accumulated collection-changed subscriptions** each time a `ListBox` reloaded, causing N scroll-to-end invocations per log append. Rewritten to subscribe exactly once per control and re-bind when `ItemsSource` swaps.
- **Settings writes were not atomic.** `SettingsService.Save` now writes to a sibling temp file and renames over the target, and `Load` surfaces parse errors via `LastLoadError` and quarantines the bad file so the user sees a warning instead of silently losing saved searches.
- **Mutex release threw on abnormal exit.** Tracked `_ownsSingleInstanceMutex` so `ReleaseMutex` only runs when the instance actually owns it, removing the swallowed `ApplicationException` from the already-running exit path.
- **Tray menu leaked `ToolStripMenuItem`s on rebuild.** `DisposeMenuItems` removes and disposes each item before rebuilding.
- **Session history and audit log grew unbounded.** Added `Prune(TimeSpan)` on both repositories, wired to a `HistoryRetentionDays` setting (default 90 days), run once at startup.
- **`TeamViewerCloudSyncService` mutated shared default auth headers** and inherited the 100s `HttpClient` default timeout. Now attaches auth per `HttpRequestMessage`, honours a `CancellationToken`, and ships with a 20s default timeout.
- **`EntryRepository.Materialize` allocated twice per row.** Single-pass construction using the `init`-only timestamp fields directly.
- **Clipboard-password mode could leave the password on the clipboard** if `Launch` threw or failed — even though no session was started. The clipboard is cleared eagerly on every failure path; the 30s auto-clear still runs on successful launches so the user has time to paste into the TeamViewer prompt.
- **Audit-log ordering was not deterministic** for events sharing a millisecond. Added a secondary sort on `id`.
- **Entry/folder editors trimmed passwords on save.** Trimming is data-lossy for credentials that legitimately contain whitespace; the validator now surfaces a clear error instead.

### Changed
- `CloudMirrorService` moved from `TeamStation.App.Services` to `TeamStation.Data.Storage` so it can be tested in isolation.

### Added
- xUnit coverage for `CloudMirrorService` (valid SQLite output, atomic replace), session/audit pruning, `SettingsService` round-trip, atomic writes, and corrupt-file quarantine, plus a full-field `JsonBackup` round-trip test. Suite at 131 tests.

## [0.2.0] - 2026-04-24

### Security
- Portable-mode master-password KEK now derives via **Argon2id** (time=3, memory=64 MiB, parallelism=2) instead of PBKDF2-SHA256. New envelopes are tagged `argon2id_v1` in the database's `_meta` table.
- Legacy PBKDF2-SHA256 envelopes (`pbkdf2_v1`, implicit on upgrade from v0.1.x) still unlock and are opportunistically re-wrapped to Argon2id with a fresh salt on the next successful unlock.
- Optional **clipboard-password launch mode** (`Settings → Launch without password on the command line`). When enabled, TeamStation copies the password to the clipboard and launches TeamViewer without `--PasswordB64` on the argv. The clipboard is auto-cleared 30s later, provided it still contains that password. Reduces the command-line-disclosure window on shared or hostile multi-user hosts.

### Changed
- `MainViewModel` split into focused collaborators: `LogPanelViewModel` (activity log), `QuickConnectViewModel` (quick-connect bar + command), `SearchViewModel` (search + saved searches with `SearchTextChanged`/`SavedSearchApplied`/`SavedSearchAdded` events). Legacy binding paths on `MainViewModel` still resolve — XAML bindings unchanged.
- Introduced `IDialogService` + `WpfDialogService`. The six `Func<Window?, …>` constructor delegates on `MainViewModel` collapsed to a single injectable interface.
- `WakeOnLanService`, `ExternalToolRunner`, `TeamViewerCloudSyncService`, `CloudSyncResult`, and `ExternalToolDefinition` moved from `TeamStation.App` into `TeamStation.Core`. The WPF host keeps UI glue; everything testable or portable now lives in `TeamStation.Core`.

### Added
- `.github/workflows/ci.yml` builds and tests every push and pull request to `main`, uploading `.trx` results as an artifact.
- `docs/release-runbook.md` — operator checklist covering the launch-feasibility spike, version sync, publish smoke-test, and GitHub Actions release trigger.
- `docs/screenshots/` folder with a capture guide and the required screenshot set. README gains a **Screens** section that points at it.

### Removed
- `tests/TeamStation.Tests/UnitTest1.cs` default xUnit scaffolding.

## [0.1.2] - 2026-04-23 (internal)

### Added
- App settings window for TeamViewer.exe override, TeamViewer Web API token, Wake-on-LAN, cloud mirror folder, saved searches, and external tools.
- TeamViewer Web API token is stored as a DPAPI-protected settings value rather than plain text.
- First-run trust notice that states TeamStation launches the official unmodified TeamViewer client and stores credentials locally.
- Portable-mode master-password unlock. Portable databases use a PBKDF2-SHA256-derived AES-GCM wrapper for the data-encryption key instead of user/machine-bound DPAPI.
- Quick connect bar with optional save, saved-search chips, per-entry profile names, pin/unpin, and a dynamic tray menu with pinned and recent connections.
- TeamViewer local history import from `%AppData%\TeamViewer\Connections*.txt`.
- Optional TeamViewer Web API group/device pull into a synthetic `TV Cloud` folder.
- Wake-on-LAN pre-launch support plus inherited TeamViewer.exe path, wake broadcast, pre-launch script, and post-launch script defaults.
- External tools with `%ID%`, `%NAME%`, `%PASSWORD%`, `%PROFILE%`, `%TAG:key%`, and `${ENV_VAR}` expansion.
- Session history table, CSV session export, and persistent audit-log table.
- Optional encrypted SQLite database mirror to a selected cloud sync folder after changes.
- Optional Authenticode signing in the release workflow when certificate secrets are configured.

### Changed
- SQLite schema moved to v3 for connection profiles, pins, launch overrides, Wake-on-LAN, scripts, session history, and audit log storage.
- The system tray now rebuilds its menu from live app state and can launch pinned/recent connections directly.
- The quick-connect password field now uses a `PasswordBox` rather than visible text.

### Tests
- Expanded the xUnit suite from 109 to 118 tests, covering portable master-password unlock, v3 persistence, TeamViewer history import, session CSV export, audit-log ordering, and inherited launch-path/script fields.

## [0.1.1] - 2026-04-23

Principal-engineer hardening pass on the MVP surface. Every change sits behind a test where practical.

### Fixed
- **Folder move: sibling targets were unreachable.** `FolderPickerDialog` was filtering whole root-level subtrees whenever they *contained* the moved folder — which meant moving `Customer A / Downtown site` to a sibling of `Downtown site` was impossible because the entire `Customer A` root got hidden. Replaced with a picker-specific `PickerFolderItem` tree that prunes only the moved folder's own subtree, so siblings remain valid targets. ([FolderPickerDialog.xaml.cs](src/TeamStation.App/Views/FolderPickerDialog.xaml.cs))
- **CSV header matching missed spaced headers.** `CsvImport.Normalize` kept underscores but stripped spaces, so a file with columns `TeamViewer ID` / `Friendly Name` didn't match the `teamviewer_id` / `friendly_name` aliases. Normalisation now strips all non-alphanumerics on both sides, so `TV-ID`, `TV_ID`, `TV ID`, and `TVID` all collapse to `tvid`.
- **JsonBackup crashed on hand-edited backups.** Parse now tolerates null / missing `folders` / `entries` arrays, camelCase or PascalCase keys, and malformed JSON (surfaces a clear `InvalidDataException` instead of propagating `JsonException`).
- **Dangling parent references tripped the foreign-key constraint on import.** Both JSON and CSV import now null out any `ParentFolderId` that doesn't resolve to an existing folder (in-DB or in-import). The entry / folder lands at root instead of aborting the import.
- **Non-atomic JSON export could truncate on crash.** Export writes to a sibling temp file and does an atomic `File.Move(overwrite: true)`; temp file is cleaned up on failure.
- **Legacy `TeamViewer/Version*` folders sorted lexically.** `Version9` would rank above `Version10` under `StringComparer.OrdinalIgnoreCase`. Replaced with numeric parsing of the version suffix so the newest install wins.
- **Two instances could race on the SQLite database.** Added a `Local\` mutex guard; the second instance surfaces a friendly notice and exits.
- **Tray context menu leaked GDI resources.** `TrayManager` now owns and disposes the `ContextMenuStrip`, unsubscribes `StateChanged` cleanly on exit, and guards each dispose in try/catch so one failure doesn't leave the icon stuck.
- **Folder accent colour recomputed on every binding read.** `FolderNode.AccentBrush` now caches the parsed frozen brush; `RefreshAccent()` invalidates on edit.
- **Reparent could theoretically create a cycle** if called from code bypassing the UI guards. `MainViewModel.Reparent` now walks the new-parent's ancestor chain and rejects the move if it would land the source inside its own subtree.

### Added
- **xUnit test project** (`tests/TeamStation.Tests`) with 109 passing tests covering:
  - `LaunchInputValidator` — valid + rejected IDs / passwords / proxy endpoints, CVE-2020-13699 shapes.
  - `CsvImport` — column-alias normalisation across separator styles, quoted-field parsing, dedup, skip reporting.
  - `JsonBackup` — round-trip fidelity, null tolerance, orphan stripping, format-version guardrails.
  - `InheritanceResolver` — ancestor walk, nearest-wins ordering, cycle termination.
  - `CliArgvBuilder` — argv shape per mode, URI-only throws, Base64 password default, proxy triplet.
  - `UriSchemeBuilder` — scheme per mode, URL-encoding, CLI/URI capability matrix.
  - `CryptoService` — DEK persistence, nonce uniqueness per call, tag-tamper detection, unicode + long inputs.
  - `DatabaseIntegrationTests` — real on-disk SQLite, schema bootstrap, upsert/update/delete, FK `ON DELETE SET NULL` for deleted folders, nullable-enum round-trip, tag round-trip, `TouchLastConnected` isolation.
- **`AutoScrollBehavior`** attached property — scrolls the log `ListBox` to the newest entry on add.
- **Global unhandled-exception nets** (`AppDomain.UnhandledException`, `DispatcherUnhandledException`, `TaskScheduler.UnobservedTaskException`) surface as a MessageBox instead of silent Windows Error Reporting.
- **`BindingOperations.EnableCollectionSynchronization`** on `Log` and `RootNodes` so future async writes from non-UI threads can't break the bindings.
- **Startup log diagnostics** — version, DB path, detected TeamViewer path are now emitted to the log panel on first run.
- **`AutomationProperties.Name`** on the log panel for screen-reader discoverability.

### Changed
- `MainViewModel` constructor takes optional `startupVersion` / `startupDbPath` parameters for the new diagnostics log lines.
- `JsonBackup.Options.PropertyNameCaseInsensitive = true` so camelCase hand-edited backups parse.
- Release workflow (`.github/workflows/release.yml`) no longer runs an extra `dotnet build` before publish (publish auto-builds); argv for `gh release create` is now an array so an empty `--prerelease` flag never leaks as a literal `""` argument; `fetch-depth: 0` so tag pushes work.

### Notes
- No functional changes in shipped behaviour beyond the above fixes. DB schema is unchanged from v0.1.0 (v2). No migration needed.

## [0.1.0] - 2026-04-23

**First MVP release.** Every feature promised by the P0 section of `ROADMAP.md` is in. TeamStation now has everything a sysadmin needs to replace the built-in TeamViewer contact list, plus a few things that list never had.

### Highlights
- **Nested folder tree** with drag-to-reorder, self-subtree drop rejection, folder accent colors (validated `#RRGGBB`), unified Rename / Move / Delete that dispatches by node type.
- **Runtime inheritance cascade.** Entries can defer their mode, quality, access-control, and password to the nearest ancestor folder. Changing a folder default propagates to every inheriting entry on the next launch — no bulk editing needed.
- **CVE-2020-13699-hardened launcher.** Numeric-ID regex, password denylist (`\`, `/`, `:`, whitespace, `--`, leading `-`, `\\UNC`, `--play`, `--control`, `--Sendto`), argv-array `Process.Start` only, length-bounded params. The CLI + URI fallback matrix picks the right mechanism per mode (`--mode` only supports `fileTransfer` / `vpn`, so Chat / Video / Presentation route through their URI handlers).
- **DPAPI-wrapped AES-256-GCM credential storage.** 256-bit random DEK is DPAPI-wrapped under `CurrentUser` scope and stored in the local SQLite database; every password field is AES-256-GCM encrypted with a fresh 96-bit nonce.
- **Debounced multi-field search/filter.** Matches on name, TeamViewer ID, notes, tags, and folder names. Folders with a matching descendant stay visible and auto-expand.
- **CSV import** that accepts TeamViewer Management Console, Remote Desktop Manager, mRemoteNG, and hand-rolled spreadsheets via flexible column aliases; missing folders are created, non-numeric rows are skipped with line-referenced reasons surfaced to the log.
- **JSON backup** round-trips the full graph (folders, entries, proxy settings, all metadata) with tested fidelity.
- **Embedded log panel** (rolling 500 entries, severity-coloured) and **system tray** (minimize-to-tray, Show / Exit menu).
- **Portable mode** — drop a `teamstation.portable` marker next to the exe and the database lives beside it instead of under `%LocalAppData%`.

### Verified
- Clean Release build, 0 warnings / 0 errors across all five projects.
- Self-contained single-file publish (`win-x64`, ~189 MB) launches cleanly and shuts down without leaks.
- v1 → v2 schema migration preserves data when upgrading from an older build.
- CSV round-trip + JSON round-trip both confirmed via harness runs.

### Known open items
- The two feasibility spikes from `ROADMAP.md` have not yet been run against a real TeamViewer peer. The launcher design already accounts for both plausible outcomes (silent CLI launch or fallback to URI-handler prompting), but a real-world run via [`tools/TvLaunchSpike`](tools/TvLaunchSpike/Program.cs) is recommended before wider deployment.

### Release artifacts
- `TeamStation.exe` — self-contained single-file build, no .NET install required
- `TeamStation-v0.1.0-win-x64.zip` — the same exe plus LICENSE, README, CHANGELOG

## [0.0.8] - 2026-04-23

### Added
- **CSV import** (`TeamStation.Core/Serialization/CsvImport`).
  - RFC 4180 parser handles quoted fields, embedded commas and newlines, and escaped quotes.
  - Flexible column-name matching: `name` / `alias` / `friendly_name` / `host` / `computer_name`; `teamviewer_id` / `tv_id` / `remotecontrol_id` / `id` / `device_id`; `group` / `folder` / `category` / `parent`; `password` / `pw`; `notes` / `description` / `comment`; `tags` / `labels`. Case-insensitive, underscore-tolerant.
  - Folders referenced in the `group` column are created on-the-fly (deduped both against the existing tree and within the same import batch).
  - Non-numeric TeamViewer IDs are skipped with a line-referenced reason surfaced in the log.
  - Results flow through the regular repositories so every password is re-encrypted by the `CryptoService`.
- **Embedded log panel.** Catppuccin-styled collapsible panel at the bottom of the window shows a rolling 500-entry log of info / success / warning / error events. Toggled via the **Log** button in the toolbar. Every `ReportStatus` call pushes a timestamped `LogEntry` so the status bar stays a one-liner while history accumulates.
- **System tray icon.** `TrayManager` owns a `NotifyIcon` (WinForms, included with the Windows Desktop SDK once `UseWindowsForms=true`). The tray icon is rendered at runtime from a small dark-blue bitmap so the repo ships without a committed `.ico`. Minimising the window hides it to tray; left-click restores; right-click offers **Show TeamStation** and **Exit**.

### Changed
- `MainViewModel` routes all user-visible status updates through a new `ReportStatus(LogLevel, string)` helper so the status bar and the log panel stay in sync. Save / launch / move / import / export paths now emit appropriately-severed entries (`Success`, `Warning`, `Error`) rather than plain info text.
- Toolbar split: the single **Import** button became **Import JSON** + **Import CSV**.

### Infrastructure
- `TeamStation.App.csproj` enables `UseWindowsForms=true`. Implicit usings for `System.Windows.Forms` and `System.Drawing` are removed via `<Using Remove="…" />` to keep WPF's `Brush` / `Point` / `TreeView` / `ComboBox` / `Application` / `MouseEventArgs` / `DragEventArgs` / `TreeNode` as the unambiguous defaults. The WinForms analyzer warning WFO0003 (DPI-in-manifest) is suppressed because this is a WPF-first app where the manifest-declared PerMonitorV2 awareness is correct.

### Verified
- Clean Release build, 0 warnings / 0 errors across all five projects.
- **CSV parse harness** seeded a 4-row CSV with inconsistent column casing (`Group`, `Alias`, `TeamViewer_ID`, `Password`, `Notes`, `Tags`) and mixed content: two entries under "Customer A", one under "Lab", one with an empty group. Parser produced exactly 2 folders (deduped) and 4 entries, with passwords / notes / tags preserved and the unassigned row correctly routed to root.
- App boots cleanly with the new toolbar layout, tray icon registration, and log panel — no startup exceptions.

### Notes
- `v0.1.0` is now a single task away: running `TvLaunchSpike` against a real TeamViewer peer to close the two open feasibility questions (`--PasswordB64` silent-launch behavior, URI-handler param survival post-CVE-2020-13699).

## [0.0.7] - 2026-04-23

### Added
- **Search / filter on the tree.** New search box in the toolbar with 150 ms input debounce. Filter matches on entry name, TeamViewer ID, notes, tags, and folder names. Folders are kept visible whenever any descendant matches, and auto-expand so the match is reachable without extra clicks. Empty query restores the full tree.
  - `TreeNode.IsVisible` drives `TreeViewItem.Visibility` via a `BooleanToVisibilityConverter`.
  - `MainViewModel.ApplyFilter()` runs a post-order DFS per root, so folder visibility tracks *current-subtree* matches without touching untouched branches.
  - `ClearSearchCommand` resets in one click.
- **JSON backup.** `TeamStation.Core/Serialization/JsonBackup` serializes the full folder + entry graph (and proxy settings) using `System.Text.Json` with `JsonStringEnumConverter`, pretty-printed, nulls omitted.
  - **Export** prompts for a path via `SaveFileDialog`; warns before writing if any password field would appear in plaintext; writes a versioned envelope (`formatVersion`, `exportedAtUtc`, `folders`, `entries`).
  - **Import** prompts via `OpenFileDialog`, parses, counts rows, asks for explicit confirmation ("existing rows with matching IDs will be overwritten"), and upserts through the same repository path so crypto re-encrypts each password.

### Verified
- Clean Release build, 0 warnings / 0 errors.
- **Round-trip test:** a one-off harness seeded 1 folder (with accent `#F9E2AF`, default password, default mode) and 2 entries (one with null mode + concrete password, one with `FileTransfer` mode + tags), called `JsonBackup.Build`, wiped the DB, then called `JsonBackup.Parse` and re-upserted. All fields — accent color, default password, tags, null-vs-concrete mode, plaintext password — survived intact.
- App boots cleanly with the new toolbar layout.

### Notes
- Import overwrites rows with matching IDs. If you want to merge two databases that share IDs (e.g. from a past clone), export one, hand-edit the IDs, then import.
- `v0.1.0` still gating on CSV import (TeamViewer Management Console export format), tray with recent connections, embedded log panel, and a real-peer run of `TvLaunchSpike`.

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

# Changelog

All notable changes to TeamStation are documented here. Format loosely follows [Keep a Changelog](https://keepachangelog.com/) and [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Security

- **HMAC-SHA256 integrity chain on `audit_log` rows.** Schema v4 adds `prev_hash BLOB` and `row_hash BLOB` to `audit_log`. Every new row carries `row_hash = HMAC-SHA256(key=DEK, prev_hash ‖ row_data)` so a verifier can detect retroactive insertion, deletion, or modification without additional key material — the existing 256-bit AES-GCM DEK doubles as the HMAC key. The chain is verified via `TeamStation.exe --verify-audit-chain` (attaches to the parent console, exits 0/1, prints a one-line summary). Rows written before the schema-v4 migration carry NULL hashes and are counted as skipped legacy rows; they do not invalidate newer rows. `CryptoService.ComputeHmac(ReadOnlySpan<byte>)` added. `AuditLogRepository` accepts an optional `CryptoService?`; legacy callers continue to work with `crypto = null`. New `AuditChainVerificationResult` record in `TeamStation.Core.Models`.
- **DEK rotation (Settings UI)** — see previous entry.

### Changed

- **SQLite maintenance upgrade.** `TeamStation.Data` now references `Microsoft.Data.Sqlite` 10.0.6. `Database.OpenConnection()` returns a small optimizing connection that runs best-effort `PRAGMA optimize` during `Close` / `Dispose`, keeping the existing raw-SQL repository shape while picking up SQLite's lightweight planner-statistics maintenance.
- **Settings exposes database maintenance opt-out.** The Settings dialog now includes a default-on "Optimize SQLite planner statistics when database connections close" checkbox for operators diagnosing database issues.
- **Startup database integrity warning.** Startup now runs `PRAGMA integrity_check` after the SQLite database opens and logs the result before normal interaction begins. A clean vault records an info entry; failed or incomplete checks record a warning with the first reported SQLite messages.
- **Activity log structured export.** The activity dock now has a disabled-aware `Export` action that writes the visible transient log buffer as newline-delimited JSON (`teamstation.activity.v1`) for ELK / Splunk / Loki-style ingestion. This remains separate from the tamper-evident audit log.
- **Launch-latency histogram.** Successful launches now feed a rolling 50-sample latency view in the activity dock: bucketed time-to-`Process.Start`, p50 / p95, and the last credential-read and session-history write timings.

### Tests

- Added focused coverage for the new settings default / persistence, SQLite integrity report API, startup wiring, structured activity-log export, launch-latency rollups, and a SQLite smoke that disposes an optimized connection, reopens the vault, and verifies `PRAGMA integrity_check` returns `ok`.

## [0.3.5] - 2026-04-25

Three-task patch on top of v0.3.4: completes the byte[] launch-path wiring v0.3.4 introduced, ships TeamViewer client-version detection + an "Update available" pill in the status bar (operator remediation guidance for CVE-2026-23572), and lays down bulk multi-select infrastructure on the connection tree with Bulk Pin / Bulk Unpin as the first commands.

### Security

- **`MainViewModel.LaunchEntry` is plumbed to the byte[] launcher overload** that v0.3.4 introduced. `EntryRepository.LoadEntryPasswordBytes(source.Id)` and `LoadEntryProxyPasswordBytes(source.Id)` are loaded immediately before `_launcher.Launch(launchTarget, pwBytes, proxyPwBytes, options)` runs; the launcher zeros the buffers via `try/finally + CryptographicOperations.ZeroMemory` after argv has been handed to `Process.Start`. Clipboard-password mode (`ShouldUseClipboardPasswordMode`) bypasses the byte path entirely — clipboard staging and argv-password are mutually exclusive by design. Folder-default-inheritance launches (`source.Password` is null but `effective.Password` came from a folder default) fall back to the legacy string path automatically. Closes the v0.3.4 deferred follow-up.

### Added

- **TeamViewer client-version detection + "Update available" status pill.** New `TeamStation.Launcher.TeamViewerVersionDetector` reads the version from `HKLM\SOFTWARE\TeamViewer\Version` (with `WOW6432Node` mirror and `HKCU` fallback), then falls back to `System.Diagnostics.FileVersionInfo` on the resolved `TeamViewer.exe`. The status bar shows the parsed version (`TeamViewer 15.71.5`) as a small monospace chip; below 15.74.5 a yellow "Update available" pill appears with a tooltip explaining CVE-2026-23572 (TeamViewer auth-bypass, CVSS 7.2, fixed in 15.74.5+). Refreshed at startup and after Settings save. Decoupled from the registry via `ITeamViewerVersionSource` so unit tests can pump fakes. Iter-2 P2 closed.
- **Bulk multi-select infrastructure on the connection tree.** WPF TreeView has no native multi-select; v0.3.5 ships the foundation. `TreeNode.IsMultiSelected` flag with `INotifyPropertyChanged`. `MainWindow.xaml.cs` `Tree_PreviewMouseLeftButtonDown` accumulator: plain click clears the multi-selection and lets the WPF default selection take over (single-select / double-click-launch / arrow-key-nav unchanged); Ctrl+left-click toggles `IsMultiSelected` on the clicked node. `MainViewModel.SelectedNodes` flattens the tree and returns the multi-selected nodes; `MultiSelectedEntryCount` and `IsBulkSelectionActive` drive context-menu visibility. `Bulk Pin (N)` and `Bulk Unpin (N)` context-menu items operate on the selection (only the `EntryNode` subset; folders multi-select for visual feedback but bulk-pin filters via `OfType<EntryNode>`). Multi-select state is cleared on `Reload()` so node-identity churn cannot leak references to nodes no longer in the tree. Visual highlight: multi-selected rows use the same `BlueSoftBrush` background and `BlueBrush` border as `IsSelected`. Sets the pattern for `BulkSetTag` / `BulkSetProxy` / `BulkSetMode` in v0.3.6. P1, top r/sysadmin migration friction point per iter-1 research.

### Tests

- **`TeamViewerVersionDetectorTests` (12 cases — `[Theory]` + `[Fact]` parameter expansion).** Pins the parser + threshold: `TryParseVersion` accepts well-formed TeamViewer version strings (3-component and 4-component) including surrounding whitespace, rejects null / empty / "v"-prefixed / non-version inputs, `Detect` returns null when the source returns null and otherwise returns the parsed `Version`, `NeedsUpdate` correctly thresholds 15.74.4 as needing update / 15.74.5 as safe / 16.0.0 as safe / null as not-needing-update (so the pill never lights when TeamViewer isn't detected — the chip's "TeamViewer not detected" message handles that). `MinimumSafeVersion` constant pinned to `Version(15, 74, 5)` — any future shift surfaces deliberately.
- **`BulkMultiSelectTests` (5 cases).** Pins the testable surface of the bulk-select infrastructure: `TreeNode.IsMultiSelected` defaults to false; setter raises `INotifyPropertyChanged.PropertyChanged` for the `IsMultiSelected` name; idempotent assignment does NOT raise (consumers of `INotifyPropertyChanged` won't see spurious notifications); `IsMultiSelected` is independent of `IsSelected` (single-select can coexist with multi-select); the flatten-walk that `MainViewModel.SelectedNodes` uses correctly collects only multi-selected nodes across nested folders; folder multi-select is allowed (the visual highlight applies; bulk-pin filters via `OfType<EntryNode>` so folders are no-ops for that command, which is the contract).

Test count: 398 → 422.

## [0.3.4] - 2026-04-25

Credential-handling patch closing the two related items that v0.3.3 deferred. Both extend the v0.3.3 DPAPI entropy story: the API-token surface gets the same per-database salt the DEK already uses, and the launch hot path moves to a byte[] credential-read API so the password lives in a zeroable buffer instead of a CLR-interned string.

### Security

- **`AppSettings.TeamViewerApiToken` DPAPI wrap binds to the per-database entropy salt** — same `_meta.dpapi_entropy_v1` row `CryptoService` already uses for the DEK. The architectural blocker noted in the v0.3.3 continuation brief was that `SettingsService.Load` runs BEFORE the `Database` is opened in `App.OnStartup`, so the salt isn't in scope at Load time. The fix is lazy `UnprotectApiToken`: `SettingsService.Load` leaves `AppSettings.TeamViewerApiTokenProtected` in place but no longer eagerly unwraps it; the host pushes the salt in via `SettingsService.Entropy` after `Database` opens, then calls `SettingsService.UnprotectApiToken(settings)`. Subsequent `Save` calls use the salt-bound wrap. Existing v0.3.3-and-earlier null-entropy wraps fall back transparently and re-wrap under the salt on the next `Save`. Closes the long-standing `AppSettings` entropy follow-up.
- **byte[] credential-read API on the launch hot path.** `CryptoService.EncryptBytes(byte[]?)` and `CryptoService.DecryptToBytes(byte[]?)` are new public methods that route directly between byte buffers without an intermediate `System.String`. `EntryRepository.LoadEntryPasswordBytes(Guid)` and `LoadEntryProxyPasswordBytes(Guid)` return fresh zeroable byte buffers fetched directly from the encrypted columns. A new `CliArgvBuilder.Build(entry, byte[]? passwordBytes, byte[]? proxyPasswordBytes, bool base64Password)` overload uses the byte[] buffers as the source of `--PasswordB64` / `--ProxyPassword` (`Convert.ToBase64String(byte[])` skips the UTF-8 string round-trip entirely). A new `TeamViewerLauncher.Launch(entry, byte[]? passwordBytes, byte[]? proxyPasswordBytes, options)` overload zeros the input buffers via `CryptographicOperations.ZeroMemory` immediately after argv has been handed to `Process.Start` — the `try/finally` is at the public-API boundary so even validation throws and resolver failures still zero the buffers. Closes the v0.3.0 postflight `System.String credential leak` finding for the launch path; UI bindings on `PasswordBox` / `TextBox` keep using `System.String` because the binding layer cannot zero CLR-interned strings (refactoring those would be performative).

### Changed

- **`CryptoService.EncryptString` / `DecryptString` are now compatibility shims that internally route through the byte[] path** AND zero the intermediate UTF-8 buffer via `CryptographicOperations.ZeroMemory` once the wrap has been produced (Encrypt) or the string has been materialised (Decrypt). Behaviour is identical to v0.3.3; the only difference is the brief allocation of an extra byte buffer that's now zeroed on completion instead of left for the GC to reclaim.

### Tests

- **`AppSettingsEntropyTests` (6 cases).** Pins the entropy hardening: `Load` no longer eagerly unwraps the token, `UnprotectApiToken` round-trips a freshly saved token under the salt, raw `ProtectedData.Unprotect` succeeds with the salt and FAILS with null entropy (proves the new wrap is salt-bound), legacy null-entropy wraps unwrap via the fallback path, the next `Save` re-wraps under the salt, the unwrap is a no-op when no protected blob is present, and a token encrypted under different entropy returns null instead of throwing (matches the "settings file copied between machines" UX expectation).
- **`CredentialByteApiTests` (10 cases).** Pins the byte[] credential-read API: `EncryptBytes` / `DecryptToBytes` round-trip; `EncryptBytes` and `EncryptString` produce cross-decryptable wraps (same wire format); `DecryptToBytes` returns a fresh buffer the caller can zero without affecting the wrap or other callers; null inputs handled; `EntryRepository.LoadEntryPasswordBytes` round-trips against `Upsert`; absent / unknown id returns null; `LoadEntryProxyPasswordBytes` round-trips; `CliArgvBuilder.Build` byte-overload emits `--PasswordB64` from bytes (and the string fallback is ignored when bytes are non-null); the byte path validates the password through the same `LaunchInputValidator.ValidatePassword` predicate; the proxy-password byte path emits `--ProxyPassword` correctly.
- **`TeamViewerLauncherZeroingTests` (4 cases).** Pins the security-relevant property of the new launch path: the input byte[] buffers are zeroed even on the failure path (path-resolver returns null), only the main-password buffer is provided, both buffers are null (no NRE), and validation throws (leading-dash password) — in every case the byte[] is all-zero on return. The `try/finally` at the Launch overload boundary means callers don't have to second-guess which exception path the launcher took.

Test count: 378 → 398.

## [0.3.3] - 2026-04-24

Security + accessibility patch on top of the v0.3.2 visual overhaul. Two contained changes ship in this release; the System.String credential-leak refactor (P1 from v0.3.0 postflight) remains deferred to v0.3.4 because its blast radius — DecryptString, EntryRepository, FolderRepository, TeamViewerLauncher, every launch/edit UI binding — wants a release cycle of its own with explicit migration notes for downstream consumers of the credential-read surface.

### Security

- **Per-database DPAPI entropy salt for the DEK wrap.** Every `ProtectedData.Protect` / `ProtectedData.Unprotect` call in `CryptoService` now binds the wrap to a 32-byte random salt held in the same `_meta` table as the DEK itself (key `dpapi_entropy_v1`). Without that salt no DPAPI unwrap succeeds — the trust boundary moves from "same Windows user" to "same Windows user AND has read this database file". Defends against opportunistic malware that scrapes DPAPI blobs in bulk without ever opening our DB. `RotateDek` participates in the same hardening, and a one-shot legacy fallback transparently re-wraps existing v0.3.0 / v0.3.1 / v0.3.2 (null-entropy) DEKs under the new salt on first launch — no user action required.
- **CVE-2026-23572 (TeamViewer auth bypass, CVSS 7.2) operator note.** The April 2026 TeamViewer security bulletin covers a confirmation-bypass in TeamViewer Full / Host below 15.74.5. TeamStation orchestrates the installed client and does not ship the protocol implementation, so the patch lives upstream — but anyone running TeamStation against a vulnerable client should update the host installation. Landscape research log: `docs/research/iter-2-sources.md`.

### Added

- **Keyboard navigation on the connection tree (A11y baseline).** The folder TreeView now exposes Enter / F2 / Delete as single-key actions on the focused item — Launch, Rename, Delete respectively. Matches Explorer + VS Code conventions. Per the project "no keyboard shortcuts" rule there are NO chord (Ctrl/Alt/Shift) bindings; arrow-key + Home / End navigation continues to work via the WPF default handler. The tree carries `KeyboardNavigation.TabNavigation="Once"` so Tab steps over the entire tree as a single stop instead of trapping keyboard users on every visible TreeViewItem.
- **AutomationProperties.Name + HelpText** on the connection tree and the search box so screen readers announce them with intent rather than as anonymous controls.

### Tests

- **`CryptoEntropyTests` (9 cases).** Pins the DPAPI entropy hardening: salt seeded on first run, salt persisted across `CreateOrLoad` calls, raw `ProtectedData.Unprotect` succeeds with the persisted salt and fails with null entropy / wrong entropy (proves the wrap is in fact entropy-bound), legacy null-entropy wraps load and are silently re-wrapped under the new salt, second load takes the fast path (no further re-wrap), `RotateDek` preserves entropy across rotation, `RotateDek` creates entropy on a legacy install, the `IKeyStore`-only legacy stub keeps working with null entropy (graceful no-op), and a master-password carry-over from a legacy DPAPI install brings the existing DEK forward.
- **`MainWindowKeyboardNavTests` (5 cases).** Pins the A11y baseline structurally (XAML parsed as XML, no WPF runtime needed): three single-key bindings on the tree, exact key set is `{Enter, F2, Delete}`, no `Modifiers` attribute on any of them (regression guard against the project's "no keyboard shortcuts" rule), each key maps to its expected command (Enter→Launch, F2→Rename, Delete→Delete), tree has an `AutomationProperties.Name`, search box has an `AutomationProperties.Name`, and `KeyboardNavigation.TabNavigation="Once"` is in place to avoid the tab-trap.

Test count: 364 → 378.

## [0.3.2] - 2026-04-24

UI overhaul. Reshapes the workspace around the mRemoteNG / Visual Studio sysadmin-tool layout users expect from a TeamViewer connection manager: classic top menubar, compact icon toolbar, slim quick-connect strip, 2-pane split with a categorised property-grid inspector, dockable activity log, and a single-line status bar. No view-model or behaviour changes — the entire pass is visual + accessibility.

### Changed

- **Main window — mRemoteNG-style workbench.** Top-of-window menubar (File / Edit / View / Connection / Tools); 17 equal-weight toolbar buttons collapsed into four semantic groups (Connection, Organization, Data, System) with vertical dividers between them; each toolbar button is a Segoe Fluent Icons glyph plus a tiny label. Right-anchored status cluster shows the TeamViewer-ready chip and library counter. Quick-connect compresses from a 4-row hero header into one horizontal strip (ID / Name / Password / Save / Connect + Search). 2-pane split with a `GridSplitter` so the sidebar is no longer locked at 410 px.
- **Inspector — property-grid layout.** Selection renders as categorised sections (BASICS, LAUNCH BEHAVIOR, OPERATIONAL, NOTES, EXTERNAL TOOLS, SECURITY for connections; STRUCTURE, INHERITED DEFAULTS, APPEARANCE, HOW INHERITANCE WORKS for folders). Alternating Surface0 row shading, 180 px labels, MonoText for technical values (TeamViewer ID, route, proxy, TeamViewer.exe path, accent hex). Hero strip at the top carries the display name, breadcrumb, colour-coded chips, and a Launch primary + icon-only Edit / Move / Pin / Delete actions.
- **Tree rows.** Folder rows now show a Fluent folder glyph tinted with the folder's accent colour; entry rows show a Fluent connection glyph + monospace TeamViewer ID + tag summary, with a small route chip + pin icon on the right.
- **Empty + selection states.** Welcome, no-results, and selection placeholders gained a Fluent-icon anchor, `HeadlineText` title, human copy, and a clear single primary action plus a Ghost secondary. They feel like designed moments instead of blank panes.
- **Log panel.** Dockable at 240 px height; level badges became 999-radius capsules with uppercase mono tags; 11 px mono timestamp in Subtext0; header gained a Fluent activity glyph + Clear button and an icon-only Hide.
- **Status bar.** Dropped from 3 rows to one slim row: status pill (semantic colour) + message on the left, DB location (MonoText) + version chip on the right. No redundant status dot, no duplicated search hint.
- **Chips.** All chips standardise on a 999-radius pill shape with 10 x 4 padding — header chips, toolbar status chips, tree badges, log-level tags. No more mismatched 4 px squares.
- **Dialogs (Entry editor, Folder editor, Settings, Master password, Folder picker, Input).** Hard-coded 24-28 px SemiBold titles replaced with the shared `DisplayText` / `HeadlineText` styles; every dialog announces itself with a tiny Fluent-icon + uppercase caption above the title (CONNECTION / FOLDER / PREFERENCES / PORTABLE MODE / MOVE). Card radii dropped from 16 to 10 px and padding tightened to 22 x 20 to match the main window's 8 px panel language. Validation banners lead with a Fluent warning glyph and an 8 px radius. Settings reorganised into five categorised section cards (Appearance, TeamViewer, Launch safety, Backup &amp; search, External tools); the external-tools editor switched to MonoText so the `Name|Command|Arguments` lines align cleanly. Folder-picker tree adopted the Fluent folder glyph and the same hover / select state vocabulary as the main tree.

### Added

- **Typography scale.** New `DisplayText` (30 pt SemiBold, Variable Display), `HeadlineText` (22 pt), and `MonoText` (Cascadia / JetBrains Mono / Consolas) styles complement the existing Caption / SectionTitle / SectionSubtitle / FieldLabel / BodyMuted ladder. Body sizes use Segoe UI Variable Text; 22 pt+ uses Variable Display so larger copy stays optically tight.
- **Custom CheckBox template.** Replaces the beige Win9x-inset default with a flat 18 x 18 rounded box, soft blue checked fill, a Path-stroked checkmark on check, a centred indeterminate pill, and explicit hover / focus states.
- **Custom ScrollBar template.** 10 px wide, transparent track, rounded thumb (4 px radius) that thickens from 0.55 -> 1.0 opacity on hover / drag. Zero-sized line buttons remove the arrow glyphs so the scrollbar reads as a single silent rail, matching modern platform conventions.
- **Button focus ring + variants.** Templated focus ring is invisible until the button has keyboard focus, so Tab navigation is no longer silent. New `Subtle` variant for dense toolbar zones (transparent until hover); existing Primary / Ghost / Danger / Icon variants kept and tightened.
- **`Divider` + `VerticalDivider` styles.** 1 px rules at 60 % opacity for separating toolbar groups and category boundaries without heavy borders.
- **Drop shadows on tooltips and context menus.** `HasDropShadow=True` so flyouts read as proper Windows 11 surfaces instead of bare rectangles.
- **Fatal-exception breadcrumb.** `App.ShowFatal` now best-effort-appends to `%TEMP%\teamstation-fatal.log` before showing the MessageBox, so a startup crash leaves a triage trail without blocking the dialog.

### Fixed

- **Theme switching no longer crashes when a brush gets sealed.** `ThemeManager.Set` now checks `brush.IsFrozen` before mutating and falls back to swapping in a fresh mutable brush. The new ControlTemplates reference theme-mutable brushes via `DynamicResource` so the swap is picked up by every consumer.
- **Search "X" character replaced with a real glyph.** The clear-search button now renders the proper Segoe Fluent Icons cancel glyph and gains a tooltip; previously it used the literal letter "X".
- **Status of TeamViewer client and library counter always visible** in the toolbar header even after the toolbar wraps on narrow windows; the previous chip cluster was buried behind the saved-search row.

## [0.3.1] - 2026-04-24

Focused security-hardening patch closing three of the v0.3.0 postflight audit follow-ups. No public API removed; one breaking change to the `ISecretStore` interface (new `DeleteValue` member — only relevant to downstream packagers that implemented the interface themselves).

### Security

- **`CryptoService` now implements `IDisposable`.** The unwrapped 256-bit DEK buffer is allocated pinned (`GC.AllocateArray<byte>(pinned: true)`) so a GC compaction cannot leave stale key material at a previous heap address, and `Dispose()` zeros the buffer via `CryptographicOperations.ZeroMemory`. Subsequent calls to `EncryptString` / `DecryptString` throw `ObjectDisposedException`. `App.OnExit` disposes the process-wide service so a swap-file snapshot captured after shutdown cannot recover the DEK.
- **`CryptoService.RotateDek` is now two-phase commit.** Rotation stages the new wrapped DEK under a `dek_v1_pending` tombstone, runs the migrator, atomically promotes `dek_v1` via `INSERT OR REPLACE`, then deletes the tombstone. A crash between stage and promote leaves the tombstone in place; startup surfaces the state via `CryptoService.InspectPendingRotation`. The orphan case (`pending == main`, from a post-commit crash) is auto-reconciled silently. The ambiguous case (`pending != main`, from a mid-rotation crash) refuses to auto-recover — the caller must invoke `CryptoService.ForceCommitPendingRotation` or `CryptoService.ForceRollbackPendingRotation` after inspecting the DB row state, because the key store alone can't tell whether rows were already re-encrypted under the pending DEK. Closes the save-then-migrate crash window that postflight audit flagged on v0.3.0.
- **`ISecretStore` gains `DeleteValue(string key)`.** `Database.DeleteValue` drops the `_meta` row via a single parameterised `DELETE`. Required for the rotation tombstone lifecycle; also available to future hygiene code that needs to drop a stored secret rather than overwrite it with an empty buffer.

### Changed

- **Shared atomic-write helper.** The temp-file + rename + rollback pattern previously duplicated in `SettingsService.Save` and `MainViewModel.Export` is now centralised in `TeamStation.Core.Io.AtomicFile` (`WriteAllText` + `WriteAllBytes`). Both call sites delegate to the helper so behaviour stays in lock-step and the crash tests exercise the single shared code path.

### Tests

- **`CryptoDisposalTests` (6 cases).** Pins: `Dispose()` zeros the DEK buffer in-place (reflection probe on the private `_dek` field), `EncryptString`/`DecryptString` after dispose throw `ObjectDisposedException`, `Dispose` is idempotent, DEK survives multiple GC cycles (confirms pinned allocation keeps the buffer at a stable address), `RotateDek` disposes the temporary old-service on success, and `RotateDek` disposes both services on migrator failure.
- **`RotationRecoveryTests` (14 cases).** Pins the full tombstone state machine: `InspectPendingRotation` classifies None / PendingOrphan / InterruptedMidRotation correctly, `ReconcilePendingRotation` clears orphans silently and refuses to auto-recover the interrupted state (tombstone preserved so the signal isn't lost), `CreateOrLoad` auto-reconciles orphans and refuses to open on interrupted rotations, `ForceCommitPendingRotation` promotes pending to main, `ForceRollbackPendingRotation` drops pending, and both helpers throw when no pending exists. Rotation happy-path pins: pending is staged before migrator runs, deleted after a successful promote, and deleted after a migrator throw.
- **`AtomicFileTests` (5 cases).** Pins the shared helper: rename-fails-original-intact (target-is-directory simulation), no `*.tmp` residue across repeated failures, roundtrip still works after a failed save, happy-path creates the target directory if missing, and the `WriteAllBytes` overload honours the same crash-safety contract.

Test count: 330 → 355.

## [0.3.0] - 2026-04-24

First public release cut. The advanced workflow content from the v0.1.2 internal / v0.2.0 / v0.2.1 waves landed on `main` but never shipped as a tagged GitHub release — v0.3.0 is that release.

Iteration 1 of the v0.3.0 cycle adds a hardening-and-coverage pass focused on security regression vectors, crypto edge cases, schema migrations, and randomized validator fuzz.

### Security — iteration 3

- **IPv6 proxy endpoints are a first-class input.** `ValidateProxyEndpoint` used to split on `:` and reject any IPv6 literal (`[::1]:8080` became four parts). Rewritten as a bracket-aware parser: `[host]:port` for IPv6 (loopback, full, link-local with scope ID, IPv4-mapped), `host:port` for IPv4 and hostnames. Bare IPv6 without brackets is refused so the port split stays deterministic. Host argv-injection guards stay intact — the forbidden-character check skips only the `:` inside bracketed IPv6 literals, where it is syntactic.
- **`CliArgvBuilder` range-checks Quality and AccessControl.** An out-of-range int sourced from a malformed DB row used to be emitted verbatim as `--quality 55` or `--ac -7`. `Enum.IsDefined` gate now drops unknown values silently, matching the safe-default behaviour the `Mode` switch already provided.

### Security — iteration 1

- **Device-ID validation is now ASCII-only.** `LaunchInputValidator.IdPattern` matched `\d{8,12}` which, by default in .NET, accepts every Unicode decimal digit category character — Arabic-Indic, full-width, Bengali, and others. A device ID written in those scripts would pass the validator even though the TeamViewer CLI only accepts ASCII decimal IDs, giving homograph-style obfuscation headroom. Pattern tightened to `[0-9]{8,12}`.
- **Proxy-endpoint validator now rejects argv-injection shapes in the host.** `ValidateProxyEndpoint` previously only checked that the port parsed in range, so hosts like `--ProxyIP 1.2.3.4` (embedded flag + space) or `\\attacker\share` were accepted. Host part is now subject to the same forbidden-character and forbidden-substring rules as passwords plus the leading-dash guard.
- **CVE-2020-13699 regression vectors.** New pinned test suite (`CveRegressionTests`) drives curated argv-injection shapes against `LaunchInputValidator`, `CliArgvBuilder`, and `UriSchemeBuilder`: `--play` smuggling, `\\UNC` prefixes, colon/slash/backslash splitters, whitespace-encoded exploits, leading-hyphen probes, mixed-case variants, and non-ASCII digit ID homographs. Any regression fails CI before code review.

### Tests

- **`LaunchInputValidator` fuzz.** 10k randomized-draw fuzz run per test invocation asserting the contract invariant: every rejection surfaces as `LaunchValidationException`, never an unhandled framework exception (`IndexOutOfRange`, `NullReference`, `Regex` engine quirks).
- **Crypto edge cases.** Empty-string, null-byte-embedded, maximum-length (512 KiB), surrogate-pair, and invariant-culture plaintexts round-trip intact. Tamper-probe on each ciphertext byte independently still trips `CryptographicException`.
- **Schema migration with malformed rows.** Synthetic v1 database whose rows carry out-of-range enum integers and trailing-whitespace IDs; v1 → v3 migration rescues every recoverable row without a silent data drop.
- **Crypto rotation.** `CryptoRotationTests` exercises happy path (store rewrap + new ciphertext works + old ciphertext fails with tag mismatch), migrator-throws rollback (store untouched, original data still decrypts), guard paths (empty store, master-password envelope), and two-rotations-in-sequence produce three distinct wraps.
- **Atomic-write crash simulation.** `AtomicWriteCrashTests` forces `SettingsService.Save`'s rename step to fail (target path made a directory), then asserts original is untouched, no `*.tmp` sidecar residue, and that a subsequent save still lands cleanly after the obstacle is removed.
- **IPv6 proxy endpoints.** `LaunchInputValidatorTests` gains positive coverage for IPv6 loopback, full IPv6, link-local with scope ID (`[fe80::1%eth0]`), and IPv4-mapped IPv6 (`[::ffff:127.0.0.1]`). Eight malformed bracketed shapes (forbidden chars / substrings inside `[…]`, missing `:port`, empty host) and bare-unbracketed IPv6 are rejected.
- **`ValidateProxyUsername` length boundary.** Pinned at exactly `MaxProxyUserLength` (128) chars plus rejection at 129 and 256.

### Added — Feature

- **`CryptoService.RotateDek(IKeyStore, Action<old, new> migrator)`.** Break-glass path when a user suspects their DPAPI profile is compromised. Generates a fresh 256-bit DEK and drives the caller-supplied migrator so every password column across `folders` + `entries` can be decrypted under the old DEK and re-encrypted under the new one. Ordering stages the new wrap into the store *before* the migrator runs; if the migrator throws, the old wrap is restored so downstream ciphertexts keep decrypting. The old DEK buffer is `ZeroMemory`'d once rotation completes. Master-password envelopes explicitly refuse rotation — that flow requires re-prompting and deriving a fresh Argon2id salt, which is a separate UX.

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

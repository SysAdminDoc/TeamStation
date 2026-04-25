# TeamStation Roadmap

**Last research pass:** 2026-04-25 (iter-3, post-v0.3.5).
**Source basis:** iter-1 (38 sources, 160 harvested, 49 scored), iter-2 (24 net-new delta sources), iter-3 (94 net-new findings across community/commercial/adjacent/standards/dependencies). 156 distinct external sources total; full Appendix at the end.
**Cadence:** factory-loop iterations have shipped v0.3.0 → v0.3.5 with steady security + UX hardening. This document supersedes the prior v0.3.6 backlog with a unified v0.4.0 / v0.5.0 / v1.0.0 plan informed by all three research iterations.

Prioritization:

- **P0** — Ships in `v0.1.0`. The app is not usable without these. (All shipped.)
- **P1** — Ships by `v1.0.0`. Expected by anyone coming from mRemoteNG or Devolutions RDM.
- **P2** — Backlog. Valuable, but not gating adoption.
- **P3** — Speculative or low-leverage; revisit each iteration.

> Historical research entry-points (from earliest pass): official TeamViewer CLI docs, KB 34447, REACH API guide, `webapi.teamviewer.com/api/v1/docs`, mRemoteNG docs (external tools + inheritance), Devolutions RDM TeamViewer-entry docs, r/sysadmin migration threads, CVE-2020-13699 advisory, `MyUncleSam/TeamviewerExporter` CSV format. Subsequent iterations expanded to include the full Appendix at the bottom of this document.

---

## Charter (non-negotiable)

These constraints supersede every backlog item. Items that violate the charter are explicitly placed under "Rejected (with reasoning)" or "Under Consideration (CHARTER-REVIEW)" — never silently dropped.

- **TeamViewer-only.** TeamStation orchestrates the installed `TeamViewer.exe` client and does NOT implement, MITM, or relay the TeamViewer protocol. Other remote-access protocols (RDP, VNC, SSH, AnyDesk, ScreenConnect, RustDesk) are out of scope.
- **Local-first.** SQLite + DPAPI by default; portable mode wraps the DEK with an Argon2id-derived KEK. No mandatory cloud accounts. Optional encrypted DB mirror to a user-chosen sync folder is the only network-touching feature in scope, and the cloud provider never sees plaintext.
- **Windows-only at the WPF tier.** Cross-platform support exists only as an explicit Avalonia FORK post-v1.0, not a refactor. iOS / Android / macOS / Linux are charter-blocked for the WPF host.
- **No telemetry, no update pings, no phone-home.** Any future "update available" surfacing pulls from a static manifest the user opts into; no analytics, no crash beacons.
- **MIT licensed.** No freemium gate, no paid tiers, no subscription model. The free OSS tier IS the product.
- **No keyboard chord shortcuts** (per project rule). Single-key actions on focused items (Enter, F2, Delete) and platform-standard navigation (arrow keys, Tab) are A11y baseline; Ctrl/Alt/Shift chord bindings are explicitly excluded.

---

## Current main progress through v0.3.5

v0.3.0 → v0.3.5 ships the largest adoption blockers from P1/P2 plus a sustained security + UX hardening cadence:

- App settings, first-run trust notice, portable-mode master password (Argon2id), and configurable TeamViewer.exe path.
- Quick connect, saved searches, per-entry profile names, pinned entries, and pinned/recent tray launch menu.
- TeamViewer local history import plus optional read-only Web API group/device pull into a synthetic `TV Cloud` folder.
- Wake-on-LAN, folder/entry launch scripts, external tools, and inherited TeamViewer path / wake broadcast / scripts.
- Session history, CSV session export, persistent audit log storage, and optional encrypted DB mirror to a cloud folder.
- Optional Authenticode signing in the release workflow when signing certificate secrets are configured.
- Credential-handling story: pinned + zeroed DEK buffer (v0.3.1), two-phase-commit DEK rotation (v0.3.1), per-database DPAPI entropy salt (v0.3.3), shared atomic-write helper (v0.3.1), AppSettings token entropy via lazy Unprotect (v0.3.4), byte[] credential-read API on the launch hot path (v0.3.4), `MainViewModel.LaunchEntry` plumbed through to that overload (v0.3.5).
- mRemoteNG-style workbench layout — top menubar, semantic toolbar, two-pane split with property-grid inspector, dockable activity log, single-line status bar (v0.3.2).
- A11y baseline on the connection tree: Enter / F2 / Delete single-key actions, `KeyboardNavigation.TabNavigation="Once"` to avoid the tab-trap, `AutomationProperties.Name` on the tree and search box (v0.3.3).
- TeamViewer client version detection + status-bar update-available pill (v0.3.5) — surfaces CVE-2026-23572 baseline (15.74.5) without an auto-update path.
- Bulk multi-select infrastructure on the connection tree + Bulk Pin/Unpin (v0.3.5) — Ctrl-click accumulator, `TreeNode.IsMultiSelected`, `MainViewModel.SelectedNodes`, foundation for `BulkSetTag` / `BulkSetProxy` / `BulkSetMode`.

Still open before a formal 1.0 release: real-peer TeamViewer launch validation, Web API pagination/rate-limit hardening, online-state polling, conflict-aware cloud sync, signed installer packaging, per-monitor DPI hardening, immutable audit-trail UI, and UX testing on real support workflows.

---

## v0.4.0 — Now (target: 2026-Q2)

The highest-leverage P0/P1 items uncovered by iter-3 plus the open follow-ups from iter-1 / iter-2 that fit cleanly into one minor release. Theme: **enterprise readiness** — fix the per-monitor DPI bug that dominates mRemoteNG community complaints, ship the bulk-ops dialogs the multi-select infrastructure was laid down for, close the TeamViewer-version surface with a CVE registry, and start the audit-trail integrity story.

Each item carries `[fit:F impact:I effort:E risk:R deps:D novelty:N → avg]` from the six-dimension scoring rubric and a justification line.

### TeamViewer control-surface expansion ledger (active, 2026-04-25)

Goal: reduce direct `TeamViewer.exe` launch dependence where official TeamViewer-supported control surfaces are safer or less intrusive, while preserving the charter rule that TeamStation never reimplements or MITMs the TeamViewer protocol.

- [x] **Protocol-first launch preference.** Settings now lets users prefer registered TeamViewer protocol handlers for remote control, file transfer, and VPN when proxy / quality / access-control overrides do not require command-line flags. TeamStation still falls back to `TeamViewer.exe` for executable-only settings.
- [x] **Explicit "Launch via protocol link" action.** Add a per-connection command that forces the registered TeamViewer URI handler even when the global launch preference remains conservative.
- [x] **TeamViewer Web Client handoff.** Add a browser handoff to `https://web.teamviewer.com/` that copies the selected TeamViewer ID first, because official Web Client docs support connecting to TeamViewer IDs but do not publish a stable direct-ID URL contract.
- [ ] **Protocol handler validation matrix.** Verify current behavior of `teamviewer10://control`, `tvfiletransfer1://`, `tvvpn1://`, `tvchat1://`, `tvvideocall1://`, and `tvpresent1://` against a current TeamViewer 15.x install, especially whether `authorization=` still works after the CVE-2020-13699 patch.
- [ ] **Web API expansion.** Keep the REST API path read-only by default: groups/devices sync first, then paginated devices, online-state polling, connection reports, and per-tier capability logging. Do not claim API-started unattended sessions unless official docs and a licensed test account prove the endpoint.
- [ ] **Remote Management control surfaces.** Research and gate Remote Terminal / Remote Scripts / Remote Scripting APIs behind explicit capability detection and token scopes. Prefer "open/manage in TeamViewer web app" handoffs until the API contract is verified.
- [ ] **Deployment and assignment helpers.** Build guarded command builders for supported onboarding paths: `TeamViewer.exe assign` / `assignment`, `--api-token`, `--group`, `--group-id`, `--alias`, `--grant-easy-access`, `--reassign`, proxy fields, retries, and timeout. These can use `TeamViewer.exe` because they are setup-time automation, not session launch.
- [ ] **COM API detection and diagnostics.** Surface whether `TeamViewer.exe api --install` has registered `TeamViewer.Application`; use it only for supported diagnostics until a current COM control contract is verified.
- [ ] **Command-line reference coverage.** Track support for official CLI surfaces already relevant to TeamStation: `--id`, `--PasswordB64`, `--mode`, `--quality`, `--ac`, `--ProxyIP`, `--ProxyUser`, `--ProxyPassword`, plus future-safe entries for `--control` `.tvc`, `--play` `.tvs`, and `--sendto` file-transfer handoff.
- [ ] **Rejected boundary.** No hidden sessions, protocol reverse engineering, credential injection into third-party surfaces, browser automation against TeamViewer web UI, or background remote-control start flows without explicit official support and user-visible consent.

### Per-monitor DPI awareness (P0, [5/4/3/3/4/3 → 3.67])

- [ ] **Per-monitor DPI V2 in WPF app.manifest + `VisualTreeHelper.GetDpi()` audit.** Test with 125% / 150% / 200% scaling and 4K + 1080p multi-monitor configurations. Cursor offset + click misalignment dominate mRemoteNG's recent issue tracker (#3222, #3223, #3261). HN community signal flags scaling as the #1 reason RustDesk users fall back to TeamViewer. The current TeamStation manifest does not declare per-monitor DPI awareness; this is a P0 bug for any operator running on a docked laptop with an external monitor at a different DPI.
- **Justification:** Charter-aligned, unblocks multi-monitor sysadmins, modest WPF manifest + binding work. Sources: mRemoteNG #3222/#3223/#3261, [HN 42963070](https://news.ycombinator.com/item?id=42963070), [WPF DPI docs](https://learn.microsoft.com/en-us/windows/win32/hidpi/declaring-managed-apps-dpi-aware).

### Bulk-ops value-pick dialog suite (P1, [5/4/4/4/5/3 → 4.17])

- [ ] **BulkSetTag / BulkAddTag / BulkRemoveTag.** Multi-select entries → context menu → "Set tag…" dialog with add/remove/replace semantics. mRemoteNG ships this; r/sysadmin and Royal TS support threads call it the #1 migration friction operation. Reuses the v0.3.5 `MainViewModel.SelectedNodes` + `IsBulkSelectionActive` infrastructure.
- [ ] **BulkSetProxy / ClearProxy.** Same dialog shape — pick proxy host:port:user:password (or "Clear proxy"), apply across `SelectedNodes.OfType<EntryNode>()`. Operator-fleet bulk reconfiguration.
- [ ] **BulkSetMode / BulkSetQuality / BulkSetAccessControl.** Three additional dialogs that share a single value-pick component templated by enum source (`ConnectionMode`, `ConnectionQuality`, `AccessControl`).
- [ ] **Shift-range-select on the tree.** Currently only Ctrl-click accumulates. Range-select needs walk-from-anchor semantics (anchor + cursor + everything in between in display order). Modest WPF work on top of the v0.3.5 `Tree_PreviewMouseLeftButtonDown` handler.
- **Justification:** Top community-research signal across iter-1 and iter-3 (sources A.11, A.16). Infrastructure already shipped in v0.3.5; v0.4.0 adds the consumer dialogs.

### Surface RotateDek in Settings UI (P1 from iter-1, deferred since v0.3.1, [5/4/4/4/5/4 → 4.33])

- [ ] **Wire a "Rotate encryption key" button** in `SettingsWindow` → opens a two-step dialog: (1) confirm + status-bar progress, (2) on success display "Rotated N entries / M folders" toast. Runs the migrator across `folders` + `entries` inside a single `BEGIN IMMEDIATE` transaction. Rollback on any failure; relies on the v0.3.1 two-phase-commit primitive that's already shipped. iter-1 source #157 (ROADMAP P1 internal flag).
- **Justification:** The crypto primitive is shipped (v0.3.1); the missing piece is operator UX. Low-risk wire-up with strong security narrative.

### Microsoft.Data.Sqlite 9.x → 10.0.6 (P1, [5/3/5/4/5/3 → 4.17])

- [ ] **Upgrade NuGet `Microsoft.Data.Sqlite` to 10.0.6** (April 2026 stable). Wraps SQLite 3.45+ — improved WAL fsync batching, JSON function enhancements, `PRAGMA optimize` improvements. Patch-level API change; no source modifications expected.
- [ ] **Add `PRAGMA optimize` on connection close** as part of the upgrade, gated behind a settings toggle (default on) — phiresky's SQLite tuning blog documents the latency reduction. iter-3 sources C1, C2.
- **Justification:** Free perf + maintenance, no API change, no charter friction.

### CVE registry + status-bar regression alert (P1, [5/4/4/5/5/4 → 4.50])

- [ ] **Maintain a static JSON registry of known TeamViewer CVEs** — `assets/cve/teamviewer-known.json` shipped in the binary, mapping `{CVE-id, CVSS, affected-version-range, remediation-link}`. The v0.3.5 status-bar pill currently uses a hardcoded `MinimumSafeVersion = 15.74.5`; this generalises the comparison so future CVE bulletins (CVE-2027-X, CVE-2028-Y) become a JSON edit + retag, not a code change.
- [ ] **Per-CVE tooltip on the update-available pill.** Hover renders the matched CVE summary + remediation URL. Operator-side guidance, no auto-update.
- **Justification:** Closes iter-2 P0 follow-up; complements v0.3.5's version detector. Iter-3 source A.10 and B.15 corroborate.

### Audit-trail integrity (Phase 1: HMAC chain) (P1, [5/4/3/4/4/4 → 4.00])

- [ ] **Append-only HMAC chain on `audit_log` rows.** Each row carries `prev_hash` + `row_hash = HMAC-SHA256(key=DEK, msg=prev_hash || row_data)` so a verifier can detect retroactive tampering. The DEK already exists; no new key material. Integrity check tool ships as a CLI: `TeamStation.exe --verify-audit-chain`.
- [ ] **CSV / NDJSON export of the verified audit log** for SIEM ingestion (Splunk, Elastic). iter-3 source A.10 (compliance pain), A.18 (RDM / BeyondTrust paywall the equivalent feature).
- **Justification:** Iter-3 finds compliance + audit trails are the most-paywalled commercial-competitor feature. TeamStation can ship the OSS equivalent within charter (local-first, no cloud upload of the logs). Differentiator.

### Per-monitor DPI snapshot regression test in CI (P2, [4/3/3/4/4/4 → 3.67])

- [ ] **Capture-then-compare screenshots at 100% / 125% / 150% in CI.** Runs in an automation-friendly headless WPF runner via `Microsoft.UI.Xaml.Tests.Hosting` or equivalent. Fail CI on >2 px drift in critical-control bounding boxes. Iter-1 source #141 (release-rehearsal requirement).

### Frozen collections + `ReadOnlySpan` adoption (P2, [4/2/4/4/5/3 → 3.67])

- [ ] **Convert static reference data to .NET 9 frozen collections** — enum maps, `LaunchInputValidator.ForbiddenInPassword` chars, `ForbiddenSubstringsInAny`, the brush dictionaries in `ThemeManager`. Reduces GC pressure during launch; faster equality / contains checks. Iter-3 source B9.

### `DpapiDataProtector` purpose-string layer (P2, [5/3/4/5/5/3 → 4.17])

- [ ] **Add a purpose-string layer on top of the v0.3.3 entropy-salt work.** `DpapiDataProtector("TeamStation.CredentialStore")` vs `DpapiDataProtector("TeamStation.Settings")` namespaces the wraps so a hypothetical attacker who recovers one DPAPI scope cannot replay it across components. Defense in depth; backward-compatible via the same legacy-fallback pattern used for entropy. Iter-3 source B11.

### README "Prerequisite" line update (P0, docs, [5/2/5/5/5/2 → 4.00])

- [ ] **Update the README "Prerequisite" block** to recommend TeamViewer ≥ 15.74.5 explicitly, citing CVE-2026-23572. The status-bar pill surfaces this in the running app; the README still reads "TeamViewer 15+" generically. Documentation only, but it's the first thing an evaluator sees. Iter-2 P0 follow-up.

---

## v0.5.0 — Next (target: 2026-Q3)

Items that are P1 / table-stakes vs. competitors but need more design surface than fits in v0.4.0. Theme: **plugin-equivalent integrations + signed distribution**.

### Credential-provider lookup pattern (KeePassXC-style integrations) (P1, [5/3/3/4/4/5 → 4.00])

- [ ] **Read-only credential lookup at launch time** for KeePass / Bitwarden / 1Password. Borrow KeePassXC's "built-in integrations, no plugin loader" architecture (iter-3 source A1) — a small set of named integrations live inline, configured via Settings, queried on demand. Never store externally-fetched passwords locally; the cleartext leaves the manager only during the launch window and gets zeroed via the v0.3.4 byte[] path.
- [ ] **Bitwarden Agent Access SDK lookup** (iter-3 source A2). The `--domain github.com` / `--id <vault-item-id>` contract is a clean read-only abstraction TeamStation can mirror.
- [ ] **1Password Connect lookup** (iter-3 source A15). REST API; same shape as Bitwarden.
- [ ] **KeePassXC native messaging integration** (iter-3 source A1). Browser-bridge protocol over `keepassxc-protocol`.
- **Justification:** mRemoteNG ships KeePass integration; Royal TS ships KeePass/AzureKeyVault; iter-3 community signal flags credential providers as a top-3 OSS-migration request. Reclassified to NEXT in iter-1 from LATER.

### Web API sync — pagination + rate-limit hardening (P1, [5/4/3/4/4/4 → 4.00])

- [ ] **Paginated `/api/v1/devices?groupid=…` with backoff.** Currently the read-only Web API pull loads groups → devices in O(n) requests. Users with >500 devices hit the rate limiter; needs cursor-based pagination and exponential backoff. iter-1 risk #3.
- [ ] **`/api/v1/devices.online_state` polling on a 30s timer** with the same backoff harness. Tints tree nodes green/grey. Falls back to ICMP ping for entries without API coverage. iter-1 source #1, #17 (mRemoteNG / RDM feature parity).
- [ ] **Surface `X-RateLimit-Remaining` in the log panel** so operators can see when the API is throttling. iter-1 source #107.

### Signed MSI installer + Authenticode pipeline (P1, [5/3/2/4/3/4 → 3.50])

- [ ] **WiX-based MSI** with start-menu shortcut, uninstall support, registry markers. Reduces SmartScreen warnings; required for Group Policy / SCCM deployment.
- [ ] **Authenticode signing pipeline** via Microsoft Artifact Signing (formerly Trusted Signing) — automated re-issuance handles the new 459-day cert cap (CA/B Forum, Feb 2026). iter-3 source B1. GitHub Actions secrets: `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`, `TRUSTED_SIGNING_ENDPOINT`. Already scaffolded as optional in `release.yml`; v0.5.0 makes it the default.
- [ ] **Chocolatey / winget manifests** so `choco install teamstation` / `winget install teamstation` work out of the box. Distribution channels TeamStation can populate cheaply once signing is in place.

### Auto-update with rollback (P1, [4/4/3/3/4/3 → 3.50])

- [ ] **Background update check** against a static `latest.json` manifest (GitHub Releases API or the user's own `manifest_url`). Daily check; user-opt-in only, off by default. No phone-home.
- [ ] **Silent MSI install + rollback** on failed update — keep the prior MSI for one cycle so a bad release doesn't brick the operator's deployment. iter-3 source A.4 (RustDesk's missing auto-updater is the primary OSS-migration friction).
- [ ] **Air-gapped network mode** that disables the manifest fetch with a single setting toggle. iter-3 source A.4 follow-up.

### Session-report dashboard (P1, [4/4/3/4/4/4 → 3.83])

- [ ] **Top-N devices by hours, day-of-week heatmap, recent sessions** dashboard pulled from the existing `session_history` table. Cross-references `/api/v1/reports/connections` if a Web API token is configured, otherwise local-history-only. iter-1 source #17 (RDM benchmark), iter-2 source 5 (Devolutions session-recording).
- [ ] **CSV / NDJSON export of dashboard data** for billing reconciliation.

### Bulk entry rename (find + replace) (P1 from iter-1 reclassification, [5/4/3/4/4/2 → 3.67])

- [ ] **Find/replace dialog** that operates across `SelectedNodes.OfType<EntryNode>()` (or all entries if none selected). Supports literal + regex match on name / TeamViewer ID / notes / tags. iter-1 source #100; r/sysadmin "top-3 complaint" per the iter-1 NEXT-tier reclassification.

### Toast notifications for session events (P2, [5/3/4/4/4/4 → 4.00])

- [ ] **Win 11 toast on session end** via WinRT `ToastNotificationManager`. Triggered by the `TeamViewer.exe` process-exit hook the session-history feature already wires up. Notification includes connection name + duration, click-to-open the session-history dashboard. iter-3 source B6.

### `mRemoteNG` + `RDM XML` + `RoyalTS` import (P1, [5/4/3/4/4/3 → 3.83])

- [ ] **mRemoteNG `*.xml` import.** The CSV path already covers the TeamviewerExporter format; native mRemoteNG-XML-import unblocks operators with hundreds of saved connections to migrate. iter-1 source #1.
- [ ] **Devolutions RDM XML import** mapping `<Connection type="TeamViewer">` rows. iter-1 source #17 + iter-2 source 7.
- [ ] **Royal TS `*.rtsz` import** — bulk-loading a real Royal TS vault (the file is a zipped XML with credentials per-row). Iter-3 source A.16 calls this out as a top OSS-migration enabler.

### Custom env-var expansion + per-environment variables in external tools (P2, [5/3/3/4/4/3 → 3.67])

- [ ] **Custom variable scope** on the external-tools editor — define `%CUSTOMER%`, `%REGION%` once at the folder level, expand in tool templates. iter-3 source A.14 (mRemoteNG #3231 — portable USB env vars). iter-3 source A5 (Insomnia env-scoping pattern).

### Connection-confirmation dialog per folder (P2, [4/2/4/4/5/2 → 3.50])

- [ ] **"Ask before connecting" checkbox** on folder defaults. When inherited to an entry, prompts a 2s timeout dialog before launch. Hostile-environment use case; mRemoteNG ships this. iter-1 source #168.

### Heartbeat / online-state column (P2, [5/3/3/3/4/4 → 3.67])

- [ ] **Periodic ICMP ping to the resolved hostname** (when the entry has a `host` tag) on a 60s timer. Tints tree nodes green / grey / amber-stale. Pairs with the Web API online-state feature for entries without API coverage. iter-1 source #12, #17.

### A11y polish wave (P2, [5/3/3/4/4/3 → 3.67])

- [ ] **AutomationProperties.Name on every toolbar button** (Launch, Add, Edit, Delete, Search, etc.). v0.3.3 added it for the tree + search; v0.5.0 finishes the rest. iter-1 source #19.
- [ ] **High-contrast theme + Windows system-color awareness.** Catppuccin is beautiful but low-contrast for some vision types. Detect Windows high-contrast mode and apply system-color palette. iter-1 source #25.
- [ ] **Dialog tab-order + Esc / Enter wiring** across the six dialog windows (Entry, Folder, Settings, Master-password, Folder-picker, Input). iter-1 source #23.

### "Why TeamStation" white paper (P2, [5/3/2/5/5/5 → 4.17])

- [ ] **Threat-model + security narrative document** (`docs/security-model.md`) — what TeamStation protects against, what it doesn't, the DPAPI scope caveat, the master-password recovery path, the audit-log integrity story (post-v0.4.0). Marketing differentiator vs. the freemium competitors. iter-1 source #61.

### Observability P2 wave (iter-1 category coverage)

- [ ] **Structured (JSON) log export** from the activity log panel for operators piping into ELK / Splunk / Grafana Loki. Separate from the v0.4.0 audit-log export — activity logs carry more detail but aren't tamper-evident. iter-1 sources #33, #43.
- [ ] **Launch-latency histogram** in the log panel — UI-click → `Process.Start` time, crypto-operation time, DB-query time. Catches regressions against the v0.4.0 `Microsoft.Data.Sqlite` 10.0.6 upgrade. iter-1 source #35.
- [ ] **`PRAGMA integrity_check` on startup**, warn on corruption before the first user action. iter-1 source #18.
- [ ] **Slow-query logging** — any SQLite query over a configurable threshold (default 100ms) logs the query + elapsed time in the activity panel. iter-1 source #42.

### Offline / resilience P2 wave (iter-1 category coverage)

- [ ] **Offline-mode credential caching** with PIN-protected local decryption so launches work when the Web API is unreachable (relevant once Web API sync is in). iter-1 sources #64, #177.
- [ ] **SQLite WAL recovery test** — force WAL corruption in a test, verify recovery to the last checkpoint without data loss. iter-1 source #54.

---

## v1.0.0 — Later (target: 2026-H2)

Items that round out the 1.0 promise: anyone migrating from mRemoteNG or RDM should find no missing pieces.

### TeamViewer Web API sync — full scope

- [ ] Pull `GET /api/v1/groups`, `GET /api/v1/devices?groupid=…`, `GET /api/v1/contacts`, `GET /api/v1/reports/connections` into a synthetic **TV Cloud** root folder. Re-runnable, diff-aware. (P1 from charter doc.)
- [ ] **"Deploy Host" wizard** — wraps `TeamViewer.exe assign --api-token … --group … --alias %COMPUTERNAME% --grant-easy-access` into a generator that produces a one-shot `.cmd` to email to the client. (P1 from charter doc.)

### Plugin / external-tool API surface

- [ ] **Formal external-tool plugin contract** — replace the current hardcoded `%ID%` / `%NAME%` / `%TAG:<key>%` / `%PASSWORD%` / `${ENV}` expansion with a declarative manifest format that third parties can extend. Borrows the VS Code declarative-contribution model (iter-3 source A6) — no arbitrary code loading.
- [ ] **Custom search-filter predicates** — let operators add `is:offline-30d` / `tag:site=NYC` style predicates via a static config file. iter-1 source #77.
- [ ] **Connection-profile templates** — folder-by-reference (not creation-time snapshot) so a single "Customer A" template flows updates to all child entries. iter-3 source A12 (Royal TS pattern).

### Cloud-folder DB sync (conflict-aware)

- [ ] **Lock-file + last-writer-wins with `.bak` on conflict.** Encrypted blob in the user-chosen folder. v0.3.5 ships the encrypted DB mirror; v1.0 adds explicit conflict resolution. (P1 from charter doc.)

### TOTP seed per entry (RFC 6238)

- [ ] **TOTP storage in the entry editor** for the post-TV Windows-login MFA. RFC 6238, click-to-copy, never shared with TeamViewer. iter-1 source #1, #17.

### Billable-hours export

- [ ] **Per-tag hourly rate + monthly CSV/PDF stub** generated from the session-history dashboard. iter-1 source #17 (RDM benchmark).

### Session-recording integration

- [ ] **`--play <path.tvs>` launcher** for saved sessions; optional auto-record toggle that writes to a folder of the user's choosing. Iter-2 source 5 (Devolutions makes session recording free as of April 2026).

### Avalonia fork (post-1.0, EXPLICIT FORK)

- [ ] **Cross-platform Avalonia port** for macOS / Linux as a separate repo. WPF code stays the canonical desktop tier; Avalonia inherits the `TeamStation.Core` + `TeamStation.Data` + `TeamStation.Launcher` projects unchanged. Re-implements only the UI tier. iter-1 source #82, scored 2.83 → REJECTED-from-NOW; flagged here as the explicit post-1.0 deliverable.

### i18n / l10n (Later — deferred until explicit demand)

iter-1 harvested six i18n items (string externalisation, RTL layout, top-5 language packs, CJK-safe fonts, translatable exception messages, locale-aware date/time export). They scored 3.33 avg (iter-1 LATER / P2) because TeamStation has no stated localization demand yet and the WPF resource-dictionary work is prerequisite for any translation contribution. The category is acknowledged here so future research iterations don't re-harvest it as "missing".

- [ ] **`Strings.resx` externalisation** — extract every hard-coded UI string, button label, tooltip, error message into a single resource file. Prerequisite for any localization PR. iter-1 sources #27, #31.
- [ ] **CJK-safe fallback font** in the Catppuccin theme pack — test the six dialog surfaces + tree + inspector with Japanese / Chinese entry names. iter-1 source #32.
- [ ] **RTL layout audit** — test WPF layout mirroring for Hebrew / Arabic tags and notes. iter-1 source #28.
- [ ] **Community translation packs** (German, French, Spanish, Japanese, Simplified Chinese) — ship when externalised strings attract contributor PRs. iter-1 source #29.
- [ ] **Locale-aware date/time in CSV exports** — session-history CSV + billable-hours CSV respect the Windows locale instead of hard-coding invariant culture. iter-1 source #30.

---

## Under Consideration (CHARTER-REVIEW)

Items that contradict the charter as currently written. Not silently dropped — surfaced for explicit decision in a future charter revision. Each carries the constraint it would require relaxing.

- **RustDesk + Windows App federation** — multi-protocol launcher. Would require relaxing "TeamViewer-only" charter. iter-2 source 14 (HN), iter-3 sources A14, B12. **Decision pending:** would TeamStation expand into a "remote-desktop launcher hub" in 2027+ given the Windows-App / legacy-RDP-client EOL transition? Sysadmin migration window is real.
- **WebAuthn / passkey replacement for the master-password KEK.** Would require relaxing "Argon2id portable mode" master-password as the only portable-mode credential. iter-3 source B2. **Decision pending:** does TeamStation want to be passwordless on Windows Hello-equipped boxes?
- **Microsoft Store distribution (MSIX + AppContainer).** Would require relaxing "GitHub-only release distribution" in exchange for store auto-update. iter-3 sources B5, B12. **Decision pending:** is Store reach worth MSIX packaging + Authenticode mandatory + AppContainer side effects (TV.exe discovery may be restricted)?
- **WinUI 3 / Windows App SDK migration from WPF.** Would require relaxing "WPF as canonical UI". iter-3 source B3. **Decision pending:** WPF is mature and stable; WinUI 3 only justified if Microsoft EOLs WPF.
- **Native AOT compilation.** Would require relaxing "self-contained .NET 9 framework-dependent publish" — AOT may break DPAPI / SQLite / WPF reflection paths. iter-2 P2 follow-up. **Decision pending:** binary-size win (~120 MB est) vs migration risk.
- **Snipe-IT-style asset catalog** (custom fields, REST API, host inventory). Would relax "connection manager only — not infrastructure monitoring". iter-3 source A16. **Decision pending:** does TeamStation become a managed asset catalog at v2.0?
- **Multi-monitor preset profiles for RDP-style sessions.** TeamViewer doesn't take per-monitor argv flags directly; presets would only matter if proxying multi-monitor TV sessions. iter-3 source A.19. **Decision pending:** does this fit charter? Probably no.
- **Hidden / stealth session mode.** ConnectWise Control offers it; iter-3 flags as charter-violating "transparency by design" rule. Out of scope as currently formulated.

---

## Rejected (with reasoning preserved)

These items have been considered and rejected. Reasons preserved here so future research iterations cannot silently resurrect them.

- **iOS / Android companion apps.** Charter: Windows-only WPF host; companion apps live in the post-v1.0 Avalonia-fork conversation, not the WPF tree. iter-1 sources #79, #80.
- **Reimplementing the TeamViewer protocol or MITM'ing sessions.** Charter blocker; would expose users to TeamViewer EULA action and is technically out of scope. iter-1 charter doc.
- **Telemetry / phone-home / crash beacons.** Charter blocker; would violate "no telemetry" rule.
- **In-process LLM-based automation** (Atera-style autonomous agents). Iter-3 source B (Atera/Devolutions). Charter blocker — local-first, no LLM dependencies. Webhook/n8n/Zapier integration is the alternative if external automation is wanted.
- **RMM bundling** (N-able / NinjaOne style). iter-3 sources B (N-able / NinjaOne). Out of scope — TeamStation is a connection manager, not an infrastructure monitor.
- **SSH bastion / Azure Bastion cloud-identity logins.** iter-3 source A12 (Royal TS). Charter blocker — non-TeamViewer protocol.
- **Health-check / `localhost:8888` REST API.** iter-1 source #155, scored 1.5. Scope creep, attack surface increase, no clear demand.
- **Crypto-operation result caching.** iter-1 source #128, scored 2.17. Premature optimization; AES-GCM is HW-accelerated, launch latency is acceptable.
- **Web dashboard (optional, opt-in).** iter-1 source #81, scored 2.17. Scope creep beyond TeamViewer-only focus; optional web UI is a MITM risk.
- **Linux .AppImage / Snap distribution from the WPF tree.** iter-1 source #83. Premature before the Avalonia fork lands; rejected from the WPF host's release pipeline.
- **macOS Cocoa host (Swift, not Avalonia).** iter-1 source #84. Platform fragmentation outside the Avalonia-fork strategy.
- **Session-history logger webhook to third-party SaaS** (Sentry / Datadog / Splunk-cloud out of the box). iter-1 source #75, scored 1.67. Scope creep; SIEM ingestion is covered by the v0.4.0 audit-log NDJSON export instead.
- **Per-user account brokering / multitenancy.** iter-3 source B (Devolutions / TeamViewer Tensor). Out of scope; TeamStation is a single-user local tool.
- **EF Core ORM adoption.** iter-3 source B13. Not applicable; raw `Microsoft.Data.Sqlite` + hand-written SQL is intentional.
- **Newtonsoft.Json migration.** iter-3 source C10. Not applicable; `System.Text.Json` is already the project's choice and outperforms Newtonsoft.

---

## Risks and unknowns — spike before coding

Each of these still needs a 1–2 hour prototype before committing architecture around it. iter-3 surfaced one new follow-up risk (DPI behaviour); the rest are carried forward from iter-1.

1. **Does `--Password` / `--PasswordB64` launch silently on TV 15.58+?** Docs list the flag but do not promise headless auth. Community reports on recent builds suggest the "Authorization" dialog may still appear depending on Easy Access status and commercial-use flags. Spike: run the flag against a known host, capture whether a prompt fires. If it does, the entire launch UX pivots to "select-and-click; confirm in TV dialog" — still better than typing the ID but less seamless. Bonus: confirm CVE-2026-23572 mitigation by testing against TV ≥ 15.74.5 — does the "Allow after confirmation" prompt now appear regardless of cmdline?
2. **Do non-`teamviewer10` URI handlers still accept `?authorization=` after the CVE-2020-13699 patch?** The CVE fix quoted argv but did not remove params. Verify each of `tvfiletransfer1`, `tvchat1`, `tvvpn1`, `tvvideocall1`, `tvpresent1` still accept `device` + `authorization` on TV 15.58+. If some don't, P0 "URI fallback matrix" shrinks to whatever survives and those modes revert to CLI-only.
3. **Web API pagination.** Officially "not planned." Groups → devices fan-out is O(n) requests. Users with >500 devices need backoff + caching. Confirm rate limits empirically.
4. **Script-token scope reality by tier.** Some fields (`online_state`) may only populate on paid tiers. Document a per-tier feature matrix after testing with a free account.
5. **DPAPI vs portable mode.** Users expect portable builds to "just work" across machines — that rules out DPAPI in portable mode, master-password-only. Spell it out in the portable-mode toggle UI so nobody loses data.
6. **TeamViewer EULA on automation/wrapping.** Commercial-license holders have historically received nastygrams for scripted usage. The first-run dialog (shipped v0.3.0) clarifies: TeamStation is a **shortcut manager** that launches the official unmodified client — no session MITM, no protocol reimplementation, no telemetry.
7. **Per-monitor DPI behaviour under WPF + custom XAML templates.** The v0.3.2 theme overhaul ships a lot of custom templates; some may freeze brushes at parse time and break under DPI changes. Spike before v0.4.0: test the four shipped themes (Catppuccin, GitHub Dark, Light, System) at 100% / 125% / 150% / 200% on a docked + external-monitor configuration. iter-3 sources A.6, A.13, B (WPF DPI docs).

---

## Out of scope (explicitly)

- Alternative remote-desktop protocols (RDP, VNC, SSH, AnyDesk, ScreenConnect, RustDesk). Per project charter, TeamViewer-only. (CHARTER-REVIEW: see "Under Consideration" if the v2.0 conversation reopens.)
- Reimplementing the TeamViewer protocol or MITM'ing sessions.
- Telemetry, update pings, cloud accounts, or any network traffic beyond the user-initiated Web API sync + the optional auto-update manifest fetch (post-v0.5.0, opt-in only).

---

## Historical record — shipped releases

The factory-loop iterations have shipped v0.3.0 → v0.3.5 in 24 hours. The original P0 / P1 / P2 lists from earlier in this document remain the charter-level reference; this section captures what each shipped release added so future research iterations can trace what's done.

### v0.1.0 — P0 (shipped 2026-04-23)

Original P0 list (charter doc above) — all features shipped:

**Launch + security core:** `--PasswordB64` default; full per-entry parameter surface; URI-handler fallback matrix; CVE-2020-13699 argv hardening; per-entry launch profiles; folder-level inheritance; TeamViewer.exe auto-discovery; portable-mode master password.

**Organization + UX:** Tree view with folders + drag-reorder; entry editor; quick-connect bar; search + filter; Catppuccin Mocha + GitHub Dark + Light themes; embedded log panel; tray icon.

**Data:** Local on-ramp from `Connections.txt`; CSV import; JSON export/import; portable mode.

### v0.3.0 — first external release (shipped 2026-04-24, 05:25 UTC)

Cumulative roll-up of v0.1.2 / v0.2.0 / v0.2.1 + iter-1 hardening pass. Cumulative changes:

- [x] **P0 — Version cut.** Bumped `Directory.Build.props`, README badge, CHANGELOG.
- [x] **P0 — CVE-2020-13699 regression suite.** `CveRegressionTests` against validator + CLI argv builder + URI scheme builder. Uncovered two real validator bugs (ASCII-only ID regex, proxy-host hardening).
- [x] **P0 — `LaunchInputValidator` fuzz coverage.** `ValidatorFuzzTests` — 10k draws per run.
- [x] **P0 — DPAPI / AES-GCM round-trip edge cases.** `CryptoEdgeCaseTests`.
- [x] **P0 — Schema migration with malformed rows.** `SchemaMigrationTests` v1 → v3 path with out-of-range enum ints, whitespace-laden IDs, oversized notes.
- [x] **P1 — IPv6 proxy endpoint support.** Bracket-form parser (`[host]:port` for IPv6 literals; `host:port` for everything else).

### v0.3.0 iteration 2 + 3 (shipped 2026-04-24)

- [x] **P1 — DPAPI DEK rotation.** `CryptoService.RotateDek(IKeyStore, Action<old, new> migrator)` with happy / migrator-throws / missing-DEK guard / master-password-refusal / two-rotations-distinctness coverage.
- [x] **P1 — Atomic-write crash simulation for `SettingsService.Save`.** Target path is forced into a directory so the rename step fails deterministically.
- [x] **P1 — `CliArgvBuilder` range-checks for Quality / AccessControl** (`Enum.IsDefined`).
- [x] **P1 — Apply atomic-write pattern to `JsonBackup` export.** Closed in v0.3.1 via shared `TeamStation.Core.Io.AtomicFile`.

### v0.3.0 postflight security audit follow-ups

Adversarial security pass flagged systemic concerns; all four are now closed:

- [x] **P1 — DEK memory lifecycle.** Closed in v0.3.1. `CryptoService` is `IDisposable`; pinned `GC.AllocateArray<byte>(pinned: true)`; `ZeroMemory` on dispose; `App.OnExit` disposes the process-wide service. `CryptoDisposalTests` (6 cases).
- [x] **P1 — `System.String` credential leak (LAUNCH HOT PATH).** Closed in v0.3.4. UI bindings remain on `System.String` (binding layer can't zero CLR-interned strings).
- [x] **P1 — `RotateDek` crash window between save-new-wrap and migrator-complete.** Closed in v0.3.1 via two-phase commit + `dek_v1_pending` tombstone + `RotationState` classification.
- [x] **P1 — DPAPI wrap adds no `optionalEntropy`.** Closed in v0.3.3 with per-database 32-byte salt + one-shot legacy fallback.

### Competitor gap matrix (iteration 2, 2026-04-24)

One-time scan of the three most-cited OSS / freemium alternatives. Surface existing parity first, then open gaps.

**mRemoteNG** (GPL, C#, .NET Framework, multi-protocol) — parity on nested folders, drag-reorder, folder-level inheritance, credential encryption, external-tool launcher, quick-connect, search/filter. Areas where mRemoteNG still beats us:

- [ ] **P1 — Bulk operations.** Multi-select entries to set proxy / mode / AC / tag. **NOW IN v0.4.0** — see "Bulk-ops value-pick dialog suite" above.
- [ ] **P2 — Connection confirmation dialog.** Per-folder opt-in "ask before connecting". **NOW IN v0.5.0** — see "Connection-confirmation dialog per folder".

**Royal TSX / Royal Server** — parity on folder tree, credential inheritance, entry editor. Document-based vaults are explicitly out of scope (we're single-database). Real gaps:

- [ ] **P2 — Entry templates.** Template folder by reference. **NOW IN v1.0.0** — see "Connection-profile templates".
- [ ] **P2 — Credential providers beyond the local DB.** **NOW IN v0.5.0** — see "Credential-provider lookup pattern".

**Devolutions Remote Desktop Manager free tier** — parity on tray-menu quick launch, pin/unpin entries, drag-reorder, session history export, tags. Gaps:

- [ ] **P2 — Per-entry TOTP seed (RFC 6238).** **NOW IN v1.0.0** — see "TOTP seed per entry".
- [ ] **P1 — Session-report dashboard (read-only).** **NOW IN v0.5.0**.
- [ ] **P2 — Import from Devolutions RDM XML export.** **NOW IN v0.5.0** — see "Import" section.

**Where TeamStation wins today** — keep in the README's "Why TeamStation" pitch:

- Open-source, MIT. No freemium gate on credential storage. No license sunset risk (iter-3 sources A.1, A.2 — sysadmin licensing fatigue).
- TeamViewer-only focus means the URI matrix, CVE-2020-13699 hardening, and launch heuristics are tighter than any multi-protocol competitor that supports TeamViewer as one of twenty.
- DPAPI-wrapped AES-GCM + Argon2id portable-mode KEK + audit log + DEK rotation + per-database DPAPI entropy salt + lazy-Unprotect AppSettings token + byte[] launch path — a full credential-handling story without a subscription.
- Runtime inheritance resolver with a nullable-enum storage model: "inherit from folder" is a first-class value, not a magic-string sentinel.
- v0.3.5 ships TV-version-aware status pill — operator-side CVE-2026-23572 mitigation guidance with no auto-update or telemetry. None of the freemium / commercial competitors ship a CVE-aware client-version surface.

### v0.3.3 — security + a11y patch (shipped 2026-04-24)

- [x] **P1 — Per-database DPAPI entropy salt for the DEK wrap.** `CryptoEntropyTests` (9 cases) + one-shot legacy fallback.
- [x] **P1 — Keyboard navigation on the connection tree (A11y baseline).** `MainWindowKeyboardNavTests` (5 cases via XAML XML parsing).

### v0.3.4 — credential-handling patch (shipped 2026-04-25)

- [x] **P1 — `AppSettings.TeamViewerApiToken` DPAPI entropy via lazy Unprotect.** `AppSettingsEntropyTests` (6 cases).
- [x] **P1 — byte[] credential-read API on the launch hot path.** `CredentialByteApiTests` (10) + `TeamViewerLauncherZeroingTests` (4).

### v0.3.5 — launch-path completion + bulk-ops infrastructure + TV version pill (shipped 2026-04-25)

- [x] **P1 — Wire MainViewModel.LaunchEntry through to byte[] launcher overload.** Completes the v0.3.4 deliverable.
- [x] **P2 — TeamViewer client version detection + status-bar update-available pill.** Closes iter-2 P2. `TeamViewerVersionDetectorTests` (12 cases via `[Theory]`).
- [x] **P1 — Bulk multi-select infrastructure on the connection tree + Bulk Pin/Unpin.** Foundation for the v0.4.0 dialog suite. `BulkMultiSelectTests` (5 cases).

---

## Appendix — Sources (156 distinct URLs)

### iter-1 baseline (38 sources)

Full inventory at `docs/research/iter-1-sources.md`. Categories: direct OSS competitors (mRemoteNG, Remmina, RustDesk, KeePassXC, Bitwarden, MyUncleSam/TeamviewerExporter, Royal TSX, Devolutions RDM free), commercial competitors (BeyondTrust, Splashtop, AnyDesk, TeamViewer MC), adjacent-domain (Beekeeper Studio, VS Code, Ansible Tower, KeePassXC plugins), awesome-lists, community signal (r/sysadmin, HN), standards (RFC 9106 Argon2id, RFC 3986 URI, CVE-2020-13699), academic + engineering blogs (DPAPI threat models, SQLite WAL tuning), dependency changelogs, and CVE databases.

### iter-2 delta (24 net-new sources, April 2026)

Full inventory at `docs/research/iter-2-sources.md`. Highlights: RustDesk 1.4.6 release, Royal TS 7.04.50331, Devolutions 2026 roadmap, TeamViewer April 2026 "Up to Speed" (Tia Reporting / DEX Hub / Intune integration), Devolutions RDM April 2026 features, WPF .NET 9 features, awesome-wpf curation, CVE-2026-23572 (TeamViewer auth bypass), .NET 10.0.7 OOB CVE chain (CVE-2026-26130 / 26127 / 40372), CVE-2026-40315 SQLite SQL injection, Windows App Development CLI v0.2, UWP .NET 9 Native AOT preview, DpapiDataProtector, phiresky SQLite performance tuning, Microsoft "Windows App" client EOL announcement, dasroot password manager comparison, Sobrii TeamViewer alternatives, Fahimai TeamViewer 2026 analysis.

### iter-3 net-new (94 sources)

Two parallel deep-dive subagents covered five source classes prior research lightly touched.

#### iter-3 community + commercial (47 sources, full inventory at `docs/research/iter-3-community-and-commercial.md`)

- **Community signal — pain points + feature requests:** sobrii.io (TeamViewer alternatives), fahimai.com (TV 2026 analysis), SolarWinds blog (alternatives guide), HN 42963070 (RustDesk discussion), Open Tech Hub (RustDesk operational complexity), Develeap case study (TV → RustDesk migration cost), mRemoteNG GitHub issues (#3252, #3247/#3249/#3285, #3222/#3223/#3261, #3231, #3275, #568, #1026), Royal TS support forum (quick-connect ribbon gap), Comparitech (PowerShell / bulk scripting requests), TechTarget (multi-monitor RDP), WPF DPI MS Learn docs, SecureFrame (HIPAA/SOC 2 audit-trail requirements), Latacora (compliance posture), Microsoft Windows 10 EOL announcements, MS "Windows App" client transition.
- **Commercial competitor feature pages:** Devolutions RDM (free + paid, AI automation, 100+ protocols, account brokering, 2FA, session audit), Royal TS (document vault, team-sharing without exposing creds, SSH bastion, KeePass integration, dashboards, PowerShell tasks), Splashtop SOS / Business (PSA integration with Jira/ServiceNow/Zendesk), TeamViewer Tensor (conditional access, multitenancy, SSO, agentless access, managed groups), ManageEngine Remote Access Plus (free tier, file transfer, multi-monitor, registry access), N-able Take Control (RMM, silent deployment, PowerShell API), NinjaOne (multi-OS, EDR + patch, self-service portal), BeyondTrust (credential vault injection, rotation, Password Safe), Atera (autonomous AI agents, ticketing, patch/vuln alerts), ConnectWise Control (proprietary protocol, hidden sessions).

#### iter-3 adjacent-domain + standards/specs + dependency changelogs (47 sources, full inventory at `docs/research/iter-3-adjacent-standards-deps.md`)

- **Adjacent-domain:** KeePassXC integration features (no plugin loader), Bitwarden Agent Access SDK + Bitwarden CLI, Beekeeper Studio connection-store split, DBeaver multi-secrets pattern, Postman / Insomnia env-variable scoping, VS Code extension architecture + command palette, mRemoteNG docs, RDCMan Sysinternals resurrection, awesome-dotnet / awesome-sysadmin / awesome-wpf curation, ThemeWPF + WPF-Theme-Example + Catppuccin VS, atc-wpf MVVM library, Royal TS 7.04 release notes, Devolutions RDM April 2026, RustDesk 1.4.6, 1Password Connect, Snipe-IT custom fields, Xceed WPF .NET 9 features, Sobrii / Fahimai community signal.
- **Standards / specs / RFCs / platform APIs:** Microsoft Trusted → Artifact Signing (459-day cert cap), WebAuthn / passkey APIs (Win 11 24H2 / 25H2), UWP .NET 9 Native AOT, WPF → WinUI 3 migration, CNG DPAPI vs classic DPAPI threat-model analysis (Sygnia), Smart App Control + AppContainer / MSIX, Windows 11 toast notifications + XAML Islands, TeamViewer CLI parameters official docs, .NET 9 `System.Threading.Lock`, .NET 9 frozen collections + IReadOnlyList params, System.Drawing.Common Windows-only, `DpapiDataProtector` purpose-string, Windows App Development CLI v0.2, EF Core 9, RFC 3986 URI syntax, CVE-2026-23572 NVD, TeamViewer security bulletins, .NET 9 System.Text.Json.
- **Dependency changelogs:** Microsoft.Data.Sqlite 10.0.6 (April 2026), SQLite 3.45.0 release log, Konscious Argon2 1.3.1 (June 2024), `System.Security.Cryptography.ProtectedData` 9.0.3, xUnit 2.7.0 (Feb 2024) maintenance mode, xUnit v3 4.0.0-pre.81 preview, .NET April 2026 servicing updates, `System.Drawing.Common` 9.0.3, `System.Text.Json` vs Newtonsoft analysis, RFC 9106 Argon2 spec + Argon2 official, SQLite PRAGMA optimize docs, phiresky SQLite performance tuning blog.

### Hot links (most actionable for v0.4.0+)

| iter | Source | URL |
|---|---|---|
| 2 | CVE-2026-23572 NVD | https://nvd.nist.gov/vuln/detail/CVE-2026-23572 |
| 3 | mRemoteNG #3222 (DPI scaling) | https://github.com/mRemoteNG/mRemoteNG/issues/3222 |
| 3 | HN 42963070 (RustDesk) | https://news.ycombinator.com/item?id=42963070 |
| 3 | Microsoft.Data.Sqlite 10.0.6 | https://www.nuget.org/packages/microsoft.data.sqlite/ |
| 3 | Bitwarden Agent Access SDK | https://github.com/bitwarden/agent-access |
| 3 | KeePassXC integration features | https://keepassxc.org/docs/ |
| 3 | DpapiDataProtector | https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.dpapidataprotector |
| 3 | Microsoft Artifact Signing | https://azure.microsoft.com/en-us/products/artifact-signing |
| 3 | Win 11 toast notifications | https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/send-local-toast |
| 3 | WPF DPI manifest | https://learn.microsoft.com/en-us/windows/win32/hidpi/declaring-managed-apps-dpi-aware |

For the full URL list (156 entries), see the three research artifact files in `docs/research/`. Those files are gitignored (local-only research history) — the URL inventory above is the canonical reference for any item cited in this roadmap.

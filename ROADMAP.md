# TeamStation Roadmap

Prioritization:

- **P0** — Ships in `v0.1.0`. The app is not usable without these.
- **P1** — Ships in `v1.0.0`. Expected by anyone coming from mRemoteNG or Devolutions RDM.
- **P2** — Backlog. Valuable, but not gating adoption.

> Research sources: official TeamViewer CLI docs, KB 34447, REACH API guide, `webapi.teamviewer.com/api/v1/docs`, mRemoteNG docs (external tools + inheritance), Devolutions RDM TeamViewer-entry docs, r/sysadmin migration threads, CVE-2020-13699 advisory, `MyUncleSam/TeamviewerExporter` CSV format.

---

## Current main progress after v0.1.1

The next product pass has landed the largest adoption blockers from P1/P2:

- App settings, first-run trust notice, portable-mode master password, and configurable TeamViewer.exe path.
- Quick connect, saved searches, per-entry profile names, pinned entries, and pinned/recent tray launch menu.
- TeamViewer local history import plus optional read-only Web API group/device pull into a synthetic `TV Cloud` folder.
- Wake-on-LAN, folder/entry launch scripts, external tools, and inherited TeamViewer path / wake broadcast / scripts.
- Session history, CSV session export, persistent audit log storage, and optional encrypted DB mirror to a cloud folder.
- Optional Authenticode signing in the release workflow when signing certificate secrets are configured.

Still open before a formal 1.0 release: real-peer TeamViewer launch validation, Web API pagination/rate-limit hardening, online-state polling, conflict-aware cloud sync, installer packaging, and UX testing on real support workflows.

---

## v0.1.0 — P0

### Launch + security core
- **Default to `--PasswordB64`.** Plain `-p` is a per-entry opt-in only. Keeps cleartext out of shell history and CMD echo.
- **Full per-entry parameter surface.** `--ac` (Access Control: `0` full / `1` confirm-all / `2` view-show / `3` custom / `9` undefined), `--quality` (`1` auto / `2` quality / `3` speed / `4` custom / `5` undef), and the proxy triplet `--ProxyIP` / `--ProxyUser` / `--ProxyPassword` (base64). These are the flags field techs actually reach for.
- **URI-handler fallback matrix.** The CLI `--mode` flag only supports `fileTransfer` and `vpn` — chat, video-call, and presentation **must** go through their URI handlers. TeamStation picks URI vs CLI per connection mode automatically (see `docs/teamviewer-reference.md`).
- **Argv hardening against CVE-2020-13699.** Validate `device` is numeric, reject `\\UNC`, `--play`, and any whitespace-smuggling in `authorization`. Never concatenate user strings into `ShellExecute` — use argv arrays end-to-end. Non-negotiable for an auth-handling tool.
- **Per-entry launch profiles.** Same peer ID can carry multiple profiles — e.g. `Control`, `File Transfer`, `Chat` on the same row. Each profile carries its own mode/quality/access-control. TeamViewer's built-in contact list cannot do this cleanly; it's the #1 reason to switch.
- **Folder-level inheritance.** Credentials, mode, quality, proxy, and access-control cascade from parent folders; entries override per-field. Copied from mRemoteNG, the single biggest power-user feature. Scales past 200 entries without duplication.
- **TeamViewer.exe auto-discovery.** Probe order: `HKLM\Software\TeamViewer` → `HKCU\Software\TeamViewer` → `InstallationDirectory` value → `%ProgramFiles%\TeamViewer\TeamViewer.exe` → `%ProgramFiles(x86)%\TeamViewer\TeamViewer.exe` → legacy `...\Version*\TeamViewer.exe`. Manual per-entry override so TV Host, Portable, and Full can coexist.
- **Master password on top of DPAPI / portable mode.** Current implementation uses PBKDF2-SHA256 with a per-database salt to wrap the AES-GCM DEK for portable mode. Argon2id remains a future hardening option if the project accepts the extra dependency. DPAPI alone dies if a profile is restored to another box; the master password is the recovery path.

### Organization + UX
- **Tree view with folders + drag-reorder.** Entries, folders, nested folders. Right-click: new, rename, duplicate, move, delete.
- **Entry editor.** Name, ID, password, mode, quality, access control, proxy, notes, tags. Inheritance indicators on each field.
- **Quick-connect bar.** Type an ID + password, optional "save as new", launch. Replaces the TV main window entirely for one-offs.
- **Search + filter.** Name, ID, notes, tags, last-connected.
- **Catppuccin Mocha default theme**, GitHub Dark and Light bundled.
- **Embedded log panel + toasts.** No separate log window (per repo rules).
- **Tray icon** with recent connections list.

### Data
- **Local on-ramp: read `%AppData%\TeamViewer\Connections.txt`** and `Connections_incoming.txt`. Instant import of existing TV users' history — no API token needed.
- **CSV import** in the `MyUncleSam/TeamviewerExporter` format (already in the wild).
- **JSON export/import** for the full database.
- **Portable mode** (`--portable` or detects if running from a writable dir next to the exe). Writes DB next to the exe, skips DPAPI, **uses master password only**. Sysadmins drop this on a USB and go.

---

## v1.0.0 — P1

### TeamViewer Web API sync (script-token based)
- Scopes required: `Groups.Read`, `Contacts.Read`, `Computers.Read`, `Reports.Read` (only what's needed — read-only).
- Pull `GET /api/v1/groups`, `GET /api/v1/devices?groupid=…`, `GET /api/v1/contacts`, `GET /api/v1/reports/connections` into a synthetic **TV Cloud** root folder. Re-runnable, diff-aware.
- This is the direct answer to the #1 complaint on r/sysadmin: "I can't export my Computers & Contacts."

### Live + offline awareness
- **Online-status column.** Poll `/api/v1/devices.online_state` on a 30s timer; tint tree nodes green/grey. Fall back to ICMP ping for entries without API coverage.
- **Wake-on-LAN pre-launch.** MAC + broadcast address per entry; fires a magic packet before launch when the peer is offline.

### Power-user scripting
- **External-tool launcher** with variable expansion. `%ID%`, `%NAME%`, `%TAG:<key>%`, `%PASSWORD%`, `${ENV}`. Right-click an entry to run `mstsc /v:%HOST_TAG%`, `ping`, `psexec`, or open the ticketing system in the default browser. mRemoteNG's killer feature and trivially portable.
- **Pre/post-launch scripts** per entry and folder (inheritable). PowerShell or CMD. Use cases: stop-VPN-then-connect, log to ticketing on session start, rotate password after session end.
- **Session history + duration.** Hook `TeamViewer.exe` process exit; append `(start, end, duration, entry, notes)` to a local session log. Export to CSV for billing.
- **Access-control presets per folder.** "Customer sites = confirm-all (`--ac 1`)", "My lab = full (`--ac 0`)". Inherited.

### Polish
- **Custom icons per entry + color-coded folders.** Catppuccin palette picker.
- **Saved searches.** "All sites offline > 30d", "Tag:site=NYC".
- **Session-recording integration.** `--play <path.tvs>` launcher for saved sessions; optional auto-record toggle.
- **Cloud-folder DB sync.** User picks a folder (OneDrive, Dropbox, Syncthing). Lock-file + last-writer-wins with `.bak` on conflict. Encrypted blob, cloud provider never sees plaintext.
- **"Deploy Host" wizard.** Wrap `TeamViewer.exe assign --api-token … --group … --alias %COMPUTERNAME% --grant-easy-access` into a generator that produces a one-shot `.cmd` to email to the client.
- **Tray mini-picker.** Click tray → 10 most-recent + pinned. No keyboard shortcut (per repo rules — tray click instead of `Win+Space`).
- **Signed installer + portable ZIP per release.**

---

## P2 — Backlog

- **TOTP seed per entry** for the target Windows login that follows the TV session. RFC 6238, click-to-copy, never shared with TeamViewer.
- **Billable-hours export.** Per-tag hourly rate, monthly CSV/PDF invoice stub.
- **Session notes + ticket-link field.** Free text + URL that opens in default browser from right-click.
- **Device-group assignment from UI.** Company-token-gated; move devices between TV groups without the MC web UI.
- **Session-report dashboard.** Pull `/reports/connections`, show top-N devices by hours and a day-of-week heatmap.
- **`.tvc` control-file generator.** Export a launch-able file per entry for non-TeamStation users.
- **Bulk operations.** Multi-select → set proxy, set AC, reassign group, rotate master password and re-encrypt.
- **Audit log.** Append-only signed log of every launch and edit, for compliance shops.
- **External credential providers.** KeePass, Bitwarden, 1Password. Read-only lookup at launch; nothing stored locally. mRemoteNG parity.
- **Presentation / video-call / VPN-only entry subtypes.** Leverages `tvpresent1` / `tvvideocall1` / `tvvpn1` URIs. Rarely used but differentiates from the built-in client.
- **Avalonia fork for macOS/Linux.** TV exists on both and URI schemes are identical; WPF is the only Windows-only piece. Explicit fork, not a refactor.

---

## Risks and unknowns — spike before coding

Each of these needs a 1–2 hour prototype before committing architecture around it.

1. **Does `--Password` / `--PasswordB64` launch silently on TV 15.58+?**
   Docs list the flag but do not promise headless auth. Community reports on recent builds suggest the "Authorization" dialog may still appear depending on Easy Access status and commercial-use flags. Spike: run the flag against a known host, capture whether a prompt fires. If it does, the entire launch UX pivots to "select-and-click; confirm in TV dialog" — still better than typing the ID but less seamless.

2. **Do non-`teamviewer10` URI handlers still accept `?authorization=` after the CVE-2020-13699 patch?**
   The CVE fix quoted argv but did not remove params. Verify each of `tvfiletransfer1`, `tvchat1`, `tvvpn1`, `tvvideocall1`, `tvpresent1` still accept `device` + `authorization` on TV 15.58+. If some don't, P0 "URI fallback matrix" shrinks to whatever survives and those modes revert to CLI-only (which means chat/video/present may not be launchable headless at all).

3. **Web API pagination.** Officially "not planned." Groups → devices fan-out is O(n) requests. Users with >500 devices need backoff + caching. Confirm rate limits empirically.

4. **Script-token scope reality by tier.** Some fields (`online_state`) may only populate on paid tiers. Document a per-tier feature matrix after testing with a free account.

5. **DPAPI vs portable mode.** Users expect portable builds to "just work" across machines — that rules out DPAPI in portable mode, master-password-only. Spell it out in the portable-mode toggle UI so nobody loses data.

6. **TeamViewer EULA on automation/wrapping.** Commercial-license holders have historically received nastygrams for scripted usage. Add a first-run dialog clarifying: TeamStation is a **shortcut manager** that launches the official unmodified client — no session MITM, no protocol reimplementation, no telemetry.

## Out of scope (explicitly)

- Alternative remote-desktop protocols (RDP, VNC, SSH, AnyDesk, ScreenConnect). Per project charter, TeamViewer-only.
- Reimplementing the TeamViewer protocol or MITM'ing sessions.
- Telemetry, update pings, cloud accounts, or any network traffic beyond the user-initiated Web API sync.

---

## v0.3.0 — in flight (iteration 1 shipped, 2026-04-24)

The advanced workflow features from the v0.1.2 internal / v0.2.0 / v0.2.1 waves landed on `main` without a GitHub release. v0.3.0 cuts them together with a security-and-test-coverage hardening pass.

- [x] **P0 — Version cut.** Bump `Directory.Build.props`, README badge, and the `v0.3.0` CHANGELOG entry to reflect the sum of the v0.1.2, v0.2.0, and v0.2.1 content already on HEAD. Shipped in iteration 1.
- [x] **P0 — CVE-2020-13699 regression suite.** `CveRegressionTests` drives curated vectors against the validator, CLI argv builder, and URI scheme builder. Uncovered two real validator bugs fixed in the same iteration (ASCII-only ID regex, proxy host hardening).
- [x] **P0 — LaunchInputValidator fuzz coverage.** `ValidatorFuzzTests` — 10k draws per run assert the exception-contract invariant.
- [x] **P0 — DPAPI / AES-GCM round-trip edge cases.** `CryptoEdgeCaseTests` covers empty / null-byte / 512 KiB / surrogate / invariant-culture / per-byte tamper / nonce-uniqueness / corrupt-salt.
- [x] **P0 — Schema migration with malformed rows.** `SchemaMigrationTests` seeds v1 rows with out-of-range enum ints, whitespace-laden IDs, and oversized notes; asserts v1 → v3 rescues every recoverable row and that downstream builders never throw unexpected framework exceptions.
- [ ] **P1 — Atomic-write crash simulation.** Force a failed `File.Move` (target directory read-only, file locked) during JSON backup export and during `SettingsService.Save`. Assert the original file is unchanged and the temp sidecar is cleaned up on the next run.
- [ ] **P1 — DPAPI DEK rotation path.** Add a `CryptoService.RotateDek` entry point that generates a new DEK, re-encrypts every password column across `folders` + `entries` inside a single transaction, and replaces the wrapped DEK atomically. Rollback on any failure. Ship with a test that proves all plaintexts survive across rotation.
- [ ] **P1 — Competitor gap matrix.** One-time landscape scan against mRemoteNG (external tools, inheritance), Royal TSX / Server (document-based vaults, SSH bastion), and Devolutions RDM free tier (entry templates, connection logs). Feed anything we're missing into this roadmap with a P-tier.
- [ ] **P2 — Release rehearsal.** Local `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true` followed by a smoke-run of the packaged exe. Confirm the ~189 MB artifact boots and that the first-run trust notice fires before any persistence path executes.
- [ ] **P2 — Continuation brief to CLAUDE.md.** Record what each iteration changed, what remains open, and any non-obvious lessons — so the next session resumes without rediscovering state.
- [x] **P1 — IPv6 proxy endpoint support.** Shipped in iteration 3. Bracket-form parser (`[host]:port` for IPv6 literals, `host:port` for everything else) with preserved argv-injection guards on the host component. `LaunchInputValidatorTests` covers IPv6 loopback / full / link-local positives and eight malformed shapes.

### Iteration 2 additions (2026-04-24)

- [x] **P1 — DPAPI DEK rotation.** `CryptoService.RotateDek(IKeyStore, Action<old, new> migrator)` landed, with four xUnit cases covering happy path, migrator-throws rollback, missing-DEK guard, refusal on master-password envelopes, and two-rotations-in-sequence distinctness.
- [x] **P1 — Atomic-write crash simulation for `SettingsService.Save`.** Target path is forced into a directory so the rename step fails deterministically; tests assert the original file (or directory) is unchanged and no `*.tmp` residue survives, even after repeated failures.
- [x] **P1 — `CliArgvBuilder` range-checks for Quality / AccessControl.** Shipped in iteration 3. `Enum.IsDefined` gate on both enum fields; out-of-range ints sourced from a malformed DB row are now silently skipped instead of emitted as `--quality 55` / `--ac -7`. `SchemaMigrationTests.Materialized_entries_with_oob_enum_ints_do_not_crash_launchers` now asserts `--quality` and `--ac` are absent.
- [ ] **P2 — Extend fuzz coverage to `ProxyHost` + `ProxyUsername` + `ProxyPassword`.** The iteration-1 fuzz targets only the four public validator methods; full argv/URI-builder fuzz would catch boundary bugs missed by individual validators.
- [ ] **P1 — Surface `RotateDek` in Settings UI.** Now that the crypto primitive exists, wire a "Rotate encryption key" button that runs the migrator across `folders` + `entries` inside a single `BEGIN IMMEDIATE` transaction. Rollback on any failure, toast + log on success.
- [ ] **P1 — Apply atomic-write pattern to `JsonBackup` export.** Current `Export` path writes to temp + atomic move but lacks the exception-rollback-and-cleanup sidecar test proved necessary for `SettingsService`. Mirror the guarantee and pin it with a twin of `AtomicWriteCrashTests`.

### Competitor gap matrix (iteration 2, 2026-04-24)

One-time scan of the three most-cited OSS / freemium alternatives sysadmins compare TeamStation against. Surface existing parity first, then open gaps.

**mRemoteNG** (GPL, C#, .NET Framework, multi-protocol) — parity on nested folders, drag-reorder, folder-level inheritance, credential encryption, external-tool launcher, quick-connect, search/filter. Gaps vs. TeamStation are intentional (TeamViewer-only charter). Areas where mRemoteNG still beats us:

- [ ] **P1 — Bulk operations.** Multi-select entries to set proxy / mode / AC / tag in one pass. Currently in P2; promoting to P1 on the strength of r/sysadmin threads calling it the #1 migration-friction point.
- [ ] **P2 — Connection confirmation dialog.** Per-folder opt-in "ask before connecting" prompt. Hostile-environment use case; mRemoteNG's checkbox is widely used.

**Royal TSX / Royal Server** (commercial, document-based, multi-vault) — parity on folder tree, credential inheritance, entry editor. Document-based vaults are explicitly out of scope (we're single-database). Real gaps:

- [ ] **P2 — Entry templates.** A "template" folder whose children inherit from it by reference (not by creation-time snapshot). New entries from that folder pick up updates when the template changes. Superset of current folder-default cascade.
- [ ] **P2 — Credential providers beyond the local DB.** KeePass / Bitwarden / 1Password read-only lookup at launch time. Already in P2 backlog; noted here for cross-reference.

**Devolutions Remote Desktop Manager free tier** (commercial, multi-protocol, 2026) — parity on tray-menu quick launch, pin/unpin entries, drag-reorder, session history export, tags. Gaps:

- [ ] **P2 — Per-entry TOTP seed (RFC 6238).** Already in P2 — restated because RDM's implementation is the benchmark to beat. Click-to-copy, never transmitted.
- [ ] **P1 — Session-report dashboard (read-only).** Top-N devices by hours, day-of-week heatmap, pulled from the existing session_history table. Cross-referenced with the Web API `/reports/connections` endpoint in the v1.0.0 block.
- [ ] **P2 — Import from Devolutions RDM XML export.** CSV already supports the TeamViewer Management Console format; RDM exports XML. Add a second importer that maps `<Connection type="TeamViewer">` rows.

**Where TeamStation wins today** — worth keeping in the README's "Why TeamStation" pitch:

- Open-source, MIT. No freemium gate on credential storage.
- TeamViewer-only focus means the URI matrix, CVE-2020-13699 hardening, and launch heuristics are tighter than any multi-protocol competitor that supports TeamViewer as one of twenty.
- DPAPI-wrapped AES-GCM + Argon2id portable-mode KEK, audit log, and now DEK rotation — a full credential-handling story without a subscription.
- Runtime inheritance resolver with a nullable-enum storage model: "inherit from folder" is a first-class value, not a magic-string sentinel.

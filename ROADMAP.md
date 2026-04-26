# TeamStation Roadmap

**Last research pass:** 2026-04-25 (iter-5, repo-recon + external OSINT refresh).
**Source basis:** iter-1 (38 sources, 160 harvested, 49 scored), iter-2 (24 net-new delta sources), iter-3 (94 net-new findings across community/commercial/adjacent/standards/dependencies), iter-4 (32 source-backed project patterns), iter-5 (72 additional source-backed repo/competitor/platform/security groups, 94 raw feature signals, 31 scored roadmap decisions). Full Appendix at the end.
**Cadence:** factory-loop iterations have shipped v0.3.0 -> v0.3.5 plus unreleased main-branch workflow completions. This document supersedes the prior iter-4 backlog with a reconciled v0.4.0 / v0.5.0 / v1.0.0 plan that removes shipped items from active backlog and preserves rejected boundaries.

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
- Bulk multi-select infrastructure on the connection tree + Bulk Pin/Unpin (v0.3.5), followed by unreleased main-branch completion of Shift-range selection, bulk move/delete/copy IDs, bulk tag add/remove/replace, bulk TeamViewer mode/quality/access-control edits, and bulk proxy set/clear.
- Protocol-first launch preference, explicit "Launch via protocol link", and TeamViewer Web Client handoff are already implemented on main. Remaining work is validation against current TeamViewer clients, not initial UI exposure.

Still open before a formal 1.0 release: real-peer TeamViewer launch validation, protocol-handler validation, Web API pagination/rate-limit hardening, online-state polling, conflict-aware cloud sync, signed installer packaging, per-monitor DPI hardening, immutable audit-trail UI, and UX testing on real support workflows.

---

## State of Repo Memo (iter-5 recon, 2026-04-25)

**What TeamStation does today:** a Windows-only, WPF/.NET 9, local-first TeamViewer connection manager for support operators. It stores encrypted TeamViewer connection metadata in SQLite, launches the official installed TeamViewer client, supports folder inheritance, imports, backups, TeamViewer history/cloud pull, external tools, session history, audit log, tray launch, protocol/web-client handoffs, and broad bulk editing.

**What it claims:** "Think mRemoteNG, but TeamViewer-only." The product deliberately avoids implementing TeamViewer's protocol, avoids telemetry, keeps credentials local, and treats TeamViewer as the audited remote-control boundary.

**What is incomplete or still unproven:** live launch behavior against a current peer TeamViewer install, URI handler behavior after CVE-2020-13699 and CVE-2026-23572 fixes, Web API rate limits/pagination/tier behavior, read-only connection reports, HMAC-chained audit integrity, signed MSI/winget/Intune distribution, per-monitor DPI proof, and formal import/export schema contracts.

**Hard constraints:** MIT license, Windows WPF host, .NET 9 SDK (`global.json` pins 9.0.313), TeamViewer-only scope, SQLite + DPAPI/Argon2id/AES-GCM storage, no phone-home telemetry, no arbitrary plugin loader, and no keyboard chord shortcuts.

**Design implication:** TeamStation should compete on operator speed, trustworthy local evidence, safer TeamViewer control-surface orchestration, import quality, and enterprise deployment polish. It should not chase RustDesk/MeshCentral/Guacamole by becoming a remote desktop protocol, relay, or agent platform.

---

## iter-5 Research Delta (2026-04-25)

Phase 1 refreshed every required source class. The strongest new signal is consistent with iter-4: the opportunity is not "more protocols"; it is a TeamViewer operations workbench with stronger evidence, safer launch/control choices, deployment guidance, and sharper state reporting.

### Direct OSS competitor matrix (captured via GitHub, 2026-04-25)

| Project | Stars | Last pushed | Maintainer signal | Useful harvest | Fit for TeamStation |
|---|---:|---|---|---|---|
| [mRemoteNG](https://github.com/mRemoteNG/mRemoteNG) | 10,767 | 2026-04-24 | `sparerd`, `Kvarkas`, `kmscode` | multi-select launch, external tools, inheritance, duplicate-name path display | High for workflow parity; no protocol expansion |
| [RustDesk](https://github.com/rustdesk/rustdesk) | 112,894 | 2026-04-25 | `rustdesk`, `fufesou`, `21pages` | online state, self-host trust, file/USB redirect demand, mobile/touch friction | Medium; use state/trust lessons only |
| [MeshCentral](https://github.com/Ylianst/MeshCentral) | 6,447 | 2026-04-23 | `Ylianst`, `si458`, `krayon007` | stale-agent alerts, concurrency limits, event logs, provenance/SBOM requests | High for audit/state patterns; agent features rejected |
| [Apache Guacamole client](https://github.com/apache/guacamole-client) | 1,644 | 2026-04-20 | `mike-jumper`, `necouchman`, `jmuehlner` | browser history, recording playback boundaries, auth extensibility | Medium; history/evidence patterns only |
| [Apache Guacamole server](https://github.com/apache/guacamole-server) | 3,796 | 2026-04-23 | `mike-jumper`, `necouchman`, `jmuehlner` | protocol gateway architecture | Low; protocol relay is charter-blocked |
| [FreeRDP](https://github.com/FreeRDP/FreeRDP) | 13,096 | 2026-04-25 | `akallabeth`, `awakecoding`, `bmiklautz` | mature protocol-client release hygiene | Low; protocol scope rejected |
| [Remmina](https://github.com/FreeRDP/Remmina) | 2,487 | 2026-02-08 | `akallabeth`, `antenore`, `awakecoding`, `bmiklautz`, `dupondje` | GTK connection profiles and plugin ecosystem | Low; plugin/protocol expansion rejected |
| [TigerVNC](https://github.com/TigerVNC/tigervnc) | 7,063 | 2026-04-13 | `CendioOssman`, `bphinz`, `dcommander` | logging, buffer-copy performance issues, build portability | Low; performance discipline only |
| [UltraVNC](https://github.com/ultravnc/UltraVNC) | 1,310 | 2026-04-25 | `RudiDeVos`, `lbocquet`, `wqweto` | long-lived Windows remote-control UX | Low; VNC protocol rejected |
| [rdesktop](https://github.com/rdesktop/rdesktop) | 1,360 | 2023-09-19 | maintainer-needed signal | maintenance risk of protocol clients | Low; cautionary only |
| [Remotely](https://github.com/immense/Remotely) | 5,044 | 2024-12-17 | `cmbankester`, `dkattan` | remote scripting, session recording, attended access requests | Medium; evidence/runbook patterns only |
| [DWService agent](https://github.com/dwservice/agent) | 559 | 2026-03-04 | `dwservice` | small cross-platform agent architecture | Low; agent boundary rejected |
| [Aspia](https://github.com/dchapyshev/aspia) | 1,876 | 2026-04-24 | `dchapyshev` | remote desktop + file transfer packaging | Low; protocol rejected |
| [Tactical RMM](https://github.com/amidaware/tacticalrmm) | 4,270 | 2026-04-09 | `dinger1986`, `sadnub`, `silversword411`, `wh1te909` | scripts, patching, agent, RMM packaging | Medium as anti-scope and deployment reference |

### Feature harvesting ledger (94 raw signals, deduped into roadmap themes)

Each item is source-traceable through the iter-5 appendix IDs. Status reflects Phase 3 prioritization.

| # | Raw feature signal | Category | Prevalence | Sources | Decision |
|---:|---|---|---|---|---|
| 1 | Multi-select Enter/open selected connections | UX | table-stakes | E1, E50 | Shipped on main |
| 2 | Shift-range selection in tree | UX/accessibility | table-stakes | E1 | Shipped on main |
| 3 | Bulk add/remove/replace tags | UX/data | table-stakes | E1, E28 | Shipped on main |
| 4 | Bulk set mode/quality/access-control | UX/platform | table-stakes | E1, E39 | Shipped on main |
| 5 | Bulk set/clear proxy | UX/platform | table-stakes | E1, E39 | Shipped on main |
| 6 | Duplicate-name path display in quick switchers | UX | common | E1, E24, E50 | Next: Action Center metadata |
| 7 | External-tool folders and hide-from-UI controls | UX/dev-experience | common | E1, E8, E29 | Next: Runbook templates |
| 8 | User-defined variables passed to external tools | dev-experience | common | E1, E24, E29 | Next: scoped variables |
| 9 | Per-folder connection confirmation | safety | common | E1, E28 | Later |
| 10 | Idle lock/close behavior | security | rare | E1 | Under consideration |
| 11 | Online/offline state with data age | reliability | table-stakes | E2, E3, E39, E41 | Next |
| 12 | Stale device/agent alert | reliability | common | E3, E51 | Next, TeamViewer API only |
| 13 | Limit concurrent remote sessions | safety | common | E3, E51, E27 | Later |
| 14 | Event and action log per device | observability | table-stakes | E3, E5, E28, E29 | Now via Evidence Pack |
| 15 | Session history filters | observability | table-stakes | E5, E28, E29 | Now/Next |
| 16 | Recording playback indicator | observability | common | E5, E28, E29, E31 | Later, `.tvs` only |
| 17 | SBOM/build provenance for releases | distribution/security | rising | E3, E51, E63 | Next |
| 18 | Read-only connection reports | observability/data | table-stakes commercial | E39, E40, E28 | Next |
| 19 | API tier/capability matrix | integrations/docs | table-stakes | E39, E40, E44 | Now |
| 20 | Web API OAuth/script-token health | security/integrations | common | E39, E40, E42 | Now via Trust Center |
| 21 | API pagination/backoff | reliability | table-stakes | E39, E44 | Next |
| 22 | API rate-limit remaining surfaced in UI | observability | common | E39, E44 | Next |
| 23 | Device groups / managed groups mapping | data/integrations | table-stakes | E39, E41, E44 | Next |
| 24 | SCIM user provisioning awareness | integrations | commercial | E42 | Later docs only |
| 25 | Session links as newer TeamViewer model | platform | rising | E38, E45 | Under consideration until API proven |
| 26 | Web Client handoff | platform/UX | common | E38, E45 | Shipped on main |
| 27 | Direct Web Client URL by ID | platform | rare/unstable | E54 | Rejected until official stable contract |
| 28 | Assignment/deployment command builder | distribution/platform | table-stakes | E46, E55, E56, E57 | Now/Next |
| 29 | MSI `ASSIGNMENTOPTIONS` recipes | distribution | table-stakes enterprise | E46, E56, E57, E58 | Next deployment kit |
| 30 | Grant Easy Access deployment checks | safety/platform | common | E38, E46, E55 | Next |
| 31 | Managed group policy/reporting prerequisites | docs/integrations | common | E40, E41, E58 | Now docs |
| 32 | Remote script/terminal API detection | integrations | rare | E43, E44 | Under consideration, gated |
| 33 | COM API registration diagnostics | platform | rare | E39 | Later spike |
| 34 | CLI/URI validation matrix | reliability/security | table-stakes for this project | E39, E47, E48 | Now |
| 35 | Current TeamViewer CVE registry | security | table-stakes | E47, E48, E49 | Now |
| 36 | Minimum safe TeamViewer version shown in README | docs/security | table-stakes | E47, E48 | Now |
| 37 | RMM abuse transparency warning | security/trust | table-stakes | E59, E60, E61 | Now |
| 38 | Signed binary/path verification for TeamViewer.exe | security | rare-but-high-value | E59, E60, E63 | Now |
| 39 | Audit-chain verification | security/observability | commercial table-stakes | E28, E29, E31, E59 | Now |
| 40 | Evidence Pack HTML + NDJSON | observability/docs | rare OSS, common commercial | E5, E28, E29, E31 | Now |
| 41 | Operator notes after session | UX/observability | common | E53, E28 | Next |
| 42 | Billable summary export | data | common commercial | E28, E29, E40 | Later |
| 43 | Trust Center dashboard | UX/security | rare OSS | E28, E39, E47, E59 | Now |
| 44 | Import preview/mapping report | migration/data | table-stakes | E18, E19, E50 | Now |
| 45 | Typed custom fields | data | common | E18, E19, E21, E22 | Next |
| 46 | Fieldsets by folder/profile | data | common | E18, E19 | Next |
| 47 | Custom-field search/filter | data/UX | common | E18, E21, E24 | Next |
| 48 | Import unsupported-field report | migration | common | E19 | Now |
| 49 | JSON schema export contract | migration/docs/testing | table-stakes | E18, E19 | Next |
| 50 | mRemoteNG XML import | migration | table-stakes | E1, E8 | Next |
| 51 | Devolutions RDM XML import | migration | common | E28 | Next |
| 52 | Royal TS archive import | migration | common | E29 | Next |
| 53 | KeePassXC read-only credential adapter | integrations/security | table-stakes | E12 | Next |
| 54 | Bitwarden CLI adapter | integrations/security | common | E13 | Next |
| 55 | 1Password Connect adapter | integrations/security | common | E14 | Next |
| 56 | Credential-provider health surface | observability/security | common | E14, E43 | Now/Next |
| 57 | Secret redaction in exports | security/docs | table-stakes | E13, E14, E28 | Now |
| 58 | Scoped environment variables | dev-experience | table-stakes | E15, E16, E17, E24 | Next |
| 59 | Dry-run preview for tasks | safety/dev-experience | common | E21, E29 | Next |
| 60 | Task timeout/output capture | reliability/dev-experience | common | E21, E29 | Next |
| 61 | Runbook success/failure parsers | observability/dev-experience | rare-but-useful | E21, E22 | Later |
| 62 | Workflow graph/runbook chains | dev-experience | advanced | E22 | Later |
| 63 | In-app command palette/action center | UX | table-stakes for power tools | E23, E25, E26, E50 | Now |
| 64 | Optional PowerToys Command Palette extension | integrations | rare | E23, E24 | Later |
| 65 | Persisted command disabled reasons | accessibility/UX | rare | E23, E25 | Now as design requirement |
| 66 | Per-monitor DPI V2 audit | accessibility/UX | table-stakes | E62, E1 | Now |
| 67 | DPI screenshot regression tests | testing/accessibility | rare | E62 | Later |
| 68 | `Strings.resx` externalization | i18n | table-stakes for translation | E70 | Later |
| 69 | CJK/RTL layout audits | i18n/accessibility | common | E70 | Later |
| 70 | Locale-aware exports | i18n/data | common | E70 | Later |
| 71 | Signed MSI | distribution | table-stakes enterprise | E32, E33, E34, E35 | Next |
| 72 | winget manifest | distribution | table-stakes Windows | E33 | Next |
| 73 | Chocolatey package | distribution | common | E35 | Next |
| 74 | Intune Win32 install/detection guide | distribution/docs | table-stakes enterprise | E34, E56, E57 | Next |
| 75 | Artifact/Trusted Signing automation | distribution/security | rising | E63, E64 | Next |
| 76 | Rollback-capable update manifest | reliability/distribution | common | E32, E63 | Later |
| 77 | Air-gapped update mode | reliability/security | common | E34, E59 | Later |
| 78 | SQLite `PRAGMA optimize` | performance | table-stakes | E65, E66 | Now |
| 79 | WAL checkpoint discipline for backups/mirrors | reliability/performance | table-stakes | E66, E67 | Now/Next |
| 80 | xUnit v3 migration spike | testing/dev-experience | rising | E68, E69 | Later |
| 81 | Coverage gate | testing | common | E68, E69 | Next |
| 82 | Mutation/property tests for launch validators | testing/security | rare but aligned | E48, E59 | Later |
| 83 | Screenshot/docs assets refresh | docs/UX | table-stakes | E28, E33 | Now |
| 84 | RMM/remote-access allowlist guidance | security/docs | table-stakes | E59, E60, E61 | Now |
| 85 | Detection-friendly audit event names | security/observability | common | E60, E61 | Now |
| 86 | Multi-user collaboration | multi-user | commercial | E28, E39, E42 | Rejected for single-user local charter |
| 87 | Mobile companion app | mobile | common commercial | E2, E30, E38 | Rejected for WPF charter |
| 88 | Agent/RMM backend | platform/OS | common RMM | E3, E37, E59 | Rejected |
| 89 | Alternative protocols RDP/VNC/SSH/RustDesk | platform/OS | common | E4, E5, E6, E7, E8, E9 | Rejected |
| 90 | Hidden/stealth sessions | safety/security | commercial | E27, E59 | Rejected |
| 91 | Browser automation against TeamViewer web UI | security/platform | tempting but brittle | E45, E54 | Rejected |
| 92 | SaaS webhook telemetry | observability | common | E28, E59 | Rejected by no-phone-home charter |
| 93 | WinUI 3 migration | platform/OS | rising | E71 | Under consideration only |
| 94 | Avalonia cross-platform fork | platform/OS | rare | E72 | Later, explicit fork |

### Phase 3 prioritization summary

- **Now:** validate TeamViewer control surfaces, add static CVE registry, expose Trust Center/evidence/audit integrity, reconcile README safe-version docs, add import preflight reporting, harden SQLite maintenance, and make Action Center metadata the command-discovery backbone.
- **Next:** credential-provider broker, Web API pagination/online-state/reports, runbook templates, typed custom fields, signed installer/winget/Chocolatey/Intune, schema contracts, migration importers, coverage gate.
- **Later:** PowerToys companion, xUnit v3 migration, workflow graphs, DPI screenshot regression, i18n packs, update rollback, TOTP, billable summaries, Avalonia fork.
- **Under consideration:** TeamViewer session-link automation, Remote Management APIs, COM control contract, WebAuthn portable unlock, WinUI 3, Microsoft Store/MSIX.
- **Rejected:** protocol reimplementation, non-TeamViewer protocols, hidden sessions, browser automation against TeamViewer web UI, RMM agents/backend, mobile companion apps in the WPF tree, mandatory cloud accounts, telemetry/phone-home, multi-user SaaS collaboration.

---

## v0.4.0 — Now (target: 2026-Q2)

The highest-leverage P0/P1 items uncovered by iter-3 through iter-5 plus the open follow-ups from iter-1 / iter-2 that fit cleanly into one minor release. Theme: **enterprise readiness** — prove TeamViewer control-surface behavior, fix the per-monitor DPI bug that dominates mRemoteNG community complaints, close the TeamViewer-version surface with a CVE registry, and start the audit-trail/evidence integrity story.

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

### TeamViewer capability and abuse-resistance pass (iter-5, P1)

- [x] **Capability matrix by TeamViewer surface.** Added `docs/teamviewer-capability-matrix.md` with proof status, dependencies, token/scope, credential behavior, failure modes, and next actions for CLI launch flags, URI handlers, Web Client handoff, Web API groups/devices, Web API connection reports, service cases/session links, SCIM, deployment/assignment commands, Remote Management scripts/terminal, `.tvc` / `.tvs` files, file-transfer handoff, and rejected boundaries. Remaining work is lab proof against current TeamViewer clients and licensed API surfaces before expanding behavior.
- [x] **Signed TeamViewer binary provenance check.** v0.4.0 ships `TeamViewerBinaryProvenanceInspector.Inspect()` — runs `WinVerifyTrust` against the resolved `TeamViewer.exe`, reads the signing certificate subject via `X509Certificate.CreateFromSignedFile`, classifies via the pure `TeamViewerBinaryProvenanceEvaluator` into one of `NotFound` / `SignedByExpectedPublisher` / `SignedOutsideExpectedRoot` / `SignedByUnexpectedPublisher` / `UnsignedOrUntrusted` / `UnableToVerify`. Surfaced today as a one-line activity-log entry on startup; the dedicated Trust Center surface still wants the rest of the dashboard to land. Pinned by `TeamViewerBinaryProvenanceTests` (custom publisher substring, expected-root markers, all four signature states, fileVersion plumbing). Result is advisory only — TeamStation never blocks launch on a failed provenance check.
- [x] **RMM-abuse transparency checklist.** Added a Trust Center "Use and audit posture" panel plus README/reference notes: TeamStation launches the official TeamViewer client, records local launch/audit events, never hides sessions, does not reverse engineer or relay TeamViewer protocol traffic, and recommends EDR/application-control allowlisting by publisher/path.
- [x] **TeamViewer deployment helper spec.** Added `docs/teamviewer-deployment-helper.md` before implementation. It defines safe command builders, redaction rules, dry-run output, accepted `assign` vs `assignment` families, MSI `ASSIGNMENTOPTIONS`, retry/timeout fields, managed-group prerequisites, audit events, and lab-proof requirements.

### Cross-project capability research ledger (iter-4, 2026-04-25)

Intent: make TeamStation feel like a first-class TeamViewer operations workbench, not a generic RMM clone. Adjacent projects show the strongest leverage is not another protocol; it is faster action discovery, reusable runbooks, trustworthy evidence, richer connection metadata, safer credential lookup, higher-quality import paths, and enterprise-ready deployment. All items below preserve the TeamViewer-only, local-first, Windows-WPF charter unless explicitly marked `CHARTER-REVIEW`.

#### Immediate backlog promotions

- [ ] **P1 - Action Center / command palette.** Build a toolbar/menu-opened searchable command surface for every selected-connection action: Launch, Launch via protocol, Web Client handoff, copy ID, open TeamViewer installed path, run external tool, add tag, bulk move, export evidence pack, verify audit chain, import preview, open settings page. Borrow the discoverability model from PowerToys Command Palette, VS Code command names, and Windows Terminal actions, but do not require keyboard chords. Each command must declare: display name, category, icon, required selection shape, disabled reason, confirmation policy, audit event name, and whether it is safe in portable mode. Sources: [PowerToys Command Palette](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/overview), [VS Code Command Palette UX](https://code.visualstudio.com/api/ux-guidelines/command-palette), [Windows Terminal actions](https://learn.microsoft.com/en-us/windows/terminal/customize-settings/actions).
- [ ] **P1 - Runbook / task template system.** Promote external tools from "launch arbitrary process" to reusable, auditable task templates. Each template gets variables, dry-run preview, confirmation policy, output capture, timeout, success/failure parsing, and per-folder inheritance. This directly adapts Royal TS Command Tasks, mRemoteNG External Tools, and AWX job templates without executing remote-control work outside TeamViewer. Sources: [Royal TS tasks](https://docs.royalapps.com/r2021/royalts/tutorials/working-with-tasks.html), [mRemoteNG External Tools](https://mremoteng.readthedocs.io/en/latest/user_interface/external_tools.html), [AWX job templates](https://docs.ansible.com/projects/awx/en/24.6.1/userguide/job_templates.html).
- [ ] **P1 - Evidence Pack export.** One-click export for a selected connection or folder: session history, launch attempts, TeamViewer client version and CVE matches, settings snapshot with secrets redacted, audit-chain verification result, Web API sync metadata, import source lineage, and operator notes. Output both human-readable HTML and machine-readable NDJSON. Inspired by Guacamole connection history, Devolutions logs/reports/audits, MeshCentral event logs, and Boundary session accountability. TeamStation does not record or proxy live sessions; it packages the evidence it already owns. Sources: [Apache Guacamole connection history](https://guacamole.incubator.apache.org/doc/gug/administration.html), [Devolutions logs/reports/audits](https://docs.devolutions.net/rdm/concepts/advanced-concepts/logs-reports-audits/), [MeshCentral device tabs](https://docs.meshcentral.com/meshcentral/devicetabs/), [Boundary session recording](https://developer.hashicorp.com/boundary/docs/session-recording).
- [x] **P1 - Trust Center dashboard.** v0.4.0 ships a `Tools -> Trust Center...` menu entry that opens a dedicated read-only `TrustCenterDialog` with seven panels — TeamViewer client safety (matched CVEs + remediation URLs), `TeamViewer.exe` provenance (path / Authenticode / publisher / install root), local database (path / size / last-write / per-user-vs-portable mode), encrypted mirror (configured? written? freshness against a 7-day stale threshold), CVE registry metadata (entry count, `last_updated`, source, load diagnostics), Web API token presence (never the value), and local use/audit posture. Pure logic in `TrustCenterReportFactory.Build` synthesises the immutable `TrustCenterReport` from already-collected probes; `TrustCenterViewModel` does the IO at runtime. 4-tone status pills (`Healthy` / `Caution` / `Action` / `Info`) keep the surface calm — no alarmist colours. **Still pending for v0.5.0/v1.0.0:** audit-chain validity panel (depends on the HMAC chain landing first), credential-provider broker health rows, update-manifest opt-in status. Sources: Devolutions reporting/auditing, Guacamole history, 1Password Connect heartbeat/API activity, Bitwarden CLI health/update patterns.
- [ ] **P1 - Import preview and mapping report.** Every importer must show a preflight table before writing: source app, source record count, field mapping, unsupported fields, duplicate strategy, secret handling, validation errors, and redaction mode. Snipe-IT's import discipline is the key lesson: never silently create or guess schema from bad input. Sources: [Snipe-IT importing assets](https://snipe-it.readme.io/docs/importing-assets), [mRemoteNG inheritance](https://mremoteng.readthedocs.io/en/v1.77.3-dev/folders_and_inheritance.html), Devolutions/Royal TS import targets already listed in v0.5.0.

#### v0.5.0 capability upgrades

- [ ] **P1 - Connection custom fields, not an asset catalog.** Add typed custom fields for entries and folders: text, number, date, URL, enum, multi-select, secret-reference, and object-link. Fieldsets can be assigned per folder/profile and used in search, import mapping, task templates, evidence packs, and dashboard grouping. This is the charter-safe subset of Snipe-IT/NetBox/GLPI: TeamStation stores operational metadata for connections, not inventory scans or RMM asset management. Sources: [Snipe-IT custom fields](https://snipe-it.readme.io/docs/custom-fields), [NetBox custom fields](https://netbox.readthedocs.io/en/stable/customization/custom-fields/).
- [ ] **P1 - Scoped variables and environment profiles.** Extend external tools/runbooks with resolution order: command-local, entry custom fields, folder fields, active environment profile, app variables, OS environment. Show a resolved-value preview before launch and mark secret-derived values as redacted. This merges Postman environment scopes, DBeaver variable resolution, and mRemoteNG variable expansion into a safer TeamStation-specific model. Sources: [Postman variables](https://learning.postman.com/docs/sending-requests/variables/variables), [DBeaver admin variables](https://dbeaver.com/docs/dbeaver/Admin-Variables/), [DBeaver preconfigured variables](https://dbeaver.com/docs/dbeaver/Pre-configured-Variables/), [mRemoteNG External Tools](https://mremoteng.readthedocs.io/en/latest/user_interface/external_tools.html).
- [ ] **P1 - Credential-provider broker facade.** Formalize the existing v0.5 credential-provider item into a common read-only lookup contract: `CanResolve`, `ResolveSecretBytesAsync`, `ExplainHealth`, `RedactForExport`, and `ForgetSession`. Initial adapters remain KeePassXC, Bitwarden CLI, and 1Password Connect. Do not store fetched cleartext locally; cache only provider binding metadata and last-health status. Sources: [KeePassXC docs](https://keepassxc.org/docs/), [Bitwarden CLI](https://bitwarden.com/help/cli/), [1Password Connect API](https://developer.1password.com/docs/connect/api-reference/), [Boundary credential brokering](https://developer.hashicorp.com/boundary/tutorials/credential-management/hcp-vault-cred-brokering-quickstart).
- [ ] **P1 - Online-state and stale-state model.** Make device state explicit: TeamViewer Web API `online_state` when available, last successful local launch, last Web API refresh, optional ICMP result, and "stale after" threshold. Do not imply agent-grade monitoring. MeshCentral and RustDesk prove users value live state, but TeamStation should surface confidence and data age rather than invent liveness. Sources: [MeshCentral guide](https://docs.meshcentral.com/meshcentral/), [RustDesk self-host](https://rustdesk.com/docs/en/self-host/index.html).
- [ ] **P1 - Enterprise deployment kit.** Ship a signed MSI plus `winget`, Chocolatey, and Intune-ready install/uninstall command documentation. Include detection-rule guidance, silent install examples, optional policy file location, and air-gapped update-manifest settings. Sources: [WiX Toolset](https://docs.firegiant.com/wix/), [WinGet manifest docs](https://learn.microsoft.com/en-us/windows/package-manager/package/manifest), [Intune Win32 app docs](https://learn.microsoft.com/en-us/intune/app-management/deployment/add-win32), [Chocolatey package docs](https://docs.chocolatey.org/en-us/create/create-packages/).
- [ ] **P2 - PowerToys Command Palette extension (optional companion).** After the in-app Action Center exists, consider a small Windows-only companion extension that exposes pinned TeamStation actions to PowerToys Command Palette. It must call TeamStation through a documented local command contract, never a listening HTTP server. Sources: [PowerToys extension guide](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/creating-an-extension).

#### v1.0.0 / post-1.0 differentiators

- [ ] **P1 - Data portability contract.** Publish `docs/schemas/teamstation-export.v1.json`, importer mapping docs, and a round-trip test suite covering JSON export/import, mRemoteNG XML, RDM XML, Royal TS archives, and TeamViewer history CSV. Importability is a product feature, not just a migration utility.
- [ ] **P1 - Session-review workspace.** Evolve the existing dashboard into a review surface: filter by customer/tag/operator/date, compare local launch history vs TeamViewer connection reports, export billable summaries, flag suspicious after-hours launches, and attach operator notes. Sources: Guacamole connection history, Devolutions reports/audits, and Boundary-style session accountability.
- [ ] **P2 - Guided first-run migration wizard.** First-run should ask one question: "Where are your existing TeamViewer connections?" Then offer TeamViewer local history, CSV, mRemoteNG XML, RDM XML, Royal TS, or manual quick connect. The wizard should end with a trust summary: encrypted local DB path, backup status, and TeamViewer version status.
- [ ] **P2 - Policy profile packs.** Add local JSON policy packs for teams: require confirmation before launch, forbid plaintext exports, require evidence pack on destructive bulk edits, hide Web Client handoff, enforce TeamViewer minimum safe version, disable third-party credential providers, or require encrypted mirror. No multitenancy; this is local policy enforcement for managed desktops.

#### Charter guardrails from iter-4

- **Do not import protocols from other remote-access tools.** RustDesk, MeshCentral, Guacamole, Boundary, Royal TS, and mRemoteNG are inspiration sources, not protocol targets. TeamViewer-only remains the differentiator.
- **Do not build an agent, relay, or RMM backend.** MeshCentral/RustDesk features such as installed agents, relay infrastructure, file transfer, terminal control, registry editing, and package deployment are out of scope unless TeamViewer officially exposes a supported surface and the user explicitly invokes it.
- **Do not add an arbitrary plugin loader.** Use curated built-in integrations and declarative templates. VS Code/PowerToys extension patterns are useful for command metadata, not a reason to load third-party code in-process.
- **Do not record live sessions unless TeamViewer itself produces a supported `.tvs` or equivalent artifact.** TeamStation can launch playback and export local evidence, but it must not screen-capture, proxy, or MITM sessions.
- **Do not add SaaS webhooks by default.** Evidence export and SIEM-friendly NDJSON are local-first. Any future webhook sink is `CHARTER-REVIEW` and off by default.

### Per-monitor DPI awareness (P0, [5/4/3/3/4/3 → 3.67])

- [x] **Per-monitor DPI V2 declared in WPF `app.manifest`.** `src/TeamStation.App/app.manifest` already declares `<dpiAware>true/pm</dpiAware>` and `<dpiAwareness>PerMonitorV2, PerMonitor</dpiAwareness>` — the iter-3 ROADMAP claim that the manifest "does not declare per-monitor DPI awareness" was stale by the time it was written; recorded here so future research passes do not re-harvest it. Verified by reading `app.manifest` in v0.4.0.
- [ ] **`VisualTreeHelper.GetDpi()` audit across dialogs at 125% / 150% / 200% scaling and 4K + 1080p multi-monitor configurations.** Cursor offset + click misalignment dominate mRemoteNG's recent issue tracker (#3222, #3223, #3261); requires real multi-monitor hardware to verify so it stays open here.
- **Justification:** Charter-aligned, unblocks multi-monitor sysadmins. Sources: mRemoteNG #3222/#3223/#3261, [HN 42963070](https://news.ycombinator.com/item?id=42963070), [WPF DPI docs](https://learn.microsoft.com/en-us/windows/win32/hidpi/declaring-managed-apps-dpi-aware).

### Bulk-ops value-pick dialog suite — shipped on main after v0.3.5

- [x] **BulkSetTag / BulkAddTag / BulkRemoveTag.** Multi-select entries now support add/remove/replace tag semantics from the selection banner/context menu.
- [x] **BulkSetProxy / ClearProxy.** Selected entries can apply a shared proxy tuple or clear inherited proxy settings in one audited operation.
- [x] **BulkSetMode / BulkSetQuality / BulkSetAccessControl.** Selected entries can receive shared TeamViewer mode, quality, and access-control values without opening each editor.
- [x] **Shift-range-select on the tree.** The tree now supports anchor-to-cursor range selection in display order.
- **Disposition:** Remove from active v0.4 backlog. Keep this section as shipped-status evidence so future research iterations do not re-harvest bulk editing as missing. Sources remain iter-1/iter-3 mRemoteNG/Royal TS/community signal plus iter-5 raw signals #1-#5.

### Surface RotateDek in Settings UI (P1 from iter-1, deferred since v0.3.1, [5/4/4/4/5/4 → 4.33])

- [ ] **Wire a "Rotate encryption key" button** in `SettingsWindow` → opens a two-step dialog: (1) confirm + status-bar progress, (2) on success display "Rotated N entries / M folders" toast. Runs the migrator across `folders` + `entries` inside a single `BEGIN IMMEDIATE` transaction. Rollback on any failure; relies on the v0.3.1 two-phase-commit primitive that's already shipped. iter-1 source #157 (ROADMAP P1 internal flag).
- **Justification:** The crypto primitive is shipped (v0.3.1); the missing piece is operator UX. Low-risk wire-up with strong security narrative.

### Microsoft.Data.Sqlite 9.x → 10.0.6 (P1, [5/3/5/4/5/3 → 4.17])

- [x] **Upgrade NuGet `Microsoft.Data.Sqlite` to 10.0.6** (April 2026 stable). `TeamStation.Data` now restores against `Microsoft.Data.Sqlite` 10.0.6; `dotnet restore` and Release build verified the package/API transition.
- [x] **Add `PRAGMA optimize` on connection close** as part of the upgrade, gated behind a settings toggle (default on) — `Database.OpenConnection()` returns an optimizing connection that runs best-effort maintenance during `Close` / `Dispose`, and Settings exposes "Optimize SQLite planner statistics when database connections close" for troubleshooting opt-out.
- **Justification:** Free perf + maintenance, no API change, no charter friction.
- **Implementation note (main, 2026-04-25).** Added `AppSettings.OptimizeDatabaseOnClose` (default `true`), Settings UI binding, and regression tests covering default persistence plus `PRAGMA integrity_check` after optimize-on-dispose. Remaining observability work stays under the P2 wave: startup `integrity_check`, slow-query logging, and WAL recovery tests.

### CVE registry + status-bar regression alert (P1, [5/4/4/5/5/4 → 4.50])

- [x] **Maintain a static JSON registry of known TeamViewer CVEs.** `assets/cve/teamviewer-known.json` ships embedded in `TeamStation.Launcher.dll` (LogicalName `TeamStation.Launcher.assets.cve.teamviewer-known.json`) carrying `{id, title, cvss, severity, published, summary, remediation, remediation_url, fixed_in, affected: [{min_inclusive, max_exclusive}]}`. `TeamViewerCveRegistry.LoadFromJson` parses defensively: malformed individual rows are skipped with a `LoadDiagnostics` line, the rest still load; a malformed top-level document degrades to `Empty(diagnostic)` and the safety surface treats the result as `Unknown` rather than `Safe`. The previously hardcoded `MinimumSafeVersion = 15.74.5` is now `TeamViewerCveRegistry.Default.RecommendedMinimumSafeVersion()` (highest `fixed_in` across loaded entries) with the constant kept as `FallbackMinimumSafeVersion` so existing call sites work even if the embedded resource is stripped from a custom build.
- [x] **Per-CVE tooltip on the update-available pill.** Status bar binds to `MainViewModel.TeamViewerSafetyTooltip`, which renders one line per matched CVE (`CVE-2026-23572 — fixed in 15.74.5: TeamViewer auth bypass`) plus the remediation URL and the calm sysadmin reminder that TeamStation does not auto-update TeamViewer. Same tooltip hangs off the version chip and the yellow pill so the operator can hover either.
- **Justification:** Closes iter-2 P0 follow-up; complements v0.3.5's version detector. Iter-3 source A.10 and B.15 corroborate.
- **Implementation note (v0.4.0).** Pure logic in `TeamViewerSafetyEvaluator` (4-state classifier: `NotDetected` / `Unknown` / `Safe` / `Vulnerable`) keeps the version detector decoupled from the registry so unit tests pump synthetic registries without touching a registry hive. New tests across `TeamViewerCveRegistryTests` (registry parse + range matching + malformed-row + future-extensibility), `TeamViewerSafetyEvaluatorTests` (4-state evaluator), and `TeamViewerBinaryProvenanceTests` (Authenticode classifier — see binary-provenance item below). The existing `TeamViewerVersionDetectorTests` continue to pin the 15.74.5 baseline. Maintainer update procedure documented in `docs/teamviewer-reference.md` — append to `entries[]`, bump `last_updated`, retag.

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

- [x] **Updated the README "Prerequisite" block** in v0.4.0 to recommend TeamViewer ≥ 15.74.5 explicitly, name the matched CVE registry entries (CVE-2026-23572 + the older CVE-2020-13699 affecting pre-15.8.3 builds), and link out to the new `docs/teamviewer-reference.md#teamviewer-cve-registry-v040` section so evaluators can see the schema and maintainer update procedure on the same page.

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

- [x] **Structured (JSON) log export** from the activity log panel for operators piping into ELK / Splunk / Grafana Loki. The activity dock now has an `Export` action that writes the visible 500-entry transient buffer as `teamstation.activity.v1` NDJSON with sequence, UTC timestamp, normalized level, message, and source fields. This stays separate from the tamper-evident audit log. Format choice follows line-delimited ingestion guidance from [Splunk event line breaking](https://docs.splunk.com/Documentation/SplunkCloud/latest/Data/Configureeventlinebreaking), [Elastic Logstash json_lines](https://www.elastic.co/guide/en/logstash/current/plugins-codecs-json_lines.html), and [Grafana Loki JSON parsing](https://grafana.com/docs/grafana-cloud/connect-externally-hosted/data-sources/loki/log_queries/). iter-1 sources #33, #43.
- [x] **Launch-latency histogram** in the log panel — successful launches now feed a rolling 50-sample activity-dock histogram with bucketed UI-click-to-`Process.Start` time, p50 / p95, and the last credential-read and session-history DB write timings. This keeps the signal local and dependency-free while following the bucketed latency pattern used by [Grafana histograms](https://grafana.com/docs/grafana/latest/panels-visualizations/visualizations/histogram/) and [Prometheus histograms](https://prometheus.io/docs/tutorials/understanding_metric_types/). iter-1 source #35.
- [x] **`PRAGMA integrity_check` on startup**, warn on corruption before the first user action. `Database.CheckIntegrity()` now runs during `App.OnStartup` after the DB opens and before the main window is shown; `MainViewModel` logs a calm info entry on `ok` and a warning with reported SQLite messages when the check fails or cannot complete. iter-1 source #18.
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
- **Snipe-IT-style asset catalog** (custom fields, REST API, host inventory). Would relax "connection manager only — not infrastructure monitoring". iter-3 source A16. **Decision pending:** does TeamStation become a managed asset catalog at v2.0? **iter-4 note:** the charter-safe subset, typed custom fields attached only to TeamStation entries/folders, has been promoted into the iter-4 capability ledger.
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

### Unreleased main after v0.3.5 — workflow completion + TeamViewer surface expansion (2026-04-25)

- [x] **Bulk workflow completion.** Shift-range selection, bulk move/delete/copy IDs, bulk tag add/remove/replace, bulk TeamViewer mode/quality/access-control edits, and bulk proxy set/clear are implemented on main. Active backlog now shifts from "bulk editing exists" to "Action Center discovers and explains every command."
- [x] **TeamViewer launch-surface expansion.** Protocol-first preference, forced protocol launch action, and Web Client handoff are implemented on main. Active backlog now shifts to validation against current TeamViewer clients and a capability matrix.
- [x] **Roadmap cross-project research.** Iter-4 and iter-5 expanded the plan from UI polish into an operations-workbench strategy: evidence packs, Trust Center, runbooks, credential-provider broker, typed custom fields, deployment kit, audit integrity, and TeamViewer control-surface proof.
- [x] **Enterprise trust docs + posture panel.** Added the TeamViewer capability matrix, TeamViewer deployment-helper spec, and Trust Center use/audit posture panel. Active backlog now shifts to real-client validation, API pagination/capability logging, evidence pack export, import preflight, and Action Center metadata.

---

## Appendix — Sources (156 prior distinct URLs + iter-4 + iter-5 research links)

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

### iter-4 cross-project capability research (32 source links, 2026-04-25)

Purpose: identify project patterns that can make TeamStation more capable without violating the TeamViewer-only/local-first charter. These links back the "Cross-project capability research ledger" above.

| ID | Project / domain | TeamStation pattern to harvest | URL |
|---|---|---|---|
| D1 | Apache Guacamole | Connection history, sortable/filterable session records, recording availability indicator | https://guacamole.incubator.apache.org/doc/gug/administration.html |
| D2 | Apache Guacamole | In-browser playback model for externally produced recordings; useful boundary for `.tvs` playback only | https://guacamole.apache.org/doc/gug/recording-playback.html |
| D3 | MeshCentral | Device groups, node data, event/history storage, and remote-management cautionary boundary | https://docs.meshcentral.com/meshcentral/ |
| D4 | MeshCentral | Device tabs, notes, logs, events, actions, and operator-facing state panels | https://docs.meshcentral.com/meshcentral/devicetabs/ |
| D5 | MeshCtrl | CLI automation surface over a management product; inspiration for documented TeamStation command contracts | https://docs.meshcentral.com/meshctrl/ |
| D6 | RustDesk | Online presence, ID/relay architecture, self-host trust story; use as inspiration, not protocol scope | https://rustdesk.com/docs/en/self-host/index.html |
| D7 | mRemoteNG | External tools, variables, escaping rules, and selected-connection command execution | https://mremoteng.readthedocs.io/en/latest/user_interface/external_tools.html |
| D8 | mRemoteNG | Folder inheritance as a productivity multiplier for large connection sets | https://mremoteng.readthedocs.io/en/v1.77.3-dev/folders_and_inheritance.html |
| D9 | Royal TS | Command tasks, favorites, replacement tokens, and confirmation policy | https://docs.royalapps.com/r2021/royalts/tutorials/working-with-tasks.html |
| D10 | Devolutions RDM | Logs, reports, audits, and evidence generation as premium trust features | https://docs.devolutions.net/rdm/concepts/advanced-concepts/logs-reports-audits/ |
| D11 | Devolutions PAM | Session recording as accountability pattern; TeamStation boundary is playback/export of supported artifacts only | https://docs.devolutions.net/pam/concepts/session-recording/ |
| D12 | KeePassXC | Built-in credential integrations without arbitrary plugin loading | https://keepassxc.org/docs/ |
| D13 | Bitwarden | CLI `get` workflows for read-only launch-time secret lookup | https://bitwarden.com/help/cli/ |
| D14 | 1Password Connect | Vault/item API, heartbeat, API activity, and token-scoped secret retrieval | https://developer.1password.com/docs/connect/api-reference/ |
| D15 | Postman | Scoped variables, environment selection, local values, and sensitive variable handling | https://learning.postman.com/docs/sending-requests/variables/variables |
| D16 | DBeaver | Variable resolution order and external variable files | https://dbeaver.com/docs/dbeaver/Admin-Variables/ |
| D17 | DBeaver | Preconfigured variables such as host, port, date/time, folder, and file | https://dbeaver.com/docs/dbeaver/Pre-configured-Variables/ |
| D18 | NetBox | Typed custom fields, visibility controls, object links, and template access | https://netbox.readthedocs.io/en/stable/customization/custom-fields/ |
| D19 | Snipe-IT | Custom fields and fieldsets for structured operational metadata | https://snipe-it.readme.io/docs/custom-fields |
| D20 | Snipe-IT | Import discipline: pre-create fields, avoid guessing schema from bad data | https://snipe-it.readme.io/docs/importing-assets |
| D21 | Ansible AWX | Job templates, promptable fields, credentials, callbacks, and webhook cautions | https://docs.ansible.com/projects/awx/en/24.6.1/userguide/job_templates.html |
| D22 | Ansible AWX | Workflow graphs that link templates into reusable operational sequences | https://docs.ansible.com/projects/awx/en/24.6.1/userguide/workflows.html |
| D23 | PowerToys Command Palette | Fast action discovery and command/result shape for Windows power users | https://learn.microsoft.com/en-us/windows/powertoys/command-palette/overview |
| D24 | PowerToys Command Palette | C# extension model for optional companion integrations | https://learn.microsoft.com/en-us/windows/powertoys/command-palette/creating-an-extension |
| D25 | VS Code | Command naming, categories, and palette discoverability guidelines | https://code.visualstudio.com/api/ux-guidelines/command-palette |
| D26 | Windows Terminal | Declarative actions, command names, icons, IDs, and palette integration | https://learn.microsoft.com/en-us/windows/terminal/customize-settings/actions |
| D27 | WiX Toolset | MSI/package authoring model for enterprise deployment | https://docs.firegiant.com/wix/ |
| D28 | WinGet | Manifest creation and community repository submission path | https://learn.microsoft.com/en-us/windows/package-manager/package/manifest |
| D29 | Microsoft Intune | Win32 app install/uninstall command requirements and script deployment cautions | https://learn.microsoft.com/en-us/intune/app-management/deployment/add-win32 |
| D30 | Chocolatey | Package scripts, upgrade/uninstall hooks, naming, and package maintenance expectations | https://docs.chocolatey.org/en-us/create/create-packages/ |
| D31 | HashiCorp Boundary | Session recording/accountability model for privileged access products | https://developer.hashicorp.com/boundary/docs/session-recording |
| D32 | HashiCorp Boundary | Credential brokering pattern; TeamStation can borrow lookup semantics without becoming an access proxy | https://developer.hashicorp.com/boundary/tutorials/credential-management/hcp-vault-cred-brokering-quickstart |

### iter-5 repo-recon + external OSINT refresh (72 source groups, 2026-04-25)

These source groups back the iter-5 raw feature ledger and Phase 3 decisions above. GitHub star/pushed/maintainer values were captured with `gh repo view` on 2026-04-25 and should be treated as point-in-time metadata.

| ID | Source class | Why it mattered | URL |
|---|---|---|---|
| E1 | Direct OSS: mRemoteNG repo/issues | Bulk workflows, external tools, duplicate-name path display, idle/confirmation requests | https://github.com/mRemoteNG/mRemoteNG |
| E2 | Direct OSS: RustDesk repo/issues | Online state, self-host trust story, file/USB redirect and touch/mobile friction | https://github.com/rustdesk/rustdesk |
| E3 | Direct OSS: MeshCentral repo/issues | Device/event logs, stale-agent alerts, concurrency limits, SBOM/provenance request | https://github.com/Ylianst/MeshCentral |
| E4 | Direct OSS: Apache Guacamole repos | Browser gateway boundary and why TeamStation should not become a protocol relay | https://github.com/apache/guacamole-client |
| E5 | Direct OSS docs: Guacamole administration/recording | Connection history and externally produced recording playback model | https://guacamole.apache.org/doc/gug/administration.html |
| E6 | Direct OSS: FreeRDP | Mature protocol-client maintenance/release reference; protocol scope rejected | https://github.com/FreeRDP/FreeRDP |
| E7 | Direct OSS: Remmina | Connection-profile UX and plugin ecosystem caution | https://github.com/FreeRDP/Remmina |
| E8 | Direct OSS docs: mRemoteNG External Tools + inheritance | Variables, escaping, selected-connection command execution, folder inheritance | https://mremoteng.readthedocs.io/en/latest/user_interface/external_tools.html |
| E9 | Direct OSS: TigerVNC | Logging, build, and performance issue patterns | https://github.com/TigerVNC/tigervnc |
| E10 | Direct OSS: UltraVNC | Long-lived Windows remote-control app reference; VNC protocol rejected | https://github.com/ultravnc/UltraVNC |
| E11 | Direct OSS: rdesktop | Maintainer-risk caution for protocol-client scope | https://github.com/rdesktop/rdesktop |
| E12 | Adjacent OSS: KeePassXC | Built-in credential integration without arbitrary plugin loading | https://keepassxc.org/docs/ |
| E13 | Adjacent OSS: Bitwarden CLI | Read-only launch-time secret lookup shape | https://bitwarden.com/help/cli/ |
| E14 | Adjacent OSS/commercial: 1Password Connect | Token-scoped vault API, heartbeat, and API activity health | https://developer.1password.com/docs/connect/api-reference/ |
| E15 | Adjacent tooling: Postman variables | Environment/local/sensitive variable scoping | https://learning.postman.com/docs/sending-requests/variables/variables |
| E16 | Adjacent tooling: DBeaver admin variables | Variable resolution order and external variable files | https://dbeaver.com/docs/dbeaver/Admin-Variables/ |
| E17 | Adjacent tooling: DBeaver preconfigured variables | Safe predefined variable catalog | https://dbeaver.com/docs/dbeaver/Pre-configured-Variables/ |
| E18 | Adjacent OSS: NetBox custom fields | Typed fields, visibility, object links, and operational metadata | https://netbox.readthedocs.io/en/stable/customization/custom-fields/ |
| E19 | Adjacent OSS: Snipe-IT custom fields/imports | Fieldsets and import discipline with explicit mappings | https://snipe-it.readme.io/docs/importing-assets |
| E20 | Adjacent OSS: GLPI | ITSM/inventory boundary; reinforces custom-fields-not-asset-catalog decision | https://github.com/glpi-project/glpi |
| E21 | Adjacent automation: AWX job templates | Promptable fields, credentials, callbacks, and task templates | https://docs.ansible.com/projects/awx/en/24.6.1/userguide/job_templates.html |
| E22 | Adjacent automation: AWX workflows | Workflow graphs and chained operational templates | https://docs.ansible.com/projects/awx/en/24.6.1/userguide/workflows.html |
| E23 | Adjacent Windows UX: PowerToys Command Palette | Fast action discovery and command metadata | https://learn.microsoft.com/en-us/windows/powertoys/command-palette/overview |
| E24 | Adjacent OSS UX: Beekeeper Studio issue #4124 | Quick switcher should show folder/group organization | https://github.com/beekeeper-studio/beekeeper-studio/issues/4124 |
| E25 | Adjacent UX: VS Code command palette guidelines | Command naming, categories, and discoverability expectations | https://code.visualstudio.com/api/ux-guidelines/command-palette |
| E26 | Adjacent Windows UX: Windows Terminal actions | Declarative action metadata and command IDs | https://learn.microsoft.com/en-us/windows/terminal/customize-settings/actions |
| E27 | Commercial competitor: ConnectWise ScreenConnect overview | Hidden/background tooling and session-control boundary signal | https://docs.connectwise.com/ScreenConnect_Documentation |
| E28 | Commercial competitor: Devolutions RDM audits/reports/logs | Audit trail, activity log, reporting, real-time connection status | https://devolutions.net/remote-desktop-manager/features/audits-and-reports/ |
| E29 | Commercial competitor: Royal TS tasks/credentials | Command tasks, replacement tokens, credential inheritance | https://docs.royalapps.com/r2023/royalts/reference/organization/dynamic-folder.html |
| E30 | Commercial competitor: Zoho Assist features | Unattended access, session recording, mobile, scheduling, reboot/reconnect | https://www.zoho.com/assist/features.html |
| E31 | Commercial competitor: BeyondTrust Remote Support audit | Session reports, recordings, surveys, real-time monitoring, compliance framing | https://www.beyondtrust.com/products/remote-support/features/audit |
| E32 | Distribution: WiX Toolset | MSI authoring path | https://docs.firegiant.com/wix/ |
| E33 | Distribution: WinGet manifest docs | Community/package manifest path | https://learn.microsoft.com/en-us/windows/package-manager/package/manifest |
| E34 | Distribution: Intune Win32 app docs | Install/uninstall command and detection-rule requirements | https://learn.microsoft.com/en-us/intune/app-management/deployment/add-win32 |
| E35 | Distribution: Chocolatey package docs | Package scripts, upgrade/uninstall hooks, maintenance expectations | https://docs.chocolatey.org/en-us/create/create-packages/ |
| E36 | Adjacent RMM: RemoteIQ feature page | RMM feature boundary and why TeamStation should not become infrastructure monitoring | https://remoteiqrmm.com/ |
| E37 | Adjacent OSS RMM: Tactical RMM | Agent/RMM anti-scope plus deployment automation inspiration | https://github.com/amidaware/tacticalrmm |
| E38 | TeamViewer official: connect/get started | ID/password, Easy Access, confirmation, session links, Web Client access | https://support.teamviewer.com/en/support/solutions/articles/75000128752-get-started-with-teamviewer-remote |
| E39 | TeamViewer official: API overview/developer docs | REST/OAuth, groups/devices, reports, service cases, license-gated functions | https://www.teamviewer.com/en-us/global/support/knowledge-base/teamviewer-classic/integrations/core-integrations/teamviewer-api/ |
| E40 | TeamViewer official: connection reports | Incoming/outgoing report prerequisites, filters, CSV export, license gates | https://support.teamviewer.com/en/support/solutions/articles/75000128841-connection-reports |
| E41 | TeamViewer official: device groups | Managed groups, managers, permissions, pending/offline behavior | https://support.teamviewer.com/en/support/solutions/articles/75000128719-device-groups |
| E42 | TeamViewer official: SCIM API | User provisioning and OAuth/token contract | https://teamviewer.github.io/scim-api-docs/ |
| E43 | TeamViewer official: Remote Management guide | Script/remote management feature boundary and capability-gating need | https://dl.teamviewer.com/docs/en/User-Guide-TeamViewer-Remote-Management.pdf |
| E44 | TeamViewer Web API docs/community examples | `devices`, `groupid`, `online_state`, CORS/token friction, rate-limit unknowns | https://webapi.teamviewer.com/api/v1/docs/index |
| E45 | TeamViewer Web Client/session-link signal | Web Client is official; direct-ID URLs are not documented as stable | https://www.teamviewer.com/en-us/global/support/documents/ |
| E46 | TeamViewer Tensor setup/deployment guide | `TeamViewer.exe assign --api-token --grant-easy-access --alias` command surface | https://dl.teamviewer.com/docs/en/TeamViewerTensor_SetupGuide_ManualSolution.pdf |
| E47 | TeamViewer official security bulletins | CVE bulletin inventory and CVE-2026-23572 baseline | https://www.teamviewer.com/en-mea/resources/trust-center/security-bulletins/ |
| E48 | NVD CVE-2026-23572 | Affected versions before 15.74.5; access-control bypass context | https://nvd.nist.gov/vuln/detail/CVE-2026-23572 |
| E49 | NVD CVE-2020-13699 | URI handler quoting vulnerability; validates TeamStation's URI/argv hardening | https://nvd.nist.gov/vuln/detail/CVE-2020-13699 |
| E50 | Community: HN RustDesk discussion | Scaling, fallback-to-TeamViewer, self-host tradeoffs | https://news.ycombinator.com/item?id=42963070 |
| E51 | Community/OSS: MeshCentral stale-agent issue | Stale-state alert demand | https://github.com/Ylianst/MeshCentral/issues/7762 |
| E52 | Community/OSS: MeshCentral concurrent-session issue | Concurrent remote session limits as operational control | https://github.com/Ylianst/MeshCentral/issues/7754 |
| E53 | Community: ConnectWise session-note request | Operator notes/ticket reason after remote sessions | https://www.reddit.com/r/ConnectWiseControl/comments/11kik98 |
| E54 | Community: TeamViewer Web Client direct-URL request | Confirms direct Web Client URL is a user need but undocumented/unstable | https://www.reddit.com/r/teamviewer/comments/18o0xkl |
| E55 | Community: TeamViewer Easy Access assignment command | Real-world `assign --api-token --grant-easy-access` script surface | https://www.reddit.com/r/teamviewer/comments/lc350w |
| E56 | Community: TeamViewer automatic assignment + Intune | Operational pain around MSI, assignment, managed groups, policies | https://www.reddit.com/r/teamviewer/comments/1lj9sdy/teamviewer_automatic_assignment_easy_access_not/ |
| E57 | Community: TeamViewer assignment retries/offline | `assignment --id --offline --retries --timeout` observed command shape | https://www.reddit.com/r/teamviewer/comments/1nuhn2l |
| E58 | Community: TeamViewer Host Intune deployment | Managed group/API token/group ID deployment friction | https://www.reddit.com/r/Intune/comments/16d6shq |
| E59 | CISA RMM Cyber Defense Plan | Remote-management tools are dual-use; need transparency and defensive guidance | https://www.cisa.gov/news-events/news/cisa-publishes-jcdc-remote-monitoring-and-management-systems-cyber-defense-plan |
| E60 | MITRE ATT&CK T1219.002 Remote Desktop Software | TeamViewer/AnyDesk/ScreenConnect abuse context for audit and allowlisting | https://attack.mitre.org/techniques/T1219/002/ |
| E61 | CISA advisory AA23-025A | Legitimate RMM/remote desktop tools abused as persistence/C2 | https://www.cisa.gov/news-events/cybersecurity-advisories/aa23-025a |
| E62 | Microsoft WPF DPI docs | Per-monitor DPI V2 manifest and WPF DPI handling | https://learn.microsoft.com/en-us/windows/win32/hidpi/declaring-managed-apps-dpi-aware |
| E63 | Microsoft Artifact Signing | Cloud code-signing path for release hardening | https://azure.microsoft.com/en-us/products/artifact-signing |
| E64 | DigiCert code-signing validity change | 459-day public code-signing certificate validity pressure | https://knowledge.digicert.com/alerts/code-signing-certificates-459-day-validity |
| E65 | NuGet: Microsoft.Data.Sqlite 10.0.6 | Current package version and dependency floor | https://www.nuget.org/packages/microsoft.data.sqlite/ |
| E66 | SQLite PRAGMA optimize docs | Recommended `PRAGMA optimize` maintenance behavior | https://www.sqlite.org/pragma.html |
| E67 | SQLite WAL docs | WAL checkpoint/backup implications | https://sqlite.org/wal.html |
| E68 | xUnit v3 migration guide | Test framework migration considerations | https://xunit.net/docs/getting-started/v3/migration |
| E69 | xUnit release notes | v2/v3 release state and package names | https://xunit.net/releases |
| E70 | WPF globalization/localization docs | Resource externalization and localization path | https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/wpf-globalization-and-localization-overview |
| E71 | Microsoft WinUI 3 migration docs | Windows App SDK/WinUI migration tradeoffs from WPF | https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/migrate-to-windows-app-sdk/guides/winui3 |
| E72 | Avalonia WPF migration docs | Cross-platform fork feasibility and WPF-to-Avalonia differences | https://docs.avaloniaui.net/docs/get-started/wpf/comparison-of-avalonia-with-wpf-and-uwp |

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

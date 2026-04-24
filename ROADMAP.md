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

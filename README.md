<div align="center">

# TeamStation

**A focused, open-source connection manager for TeamViewer on Windows.**

Organize TeamViewer IDs and passwords in a nested folder tree, launch any saved peer with one click, and keep credentials encrypted at rest. Think mRemoteNG, but TeamViewer-only.

[![Version](https://img.shields.io/badge/version-0.3.1-blue)](CHANGELOG.md)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?logo=windows)](https://www.microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![TeamViewer](https://img.shields.io/badge/TeamViewer-15%2B-0E8EE9)](https://www.teamviewer.com/)
[![CI](https://github.com/SysAdminDoc/TeamStation/actions/workflows/ci.yml/badge.svg)](https://github.com/SysAdminDoc/TeamStation/actions/workflows/ci.yml)

</div>

---

## Why TeamStation

TeamViewer's built-in contact list is tied to your account and lives in the main TeamViewer window. If you manage dozens or hundreds of unattended machines across customers, sites, or environments, you want:

- A **standalone, local tree** that groups machines by whatever hierarchy you care about (customer / site / role / environment).
- **Named connections** — "Dr. Smith — Reception PC" is easier to scan than a nine-digit ID.
- **Saved passwords** for unattended access, encrypted at rest, launched with one click.
- A UI that does **only** this, without coaxing you into trial features or cloud sync you don't want.

TeamStation fills that gap. It is not a remote-desktop protocol — it orchestrates the TeamViewer client you already have installed.

> **Prerequisite:** The full TeamViewer client must be installed on the launching machine (`TeamViewer.exe` on the `PATH` or at its default install location). TeamStation does not bundle or replace TeamViewer.

## Status

`v0.3.1` — **Security-hardening patch.** Closes two of the three v0.3.0 postflight audit P1 items. `CryptoService` is now `IDisposable` — the unwrapped DEK is allocated pinned (`GC.AllocateArray<byte>(pinned: true)`) and zeroed via `CryptographicOperations.ZeroMemory` on dispose, so a memory dump or swap-file snapshot taken after shutdown cannot recover the key. `RotateDek` is now two-phase commit with a `dek_v1_pending` tombstone; startup classifies `None` / `PendingOrphan` / `InterruptedMidRotation` and auto-reconciles the orphan case, refusing the ambiguous mid-rotation case so recovery UX can inspect DB row state before choosing `ForceCommit` or `ForceRollback`. The shared `TeamStation.Core.Io.AtomicFile` helper now backs both `SettingsService.Save` and the JSON-backup export, pinned by a twin of the settings crash tests. Test count 330 → 355, build clean at 0 warnings / 0 errors. See [CHANGELOG.md](CHANGELOG.md).

`v0.3.0` — **First external release.** Bundles every wave of work since the v0.1.x MVP — Argon2id portable-mode KEK, the sub-view-model split, service relocation into `TeamStation.Core`, v0.2.1 hardening (lossy-backup fix, atomic-rename `CloudMirrorService`, single-instance guard, log auto-scroll, retention-based pruning) — plus a focused security and test-coverage hardening pass. Highlights: ASCII-only device ID regex (replaces `\d{8,12}` which accepted Arabic-Indic and full-width digits), argv-injection guard on proxy hosts, bracket-form IPv6 proxy endpoints, `Enum.IsDefined` range-check on `Quality` / `AccessControl` so out-of-range DB rows never reach the argv, a `CryptoService.RotateDek` primitive with save-before-migrate ordering + rollback + old-DEK zero, pinned CVE-2020-13699 regression vectors, 10k-draw validator fuzz asserting only `LaunchValidationException` ever surfaces, atomic-write crash simulation for `SettingsService`. Test count 131 → 330, build clean at 0 warnings / 0 errors. See [CHANGELOG.md](CHANGELOG.md).

`v0.2.0` / `v0.2.1` — **Architecture + hardening waves** rolled into `v0.3.0`. Argon2id replaces PBKDF2-SHA256 for the portable-mode master-password KEK (legacy wraps still unlock and upgrade in place); cloud / Wake-on-LAN / external-tool services moved into `TeamStation.Core` so an eventual Avalonia port has one less barrier; `MainViewModel` carved into `LogPanelViewModel`, `QuickConnectViewModel`, `SearchViewModel` with a single `IDialogService` replacing six constructor delegates; optional clipboard-password launch mode for hostile multi-user hosts; dedicated `ci.yml` build-and-test workflow; JSON backup v2 round-trips every persisted field; `CloudMirrorService` uses `VACUUM INTO` + atomic rename; `SettingsService.Save` is atomic and quarantines corrupt files. See [CHANGELOG.md](CHANGELOG.md).

`v0.1.1` — Hardening pass on top of the MVP. Fixed the folder-picker bug that blocked moving a folder to a sibling, broadened CSV header matching so "Friendly Name" / "TV ID" / "Remote Control ID" columns work, made JSON import tolerant of hand-edited backups (null arrays, camelCase keys, dangling parent references), atomic export writes, a single-instance guard, numeric sort on legacy `Version*` directories, tray-menu cleanup, log auto-scroll, and the first xUnit suite wired into the solution. See [CHANGELOG.md](CHANGELOG.md).

`v0.1.0` — **First MVP release.** TeamStation now has everything a sysadmin needs to replace the built-in TeamViewer contact list: a nested folder tree with drag-to-reorder, entries with name / ID / password / mode / quality / access-control / notes / tags, DPAPI-wrapped AES-256-GCM at rest, one-click launch that walks folder-chain inheritance at launch time, debounced multi-field search, CSV import (TeamViewer Management Console, Remote Desktop Manager, mRemoteNG, and ad-hoc spreadsheet formats all supported via flexible column aliases), JSON backup with round-trip fidelity, an embedded log panel, and a system tray with minimize-to-tray. See [CHANGELOG.md](CHANGELOG.md).

## Feature highlights (shipping)

- Nested folder tree with drag-to-reorder, self-subtree-drop rejection, and per-folder accent colors
- Per-entry fields: friendly name, TeamViewer ID, password, connection mode (Remote Control / File Transfer / Chat / VPN / Video Call / Presentation), quality, access control, proxy, notes, tags
- Runtime **inheritance cascade** — mode / quality / access control / password can be set to "(inherit from folder)" and resolved at launch time
- Per-connection profile names, pinned entries, TeamViewer.exe overrides, Wake-on-LAN, and launch scripts
- CVE-2020-13699-hardened launcher — numeric-ID regex, password denylist, argv-array `Process.Start` only
- DPAPI-wrapped AES-256-GCM credential storage in local SQLite (WAL, FKs); portable mode uses a master-password-wrapped DEK
- Debounced multi-field search; folders with matching descendants stay visible and auto-expand
- Saved searches, quick connect, and pinned/recent tray launching
- Flexible **CSV import** (TeamViewer Management Console, Remote Desktop Manager, mRemoteNG, ad-hoc spreadsheets) with column aliases that tolerate spaces / underscores / hyphens / case
- TeamViewer local history import and optional read-only TeamViewer Web API group/device pull
- **JSON backup** round-trip, atomic on-disk writes, hand-edit tolerant (null arrays, camelCase keys, orphan-safe)
- External tools with `%ID%`, `%NAME%`, `%PASSWORD%`, `%TAG:key%`, and `${ENV_VAR}` expansion
- Session history with CSV export and persistent audit-log storage
- Optional encrypted database mirror to a user-selected cloud sync folder
- Embedded 500-entry log panel with auto-scroll, severity-coloured
- System tray with minimize-to-tray, pinned connections, recent connections, Show, Settings, and Exit
- Single-instance enforcement so two launches don't race on one SQLite file
- Portable mode via a marker file next to the exe
- Optional clipboard-password launch mode (clears automatically after 30s) for shared or hostile hosts
- Dark-first UI (Catppuccin Mocha)

Roadmap / backlog: [ROADMAP.md](ROADMAP.md).

## Screens

Screenshot exports live in [`docs/screenshots/`](docs/screenshots/). See the
capture guide in that folder for the required set; re-capture on any UI change.

> Screenshots are regenerated as part of each release pass. The latest shots
> match the installed `TeamStation.exe` version in the badge above.

## Security model

- Passwords are encrypted at rest with **AES-256-GCM**. In normal mode, the data-encryption key is wrapped by **Windows DPAPI** bound to the current user account, so the database is only decryptable by you on this machine.
- Portable mode uses a master password instead of DPAPI. New master-password wraps use **Argon2id** (time=3, memory=64 MiB, parallelism=2) with a per-database 32-byte salt; legacy PBKDF2-SHA256 wraps from v0.1.x keep unlocking and are silently re-wrapped to Argon2id on next unlock.
- The optional TeamViewer Web API token is stored in the settings file as a DPAPI-protected value for the current Windows user.
- A cloud mirror folder can receive an encrypted copy of the SQLite database after changes. It is a mirror/backup mechanism, not a multi-writer merge engine.
- **Known residual risk:** launching a session normally passes `--PasswordB64` on the TeamViewer command line. That value is visible to any process on the machine that can read the command line of another user-owned process during the brief launch window. Toggle **Settings → Launch without password on the command line** to stage the password on the clipboard instead — TeamStation clears the clipboard 30s after launch.
- TeamStation never phones home. There is no telemetry, no update ping, no cloud account.

## Download

Grab the latest `TeamStation.exe` (or the zipped release bundle) from the [Releases](../../releases) page. Self-contained — no .NET runtime needed separately.

System requirements:
- Windows 10 1809+ or Windows 11 (x64)
- TeamViewer 15 Classic or TeamViewer Remote (the full client; **QuickSupport is not enough** — TeamStation needs `TeamViewer.exe` on the `PATH` or at its default install location)

First run creates `%LocalAppData%\TeamStation\teamstation.db` with WAL mode. Passwords are encrypted at rest with AES-256-GCM under a key wrapped by Windows DPAPI (user-scoped). No network traffic, no telemetry, no update pings.

## Build from source

Requires:
- .NET 9 SDK (`9.0.313` or newer; pinned via [`global.json`](global.json))
- Windows 10 1809+ or Windows 11

```powershell
dotnet restore
dotnet build -c Release
```

Run the WPF shell:
```powershell
dotnet run --project src/TeamStation.App -c Release
```

Produce a self-contained single-file executable (matches the shipped release):
```powershell
dotnet publish src/TeamStation.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=embedded -o publish/win-x64
```

Run the launch-feasibility spike (needs a real TeamViewer peer you own):
```powershell
dotnet run --project tools/TvLaunchSpike -c Release -- --id <TV_ID> --password <PW>
```
It walks the CLI and URI-handler test matrix, captures operator observations, and writes `spike-report.md` next to the binary.

Run the test suite:
```powershell
dotnet test -c Release
```
Tests cover crypto (including Argon2id upgrade from legacy PBKDF2 wraps), portable master-password unlock, inheritance, TeamViewer history import, CSV/JSON parsing, the CLI/URI builders, session/audit storage, and end-to-end SQLite repo operations against a temp DB. The CI workflow at [`.github/workflows/ci.yml`](.github/workflows/ci.yml) builds and tests every push and pull request.

## Why not just use mRemoteNG?

[mRemoteNG](https://github.com/mRemoteNG/mRemoteNG) is excellent, but it only supports RDP, VNC, SSH, Telnet, HTTP/S, rlogin, and raw TCP. TeamViewer's ID-based protocol is not a standard remote-desktop protocol and cannot be spoken by a generic client. The only way to drive TeamViewer is to hand the official `TeamViewer.exe` an ID and password — which is exactly what TeamStation does, while giving you the organizational UX mRemoteNG pioneered.

## Contributing

TeamStation is MIT-licensed. Issues and pull requests welcome once the v0.1.0 baseline is up. In the meantime, feature suggestions via GitHub Issues are the most useful contribution.

## License

[MIT](LICENSE)

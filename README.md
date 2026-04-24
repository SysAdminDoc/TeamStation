<div align="center">

# TeamStation

**A focused, open-source connection manager for TeamViewer on Windows.**

Organize TeamViewer IDs and passwords in a nested folder tree, launch any saved peer with one click, and keep credentials encrypted at rest. Think mRemoteNG, but TeamViewer-only.

[![Version](https://img.shields.io/badge/version-0.1.1-blue)](CHANGELOG.md)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?logo=windows)](https://www.microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![TeamViewer](https://img.shields.io/badge/TeamViewer-15%2B-0E8EE9)](https://www.teamviewer.com/)

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

`v0.1.1` — Hardening pass on top of the MVP. Fixed the folder-picker bug that blocked moving a folder to a sibling, broadened CSV header matching so "Friendly Name" / "TV ID" / "Remote Control ID" columns work, made JSON import tolerant of hand-edited backups (null arrays, camelCase keys, dangling parent references), atomic export writes, a single-instance guard, numeric sort on legacy `Version*` directories, tray-menu cleanup, log auto-scroll, and a 109-test xUnit suite wired into the solution. See [CHANGELOG.md](CHANGELOG.md).

`v0.1.0` — **First MVP release.** TeamStation now has everything a sysadmin needs to replace the built-in TeamViewer contact list: a nested folder tree with drag-to-reorder, entries with name / ID / password / mode / quality / access-control / notes / tags, DPAPI-wrapped AES-256-GCM at rest, one-click launch that walks folder-chain inheritance at launch time, debounced multi-field search, CSV import (TeamViewer Management Console, Remote Desktop Manager, mRemoteNG, and ad-hoc spreadsheet formats all supported via flexible column aliases), JSON backup with round-trip fidelity, an embedded log panel, and a system tray with minimize-to-tray. See [CHANGELOG.md](CHANGELOG.md).

## Feature highlights (shipping)

- Nested folder tree with drag-to-reorder, self-subtree-drop rejection, and per-folder accent colors
- Per-entry fields: friendly name, TeamViewer ID, password, connection mode (Remote Control / File Transfer / Chat / VPN / Video Call / Presentation), quality, access control, proxy, notes, tags
- Runtime **inheritance cascade** — mode / quality / access control / password can be set to "(inherit from folder)" and resolved at launch time
- CVE-2020-13699-hardened launcher — numeric-ID regex, password denylist, argv-array `Process.Start` only
- DPAPI-wrapped AES-256-GCM credential storage in local SQLite (WAL, FKs)
- Debounced multi-field search; folders with matching descendants stay visible and auto-expand
- Flexible **CSV import** (TeamViewer Management Console, Remote Desktop Manager, mRemoteNG, ad-hoc spreadsheets) with column aliases that tolerate spaces / underscores / hyphens / case
- **JSON backup** round-trip, atomic on-disk writes, hand-edit tolerant (null arrays, camelCase keys, orphan-safe)
- Embedded 500-entry log panel with auto-scroll, severity-coloured
- System tray with minimize-to-tray, Show / Exit menu
- Single-instance enforcement so two launches don't race on one SQLite file
- Portable mode via a marker file next to the exe
- Dark-first UI (Catppuccin Mocha)

Roadmap / backlog: [ROADMAP.md](ROADMAP.md).

## Security model

- Passwords are encrypted at rest with **AES-256-GCM**. The data-encryption key is wrapped by **Windows DPAPI** bound to the current user account, so the database is only decryptable by you on this machine. Optional master password on top.
- The database file can be placed in a synced folder (OneDrive, Syncthing, etc.) — DPAPI binding means another user account won't be able to decrypt it.
- **Known residual risk:** launching a session passes `--Password` on the TeamViewer command line. That value is visible to any process on the machine that can read the command line of another user-owned process during the brief launch window. This is inherent to the TeamViewer CLI and affects any launcher, including manually-typed commands. TeamStation will default to `--Base64Password` (still inspectable but obscured) and document this transparently.
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
109 tests cover crypto, inheritance, CSV/JSON parsing, the CLI/URI builders, and end-to-end SQLite repo operations against a temp DB.

## Why not just use mRemoteNG?

[mRemoteNG](https://github.com/mRemoteNG/mRemoteNG) is excellent, but it only supports RDP, VNC, SSH, Telnet, HTTP/S, rlogin, and raw TCP. TeamViewer's ID-based protocol is not a standard remote-desktop protocol and cannot be spoken by a generic client. The only way to drive TeamViewer is to hand the official `TeamViewer.exe` an ID and password — which is exactly what TeamStation does, while giving you the organizational UX mRemoteNG pioneered.

## Contributing

TeamStation is MIT-licensed. Issues and pull requests welcome once the v0.1.0 baseline is up. In the meantime, feature suggestions via GitHub Issues are the most useful contribution.

## License

[MIT](LICENSE)

<div align="center">

# TeamStation

**A focused, open-source connection manager for TeamViewer on Windows.**

Organize TeamViewer IDs and passwords in a nested folder tree, launch any saved peer with one click, and keep credentials encrypted at rest. Think mRemoteNG, but TeamViewer-only.

[![Version](https://img.shields.io/badge/version-0.0.6-blue)](CHANGELOG.md)
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

`v0.0.6` — Runtime inheritance + folder accent colors. An entry's mode, quality, access-control, and password can each be set to **(inherit from folder)** in the editor. On launch, TeamStation walks the folder chain upward and resolves each null field from the nearest ancestor that carries a default — so changing a folder's default password once updates every inheriting entry under it on the next launch. Schema moved to v2 with a tested v1→v2 migration path. Folder dots in the tree now render in each folder's chosen accent color. CSV import, search, and tray remain the gating features for `v0.1.0`. See [CHANGELOG.md](CHANGELOG.md).

## Planned feature highlights (v0.1.0)

- Nested folder/group tree with drag-to-reorder
- Per-entry fields: friendly name, TeamViewer ID, password, connection mode (Remote Control / File Transfer / Chat / VPN / Presentation), quality, notes, tags
- One-click launch via `TeamViewer.exe` CLI; optional `teamviewer10://` URI launcher as a fallback
- Encrypted credential storage: AES-256-GCM with a DPAPI-wrapped user-scoped key, stored in local SQLite
- Search, filter by tag, recent-connections tray menu
- CSV import (TeamViewer Management Console export format) and JSON export
- Embedded log panel, async launches, toast notifications
- Dark-first UI: Catppuccin Mocha by default, GitHub Dark and Light themes included

Full roadmap with P0 / P1 / P2 prioritization: [ROADMAP.md](ROADMAP.md).

## Security model

- Passwords are encrypted at rest with **AES-256-GCM**. The data-encryption key is wrapped by **Windows DPAPI** bound to the current user account, so the database is only decryptable by you on this machine. Optional master password on top.
- The database file can be placed in a synced folder (OneDrive, Syncthing, etc.) — DPAPI binding means another user account won't be able to decrypt it.
- **Known residual risk:** launching a session passes `--Password` on the TeamViewer command line. That value is visible to any process on the machine that can read the command line of another user-owned process during the brief launch window. This is inherent to the TeamViewer CLI and affects any launcher, including manually-typed commands. TeamStation will default to `--Base64Password` (still inspectable but obscured) and document this transparently.
- TeamStation never phones home. There is no telemetry, no update ping, no cloud account.

## Build

Requires:
- .NET 9 SDK (`9.0.313` or newer; pinned via [`global.json`](global.json))
- Windows 10 1809+ or Windows 11
- TeamViewer 15 Classic or TeamViewer Remote (full client) — only needed at runtime, not at build time

```powershell
dotnet restore
dotnet build -c Release
```

Run the WPF shell:
```powershell
dotnet run --project src/TeamStation.App -c Release
```

Run the launch-feasibility spike (needs a real TeamViewer peer you own):
```powershell
dotnet run --project tools/TvLaunchSpike -c Release -- --id <TV_ID> --password <PW>
```
It walks the CLI and URI-handler test matrix, captures operator observations, and writes `spike-report.md` next to the binary.

Release binaries will be published on the [Releases](../../releases) page once the first working build ships as `v0.1.0`.

## Why not just use mRemoteNG?

[mRemoteNG](https://github.com/mRemoteNG/mRemoteNG) is excellent, but it only supports RDP, VNC, SSH, Telnet, HTTP/S, rlogin, and raw TCP. TeamViewer's ID-based protocol is not a standard remote-desktop protocol and cannot be spoken by a generic client. The only way to drive TeamViewer is to hand the official `TeamViewer.exe` an ID and password — which is exactly what TeamStation does, while giving you the organizational UX mRemoteNG pioneered.

## Contributing

TeamStation is MIT-licensed. Issues and pull requests welcome once the v0.1.0 baseline is up. In the meantime, feature suggestions via GitHub Issues are the most useful contribution.

## License

[MIT](LICENSE)

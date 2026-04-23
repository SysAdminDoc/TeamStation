# TeamViewer launch reference

Implementation reference for the TeamStation launcher. Distilled from the official CLI docs, KB 34447, the REACH API guide, the `webapi.teamviewer.com/api/v1` schema, and the CVE-2020-13699 advisory. Cross-checked against local TV 15 install behavior.

---

## CLI parameters — `TeamViewer.exe`

| Flag | Short | Value | Notes |
| --- | --- | --- | --- |
| `--Minimize` | `-n` | — | Start minimized to tray |
| `--id` | `-i` | numeric | Partner ID. Aliases not accepted. |
| `--Password` | `-p` | plaintext | Avoid — leaks to shell history and command-line scrapers |
| `--PasswordB64` | `-B` | base64 | **Preferred.** Still inspectable during launch window. |
| `--mode` | `-m` | `fileTransfer`, `vpn` | **Only these two modes** via CLI. Chat, video-call, and presentation must use URI handlers. |
| `--quality` | `-q` | `1`–`5` | `1` auto / `2` quality / `3` speed / `4` custom / `5` undefined |
| `--ac` | `-a` | `0`–`3`, `9` | Access control: `0` full / `1` confirm-all / `2` view-show / `3` custom / `9` undefined |
| `--ProxyIP` | — | `ip:port` | |
| `--ProxyUser` | — | string | |
| `--ProxyPassword` | — | base64 | |
| `--play` | — | `<file.tvs>` | Replay recording. **Validate path, SMB/UNC risk — CVE-2020-13699 shape.** |
| `--control` | — | `<file.tvc>` | Control file |
| `--Sendto` | — | file path(s) | File-transfer shortcut |
| `assign` | — | sub-verb | Device assignment — see below |
| `api --install` / `--uninstall` | — | — | COM API registration (admin) |

### `assign` sub-parameters

`--api-token`, `--group`, `--group-id`, `--alias`, `--grant-easy-access`, `--reassign`, `--wait`, `--proxy`, `--proxy-user`, `--proxy-pw`, `--proxy-pwbase64`, `--retries`, `--timeout`, `--verbose` (macOS only).

---

## URI handlers

All URI handlers were flagged by CVE-2020-13699. The fix quoted argv but did not remove params — validate aggressively before invoking.

| Scheme | Action | Params | Notes |
| --- | --- | --- | --- |
| `teamviewer10://control` | Remote control (v10+) | `device`, `authorization` | Primary |
| `teamviewer8://` | Legacy control | same | Fallback for old clients |
| `teamviewerapi://` | API-triggered session | per REACH API | For ISV flows |
| `tvcontrol1://` | Control | `device`, `authorization` | Explicit mode |
| `tvfiletransfer1://` | File transfer | same | CLI also supports this mode |
| `tvchat1://` | Chat | same | Chat-only, no screen — **URI-only path** |
| `tvvpn1://` | VPN tunnel | same | CLI also supports this mode |
| `tvvideocall1://` | Video call | same | **URI-only path** |
| `tvpresent1://` | Presentation (host shares) | same | **URI-only path** |
| `tvsendfile1://` | Push file to partner | `device`, file | |
| `tvsqcustomer1://` / `tvsqsupport1://` | ServiceQueue customer/support | queue params | Support-queue shops |
| `tvjoinv8://` | Join existing session | session id | Meetings |

### Hardening rules (CVE-2020-13699)

Every URI launch goes through these checks before `ShellExecute`:

1. `device` must match `^\d{8,12}$`.
2. `authorization` must be reject-listed for `\`, `/`, `:`, whitespace, `--`, and starts with `-`.
3. Never hand a user-controlled string to `cmd.exe /c` or any shell. Use `argv`-array `Process.Start`.
4. Reject any URI containing `--play`, `--control`, `--Sendto`, or `\\UNC`.
5. Length-bound both params (ID ≤ 12, auth ≤ 256).

---

## Mode selection matrix

Which mechanism TeamStation picks for each connection mode:

| Mode | CLI supported? | URI supported? | TeamStation choice |
| --- | --- | --- | --- |
| Remote Control | No (only via default when `--mode` omitted) | Yes (`teamviewer10://control`) | **URI** — explicit, no ambiguity |
| File Transfer | Yes (`--mode fileTransfer`) | Yes (`tvfiletransfer1://`) | **CLI** — returns process handle we can track |
| VPN | Yes (`--mode vpn`) | Yes (`tvvpn1://`) | **CLI** |
| Chat | No | Yes (`tvchat1://`) | **URI (required)** |
| Video Call | No | Yes (`tvvideocall1://`) | **URI (required)** |
| Presentation | No | Yes (`tvpresent1://`) | **URI (required)** |
| Replay | Yes (`--play`) | No | **CLI** |

---

## Web API v1 — sync endpoints (P1)

Base URL: `https://webapi.teamviewer.com/api/v1`. Auth: bearer script token.

| Method | Path | Required scope | Purpose |
| --- | --- | --- | --- |
| GET | `/groups` | `Groups.Read` | List contact-list groups |
| GET | `/devices` | `Computers.Read` | List managed devices. `?groupid=` filter. Fields include `online_state` (paid tiers), `alias`, `device_id`. |
| GET | `/contacts` | `Contacts.Read` | Personal contact list |
| GET | `/reports/connections` | `Reports.Read` | Historical session log |

### Known quirks

- No official pagination story. Groups → devices fan-out needed for large tenants. Backoff + on-disk cache are P1 requirements.
- `online_state` populates reliably only on paid tiers. Free-tier users will see nulls; fall back to ICMP ping.
- `device_id` in the API (numeric, e.g. `d12345678`) is NOT the same as the TeamViewer ID used for launching. The remote-control peer ID lives in the `remotecontrol_id` field.

---

## Install-path discovery

Probe order for `TeamViewer.exe`:

1. `HKLM\Software\TeamViewer` → `InstallationDirectory` (64-bit)
2. `HKLM\Software\WOW6432Node\TeamViewer` → `InstallationDirectory` (32-bit)
3. `HKCU\Software\TeamViewer` → `InstallationDirectory` (per-user install)
4. `%ProgramFiles%\TeamViewer\TeamViewer.exe`
5. `%ProgramFiles(x86)%\TeamViewer\TeamViewer.exe`
6. Legacy `%ProgramFiles(x86)%\TeamViewer\Version*\TeamViewer.exe` (globbed, highest version wins)
7. Per-entry manual override

Also detect TV Host (`TeamViewer_Host.exe`) and Portable separately — they are not interchangeable for outgoing connections.

---

## Local data files worth reading

- `%AppData%\TeamViewer\Connections.txt` — outgoing connection history. Tab-delimited. One-time import source for v0.1.0.
- `%AppData%\TeamViewer\Connections_incoming.txt` — incoming sessions, useful for session-history backfill.
- `%AppData%\TeamViewer\tvinfo.ini` — local client metadata. Read-only; do not modify.

Computers & Contacts is **server-only** — there is no local cache file. The Web API sync is the only legitimate path to pull it offline.

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

---

## TeamViewer CVE registry (v0.4.0)

TeamStation ships a small, **static, offline-only** registry of publicly disclosed TeamViewer client CVEs at `src/TeamStation.Launcher/assets/cve/teamviewer-known.json`. It is embedded in `TeamStation.Launcher.dll` at build time and consumed by `TeamViewerCveRegistry.Default` plus `TeamViewerSafetyEvaluator` to drive the status-bar version chip and the "Update available" pill.

**The registry is advisory only.** TeamStation never auto-updates the installed TeamViewer client and never makes a network call to fetch or refresh CVE data — the JSON file is the entire dataset.

### Schema (v1)

```json
{
  "schema_version": 1,
  "description": "...",
  "last_updated": "YYYY-MM-DD",
  "source": "Curated by TeamStation maintainers ...",
  "entries": [
    {
      "id": "CVE-YYYY-NNNNN",
      "title": "Short human title",
      "cvss": 7.2,
      "severity": "high",
      "published": "YYYY-MM-DD",
      "summary": "Two-to-four-sentence operator-facing summary.",
      "remediation": "Update to TeamViewer X.Y.Z or later.",
      "remediation_url": "https://www.teamviewer.com/.../security-bulletins/",
      "fixed_in": "X.Y.Z",
      "affected": [
        { "min_inclusive": "A.B.C", "max_exclusive": "X.Y.Z" }
      ]
    }
  ]
}
```

`affected` ranges are half-open: `min_inclusive <= version < max_exclusive`. Either bound may be omitted (e.g. an entry with no upper bound has not yet been fixed). An entry whose only ranges are unparseable is dropped on load with a diagnostic — the rest of the registry continues to load.

### Maintainer update procedure

1. Read the upstream TeamViewer security bulletin and the matching MITRE / NVD entry.
2. Append a new object to `entries[]` in `assets/cve/teamviewer-known.json`. Preserve existing entries — they remain useful for older installs that never updated.
3. Bump the top-level `last_updated` field.
4. Run `dotnet test TeamStation.sln -c Release` — the registry tests parse the bundled JSON and will fail on any malformed range.
5. Tag a patch release and ship a normal binary; users get the new advisory as part of the regular update.

### Operator-facing surface

- **Status-bar version chip** — `TeamViewer 15.71.5` or `TeamViewer not detected`. Tooltip lists every matched CVE with fixed-in version and remediation URL.
- **Yellow "Update available" pill** — appears only when `TeamViewerSafetyEvaluator.Evaluate` returns `Vulnerable`. Tooltip is the same as the chip's, so the operator can hover either.
- **Activity log warning** — single line on startup naming the matched CVE IDs and the recommended `fixed_in` baseline.

### Failure modes

| Scenario | Result | UI behaviour |
| --- | --- | --- |
| Embedded JSON missing | `TeamViewerCveRegistry.Default.LoadDiagnostics` carries a single message; `Match()` returns empty | Status bar reverts to "version chip only, no pill"; safety state = `Unknown` |
| JSON parse failure | Registry treated as empty with a parse-error diagnostic | Same as above |
| Individual entry malformed | That entry is skipped; remaining entries load | Other CVEs still surface; the bad row is recorded in `LoadDiagnostics` for diagnostics but is not shown in UI |
| No installed TeamViewer detected | Safety state = `NotDetected`; chip reads "TeamViewer not detected" | No pill |

---

## TeamViewer binary provenance (v0.4.0)

`TeamViewerBinaryProvenanceInspector.Inspect()` runs once at startup and classifies the resolved `TeamViewer.exe` along four dimensions:

1. **Existence** — does the resolved path point at a real file?
2. **Install root** — does the path sit under `\Program Files\TeamViewer` or `\Program Files (x86)\TeamViewer` (or a portable / per-user install elsewhere)?
3. **Authenticode signature** — does `WinVerifyTrust` validate the signature against the local trust store? Distinguishes `Trusted` / `Untrusted` / `Unsigned` / `UnableToVerify` (the last covers offline-CRL situations and is treated as advisory, not alarming).
4. **Publisher subject** — does the signing certificate's subject contain the literal `"TeamViewer"` (case-insensitive)? Defends against renamed or side-loaded executables.

The result is collapsed to a `TeamViewerProvenanceHealth` enum surfaced in the activity log:

| Health | Meaning | Operator action |
| --- | --- | --- |
| `NotFound` | No file at the resolved path | Install TeamViewer or set the path in Settings |
| `SignedByExpectedPublisher` | Signed, expected publisher, standard root | None — green-light |
| `SignedOutsideExpectedRoot` | Signed by TeamViewer but path is unusual | Confirm the install matches your deployment expectations |
| `SignedByUnexpectedPublisher` | Signed but subject does not contain "TeamViewer" | Confirm the path points at the genuine client |
| `UnsignedOrUntrusted` | No signature, or signature failed local trust | Confirm the install came from teamviewer.com |
| `UnableToVerify` | `WinVerifyTrust` could not run (offline, API failure) | Treat as advisory; not a launch blocker |

**This is not anti-malware.** A determined attacker who can drop a signed binary on the same machine still passes. The check exists to catch the much more common "operator pointed Settings at the wrong path" and "renamed stub launcher from a different vendor" scenarios.

TeamStation **never blocks launch** on a failed provenance check. The activity log carries the verdict, and the Trust Center dialog (`Tools -> Trust Center...`) renders the result alongside the CVE state.

---

## Trust Center dashboard (v0.4.0)

`Tools -> Trust Center...` opens a read-only health snapshot composed of six panels. Every value is local: nothing is uploaded, nothing is fetched. The dialog uses calm sysadmin tone (4 status pills — `OK` / `CHECK` / `ACTION` / `INFO`) and never blocks launch.

| Panel | Source | Action tone |
| --- | --- | --- |
| TeamViewer client safety | `TeamViewerSafetyEvaluator.Evaluate` against the bundled CVE registry | `Action` for vulnerable / not-detected, `Healthy` for safe, `Info` for unknown |
| TeamViewer.exe provenance | `TeamViewerBinaryProvenanceInspector.Inspect` (Authenticode + publisher + install root) | Maps from `TeamViewerProvenanceHealth` |
| Local database | File metadata + `StoragePaths.IsPortable` | `Info` when no DB yet, `Healthy` otherwise |
| Encrypted mirror | Mirror file `LastWriteTime` against a 7-day stale threshold | `Info` when not configured, `Caution` when stale, `Healthy` when fresh |
| CVE registry | `TeamViewerCveRegistry.Default` entry count + diagnostics | `Caution` when diagnostics present, `Healthy` for clean load |
| TeamViewer Web API | Token presence (never the value) | `Info` when not configured, `Healthy` when set |

Pure logic lives in `TrustCenterReportFactory.Build` so unit tests can pump synthetic probes through without spinning up Win32; `TrustCenterViewModel` owns the IO at runtime and exposes a `RefreshCommand` so the operator can re-probe without closing the dialog.

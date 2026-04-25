# TeamViewer capability matrix

Last reviewed: 2026-04-25.

This matrix records the TeamViewer control surfaces TeamStation can safely
orchestrate, the proof status for each surface, and the product boundary around
that surface. It is intentionally conservative: TeamStation should expose only
behavior that is implemented locally or verified against current TeamViewer
clients and official API contracts.

## Proof states

| State | Meaning |
| --- | --- |
| Implemented | TeamStation has code and tests for the local orchestration path. |
| Documented | Official docs describe the surface, but TeamStation does not yet automate it. |
| Needs lab proof | A current TeamViewer install, licensed account, or owned peer is required before TeamStation can claim the behavior. |
| Rejected | Outside the TeamStation charter or unsafe without official support. |

## Matrix

| Surface | TeamStation status | License or tier | Token or scope | Local dependency | Credential behavior | Observed or expected failure mode | Proof status | Next action |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `TeamViewer.exe --id` with `--PasswordB64` | Implemented launcher default when CLI is selected | Full TeamViewer client | None | Resolved `TeamViewer.exe` | Password is base64 in argv during the launch window unless clipboard-password mode is enabled | TeamViewer may still prompt depending on Easy Access, policy, or commercial-use state | Implemented, needs recurring lab proof | Run `tools/TvLaunchSpike` before release claims |
| `--mode fileTransfer` and `--mode vpn` | Implemented through the launcher route planner | Full TeamViewer client | None | Resolved `TeamViewer.exe` | Same command-line password risk as CLI remote control | Mode can prompt or fail if client support changes | Implemented, needs recurring lab proof | Keep in spike matrix |
| `--quality`, `--ac`, proxy flags | Implemented when executable launch is required | Full TeamViewer client | None | Resolved `TeamViewer.exe` | Proxy password is base64 in argv when set | Out-of-range DB values are skipped by builder tests; TeamViewer may reject unsupported values | Implemented | Keep validator and argv tests current |
| Registered URI handlers (`teamviewer10://control`, `tvfiletransfer1://`, `tvvpn1://`, `tvchat1://`, `tvvideocall1://`, `tvpresent1://`) | Implemented for forced protocol launch and URI-only modes | Installed TeamViewer protocol handlers | None | Shell protocol registration | `authorization=` remains inspectable in the URI handoff | Current TeamViewer clients may ignore `authorization=` after security patches | Implemented, needs lab proof | Complete protocol-handler validation matrix |
| Web Client handoff | Implemented as browser open plus TeamViewer ID copy | TeamViewer web account as required by TeamViewer | Browser sign-in, no TeamStation token | Default browser | TeamStation copies ID only; no password is sent to the browser | No stable official direct-ID URL contract | Implemented | Keep direct-ID URL automation rejected until documented |
| Web API groups and devices | Implemented read-only sync into synthetic `TV Cloud` folder | TeamViewer account with matching API permissions | Script token with group/device read permissions | HTTPS to TeamViewer API when user invokes sync | API token stored DPAPI-protected; never displayed | Pagination, tier limits, and `online_state` availability vary by account | Implemented basic sync; deeper behavior needs account proof | Add pagination/backoff and capability logging |
| Web API connection reports | Not implemented | TeamViewer account with reports access | Reports read scope | HTTPS to TeamViewer API | No local credential injection | Endpoint availability and fields vary by tier | Documented | Add read-only reports after token capability checks |
| Service cases and session links | Not implemented | TeamViewer account and product support | Session/service-case scopes | HTTPS to TeamViewer API and web app | No TeamStation password handling unless official docs require it | API-started unattended session behavior not proven | Documented, needs lab proof | Do not claim unattended API launch until licensed test proves it |
| SCIM user provisioning | Not implemented | Company administrator / IdP configuration | SCIM user-management scopes | HTTPS to TeamViewer SCIM API | No TeamStation connection credentials | User lifecycle only; not a connection-launch surface | Documented | Keep as docs-only unless enterprise policy support is added |
| Deployment and assignment commands (`assign` / `assignment`) | Spec only | Depends on TeamViewer deployment model and tenant setup | Assignment token or API token | Resolved installer or `TeamViewer.exe` | Tokens must be redacted in previews/logs; command is setup-time, not session launch | `--grant-easy-access`, group assignment, and offline assignment vary across client versions and tenant settings | Documented, needs lab proof | Build guarded dry-run helper from `teamviewer-deployment-helper.md` |
| Remote Management scripts / terminal | Not implemented | TeamViewer Remote Management licensing | Capability-specific scopes | TeamViewer APIs or web app | No local TeamStation credential injection | API contract and account capabilities are not locally proven | Needs lab proof | Prefer "open/manage in TeamViewer web app" until verified |
| COM API registration (`TeamViewer.exe api --install`) | Not implemented | Full client and admin registration | None known | Registered COM server | No password handling by TeamStation until contract is verified | Current COM control contract is not proven | Needs lab proof | Add diagnostics only after registration proof |
| `.tvc` control files and `.tvs` replay | Not implemented | Full TeamViewer client | None | Local file and `TeamViewer.exe` | File path only; no stored password required by TeamStation | Path validation and UNC restrictions are security-sensitive | Documented | Keep rejected from launch surface until hardened path contract exists |
| File-transfer handoff (`--Sendto`, `tvsendfile1://`) | Not implemented | Full TeamViewer client | None | File path and TeamViewer surface | File paths only; credential behavior must be verified | Unsafe path/UNC shapes mirror CVE-2020-13699 risk class | Needs lab proof | Require strict path validation before coding |
| Browser automation against TeamViewer web UI | Rejected | N/A | N/A | Browser automation | Would risk credential/session injection into unstable UI | Brittle, hard to audit, and explicitly outside charter | Rejected | Keep rejected |
| Hidden sessions, protocol reverse engineering, relay/agent backend | Rejected | N/A | N/A | N/A | N/A | Violates the TeamStation charter and abuse-resistance posture | Rejected | Keep rejected |

## Source basis

- [TeamViewer API overview](https://www.teamviewer.com/en-us/global/support/for-developers/) documents Web API categories, token models, reports, sessions, and Computers & Contacts use cases.
- [TeamViewer Web API docs](https://webapi.teamviewer.com/api/v1/docs/index) remain the canonical endpoint index, even though the public page is sparse without interactive schema expansion.
- [TeamViewer device groups explainer](https://support.teamviewer.com/en/support/solutions/articles/75000128834-device-groups-explained) separates personal devices, company devices, legacy/bookmarked groups, and managed device groups.
- [mRemoteNG External Tools](https://mremoteng.readthedocs.io/en/latest/user_interface/external_tools.html) and [Royal TS Command Tasks](https://docs.royalapps.com/r2021/royalts/reference/tasks/command.html) are useful workflow references for future runbook/task-template work, especially variable expansion and confirmation policy.
- [Devolutions logs, reports, and audits](https://docs.devolutions.net/rdm/concepts/advanced-concepts/logs-reports-audits/) reinforces the evidence direction: local launch history, audit events, and reports should become reviewable artifacts.

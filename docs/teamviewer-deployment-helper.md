# TeamViewer deployment helper spec

Last reviewed: 2026-04-25.

This is the design contract for future TeamViewer deployment and assignment
helpers. It is a spec, not an implementation: setup-time automation is in scope
only when it builds transparent, auditable commands for supported TeamViewer
deployment paths. It must never become a session-launch bypass.

## Scope

Allowed first version:

- Build dry-run commands for `TeamViewer.exe assign` and `TeamViewer.exe assignment`.
- Build MSI `ASSIGNMENTOPTIONS` strings for TeamViewer Host deployment notes.
- Redact tokens and proxy passwords in previews, logs, audit rows, and Evidence Pack output.
- Capture retry/timeout values and whether Easy Access is requested.
- Record the command family, target group strategy, and proof status.

Out of scope:

- Running installers silently without explicit operator confirmation.
- Browser automation against the TeamViewer web app.
- Hidden sessions or unattended session start flows not documented by TeamViewer.
- Storing assignment/API tokens as reusable connection passwords.
- Inferring tenant capability from command syntax alone.

## Inputs

| Field | Required | Validation | Notes |
| --- | --- | --- | --- |
| Command family | Yes | `assign`, `assignment`, or `msi-assignmentoptions` | Persist the family so previews stay reproducible. |
| TeamViewer path | Yes for exe commands | Existing local path or app resolver result | Run through the existing provenance inspector before execution. |
| Assignment token or API token | Yes | Non-empty, max 512 chars | Always redact as `<redacted-token>` outside the in-memory command builder. |
| Group name | No | Non-empty, no control chars | Mutually exclusive with group ID unless TeamViewer docs prove combined use. |
| Group ID | No | `g` plus digits, or docs-proven current shape | Prefer group ID for stable deployment. |
| Alias | No | Non-empty, max 128 chars | Allow `%COMPUTERNAME%` as a documented deployment variable. |
| Grant Easy Access | No | Boolean | Must be explicit because tenant/license behavior varies. |
| Reassign existing device | No | Boolean | Must be explicit; include in audit summary. |
| Proxy endpoint | No | Reuse launcher proxy endpoint validator | Do not accept shell metacharacters. |
| Proxy user | No | Non-empty, max 128 chars | Redact only if policy marks user names sensitive. |
| Proxy password | No | Max 256 chars | Prefer base64 form only when TeamViewer command supports it. |
| Retries | No | 0-10 | Default should be visible in preview. |
| Timeout seconds | No | 5-300 | Default should be visible in preview. |

## Builder contract

The eventual implementation should expose a pure builder first:

```csharp
public sealed record DeploymentCommandPreview(
    string Family,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<string> RedactedArguments,
    string DisplayCommand,
    string RedactedDisplayCommand,
    IReadOnlyList<string> Warnings);
```

Rules:

- Build argv arrays, not shell strings.
- Treat the redacted form as the default UI/log form.
- Do not include empty flags.
- Preserve argument order so dry-run output can be compared with support docs.
- Emit warnings for unproven combinations such as Easy Access without a managed group proof.
- Execution must require an explicit confirmation dialog that shows the redacted command and proof status.

## Dry-run output

The UI should show:

- Command family and resolved TeamViewer path.
- Redacted display command.
- Group strategy: name, ID, or none.
- Easy Access / reassign / retry / timeout flags.
- Provenance status for the resolved executable.
- Required operator proof: client version, tenant type, expected group placement.
- "Copy redacted command" and "Copy full command" as separate explicit actions; the full command action requires confirmation.

Example redacted output:

```text
"C:\Program Files\TeamViewer\TeamViewer.exe" assign --api-token <redacted-token> --group-id g123456789 --alias %COMPUTERNAME% --grant-easy-access --reassign --retries 3 --timeout 30
```

MSI deployment notes should build only the `ASSIGNMENTOPTIONS` segment by
default, then show how an operator can place it into their own deployment tool:

```text
ASSIGNMENTOPTIONS="--group-id g123456789 --alias %COMPUTERNAME% --grant-easy-access --reassign --retries 3 --timeout 30"
```

## Audit events

Future execution should write a local audit event with:

- Event name: `deployment_command_previewed`, `deployment_command_copied`, or `deployment_command_executed`.
- Command family.
- Redacted display command.
- TeamViewer path and provenance health.
- Whether the operator copied a full command containing secrets.
- Result code and elapsed time if executed.

No token value, proxy password, or full command containing secrets should be
stored in the audit log.

## Lab-proof checklist

Before the helper graduates from spec to executable workflow:

1. Run commands against a disposable TeamViewer Host in a lab tenant.
2. Verify `assign` vs `assignment` syntax on the currently supported TeamViewer version.
3. Verify Easy Access behavior with and without managed/device groups.
4. Verify retries and timeout behavior while offline.
5. Verify Intune/MSI quoting of `ASSIGNMENTOPTIONS`.
6. Confirm TeamViewer account/tier prerequisites and document any unsupported free-tier behavior.
7. Confirm command output and logs do not echo unredacted tokens.

## Source basis

- [TeamViewer API overview](https://www.teamviewer.com/en-us/global/support/for-developers/) describes script/app token models and API categories.
- [TeamViewer device groups explainer](https://support.teamviewer.com/en/support/solutions/articles/75000128834-device-groups-explained) explains personal, company, legacy/bookmarked, and managed device groups.
- [TeamViewer Web API docs](https://webapi.teamviewer.com/api/v1/docs/index) are the endpoint reference to validate token scopes and report/device availability.

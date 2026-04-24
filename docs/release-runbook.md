# TeamStation release runbook

This is the operator checklist for cutting a new TeamStation release. Follow
it top-to-bottom. Nothing in this list requires a second engineer; everything
is reversible up to the point where the GitHub Release is published.

## 0. Prerequisites

- Latest `main` checked out locally
- `gh` authenticated against the `SysAdminDoc/TeamStation` repository
- TeamViewer 15.x installed on the local box (for the must-spike pass)
- A disposable TeamViewer peer you control (VM, spare device, lab machine)

## 1. Run the launch-feasibility spike

Pre-release, verify the two launch behaviors that gate the seamless-launch UX
claim. Skip only if the spike was run within the last two TeamViewer releases
and nothing in `TeamStation.Launcher/` has changed since.

```powershell
dotnet run --project tools/TvLaunchSpike -c Release -- --id <TV_ID> --password <PW>
```

Output lands in `spike-report.md` next to the binary. Look for:

| Question                                                   | Expected       | If different                                                          |
| ---------------------------------------------------------- | -------------- | --------------------------------------------------------------------- |
| CLI `--PasswordB64` silent for Remote Control              | silent connect | Document the new TV version + prompt behavior in [`CLAUDE.md`](../CLAUDE.md) **Must-spike** section |
| CLI `--mode fileTransfer` / `--mode vpn`                    | silent connect | Same — downgrade those modes to URI-only in the launcher              |
| URI handlers (`teamviewer10`, `tvfiletransfer1`, …) accept `?authorization=` | silent connect | Mark the affected modes as CLI-only in `UriSchemeBuilder.IsUriOnly`   |

Commit the regenerated `spike-report.md` only if something observably changed.

## 2. Sync versions in a single commit

Every version string must match. When bumping to `X.Y.Z`:

- [`Directory.Build.props`](../Directory.Build.props): `<Version>`, `<AssemblyVersion>`, `<FileVersion>`, `<InformationalVersion>`
- [`README.md`](../README.md): shields.io version badge
- [`CHANGELOG.md`](../CHANGELOG.md): new entry at the top dated today
- [`CLAUDE.md`](../CLAUDE.md): Version history section + Status checklist
- Any memory files that reference the app version

Verify with a quick grep — no stray `0.1.1` references, no `-v2` / `-fixed`
suffixes, no unresolved `TODO:` markers:

```bash
grep -rn "0\.1\.1" src README.md CHANGELOG.md CLAUDE.md
```

## 3. Build + test locally

```powershell
dotnet restore
dotnet build TeamStation.sln -c Release
dotnet test TeamStation.sln -c Release
```

All tests must pass on the exact commit that becomes the tag.

## 4. Smoke-test the published exe

```powershell
dotnet publish src/TeamStation.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=embedded -o publish/win-x64

.\publish\win-x64\TeamStation.exe
```

Exercise at minimum: create a folder, create an entry, edit an entry, launch
(against the spike peer), open Settings, toggle the clipboard-password mode,
close to tray, restore from tray, exit.

## 5. Refresh screenshots if the UI changed

Per [`docs/screenshots/README.md`](screenshots/README.md), re-capture any
screens that drifted. Commit alongside the version bump.

## 6. Push + trigger the release workflow

```bash
git push origin main
gh workflow run release.yml -f version=0.2.0
```

The workflow tags `v0.2.0`, publishes a self-contained single-file
`TeamStation.exe`, zips it with LICENSE/README/CHANGELOG, and uploads both
assets to a new GitHub Release.

## 7. Post-release verification

- Download the release ZIP on a clean VM and confirm the exe runs
- Confirm the tag and artifacts appear on the Releases page
- Confirm CI stays green on the post-tag commit

## Rollback

- If the release ships with a broken binary, mark the Release as a draft and
  delete the bad asset from the tag page. Tag history stays — the next release
  is `0.X.Y+1`, not a re-tag of the same version.
- Never force-push to `main` or delete a published tag.

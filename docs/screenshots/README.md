# Screenshots

These PNGs are consumed by the top-level `README.md` **Screens** section. They
must be captured DPI-aware so small text stays readable on the repo page.

## Capture process (Windows 11 @ 125% DPI)

```powershell
dotnet run --project src/TeamStation.App -c Release
```

For each shot, set the window to the documented size, focus the target panel,
and capture via a DPI-aware snipping tool (the built-in Snipping Tool works
once the dev has turned **Settings → Display → Scale → 100%** in the capture
session — or use a PowerShell `SetProcessDPIAware()` wrapper).

## Required files

| Filename                  | Window size | Focus                                                 |
| ------------------------- | ----------- | ----------------------------------------------------- |
| `tree.png`                | 1420x860    | Main window, folder tree populated, one folder open   |
| `entry-editor.png`        | 760x680     | Entry editor dialog — all inheritance toggles visible |
| `quick-connect.png`       | 1420x860    | Main window with quick-connect bar filled in          |
| `settings.png`            | 760x700     | Settings dialog — clipboard-password toggle ticked    |

## Re-capture triggers

Any of the following require a re-capture:

- Theme palette change (Catppuccin / GitHub Dark / Light)
- New top-level field or tree element
- Status-bar or log-panel layout change
- Settings dialog grows a new section

Don't wait for a release; stale screenshots mislead users.

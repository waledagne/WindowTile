# Verification — 18 October 2026

Release build: KeyTile 0.1.0, C# / WPF, net10.0-windows.

## Thirds increment

- `Build.ps1` built and published with zero warnings/errors; all 105 checks passed.
- Added a Thirds tab with left, center, and right columns (Ctrl + Alt + 5–7) plus left and right two-thirds tiles (Ctrl + Alt + 8–9), using the number row.
- Geometry checks cover widths divisible by three and both remainder cases, negative monitor origins, and portrait work areas. The three columns partition the full work area; each wider tile aligns with its complementary third.
- Native WPF and WinForms fixtures fit all five tiles. A sequence across thirds, corners, halves, and maximize restores the original placement. Third and two-thirds commands handle initially maximized windows and restore their maximized state and normal bounds.
- Third and two-thirds shortcut conflicts leave the other twelve shortcuts available. Pause and disposal release the new shortcuts alongside existing ones.
- Fixed-size windows reject column and two-thirds commands.
- Visually inspected the published Thirds tab and tested all five shortcuts through Windows Computer Use. Native measurements confirmed 640 × 1032 columns at x = 0, 640, and 1280, plus 1280 × 1032 wider tiles at x = 0 and 640, on a 1920 × 1032 work area. Ctrl + Alt + Down restored the original (180, 110), 620 × 400 placement.
- The Thirds tab displays Paused for all five rows when paused. An independent probe acquired all thirteen shortcuts while paused and confirmed all thirteen were owned by KeyTile after resume.

## Corners increment

- `Build.ps1` built and published with zero warnings/errors; all 61 checks passed.
- Added Basics and Corners tabs, with global Ctrl + Alt + 1–4 number-row shortcuts for the four quarters.
- Quarter geometry covers even and odd dimensions, negative origins, and portrait-shaped work areas without gaps or overlap.
- Native WPF and WinForms fixtures fit all four quarters. Corners → half → maximize → restore preserves the original position; corner snapping also restores an initially maximized window correctly.
- A corner shortcut conflict leaves the other seven shortcuts available. Pause and disposal release corner and basic shortcuts.
- Fixed-size windows reject corner commands.
- Visually inspected both tabs and verified the published app's Ctrl + Alt + 1–4 shortcuts through Windows Computer Use. Native measurements confirmed 960 × 516 frames at (0, 0), (960, 0), (0, 516), and (960, 516) on the 1920 × 1032 work area. Ctrl + Alt + Down restored the fixture's original (180, 110), 620 × 400 placement.
- The Corners panel shows Paused for all four rows when paused. An independent hotkey probe acquired all eight keys while paused and none after resume.

The sections below record the original prototype verification. The current automated output is in `artifacts\test-results.txt`.

## Project-local development setup

The relocated project was verified using only its own `.tools\dotnet` SDK 10.0.402 and Desktop Runtime 10.0.13. Its scripts resolve their locations relative to the project folder; the pinned SDK, CLI state, package cache, and published output all live inside that folder.

- Forced restore regenerated project/package paths for the relocated project.
- `Build.ps1` built with zero warnings/errors, passed all 31 checks, and published to `artifacts\app`.
- `Run.cmd` launched the published app using Windows PowerShell 5.1.
- Inspection of the running process confirmed `hostfxr.dll`, `coreclr.dll`, and `wpfgfx_cor3.dll` all loaded from the project's `.tools\dotnet` directory.
- Test and publish guards produced clear instructions when the app was already running.
- Source and script scans found no absolute paths to the previous development workspace.

The latest automated check output is in `artifacts\test-results.txt`. Runtime module paths are recorded in `artifacts\local-runtime-verification.json`.

## Build

- Built with the project-local .NET SDK 10.0.402.
- Release build and publish succeeded with zero warnings and zero errors.
- Published framework-dependent Windows executable launches through `Run.ps1` using the project's own .NET 10 Desktop Runtime.

## Automated checks

All 31 checks passed. The dependency-free verification program covers:

- Half-layout geometry, odd widths, negative coordinates, and portrait-shaped work areas.
- Shortcut conflicts, partial registration, pause, resume, and disposal cleanup.
- Actual visible frame positions for left/right snapping to the monitor work area.
- Native maximized state.
- Original placement restored after left → right → maximize.
- Repeated restore, initially maximized windows, and native restore without a saved placement.
- Independent histories for WPF and WinForms windows.
- Fixed-size, invalid, and closed targets.

## Interactive end-to-end checks

Verified the published executable using Windows Computer Use and a separate test window. Native window measurements independently confirmed the results:

| Check | Result |
| --- | --- |
| Ctrl + Alt + Left | Visible frame at (0, 0), 960 × 1032 |
| Ctrl + Alt + Right | Visible frame at (960, 0), 960 × 1032 |
| Ctrl + Alt + Up | Native maximized state confirmed |
| Ctrl + Alt + Down | Original bounds (180, 110), 620 × 400 restored |
| Hide to tray | Panel hidden; process and shortcuts remain active |
| Second launch | Existing panel reopened; second process exits |
| Pause | All four keys successfully acquired by an independent probe |
| Resume | Independent probe confirms all four keys are owned by KeyTile |
| Exit | Process exits; independent probe can acquire all four keys |

The WPF panel was visually inspected in active and paused states. Temporary test windows were closed after verification.

## Not yet covered

Physical mixed-DPI / multi-monitor combinations, monitor removal during a command, elevated target apps, remote desktop, and a broad application compatibility matrix. Geometry tests cover synthetic negative monitor origins; they do not substitute for physical display tests.

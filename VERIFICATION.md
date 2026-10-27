# Verification — 28 October 2026

Current release: KeyTile 0.4.0, C# / WPF, net10.0-windows, Windows x64.

## Individual shortcut enable/disable

- All 285 automated checks pass. The Release build has zero warnings/errors.
- Added an Enabled checkbox to each editor row, retaining its remembered combination when disabled. Disabled actions are excluded from registration and conflict checks. The main panel distinguishes Disabled from Paused, and an all-disabled profile displays ALL OFF.
- Profile version 2 persists enabled flags. Tests verify backward-compatible loading of version 1 custom bindings without rewriting the file, disabled-state reload, validation of explicit version 2 flags, preserved data after rejection, all-disabled profiles, and restoring enabled defaults.
- Native registration checks confirm that disabling one action frees only its combination, occupied disabled combinations do not block saving, re-enabling an occupied combination fails before persistence, save failures restore enabled flags, and Pause/Resume preserves individual choices. An all-disabled profile frees all seventeen combinations without reporting conflicts.
- These results cover automated settings, editor-row notifications, and native registration tests. The 0.4.0 installed UI walkthrough has not been completed.

## Customizable shortcuts (0.3.0 increment)

- Release build succeeds with zero warnings/errors. All 242 automated checks pass.
- Added a category-based editor for all seventeen actions, with shortcut capture, Save, Cancel, and Restore defaults. The main panel displays the active shortcut labels.
- Settings tests cover missing files, persistent custom keys/modifiers, generated labels, duplicate and incomplete action sets, invalid IDs/actions/keys/modifiers, malformed and oversized JSON, recovery through defaults, and preserving previous data on rejected saves.
- Native registration tests verify releasing an old shortcut, owning its replacement, rejecting another window's occupied shortcut before persistence, restoring previous registrations after conflicts or save errors, and retaining the paused state when saving or resetting defaults.
- Personal settings live outside the installation folder in `%LOCALAPPDATA%\KeyTile\shortcuts.json`; missing or invalid profiles fall back to defaults, and invalid files remain untouched until a valid save.
- Checked the installed 0.3.0 editor through Windows Computer Use: duplicate combinations disable Save, recording Ctrl + Alt + Shift + J works, Cancel leaves the profile untouched, and Save updates the main panel and JSON file. The custom Left shortcut moved a real test window to the exact (0, 0), 960 × 1032 left half, and Restore returned its original placement. The custom label survived an app restart.
- Restore defaults and Save returned the profile to its defaults while preserving Pause. An independent native probe acquired all seventeen combinations while paused and confirmed KeyTile owned all seventeen after Resume. The app's startup checkbox remained enabled after the installer upgrade.

## Installation and sign-in startup (0.2.0 increment)

- All 175 application checks passed, including startup path validation, quiet tray arguments, enable/disable, long-path rejection, and preserving other apps' or another KeyTile copy's shortcuts. Startup tests use an isolated temporary folder.
- Standalone publishing bundles the .NET runtime and WPF native libraries in `KeyTile.exe`. MSI and portable ZIP builds include runtime license notices and SHA-256 hashes.
- The installer targets the current user's `%LOCALAPPDATA%\Programs\KeyTile`, creates desktop and Start menu shortcuts, and adds a Windows Startup-folder shortcut for `KeyTile.exe --tray`. The startup checkbox controls the current executable's shortcut.
- Installer lifecycle checks cover deployment to a path containing spaces, both app shortcuts, startup target/arguments, loading bundled WPF libraries without a project or machine-wide runtime, quiet duplicate launches, repair with startup on/off, initial opt-out and its repair, and uninstall cleanup after enabling startup following an opt-out.
- A second `--tray` launch exits quietly without reopening the existing panel.
- Final UI check on 27 October: the installed app displays the startup checkbox correctly. Turning it off removes the Startup-folder shortcut; turning it on restores the installed executable with `--tray`. Hide to tray keeps one hidden instance running, a duplicate tray launch stays quiet, and a normal launch reopens the existing panel.

Earlier feature increments below describe their original 0.1.0 builds.

## Window Placement increment

- `Build.ps1` built and published with zero warnings/errors; all 166 checks passed.
- Added Window Placement: Center keeps the visible size, while Small, Medium, and Large use 50%, 70%, and 90% of the current work-area width and height and center the result. Shortcuts use Ctrl + Alt + Shift + C/S/M/L.
- Geometry checks cover odd dimensions, negative monitor origins, portrait work areas, oversized windows, and nonzero preset dimensions.
- WPF and WinForms fixtures match all four placements. Center preserves preset sizes, and sequences across placement, tiles, and maximize restore the original bounds. Initially maximized windows return to their saved maximized state.
- Fixed-size windows support Center and Restore but reject resize presets. A minimum-width fixture rejects a preset it cannot fit and still restores the saved original placement.
- Conflict, pause, resume, and disposal checks cover all seventeen shortcuts, including per-binding Shift modifiers.
- Visually inspected the published Window Placement tab and tested all four shortcuts through Windows Computer Use. On the 1920 × 1032 work area, Center kept the fixture's 606 × 393 visible size at (657, 319). Small, Medium, and Large produced centered visible frames of 960 × 516 at (480, 258), 1344 × 722 at (288, 155), and 1728 × 928 at (96, 52). Repeating Medium kept the same bounds, and Restore returned to the original (180, 110), 620 × 400 placement.
- All four placement rows show Paused while paused. An independent probe acquired all seventeen shortcuts while paused and confirmed they were all owned by KeyTile after resume.

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

# WindowTile

A C# / WPF window manager prototype for Windows 11. It moves the focused app window using eight global keyboard shortcuts, organized into **Basics** and **Corners** tabs. All shortcuts stay active regardless of the selected tab. The executable and tray app are currently named **KeyTile**.

## Basics

| Shortcut | Action |
| --- | --- |
| Ctrl + Alt + Left | Snap to the left half of the current monitor |
| Ctrl + Alt + Right | Snap to the right half of the current monitor |
| Ctrl + Alt + Up | Maximize |
| Ctrl + Alt + Down | Restore the position and state saved before the first snap or maximize |

## Corners

| Shortcut | Action |
| --- | --- |
| Ctrl + Alt + 1 | Snap to the top-left quarter of the current monitor |
| Ctrl + Alt + 2 | Snap to the top-right quarter of the current monitor |
| Ctrl + Alt + 3 | Snap to the bottom-left quarter of the current monitor |
| Ctrl + Alt + 4 | Snap to the bottom-right quarter of the current monitor |

Use the number row above the letters, rather than the numeric keypad. Corners divide the monitor's work area into four tiles, excluding the taskbar. Odd pixel dimensions give the extra pixel to the right or bottom tiles. Restore works across corners, halves, and maximize in the same sequence.

## Clone and set up

Requires Windows 11 x64 and Git. Run these commands in PowerShell to clone the source and install the pinned .NET SDK inside the project:

```powershell
git clone https://github.com/waledagne/WindowTile.git
cd WindowTile

New-Item -ItemType Directory -Force -Path .\.tools | Out-Null
Invoke-WebRequest -UseBasicParsing https://dot.net/v1/dotnet-install.ps1 -OutFile .\.tools\dotnet-install.ps1
$sdkVersion = (Get-Content -Raw .\global.json | ConvertFrom-Json).sdk.version
& .\.tools\dotnet-install.ps1 -Version $sdkVersion -Architecture x64 -InstallDir "$PWD\.tools\dotnet" -NoPath
```

Then build and launch:

```powershell
.\Build.ps1
.\Run.ps1
```

The initial SDK download requires Internet access. The SDK, Desktop Runtime, caches, and published binaries are generated locally and are not committed to this repository. The scripts resolve paths relative to the project and use `.tools\dotnet`; they do not require a machine-wide SDK installation or a separate development workspace. Keep `.tools` with the project when moving an existing checkout.

## Run

After setup, run `.\Run.ps1` from PowerShell, or open the project folder in **File Explorer** and double-click `Run.cmd`. Double-clicking `Run.ps1` may open it in an editor instead.

The launchers run `artifacts\app\KeyTile.exe` and build a missing published app automatically. Use `.\Run.ps1 -Tray` to start with the panel hidden. A machine-local **Launch KeyTile** shortcut can also point directly to the published executable; shortcuts are not included in Git. Launching the executable directly requires an installed .NET 10 Desktop Runtime, while the scripts use the project's local runtime.

Focus another app, then press a shortcut. KeyTile ignores its own panel. Closing or minimizing the panel hides it to the system tray; shortcuts stay active. Double-click the tray icon to reopen the panel. Choose **Exit** in the panel or tray menu to stop KeyTile and release its shortcuts.

**Pause shortcuts** releases the keys for other apps. Resume registers them again. If another app already owns a shortcut, its row shows **Unavailable**. Close or reconfigure the competing app, then pause and resume to retry. Launching KeyTile again opens the existing instance.

This prototype does not register itself to start at sign-in or change Windows Snap settings. Published builds go to `artifacts\app`.

## Restore behavior

KeyTile keeps one original placement per window in memory. For example: original position → left → right → maximize → restore returns to the original position. A new sequence starts after a successful restore. An initially maximized window returns to its maximized state. Without a saved placement, Restore unmaximizes the window if applicable; otherwise it does nothing. Saved positions are cleared when KeyTile exits.

## Build and verify

Run these commands from the project folder. Exit KeyTile using its **Exit** button or tray menu before building or testing; closing the window only hides it.

| Command | Purpose |
| --- | --- |
| `.\Build.ps1` | Build, run all checks, and publish to `artifacts\app` |
| `.\Test.ps1` | Run the checks separately |
| `.\Run.ps1` | Run the published app |

For source development and ordinary SDK commands, use the local wrapper:

```powershell
.\Dotnet.ps1 --info
.\Dotnet.ps1 run --project .\KeyTile.csproj
.\Dotnet.ps1 build .\KeyTile.csproj -c Debug
```

`Build.ps1 -SkipTests` builds and publishes without running checks. `Build.ps1 -Configuration Debug` uses a Debug build. `Build.ps1 -OutputDirectory <path>` chooses a different publish directory; `Run.ps1` always launches the default `artifacts\app` copy. Rebuild after changing source code to update that copy.

The verification program opens temporary WPF and WinForms test windows, checks their actual native positions, then closes only those test processes. The latest output is saved in `artifacts\test-results.txt`.

The SDK version is pinned by `global.json`. The scripts set runtime paths, CLI state, and package-cache paths for their current process and children; they do not change machine or user environment settings. `.tools`, `artifacts`, `bin`, and `obj` are ignored by Git. No third-party NuGet packages are used.

## Implementation

- `WindowManager.cs`: layout calculations, per-window snapshots, native state changes, bounded asynchronous verification.
- `HotkeyManager.cs`: global shortcut registration, conflict detection, cleanup, and a message-only Win32 window that survives hiding the panel.
- `NativeMethods.cs`: Win32 / DWM interop.
- `App.xaml.cs`: application lifetime, single-instance signaling, pause/resume, and tray controls.
- `MainWindow.xaml`: WPF shortcut panel and live status.
- `tests/`: dependency-free geometry, hotkey lifecycle, and real-window integration checks.

Monitor work areas exclude the taskbar. The manifest enables Per Monitor V2 DPI awareness, and DWM frame measurements compensate for invisible resize borders. WINDOWPLACEMENT snapshots are restored with SetWindowPlacement so workspace coordinates are preserved.

## Prototype limits

- Shortcuts are fixed for this iteration. Shortcut editing and mouse snapping are future work.
- Fixed-size, tool, desktop, and hidden/cloaked windows are skipped. Applications with large minimum sizes may not fit a half or quarter tile.
- Elevated applications may require running KeyTile at the same privilege level. The app runs as the current user by default.
- Mixed-DPI hardware, monitor removal during a command, remote desktop, and app-specific rendering behavior need broader compatibility testing. Snapshot identity uses window handle, process ID, and thread ID; rare reuse of all three within a running process is not yet tracked with window-destruction events.
- A successful native resize does not register the window as part of a Windows Snap group.

Unexpected command exceptions are written to `%LOCALAPPDATA%\KeyTile\errors.log`.

## API references

- [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)
- [GetWindowRect and invisible resize borders](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowrect)
- [WINDOWPLACEMENT coordinate rules](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-windowplacement)
- [SetWindowPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static KeyTile.NativeMethods;

namespace KeyTile;

internal enum WindowAction
{
    Left, Right, Maximize, Restore, TopLeft, TopRight, BottomLeft, BottomRight,
    LeftThird, CenterThird, RightThird, LeftTwoThirds, RightTwoThirds
}
internal readonly record struct CommandResult(bool Success, string Message);

internal sealed class WindowManager
{
    private sealed record Snapshot(uint ProcessId, uint ThreadId, WindowPlacement Placement);
    private readonly Dictionary<nint, Snapshot> _originals = new();

    // Halves share the same boundary even with odd widths or negative monitor origins.
    internal static Rect Half(Rect work, bool right)
    {
        int middle = work.Left + work.Width / 2;
        return right ? new(middle, work.Top, work.Right, work.Bottom) : new(work.Left, work.Top, middle, work.Bottom);
    }

    internal static Rect TileBounds(Rect work, WindowAction action)
    {
        Rect half = Half(work, action is WindowAction.Right or WindowAction.TopRight or WindowAction.BottomRight);
        int middle = work.Top + work.Height / 2;
        // Use shared boundaries so columns and wider tiles align at every monitor width.
        int firstThird = work.Left + work.Width / 3;
        int secondThird = work.Left + (int)((long)work.Width * 2 / 3);
        return action switch
        {
            WindowAction.Left or WindowAction.Right => half,
            WindowAction.TopLeft or WindowAction.TopRight => new(half.Left, work.Top, half.Right, middle),
            WindowAction.BottomLeft or WindowAction.BottomRight => new(half.Left, middle, half.Right, work.Bottom),
            WindowAction.LeftThird => new(work.Left, work.Top, firstThird, work.Bottom),
            WindowAction.CenterThird => new(firstThird, work.Top, secondThird, work.Bottom),
            WindowAction.RightThird => new(secondThird, work.Top, work.Right, work.Bottom),
            WindowAction.LeftTwoThirds => new(work.Left, work.Top, secondThird, work.Bottom),
            WindowAction.RightTwoThirds => new(firstThird, work.Top, work.Right, work.Bottom),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "This action has no tile bounds.")
        };
    }

    private static string TileName(WindowAction action) => action switch
    {
        WindowAction.Left => "left half", WindowAction.Right => "right half",
        WindowAction.TopLeft => "top-left quarter", WindowAction.TopRight => "top-right quarter",
        WindowAction.BottomLeft => "bottom-left quarter", WindowAction.BottomRight => "bottom-right quarter",
        WindowAction.LeftThird => "left third", WindowAction.CenterThird => "center third", WindowAction.RightThird => "right third",
        WindowAction.LeftTwoThirds => "left two-thirds", WindowAction.RightTwoThirds => "right two-thirds",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    internal static Rect GetVisibleBounds(nint window)
    {
        if (GetFrameBounds(window, DwmExtendedFrameBounds, out Rect visible, Marshal.SizeOf<Rect>()) == 0 && visible.Width > 0)
            return visible;
        if (!GetWindowRect(window, out Rect outer))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return outer;
    }

    internal async Task<CommandResult> ExecuteAsync(WindowAction action, nint window)
    {
        PruneSnapshots();
        uint threadId = GetWindowThreadProcessId(window, out uint processId);
        if (!CanManage(window, processId))
            return new(false, "Focus a resizable app window, then try a shortcut.");

        if (action == WindowAction.Restore)
            return await RestoreAsync(window, processId, threadId);

        long style = GetStyle(window, GwlStyle);
        if ((style & WsThickFrame) == 0 || (action == WindowAction.Maximize && (style & WsMaximizeBox) == 0))
            return new(false, "This window does not support resizing or maximizing.");

        if (!_originals.ContainsKey(window))
        {
            var placement = WindowPlacement.Create();
            if (!GetWindowPlacement(window, ref placement)) return NativeFailure("read the window's position");
            _originals[window] = new(processId, threadId, placement);
        }

        if (action == WindowAction.Maximize)
        {
            if (IsZoomed(window)) return new(true, "Window is already maximized.");
            if (!ShowWindowAsync(window, SwMaximize)) return NativeFailure("maximize this window");
            return await WaitAsync(() => IsWindow(window) && IsZoomed(window))
                ? new(true, "Maximized. Restore returns to the original position.")
                : new(false, "The window did not maximize. It may be busy or running as administrator.");
        }

        // Remember the selected monitor before a maximized window returns to its normal bounds.
        var monitor = MonitorInfo.Create();
        if (!GetMonitorInfo(MonitorFromWindow(window, MonitorDefaultToNearest), ref monitor))
            return NativeFailure("read the monitor's work area");

        if (IsZoomed(window) || IsIconic(window))
        {
            if (!ShowWindowAsync(window, SwRestore)) return NativeFailure("restore this window before snapping");
            if (!await WaitAsync(() => IsWindow(window) && !IsZoomed(window) && !IsIconic(window)))
                return new(false, "The window did not restore in time. Try again when it responds.");
            // Let the target finish its non-client frame transition before measuring borders.
            await Task.Delay(100);
        }

        Rect target = TileBounds(monitor.Work, action);
        if (!GetWindowRect(window, out Rect outer)) return NativeFailure("measure this window");
        Rect visible = GetVisibleBounds(window);
        // DWM's visible frame excludes the invisible resize border in GetWindowRect.
        int leftBorder = visible.Left - outer.Left, topBorder = visible.Top - outer.Top;
        int rightBorder = outer.Right - visible.Right, bottomBorder = outer.Bottom - visible.Bottom;
        if (!SetWindowPos(window, 0, target.Left - leftBorder, target.Top - topBorder,
            target.Width + leftBorder + rightBorder, target.Height + topBorder + bottomBorder,
            SwpNoZOrder | SwpNoActivate | SwpAsync))
            return NativeFailure("snap this window");

        bool fitted = await WaitAsync(() => IsWindow(window) && Near(GetVisibleBounds(window), target));
        return fitted
            ? new(true, $"Snapped to the {TileName(action)}.")
            : new(false, "The window could not fit the tile. It may have a minimum size or require administrator rights.");
    }

    private async Task<CommandResult> RestoreAsync(nint window, uint processId, uint threadId)
    {
        if (_originals.TryGetValue(window, out Snapshot? snapshot) && snapshot.ProcessId == processId && snapshot.ThreadId == threadId)
        {
            WindowPlacement placement = snapshot.Placement;
            // Keep workspace coordinates paired with SetWindowPlacement; do not feed them to SetWindowPos.
            placement.Flags = PlacementAsync;
            if (!SetWindowPlacement(window, in placement)) return NativeFailure("restore the original position");
            bool restored = await WaitAsync(() =>
            {
                var current = WindowPlacement.Create();
                return IsWindow(window) && GetWindowPlacement(window, ref current)
                    && current.ShowCmd == snapshot.Placement.ShowCmd
                    && Near(current.NormalPosition, snapshot.Placement.NormalPosition);
            });
            if (restored) _originals.Remove(window);
            return restored ? new(true, "Restored the original size, position, and state.")
                : new(false, "The window did not restore in time. Its saved position is still available.");
        }

        if (IsZoomed(window))
        {
            if (!ShowWindowAsync(window, SwRestore)) return NativeFailure("restore this window");
            return await WaitAsync(() => IsWindow(window) && !IsZoomed(window))
                ? new(true, "Restored the window from maximized.") : new(false, "The window did not restore in time.");
        }
        return new(true, "No saved position for this window yet.");
    }

    private static bool CanManage(nint window, uint processId)
    {
        if (window == 0 || !IsWindow(window) || !IsWindowVisible(window) || !IsWindowEnabled(window)
            || window == GetShellWindow() || window == GetDesktopWindow() || processId == Environment.ProcessId)
            return false;
        if ((GetStyle(window, GwlStyle) & WsChild) != 0 || (GetStyle(window, GwlExStyle) & WsExToolWindow) != 0)
            return false;
        if (GetCloaked(window, DwmCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        var className = new StringBuilder(256);
        GetClassName(window, className, className.Capacity);
        return className.ToString() is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
    }

    private void PruneSnapshots()
    {
        foreach (nint handle in _originals.Keys.ToArray())
        {
            uint thread = GetWindowThreadProcessId(handle, out uint process);
            Snapshot snapshot = _originals[handle];
            if (!IsWindow(handle) || process != snapshot.ProcessId || thread != snapshot.ThreadId) _originals.Remove(handle);
        }
        if (_originals.Count >= 128) _originals.Remove(_originals.Keys.First());
    }

    internal static bool Near(Rect a, Rect b, int tolerance = 2) => Math.Abs(a.Left - b.Left) <= tolerance
        && Math.Abs(a.Top - b.Top) <= tolerance && Math.Abs(a.Right - b.Right) <= tolerance && Math.Abs(a.Bottom - b.Bottom) <= tolerance;

    private static async Task<bool> WaitAsync(Func<bool> predicate)
    {
        var timer = Stopwatch.StartNew();
        do { if (predicate()) return true; await Task.Delay(25); } while (timer.ElapsedMilliseconds < 1500);
        return predicate();
    }

    private static CommandResult NativeFailure(string operation)
    {
        int error = Marshal.GetLastWin32Error();
        return new(false, $"Windows could not {operation} (error {error}). The app may require administrator rights.");
    }
}

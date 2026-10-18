using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using KeyTile;
using static KeyTile.NativeMethods;
using Forms = System.Windows.Forms;
using Rect = KeyTile.NativeMethods.Rect;

internal static class Program
{
    private static int _passed;
    private static readonly WindowAction[] Corners =
        [WindowAction.TopLeft, WindowAction.TopRight, WindowAction.BottomLeft, WindowAction.BottomRight];
    private static readonly WindowAction[] Thirds =
        [WindowAction.LeftThird, WindowAction.CenterThird, WindowAction.RightThird, WindowAction.LeftTwoThirds, WindowAction.RightTwoThirds];
    private static readonly WindowAction[] Placements =
        [WindowAction.Center, WindowAction.ResizeSmall, WindowAction.ResizeMedium, WindowAction.ResizeLarge];
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--probe-hotkeys")
        {
            using var manager = new HotkeyManager();
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(manager.SetEnabled(true)
                .Select(status => new { status.Binding.Label, status.Registered, status.Error })));
            return 0;
        }
        if (args.Length == 2 && args[0] == "--inspect")
        {
            using Process process = Process.GetProcessById(int.Parse(args[1]));
            process.Refresh();
            nint window = process.MainWindowHandle;
            var monitor = MonitorInfo.Create();
            GetMonitorInfo(MonitorFromWindow(window, MonitorDefaultToNearest), ref monitor);
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                ProcessId = process.Id, Handle = window.ToInt64(), Maximized = IsZoomed(window),
                Normal = Placement(window).NormalPosition.ToString(), Visible = WindowManager.GetVisibleBounds(window).ToString(),
                LeftHalf = WindowManager.Half(monitor.Work, false).ToString(), RightHalf = WindowManager.Half(monitor.Work, true).ToString()
            }));
            return 0;
        }
        if (args.Contains("--fixture"))
        {
            if (args.Contains("--winforms"))
            {
                Forms.Application.EnableVisualStyles();
                Forms.Application.Run(new Forms.Form
                {
                    Text = "KeyTile WinForms test window", Width = 620, Height = 400, Left = 220, Top = 150,
                    StartPosition = Forms.FormStartPosition.Manual,
                    Controls = { new Forms.Label { Text = "Independent WinForms test window", Dock = Forms.DockStyle.Fill } }
                });
                return 0;
            }
            var app = new Application();
            var window = new Window
            {
                Title = args.Contains("--fixed") ? "KeyTile fixed test window" : "KeyTile shortcut test window",
                Width = 620, Height = 400,
                MinWidth = args.Contains("--minimum") ? SystemParameters.WorkArea.Width * 0.75 : 200, MinHeight = 150,
                Left = 180, Top = 110, WindowStartupLocation = WindowStartupLocation.Manual,
                ResizeMode = args.Contains("--fixed") ? ResizeMode.NoResize : ResizeMode.CanResize,
                Content = new TextBlock
                {
                    Text = "KeyTile test window\n\nCtrl + Alt + Left / Right: snap\nCtrl + Alt + 1 / 2 / 3 / 4: corners\nCtrl + Alt + 5 / 6 / 7 / 8 / 9: thirds\nCtrl + Alt + Shift + C / S / M / L: placement\nCtrl + Alt + Up: maximize\nCtrl + Alt + Down: restore",
                    Margin = new Thickness(30), FontSize = 20, TextWrapping = TextWrapping.Wrap
                }
            };
            app.Run(window);
            return 0;
        }
        try
        {
            TestGeometry();
            TestThirdGeometry();
            TestPlacementGeometry();
            TestHotkeyLifecycle();
            RunNativeTestsAsync().GetAwaiter().GetResult();
            Console.WriteLine($"PASS: {_passed} checks completed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine($"FAIL: {error}"); return 1; }
    }

    private static void TestGeometry()
    {
        foreach (Rect work in new[] { new Rect(0, 0, 1920, 1040), new Rect(-1920, -200, -1, 881), new Rect(40, 70, 1121, 1991) })
        {
            Rect left = WindowManager.Half(work, false), right = WindowManager.Half(work, true);
            Check(left.Left == work.Left && right.Right == work.Right && left.Right == right.Left
                && left.Width + right.Width == work.Width && left.Top == work.Top && right.Bottom == work.Bottom,
                $"Halves cover work area without gaps or overlap: {work}");
            Rect topLeft = WindowManager.TileBounds(work, WindowAction.TopLeft), topRight = WindowManager.TileBounds(work, WindowAction.TopRight);
            Rect bottomLeft = WindowManager.TileBounds(work, WindowAction.BottomLeft), bottomRight = WindowManager.TileBounds(work, WindowAction.BottomRight);
            Check(topLeft.Left == work.Left && topLeft.Top == work.Top && topRight.Right == work.Right
                && bottomLeft.Left == work.Left && bottomLeft.Bottom == work.Bottom && bottomRight.Right == work.Right
                && topRight.Top == work.Top && bottomRight.Bottom == work.Bottom
                && topLeft.Right == topRight.Left && bottomLeft.Right == bottomRight.Left
                && topLeft.Bottom == bottomLeft.Top && topRight.Bottom == bottomRight.Top
                && topLeft.Right == bottomLeft.Right && topLeft.Bottom == topRight.Bottom
                && Math.Abs(topLeft.Width - topRight.Width) <= 1 && Math.Abs(topLeft.Height - bottomLeft.Height) <= 1,
                $"Quarters partition work area evenly without gaps or overlap: {work}");
        }
    }

    private static void TestThirdGeometry()
    {
        foreach (Rect work in new[] { new Rect(0, 0, 1920, 1040), new Rect(0, 0, 1921, 1040),
            new Rect(-1922, -200, 0, 881), new Rect(40, 70, 1121, 1991) })
        {
            Rect left = WindowManager.TileBounds(work, WindowAction.LeftThird);
            Rect center = WindowManager.TileBounds(work, WindowAction.CenterThird);
            Rect right = WindowManager.TileBounds(work, WindowAction.RightThird);
            Check(left.Left == work.Left && left.Right == center.Left && center.Right == right.Left && right.Right == work.Right
                && left.Width > 0 && center.Width > 0 && right.Width > 0
                && Math.Abs(left.Width - center.Width) <= 1 && Math.Abs(center.Width - right.Width) <= 1
                && Math.Abs(left.Width - right.Width) <= 1
                && new[] { left, center, right }.All(r => r.Top == work.Top && r.Bottom == work.Bottom),
                $"Three columns cover the work area evenly without gaps or overlap: {work}");
            Rect leftWide = WindowManager.TileBounds(work, WindowAction.LeftTwoThirds);
            Rect rightWide = WindowManager.TileBounds(work, WindowAction.RightTwoThirds);
            Check(leftWide.Left == left.Left && leftWide.Right == center.Right && leftWide.Right == right.Left
                && rightWide.Left == center.Left && rightWide.Left == left.Right && rightWide.Right == right.Right
                && leftWide.Top == work.Top && rightWide.Top == work.Top
                && leftWide.Bottom == work.Bottom && rightWide.Bottom == work.Bottom,
                $"Two-thirds tiles match adjacent columns and their complementary third: {work}");
        }
    }

    private static void TestPlacementGeometry()
    {
        foreach (Rect work in new[] { new Rect(0, 0, 1920, 1032), new Rect(-1920, -200, -1, 881), new Rect(40, 70, 1121, 1991) })
        {
            foreach (Rect current in new[] { new Rect(180, 110, 786, 503), new Rect(-100, -300, 2400, 2200) })
            {
                Rect centered = WindowManager.PlacementBounds(work, current, WindowAction.Center);
                Check(centered.Width == current.Width && centered.Height == current.Height && IsCentered(centered, work),
                    $"Center preserves size and balances margins, including oversized windows: {work}, {current}");
            }
            foreach (var (action, percent) in new[] { (WindowAction.ResizeSmall, 50), (WindowAction.ResizeMedium, 70), (WindowAction.ResizeLarge, 90) })
            {
                Rect resized = WindowManager.PlacementBounds(work, new Rect(180, 110, 786, 503), action);
                Check(IsCentered(resized, work) && resized.Left >= work.Left && resized.Top >= work.Top
                    && resized.Right <= work.Right && resized.Bottom <= work.Bottom
                    && work.Width * percent - resized.Width * 100 is >= 0 and < 100
                    && work.Height * percent - resized.Height * 100 is >= 0 and < 100,
                    $"{action} uses the requested work-area proportions and equal margins: {work}");
            }
        }
        Rect tiny = WindowManager.PlacementBounds(new Rect(-1, -1, 0, 0), default, WindowAction.ResizeSmall);
        Check(tiny.Width == 1 && tiny.Height == 1, "Resize presets never produce a zero-sized target");
    }

    private static bool IsCentered(Rect bounds, Rect work) => Math.Abs((bounds.Left - work.Left) - (work.Right - bounds.Right)) <= 1
        && Math.Abs((bounds.Top - work.Top) - (work.Bottom - bounds.Bottom)) <= 1;

    private static void TestHotkeyLifecycle()
    {
        using var blocker = new HwndSource(new HwndSourceParameters("KeyTile test blocker") { ParentWindow = new nint(-3) });
        Check(RegisterHotKey(blocker.Handle, 42, ModControl | ModAlt | ModNoRepeat, 0x25), "Reserve left shortcut for conflict test");
        try
        {
            using var manager = new HotkeyManager();
            var statuses = manager.SetEnabled(true);
            Check(!statuses.Single(s => s.Binding.Action == WindowAction.Left).Registered, "Occupied shortcut is reported as unavailable");
            Check(statuses.Where(s => s.Binding.Action != WindowAction.Left).All(s => s.Registered), "Other shortcuts register despite one conflict");
            Check(manager.SetEnabled(false).All(s => !s.Registered), "Pause releases shortcut registrations");
            Check(RegisterHotKey(blocker.Handle, 43, ModControl | ModAlt | ModNoRepeat, 0x27), "Paused shortcut is available to another app");
            UnregisterHotKey(blocker.Handle, 43);
        }
        finally { UnregisterHotKey(blocker.Handle, 42); }
        Check(RegisterHotKey(blocker.Handle, 45, ModControl | ModAlt | ModNoRepeat, 0x31), "Reserve top-left shortcut for corner conflict test");
        try
        {
            using var manager = new HotkeyManager();
            var statuses = manager.SetEnabled(true);
            Check(!statuses.Single(s => s.Binding.Action == WindowAction.TopLeft).Registered
                && statuses.Where(s => s.Binding.Action != WindowAction.TopLeft).All(s => s.Registered),
                "Corner conflict is isolated from the other shortcuts");
            manager.SetEnabled(false);
            foreach (HotkeyBinding binding in HotkeyManager.Bindings.Where(b => b.Action != WindowAction.TopLeft))
            {
                bool acquired = RegisterHotKey(blocker.Handle, 46, binding.Modifiers | ModNoRepeat, binding.Key);
                if (acquired) UnregisterHotKey(blocker.Handle, 46);
                Check(acquired, $"Pause releases {binding.Label}");
            }
        }
        finally { UnregisterHotKey(blocker.Handle, 45); }
        foreach (WindowAction action in new[] { WindowAction.CenterThird, WindowAction.RightTwoThirds, WindowAction.Center, WindowAction.ResizeMedium })
        {
            HotkeyBinding binding = HotkeyManager.Bindings.Single(b => b.Action == action);
            Check(RegisterHotKey(blocker.Handle, 47, binding.Modifiers | ModNoRepeat, binding.Key), $"Reserve {action} for conflict test");
            try
            {
                using var manager = new HotkeyManager();
                var statuses = manager.SetEnabled(true);
                Check(!statuses.Single(s => s.Binding.Action == action).Registered
                    && statuses.Where(s => s.Binding.Action != action).All(s => s.Registered),
                    $"{action} conflict leaves the other shortcuts available");
            }
            finally { UnregisterHotKey(blocker.Handle, 47); }
        }
        using (var manager = new HotkeyManager()) Check(manager.SetEnabled(true).All(s => s.Registered), $"Resume acquires all {HotkeyManager.Bindings.Length} shortcuts");
        foreach (HotkeyBinding binding in HotkeyManager.Bindings)
        {
            bool acquired = RegisterHotKey(blocker.Handle, 44, binding.Modifiers | ModNoRepeat, binding.Key);
            if (acquired) UnregisterHotKey(blocker.Handle, 44);
            Check(acquired, $"Dispose releases {binding.Label}");
        }
    }

    private static async Task RunNativeTestsAsync()
    {
        var fixtures = new List<Process>();
        try
        {
            Process first = StartFixture(); fixtures.Add(first);
            Process second = StartFixture(winForms: true); fixtures.Add(second);
            Process fixedWindow = StartFixture(true); fixtures.Add(fixedWindow);
            nint a = await WaitForWindowAsync(first), b = await WaitForWindowAsync(second), fixedHandle = await WaitForWindowAsync(fixedWindow);
            var manager = new WindowManager();
            WindowPlacement originalA = Placement(a), originalB = Placement(b);
            var monitor = MonitorInfo.Create();
            Check(GetMonitorInfo(MonitorFromWindow(a, MonitorDefaultToNearest), ref monitor), "Read test monitor work area");
            Check((await manager.ExecuteAsync(WindowAction.Left, a)).Success, "Left snap succeeds");
            Check(WindowManager.Near(WindowManager.GetVisibleBounds(a), WindowManager.Half(monitor.Work, false)), "Left visible frame matches work-area half");
            Check((await manager.ExecuteAsync(WindowAction.Right, a)).Success, "Right snap succeeds");
            Check(WindowManager.Near(WindowManager.GetVisibleBounds(a), WindowManager.Half(monitor.Work, true)), "Right visible frame matches work-area half");
            Check((await manager.ExecuteAsync(WindowAction.Maximize, a)).Success && IsZoomed(a), "Maximize uses native maximized state");
            Check((await manager.ExecuteAsync(WindowAction.Restore, a)).Success, "Restore after left, right, maximize succeeds");
            Check(!IsZoomed(a) && WindowManager.Near(Placement(a).NormalPosition, originalA.NormalPosition), "Restore preserves original normal position across chained commands");
            Check((await manager.ExecuteAsync(WindowAction.Restore, a)).Success && WindowManager.Near(Placement(a).NormalPosition, originalA.NormalPosition), "Repeated restore is harmless");

            foreach (WindowAction corner in Corners)
            {
                Check((await manager.ExecuteAsync(corner, a)).Success
                    && WindowManager.Near(WindowManager.GetVisibleBounds(a), WindowManager.TileBounds(monitor.Work, corner)),
                    $"WPF visible frame matches {corner} quarter");
                var secondMonitor = MonitorInfo.Create();
                Check(GetMonitorInfo(MonitorFromWindow(b, MonitorDefaultToNearest), ref secondMonitor)
                    && (await manager.ExecuteAsync(corner, b)).Success
                    && WindowManager.Near(WindowManager.GetVisibleBounds(b), WindowManager.TileBounds(secondMonitor.Work, corner)),
                    $"WinForms visible frame matches {corner} quarter");
            }
            Check((await manager.ExecuteAsync(WindowAction.Left, a)).Success
                && (await manager.ExecuteAsync(WindowAction.Maximize, a)).Success
                && (await manager.ExecuteAsync(WindowAction.Restore, a)).Success
                && WindowManager.Near(Placement(a).NormalPosition, originalA.NormalPosition),
                "Corners, half, maximize, restore retain the original position");
            Check((await manager.ExecuteAsync(WindowAction.Restore, b)).Success
                && WindowManager.Near(Placement(b).NormalPosition, originalB.NormalPosition),
                "Corner sequence restores the second window independently");

            foreach (WindowAction third in Thirds)
            {
                Check((await manager.ExecuteAsync(third, a)).Success
                    && WindowManager.Near(WindowManager.GetVisibleBounds(a), WindowManager.TileBounds(monitor.Work, third)),
                    $"WPF visible frame matches {third} tile");
                var secondMonitor = MonitorInfo.Create();
                Check(GetMonitorInfo(MonitorFromWindow(b, MonitorDefaultToNearest), ref secondMonitor)
                    && (await manager.ExecuteAsync(third, b)).Success
                    && WindowManager.Near(WindowManager.GetVisibleBounds(b), WindowManager.TileBounds(secondMonitor.Work, third)),
                    $"WinForms visible frame matches {third} tile");
            }
            Check((await manager.ExecuteAsync(WindowAction.TopLeft, a)).Success
                && (await manager.ExecuteAsync(WindowAction.Left, a)).Success
                && (await manager.ExecuteAsync(WindowAction.Maximize, a)).Success
                && (await manager.ExecuteAsync(WindowAction.CenterThird, a)).Success
                && (await manager.ExecuteAsync(WindowAction.Restore, a)).Success
                && !IsZoomed(a) && WindowManager.Near(Placement(a).NormalPosition, originalA.NormalPosition),
                "Thirds, corners, half, maximize, third, restore retain the original position");
            Check((await manager.ExecuteAsync(WindowAction.Restore, b)).Success
                && WindowManager.Near(Placement(b).NormalPosition, originalB.NormalPosition),
                "Thirds sequence restores the second window independently");

            await TestNativePlacementAsync(manager, a, "WPF");
            await TestNativePlacementAsync(manager, b, "WinForms");

            foreach (WindowAction action in new[] { WindowAction.Center, WindowAction.ResizeMedium })
            {
                Check(ShowWindowAsync(a, SwMaximize), $"Set initial maximized state before {action}");
                await UntilAsync(() => IsZoomed(a));
                await Task.Delay(150);
                Check((await manager.ExecuteAsync(action, a)).Success && !IsZoomed(a), $"{action} positions an initially maximized window");
                Check((await manager.ExecuteAsync(WindowAction.Restore, a)).Success && IsZoomed(a)
                    && WindowManager.Near(Placement(a).NormalPosition, originalA.NormalPosition),
                    $"Restore after {action} returns to the original maximized state and normal bounds");
                Check((await manager.ExecuteAsync(WindowAction.Restore, a)).Success && !IsZoomed(a), $"Second restore after {action} unmaximizes");
            }

            WindowPlacement originalFixed = Placement(fixedHandle);
            Rect fixedBounds = WindowManager.GetVisibleBounds(fixedHandle);
            Check((await manager.ExecuteAsync(WindowAction.Center, fixedHandle)).Success
                && WindowManager.GetVisibleBounds(fixedHandle).Width == fixedBounds.Width
                && WindowManager.GetVisibleBounds(fixedHandle).Height == fixedBounds.Height,
                "Center moves a fixed-size window without resizing it");
            Check((await manager.ExecuteAsync(WindowAction.Restore, fixedHandle)).Success
                && WindowManager.Near(Placement(fixedHandle).NormalPosition, originalFixed.NormalPosition),
                "Fixed-size window restores its original placement after centering");
            Check(!(await manager.ExecuteAsync(WindowAction.ResizeSmall, fixedHandle)).Success, "Fixed-size window rejects resize presets");

            Process minimumWindow = StartFixture(minimumSize: true); fixtures.Add(minimumWindow);
            nint minimumHandle = await WaitForWindowAsync(minimumWindow);
            WindowPlacement originalMinimum = Placement(minimumHandle);
            Check(!(await manager.ExecuteAsync(WindowAction.ResizeSmall, minimumHandle)).Success,
                "Resize reports failure when an app's minimum width exceeds the preset");
            Check((await manager.ExecuteAsync(WindowAction.Restore, minimumHandle)).Success
                && WindowManager.Near(Placement(minimumHandle).NormalPosition, originalMinimum.NormalPosition),
                "Restore retains the saved placement after a preset cannot fit");

            foreach (WindowAction third in new[] { WindowAction.CenterThird, WindowAction.LeftTwoThirds })
            {
                Check(ShowWindowAsync(a, SwMaximize), $"Set initial maximized state before {third}");
                await UntilAsync(() => IsZoomed(a));
                await Task.Delay(150);
                Check((await manager.ExecuteAsync(third, a)).Success && !IsZoomed(a)
                    && WindowManager.Near(WindowManager.GetVisibleBounds(a), WindowManager.TileBounds(monitor.Work, third)),
                    $"{third} snaps an initially maximized window");
                Check((await manager.ExecuteAsync(WindowAction.Restore, a)).Success && IsZoomed(a)
                    && WindowManager.Near(Placement(a).NormalPosition, originalA.NormalPosition),
                    $"Restore after {third} preserves the initial maximized state and normal bounds");
                Check((await manager.ExecuteAsync(WindowAction.Restore, a)).Success && !IsZoomed(a),
                    $"Second restore after {third} unmaximizes without a snapshot");
            }

            Check(ShowWindowAsync(a, SwMaximize), "Set initial maximized state");
            await UntilAsync(() => IsZoomed(a));
            await Task.Delay(150);
            Check((await manager.ExecuteAsync(WindowAction.BottomRight, a)).Success && !IsZoomed(a), "Corner snap initially maximized window");
            Check((await manager.ExecuteAsync(WindowAction.Restore, a)).Success && IsZoomed(a), "Restore returns initially maximized window to maximized state");
            Check(WindowManager.Near(Placement(a).NormalPosition, originalA.NormalPosition), "Initially maximized window retains its normal restore bounds");
            Check((await manager.ExecuteAsync(WindowAction.Restore, a)).Success && !IsZoomed(a), "Restore without snapshot unmaximizes a window");

            Check((await manager.ExecuteAsync(WindowAction.Right, a)).Success && (await manager.ExecuteAsync(WindowAction.Left, b)).Success, "Manage two windows independently");
            Check((await manager.ExecuteAsync(WindowAction.Restore, b)).Success && WindowManager.Near(Placement(b).NormalPosition, originalB.NormalPosition), "Second window restores its own snapshot");
            Check(WindowManager.Near(WindowManager.GetVisibleBounds(a), WindowManager.Half(monitor.Work, true)), "Restoring second window leaves first window untouched");
            Check((await manager.ExecuteAsync(WindowAction.Restore, a)).Success, "First window still has its snapshot");
            Check(!(await manager.ExecuteAsync(WindowAction.Left, fixedHandle)).Success, "Fixed-size window is rejected");
            Check(!(await manager.ExecuteAsync(WindowAction.TopLeft, fixedHandle)).Success, "Fixed-size window rejects corner snapping");
            Check(!(await manager.ExecuteAsync(WindowAction.CenterThird, fixedHandle)).Success, "Fixed-size window rejects column snapping");
            Check(!(await manager.ExecuteAsync(WindowAction.LeftTwoThirds, fixedHandle)).Success, "Fixed-size window rejects two-thirds snapping");
            Check(!(await manager.ExecuteAsync(WindowAction.Left, 0)).Success, "Invalid target is rejected");
            first.Kill(); await first.WaitForExitAsync();
            Check(!(await manager.ExecuteAsync(WindowAction.Restore, a)).Success, "Closed window is rejected without crashing");
        }
        finally
        {
            foreach (Process process in fixtures)
            {
                if (!process.HasExited) { process.Kill(); process.WaitForExit(); }
                process.Dispose();
            }
        }
    }

    private static async Task TestNativePlacementAsync(WindowManager manager, nint window, string label)
    {
        WindowPlacement original = Placement(window);
        var monitor = MonitorInfo.Create();
        Check(GetMonitorInfo(MonitorFromWindow(window, MonitorDefaultToNearest), ref monitor), $"Read {label} monitor for placement");
        foreach (WindowAction action in Placements)
        {
            Rect before = WindowManager.GetVisibleBounds(window);
            Rect target = WindowManager.PlacementBounds(monitor.Work, before, action);
            Check((await manager.ExecuteAsync(action, window)).Success
                && WindowManager.Near(WindowManager.GetVisibleBounds(window), target), $"{label} visible frame matches {action} placement");
            Rect positioned = WindowManager.GetVisibleBounds(window);
            Check((await manager.ExecuteAsync(WindowAction.Center, window)).Success
                && WindowManager.Near(WindowManager.GetVisibleBounds(window), positioned), $"{label} centering is idempotent after {action}");
        }
        Check((await manager.ExecuteAsync(WindowAction.TopRight, window)).Success
            && (await manager.ExecuteAsync(WindowAction.CenterThird, window)).Success
            && (await manager.ExecuteAsync(WindowAction.Left, window)).Success
            && (await manager.ExecuteAsync(WindowAction.Maximize, window)).Success
            && (await manager.ExecuteAsync(WindowAction.ResizeSmall, window)).Success
            && (await manager.ExecuteAsync(WindowAction.Restore, window)).Success
            && !IsZoomed(window) && WindowManager.Near(Placement(window).NormalPosition, original.NormalPosition),
            $"{label} restores the original placement after centering, presets, tiles, and maximize");
    }

    private static Process StartFixture(bool fixedSize = false, bool winForms = false, bool minimumSize = false) => Process.Start(new ProcessStartInfo(Environment.ProcessPath!,
        "--fixture" + (fixedSize ? " --fixed" : "") + (winForms ? " --winforms" : "") + (minimumSize ? " --minimum" : "")) { UseShellExecute = false, CreateNoWindow = true })!;
    private static async Task<nint> WaitForWindowAsync(Process process)
    {
        await UntilAsync(() => { process.Refresh(); return process.MainWindowHandle != 0; });
        await Task.Delay(300);
        return process.MainWindowHandle;
    }
    private static WindowPlacement Placement(nint window)
    {
        var placement = WindowPlacement.Create();
        if (!GetWindowPlacement(window, ref placement)) throw new Exception("Could not read fixture window placement.");
        return placement;
    }
    private static async Task UntilAsync(Func<bool> test)
    {
        var timer = Stopwatch.StartNew();
        while (!test()) { if (timer.ElapsedMilliseconds > 10000) throw new TimeoutException("Test window did not reach expected state."); await Task.Delay(30); }
    }
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        _passed++; Console.WriteLine($"PASS {description}");
    }
}

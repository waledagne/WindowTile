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
                Width = 620, Height = 400, MinWidth = 200, MinHeight = 150,
                Left = 180, Top = 110, WindowStartupLocation = WindowStartupLocation.Manual,
                ResizeMode = args.Contains("--fixed") ? ResizeMode.NoResize : ResizeMode.CanResize,
                Content = new TextBlock
                {
                    Text = "KeyTile test window\n\nCtrl + Alt + Left / Right: snap\nCtrl + Alt + Up: maximize\nCtrl + Alt + Down: restore",
                    Margin = new Thickness(30), FontSize = 20, TextWrapping = TextWrapping.Wrap
                }
            };
            app.Run(window);
            return 0;
        }
        try
        {
            TestGeometry();
            TestHotkeyLifecycle();
            RunNativeTestsAsync().GetAwaiter().GetResult();
            Console.WriteLine($"PASS: {_passed} checks completed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine($"FAIL: {error}"); return 1; }
    }

    private static void TestGeometry()
    {
        foreach (Rect work in new[] { new Rect(0, 0, 1920, 1040), new Rect(-1920, -200, -1, 880), new Rect(40, 70, 1121, 1991) })
        {
            Rect left = WindowManager.Half(work, false), right = WindowManager.Half(work, true);
            Check(left.Left == work.Left && right.Right == work.Right && left.Right == right.Left
                && left.Width + right.Width == work.Width && left.Top == work.Top && right.Bottom == work.Bottom,
                $"Halves cover work area without gaps or overlap: {work}");
        }
    }

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
        using (var manager = new HotkeyManager()) Check(manager.SetEnabled(true).All(s => s.Registered), "Resume acquires all four shortcuts");
        Check(RegisterHotKey(blocker.Handle, 44, ModControl | ModAlt | ModNoRepeat, 0x28), "Dispose releases global shortcuts");
        UnregisterHotKey(blocker.Handle, 44);
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

            Check(ShowWindowAsync(a, SwMaximize), "Set initial maximized state");
            await UntilAsync(() => IsZoomed(a));
            await Task.Delay(150);
            Check((await manager.ExecuteAsync(WindowAction.Left, a)).Success && !IsZoomed(a), "Snap initially maximized window");
            Check((await manager.ExecuteAsync(WindowAction.Restore, a)).Success && IsZoomed(a), "Restore returns initially maximized window to maximized state");
            Check(WindowManager.Near(Placement(a).NormalPosition, originalA.NormalPosition), "Initially maximized window retains its normal restore bounds");
            Check((await manager.ExecuteAsync(WindowAction.Restore, a)).Success && !IsZoomed(a), "Restore without snapshot unmaximizes a window");

            Check((await manager.ExecuteAsync(WindowAction.Right, a)).Success && (await manager.ExecuteAsync(WindowAction.Left, b)).Success, "Manage two windows independently");
            Check((await manager.ExecuteAsync(WindowAction.Restore, b)).Success && WindowManager.Near(Placement(b).NormalPosition, originalB.NormalPosition), "Second window restores its own snapshot");
            Check(WindowManager.Near(WindowManager.GetVisibleBounds(a), WindowManager.Half(monitor.Work, true)), "Restoring second window leaves first window untouched");
            Check((await manager.ExecuteAsync(WindowAction.Restore, a)).Success, "First window still has its snapshot");
            Check(!(await manager.ExecuteAsync(WindowAction.Left, fixedHandle)).Success, "Fixed-size window is rejected");
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

    private static Process StartFixture(bool fixedSize = false, bool winForms = false) => Process.Start(new ProcessStartInfo(Environment.ProcessPath!,
        "--fixture" + (fixedSize ? " --fixed" : "") + (winForms ? " --winforms" : "")) { UseShellExecute = false, CreateNoWindow = true })!;
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

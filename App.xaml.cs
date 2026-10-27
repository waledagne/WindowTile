using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace KeyTile;

public partial class App : Application
{
    private Mutex? _instance;
    private EventWaitHandle? _showSignal;
    private DispatcherTimer? _signalTimer;
    private HotkeyManager? _hotkeys;
    private Forms.NotifyIcon? _tray;
    private Forms.ToolStripMenuItem? _pauseMenu;
    private MainWindow? _panel;
    private readonly WindowManager _windows = new();
    private bool _enabled = true, _busy;
    internal bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance = new Mutex(true, @"Local\KeyTile.Prototype.Instance", out bool first);
        if (!first)
        {
            if (!e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase))
            {
                try { using var signal = EventWaitHandle.OpenExisting(@"Local\KeyTile.Prototype.Show"); signal.Set(); }
                catch (WaitHandleCannotBeOpenedException) { }
            }
            Shutdown();
            return;
        }
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\KeyTile.Prototype.Show");
        _panel = new MainWindow(this);
        MainWindow = _panel;
        _hotkeys = new HotkeyManager();
        _hotkeys.Pressed += HandleShortcut;
        CreateTray();
        RefreshShortcuts();
        _signalTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _signalTimer.Tick += (_, _) => { if (_showSignal.WaitOne(0)) _panel.ShowPanel(); };
        _signalTimer.Start();
        if (!e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase)) _panel.Show();
    }

    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open KeyTile", null, (_, _) => Dispatcher.Invoke(() => _panel?.ShowPanel()));
        _pauseMenu = new Forms.ToolStripMenuItem("Pause shortcuts", null, (_, _) => Dispatcher.Invoke(ToggleShortcuts));
        menu.Items.Add(_pauseMenu);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(ExitApp));
        using Stream iconStream = GetResourceStream(new Uri("pack://application:,,,/Assets/KeyTile.ico")).Stream;
        using var original = new System.Drawing.Icon(iconStream);
        _tray = new Forms.NotifyIcon
        {
            Text = "KeyTile · shortcuts active", Icon = (System.Drawing.Icon)original.Clone(),
            ContextMenuStrip = menu, Visible = true
        };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => _panel?.ShowPanel());
    }

    internal void ToggleShortcuts()
    {
        _enabled = !_enabled;
        _panel?.SetActivity(_enabled ? "Shortcuts resumed. Focus another app to use them." : "Shortcuts paused and released for other apps.");
        RefreshShortcuts();
    }

    private void RefreshShortcuts()
    {
        if (_hotkeys is null) return;
        var statuses = _hotkeys.SetEnabled(_enabled);
        _panel?.UpdateShortcuts(_enabled, statuses);
        if (_pauseMenu is not null) _pauseMenu.Text = _enabled ? "Pause shortcuts" : "Resume shortcuts";
        if (_tray is not null) _tray.Text = !_enabled ? "KeyTile · paused"
            : statuses.All(x => x.Registered) ? "KeyTile · shortcuts active" : "KeyTile · shortcut conflict";
    }

    private async void HandleShortcut(WindowAction action, nint window)
    {
        if (!_enabled || _busy || IsExiting) return;
        _busy = true;
        try
        {
            CommandResult result = await _windows.ExecuteAsync(action, window);
            _panel?.SetActivity(result.Message);
            if (!result.Success && _panel is { IsVisible: false } && _tray is not null)
                _tray.ShowBalloonTip(3500, "KeyTile", result.Message, Forms.ToolTipIcon.Info);
        }
        catch (Exception exception)
        {
            _panel?.SetActivity("Could not change this window. Try again when the app responds.");
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyTile");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "errors.log"), $"{DateTimeOffset.Now:O} {exception}\n");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        finally { _busy = false; }
    }

    internal void ExitApp() { IsExiting = true; Shutdown(); }

    protected override void OnExit(ExitEventArgs e)
    {
        IsExiting = true;
        _signalTimer?.Stop();
        _hotkeys?.Dispose();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.ContextMenuStrip?.Dispose();
            _tray.Icon?.Dispose();
            _tray.Dispose();
        }
        _showSignal?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}

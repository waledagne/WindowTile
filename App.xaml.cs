using System;
using System.IO;
using System.Collections.Generic;
using System.Security;
using System.Text.Json;
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
    private readonly ShortcutSettings _shortcutSettings = new();
    private bool _enabled = true, _busy, _editingShortcuts;
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
        string? settingsNotice = null;
        HotkeyBinding[] bindings;
        try { bindings = _shortcutSettings.Load(); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or SecurityException or JsonException or ArgumentException)
        {
            bindings = ShortcutSettings.Validate(HotkeyManager.Bindings);
            settingsNotice = $"Could not load saved shortcuts; using defaults. {error.Message}";
        }
        _hotkeys = new HotkeyManager(bindings);
        _hotkeys.Pressed += HandleShortcut;
        CreateTray();
        RefreshShortcuts();
        if (settingsNotice is not null) _panel.SetActivity(settingsNotice);
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
        if (_hotkeys is null || IsExiting) return;
        bool active = _enabled && !_editingShortcuts;
        var statuses = _hotkeys.SetEnabled(active);
        _panel?.UpdateShortcuts(active, statuses);
        if (_pauseMenu is not null) _pauseMenu.Text = _enabled ? "Pause shortcuts" : "Resume shortcuts";
        if (_tray is not null) _tray.Text = !active ? "KeyTile · paused"
            : statuses.All(x => !x.Binding.Enabled) ? "KeyTile · all shortcuts disabled"
            : statuses.All(x => !x.Binding.Enabled || x.Registered) ? "KeyTile · shortcuts active" : "KeyTile · shortcut conflict";
    }

    internal void CustomizeShortcuts()
    {
        if (_hotkeys is null || _panel is null || _editingShortcuts) return;
        _editingShortcuts = true;
        RefreshShortcuts();
        try
        {
            var editor = new ShortcutEditor(this, _hotkeys.CurrentBindings);
            editor.Completed += FinishShortcutEditing;
            _panel.ShowShortcutEditor(editor);
        }
        catch { FinishShortcutEditing(false); throw; }
    }

    internal void FinishShortcutEditing(bool saved = false)
    {
        if (!_editingShortcuts) return;
        _editingShortcuts = false;
        _panel?.CloseShortcutEditor();
        if (IsExiting) return;
        RefreshShortcuts();
        _panel?.SetActivity(saved ? "Shortcuts saved. Focus another app to use them." : "Shortcut edits cancelled. Your shortcuts are unchanged.");
    }

    internal bool TrySaveShortcuts(IReadOnlyList<HotkeyBinding> bindings, out string message)
    {
        if (_hotkeys is null || !_editingShortcuts) throw new InvalidOperationException("Open the shortcut editor first.");
        try
        {
            if (!_hotkeys.TryUpdateBindings(bindings, () => _shortcutSettings.Save(bindings), out var statuses))
            {
                message = "Unavailable: " + string.Join(", ", statuses.Where(status => status.Binding.Enabled && !status.Registered)
                    .Select(status => $"{ShortcutSettings.ActionName(status.Binding.Action)} ({status.Binding.Label})"))
                    + ". Close the app using those keys or choose another combination.";
                return false;
            }
            message = "Shortcuts saved.";
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            message = $"Could not save shortcuts: {error.Message}";
            return false;
        }
    }

    private async void HandleShortcut(WindowAction action, nint window)
    {
        if (!_enabled || _editingShortcuts || _busy || IsExiting) return;
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

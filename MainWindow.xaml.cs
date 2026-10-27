using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Security;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KeyTile;

public partial class MainWindow : Window
{
    private readonly App _app;
    private bool _updatingStartup;
    internal MainWindow(App app)
    {
        InitializeComponent();
        _app = app;
        Activated += (_, _) => RefreshStartup();
        RefreshStartup();
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) { _app.FinishShortcutEditing(); Hide(); } };
    }

    private static StartupManager Startup() => new(Environment.ProcessPath
        ?? throw new InvalidOperationException("Could not find this app's executable path."));

    private void RefreshStartup()
    {
        _updatingStartup = true;
        try { StartupCheckBox.IsChecked = Startup().IsEnabled; StartupCheckBox.IsEnabled = true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or InvalidOperationException or COMException)
        {
            StartupCheckBox.IsEnabled = false;
            SetActivity($"Could not read startup settings: {error.Message}");
        }
        finally { _updatingStartup = false; }
    }

    private void Startup_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingStartup) return;
        bool enabled = StartupCheckBox.IsChecked == true;
        try
        {
            Startup().SetEnabled(enabled);
            SetActivity(enabled ? "KeyTile will start quietly in the tray when you sign in." : "KeyTile will no longer start at sign-in.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or InvalidOperationException or COMException)
        {
            SetActivity($"Could not change startup settings: {error.Message}");
        }
        RefreshStartup();
    }

    internal void UpdateShortcuts(bool enabled, IReadOnlyList<HotkeyStatus> statuses)
    {
        int available = 0, requested = 0;
        foreach (HotkeyStatus status in statuses)
        {
            if (FindName($"{status.Binding.Action}Shortcut") is TextBlock shortcut) shortcut.Text = status.Binding.Label;
            TextBlock label = status.Binding.Action switch
            {
                WindowAction.Left => LeftStatus, WindowAction.Right => RightStatus,
                WindowAction.Maximize => MaximizeStatus, WindowAction.Restore => RestoreStatus,
                WindowAction.TopLeft => TopLeftStatus, WindowAction.TopRight => TopRightStatus,
                WindowAction.BottomLeft => BottomLeftStatus, WindowAction.BottomRight => BottomRightStatus,
                WindowAction.LeftThird => LeftThirdStatus, WindowAction.CenterThird => CenterThirdStatus,
                WindowAction.RightThird => RightThirdStatus, WindowAction.LeftTwoThirds => LeftTwoThirdsStatus,
                WindowAction.RightTwoThirds => RightTwoThirdsStatus,
                WindowAction.Center => CenterStatus, WindowAction.ResizeSmall => ResizeSmallStatus,
                WindowAction.ResizeMedium => ResizeMediumStatus, WindowAction.ResizeLarge => ResizeLargeStatus,
                _ => throw new ArgumentOutOfRangeException(nameof(statuses))
            };
            label.Text = !status.Binding.Enabled ? "Disabled" : !enabled ? "Paused" : status.Registered ? "Ready" : $"Unavailable · error {status.Error}";
            label.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                enabled && status.Binding.Enabled && !status.Registered ? "#FFBD89" : "#90A7B7"));
            if (status.Binding.Enabled) requested++;
            if (status.Registered) available++;
        }
        bool allAvailable = available == requested;
        StatusBadge.Text = !enabled ? "PAUSED" : requested == 0 ? "ALL OFF" : allAvailable ? "ACTIVE" : "CONFLICT";
        StatusBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
            enabled && allAvailable ? "#71E6C0" : "#FFBD89"));
        PauseButton.Content = enabled ? "Pause shortcuts" : "Resume shortcuts";
        if (enabled && !allAvailable)
            SetActivity("Some shortcuts are unavailable. Close the app using those keys, then pause and resume to retry.");
    }

    internal void SetActivity(string message) => ActivityText.Text = message;
    internal void ShowShortcutEditor(ShortcutEditor editor)
    {
        MainPanel.Visibility = Visibility.Collapsed;
        ShortcutEditorHost.Content = editor;
        ShortcutEditorHost.Visibility = Visibility.Visible;
    }
    internal void CloseShortcutEditor()
    {
        ShortcutEditorHost.Visibility = Visibility.Collapsed;
        ShortcutEditorHost.Content = null;
        MainPanel.Visibility = Visibility.Visible;
    }
    internal void ShowPanel()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_app.IsExiting) { e.Cancel = true; _app.FinishShortcutEditing(); Hide(); }
        base.OnClosing(e);
    }
    private void Pause_Click(object sender, RoutedEventArgs e) => _app.ToggleShortcuts();
    private void Customize_Click(object sender, RoutedEventArgs e) => _app.CustomizeShortcuts();
    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();
    private void Exit_Click(object sender, RoutedEventArgs e) => _app.ExitApp();
}

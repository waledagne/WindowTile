using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KeyTile;

public partial class MainWindow : Window
{
    private readonly App _app;
    internal MainWindow(App app)
    {
        InitializeComponent();
        _app = app;
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Hide(); };
    }

    internal void UpdateShortcuts(bool enabled, IReadOnlyList<HotkeyStatus> statuses)
    {
        int available = 0;
        foreach (HotkeyStatus status in statuses)
        {
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
            label.Text = !enabled ? "Paused" : status.Registered ? "Ready" : $"Unavailable · error {status.Error}";
            label.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                enabled && !status.Registered ? "#FFBD89" : "#90A7B7"));
            if (status.Registered) available++;
        }
        bool allAvailable = available == HotkeyManager.Bindings.Length;
        StatusBadge.Text = !enabled ? "PAUSED" : allAvailable ? "ACTIVE" : "CONFLICT";
        StatusBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
            enabled && allAvailable ? "#71E6C0" : "#FFBD89"));
        PauseButton.Content = enabled ? "Pause shortcuts" : "Resume shortcuts";
        if (enabled && !allAvailable)
            SetActivity("Some shortcuts are unavailable. Close the app using those keys, then pause and resume to retry.");
    }

    internal void SetActivity(string message) => ActivityText.Text = message;
    internal void ShowPanel()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_app.IsExiting) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
    private void Pause_Click(object sender, RoutedEventArgs e) => _app.ToggleShortcuts();
    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();
    private void Exit_Click(object sender, RoutedEventArgs e) => _app.ExitApp();
}

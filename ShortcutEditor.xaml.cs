using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KeyTile;

internal sealed class ShortcutRow : INotifyPropertyChanged
{
    internal HotkeyBinding Binding { get; private set; }
    public string Name => ShortcutSettings.ActionName(Binding.Action);
    public string AccessibleName => $"Shortcut for {Name}";
    public string Label => Binding.Label;
    public string CategoryHeader { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    internal ShortcutRow(HotkeyBinding binding, string categoryHeader) => (Binding, CategoryHeader) = (binding, categoryHeader);
    internal void SetBinding(HotkeyBinding binding)
    {
        Binding = binding with { Label = ShortcutSettings.Format(binding.Key, binding.Modifiers) };
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
    }
}

public partial class ShortcutEditor : UserControl
{
    private readonly App _app;
    private readonly ShortcutRow[] _rows;
    internal event Action<bool>? Completed;
    internal ShortcutEditor(App app, IReadOnlyList<HotkeyBinding> bindings)
    {
        InitializeComponent();
        _app = app;
        _rows = bindings.Select(binding => new ShortcutRow(binding, binding.Id switch
        {
            1 => "Basics", 5 => "Corners", 9 => "Thirds", 14 => "Window Placement", _ => ""
        })).ToArray();
        ShortcutRows.ItemsSource = _rows;
    }

    private void Capture_KeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab or Key.Escape) return;
        e.Handled = true;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift) return;
        try
        {
            ModifierKeys modifiers = Keyboard.Modifiers;
            if ((modifiers & ModifierKeys.Windows) != 0) throw new ArgumentException("Choose a combination without the Windows key.");
            uint nativeModifiers = ((modifiers & ModifierKeys.Control) != 0 ? NativeMethods.ModControl : 0)
                | ((modifiers & ModifierKeys.Alt) != 0 ? NativeMethods.ModAlt : 0)
                | ((modifiers & ModifierKeys.Shift) != 0 ? NativeMethods.ModShift : 0);
            uint nativeKey = (uint)KeyInterop.VirtualKeyFromKey(key);
            ShortcutSettings.ValidateCombination(nativeKey, nativeModifiers);
            var row = (ShortcutRow)((TextBox)sender).Tag;
            row.SetBinding(row.Binding with { Key = nativeKey, Modifiers = nativeModifiers });
            ShortcutSettings.Validate(_rows.Select(item => item.Binding).ToArray());
            ErrorText.Text = "";
            SaveButton.IsEnabled = true;
        }
        catch (ArgumentException error)
        {
            ErrorText.Text = error.Message;
            SaveButton.IsEnabled = false;
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        foreach (ShortcutRow row in _rows) row.SetBinding(HotkeyManager.Bindings.Single(binding => binding.Action == row.Binding.Action));
        ErrorText.Text = "Defaults restored in this editor. Save to apply them, or Cancel to keep your shortcuts.";
        SaveButton.IsEnabled = true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_app.TrySaveShortcuts(_rows.Select(row => row.Binding).ToArray(), out string message)) Completed?.Invoke(true);
        else ErrorText.Text = message;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Completed?.Invoke(false);
    private void Editor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Completed?.Invoke(false);
    }
}

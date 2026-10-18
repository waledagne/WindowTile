using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace KeyTile;

internal sealed record HotkeyBinding(int Id, WindowAction Action, uint Key, string Label);
internal sealed record HotkeyStatus(HotkeyBinding Binding, bool Registered, int Error);

internal sealed class HotkeyManager : IDisposable
{
    internal static readonly HotkeyBinding[] Bindings =
    [
        new(1, WindowAction.Left, 0x25, "Ctrl + Alt + Left"),
        new(2, WindowAction.Right, 0x27, "Ctrl + Alt + Right"),
        new(3, WindowAction.Maximize, 0x26, "Ctrl + Alt + Up"),
        new(4, WindowAction.Restore, 0x28, "Ctrl + Alt + Down"),
        new(5, WindowAction.TopLeft, 0x31, "Ctrl + Alt + 1"),
        new(6, WindowAction.TopRight, 0x32, "Ctrl + Alt + 2"),
        new(7, WindowAction.BottomLeft, 0x33, "Ctrl + Alt + 3"),
        new(8, WindowAction.BottomRight, 0x34, "Ctrl + Alt + 4")
    ];
    private readonly HwndSource _source;
    private readonly List<int> _registered = new();
    internal event Action<WindowAction, nint>? Pressed;
    internal HotkeyManager()
    {
        _source = new HwndSource(new HwndSourceParameters("KeyTile shortcuts")
        { ParentWindow = new nint(-3), Width = 0, Height = 0, WindowStyle = 0 });
        _source.AddHook(WindowProcedure);
    }

    internal IReadOnlyList<HotkeyStatus> SetEnabled(bool enabled)
    {
        foreach (int id in _registered) NativeMethods.UnregisterHotKey(_source.Handle, id);
        _registered.Clear();
        var statuses = new List<HotkeyStatus>();
        foreach (HotkeyBinding binding in Bindings)
        {
            bool registered = enabled && NativeMethods.RegisterHotKey(_source.Handle, binding.Id,
                NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModNoRepeat, binding.Key);
            int error = enabled && !registered ? Marshal.GetLastWin32Error() : 0;
            if (registered) _registered.Add(binding.Id);
            statuses.Add(new(binding, registered, error));
        }
        return statuses;
    }

    private nint WindowProcedure(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == NativeMethods.WmHotkey && _registered.Contains(wParam.ToInt32()))
        {
            foreach (HotkeyBinding binding in Bindings)
                if (binding.Id == wParam.ToInt32())
                {
                    handled = true;
                    Pressed?.Invoke(binding.Action, NativeMethods.GetForegroundWindow());
                    break;
                }
        }
        return 0;
    }

    public void Dispose()
    {
        foreach (int id in _registered) NativeMethods.UnregisterHotKey(_source.Handle, id);
        _registered.Clear();
        _source.RemoveHook(WindowProcedure);
        _source.Dispose();
    }
}

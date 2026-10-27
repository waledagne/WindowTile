using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace KeyTile;

internal sealed record HotkeyBinding(int Id, WindowAction Action, uint Key, string Label,
    uint Modifiers = NativeMethods.ModControl | NativeMethods.ModAlt, bool Enabled = true);
internal sealed record HotkeyStatus(HotkeyBinding Binding, bool Registered, int Error);

internal sealed class HotkeyManager : IDisposable
{
    private const uint PlacementModifiers = NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModShift;
    internal static readonly HotkeyBinding[] Bindings =
    [
        new(1, WindowAction.Left, 0x25, "Ctrl + Alt + Left"),
        new(2, WindowAction.Right, 0x27, "Ctrl + Alt + Right"),
        new(3, WindowAction.Maximize, 0x26, "Ctrl + Alt + Up"),
        new(4, WindowAction.Restore, 0x28, "Ctrl + Alt + Down"),
        new(5, WindowAction.TopLeft, 0x31, "Ctrl + Alt + 1"),
        new(6, WindowAction.TopRight, 0x32, "Ctrl + Alt + 2"),
        new(7, WindowAction.BottomLeft, 0x33, "Ctrl + Alt + 3"),
        new(8, WindowAction.BottomRight, 0x34, "Ctrl + Alt + 4"),
        new(9, WindowAction.LeftThird, 0x35, "Ctrl + Alt + 5"),
        new(10, WindowAction.CenterThird, 0x36, "Ctrl + Alt + 6"),
        new(11, WindowAction.RightThird, 0x37, "Ctrl + Alt + 7"),
        new(12, WindowAction.LeftTwoThirds, 0x38, "Ctrl + Alt + 8"),
        new(13, WindowAction.RightTwoThirds, 0x39, "Ctrl + Alt + 9"),
        new(14, WindowAction.Center, 0x43, "Ctrl + Alt + Shift + C", PlacementModifiers),
        new(15, WindowAction.ResizeSmall, 0x53, "Ctrl + Alt + Shift + S", PlacementModifiers),
        new(16, WindowAction.ResizeMedium, 0x4D, "Ctrl + Alt + Shift + M", PlacementModifiers),
        new(17, WindowAction.ResizeLarge, 0x4C, "Ctrl + Alt + Shift + L", PlacementModifiers)
    ];
    private readonly HwndSource _source;
    private readonly List<int> _registered = new();
    private HotkeyBinding[] _bindings;
    private bool _enabled;
    internal IReadOnlyList<HotkeyBinding> CurrentBindings => Array.AsReadOnly(_bindings);
    internal event Action<WindowAction, nint>? Pressed;
    internal HotkeyManager(IReadOnlyList<HotkeyBinding>? bindings = null)
    {
        _bindings = ShortcutSettings.Validate(bindings ?? Bindings);
        _source = new HwndSource(new HwndSourceParameters("KeyTile shortcuts")
        { ParentWindow = new nint(-3), Width = 0, Height = 0, WindowStyle = 0 });
        _source.AddHook(WindowProcedure);
    }

    internal IReadOnlyList<HotkeyStatus> SetEnabled(bool enabled)
    {
        _enabled = enabled;
        foreach (int id in _registered) NativeMethods.UnregisterHotKey(_source.Handle, id);
        _registered.Clear();
        var statuses = new List<HotkeyStatus>();
        foreach (HotkeyBinding binding in _bindings)
        {
            bool requested = enabled && binding.Enabled;
            bool registered = requested && NativeMethods.RegisterHotKey(_source.Handle, binding.Id,
                binding.Modifiers | NativeMethods.ModNoRepeat, binding.Key);
            int error = requested && !registered ? Marshal.GetLastWin32Error() : 0;
            if (registered) _registered.Add(binding.Id);
            statuses.Add(new(binding, registered, error));
        }
        return statuses;
    }

    // Probe enabled combinations before persistence; failures restore previous bindings and pause state.
    internal bool TryUpdateBindings(IReadOnlyList<HotkeyBinding> bindings, Action persist, out IReadOnlyList<HotkeyStatus> statuses)
    {
        HotkeyBinding[] candidate = ShortcutSettings.Validate(bindings);
        ArgumentNullException.ThrowIfNull(persist);
        HotkeyBinding[] previous = _bindings;
        bool wasEnabled = _enabled, accepted = false;
        SetEnabled(false);
        _bindings = candidate;
        try
        {
            statuses = SetEnabled(true);
            if (statuses.Any(status => status.Binding.Enabled && !status.Registered)) return false;
            persist();
            accepted = true;
            return true;
        }
        finally
        {
            if (!accepted)
            {
                SetEnabled(false);
                _bindings = previous;
                SetEnabled(wasEnabled);
            }
            else if (!wasEnabled) SetEnabled(false);
        }
    }

    private nint WindowProcedure(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == NativeMethods.WmHotkey && _registered.Contains(wParam.ToInt32()))
        {
            foreach (HotkeyBinding binding in _bindings)
                if (binding.Id == wParam.ToInt32()
                    && (uint)((lParam.ToInt64() >> 16) & 0xFFFF) == binding.Key
                    && ((uint)lParam.ToInt64() & 0x0007) == binding.Modifiers)
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

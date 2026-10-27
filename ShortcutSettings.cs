using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace KeyTile;

internal sealed class ShortcutSettings
{
    private sealed record Entry(string Action, uint Key, uint Modifiers);
    private sealed record Profile(int Version, Entry[]? Shortcuts);
    private readonly string _path;
    internal ShortcutSettings(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyTile", "shortcuts.json");

    internal HotkeyBinding[] Load()
    {
        if (!File.Exists(_path)) return Validate(HotkeyManager.Bindings);
        if (new FileInfo(_path).Length > 65536) throw new InvalidDataException("Shortcut settings are too large.");
        Profile? profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(_path));
        if (profile is not { Version: 1, Shortcuts: not null })
            throw new InvalidDataException("Unsupported shortcut settings format.");
        var bindings = new List<HotkeyBinding>();
        foreach (Entry entry in profile.Shortcuts)
        {
            if (entry is null) throw new InvalidDataException("Missing shortcut action.");
            HotkeyBinding? original = HotkeyManager.Bindings.FirstOrDefault(b => b.Action.ToString() == entry.Action);
            if (original is null) throw new InvalidDataException("Unknown shortcut action.");
            bindings.Add(original with { Key = entry.Key, Modifiers = entry.Modifiers });
        }
        return Validate(bindings);
    }

    internal void Save(IReadOnlyList<HotkeyBinding> bindings)
    {
        HotkeyBinding[] validated = Validate(bindings);
        string folder = Path.GetDirectoryName(Path.GetFullPath(_path))!;
        Directory.CreateDirectory(folder);
        string temporary = Path.Combine(folder, $"shortcuts-{Guid.NewGuid():N}.tmp");
        try
        {
            string json = JsonSerializer.Serialize(new Profile(1, validated.Select(b => new Entry(b.Action.ToString(), b.Key, b.Modifiers)).ToArray()),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(temporary, json);
            File.Move(temporary, _path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    internal static HotkeyBinding[] Validate(IReadOnlyList<HotkeyBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        if (bindings.Count != HotkeyManager.Bindings.Length) throw new ArgumentException("Every action needs a shortcut.");
        var actions = new HashSet<WindowAction>();
        var combinations = new HashSet<(uint, uint)>();
        foreach (HotkeyBinding binding in bindings)
        {
            if (binding is null) throw new ArgumentException("Missing shortcut action.");
            HotkeyBinding? original = HotkeyManager.Bindings.FirstOrDefault(b => b.Action == binding.Action);
            if (original is null || binding.Id != original.Id || !actions.Add(binding.Action))
                throw new ArgumentException("Each action must appear exactly once.");
            ValidateCombination(binding.Key, binding.Modifiers);
            if (!combinations.Add((binding.Key, binding.Modifiers)))
                throw new ArgumentException($"{Format(binding.Key, binding.Modifiers)} is assigned to more than one action.");
        }
        return HotkeyManager.Bindings.Select(original =>
        {
            HotkeyBinding binding = bindings.Single(b => b.Action == original.Action);
            return binding with { Label = Format(binding.Key, binding.Modifiers) };
        }).ToArray();
    }

    internal static void ValidateCombination(uint key, uint modifiers)
    {
        const uint allowed = NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModShift;
        if ((modifiers & ~allowed) != 0 || (modifiers & (NativeMethods.ModControl | NativeMethods.ModAlt)) == 0)
            throw new ArgumentException("Include Ctrl or Alt; Shift is optional. Windows-key combinations are not supported.");
        if (!(key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x70 and <= 0x87
            or 0x20 or >= 0x21 and <= 0x28 or 0x2D or 0x2E))
            throw new ArgumentException("Choose a letter, number-row key, F1–F24, arrow, Space, Home, End, Page Up/Down, Insert, or Delete.");
        if (key == 0x7B) throw new ArgumentException("F12 is reserved by Windows. Choose another key.");
    }

    internal static string Format(uint key, uint modifiers)
    {
        string name = key switch
        {
            0x20 => "Space", 0x21 => "Page Up", 0x22 => "Page Down", 0x23 => "End", 0x24 => "Home",
            0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down", 0x2D => "Insert", 0x2E => "Delete",
            >= 0x70 and <= 0x87 => $"F{key - 0x6F}", _ => ((char)key).ToString()
        };
        return ((modifiers & NativeMethods.ModControl) != 0 ? "Ctrl + " : "")
            + ((modifiers & NativeMethods.ModAlt) != 0 ? "Alt + " : "")
            + ((modifiers & NativeMethods.ModShift) != 0 ? "Shift + " : "") + name;
    }

    internal static string ActionName(WindowAction action) => action switch
    {
        WindowAction.Left => "Left half", WindowAction.Right => "Right half", WindowAction.Maximize => "Maximize", WindowAction.Restore => "Restore",
        WindowAction.TopLeft => "Top-left quarter", WindowAction.TopRight => "Top-right quarter",
        WindowAction.BottomLeft => "Bottom-left quarter", WindowAction.BottomRight => "Bottom-right quarter",
        WindowAction.LeftThird => "Left third", WindowAction.CenterThird => "Center third", WindowAction.RightThird => "Right third",
        WindowAction.LeftTwoThirds => "Left two-thirds", WindowAction.RightTwoThirds => "Right two-thirds",
        WindowAction.Center => "Center · keep size", WindowAction.ResizeSmall => "Small · 50%",
        WindowAction.ResizeMedium => "Medium · 70%", WindowAction.ResizeLarge => "Large · 90%", _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
}

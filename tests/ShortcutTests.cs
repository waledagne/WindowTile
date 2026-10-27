using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Interop;
using KeyTile;
using static KeyTile.NativeMethods;

internal static partial class Program
{
    private static bool SameBindings(HotkeyBinding[] a, HotkeyBinding[] b) => a.Select(x => (x.Id, x.Action, x.Key, x.Modifiers))
        .SequenceEqual(b.Select(x => (x.Id, x.Action, x.Key, x.Modifiers)));

    private static void TestShortcutSettings()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"KeyTile.Shortcuts-{Guid.NewGuid():N}");
        string file = Path.Combine(folder, "shortcuts.json");
        var settings = new ShortcutSettings(file);
        try
        {
            Check(SameBindings(settings.Load(), HotkeyManager.Bindings), "Missing shortcut settings use all 17 defaults");
            HotkeyBinding[] custom = ShortcutSettings.Validate(HotkeyManager.Bindings);
            custom[0] = custom[0] with { Key = 0x4A, Modifiers = ModControl | ModShift, Label = "untrusted label" };
            settings.Save(custom);
            Check(SameBindings(settings.Load(), custom), "Custom shortcuts persist all actions and modifiers across a fresh settings instance");
            Check(new ShortcutSettings(file).Load()[0].Label == "Ctrl + Shift + J", "Shortcut labels are generated from saved keys rather than stored display text");
            string saved = File.ReadAllText(file);
            var duplicate = custom.ToArray();
            duplicate[1] = duplicate[1] with { Key = duplicate[0].Key, Modifiers = duplicate[0].Modifiers };
            ExpectRejected(() => settings.Save(duplicate), "Duplicate shortcuts cannot be saved");
            Check(File.ReadAllText(file) == saved, "Rejected edits preserve the previous settings file");
            Check(Directory.GetFiles(folder).Length == 1, "Successful and rejected saves leave no temporary files");
            foreach ((uint key, uint modifiers) in new[] { (0x41u, 0u), (0x41u, ModShift), (0x41u, 8u), (0x41u, ModControl | ModNoRepeat),
                (0u, ModControl), (0x1Bu, ModControl), (0x7Bu, ModAlt) })
                ExpectRejected(() => ShortcutSettings.ValidateCombination(key, modifiers), $"Invalid shortcut combination is rejected: key {key}, modifiers {modifiers}");
            foreach (uint key in new uint[] { 0x30, 0x41, 0x70, 0x87, 0x25, 0x24, 0x20, 0x2E })
            {
                ShortcutSettings.ValidateCombination(key, ModControl | ModAlt | ModShift);
                Check(true, $"Supported key can be customized: {ShortcutSettings.Format(key, ModControl | ModAlt | ModShift)}");
            }
            ExpectRejected(() => ShortcutSettings.Validate(custom.Skip(1).ToArray()), "Incomplete action lists are rejected");
            var wrongIdentity = custom.ToArray();
            wrongIdentity[0] = wrongIdentity[0] with { Id = 1000 };
            ExpectRejected(() => ShortcutSettings.Validate(wrongIdentity), "Saved shortcut IDs cannot change their action identity");
            wrongIdentity[0] = custom[1];
            ExpectRejected(() => ShortcutSettings.Validate(wrongIdentity), "Repeated actions are rejected");
            wrongIdentity[0] = custom[0] with { Action = (WindowAction)999 };
            ExpectRejected(() => ShortcutSettings.Validate(wrongIdentity), "Unknown actions are rejected");
            foreach (string invalid in new[] { "{broken", "null", "{}", "{\"Version\":2,\"Shortcuts\":[]}",
                "{\"Version\":1,\"Shortcuts\":[null]}", "{\"Version\":1,\"Shortcuts\":[{\"Action\":\"Unknown\"}]}" })
            {
                File.WriteAllText(file, invalid);
                ExpectRejected(() => settings.Load(), "Malformed or unsupported shortcut profiles are rejected without overwriting them");
                Check(File.ReadAllText(file) == invalid, "Invalid profile remains available for recovery");
            }
            File.WriteAllText(file, new string(' ', 65537));
            ExpectRejected(() => settings.Load(), "Oversized shortcut profiles are rejected");
            settings.Save(HotkeyManager.Bindings);
            Check(SameBindings(settings.Load(), HotkeyManager.Bindings), "Restore defaults can replace a malformed profile and survive reload");
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
            if (Directory.Exists(folder)) Directory.Delete(folder);
        }
    }

    private static void ExpectRejected(Action action, string message)
    {
        bool rejected = false;
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or JsonException) { rejected = true; }
        Check(rejected, message);
    }

    private static void TestCustomHotkeys()
    {
        using var blocker = new HwndSource(new HwndSourceParameters("KeyTile custom shortcut blocker")
            { ParentWindow = new nint(-3), Width = 0, Height = 0, WindowStyle = 0 });
        using var manager = new HotkeyManager();
        Check(manager.SetEnabled(true).All(status => status.Registered), "Default shortcuts are registered before editing");
        HotkeyBinding[] custom = ShortcutSettings.Validate(HotkeyManager.Bindings);
        custom[0] = custom[0] with { Key = 0x4A, Modifiers = ModControl | ModAlt | ModShift };
        bool persisted = false;
        Check(manager.TryUpdateBindings(custom, () => persisted = true, out var statuses) && persisted && statuses.All(status => status.Registered),
            "Changing a shortcut registers the complete custom profile before saving");
        bool oldAvailable = RegisterHotKey(blocker.Handle, 80, ModControl | ModAlt | ModNoRepeat, 0x25);
        if (oldAvailable) UnregisterHotKey(blocker.Handle, 80);
        Check(oldAvailable, "Changing a shortcut releases its old key combination");
        bool customAvailable = RegisterHotKey(blocker.Handle, 81, custom[0].Modifiers | ModNoRepeat, custom[0].Key);
        if (customAvailable) UnregisterHotKey(blocker.Handle, 81);
        Check(!customAvailable, "The custom combination belongs to KeyTile after saving");
        Check(RegisterHotKey(blocker.Handle, 82, ModControl | ModAlt | ModShift | ModNoRepeat, 0x4B), "Reserve a custom combination in another window");
        try
        {
            var conflict = custom.ToArray();
            conflict[0] = conflict[0] with { Key = 0x4B };
            persisted = false;
            Check(!manager.TryUpdateBindings(conflict, () => persisted = true, out statuses) && !persisted
                && !statuses.Single(status => status.Binding.Action == WindowAction.Left).Registered,
                "Conflicting custom shortcut prevents persistence and identifies the affected action");
            Check(SameBindings(manager.CurrentBindings.ToArray(), custom), "A conflict restores the previous shortcut profile");
            customAvailable = RegisterHotKey(blocker.Handle, 83, custom[0].Modifiers | ModNoRepeat, custom[0].Key);
            if (customAvailable) UnregisterHotKey(blocker.Handle, 83);
            Check(!customAvailable, "Previous shortcuts remain registered after a rejected change");
        }
        finally { UnregisterHotKey(blocker.Handle, 82); }
        bool failed = false;
        try { manager.TryUpdateBindings(HotkeyManager.Bindings, () => throw new IOException("Simulated save failure"), out _); }
        catch (IOException) { failed = true; }
        Check(failed && SameBindings(manager.CurrentBindings.ToArray(), custom), "A save failure restores the previous custom bindings");
        manager.SetEnabled(false);
        Check(manager.TryUpdateBindings(HotkeyManager.Bindings, () => { }, out _), "Defaults can be restored while shortcuts are paused");
        foreach (HotkeyBinding binding in HotkeyManager.Bindings)
        {
            bool acquired = RegisterHotKey(blocker.Handle, 84, binding.Modifiers | ModNoRepeat, binding.Key);
            if (acquired) UnregisterHotKey(blocker.Handle, 84);
            Check(acquired, $"Saving while paused leaves {binding.Label} released");
        }
        Check(manager.SetEnabled(true).All(status => status.Registered), "Resume uses the restored default profile");
    }
}

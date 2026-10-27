using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Interop;
using KeyTile;
using static KeyTile.NativeMethods;

internal static partial class Program
{
    private static void TestIndividualShortcutSettings()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"KeyTile.Individual-{Guid.NewGuid():N}");
        string path = Path.Combine(folder, "shortcuts.json");
        var settings = new ShortcutSettings(path);
        Directory.CreateDirectory(folder);
        try
        {
            HotkeyBinding[] custom = ShortcutSettings.Validate(HotkeyManager.Bindings);
            custom[0] = custom[0] with { Key = 0x4A, Modifiers = ModControl | ModShift };
            string legacy = JsonSerializer.Serialize(new
            {
                Version = 1, Shortcuts = custom.Select(binding => new { Action = binding.Action.ToString(), binding.Key, binding.Modifiers })
            });
            File.WriteAllText(path, legacy);
            Check(SameBindings(settings.Load(), custom) && settings.Load().All(binding => binding.Enabled),
                "Version 1 profiles retain their custom keys and enable every shortcut");
            Check(File.ReadAllText(path) == legacy, "Loading a legacy profile does not rewrite personal settings");
            custom[0] = custom[0] with { Enabled = false };
            settings.Save(custom);
            HotkeyBinding[] loaded = new ShortcutSettings(path).Load();
            Check(SameBindings(loaded, custom), "Disabled state and remembered custom combinations survive a fresh settings load");
            using (JsonDocument profile = JsonDocument.Parse(File.ReadAllText(path)))
                Check(profile.RootElement.GetProperty("Version").GetInt32() == 2
                    && !profile.RootElement.GetProperty("Shortcuts")[0].GetProperty("Enabled").GetBoolean(),
                    "New profiles explicitly store enabled flags in version 2");
            custom[0] = custom[0] with { Key = custom[1].Key, Modifiers = custom[1].Modifiers };
            settings.Save(custom);
            Check(SameBindings(settings.Load(), custom), "A disabled shortcut may remember a combination used by an enabled action");
            string saved = File.ReadAllText(path);
            var duplicate = custom.ToArray();
            duplicate[0] = duplicate[0] with { Enabled = true };
            ExpectRejected(() => settings.Save(duplicate), "Re-enabling a duplicate combination is rejected");
            Check(File.ReadAllText(path) == saved, "Rejected re-enabling preserves the previously saved disabled state");
            var invalid = custom.ToArray();
            invalid[0] = invalid[0] with { Key = 0 };
            ExpectRejected(() => settings.Save(invalid), "Disabled entries must still contain valid remembered combinations");
            string incomplete = legacy.Replace("\"Version\":1", "\"Version\":2", StringComparison.Ordinal);
            File.WriteAllText(path, incomplete);
            ExpectRejected(() => settings.Load(), "Version 2 profiles require an explicit enabled state for every action");
            Check(File.ReadAllText(path) == incomplete, "Malformed enabled state does not overwrite the profile");
            HotkeyBinding[] allOff = custom.Select(binding => binding with { Enabled = false }).ToArray();
            settings.Save(allOff);
            Check(settings.Load().All(binding => !binding.Enabled), "All seventeen shortcuts can be disabled and reloaded");
            settings.Save(HotkeyManager.Bindings);
            Check(settings.Load().All(binding => binding.Enabled) && SameBindings(settings.Load(), HotkeyManager.Bindings),
                "Saving defaults enables every action and restores all default combinations");
            var row = new ShortcutRow(custom[0], "Basics");
            int changes = 0;
            row.Changed += () => changes++;
            row.Enabled = true;
            Check(row.Binding.Enabled && row.Binding.Key == custom[0].Key && changes == 1,
                "Editor enabling retains the remembered combination and notifies draft validation");
            row.SetBinding(HotkeyManager.Bindings[0]);
            Check(row.Enabled && row.Label == HotkeyManager.Bindings[0].Label, "Editor defaults update both checkbox state and shortcut label");
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }

    private static void TestIndividualHotkeys()
    {
        using var blocker = new HwndSource(new HwndSourceParameters("KeyTile individual shortcut probe")
            { ParentWindow = new nint(-3), Width = 0, Height = 0, WindowStyle = 0 });
        HotkeyBinding[] oneOff = ShortcutSettings.Validate(HotkeyManager.Bindings);
        oneOff[0] = oneOff[0] with { Enabled = false };
        using var manager = new HotkeyManager(oneOff);
        var statuses = manager.SetEnabled(true);
        Check(!statuses[0].Registered && statuses[0].Error == 0 && statuses.Skip(1).All(status => status.Registered),
            "Disabling Left releases only its shortcut and produces no conflict error");
        Check(RegisterHotKey(blocker.Handle, 90, oneOff[0].Modifiers | ModNoRepeat, oneOff[0].Key),
            "Another window can acquire an individually disabled combination");
        try
        {
            bool persisted = false;
            Check(manager.TryUpdateBindings(oneOff, () => persisted = true, out statuses) && persisted,
                "An occupied disabled combination does not prevent saving other enabled shortcuts");
            persisted = false;
            Check(!manager.TryUpdateBindings(HotkeyManager.Bindings, () => persisted = true, out statuses) && !persisted
                && !statuses[0].Registered && statuses[0].Error != 0,
                "Re-enabling a combination owned by another window fails before persistence");
            Check(SameBindings(manager.CurrentBindings.ToArray(), oneOff), "A failed re-enable restores the disabled profile");
        }
        finally { UnregisterHotKey(blocker.Handle, 90); }
        Check(manager.TryUpdateBindings(HotkeyManager.Bindings, () => { }, out statuses) && statuses.All(status => status.Registered),
            "A freed shortcut can be re-enabled alongside every other action");
        bool available = RegisterHotKey(blocker.Handle, 91, oneOff[0].Modifiers | ModNoRepeat, oneOff[0].Key);
        if (available) UnregisterHotKey(blocker.Handle, 91);
        Check(!available, "The re-enabled shortcut belongs to KeyTile again");
        bool failed = false;
        try { manager.TryUpdateBindings(oneOff, () => throw new IOException("Simulated save failure"), out _); }
        catch (IOException) { failed = true; }
        Check(failed && manager.CurrentBindings.All(binding => binding.Enabled), "A failed save restores the previous per-shortcut enabled flags");
        manager.TryUpdateBindings(oneOff, () => { }, out _);
        manager.SetEnabled(false);
        statuses = manager.SetEnabled(true);
        Check(!statuses[0].Registered && statuses.Skip(1).All(status => status.Registered),
            "Global Pause/Resume does not re-enable an individually disabled shortcut");
        HotkeyBinding[] allOff = oneOff.Select(binding => binding with { Enabled = false }).ToArray();
        bool saved = false;
        Check(manager.TryUpdateBindings(allOff, () => saved = true, out statuses) && saved
            && statuses.All(status => !status.Registered && status.Error == 0), "An all-disabled profile saves without false conflicts");
        foreach (HotkeyBinding binding in allOff)
        {
            available = RegisterHotKey(blocker.Handle, 92, binding.Modifiers | ModNoRepeat, binding.Key);
            if (available) UnregisterHotKey(blocker.Handle, 92);
            Check(available, $"An all-disabled profile frees {binding.Label}");
        }
        manager.SetEnabled(false);
        Check(manager.TryUpdateBindings(oneOff, () => { }, out _), "Individual choices can be saved while globally paused");
        available = RegisterHotKey(blocker.Handle, 93, oneOff[1].Modifiers | ModNoRepeat, oneOff[1].Key);
        if (available) UnregisterHotKey(blocker.Handle, 93);
        Check(available && !manager.CurrentBindings[0].Enabled, "Saving while paused preserves both the global pause and individual choices");
    }
}

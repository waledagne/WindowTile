using System;
using System.IO;
using System.Runtime.InteropServices;

namespace KeyTile;

internal sealed class StartupManager
{
    private readonly string _executable, _shortcut;

    internal StartupManager(string executablePath, string? startupFolder = null)
    {
        _ = BuildCommand(executablePath);
        _executable = executablePath;
        _shortcut = Path.Combine(startupFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.Startup), "KeyTile.lnk");
    }

    internal static string BuildCommand(string executablePath)
    {
        if (!Path.IsPathFullyQualified(executablePath) || executablePath.IndexOfAny(['"', '\r', '\n']) >= 0
            || !string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Startup requires the full path to a Windows executable.", nameof(executablePath));
        string command = $"\"{executablePath}\" --tray";
        if (command.Length > 260)
            throw new ArgumentException("Move KeyTile to a shorter folder path before enabling startup.", nameof(executablePath));
        return command;
    }

    private T WithShortcut<T>(Func<dynamic, T> action)
    {
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows shortcut support is unavailable."))!;
        object? shortcut = null;
        try
        {
            shortcut = ((dynamic)shell).CreateShortcut(_shortcut);
            return action(shortcut);
        }
        finally
        {
            if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
        }
    }

    internal bool IsEnabled => File.Exists(_shortcut) && WithShortcut(link =>
        string.Equals((string)link.TargetPath, _executable, StringComparison.OrdinalIgnoreCase)
        && string.Equals((string)link.Arguments, "--tray", StringComparison.OrdinalIgnoreCase));

    internal void SetEnabled(bool enabled)
    {
        // A development or portable copy must not disable another copy's startup shortcut.
        if (!enabled && File.Exists(_shortcut) && !IsEnabled) return;
        if (enabled)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_shortcut)!);
            WithShortcut(link =>
            {
                link.TargetPath = _executable;
                link.Arguments = "--tray";
                link.WorkingDirectory = Path.GetDirectoryName(_executable);
                link.Description = "Start KeyTile quietly at sign-in";
                link.Save();
                return true;
            });
        }
        else File.Delete(_shortcut);
    }
}

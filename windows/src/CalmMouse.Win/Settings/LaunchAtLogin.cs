// "Launch at login": a "CalmMouse" value under HKCU\Software\Microsoft\Windows\CurrentVersion\Run
// holding the quoted path of the running exe. Off by default; the setting turns it on and off.
// At start, if it's on but the value points somewhere else (the app was moved or updated into a
// new folder), the value is rewritten. Windows' own Startup apps switch (Task Manager, Settings)
// is kept separately under StartupApproved and is left alone.

using Microsoft.Win32;

namespace CalmMouse.Win.Settings;

public sealed class LaunchAtLogin
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "CalmMouse";

    private readonly RegistryKey _root;
    private readonly string _keyPath;

    /// <param name="exePath">The exe to start at login.</param>
    /// <param name="root">The registry root (HKCU unless a test passes its own).</param>
    /// <param name="keyPath">The Run key under <paramref name="root"/> (tests use a throwaway key).</param>
    public LaunchAtLogin(string exePath, RegistryKey? root = null, string keyPath = RunKeyPath)
    {
        ExePath = exePath;
        _root = root ?? Registry.CurrentUser;
        _keyPath = keyPath;
    }

    public string ExePath { get; }

    /// <summary>What the Run value should hold: the exe path in quotes.</summary>
    public string Command => $"\"{ExePath}\"";

    /// <summary>What the Run value holds now, or null if there is none.</summary>
    public string? RegisteredCommand
    {
        get
        {
            using RegistryKey? key = _root.OpenSubKey(_keyPath, writable: false);
            return key?.GetValue(ValueName) as string;
        }
    }

    public bool IsRegistered => RegisteredCommand != null;

    /// <summary>The Run value is there and starts this exe.</summary>
    public bool IsCurrent => string.Equals(RegisteredCommand, Command, StringComparison.OrdinalIgnoreCase);

    /// <summary>Adds (or corrects) the Run value when <paramref name="enabled"/>, removes it otherwise. True if anything changed.</summary>
    public bool Apply(bool enabled)
    {
        if (enabled)
        {
            if (IsCurrent) { return false; }
            using RegistryKey key = _root.CreateSubKey(_keyPath, writable: true);
            key.SetValue(ValueName, Command, RegistryValueKind.String);
            return true;
        }
        using RegistryKey? existing = _root.OpenSubKey(_keyPath, writable: true);
        if (existing?.GetValue(ValueName) == null) { return false; }
        existing.DeleteValue(ValueName, throwOnMissingValue: false);
        return true;
    }

    /// <summary>At start: if the setting is on, make sure the Run value points at this exe. Leaves things alone when it's off.</summary>
    public bool Repair(bool enabled) => enabled && Apply(true);
}

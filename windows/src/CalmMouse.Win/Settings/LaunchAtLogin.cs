// "Start when I sign in": a "CalmMouse" value under HKCU\Software\Microsoft\Windows\CurrentVersion\Run
// holding the quoted path of the running exe. Off by default; the setting turns it on and off.
//
// Windows keeps its own switch for each Run entry (Task Manager › Startup apps, Settings › Apps ›
// Startup) under Explorer\StartupApproved\Run: a binary value whose first byte is odd when the user
// turned the entry off there. CalmMouse reads it so Settings can say "Turned off in Task Manager",
// and never changes it: that is the user's own choice, made in Windows.

using System.IO;
using Microsoft.Win32;

namespace CalmMouse.Win.Settings;

/// <summary>What Windows will actually do at the next sign-in.</summary>
public enum LaunchAtLoginState
{
    /// <summary>No Run value for CalmMouse.</summary>
    Off,
    /// <summary>A Run value that starts this exe, and Windows will run it.</summary>
    On,
    /// <summary>A Run value pointing at another copy of CalmMouse (moved, or a different build).</summary>
    OtherCopy,
    /// <summary>A Run value is there, but the user turned it off in Task Manager or Settings › Startup.</summary>
    TurnedOffInWindows,
}

public sealed class LaunchAtLogin
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    public const string ValueName = "CalmMouse";
    /// <summary>The release exe's file name. Builds with another name (or `dotnet run`) never repair the Run value.</summary>
    public const string ReleaseExeName = "CalmMouse.exe";

    private readonly RegistryKey _root;
    private readonly string _keyPath;
    private readonly string _approvedKeyPath;

    /// <param name="exePath">The exe to start at sign-in.</param>
    /// <param name="root">The registry root (HKCU unless a test passes its own).</param>
    /// <param name="keyPath">The Run key under <paramref name="root"/> (tests use a throwaway key).</param>
    /// <param name="approvedKeyPath">The StartupApproved\Run key (tests use a throwaway key).</param>
    public LaunchAtLogin(string exePath, RegistryKey? root = null, string keyPath = RunKeyPath, string approvedKeyPath = ApprovedKeyPath)
    {
        ExePath = exePath;
        _root = root ?? Registry.CurrentUser;
        _keyPath = keyPath;
        _approvedKeyPath = approvedKeyPath;
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

    /// <summary>The user turned the entry off in Task Manager or Settings › Startup (an odd first byte in StartupApproved).</summary>
    public bool IsTurnedOffInWindows
    {
        get
        {
            using RegistryKey? key = _root.OpenSubKey(_approvedKeyPath, writable: false);
            return key?.GetValue(ValueName) is byte[] { Length: > 0 } flags && (flags[0] & 1) != 0;
        }
    }

    public LaunchAtLoginState State =>
        !IsRegistered ? LaunchAtLoginState.Off
        : IsTurnedOffInWindows ? LaunchAtLoginState.TurnedOffInWindows
        : IsCurrent ? LaunchAtLoginState.On
        : LaunchAtLoginState.OtherCopy;

    /// <summary>Whether this build may rewrite the Run value by itself at start: only the release exe, never a debug build or `dotnet`.</summary>
    public static bool MayRepair(string exePath, bool debugBuild) =>
        !debugBuild && string.Equals(Path.GetFileName(exePath), ReleaseExeName, StringComparison.OrdinalIgnoreCase);

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

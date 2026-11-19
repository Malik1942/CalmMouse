// Which window a wheel event will scroll, and whether CalmMouse may touch it.
//
// Windows sends a wheel event to the window under the cursor by default ("Scroll inactive windows
// when I hover over them"), so the target comes from the hook's cursor point, not the foreground
// window. SPI_GETMOUSEWHEELROUTING says otherwise when that setting is off: "focus" means the
// foreground window gets it, "hybrid" means desktop apps by focus and Store apps by hover.
//
// Resolution runs inside the hook callback, so it only makes calls that never wait on another
// program (WindowFromPoint sends WM_NCHITTEST only to windows of the calling thread, and this
// thread owns nothing but a message-only window) and caches the slow part, per process.

using CalmMouse.Core.Routing;

namespace CalmMouse.Win.Input;

/// <summary>What Windows' "where does the wheel go" setting says (SPI_GETMOUSEWHEELROUTING).</summary>
public enum WheelRouting
{
    /// <summary>MOUSEWHEEL_ROUTING_FOCUS: the foreground window.</summary>
    Focus = 0,
    /// <summary>MOUSEWHEEL_ROUTING_HYBRID: desktop apps by focus, Store apps under the cursor.</summary>
    Hybrid = 1,
    /// <summary>MOUSEWHEEL_ROUTING_MOUSE_POS: the window under the cursor (the Windows 10 and 11 default).</summary>
    Hover = 2,
}

public static class WheelRoutingSetting
{
    /// <summary>The setting from SPI_GETMOUSEWHEELROUTING's value; null (the call failed, Windows before 10) or anything unknown means hover.</summary>
    public static WheelRouting FromSpi(uint? value) => value switch
    {
        0 => WheelRouting.Focus,
        1 => WheelRouting.Hybrid,
        _ => WheelRouting.Hover,
    };

    internal static unsafe WheelRouting Read()
    {
        uint value = 0;
        return Native.SystemParametersInfoW(Native.SPI_GETMOUSEWHEELROUTING, 0, &value, 0) != 0 ? FromSpi(value) : FromSpi(null);
    }
}

/// <summary>A process as far as the router cares: its file name and whether SendInput can reach it.</summary>
/// <param name="ExeName">The process's file name (<c>notepad.exe</c>), or null if it couldn't be read.</param>
/// <param name="Elevated">
/// True when its integrity level is above ours (run as administrator while we aren't), or when it
/// can't be looked at: UIPI would block our resends there, so its wheel events must pass untouched.
/// </param>
public readonly record struct ProcessInfo(string? ExeName, bool Elevated)
{
    public const string FrameHostExe = "ApplicationFrameHost.exe";

    /// <summary>The frame window of a Store (UWP) app; the app itself runs in another process.</summary>
    public bool IsFrameHost => string.Equals(ExeName, FrameHostExe, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Remembers what each process ID was, for a short while. A process ID can be reused by a new
/// process, so entries expire after <see cref="Lifetime"/> and are looked up again. When the name
/// is the same as before, the old string is kept: the router compares exe names by reference first.
/// Single-threaded (the input thread's).
/// </summary>
public sealed class ProcessInfoCache(Func<uint, ProcessInfo> describe, double lifetime = ProcessInfoCache.DefaultLifetime)
{
    /// <summary>Seconds an entry is trusted. Short enough that a reused process ID is noticed quickly.</summary>
    public const double DefaultLifetime = 2.0;

    /// <summary>Beyond this many processes, expired entries are dropped (and if none are, all of them).</summary>
    public const int MaxEntries = 256;

    private readonly record struct Entry(ProcessInfo Info, double At);
    private readonly Dictionary<uint, Entry> _entries = [];

    public double Lifetime { get; } = lifetime;
    public int Count => _entries.Count;

    /// <summary>How many times the slow lookup ran (for tests and the log).</summary>
    public long Lookups { get; private set; }

    public ProcessInfo Get(uint pid, double now)
    {
        bool known = _entries.TryGetValue(pid, out Entry old);
        if (known && now >= old.At && now - old.At < Lifetime) { return old.Info; }

        ProcessInfo info = describe(pid);
        Lookups++;
        if (known && old.Info.ExeName != null && string.Equals(old.Info.ExeName, info.ExeName, StringComparison.OrdinalIgnoreCase))
        {
            info = info with { ExeName = old.Info.ExeName };
        }
        if (!known && _entries.Count >= MaxEntries) { Prune(now); }
        _entries[pid] = new Entry(info, now);
        return info;
    }

    public void Clear() => _entries.Clear();

    private void Prune(double now)
    {
        foreach (KeyValuePair<uint, Entry> kv in _entries.ToList())
        {
            if (!(now >= kv.Value.At && now - kv.Value.At < Lifetime)) { _entries.Remove(kv.Key); }
        }
        if (_entries.Count >= MaxEntries) { _entries.Clear(); }
    }
}

/// <summary>Reads a process's name and integrity level. Never throws.</summary>
internal static unsafe class ProcessDescriber
{
    private static readonly int s_ownIntegrity = OwnIntegrity();

    /// <summary>Our own integrity level RID (0x2000 medium, 0x3000 high), or -1 if unreadable.</summary>
    public static int OwnIntegrityLevel => s_ownIntegrity;

    public static ProcessInfo Describe(uint pid)
    {
        if (pid == 0) { return new ProcessInfo(null, Elevated: true); }
        nint process = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, 0, pid);
        // Can't even look at it (a protected or system process): treat it as out of reach.
        if (process == 0) { return new ProcessInfo(null, Elevated: true); }
        try
        {
            string? name = null;
            char* path = stackalloc char[1024];
            uint length = 1024;
            // Only the file name is wanted, so the native path does: cheaper, and it works where a
            // drive letter can't be found (network or unmounted volumes).
            if (Native.QueryFullProcessImageNameW(process, Native.PROCESS_NAME_NATIVE, path, &length) != 0)
            {
                name = System.IO.Path.GetFileName(new string(path, 0, (int)length));
            }
            return new ProcessInfo(name, IsAboveUs(process));
        }
        catch
        {
            return new ProcessInfo(null, Elevated: true);
        }
        finally
        {
            Native.CloseHandle(process);
        }
    }

    /// <summary>
    /// UIPI's rule: input can't be sent into a process of higher integrity. A token we may not read
    /// counts as higher (an elevated process's token is closed to a normal one).
    /// </summary>
    private static bool IsAboveUs(nint process)
    {
        nint token;
        if (Native.OpenProcessToken(process, Native.TOKEN_QUERY, &token) == 0) { return true; }
        try
        {
            int theirs = IntegrityOf(token);
            if (theirs >= 0 && s_ownIntegrity >= 0) { return theirs > s_ownIntegrity; }
            // No integrity level: fall back to "is it elevated while we aren't".
            return IsElevated(token) && !OwnElevated();
        }
        finally
        {
            Native.CloseHandle(token);
        }
    }

    private static int IntegrityOf(nint token)
    {
        byte* buffer = stackalloc byte[128];
        uint needed;
        if (Native.GetTokenInformation(token, Native.TokenIntegrityLevel, buffer, 128, &needed) == 0) { return -1; }
        // TOKEN_MANDATORY_LABEL { SID_AND_ATTRIBUTES Label { PSID Sid; DWORD Attributes; } }
        nint sid = *(nint*)buffer;
        if (sid == 0) { return -1; }
        byte count = *Native.GetSidSubAuthorityCount(sid);
        if (count == 0) { return -1; }
        return (int)*Native.GetSidSubAuthority(sid, (uint)(count - 1));
    }

    private static bool IsElevated(nint token)
    {
        uint elevated = 0, needed;
        return Native.GetTokenInformation(token, Native.TokenElevation, &elevated, sizeof(uint), &needed) != 0 && elevated != 0;
    }

    private static bool OwnElevated()
    {
        nint token;
        if (Native.OpenProcessToken(Native.GetCurrentProcess(), Native.TOKEN_QUERY, &token) == 0) { return false; }
        try { return IsElevated(token); }
        finally { Native.CloseHandle(token); }
    }

    private static int OwnIntegrity()
    {
        try
        {
            nint token;
            if (Native.OpenProcessToken(Native.GetCurrentProcess(), Native.TOKEN_QUERY, &token) == 0) { return -1; }
            try { return IntegrityOf(token); }
            finally { Native.CloseHandle(token); }
        }
        catch
        {
            return -1;
        }
    }
}

/// <summary>
/// Turns a cursor point into the <see cref="WheelTarget"/> the router needs. Input thread only.
/// A cache hit costs a few window-manager calls and no allocation.
/// </summary>
internal sealed unsafe class WheelTargetResolver
{
    /// <summary>Where the wheel goes when nothing can be said: passed untouched.</summary>
    public static readonly WheelTarget Unknown = new(null, Elevated: true);

    private readonly ProcessInfoCache _processes;
    // The Store app inside the last ApplicationFrameHost window looked at.
    private nint _frameRoot;
    private uint _framePid;
    private double _frameAt = double.NegativeInfinity;

    public WheelTargetResolver(ProcessInfoCache? processes = null)
    {
        _processes = processes ?? new ProcessInfoCache(ProcessDescriber.Describe);
    }

    public WheelRouting Routing { get; set; } = WheelRouting.Hover;

    public ProcessInfoCache Processes => _processes;

    /// <summary>The window a wheel event at <paramref name="point"/> scrolls, under the current routing setting.</summary>
    public WheelTarget Resolve(Native.POINT point, double now)
    {
        nint hwnd;
        switch (Routing)
        {
            case WheelRouting.Focus:
                hwnd = (nint)Windows.Win32.PInvoke.GetForegroundWindow().Value;
                break;
            case WheelRouting.Hybrid:
                hwnd = Native.WindowFromPoint(point);
                if (!IsFrameHostWindow(hwnd, now)) { hwnd = (nint)Windows.Win32.PInvoke.GetForegroundWindow().Value; }
                break;
            default:
                hwnd = Native.WindowFromPoint(point);
                break;
        }
        return ForWindow(hwnd, now);
    }

    /// <summary>The target for one window: its top-level window's app, elevated if either is.</summary>
    public WheelTarget ForWindow(nint hwnd, double now)
    {
        if (hwnd == 0) { return Unknown; }
        nint root = Native.GetAncestor(hwnd, Native.GA_ROOT);
        if (root == 0) { root = hwnd; }

        uint pid = PidOf(root);
        if (pid == 0) { return Unknown; }
        ProcessInfo info = _processes.Get(pid, now);
        if (info.IsFrameHost)
        {
            uint hosted = HostedAppPid(root, pid, now);
            if (hosted != 0) { info = _processes.Get(hosted, now); }
        }

        bool elevated = info.Elevated;
        if (hwnd != root)
        {
            // A child window from another process (rare: embedded apps): the wheel lands there.
            uint childPid = PidOf(hwnd);
            if (childPid != 0 && childPid != pid) { elevated |= _processes.Get(childPid, now).Elevated; }
        }
        return new WheelTarget(info.ExeName, elevated);
    }

    private bool IsFrameHostWindow(nint hwnd, double now)
    {
        if (hwnd == 0) { return false; }
        nint root = Native.GetAncestor(hwnd, Native.GA_ROOT);
        uint pid = PidOf(root == 0 ? hwnd : root);
        return pid != 0 && _processes.Get(pid, now).IsFrameHost;
    }

    private static uint PidOf(nint hwnd)
    {
        uint pid = 0;
        Native.GetWindowThreadProcessId(hwnd, &pid);
        return pid;
    }

    private struct HostSearch
    {
        public uint HostPid;
        public uint Found;
    }

    /// <summary>
    /// A Store app's frame window (ApplicationFrameHost.exe) holds the app's own window as a child:
    /// the first child from another process is the app. 0 if there is none (a suspended app).
    /// </summary>
    private uint HostedAppPid(nint root, uint hostPid, double now)
    {
        if (root == _frameRoot && now >= _frameAt && now - _frameAt < _processes.Lifetime) { return _framePid; }
        var search = new HostSearch { HostPid = hostPid };
        Native.EnumChildWindows(root, &FindForeignChild, (nint)(&search));
        _frameRoot = root;
        _framePid = search.Found;
        _frameAt = now;
        return search.Found;
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static int FindForeignChild(nint hwnd, nint lParam)
    {
        var search = (HostSearch*)lParam;
        uint pid = PidOf(hwnd);
        if (pid != 0 && pid != search->HostPid)
        {
            search->Found = pid;
            return 0;   // stop
        }
        return 1;
    }
}

// Watches for other software that changes how the mouse scrolls (see WindowsMouseUtilities), so
// Settings can say so in one line instead of leaving a doubled reverse or glide unexplained. As
// on the Mac (ConflictMonitor.swift), only an overlap with something CalmMouse is doing right now
// counts: with every wheel option off, nothing is shown.
//
// Windows has no "app launched" notification that is cheap to use, so this looks at the running
// processes' names every 10 seconds (Process.GetProcesses: names only, no handles opened), plus
// whenever Refresh() is called (settings changed, a mouse came or went).

using System.Diagnostics;
using CalmMouse.Core;
using static CalmMouse.Core.MouseUtilityConflicts;
using static CalmMouse.Win.Localization.Strings;

namespace CalmMouse.Win.Conflicts;

/// <summary>
/// The Windows tools that change scrolling. <c>BundleIDs</c> holds lower-case exe names, matched
/// whole; <c>NameMatches</c> holds lower-case fragments of the process name (no ".exe"), for tools
/// that come as several processes or renamed builds. None of these was seen running on a real PC
/// yet. Some names were confirmed in install listings or user forums (2026-11-18); the rest are
/// marked "not confirmed".
/// </summary>
public static class WindowsMouseUtilities
{
    public static readonly IReadOnlyList<MouseUtility> Known =
    [
        // SmoothScroll (smoothscroll.net): SmoothScroll.exe (not confirmed).
        new("SmoothScroll", bundleIDs: ["smoothscroll.exe"], nameMatches: ["smoothscroll"],
            areas: [MouseUtilityArea.WheelScrolling]),
        // X-Mouse Button Control (Highresolution Enterprises): XMouseButtonControl.exe (not confirmed).
        new("X-Mouse Button Control", bundleIDs: ["xmousebuttoncontrol.exe"], nameMatches: ["xmousebuttoncontrol"],
            areas: [MouseUtilityArea.WheelScrolling]),
        // Logi Options+ (not confirmed): logioptionsplus_agent.exe runs in the background (the window is
        // logioptionsplus.exe, plus _updater, _appbroker…), so the fragment covers them all.
        new("Logi Options+", bundleIDs: ["logioptionsplus_agent.exe", "logioptionsplus.exe"], nameMatches: ["logioptionsplus"],
            areas: [MouseUtilityArea.WheelScrolling]),
        // Razer Synapse 4 runs as RazerAppEngine.exe (Razer Insider forum threads). Synapse 3:
        // "Razer Synapse 3.exe", "Razer Synapse Service.exe"; Synapse 2: RzSynapse.exe (not confirmed).
        new("Razer Synapse", bundleIDs: ["razerappengine.exe"], nameMatches: ["razer synapse", "rzsynapse"],
            areas: [MouseUtilityArea.WheelScrolling]),
        // Magic Utilities (magicutilities.net): MagicUtilities_Service.exe (the service) and
        // MagicMouseUtilities.exe (the app), per install listings of versions 3.0 to 3.1.
        new("Magic Utilities", bundleIDs: [], nameMatches: ["magicutilities", "magicmouseutilities"],
            areas: [MouseUtilityArea.WheelScrolling]),
        // AutoHotkey v1 and v2: AutoHotkey.exe, AutoHotkey64.exe, AutoHotkeyU64.exe, AutoHotkey32.exe…
        // (a compiled script has its own name and can't be told apart).
        new("AutoHotkey", bundleIDs: [], nameMatches: ["autohotkey"],
            areas: [MouseUtilityArea.WheelScrolling]),
    ];

    /// <summary>What CalmMouse is changing right now: wheel scrolling once a connected wheel mouse has any option on, unless paused.</summary>
    public static IReadOnlySet<MouseUtilityArea> ActiveAreas(bool paused, IEnumerable<WheelSettings> connectedWheelMice) =>
        !paused && connectedWheelMice.Any(w => w.Smooth || w.Reverse || w.BlockWhilePressed)
            ? new HashSet<MouseUtilityArea> { MouseUtilityArea.WheelScrolling }
            : new HashSet<MouseUtilityArea>();

    /// <summary>A process as the conflict check sees it: its exe name (lower case) and its name.</summary>
    public static RunningApp FromProcessName(string processName) =>
        new(processName.ToLowerInvariant() + ".exe", processName);
}

public sealed class ConflictWatcher : IDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(10);

    private readonly Func<IReadOnlySet<MouseUtilityArea>> _activeAreas;
    private readonly Func<IReadOnlyList<RunningApp>> _running;
    private readonly IReadOnlyList<MouseUtility> _known;
    private readonly TimeSpan _interval;
    private readonly Lock _lock = new();
    /// <summary>Held from looking to publishing, so an older look can't land after a newer one.</summary>
    private readonly Lock _refreshLock = new();
    private readonly Timer _timer;
    private IReadOnlyList<MouseUtility> _conflicts = [];
    private bool _disposed;

    /// <param name="activeAreas">What CalmMouse is changing right now (called on a background thread).</param>
    /// <param name="running">The running apps (default: every process's name).</param>
    /// <param name="known">The tools to look for (default: <see cref="WindowsMouseUtilities.Known"/>).</param>
    public ConflictWatcher(Func<IReadOnlySet<MouseUtilityArea>> activeAreas, Func<IReadOnlyList<RunningApp>>? running = null,
                           IReadOnlyList<MouseUtility>? known = null, TimeSpan? interval = null)
    {
        _activeAreas = activeAreas;
        _running = running ?? RunningProcesses;
        _known = known ?? WindowsMouseUtilities.Known;
        _interval = interval ?? DefaultInterval;
        _timer = new Timer(_ => Refresh(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>The tools found last time, in the list's order.</summary>
    public IReadOnlyList<MouseUtility> Conflicts
    {
        get { lock (_lock) { return _conflicts; } }
    }

    /// <summary>Raised on a background thread when <see cref="Conflicts"/> changes.</summary>
    public event EventHandler? ConflictsChanged;

    /// <summary>Checks now, then every 10 seconds.</summary>
    public void Start() => _timer.Change(TimeSpan.Zero, _interval);

    /// <summary>Checks again now (on a thread-pool thread).</summary>
    public void RefreshSoon() => ThreadPool.QueueUserWorkItem(_ => Refresh());

    /// <summary>Checks now, on this thread.</summary>
    public void Refresh()
    {
        lock (_refreshLock)
        {
            IReadOnlySet<MouseUtilityArea> active = _activeAreas();
            IReadOnlyList<MouseUtility> found = active.Count == 0 ? [] : MouseUtilityConflicts.Conflicts(_known, _running(), active);
            lock (_lock)
            {
                if (_disposed || found.SequenceEqual(_conflicts)) { return; }
                _conflicts = found;
            }
            ConflictsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>"SmoothScroll can also change how your mouse scrolls. …", naming every tool found, or null.</summary>
    public string? Message => MessageFor(Conflicts);

    public static string? MessageFor(IReadOnlyList<MouseUtility> conflicts) =>
        conflicts.Count == 0
            ? null
            : L("%@ can also change how your mouse scrolls. If scrolling feels odd, turn off one of them.",
                Localization.Localizer.Shared.JoinList(conflicts.Select(c => c.Name).ToList()));

    /// <summary>
    /// The names of the processes that can touch this user's mouse: those in this sign-in session,
    /// and services (session 0), where some tools run their part. Another user's session on the
    /// same PC doesn't count. Process objects are disposed straight away.
    /// </summary>
    public static IReadOnlyList<RunningApp> RunningProcesses()
    {
        int ownSession = Process.GetCurrentProcess().SessionId;
        Process[] processes = Process.GetProcesses();
        var apps = new List<RunningApp>(processes.Length);
        foreach (Process process in processes)
        {
            try
            {
                if (InScope(process.SessionId, ownSession)) { apps.Add(WindowsMouseUtilities.FromProcessName(process.ProcessName)); }
            }
            catch (InvalidOperationException) { /* exited meanwhile */ }
            finally { process.Dispose(); }
        }
        return apps;
    }

    /// <summary>A process in this session, or a service (session 0).</summary>
    public static bool InScope(int processSession, int ownSession) => processSession == ownSession || processSession == 0;

    public void Dispose()
    {
        lock (_lock) { _disposed = true; }
        _timer.Dispose();
    }
}

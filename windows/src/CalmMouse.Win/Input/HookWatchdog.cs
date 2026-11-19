namespace CalmMouse.Win.Input;

/// <summary>What the watchdog wants done now.</summary>
public enum WatchdogAction
{
    None,
    /// <summary>
    /// Inject a marked no-op event and see whether the hook gets it. (A move to where the cursor
    /// already is: Windows drops zero-delta wheels and zero relative moves before any hook sees them.)
    /// </summary>
    Ping,
    /// <summary>The ping didn't come through: the hook is gone. Remove it and install it again.</summary>
    Reinstall,
}

/// <summary>
/// Notices when Windows has quietly removed the low-level hook (it does that to a hook that once
/// took longer than LowLevelHooksTimeout, without telling anyone). Pure logic; the input thread
/// does the pinging. Seconds on the input thread's clock.
/// </summary>
/// <remarks>
/// Any hook call proves the hook is there, and the mouse moving calls it constantly, so a ping is
/// only needed when the user is busy (keyboard, say) and the hook has heard nothing since the last
/// check. When nobody uses the PC there is no ping at all: an injected event counts as input, and
/// pinging an idle PC would keep its screen from ever turning off. (If the hook died while the PC
/// sat idle, the first check after the user is back finds out.)
/// </remarks>
public sealed class HookWatchdog(double interval = HookWatchdog.DefaultInterval, double pingTimeout = HookWatchdog.DefaultPingTimeout)
{
    public const double DefaultInterval = 3.0;
    public const double DefaultPingTimeout = 0.5;

    /// <summary>
    /// The MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK coordinate (0–65535 across the virtual
    /// screen) that lands on pixel <paramref name="pixel"/>, for a virtual screen starting at
    /// <paramref name="origin"/> and <paramref name="size"/> pixels wide (or high). Windows maps a
    /// coordinate n to origin + n × size / 65536 rounded down, so this is the smallest n that gives
    /// the pixel back: the ping moves the cursor exactly where it already is.
    /// </summary>
    public static int AbsoluteCoordinate(int pixel, int origin, int size)
    {
        if (size <= 1) { return 0; }
        long offset = Math.Clamp((long)pixel - origin, 0, size - 1);
        return (int)((offset * 65536 + size - 1) / size);
    }

    private double _lastActivity = double.NegativeInfinity;
    private double _lastCheck = double.NegativeInfinity;
    private double _pingAt = double.NaN;

    public double Interval { get; } = interval;
    public double PingTimeout { get; } = pingTimeout;

    public bool PingOutstanding => !double.IsNaN(_pingAt);

    /// <summary>The hook was (re)installed at <paramref name="t"/>.</summary>
    public void Started(double t)
    {
        _lastCheck = t;
        _lastActivity = t;
        _pingAt = double.NaN;
    }

    /// <summary>The hook was called (any message). Cheap: called for every mouse move.</summary>
    public void HookActivity(double t) => _lastActivity = t;

    /// <summary>A ping went out at <paramref name="t"/>.</summary>
    public void PingSent(double t) => _pingAt = t;

    /// <summary>The ping couldn't be sent (SendInput refused: a locked or secure desktop). Nothing to wait for.</summary>
    public void PingNotSent() => _pingAt = double.NaN;

    /// <summary>When <see cref="Check"/> next has something to decide.</summary>
    public double NextCheckAt => PingOutstanding ? _pingAt + PingTimeout : _lastCheck + Interval;

    /// <param name="userActive">Whether someone used the keyboard or mouse lately (not counting our own pings).</param>
    public WatchdogAction Check(double now, bool userActive)
    {
        if (PingOutstanding)
        {
            if (_lastActivity >= _pingAt)
            {
                _pingAt = double.NaN;
                _lastCheck = now;
                return WatchdogAction.None;
            }
            if (now - _pingAt >= PingTimeout)
            {
                _pingAt = double.NaN;
                _lastCheck = now;
                return WatchdogAction.Reinstall;
            }
            return WatchdogAction.None;
        }

        if (now - _lastCheck < Interval) { return WatchdogAction.None; }
        double previous = _lastCheck;
        _lastCheck = now;
        if (_lastActivity > previous) { return WatchdogAction.None; }   // heard from since the last check
        return userActive ? WatchdogAction.Ping : WatchdogAction.None;
    }
}

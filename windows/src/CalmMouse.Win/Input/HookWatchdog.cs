namespace CalmMouse.Win.Input;

/// <summary>
/// Notices when Windows has quietly removed the low-level hook (it does that to a hook that took
/// longer than LowLevelHooksTimeout, without telling anyone). Pure logic; seconds on the input
/// thread's clock.
/// </summary>
/// <remarks>
/// It injects nothing. Raw Input keeps arriving whatever happens to the hook, and every physical
/// mouse event Raw Input reports (a device handle, not 0) also goes through the hook. So when
/// physical Raw Input keeps coming for <see cref="SilenceLimit"/> while the hook hears nothing,
/// the hook is gone and is reinstalled. Reinstalls are at least <see cref="MinimumGap"/> apart, and
/// the gap doubles (up to <see cref="MaximumGap"/>) while the hook stays silent after one, so a
/// case where the hook is legitimately silent can't make it reinstall over and over.
/// (An earlier version injected a marked no-op move as a ping, but injected input reaches every
/// Raw Input reader, so a game reading raw deltas would have seen a jump every few seconds.)
/// </remarks>
public sealed class HookWatchdog(
    double silenceLimit = HookWatchdog.DefaultSilenceLimit,
    double minimumGap = HookWatchdog.DefaultMinimumGap,
    double maximumGap = HookWatchdog.DefaultMaximumGap)
{
    public const double DefaultSilenceLimit = 0.75;
    public const double DefaultMinimumGap = 2.0;
    public const double DefaultMaximumGap = 60.0;

    private double _silentSince = double.NaN;   // first physical Raw Input the hook hasn't answered
    private double _lastPhysical = double.NegativeInfinity;
    private double _lastReinstall = double.NegativeInfinity;
    private double _gap = minimumGap;

    public double SilenceLimit { get; } = silenceLimit;
    public double MinimumGap { get; } = minimumGap;
    public double MaximumGap { get; } = maximumGap;

    /// <summary>The wait before another reinstall may happen.</summary>
    public double CurrentGap => _gap;

    /// <summary>The hook was installed at <paramref name="t"/>.</summary>
    public void Started(double t)
    {
        _silentSince = double.NaN;
        _lastReinstall = t;
    }

    /// <summary>The hook was called (any message). Cheap: called for every mouse move.</summary>
    public void HookActivity(double t)
    {
        _silentSince = double.NaN;
        _gap = MinimumGap;
    }

    /// <summary>Raw Input reported a physical mouse event (any: move, button or wheel).</summary>
    public void PhysicalInput(double t)
    {
        if (double.IsNaN(_silentSince)) { _silentSince = t; }
        _lastPhysical = t;
    }

    /// <summary>
    /// When <see cref="Check"/> could next say Reinstall. Infinity while the hook isn't silent, and
    /// while the silence hasn't yet lasted through <see cref="SilenceLimit"/> of input: only more
    /// physical input can change that, and it wakes the input thread anyway. (A time in the past
    /// here makes the input thread's wait return at once, over and over: the last Raw Input event
    /// of every movement arrives after its hook call, so this is the usual state when the mouse
    /// stops.)
    /// </summary>
    public double NextCheckAt =>
        double.IsNaN(_silentSince) || _lastPhysical - _silentSince < SilenceLimit
            ? double.PositiveInfinity
            : Math.Max(_silentSince + SilenceLimit, _lastReinstall + _gap);

    /// <summary>True: the hook has been silent through <see cref="SilenceLimit"/> of physical input. Reinstall it now.</summary>
    public bool Check(double now)
    {
        if (double.IsNaN(_silentSince)) { return false; }
        if (_lastPhysical - _silentSince < SilenceLimit) { return false; }   // input must have kept coming
        if (now - _lastReinstall < _gap) { return false; }
        _lastReinstall = now;
        _silentSince = double.NaN;
        _gap = Math.Min(_gap * 2, MaximumGap);   // reset by the next hook call
        return true;
    }

    /// <summary>Forget the silence so far (the input went somewhere our hook isn't asked about, such as an elevated window).</summary>
    public void Excuse() => _silentSince = double.NaN;
}

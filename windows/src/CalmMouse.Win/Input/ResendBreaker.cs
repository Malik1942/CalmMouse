namespace CalmMouse.Win.Input;

/// <summary>
/// The circuit breaker for resends. Swallowing a wheel event is only safe if the event we send in
/// its place gets through. Every resend that works comes back through Raw Input (no device, our
/// marker), so a resend that SendInput refuses, or that doesn't come back within
/// <see cref="EchoTimeout"/>, is a miss. After <see cref="TripAfter"/> misses in a row (a security
/// tool dropping injected input, say) the breaker opens: the engine stops swallowing and every wheel
/// event passes untouched for <see cref="CurrentPause"/>, then it tries again. The pause doubles
/// each time it trips again (up to <see cref="MaximumPause"/>) and goes back to the first one once a
/// resend comes back. Pure logic; seconds on the input thread's clock.
/// </summary>
public sealed class ResendBreaker(
    int tripAfter = ResendBreaker.DefaultTripAfter,
    double echoTimeout = ResendBreaker.DefaultEchoTimeout,
    double firstPause = ResendBreaker.DefaultFirstPause,
    double maximumPause = ResendBreaker.DefaultMaximumPause)
{
    public const int DefaultTripAfter = 5;
    public const double DefaultEchoTimeout = 0.25;
    public const double DefaultFirstPause = 30;
    public const double DefaultMaximumPause = 600;

    /// <summary>Resends still waiting for their echo, oldest first. Bounded: a flood counts as misses.</summary>
    private readonly Queue<double> _waiting = new();
    private const int MaxWaiting = 256;
    private int _misses;
    private double _openUntil = double.NegativeInfinity;
    private bool _open;

    public int TripAfter { get; } = tripAfter;
    public double EchoTimeout { get; } = echoTimeout;
    public double FirstPause { get; } = firstPause;
    public double MaximumPause { get; } = maximumPause;
    public double CurrentPause { get; private set; } = firstPause;

    public long Trips { get; private set; }
    public long Echoes { get; private set; }
    public long Misses { get; private set; }

    /// <summary>Open: the engine must not swallow anything.</summary>
    public bool IsOpen => _open;

    /// <summary>When an open breaker will try again; NaN while closed.</summary>
    public double OpenUntil => _open ? _openUntil : double.NaN;

    /// <summary>When <see cref="Check"/> next has something to decide.</summary>
    public double NextCheckAt => _open ? _openUntil : _waiting.Count > 0 ? _waiting.Peek() + EchoTimeout : double.PositiveInfinity;

    /// <summary>A resend went to SendInput; <paramref name="accepted"/> is what SendInput said. True if this tripped the breaker.</summary>
    public bool Sent(double t, bool accepted)
    {
        if (!accepted) { return Miss(t); }
        if (_waiting.Count >= MaxWaiting)
        {
            _waiting.Dequeue();
            if (Miss(t)) { return true; }
        }
        _waiting.Enqueue(t);
        return false;
    }

    /// <summary>One of our resends came back through Raw Input.</summary>
    public void Echo(double t)
    {
        Echoes++;
        if (_waiting.Count > 0) { _waiting.Dequeue(); }
        _misses = 0;
        CurrentPause = FirstPause;
    }

    /// <summary>
    /// Expires resends whose echo is overdue, and closes an open breaker whose pause is over.
    /// Returns what changed: +1 it just tripped, -1 it just closed (try again), 0 nothing.
    /// </summary>
    public int Check(double now)
    {
        if (_open)
        {
            if (now < _openUntil) { return 0; }
            _open = false;
            return -1;
        }
        while (_waiting.Count > 0 && now - _waiting.Peek() >= EchoTimeout)
        {
            _waiting.Dequeue();
            if (Miss(now)) { return 1; }
        }
        return 0;
    }

    private bool Miss(double t)
    {
        Misses++;
        if (_open) { return false; }
        _misses++;
        if (_misses < TripAfter) { return false; }
        _open = true;
        _openUntil = t + CurrentPause;
        CurrentPause = Math.Min(CurrentPause * 2, MaximumPause);
        _misses = 0;
        _waiting.Clear();
        Trips++;
        return true;
    }
}

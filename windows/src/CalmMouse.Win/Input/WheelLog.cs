using CalmMouse.Core.Routing;

namespace CalmMouse.Win.Input;

public enum WheelLogKind
{
    /// <summary>A physical wheel event in the hook, with the router's answer.</summary>
    HookWheel,
    /// <summary>An injected wheel event in the hook (ours or another program's): always passed.</summary>
    InjectedWheel,
    /// <summary>A Raw Input wheel.</summary>
    RawWheel,
    /// <summary>A wheel event the router asked for, injected with SendInput.</summary>
    Sent,
}

/// <summary>One line of the wheel log. Plain data, no allocation to record.</summary>
/// <param name="Time">Seconds on the input thread's clock.</param>
/// <param name="DeviceId">Raw Input hDevice (RawWheel); 0 otherwise.</param>
/// <param name="ExtraInfo">dwExtraInfo / ulExtraInformation.</param>
/// <param name="Target">The window the event scrolls (HookWheel).</param>
/// <param name="Swallowed">HookWheel: the hook ate it. Sent: SendInput failed (false = sent).</param>
public readonly record struct WheelLogEntry(
    WheelLogKind Kind,
    double Time,
    int Delta,
    bool Horizontal,
    long DeviceId,
    ulong ExtraInfo,
    WheelTarget Target,
    bool Swallowed);

/// <summary>
/// A fixed-size ring for the wheel log: the input thread writes, one reader drains. Writing never
/// allocates or blocks; when the reader falls behind, new entries are dropped and counted.
/// </summary>
public sealed class WheelLog(int capacity = 4096)
{
    private readonly WheelLogEntry[] _items = new WheelLogEntry[Math.Max(1, capacity)];
    private long _head;   // next write, written only by the writer
    private long _tail;   // next read, written only by the reader
    private long _dropped;

    public int Capacity => _items.Length;
    public long Dropped => Interlocked.Read(ref _dropped);

    public void Write(in WheelLogEntry entry)
    {
        long head = _head;
        if (head - Volatile.Read(ref _tail) >= _items.Length)
        {
            Interlocked.Increment(ref _dropped);
            return;
        }
        _items[head % _items.Length] = entry;
        Volatile.Write(ref _head, head + 1);
    }

    public bool TryRead(out WheelLogEntry entry)
    {
        long tail = _tail;
        if (tail >= Volatile.Read(ref _head))
        {
            entry = default;
            return false;
        }
        entry = _items[tail % _items.Length];
        Volatile.Write(ref _tail, tail + 1);
        return true;
    }
}

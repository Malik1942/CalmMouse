// The small types the scroll router takes and gives back. Windows-only ideas (exe names, the
// four Windows modifier keys, Raw Input device handles) live here rather than in Ports/, so the
// Swift port stays a one-to-one copy.

namespace CalmMouse.Core.Routing;

/// <summary>What the low-level mouse hook should do with a physical wheel event.</summary>
public enum HookAnswer
{
    /// <summary>Let it through untouched (return CallNextHookEx).</summary>
    Pass,
    /// <summary>Eat it (return 1). The router sends whatever should replace it, if anything.</summary>
    Swallow,
}

/// <summary>What kind of pointing device a Raw Input handle belongs to.</summary>
public enum DeviceKind
{
    /// <summary>Not recognised. Its scrolling is sent on unchanged.</summary>
    Unknown,
    /// <summary>An ordinary mouse with a scroll wheel: the only kind the core's wheel features apply to.</summary>
    WheelMouse,
    /// <summary>A laptop touchpad. CalmMouse leaves it alone: its scrolling is sent on unchanged.</summary>
    Touchpad,
}

/// <summary>The modifier keys held during a wheel event, as Windows names them.</summary>
[Flags]
public enum WinModifiers
{
    None = 0,
    Ctrl = 1 << 0,
    Alt = 1 << 1,
    Win = 1 << 2,
    Shift = 1 << 3,
}

/// <summary>A mouse button as Raw Input reports it.</summary>
public enum RawMouseButton
{
    Left,
    Right,
    /// <summary>The wheel's own click.</summary>
    Middle,
    X1,
    X2,
}

/// <summary>
/// One wheel event for the caller to inject with SendInput, with <see cref="ScrollRouter.ResendMarker"/>
/// in dwExtraInfo. <c>Delta</c> is in Windows wheel units (120 per notch, positive = away from the
/// user for vertical, to the right for horizontal); <c>Horizontal</c> picks MOUSEEVENTF_HWHEEL.
/// </summary>
public readonly record struct WheelSend(int Delta, bool Horizontal);

/// <summary>
/// A mouse as the router knows it. <c>DeviceId</c> is the Raw Input handle (hDevice) as a number;
/// <c>StableKey</c> survives reconnects (VID, PID, product name) and is what settings are stored by.
/// <c>Wheel</c> only matters for <see cref="DeviceKind.WheelMouse"/>.
/// </summary>
public sealed record RoutedDevice(long DeviceId, DeviceKind Kind, string StableKey)
{
    public WheelSettings Wheel { get; init; } = new();
}

/// <summary>
/// A per-app rule for wheel mice. Windows-only: the Swift core keeps wheel mice out of its
/// <c>AppRule</c>, so these are applied by the router, before the core sees the event.
/// <c>ExeName</c> is the foreground process's file name (for example <c>figma.exe</c>), matched
/// ignoring case, against the window the wheel event scrolls. A rule that is added is on; both of
/// its options start off.
/// </summary>
public sealed record WheelAppRule(string ExeName, string DisplayName)
{
    public bool Enabled { get; init; } = true;
    /// <summary>"Don't smooth in this app": every wheel mouse scrolls in whole notches here (for old apps that wait for a full 120).</summary>
    public bool NoSmoothing { get; init; }
    /// <summary>"Leave scrolling alone in this app": the hook passes every wheel event, as if CalmMouse weren't running.</summary>
    public bool LeaveAlone { get; init; }
}

/// <summary>Running counts for the status view and for tuning the matching on a real PC.</summary>
/// <param name="HooksPassed">Hook wheel events let through untouched.</param>
/// <param name="HooksSwallowed">Hook wheel events eaten (whatever replaced them was sent separately).</param>
/// <param name="UnclaimedResends">Swallowed hook events no Raw Input claimed in time, sent on unchanged (the touchpad safety net).</param>
/// <param name="UnmatchedRaws">Raw Input wheels that waited for a hook event that never came.</param>
/// <param name="IgnoredRaws">Raw Input wheels ignored as our own resends (no device, or our marker).</param>
/// <param name="InexactPairs">
/// Hook events paired with a Raw Input wheel of the same direction but a different delta. Should
/// stay near 0; if it does on real hardware, the direction-only fallback can go.
/// </param>
/// <param name="StuckWheelReleases">Times a wheel press was released because the hook said the middle button was up (a lost button-up).</param>
/// <param name="Faults">Exceptions caught inside the router. Each one left scrolling working (see <see cref="ScrollRouter.LastFault"/>).</param>
public readonly record struct RouterStats(
    long HooksPassed,
    long HooksSwallowed,
    long UnclaimedResends,
    long UnmatchedRaws,
    long IgnoredRaws,
    long InexactPairs,
    long StuckWheelReleases,
    long Faults);

/// <summary>
/// The window a wheel event will scroll: by default Windows sends the wheel to the window under
/// the cursor, not the foreground one, so the caller resolves this from the hook's cursor point.
/// <c>ExeName</c> is that window's process file name (null if unknown; UWP apps resolved through
/// ApplicationFrameHost); <c>Elevated</c> is true when it runs as administrator or its token
/// can't be read (SendInput can't reach it).
/// </summary>
public readonly record struct WheelTarget(string? ExeName, bool Elevated);

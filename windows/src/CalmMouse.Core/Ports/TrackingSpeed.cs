// Port of Sources/CalmMouseCore/TrackingSpeed.swift. `resolution(forSpeed:)` is `Resolution`.

namespace CalmMouse.Core;

/// <summary>Maps the Tracking speed slider to the resolution macOS is told a mouse has.</summary>
public static class TrackingSpeed
{
    /// <summary>What macOS assumes for a mouse that doesn't say (`HIDPointerResolution` is unset).</summary>
    public const double DefaultDPI = 400;
    /// <summary>The slider runs from -1 to 1; each end is this many times slower or faster.</summary>
    public const double MaximumFactor = 4;

    /// <summary>
    /// `speed` -1...1, where 0 is the mouse's normal speed. Returns 16.16 fixed-point DPI. A lower
    /// DPI makes the system move the pointer further per count, so faster means a smaller number.
    /// </summary>
    public static int Resolution(double speed)
    {
        double clamped = Math.Min(Math.Max(speed, -1), 1);
        double factor = Math.Pow(MaximumFactor, clamped);
        // Swift's `rounded()` rounds halves away from zero; `Int(_:)` traps on NaN, as `checked` throws.
        return checked((int)Math.Round(DefaultDPI / factor * 65536, MidpointRounding.AwayFromZero));
    }
}

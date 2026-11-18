// Port of Sources/CalmMouseCore/WheelSmoother.swift.
//
// Names: the nested `Step` struct is `StepResult`, because C# can't have a nested type and a
// method (`step(dt:)` is `Step`) with the same name. `add(dx:dy:)` is `Add`, `cancel()` is
// `Cancel`, `isIdle` and `owed` are the properties `IsIdle` and `Owed`; `owed` keeps Swift's
// tuple labels, `(x, y)`.
//
// Numbers: Swift's Int is 64 bits, so the pixels in a `StepResult` are longs, and `Int(x)` is
// `checked((long)x)`: both truncate, and both stop on NaN, infinity or out of range (Swift traps,
// C# throws). `rounded()` rounds halves away from zero; `rounded(.towardZero)` is Math.Truncate;
// `min`/`max` are Swift's (see SwiftMath), `exp` is Math.Exp.
//
// Value semantics: a mutable record struct, like Swift's `var` struct. Equality compares every
// field, private ones included, as Swift's synthesized Equatable does. Use `new WheelSmoother()`
// for the defaults; `default(WheelSmoother)` has a zero time constant and minimum speed.

namespace CalmMouse.Core;

/// <summary>
/// Turns the jumps of a scroll wheel into a glide.
///
/// A wheel reports whole notches, and macOS scrolls each one in a single frame, so the page
/// lurches. This keeps the distance still owed on each axis and pays it out a little every
/// frame, fastest at first and easing off, which is what reads as smooth. Pure arithmetic:
/// the app layer supplies the frame clock and posts the result.
/// </summary>
public record struct WheelSmoother
{
    /// <summary>
    /// How quickly the owed distance is paid out. After this long, about 63% of it is done;
    /// a longer time glides further and feels softer, a shorter one feels tighter. Seconds.
    /// </summary>
    public double TimeConstant { get; set; } = 0.09;
    /// <summary>
    /// Slowest the glide may crawl, in pixels per second. Easing alone never quite arrives:
    /// the last few pixels would dribble out one at a time for a third of a second.
    /// </summary>
    public double MinimumSpeed { get; set; } = 150;

    private double _owedX = 0;
    private double _owedY = 0;
    /// <summary>Sub-pixel remainder carried between frames, so whole pixels go out and none are lost.</summary>
    private double _carryX = 0;
    private double _carryY = 0;

    /// <summary>The defaults. (Without it `new WheelSmoother()` would be all zeros.)</summary>
    public WheelSmoother() { }

    public WheelSmoother(double timeConstant = 0.09, double minimumSpeed = 150)
    {
        TimeConstant = timeConstant;
        MinimumSpeed = minimumSpeed;
    }

    /// <summary>One frame's worth of scrolling, in whole pixels. (Swift: `WheelSmoother.Step`.)</summary>
    public record struct StepResult
    {
        public long X { get; set; }
        public long Y { get; set; }

        public StepResult(long x, long y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>True when nothing is owed: the frame clock can stop.</summary>
    public readonly bool IsIdle => _owedX == 0 && _owedY == 0 && _carryX == 0 && _carryY == 0;

    /// <summary>Distance still to scroll, in pixels.</summary>
    public readonly (double x, double y) Owed => (_owedX + _carryX, _owedY + _carryY);

    /// <summary>
    /// Adds a wheel tick's distance. Turning the wheel the other way drops whatever was still
    /// owed in the old direction, so the page turns round at once instead of finishing its
    /// glide first.
    /// </summary>
    public void Add(double dx, double dy)
    {
        Add(dx, ref _owedX, ref _carryX);
        Add(dy, ref _owedY, ref _carryY);
    }

    private static void Add(double delta, ref double owed, ref double carry)
    {
        if (delta == 0) { return; }   // guard delta != 0 (the same for NaN)
        if (owed != 0 && (owed < 0) != (delta < 0))
        {
            owed = 0;
            carry = 0;
        }
        owed += delta;
    }

    /// <summary>Drops everything owed: the page stops where it is.</summary>
    public void Cancel()
    {
        _owedX = 0; _owedY = 0; _carryX = 0; _carryY = 0;
    }

    /// <summary>
    /// Advances by `dt` seconds and returns the pixels to scroll this frame. Null when idle.
    /// A frame can come back as zero pixels on both axes while a fraction is still building up.
    /// </summary>
    public StepResult? Step(double dt)
    {
        if (IsIdle) { return null; }
        dt = SwiftMath.Max(0, SwiftMath.Min(dt, 0.1));   // a stalled frame clock must not fire one huge jump
        long x = Advance(ref _owedX, ref _carryX, dt, TimeConstant, MinimumSpeed);
        long y = Advance(ref _owedY, ref _carryY, dt, TimeConstant, MinimumSpeed);
        return new StepResult(x, y);
    }

    private static long Advance(ref double owed, ref double carry, double dt,
                                double timeConstant, double minimumSpeed)
    {
        if (owed != 0)
        {
            double sign = owed < 0 ? -1 : 1;
            double magnitude = Math.Abs(owed);
            double eased = timeConstant > 0 ? magnitude * (1 - Math.Exp(-dt / timeConstant)) : magnitude;
            double paid = SwiftMath.Min(magnitude, SwiftMath.Max(eased, minimumSpeed * dt));
            owed = magnitude - paid < 1e-9 ? 0 : owed - sign * paid;
            carry += sign * paid;
        }
        if (owed == 0)   // guard owed != 0 else …
        {
            // Nothing more is coming: settle the fraction to the nearest pixel and finish.
            double last = Math.Round(carry, MidpointRounding.AwayFromZero);
            carry = 0;
            return checked((long)last);
        }
        double whole = Math.Truncate(carry);
        carry -= whole;
        return checked((long)whole);
    }
}

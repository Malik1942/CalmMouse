namespace CalmMouse.Core;

/// <summary>
/// Swift's global `min` and `max`, which are not the same as Math.Min and Math.Max on doubles.
/// Swift's are generic over Comparable: `max(x, y)` is `y >= x ? y : x` and `min(x, y)` is
/// `y < x ? y : x`. So with a NaN the answer depends on the argument order (`max(.nan, 1)` is
/// NaN, `max(1, .nan)` is 1), where Math.Max always returns NaN; and for equal values the pick
/// is fixed (`max(0.0, -0.0)` is -0.0, where Math.Max gives 0.0).
/// </summary>
internal static class SwiftMath
{
    /// <summary>Swift's `max(x, y)`.</summary>
    public static double Max(double x, double y) => y >= x ? y : x;

    /// <summary>Swift's `min(x, y)`.</summary>
    public static double Min(double x, double y) => y < x ? y : x;
}

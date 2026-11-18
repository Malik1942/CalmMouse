// Port of Sources/CalmMouseCore/MouseFigure.swift. Pure geometry.
//
// Names: `surface(u:v:)` is `Surface`, `surfaceNormal(u:v:)` is `SurfaceNormal`; Swift's
// internal helpers stay internal (the tests see them, as Swift's `@testable` tests do).
// Swift's (x:, y:) tuples are (X, Y). `DeskCamera`'s initializer takes `frame` first, because
// C# optional parameters must come last; call it with named arguments, as Swift does.
// Vec3's operators are public because C# operators have to be.

namespace CalmMouse.Core;

/// <summary>
/// A point or direction in the mouse's own space: 1 unit = 1 cm, x across (right +), y along with
/// the front toward +y, z up from the desk.
/// </summary>
public record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    internal readonly double Dot(Vec3 b) => X * b.X + Y * b.Y + Z * b.Z;
    internal readonly Vec3 Cross(Vec3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    internal readonly Vec3 Normalized
    {
        get { double l = Math.Sqrt(X * X + Y * Y + Z * Z); return l > 0 ? this * (1 / l) : this; }
    }
}

/// <summary>
/// The Magic Mouse lid as the Settings renders were modelled in Blender (magicmouse.py): the
/// published 113.5 × 57.1 × 21.6 mm, its outline and side profile measured off Apple's
/// orthographic product views. Touches in Settings are drawn on this surface.
/// </summary>
public static class MagicMouseFigure
{
    internal const double HalfWidthMax = 2.855, HalfLength = 5.675, Height = 2.16;
    internal const double StraightTo = 0.55 * HalfLength, EndPower = 3.0, FrontTaper = 0.015;
    internal const double SeamMid = 1.26, SeamTip = 0.50, EdgeLand = 0.08;

    /// <summary>Half the width of the outline at `l` along the mouse (front −, back +).</summary>
    internal static double HalfWidth(double l)
    {
        double t = Math.Min(Math.Max(-l / HalfLength, 0), 1);
        double a = HalfWidthMax * (1 - FrontTaper * t * t);
        double al = Math.Abs(l);
        if (al <= StraightTo) { return a; }
        double v = Math.Min((al - StraightTo) / (HalfLength - StraightTo), 1);
        return a * Math.Pow(Math.Max(1 - Math.Pow(v, EndPower), 0), 1 / EndPower);
    }

    /// <summary>Height of the seam where the white lid meets the silver tray.</summary>
    internal static double Seam(double l)
    {
        double u = Math.Min(Math.Abs(l) / HalfLength, 1);
        return SeamTip + (SeamMid - SeamTip) * Math.Pow(Math.Max(1 - u * u, 0), 0.95);
    }

    /// <summary>
    /// The lid's height at fraction `r` of the way from the middle out to the outline, on the ray
    /// that meets the outline at `bl` along. Rolls over evenly into a narrow edge land.
    /// </summary>
    internal static double Dome(double r, double bl)
    {
        double edge = Seam(bl) + EdgeLand;
        double w = Math.Min((bl / HalfLength) * (bl / HalfLength), 1);
        double k = 0.80 + 0.08 * w, q = 40 + 50 * w;
        double g = Math.Pow(Math.Max(1 - r * r, 0), k) * Math.Pow(Math.Max(1 - Math.Pow(r, q), 0), 0.5);
        return edge + (Height - edge) * g;
    }

    /// <summary>How far out (x, l) sits on its ray from the middle, and where that ray meets the outline.</summary>
    internal static (double R, double Bl) Gauge(double x, double l)
    {
        if (Math.Abs(x) < 1e-9 && Math.Abs(l) < 1e-9) { return (0, HalfLength); }
        bool inside(double s) => Math.Abs(l) * s < HalfLength && HalfWidth(l * s) > Math.Abs(x) * s;
        double lo = 1.0, hi = 1.0;
        while (inside(hi)) { hi *= 1.5; }
        for (int i = 0; i < 50; i++)
        {
            double mid = (lo + hi) / 2;
            if (inside(mid)) { lo = mid; } else { hi = mid; }
        }
        double s = (lo + hi) / 2;
        return (1 / s, l * s);
    }

    /// <summary>The measured ridge along the middle, front to back.</summary>
    internal static double Ridge(double l)
    {
        double u = Math.Min(Math.Abs(l) / HalfLength, 1), tip = SeamTip + EdgeLand;
        return tip + (Height - tip) * Math.Pow(Math.Max(1 - u * u, 0), 0.85);
    }

    /// <summary>A smooth crown from the two measured profiles: the ridge along, the section across.</summary>
    internal static double Crown(double x, double l)
    {
        double w = HalfWidth(l), t = w > 1e-6 ? Math.Min(Math.Abs(x) / w, 1) : 1;
        double edge = Seam(l) + EdgeLand;
        return edge + (Ridge(l) - edge) * Math.Pow(Math.Max(1 - t * t, 0), 0.62);
    }

    /// <summary>Where the crown hands over to the ray-built dome, as a fraction of the way to the rim.</summary>
    internal const double BlendFrom = 0.35, BlendTo = 0.92;

    /// <summary>
    /// The lid's height over (x, l): the smooth crown in the middle, blending into the dome near
    /// the rim, where it rolls over evenly all round. The dome alone creases along the diagonals.
    /// </summary>
    internal static double LidHeight(double x, double l)
    {
        (double R, double Bl) g = Gauge(x, l);
        double r = Math.Min(g.R, 1);
        double k = Math.Min(Math.Max((r - BlendFrom) / (BlendTo - BlendFrom), 0), 1);
        double w = k * k * k * (k * (6 * k - 15) + 10);   // smootherstep: curvature stays continuous
        return Crown(x, l) * (1 - w) + Dome(r, g.Bl) * w;
    }

    /// <summary>The point on the lid at `u` across (−1 left … 1 right) and `v` along (−1 front … 1 back).</summary>
    public static Vec3 Surface(double u, double v)
    {
        double l = Math.Min(Math.Max(v, -1), 1) * HalfLength;
        double x = Math.Min(Math.Max(u, -1), 1) * HalfWidth(l);
        return new Vec3(x, -l, LidHeight(x, l));
    }

    /// <summary>Which way the lid faces at (u, v).</summary>
    public static Vec3 SurfaceNormal(double u, double v)
    {
        double e = 0.02;
        Vec3 du = Surface(u: u + e, v: v) - Surface(u: u - e, v: v);
        Vec3 dv = Surface(u: u, v: v + e) - Surface(u: u, v: v - e);
        Vec3 n = du.Cross(dv).Normalized;
        return n.Z < 0 ? n * -1 : n;
    }
}

/// <summary>
/// The camera of the desk renders: behind the mouse at 40°, 39 cm away, a 105 mm lens on a 36 mm
/// sensor, the mouse turned 25° plus `yaw` (degrees, counter-clockwise seen from above). `frame`
/// is the square render's side in points, drawn with its top-left corner at the origin.
/// </summary>
public readonly struct DeskCamera
{
    public const double HomeYaw = 25.0;
    internal readonly Vec3 Eye, Forward, Right, Up;
    internal readonly double Focal, CentreX, CentreY, CosA, SinA;

    public DeskCamera(double frame, double yaw = 0, double originX = 0, double originY = 0)
    {
        double elevation = 40.0 * Math.PI / 180, distance = 39.0;
        var target = new Vec3(0, 0, 0.75);
        Eye = new Vec3(target.X, target.Y - distance * Math.Cos(elevation), target.Z + distance * Math.Sin(elevation));
        Forward = (target - Eye).Normalized;
        Right = Forward.Cross(new Vec3(0, 0, 1)).Normalized;
        Up = Right.Cross(Forward);
        Focal = 105.0 / 36.0 * frame;
        CentreX = originX + frame / 2;
        CentreY = originY + frame / 2;
        double a = (HomeYaw + yaw) * Math.PI / 180;
        CosA = Math.Cos(a); SinA = Math.Sin(a);
    }

    /// <summary>Where the mouse's point `p` lands, with the whole mouse slid `slide` cm to the right.</summary>
    public (double X, double Y) Project(Vec3 p, double slide = 0)
    {
        var world = new Vec3(p.X * CosA - p.Y * SinA + slide, p.X * SinA + p.Y * CosA, p.Z);
        Vec3 d = world - Eye;
        double depth = d.Dot(Forward);
        return (CentreX + Focal * d.Dot(Right) / depth, CentreY - Focal * d.Dot(Up) / depth);
    }

    /// <summary>A circle of `radius` cm lying on the lid at (u, v), as seen: an ellipse, foreshortened.</summary>
    public (double X, double Y)[] Disc(double u, double v, double radius, double slide = 0, int steps = 28)
    {
        Vec3 p = MagicMouseFigure.Surface(u: u, v: v), n = MagicMouseFigure.SurfaceNormal(u: u, v: v);
        Vec3 t1 = n.Cross(Math.Abs(n.Z) > 0.9 ? new Vec3(1, 0, 0) : new Vec3(0, 0, 1)).Normalized, t2 = n.Cross(t1);
        var points = new (double X, double Y)[steps];
        for (int i = 0; i < steps; i++)
        {
            double a = 2 * Math.PI * i / steps;
            points[i] = Project(p + t1 * (radius * Math.Cos(a)) + t2 * (radius * Math.Sin(a)), slide: slide);
        }
        return points;
    }
}

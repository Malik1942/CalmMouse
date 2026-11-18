// Translated from Tests/CalmMouseCoreTests/MouseFigureTests.swift. XCTAssertEqual(a, b,
// accuracy:) is Assert.Equal(b, a, tolerance); xUnit takes no message there, so the point's name
// that Swift passes as the message is lost from the failure text. Tolerances are written as
// doubles: Assert.Equal(double, double, int) would round to that many decimals instead.
// `reduce(0, +)` is Aggregate, so the sums add in the same order.

namespace CalmMouse.Core.Tests;

/// <summary>
/// The Magic Mouse drawn in Settings: its shape and the camera of the Blender renders. Touches are
/// drawn on top of those renders, so the projection has to land where Blender put the pixels.
/// </summary>
public sealed class MouseFigureTests
{
    /// <summary>Points Blender itself projected for the 720 px desk render (camera.py → camera-desk.json).</summary>
    private readonly (string name, Vec3 local, double x, double y)[] blender =
    [
        ("apex", new Vec3(0, 0, 2.16), 360.0, 300.4563),
        ("front", new Vec3(0, 5.675, 0.58), 243.0049, 205.0799),
        ("back", new Vec3(0, -5.675, 0.58), 503.2089, 565.1846),
        ("right", new Vec3(2.855, 0, 1.26), 497.2286, 298.1482),
        ("left", new Vec3(-2.855, 0, 1.26), 216.0511, 381.4127),
    ];

    [Fact]
    public void testDeskCameraLandsOnBlendersPixels()
    {
        var camera = new DeskCamera(frame: 720);
        foreach ((string name, Vec3 local, double x, double y) point in blender)
        {
            (double X, double Y) p = camera.Project(point.local);
            Assert.Equal(point.x, p.X, 0.01);
            Assert.Equal(point.y, p.Y, 0.01);
        }
    }

    [Fact]
    public void testFrameSizeAndOriginScaleAndShiftTheSamePicture()
    {
        (double X, double Y) full = new DeskCamera(frame: 720).Project(blender[1].local);
        (double X, double Y) small = new DeskCamera(frame: 360, originX: 10, originY: -20).Project(blender[1].local);
        Assert.Equal(10 + full.X / 2, small.X, 1e-9);
        Assert.Equal(-20 + full.Y / 2, small.Y, 1e-9);
    }

    [Fact]
    public void testTurningTheMouseSpinsItAboutItsMiddle()
    {
        DeskCamera home = new(frame: 720), turned = new(yaw: 180, frame: 720);
        (double X, double Y) apex = turned.Project(blender[0].local);
        Assert.Equal(360.0, apex.X, 0.01);
        Assert.Equal(300.4563, apex.Y, 0.01);
        // Half a turn puts the front where the back was.
        (double X, double Y) front = turned.Project(blender[1].local), back = home.Project(blender[2].local);
        Assert.Equal(back.X, front.X, 0.01);
        Assert.Equal(back.Y, front.Y, 0.01);
    }

    [Fact]
    public void testSlidingMovesTheWholeMouseSideways()
    {
        var camera = new DeskCamera(frame: 720);
        (double X, double Y) still = camera.Project(blender[0].local), slid = camera.Project(blender[0].local, slide: 1);
        Assert.True(slid.X > still.X + 10);
        Assert.Equal(still.Y, slid.Y, 2.0);
    }

    [Fact]
    public void testTheLidPeaksInTheMiddleAndMeetsTheEdgeAtTheOutline()
    {
        Vec3 top = MagicMouseFigure.Surface(u: 0, v: 0);
        Assert.Equal(2.16, top.Z, 1e-6);
        Vec3 side = MagicMouseFigure.Surface(u: 1, v: 0);
        Assert.Equal(2.855, side.X, 1e-6);
        Assert.Equal(1.34, side.Z, 1e-6);   // seam 1.26 + the lid's edge land
        Vec3 front = MagicMouseFigure.Surface(u: 0, v: -1);
        Assert.Equal(5.675, front.Y, 1e-6); // front points to +y
    }

    /// <summary>
    /// The lid must curve evenly round its middle: built from rays alone it creased along the
    /// diagonals and came to a point there, which glossy reflections showed as a little pyramid.
    /// </summary>
    [Fact]
    public void testTheLidCurvesEvenlyRoundItsMiddle()
    {
        static double height(double x, double l) =>
            MagicMouseFigure.Surface(u: x / MagicMouseFigure.HalfWidth(l), v: l / 5.675).Z;
        double h = 0.01, rho = 0.6;
        double[] curvature = Enumerable.Range(0, 72).Select(i =>
        {
            double a = 2 * Math.PI * i / 72, x = rho * Math.Cos(a), l = rho * Math.Sin(a);
            return (height(x + h, l) + height(x - h, l) + height(x, l + h) + height(x, l - h) - 4 * height(x, l)) / (h * h);
        }).ToArray();
        double mean = curvature.Aggregate(0.0, (a, b) => a + b) / curvature.Length;
        double spread = (curvature.Max() - curvature.Min()) / Math.Abs(mean);
        Assert.True(spread < 0.2, $"curvature varies {(int)(spread * 100)}% round the middle: a crease");
    }

    [Fact]
    public void testTheLidFacesStraightUpAtItsTop()
    {
        Vec3 n = MagicMouseFigure.SurfaceNormal(u: 0, v: 0);
        Assert.Equal(1.0, n.Z, 0.001);
    }

    [Fact]
    public void testAFingerTouchIsDrawnAroundTheTouchedPoint()
    {
        var camera = new DeskCamera(frame: 720);
        (double X, double Y)[] ring = camera.Disc(u: 0.3, v: -0.4, radius: 0.5);
        Assert.Equal(28, ring.Length);
        if (ring.Length == 0) { return; }
        (double X, double Y) centre = camera.Project(MagicMouseFigure.Surface(u: 0.3, v: -0.4));
        double mx = ring.Select(p => p.X).Aggregate(0.0, (a, b) => a + b) / ring.Length,
               my = ring.Select(p => p.Y).Aggregate(0.0, (a, b) => a + b) / ring.Length;
        Assert.Equal(centre.X, mx, 1.0);
        Assert.Equal(centre.Y, my, 1.0);
        // 0.5 cm on the lid is roughly 0.5 cm on screen: tens of pixels across, not a dot.
        double width = ring.Select(p => p.X).Max() - ring.Select(p => p.X).Min();
        Assert.True(width > 30);
        Assert.True(width < 70);
    }
}

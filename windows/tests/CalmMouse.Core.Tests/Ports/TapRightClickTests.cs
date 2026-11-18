// Translated from Tests/CalmMouseCoreTests/TapRightClickTests.swift. XCTAssertEqual(actual,
// expected) is Assert.Equal(expected, actual); xUnit's Assert.Equal takes no message, so the
// Swift messages on those asserts are kept as comments. XCTAssertFalse keeps its message.
// `[.rightTap]` is `[TapEvent.RightTap]`, and `[]` stays `[]`. The `at:` / `endingAt:` labels are
// the parameter `t`. `r.config.x = y` writes through the ref-returning `Config` (see
// Ports/TapRecognizer.cs).

namespace CalmMouse.Core.Tests;

/// <summary>Tap-to-right-click, in both modes.</summary>
public sealed class TapRightClickTests
{
    private static TapRecognizer recognizer(RightClickMode mode = RightClickMode.RightSide,
                                            bool enabled = true,
                                            bool frontIsHighY = true)
    {
        var c = new TapConfig(sensitivity: 0.5);
        c.Enabled = true;
        c.TapRightClick = enabled;
        c.RightClickMode = mode;
        c.FrontIsHighY = frontIsHighY;
        c.DoubleTapWindow = 0.35;
        return new TapRecognizer(config: c);
    }

    private static TouchSample touch(long id, double x = 0.5, double y = 0.5) =>
        new(id: id, x: x, y: y, size: 0.6);

    /// <summary>
    /// Land and lift one clean tap at (x, y), lift completing at `t`; returns the events of the lift.
    /// (Swift's `endingAt t` follows the defaulted `y`; C# wants defaults last, so `t` comes
    /// before `y` here and every call names its arguments.)
    /// </summary>
    private static List<TapEvent> tap(TapRecognizer r, long id, double x, double t, double y = 0.5)
    {
        Assert.Equal([], r.HandleFrame([touch(id, x: x, y: y)], t: t - 0.08));
        return r.HandleFrame([], t: t);
    }

    // MARK: The default

    /// <summary>
    /// Pins the product decision: right-side is the default mode, because it mirrors where the
    /// physical Magic Mouse right click already lives and steals no existing gesture.
    /// </summary>
    [Fact]
    public void testRightSideIsTheDefaultMode()
    {
        Assert.Equal(RightClickMode.RightSide, new TapConfig().RightClickMode);
        Assert.False(new TapConfig().TapRightClick, "and the feature itself ships off");
    }

    // MARK: Right-side mode

    [Fact]
    public void testTapOnTheRightSideRightClicks()
    {
        var r = recognizer();
        Assert.Equal([TapEvent.RightTap], tap(r, id: 1, x: 0.85, t: 1.0));
    }

    [Fact]
    public void testTapAtTheCentreAndLeftStaysALeftClick()
    {
        var r = recognizer();
        Assert.Equal([TapEvent.Tap], tap(r, id: 1, x: 0.5, t: 1.0));
        Assert.Equal([TapEvent.Tap], tap(r, id: 2, x: 0.1, t: 2.0));
    }

    [Fact]
    public void testFeatureOffMeansEveryTapIsALeftClick()
    {
        var r = recognizer(enabled: false);
        Assert.Equal([TapEvent.Tap], tap(r, id: 1, x: 0.9, t: 1.0));
    }

    [Fact]
    public void testOrientationFlipMirrorsTheRightSide()
    {
        // frontIsHighY == false is a 180° rotation: the right side lands at LOW x.
        var r = recognizer(frontIsHighY: false);
        Assert.Equal([TapEvent.RightTap], tap(r, id: 1, x: 0.1, t: 1.0));
        Assert.Equal([TapEvent.Tap], tap(r, id: 2, x: 0.9, t: 2.0));
    }

    [Fact]
    public void testTapZoneStillGatesRightTaps()
    {
        var r = recognizer();
        r.Config.TapZoneEnabled = true;
        r.Config.TapZoneDepth = 0.5;
        r.Config.TapZoneEdgeMargin = 0.04;
        // Right side but BEHIND the front zone: rejected outright, no click of either kind.
        Assert.Equal([], tap(r, id: 1, x: 0.8, y: 0.2, t: 1.0));
        // Right side inside the zone: right click.
        Assert.Equal([TapEvent.RightTap], tap(r, id: 2, x: 0.8, y: 0.9, t: 2.0));
    }

    [Fact]
    public void testRightTapNeverArmsTapAndDrag()
    {
        var r = recognizer();
        r.Config.TapAndDrag = true;
        r.Config.DragWindow = 0.5;
        Assert.Equal([TapEvent.RightTap], tap(r, id: 1, x: 0.85, t: 1.0));
        // A follow-up touch right after a RIGHT tap is not a drag arm — drags are left-drags.
        Assert.Equal([], r.HandleFrame([touch(2, x: 0.85)], t: 1.1));
        Assert.False(r.DragActive);
        Assert.Equal([TapEvent.RightTap], r.HandleFrame([], t: 1.18));   // "it's just another right tap"
    }

    [Fact]
    public void testLeftTapStillArmsTapAndDragInRightSideMode()
    {
        var r = recognizer();
        r.Config.TapAndDrag = true;
        r.Config.DragWindow = 0.5;
        Assert.Equal([TapEvent.Tap], tap(r, id: 1, x: 0.4, t: 1.0));
        Assert.Equal([TapEvent.DragBegan], r.HandleFrame([touch(2, x: 0.4)], t: 1.2));
    }

    // MARK: Double-tap mode

    [Fact]
    public void testQuickSecondTapInTheSameSpotRightClicks()
    {
        var r = recognizer(mode: RightClickMode.DoubleTap);
        Assert.Equal([TapEvent.Tap], tap(r, id: 1, x: 0.5, t: 1.0));
        Assert.Equal([TapEvent.RightTap], tap(r, id: 2, x: 0.52, t: 1.25));
    }

    [Fact]
    public void testSlowSecondTapIsJustAnotherLeftClick()
    {
        var r = recognizer(mode: RightClickMode.DoubleTap);
        Assert.Equal([TapEvent.Tap], tap(r, id: 1, x: 0.5, t: 1.0));
        Assert.Equal([TapEvent.Tap], tap(r, id: 2, x: 0.5, t: 1.6));   // "outside the double-tap window"
    }

    [Fact]
    public void testFarawaySecondTapIsJustAnotherLeftClick()
    {
        var r = recognizer(mode: RightClickMode.DoubleTap);
        Assert.Equal([TapEvent.Tap], tap(r, id: 1, x: 0.2, t: 1.0));
        Assert.Equal([TapEvent.Tap], tap(r, id: 2, x: 0.8, t: 1.2));   // "other end of the surface"
    }

    [Fact]
    public void testThirdQuickTapStartsAFreshChain()
    {
        // tap-tap-tap = left, right, left — the right tap resets the chain, so it can't
        // alternate into an accidental second right click.
        var r = recognizer(mode: RightClickMode.DoubleTap);
        Assert.Equal([TapEvent.Tap], tap(r, id: 1, x: 0.5, t: 1.0));
        Assert.Equal([TapEvent.RightTap], tap(r, id: 2, x: 0.5, t: 1.25));
        Assert.Equal([TapEvent.Tap], tap(r, id: 3, x: 0.5, t: 1.5));
    }

    [Fact]
    public void testRightSidePlacementIsIrrelevantInDoubleTapMode()
    {
        var r = recognizer(mode: RightClickMode.DoubleTap);
        Assert.Equal([TapEvent.Tap], tap(r, id: 1, x: 0.9, t: 1.0));
        // "a lone tap on the right side stays a left click in this mode"
    }

    /// <summary>
    /// With tap-and-drag on, the quick follow-up touch arms a drag instead of completing as a
    /// tap — its unpressed lift surfaces as `.dragEnded`, and the app layer maps that to a
    /// right click while double-tap mode is active. This pins the recognizer half of that
    /// contract: the arm still happens, and the lift is still an unpressed `.dragEnded`.
    /// </summary>
    [Fact]
    public void testDoubleTapWithTapAndDragRoutesThroughTheDragPath()
    {
        var r = recognizer(mode: RightClickMode.DoubleTap);
        r.Config.TapAndDrag = true;
        r.Config.DragWindow = 0.35;
        Assert.Equal([TapEvent.Tap], tap(r, id: 1, x: 0.5, t: 1.0));
        Assert.Equal([TapEvent.DragBegan], r.HandleFrame([touch(2)], t: 1.1));
        Assert.Equal([TapEvent.DragEnded], r.HandleFrame([], t: 1.18));
    }
}

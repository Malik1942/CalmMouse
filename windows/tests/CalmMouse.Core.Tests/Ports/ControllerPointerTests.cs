// Translated from Tests/CalmMouseCoreTests/ControllerPointerTests.swift (both of its classes).
// XCTAssertEqual(actual, expected) is Assert.Equal(expected, actual); an expected `[]` stays
// `[]`, and `[.press(.left)]` is `[new PointerAction.Press(PointerButton.Left)]`.
// XCTAssertEqual(a, b, accuracy:) is Assert.Equal(b, a, tolerance); xUnit's Assert.Equal takes
// no message, so the Swift messages on those asserts are kept as comments.
// XCTAssertGreaterThan / LessThan are Assert.True on the comparison, with the message if any.
// `_ = x` on a discarded result is a plain call. `all += …` is AddRange. `moves`/`scrolls` add
// in the same order as Swift's `reduce(into:)`. `pointer.config.stickClicks = false` goes
// through a copy, since `Config` is a property holding a struct (see Ports/ControllerPointer.cs
// for the other names).

namespace CalmMouse.Core.Tests;

public sealed class ControllerPointerTests
{
    /// <summary>Swift's `(inout ControllerConfig) -> Void`.</summary>
    private delegate void ConfigChange(ref ControllerConfig config);

    private static ControllerPointer active(ConfigChange? change = null)
    {
        var config = new ControllerConfig();
        change?.Invoke(ref config);
        var pointer = new ControllerPointer(config: config);
        pointer.SetActive(true);
        return pointer;
    }

    private static TouchPoint p(double x, double y) => new(x: x, y: y);

    private static (double dx, double dy) moves(List<PointerAction> actions)
    {
        (double, double) sum = (0.0, 0.0);
        foreach (PointerAction action in actions)
        {
            if (action is PointerAction.Move(double dx, double dy)) { sum.Item1 += dx; sum.Item2 += dy; }
        }
        return sum;
    }

    private static (double v, double h) scrolls(List<PointerAction> actions)
    {
        (double, double) sum = (0.0, 0.0);
        foreach (PointerAction action in actions)
        {
            if (action is PointerAction.Scroll(double v, double h)) { sum.Item1 += v; sum.Item2 += h; }
        }
        return sum;
    }

    // MARK: Touchpad

    [Fact]
    public void testFirstReportOfATouchMovesNothing()
    {
        var pointer = active();
        Assert.Equal([], pointer.Touchpad(primary: p(0.5, 0.5), secondary: null, t: 0));
    }

    [Fact]
    public void testSlidingRightAndUpMovesThePointerRightAndUp()
    {
        var pointer = active();
        pointer.Touchpad(primary: p(-0.5, 0), secondary: null, t: 0);
        var all = new List<PointerAction>();
        for (int i = 1; i <= 10; i++)
        {
            all.AddRange(pointer.Touchpad(primary: p(-0.5 + (double)i * 0.05, (double)i * 0.02), secondary: null, t: (double)i * 0.008));
        }
        var m = moves(all);
        Assert.True(m.dx > 0);
        Assert.True(m.dy < 0, "up on the pad is up on the screen, where y grows downward");
        Assert.True(all.All(a => a is PointerAction.Move));
    }

    [Fact]
    public void testVerticalTravelIsScaledToThePadsShape()
    {
        ControllerPointer a = active(), b = active();
        a.Touchpad(primary: p(0, 0.1), secondary: null, t: 0);
        b.Touchpad(primary: p(0, 0.1), secondary: null, t: 0);
        var across = moves(a.Touchpad(primary: p(0.1, 0.1), secondary: null, t: 0.01));
        var upward = moves(b.Touchpad(primary: p(0, 0.2), secondary: null, t: 0.01));
        // The same 0.1 units: shorter vertically by the pad's aspect (and the slower speed
        // that comes with the shorter distance).
        Assert.True(Math.Abs(upward.dy) < Math.Abs(across.dx) * 0.6);
    }

    [Fact]
    public void testFasterSwipesGoFurtherForTheSameDistance()
    {
        double travel(double seconds)
        {
            var pointer = active();
            pointer.Touchpad(primary: p(-0.5, 0), secondary: null, t: 0);
            var all = new List<PointerAction>();
            for (int i = 1; i <= 10; i++)
            {
                all.AddRange(pointer.Touchpad(primary: p(-0.5 + (double)i * 0.05, 0), secondary: null, t: (double)i * seconds / 10));
            }
            return moves(all).dx;
        }
        Assert.True(travel(seconds: 0.1) > travel(seconds: 1) * 1.5);
    }

    [Fact]
    public void testPointerSpeedScalesMovement()
    {
        double travel(double speed)
        {
            var pointer = active((ref ControllerConfig c) => c.PointerSpeed = speed);
            pointer.Touchpad(primary: p(0, 0), secondary: null, t: 0);
            return moves(pointer.Touchpad(primary: p(0.1, 0), secondary: null, t: 0.02)).dx;
        }
        Assert.Equal(ControllerConfig.SpeedRange, travel(1) / travel(0), 1e-9);
        Assert.Equal(ControllerConfig.SpeedRange, travel(0) / travel(-1), 1e-9);
    }

    [Fact]
    public void testQuickStillTouchIsATap()
    {
        var pointer = active();
        pointer.Touchpad(primary: p(0.2, 0.2), secondary: null, t: 0);
        pointer.Touchpad(primary: p(0.21, 0.2), secondary: null, t: 0.05);
        Assert.Equal([new PointerAction.Click(PointerButton.Left)], pointer.Touchpad(primary: null, secondary: null, t: 0.1));
    }

    [Fact]
    public void testTwoFingerTapIsARightClick()
    {
        var pointer = active();
        pointer.Touchpad(primary: p(0.2, 0.2), secondary: null, t: 0);
        pointer.Touchpad(primary: p(0.2, 0.2), secondary: p(-0.2, 0.2), t: 0.03);
        Assert.Equal([new PointerAction.Click(PointerButton.Right)], pointer.Touchpad(primary: null, secondary: null, t: 0.1));
    }

    [Fact]
    public void testLongOrMovingTouchIsNotATap()
    {
        var slow = active();
        slow.Touchpad(primary: p(0, 0), secondary: null, t: 0);
        Assert.Equal([], slow.Touchpad(primary: null, secondary: null, t: 0.5));

        var moving = active();
        moving.Touchpad(primary: p(0, 0), secondary: null, t: 0);
        moving.Touchpad(primary: p(0.2, 0), secondary: null, t: 0.05);
        Assert.Equal([], moving.Touchpad(primary: null, secondary: null, t: 0.1));
    }

    [Fact]
    public void testTapToClickOffMeansNoTaps()
    {
        var pointer = active((ref ControllerConfig c) => c.TapToClick = false);
        pointer.Touchpad(primary: p(0, 0), secondary: null, t: 0);
        Assert.Equal([], pointer.Touchpad(primary: null, secondary: null, t: 0.05));
    }

    [Fact]
    public void testTwoFingersScrollInsteadOfMoving()
    {
        var pointer = active();
        pointer.Touchpad(primary: p(0, 0), secondary: p(0.3, 0), t: 0);
        var actions = pointer.Touchpad(primary: p(0, 0.1), secondary: p(0.3, 0.1), t: 0.01);
        Assert.Equal(0, moves(actions).dx);
        Assert.Equal(0, moves(actions).dy);
        // Natural scrolling: fingers going up push the page up, showing what is below.
        Assert.True(scrolls(actions).v < 0);
    }

    [Fact]
    public void testTwoFingerScrollFollowsTheSystemDirection()
    {
        var pointer = active((ref ControllerConfig c) => c.NaturalScrolling = false);
        pointer.Touchpad(primary: p(0, 0), secondary: p(0.3, 0), t: 0);
        var actions = pointer.Touchpad(primary: p(0, 0.1), secondary: p(0.3, 0.1), t: 0.01);
        Assert.True(scrolls(actions).v > 0);
    }

    [Fact]
    public void testAFingerJoiningOrLeavingDoesNotJumpThePointer()
    {
        var pointer = active();
        pointer.Touchpad(primary: p(0.5, 0), secondary: null, t: 0);
        // The first finger lifts while a second, far away, stays: the pointer stays put.
        pointer.Touchpad(primary: p(0.5, 0), secondary: p(-0.8, 0), t: 0.01);
        Assert.Equal([], pointer.Touchpad(primary: null, secondary: p(-0.8, 0), t: 0.02));
        var next = pointer.Touchpad(primary: null, secondary: p(-0.75, 0), t: 0.03);
        Assert.True(moves(next).dx > 0);
        Assert.True(moves(next).dx < 60);
    }

    // MARK: Pressing

    [Fact]
    public void testPressingThePadHoldsTheLeftButtonAndIsNotAlsoATap()
    {
        var pointer = active();
        pointer.Touchpad(primary: p(0, 0), secondary: null, t: 0);
        Assert.Equal([new PointerAction.Press(PointerButton.Left)], pointer.Button(ControllerButton.Touchpad, pressed: true, t: 0.02));
        Assert.True(pointer.IsHeld(PointerButton.Left));
        // Sliding while pressed still moves: the app sends it as a drag.
        Assert.True(moves(pointer.Touchpad(primary: p(0.2, 0), secondary: null, t: 0.05)).dx > 0);
        Assert.Equal([new PointerAction.Release(PointerButton.Left)], pointer.Button(ControllerButton.Touchpad, pressed: false, t: 0.08));
        Assert.Equal([], pointer.Touchpad(primary: null, secondary: null, t: 0.1));
    }

    [Fact]
    public void testPressingThePadWithTwoFingersIsARightClick()
    {
        var pointer = active();
        pointer.Touchpad(primary: p(0, 0), secondary: p(0.4, 0), t: 0);
        Assert.Equal([new PointerAction.Press(PointerButton.Right)], pointer.Button(ControllerButton.Touchpad, pressed: true, t: 0.02));
        // The release lifts the right button even if a finger has gone by then.
        pointer.Touchpad(primary: p(0, 0), secondary: null, t: 0.03);
        Assert.Equal([new PointerAction.Release(PointerButton.Right)], pointer.Button(ControllerButton.Touchpad, pressed: false, t: 0.05));
    }

    [Fact]
    public void testFaceButtonsAreLeftAndRight()
    {
        var pointer = active();
        Assert.Equal([new PointerAction.Press(PointerButton.Left)], pointer.Button(ControllerButton.South, pressed: true, t: 0));
        Assert.Equal([new PointerAction.Release(PointerButton.Left)], pointer.Button(ControllerButton.South, pressed: false, t: 0.1));
        Assert.Equal([new PointerAction.Press(PointerButton.Right)], pointer.Button(ControllerButton.East, pressed: true, t: 0.2));
        Assert.Equal([new PointerAction.Release(PointerButton.Right)], pointer.Button(ControllerButton.East, pressed: false, t: 0.3));
    }

    [Fact]
    public void testOverlappingPressesOfOneButtonReleaseOnce()
    {
        var pointer = active();
        Assert.Equal([new PointerAction.Press(PointerButton.Left)], pointer.Button(ControllerButton.South, pressed: true, t: 0));
        Assert.Equal([], pointer.Button(ControllerButton.Touchpad, pressed: true, t: 0.1));
        Assert.Equal([], pointer.Button(ControllerButton.South, pressed: false, t: 0.2));
        Assert.Equal([new PointerAction.Release(PointerButton.Left)], pointer.Button(ControllerButton.Touchpad, pressed: false, t: 0.3));
    }

    [Fact]
    public void testSwitchingOffLetsGoOfHeldButtons()
    {
        var pointer = active();
        pointer.Button(ControllerButton.South, pressed: true, t: 0);
        Assert.Equal([new PointerAction.Release(PointerButton.Left)], pointer.SetActive(false));
        Assert.Equal([], pointer.Button(ControllerButton.South, pressed: false, t: 0.1));
    }

    [Fact]
    public void testNothingHappensWhileInactive()
    {
        var pointer = new ControllerPointer();
        Assert.Equal([], pointer.Button(ControllerButton.South, pressed: true, t: 0));
        pointer.Touchpad(primary: p(0, 0), secondary: null, t: 0);
        Assert.Equal([], pointer.Touchpad(primary: p(0.3, 0), secondary: null, t: 0.01));
        pointer.Sticks(left: p(1, 0), right: p(0, 0));
        Assert.False(pointer.NeedsTicks);
        Assert.Equal([], pointer.Tick(dt: 0.01, t: 0.02));
    }

    // MARK: Sticks

    [Fact]
    public void testSticksInsideTheDeadZoneDoNothing()
    {
        var pointer = active();
        pointer.Sticks(left: p(0.1, 0.05), right: p(-0.05, 0.1));
        Assert.False(pointer.NeedsTicks);
        Assert.Equal([], pointer.Tick(dt: 0.01, t: 0));
    }

    [Fact]
    public void testLeftStickMovesAndRightStickScrolls()
    {
        var pointer = active();
        pointer.Sticks(left: p(1, 0), right: p(0, 1));
        Assert.True(pointer.NeedsTicks);
        var actions = pointer.Tick(dt: 0.01, t: 0);
        Assert.Equal(new ControllerConfig().StickMaxSpeed * 0.01, moves(actions).dx, 1e-6);
        Assert.Equal(0, moves(actions).dy, 1e-9);
        Assert.Equal(new ControllerConfig().StickMaxScroll * 0.01, scrolls(actions).v, 1e-6);   // "pushing up shows what is above"
    }

    [Fact]
    public void testStickUpMovesThePointerUp()
    {
        var pointer = active();
        pointer.Sticks(left: p(0, 1), right: p(0, 0));
        Assert.True(moves(pointer.Tick(dt: 0.01, t: 0)).dy < 0);
    }

    [Fact]
    public void testStickSpeedRisesGentlyFromCentre()
    {
        var pointer = active();
        pointer.Sticks(left: p(0.575, 0), right: p(0, 0));  // halfway between dead zone and edge
        double half = moves(pointer.Tick(dt: 0.01, t: 0)).dx;
        Assert.Equal(new ControllerConfig().StickMaxSpeed * 0.01 * 0.25, half, 1e-6);
    }

    [Fact]
    public void testAStalledClockDoesNotJump()
    {
        var pointer = active();
        pointer.Sticks(left: p(1, 0), right: p(0, 0));
        Assert.Equal(new ControllerConfig().StickMaxSpeed * 0.1, moves(pointer.Tick(dt: 5, t: 0)).dx, 1e-6);
    }

    // MARK: Stick clicks

    [Fact]
    public void testPressingTheSticksClicksLeftAndRight()
    {
        var pointer = active();
        Assert.Equal([new PointerAction.Press(PointerButton.Left)], pointer.Button(ControllerButton.LeftStick, pressed: true, t: 0));
        Assert.Equal([new PointerAction.Release(PointerButton.Left)], pointer.Button(ControllerButton.LeftStick, pressed: false, t: 0.1));
        Assert.Equal([new PointerAction.Press(PointerButton.Right)], pointer.Button(ControllerButton.RightStick, pressed: true, t: 0.5));
        Assert.Equal([new PointerAction.Release(PointerButton.Right)], pointer.Button(ControllerButton.RightStick, pressed: false, t: 0.6));
    }

    [Fact]
    public void testStickClicksCanBeTurnedOff()
    {
        var pointer = active((ref ControllerConfig c) => c.StickClicks = false);
        Assert.Equal([], pointer.Button(ControllerButton.LeftStick, pressed: true, t: 0));
        Assert.Equal([], pointer.Button(ControllerButton.RightStick, pressed: true, t: 0));
    }

    [Fact]
    public void testTurningStickClicksOffMidPressStillLetsGo()
    {
        var pointer = active();
        pointer.Button(ControllerButton.LeftStick, pressed: true, t: 0);
        var config = pointer.Config;
        config.StickClicks = false;
        pointer.Config = config;
        Assert.Equal([new PointerAction.Release(PointerButton.Left)], pointer.Button(ControllerButton.LeftStick, pressed: false, t: 0.1));
    }

    [Fact]
    public void testTheNudgeOfPressingAStickDoesNotMoveThePointer()
    {
        var pointer = active();
        pointer.Button(ControllerButton.LeftStick, pressed: true, t: 1);
        pointer.Sticks(left: p(0.4, 0.2), right: p(0, 0));
        Assert.Equal(0, moves(pointer.Tick(dt: 0.01, t: 1.05)).dx);
        // Held on and pushed on purpose: it drags.
        Assert.True(moves(pointer.Tick(dt: 0.01, t: 1.2)).dx > 0);
        Assert.True(pointer.IsHeld(PointerButton.Left));
    }

    [Fact]
    public void testTheNudgeOfLettingGoDoesNotMoveThePointer()
    {
        var pointer = active();
        pointer.Button(ControllerButton.LeftStick, pressed: true, t: 1);
        pointer.Button(ControllerButton.LeftStick, pressed: false, t: 1.1);
        pointer.Sticks(left: p(0.4, 0), right: p(0, 0));
        Assert.Equal([], pointer.Tick(dt: 0.01, t: 1.2));
        Assert.True(moves(pointer.Tick(dt: 0.01, t: 1.3)).dx > 0);
    }

    [Fact]
    public void testPressingTheRightStickDoesNotScroll()
    {
        var pointer = active();
        pointer.Button(ControllerButton.RightStick, pressed: true, t: 1);
        pointer.Sticks(left: p(0, 0), right: p(0, 0.5));
        Assert.Equal(0, scrolls(pointer.Tick(dt: 0.01, t: 1.05)).v);
        // The left stick isn't held still by a right-stick press.
        pointer.Sticks(left: p(1, 0), right: p(0, 0.5));
        Assert.True(moves(pointer.Tick(dt: 0.01, t: 1.06)).dx > 0);
    }

    [Fact]
    public void testAStickClickAndAFaceButtonShareTheLeftButton()
    {
        var pointer = active();
        Assert.Equal([new PointerAction.Press(PointerButton.Left)], pointer.Button(ControllerButton.LeftStick, pressed: true, t: 0));
        Assert.Equal([], pointer.Button(ControllerButton.South, pressed: true, t: 0.1));
        Assert.Equal([], pointer.Button(ControllerButton.LeftStick, pressed: false, t: 0.2));
        Assert.Equal([new PointerAction.Release(PointerButton.Left)], pointer.Button(ControllerButton.South, pressed: false, t: 0.3));
    }

    // MARK: On/off chord

    [Fact]
    public void testHoldingBothMenuButtonsTogglesOnceEvenWhileInactive()
    {
        var pointer = new ControllerPointer();
        pointer.Button(ControllerButton.Menu, pressed: true, t: 0);
        pointer.Button(ControllerButton.Options, pressed: true, t: 0.1);
        Assert.True(pointer.NeedsTicks);
        Assert.Equal([], pointer.Tick(dt: 0.01, t: 0.3));
        Assert.Equal([new PointerAction.Toggle()], pointer.Tick(dt: 0.01, t: 0.61));
        Assert.False(pointer.NeedsTicks);
        Assert.Equal([], pointer.Tick(dt: 0.01, t: 1.5));   // "held on, it fires once"
        pointer.Button(ControllerButton.Menu, pressed: false, t: 2);
        pointer.Button(ControllerButton.Options, pressed: false, t: 2);
        pointer.Button(ControllerButton.Menu, pressed: true, t: 3);
        pointer.Button(ControllerButton.Options, pressed: true, t: 3);
        Assert.Equal([new PointerAction.Toggle()], pointer.Tick(dt: 0.01, t: 3.6));
    }

    [Fact]
    public void testOneMenuButtonAloneNeverToggles()
    {
        var pointer = new ControllerPointer();
        pointer.Button(ControllerButton.Menu, pressed: true, t: 0);
        Assert.False(pointer.NeedsTicks);
        Assert.Equal([], pointer.Tick(dt: 0.01, t: 2));
    }

    [Fact]
    public void testLettingGoEarlyCancelsTheToggle()
    {
        var pointer = new ControllerPointer();
        pointer.Button(ControllerButton.Menu, pressed: true, t: 0);
        pointer.Button(ControllerButton.Options, pressed: true, t: 0);
        pointer.Button(ControllerButton.Options, pressed: false, t: 0.3);
        Assert.Equal([], pointer.Tick(dt: 0.01, t: 1));
    }
}

public sealed class ControllerAppsTests
{
    [Fact]
    public void testGameCategories()
    {
        Assert.True(ControllerApps.IsGameCategory("public.app-category.games"));
        Assert.True(ControllerApps.IsGameCategory("public.app-category.action-games"));
        Assert.True(ControllerApps.IsGameCategory("public.app-category.Role-Playing-Games"));
        Assert.False(ControllerApps.IsGameCategory("public.app-category.productivity"));
        Assert.False(ControllerApps.IsGameCategory("public.app-category.entertainment"));
    }
}

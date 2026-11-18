// Translated from Tests/CalmMouseCoreTests/ScrollBlockerTests.swift, including the test in its
// `extension ScrollBlockerTests` (same class here). XCTAssertEqual(actual, expected) is
// Assert.Equal(expected, actual). `.pass`/`.drop` are `new ScrollDecision.Pass()` /
// `new ScrollDecision.Drop()`, and the `at:` label of the button calls is the parameter `t`.

namespace CalmMouse.Core.Tests;

public sealed class ScrollBlockerTests
{
    private static ScrollEvent ev(double t, ScrollPhase phase = ScrollPhase.None, MomentumPhase momentum = MomentumPhase.None,
                                  double dx = 0, double dy = 3, bool magic = true) =>
        new(timestamp: t, fromMagicMouse: magic, isContinuous: true,
            phase: phase, momentum: momentum, deltaX: dx, deltaY: dy);

    private readonly ScrollDecision endedZero = new ScrollDecision.Rewrite(new ScrollRewrite(phase: ScrollPhase.Ended, momentum: MomentumPhase.None, zeroX: true, zeroY: true));
    private readonly ScrollDecision momentumEndedZero = new ScrollDecision.Rewrite(new ScrollRewrite(phase: ScrollPhase.None, momentum: MomentumPhase.Ended, zeroX: true, zeroY: true));

    // MARK: Baseline

    [Fact]
    public void testScrollPassesWhenNoButtonIsDown()
    {
        var b = new ScrollBlocker();
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0, ScrollPhase.Began)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.01, ScrollPhase.Changed)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.02, ScrollPhase.Ended)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.03, momentum: MomentumPhase.Began)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.04, momentum: MomentumPhase.Continued)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.05, momentum: MomentumPhase.Ended)));
    }

    [Fact]
    public void testNonMagicMouseScrollIsNeverTouched()
    {
        var b = new ScrollBlocker();
        b.MagicMouseButton(0, isDown: true, t: 0);
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.01, ScrollPhase.Began, magic: false)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.02, ScrollPhase.Changed, magic: false)));
    }

    [Fact]
    public void testFeatureCanBeDisabled()
    {
        var c = new BlockerConfig(); c.BlockScrollWhileClicked = false;
        var b = new ScrollBlocker(config: c);
        b.MagicMouseButton(0, isDown: true, t: 0);
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.01, ScrollPhase.Began)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.02, ScrollPhase.Changed)));
    }

    // MARK: The headline feature

    [Fact]
    public void testGestureStartingWhileClickedResumesOnceBlockingEnds()
    {
        var b = new ScrollBlocker();
        b.MagicMouseButton(0, isDown: true, t: 0);
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.01, ScrollPhase.Began)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.02, ScrollPhase.Changed)));
        b.MagicMouseButton(0, isDown: false, t: 0.03);
        // Still inside the release grace: keep swallowing.
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.10, ScrollPhase.Changed)));
        // Grace expired mid-gesture: the rest of the swipe is the user's deliberate scrolling
        // and must flow. (Swallowing it whole made "click, then scroll" dead for the entire
        // swipe, however long the finger stayed down.) Apps join mid-gesture — same contract as
        // a gesture that started before the tap was watching — then everything is normal,
        // momentum included.
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.50, ScrollPhase.Changed)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.505, ScrollPhase.Changed)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.51, ScrollPhase.Ended)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.52, momentum: MomentumPhase.Began)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.53, momentum: MomentumPhase.Continued)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.54, momentum: MomentumPhase.Ended)));
        // Next gesture: normal from the start.
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(1.0, ScrollPhase.Began)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(1.01, ScrollPhase.Changed)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(1.02, ScrollPhase.Ended)));
    }

    [Fact]
    public void testGestureEndingWhileStillBlockedStaysSwallowedWithItsMomentum()
    {
        var b = new ScrollBlocker();
        b.MagicMouseButton(0, isDown: true, t: 0);
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.01, ScrollPhase.Began)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.02, ScrollPhase.Changed)));
        // The whole gesture ends while the button is still down: nothing may leak, and the
        // momentum tail belongs to the swallowed gesture.
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.03, ScrollPhase.Ended)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.04, momentum: MomentumPhase.Began)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.05, momentum: MomentumPhase.Ended)));
        b.MagicMouseButton(0, isDown: false, t: 0.06);
    }

    [Fact]
    public void testReleaseGraceBlocksTrailingScrollAfterMouseUp()
    {
        var c = new BlockerConfig(); c.ReleaseGrace = 0.2;
        var b = new ScrollBlocker(config: c);
        b.MagicMouseButton(0, isDown: true, t: 0);
        b.MagicMouseButton(0, isDown: false, t: 0.10);
        // Finger lifts, produces a little scroll 50ms later: swallowed.
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.15, ScrollPhase.Began)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.16, ScrollPhase.Ended)));
        // After the grace window a new gesture is fine.
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.31, ScrollPhase.Began)));
    }

    [Fact]
    public void testMultipleButtonsOnlyUnblockWhenAllReleased()
    {
        var b = new ScrollBlocker();
        b.MagicMouseButton(0, isDown: true, t: 0);
        b.MagicMouseButton(1, isDown: true, t: 0.01);
        b.MagicMouseButton(0, isDown: false, t: 0.02);
        Assert.True(b.IsBlocking(t: 1.0));
        b.MagicMouseButton(1, isDown: false, t: 1.0);
        Assert.True(b.IsBlocking(t: 1.1));   // grace
        Assert.False(b.IsBlocking(t: 1.3));
    }

    [Fact]
    public void testForceReleaseClearsStuckButtons()
    {
        var b = new ScrollBlocker();
        b.MagicMouseButton(0, isDown: true, t: 0);
        b.ForceReleaseAllButtons(t: 5);
        Assert.False(b.AnyButtonDown);
        Assert.True(b.IsBlocking(t: 5.1));
        Assert.False(b.IsBlocking(t: 5.3));
    }

    // MARK: Click landing mid-gesture

    [Fact]
    public void testClickDuringVisibleGestureEndsItCleanlyThenSwallows()
    {
        var b = new ScrollBlocker();
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0, ScrollPhase.Began)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.01, ScrollPhase.Changed)));
        b.MagicMouseButton(0, isDown: true, t: 0.02);
        // The first event after the click is rewritten into a zero-delta "ended".
        Assert.Equal(endedZero, b.HandleScroll(ev(0.03, ScrollPhase.Changed)));
        // Everything else in this gesture — including its real ended and momentum — is dropped.
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.04, ScrollPhase.Changed)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.05, ScrollPhase.Ended)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.06, momentum: MomentumPhase.Began)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.07, momentum: MomentumPhase.Ended)));
    }

    [Fact]
    public void testClickAfterMayBeginCancelsIt()
    {
        var b = new ScrollBlocker();
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0, ScrollPhase.MayBegin, dy: 0)));
        b.MagicMouseButton(0, isDown: true, t: 0.01);
        Assert.Equal(new ScrollDecision.Rewrite(new ScrollRewrite(phase: ScrollPhase.Cancelled, momentum: MomentumPhase.None, zeroX: true, zeroY: true)),
                     b.HandleScroll(ev(0.02, ScrollPhase.Began)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.03, ScrollPhase.Changed)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.04, ScrollPhase.Ended)));
    }

    [Fact]
    public void testMayBeginWhileClickedIsSwallowedWithTheRest()
    {
        var b = new ScrollBlocker();
        b.MagicMouseButton(0, isDown: true, t: 0);
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.01, ScrollPhase.MayBegin, dy: 0)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.02, ScrollPhase.Began)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.03, ScrollPhase.Changed)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.04, ScrollPhase.Ended)));
    }

    // MARK: Click during momentum

    [Fact]
    public void testClickDuringVisibleMomentumStopsCoasting()
    {
        var b = new ScrollBlocker();
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0, ScrollPhase.Began)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.01, ScrollPhase.Ended)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.02, momentum: MomentumPhase.Began)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.03, momentum: MomentumPhase.Continued)));
        b.MagicMouseButton(0, isDown: true, t: 0.04);
        Assert.Equal(momentumEndedZero, b.HandleScroll(ev(0.05, momentum: MomentumPhase.Continued)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.06, momentum: MomentumPhase.Continued)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.07, momentum: MomentumPhase.Ended)));
        // The click is released, then a fresh gesture: passes normally, momentum included.
        b.MagicMouseButton(0, isDown: false, t: 0.08);
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(1.0, ScrollPhase.Began)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(1.01, ScrollPhase.Ended)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(1.02, momentum: MomentumPhase.Began)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(1.03, momentum: MomentumPhase.Ended)));
    }

    [Fact]
    public void testMomentumBeginningWhileClickedIsSwallowedWhole()
    {
        var b = new ScrollBlocker();
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0, ScrollPhase.Began)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.01, ScrollPhase.Ended)));
        b.MagicMouseButton(0, isDown: true, t: 0.015);
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.02, momentum: MomentumPhase.Began)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.03, momentum: MomentumPhase.Continued)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.04, momentum: MomentumPhase.Ended)));
    }

    // MARK: Phase-less scrolls

    [Fact]
    public void testPhaselessScrollIsSimplyDroppedWhileClicked()
    {
        var b = new ScrollBlocker();
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0)));
        b.MagicMouseButton(0, isDown: true, t: 0.01);
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.02)));
        b.MagicMouseButton(0, isDown: false, t: 0.03);
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.5)));
    }

    // MARK: Axis lock (opt-in)

    [Fact]
    public void testAxisLockZeroesMinorAxisOnceCommitted()
    {
        var c = new BlockerConfig(); c.AxisLock = true; c.AxisLockThreshold = 10; c.AxisLockRatio = 1.4;
        var b = new ScrollBlocker(config: c);
        // Small diagonal start: untouched until threshold reached.
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0, ScrollPhase.Began, dx: 1, dy: 4)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.01, ScrollPhase.Changed, dx: 1, dy: 4)));
        // cumY = 12 >= 10 and 12 >= 3*1.4 → locked vertical; horizontal drift zeroed from now on.
        Assert.Equal(new ScrollDecision.Rewrite(new ScrollRewrite(phase: ScrollPhase.Changed, momentum: MomentumPhase.None, zeroX: true, zeroY: false)),
                     b.HandleScroll(ev(0.02, ScrollPhase.Changed, dx: 1, dy: 4)));
        // Purely vertical events don't need rewriting.
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.03, ScrollPhase.Changed, dx: 0, dy: 4)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.04, ScrollPhase.Ended, dx: 0, dy: 0)));
        // Momentum inherits the lock.
        Assert.Equal(new ScrollDecision.Rewrite(new ScrollRewrite(phase: ScrollPhase.None, momentum: MomentumPhase.Began, zeroX: true, zeroY: false)),
                     b.HandleScroll(ev(0.05, momentum: MomentumPhase.Began, dx: 2, dy: 6)));
        // A new gesture starts fresh (horizontal this time).
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(1.0, ScrollPhase.Began, dx: 8, dy: 1)));
        Assert.Equal(new ScrollDecision.Rewrite(new ScrollRewrite(phase: ScrollPhase.Changed, momentum: MomentumPhase.None, zeroX: false, zeroY: true)),
                     b.HandleScroll(ev(1.01, ScrollPhase.Changed, dx: 8, dy: 1)));
    }

    [Fact]
    public void testAxisLockDoesNotEngageForGenuinelyDiagonalScroll()
    {
        var c = new BlockerConfig(); c.AxisLock = true;
        var b = new ScrollBlocker(config: c);
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev((double)i * 0.01, i == 0 ? ScrollPhase.Began : ScrollPhase.Changed, dx: 5, dy: 5)));
        }
    }

    [Fact]
    public void testAxisLockIsOffByDefault()
    {
        var b = new ScrollBlocker();
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0, ScrollPhase.Began, dx: 1, dy: 40)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.01, ScrollPhase.Changed, dx: 1, dy: 40)));
    }

    // extension ScrollBlockerTests

    [Fact]
    public void testLostEndedDoesNotPoisonTheNextGesture()
    {
        var b = new ScrollBlocker();
        b.MagicMouseButton(0, isDown: true, t: 0);
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(new ScrollEvent(timestamp: 0.01, phase: ScrollPhase.Began, deltaY: 3)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(new ScrollEvent(timestamp: 0.02, phase: ScrollPhase.Changed, deltaY: 3)));
        b.MagicMouseButton(0, isDown: false, t: 0.03);
        // No `ended` ever arrives; a brand-new gesture after the grace window must pass.
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(new ScrollEvent(timestamp: 1.0, phase: ScrollPhase.Began, deltaY: 3)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(new ScrollEvent(timestamp: 1.01, phase: ScrollPhase.Changed, deltaY: 3)));
    }
}

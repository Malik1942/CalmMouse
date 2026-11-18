// Translated from Tests/CalmMouseCoreTests/TapRecognizerTests.swift: all eight of its classes, the
// tests in its `extension TapRecognizerTests` (same class here), and its `private extension
// TapRecognizer` shim (`taps(_:at:)`, a file-local extension method here).
// XCTAssertEqual(actual, expected) is Assert.Equal(expected, actual); xUnit's Assert.Equal takes
// no message, so the Swift messages on those asserts are kept as comments. XCTAssertTrue/False
// keep their messages. XCTAssertLessThan / GreaterThan are Assert.True on the comparison.
// `[.tap]` is `[TapEvent.Tap]`, and `[]` stays `[]`. The `at:` label of the recognizer's methods
// (and of the helpers' `endingAt:`) is the parameter `t`. `r.config.x = y` writes through the
// ref-returning `Config`, so it changes the recognizer's own config, as in Swift. `_ = x` on a
// discarded result is a plain call; `events += …` is AddRange. Swift's Int counts are longs.

namespace CalmMouse.Core.Tests;

/// <summary>Swift: `private extension TapRecognizer` at the top of TapRecognizerTests.swift.</summary>
file static class TapRecognizerShim
{
    /// <summary>Compatibility shim for assertions that only care about click count.</summary>
    public static long taps(this TapRecognizer r, IReadOnlyList<TouchSample> touches, double t) =>
        r.HandleFrame(touches, t: t).LongCount(e => e == TapEvent.Tap);
}

public sealed class TapRecognizerTests
{
    private static TapRecognizer recognizer(double sensitivity = 0.5)
    {
        var c = new TapConfig(sensitivity: sensitivity);
        c.Enabled = true;
        return new TapRecognizer(config: c);
    }

    private static TouchSample touch(long id, double x = 0.5, double y = 0.5, double size = 0.6) =>
        new(id: id, x: x, y: y, size: size);

    // MARK: The basic tap

    [Fact]
    public void testCleanTapClicks()
    {
        var r = recognizer();
        Assert.Equal(0, r.taps([touch(1)], t: 0));
        Assert.Equal(0, r.taps([touch(1)], t: 0.05));
        Assert.Equal(1, r.taps([], t: 0.09));
    }

    [Fact]
    public void testTwoSequentialTapsClickTwice()
    {
        var r = recognizer();
        r.taps([touch(1)], t: 0);
        Assert.Equal(1, r.taps([], t: 0.08));
        r.taps([touch(2)], t: 0.25);
        Assert.Equal(1, r.taps([], t: 0.33));
        Assert.Equal(2, r.TapCount);
    }

    [Fact]
    public void testDisabledDoesNothing()
    {
        var r = recognizer();
        r.Config.Enabled = false;
        r.taps([touch(1)], t: 0);
        Assert.Equal(0, r.taps([], t: 0.08));
    }

    // MARK: Accidental-click prevention

    [Fact]
    public void testRestingFingerNeverClicks()
    {
        var r = recognizer();
        var t = 0.0;
        r.taps([touch(1)], t: t);
        while (t < 2.0) { t += 0.1; r.taps([touch(1)], t: t); }
        Assert.Equal(0, r.taps([], t: t + 0.05));   // "a long rest is not a tap"
    }

    [Fact]
    public void testSwipeAcrossSurfaceNeverClicks()
    {
        var r = recognizer();
        r.taps([touch(1, x: 0.3, y: 0.2)], t: 0);
        r.taps([touch(1, x: 0.32, y: 0.45)], t: 0.05);
        Assert.Equal(0, r.taps([], t: 0.09));   // "moved way past the movement threshold"
    }

    [Fact]
    public void testTwoFingersNeverClick()
    {
        var r = recognizer();
        r.taps([touch(1), touch(2, x: 0.7)], t: 0);
        Assert.Equal(0, r.taps([], t: 0.08));
    }

    [Fact]
    public void testSecondFingerLandingMidTouchPoisonsTheFirst()
    {
        var r = recognizer();
        r.taps([touch(1)], t: 0);
        r.taps([touch(1), touch(2, x: 0.8)], t: 0.03);
        r.taps([touch(1)], t: 0.05);
        Assert.Equal(0, r.taps([], t: 0.08));
    }

    [Fact]
    public void testGhostGrazeBelowMinimumSizeIsIgnored()
    {
        var r = recognizer();
        r.taps([touch(1, size: 0.02)], t: 0);
        Assert.Equal(0, r.taps([], t: 0.06));
    }

    // MARK: Physical-click interplay

    [Fact]
    public void testTouchDuringPhysicalClickNeverTaps()
    {
        var r = recognizer();
        r.PhysicalButton(isDown: true, t: 0);
        r.taps([touch(1)], t: 0.01);          // the finger doing the clicking
        r.PhysicalButton(isDown: false, t: 0.06);
        Assert.Equal(0, r.taps([], t: 0.09));
    }

    [Fact]
    public void testTapRightAfterPhysicalClickIsSuppressed()
    {
        var r = recognizer();
        r.PhysicalButton(isDown: true, t: 0);
        r.PhysicalButton(isDown: false, t: 0.1);
        r.taps([touch(1)], t: 0.15);          // repositioning finger, not a click
        Assert.Equal(0, r.taps([], t: 0.2));
        // Well after the cooldown: taps work again.
        r.taps([touch(2)], t: 0.6);
        Assert.Equal(1, r.taps([], t: 0.66));
    }

    [Fact]
    public void testButtonPressMidTouchPoisonsIt()
    {
        var r = recognizer();
        r.taps([touch(1)], t: 0);
        r.PhysicalButton(isDown: true, t: 0.02);     // the "tap" turned into a real click
        r.PhysicalButton(isDown: false, t: 0.05);
        Assert.Equal(0, r.taps([], t: 0.07));
    }

    // MARK: Scroll interplay

    [Fact]
    public void testTapDuringScrollingIsSuppressed()
    {
        var r = recognizer();
        r.taps([touch(1)], t: 0);
        r.NoteScroll(deltaMagnitude: 12, t: 0.02);   // real scrolling while the touch is alive
        Assert.Equal(0, r.taps([], t: 0.05));
    }

    [Fact]
    public void testTapRightAfterScrollingIsSuppressedButLaterTapWorks()
    {
        var r = recognizer(sensitivity: 0.5);         // scrollCooldown = 200 ms
        r.NoteScroll(deltaMagnitude: 20, t: 1.0);
        r.taps([touch(1)], t: 1.05);          // finger lands to stop the coast
        Assert.Equal(0, r.taps([], t: 1.12));
        r.taps([touch(2)], t: 1.5);
        Assert.Equal(1, r.taps([], t: 1.57));
    }

    [Fact]
    public void testMicroJitterScrollDoesNotVetoTheTap()
    {
        var r = recognizer();
        r.taps([touch(1)], t: 0);
        r.NoteScroll(deltaMagnitude: 1.5, t: 0.02);  // sub-threshold: the tap's own jiggle
        Assert.Equal(1, r.taps([], t: 0.06));
    }

    // MARK: Sensitivity

    [Fact]
    public void testFirmSettingRejectsWhatLightAccepts()
    {
        // 220 ms touch with a little wobble: fine on light, rejected on firm.
        var firm = recognizer(sensitivity: 0);
        firm.taps([touch(1, x: 0.50)], t: 0);
        firm.taps([touch(1, x: 0.52)], t: 0.1);
        Assert.Equal(0, firm.taps([], t: 0.22));

        var light = recognizer(sensitivity: 1);
        light.taps([touch(1, x: 0.50)], t: 0);
        light.taps([touch(1, x: 0.52)], t: 0.1);
        Assert.Equal(1, light.taps([], t: 0.22));
    }

    [Fact]
    public void testSensitivityTuningIsMonotonic()
    {
        var firm = new TapConfig(sensitivity: 0);
        var mid = new TapConfig(sensitivity: 0.5);
        var light = new TapConfig(sensitivity: 1);
        Assert.True(firm.MaxDuration < mid.MaxDuration);
        Assert.True(mid.MaxDuration < light.MaxDuration);
        Assert.True(firm.MaxMovement < light.MaxMovement);
        Assert.True(firm.ScrollCooldown > light.ScrollCooldown);
    }

    // extension TapRecognizerTests

    /// <summary>
    /// Replays a real tap captured from a Magic Mouse on macOS 26 (raw MultitouchSupport frames,
    /// 2026-08-20): 15 ms frame cadence, ~150 ms of contact, ~2.6% surface drift — and the tap's
    /// own drift emitting small scroll events the whole time. This exact trace produced zero
    /// clicks before the state-band + scroll-threshold fixes.
    /// </summary>
    [Fact]
    public void testRealCapturedTapFromMacOS26Clicks()
    {
        var c = new TapConfig(sensitivity: 0.5);
        c.Enabled = true;
        var r = new TapRecognizer(config: c);

        // (t, x, y, size) — touch id 7 from the capture, contact states only (2...5 band).
        (double, double, double, double)[] frames =
        [
            (111.032, 0.854, 0.671, 0.375),
            (111.047, 0.862, 0.674, 0.500),
            (111.062, 0.864, 0.679, 0.625),
            (111.077, 0.868, 0.681, 0.500),
            (111.092, 0.865, 0.685, 0.500),
            (111.107, 0.864, 0.689, 0.625),
            (111.122, 0.866, 0.690, 0.875),
            (111.137, 0.868, 0.692, 0.875), // state 3
            (111.152, 0.867, 0.693, 0.750), // state 4
            (111.167, 0.864, 0.692, 0.500), // state 5
        ];
        long taps = 0;
        foreach ((double t, double x, double y, double size) in frames)
        {
            // The drift scrolls the page a little on every frame — sub-threshold deltas.
            r.NoteScroll(deltaMagnitude: 4, t: t);
            taps += r.taps([new TouchSample(id: 7, x: x, y: y, size: size)], t: t);
        }
        // States 6/7 are lingering/gone: the touch vanishes from contact frames.
        taps += r.taps([], t: 111.182);
        Assert.Equal(1, taps);   // "the captured tap must produce exactly one click"
    }

    /// <summary>The same capture's firm press-and-hold (17+ frames of sustained contact) must not click.</summary>
    [Fact]
    public void testRealCapturedPressAndHoldDoesNotClick()
    {
        var c = new TapConfig(sensitivity: 1);   // even at the lightest setting
        c.Enabled = true;
        var r = new TapRecognizer(config: c);
        var t = 111.707;
        var taps = r.taps([new TouchSample(id: 3, x: 0.37, y: 0.76, size: 0.5)], t: t);
        for (int i = 0; i < 28; i++)          // ≈ 420 ms of contact
        {
            t += 0.015;
            taps += r.taps([new TouchSample(id: 3, x: 0.336, y: 0.775, size: 1.0)], t: t);
        }
        taps += r.taps([], t: t + 0.015);
        Assert.Equal(0, taps);
    }
}

// MARK: - Tap and drag

public sealed class TapAndDragTests
{
    private static TapRecognizer recognizer()
    {
        var c = new TapConfig(sensitivity: 0.5);
        c.Enabled = true;
        c.TapAndDrag = true;
        c.DragWindow = 0.5;
        return new TapRecognizer(config: c);
    }

    private static TouchSample touch(long id, double x = 0.5, double y = 0.5, double size = 0.6) =>
        new(id: id, x: x, y: y, size: size);

    /// <summary>Complete one clean tap ending at time `t`.</summary>
    private static void tap(TapRecognizer r, long id, double t)
    {
        Assert.Equal([], r.HandleFrame([touch(id)], t: t - 0.08));
        Assert.Equal([TapEvent.Tap], r.HandleFrame([], t: t));
    }

    [Fact]
    public void testTapThenTouchAndHoldDrags()
    {
        var r = recognizer();
        tap(r, id: 1, t: 1.0);
        // Second touch lands within the window: drag begins immediately.
        Assert.Equal([TapEvent.DragBegan], r.HandleFrame([touch(2)], t: 1.2));
        Assert.True(r.DragActive);
        // It can live arbitrarily long, and the planted finger may jiggle a little while the
        // mouse moves under it. (Wandering FAR is a scroll swipe and cancels — tested separately.)
        Assert.Equal([], r.HandleFrame([touch(2, x: 0.55, y: 0.46)], t: 3.0));
        // Lift = drop. Never evaluated as a tap, however short or long it was.
        Assert.Equal([TapEvent.DragEnded], r.HandleFrame([], t: 4.0));
        Assert.False(r.DragActive);
    }

    [Fact]
    public void testQuickSecondTapStillClicksThroughDragPath()
    {
        // tap-tap fast: the second touch becomes down...up, which the controller posts with
        // escalated click state — that's what makes it a double-click.
        var r = recognizer();
        tap(r, id: 1, t: 1.0);
        Assert.Equal([TapEvent.DragBegan], r.HandleFrame([touch(2)], t: 1.1));
        Assert.Equal([TapEvent.DragEnded], r.HandleFrame([], t: 1.18));
    }

    [Fact]
    public void testLateSecondTouchIsJustATap()
    {
        var r = recognizer();
        tap(r, id: 1, t: 1.0);
        Assert.Equal([], r.HandleFrame([touch(2)], t: 1.8));   // "outside the drag window"
        Assert.Equal([TapEvent.Tap], r.HandleFrame([], t: 1.88));
    }

    [Fact]
    public void testFarawaySecondTouchIsJustATap()
    {
        var r = recognizer();
        tap(r, id: 1, t: 1.0);
        Assert.Equal([], r.HandleFrame([touch(2, x: 0.05, y: 0.95)], t: 1.1));
        // "other end of the surface — not a drag follow-up"
        Assert.Equal([TapEvent.Tap], r.HandleFrame([], t: 1.18));
    }

    [Fact]
    public void testDisabledSettingNeverDrags()
    {
        var r = recognizer();
        r.Config.TapAndDrag = false;
        tap(r, id: 1, t: 1.0);
        Assert.Equal([], r.HandleFrame([touch(2)], t: 1.1));
        Assert.Equal([TapEvent.Tap], r.HandleFrame([], t: 1.18));   // "still a plain tap"
    }

    [Fact]
    public void testPhysicalClickCancelsTheDrag()
    {
        var r = recognizer();
        tap(r, id: 1, t: 1.0);
        Assert.Equal([TapEvent.DragBegan], r.HandleFrame([touch(2)], t: 1.2));
        Assert.Equal([TapEvent.DragCancelled], r.PhysicalButton(isDown: true, t: 1.5));
        // "release the synthetic button (if pressed) before the real press — never a click"
        Assert.False(r.DragActive);
        r.PhysicalButton(isDown: false, t: 1.6);
        Assert.Equal([], r.HandleFrame([], t: 1.7));   // "lift of the ex-drag finger is inert"
    }

    [Fact]
    public void testSecondFingerDuringDragDoesNotDropIt()
    {
        var r = recognizer();
        tap(r, id: 1, t: 1.0);
        Assert.Equal([TapEvent.DragBegan], r.HandleFrame([touch(2)], t: 1.2));
        Assert.Equal([], r.HandleFrame([touch(2), touch(3, x: 0.8)], t: 1.5));
        Assert.True(r.DragActive);
        // The bystander finger lifts: nothing. The drag finger lifts: drop.
        Assert.Equal([], r.HandleFrame([touch(2)], t: 1.9));
        Assert.Equal([TapEvent.DragEnded], r.HandleFrame([], t: 2.2));
    }

    [Fact]
    public void testScrollJiggleDuringDragDoesNotDropIt()
    {
        var r = recognizer();
        tap(r, id: 1, t: 1.0);
        Assert.Equal([TapEvent.DragBegan], r.HandleFrame([touch(2)], t: 1.2));
        r.NoteScroll(deltaMagnitude: 25, t: 1.5);  // shell jiggle while the mouse moves
        Assert.Equal([], r.HandleFrame([touch(2)], t: 1.6));
        Assert.Equal([TapEvent.DragEnded], r.HandleFrame([], t: 2.0));
    }

    [Fact]
    public void testDisablingMidDragReleasesTheButton()
    {
        var r = recognizer();
        tap(r, id: 1, t: 1.0);
        Assert.Equal([TapEvent.DragBegan], r.HandleFrame([touch(2)], t: 1.2));
        r.Config.Enabled = false;
        Assert.Equal([TapEvent.DragCancelled], r.HandleFrame([touch(2)], t: 1.3));
        // "a held synthetic button must never leak past a disable"
    }
}

// MARK: - Tap zone

public sealed class TapZoneTests
{
    private static TapRecognizer recognizer(bool frontIsHighY = true)
    {
        var c = new TapConfig(sensitivity: 0.5);
        c.Enabled = true;
        c.TapZoneEnabled = true;
        c.TapZoneDepth = 0.5;
        c.TapZoneEdgeMargin = 0.12;
        c.FrontIsHighY = frontIsHighY;
        return new TapRecognizer(config: c);
    }

    private static long tapResult(TapRecognizer r, double x, double y)
    {
        long taps = r.HandleFrame([new TouchSample(id: 9, x: x, y: y, size: 0.6)], t: 100)
            .LongCount(e => e == TapEvent.Tap);
        return taps + r.HandleFrame([], t: 100.08).LongCount(e => e == TapEvent.Tap);
    }

    [Fact]
    public void testFrontCenterTaps()
    {
        Assert.Equal(1, tapResult(recognizer(), x: 0.5, y: 0.9));
    }

    [Fact]
    public void testBackHalfDoesNotTap()
    {
        Assert.Equal(0, tapResult(recognizer(), x: 0.5, y: 0.2));
    }

    [Fact]
    public void testSideEdgesDoNotTap()
    {
        Assert.Equal(0, tapResult(recognizer(), x: 0.05, y: 0.9));   // "left edge (thumb territory)"
        Assert.Equal(0, tapResult(recognizer(), x: 0.95, y: 0.9));   // "right edge (ring finger)"
    }

    [Fact]
    public void testOrientationFlipMirrorsTheZone()
    {
        Assert.Equal(1, tapResult(recognizer(frontIsHighY: false), x: 0.5, y: 0.1));
        Assert.Equal(0, tapResult(recognizer(frontIsHighY: false), x: 0.5, y: 0.9));
    }

    [Fact]
    public void testZoneOffMeansWholeSurface()
    {
        var r = recognizer();
        r.Config.TapZoneEnabled = false;
        Assert.Equal(1, tapResult(r, x: 0.05, y: 0.1));
    }

    [Fact]
    public void testDepthSliderWidensTheZone()
    {
        var r = recognizer();
        r.Config.TapZoneDepth = 0.75;
        Assert.Equal(1, tapResult(r, x: 0.5, y: 0.3));   // "y=0.3 is 0.7 from the front — inside 0.75"
    }

    [Fact]
    public void testZoneDoesNotBlockDragFollowUp()
    {
        // The drag follow-up touch may land outside the zone; only the TAP is zone-gated.
        var r = recognizer();
        r.Config.TapAndDrag = true;
        r.Config.DragWindow = 0.5;
        r.Config.DragMaxDistance = 0.5;
        Assert.Equal([], r.HandleFrame([new TouchSample(id: 1, x: 0.5, y: 0.9, size: 0.6)], t: 1.0));
        Assert.Equal([TapEvent.Tap], r.HandleFrame([], t: 1.08));
        Assert.Equal([TapEvent.DragBegan], r.HandleFrame([new TouchSample(id: 2, x: 0.5, y: 0.55, size: 0.6)], t: 1.2));
        Assert.Equal([TapEvent.DragEnded], r.HandleFrame([], t: 2.0));
    }
}

// MARK: - Drag swipe-cancel (the "tap-then-rest locks scrolling" fix)

public sealed class DragSwipeCancelTests
{
    private static TapRecognizer recognizer()
    {
        var c = new TapConfig(sensitivity: 0.5);
        c.Enabled = true;
        c.TapAndDrag = true;
        c.DragWindow = 0.5;
        c.DragSwipeCancelDistance = 0.12;
        return new TapRecognizer(config: c);
    }

    private static TouchSample touch(long id, double x = 0.5, double y = 0.5) =>
        new(id: id, x: x, y: y, size: 0.6);

    private static void tapThenRest(TapRecognizer r)
    {
        Assert.Equal([], r.HandleFrame([touch(1)], t: 1.0));
        Assert.Equal([TapEvent.Tap], r.HandleFrame([], t: 1.08));
        Assert.Equal([TapEvent.DragBegan], r.HandleFrame([touch(2)], t: 1.2));   // "resting finger arms a drag"
    }

    [Fact]
    public void testSwipingTheRestingFingerCancelsTheDragImmediately()
    {
        var r = recognizer();
        tapThenRest(r);
        // The user swipes to scroll: the finger travels across the surface.
        Assert.Equal([], r.HandleFrame([touch(2, x: 0.5, y: 0.42)], t: 1.4));
        Assert.Equal([TapEvent.DragSwipeCancelled], r.HandleFrame([touch(2, x: 0.5, y: 0.35)], t: 1.45));
        Assert.False(r.DragActive);
        // The rest of the swipe is inert for tapping — and its lift is not a tap.
        Assert.Equal([], r.HandleFrame([touch(2, x: 0.5, y: 0.2)], t: 1.5));
        Assert.Equal([], r.HandleFrame([], t: 1.6));
    }

    [Fact]
    public void testPlantedFingerJiggleDoesNotCancelTheDrag()
    {
        var r = recognizer();
        tapThenRest(r);
        // Dragging = moving the mouse; the planted finger only wobbles a few percent.
        for (int i = 0; i < 40; i++)
        {
            var wobble = (double)(i % 2) * 0.03;
            Assert.Equal([], r.HandleFrame([touch(2, x: 0.5 + wobble, y: 0.5 - wobble)],
                                           t: 1.3 + (double)i * 0.015));
        }
        Assert.True(r.DragActive, "a real drag survives shell jiggle");
        Assert.Equal([TapEvent.DragEnded], r.HandleFrame([], t: 2.0));
    }

    [Fact]
    public void testZoneGatedDragEntry()
    {
        var c = new TapConfig(sensitivity: 0.5);
        c.Enabled = true;
        c.TapAndDrag = true;
        c.DragWindow = 0.5;
        c.TapZoneEnabled = true;
        c.TapZoneDepth = 0.5;
        c.FrontIsHighY = true;
        var r = new TapRecognizer(config: c);
        // Tap in the front zone...
        Assert.Equal([], r.HandleFrame([touch(1, y: 0.9)], t: 1.0));
        Assert.Equal([TapEvent.Tap], r.HandleFrame([], t: 1.08));
        // ...then the finger comes back to REST behind the zone: must NOT arm a drag.
        Assert.Equal([], r.HandleFrame([touch(2, y: 0.4)], t: 1.2));
        Assert.False(r.DragActive);
        // Its scroll swipe is just a swipe.
        Assert.Equal([], r.HandleFrame([touch(2, y: 0.2)], t: 1.4));
        Assert.Equal([], r.HandleFrame([], t: 1.5));
    }
}

// MARK: - Deferred-press drag mechanics

public sealed class DeferredDragTests
{
    private static TapRecognizer recognizer()
    {
        var c = new TapConfig(sensitivity: 0.5);
        c.Enabled = true;
        c.TapAndDrag = true;
        c.DragWindow = 0.5;
        c.DragSwipeCancelDistance = 0.12;
        return new TapRecognizer(config: c);
    }

    private static TouchSample touch(long id, double x = 0.5, double y = 0.5) =>
        new(id: id, x: x, y: y, size: 0.6);

    private static void armDrag(TapRecognizer r)
    {
        Assert.Equal([], r.HandleFrame([touch(1)], t: 1.0));
        Assert.Equal([TapEvent.Tap], r.HandleFrame([], t: 1.08));
        Assert.Equal([TapEvent.DragBegan], r.HandleFrame([touch(2)], t: 1.2));
    }

    [Fact]
    public void testShellDriftWhileCursorMovesNeverCancels()
    {
        var r = recognizer();
        armDrag(r);
        // A long window drag: the mouse moves and the planted finger drifts a LOT cumulatively —
        // far past the swipe threshold — but each frame re-anchors while the cursor moves.
        var y = 0.5;
        for (int i = 0; i < 60; i++)
        {
            y -= 0.01;   // 0.6 of the surface over the whole drag
            Assert.Equal([], r.HandleFrame([touch(2, y: y)], t: 1.3 + (double)i * 0.015,
                                           cursorMoving: true));
        }
        Assert.True(r.DragActive, "re-anchoring must absorb drift during real mouse motion");
        Assert.Equal([TapEvent.DragEnded], r.HandleFrame([], t: 2.5));
    }

    [Fact]
    public void testSurfaceSweepWithCursorStillCancels()
    {
        var r = recognizer();
        armDrag(r);
        Assert.Equal([], r.HandleFrame([touch(2, y: 0.42)], t: 1.4, cursorMoving: false));
        Assert.Equal([TapEvent.DragSwipeCancelled],
                     r.HandleFrame([touch(2, y: 0.34)], t: 1.45, cursorMoving: false));
    }

    [Fact]
    public void testDriftAnchorResetsWhenCursorStops()
    {
        var r = recognizer();
        armDrag(r);
        // Drift far while dragging (cursor moving) — anchor follows.
        for (int i = 0; i < 20; i++)
        {
            r.HandleFrame([touch(2, y: 0.5 - (double)i * 0.01)], t: 1.3 + (double)i * 0.015,
                          cursorMoving: true);
        }
        // Cursor stops at y≈0.31; a small settle must NOT cancel...
        Assert.Equal([], r.HandleFrame([touch(2, y: 0.29)], t: 1.7, cursorMoving: false));
        // ...but a real sweep from the new anchor still does.
        Assert.Equal([TapEvent.DragSwipeCancelled],
                     r.HandleFrame([touch(2, y: 0.15)], t: 1.75, cursorMoving: false));
    }

    [Fact]
    public void testDisarmIsCompletelySilent()
    {
        var r = recognizer();
        armDrag(r);
        r.DisarmDrag();
        Assert.False(r.DragActive);
        // The ex-drag finger can sit, swipe, lift — nothing comes out.
        Assert.Equal([], r.HandleFrame([touch(2, y: 0.3)], t: 1.5));
        Assert.Equal([], r.HandleFrame([], t: 2.0));
    }

    [Fact]
    public void testDisarmedTouchCannotRearmItself()
    {
        var r = recognizer();
        armDrag(r);
        r.DisarmDrag();
        // Still within the drag window of the original tap — but this touch already lost its
        // drag status and must not win it back on later frames.
        Assert.Equal([], r.HandleFrame([touch(2)], t: 1.3));
        Assert.False(r.DragActive);
    }
}

// MARK: - Two-finger drag (long press)

public sealed class TwoFingerDragTests
{
    private static TapRecognizer recognizer(bool zone = false)
    {
        var c = new TapConfig(sensitivity: 0.5);
        c.Enabled = true;
        c.TwoFingerDrag = true;
        c.TwoFingerPairWindow = 0.15;
        c.TwoFingerLongPress = 0.35;
        if (zone)
        {
            c.TapZoneEnabled = true;
            c.TapZoneDepth = 0.5;
            c.FrontIsHighY = true;
        }
        return new TapRecognizer(config: c);
    }

    private static TouchSample touch(long id, double x = 0.5, double y = 0.75) =>
        new(id: id, x: x, y: y, size: 0.6);

    private static TouchSample[] pair(double y = 0.75) =>
        [touch(1, x: 0.42, y: y), touch(2, x: 0.58, y: y)];

    [Fact]
    public void testPairArmsThenLongPressPressesThenLiftDrops()
    {
        var r = recognizer();
        Assert.Equal([TapEvent.DragBegan], r.HandleFrame(pair(), t: 1.0));
        Assert.True(r.DragArmIsPair);
        // Holding still: nothing until the long-press clock fires.
        Assert.Equal([], r.HandleFrame(pair(), t: 1.2));
        Assert.Equal([TapEvent.DragPressed], r.HandleFrame(pair(), t: 1.4));
        // Held drag survives arbitrarily long.
        Assert.Equal([], r.HandleFrame(pair(), t: 3.0));
        // Lifting (either finger — here both) drops. Never a click.
        Assert.Equal([TapEvent.DragCancelled], r.HandleFrame([], t: 3.5));
        Assert.False(r.DragActive);
    }

    [Fact]
    public void testQuickTwoFingerTapIsCompletelySilent()
    {
        var r = recognizer();
        Assert.Equal([TapEvent.DragBegan], r.HandleFrame(pair(), t: 1.0));
        // Lifted before the long press: no click, no press, nothing.
        Assert.Equal([TapEvent.DragCancelled], r.HandleFrame([], t: 1.15));
    }

    [Fact]
    public void testStaggeredFingersNeverArm()
    {
        var r = recognizer();
        Assert.Equal([], r.HandleFrame([touch(1)], t: 1.0));
        // Second finger arrives 0.3 s later — that's a grip, not a gesture.
        Assert.Equal([], r.HandleFrame([touch(1), touch(2, x: 0.7)], t: 1.3));
        Assert.False(r.DragActive);
        Assert.Equal([], r.HandleFrame([], t: 2.0));   // "poisoned rest, no taps either"
    }

    [Fact]
    public void testOneFingerLiftingEndsThePairDrag()
    {
        var r = recognizer();
        Assert.Equal([TapEvent.DragBegan], r.HandleFrame(pair(), t: 1.0));
        Assert.Equal([TapEvent.DragPressed], r.HandleFrame(pair(), t: 1.4));
        Assert.Equal([TapEvent.DragCancelled], r.HandleFrame([touch(1, x: 0.42)], t: 2.0));
        Assert.False(r.DragActive);
        Assert.Equal([], r.HandleFrame([], t: 2.5));   // "the survivor's lift is inert"
    }

    [Fact]
    public void testPairSwipeWhileCursorStillCancelsSilently()
    {
        var r = recognizer();
        Assert.Equal([TapEvent.DragBegan], r.HandleFrame(pair(), t: 1.0));
        // Both fingers sweep down (a scroll motion) before the press.
        Assert.Equal([TapEvent.DragSwipeCancelled],
                     r.HandleFrame([touch(1, x: 0.42, y: 0.55), touch(2, x: 0.58, y: 0.55)], t: 1.1));
        Assert.False(r.DragActive);
    }

    [Fact]
    public void testDragDriftWithCursorMovingSurvivesForPairs()
    {
        var r = recognizer();
        Assert.Equal([TapEvent.DragBegan], r.HandleFrame(pair(), t: 1.0));
        Assert.Equal([TapEvent.DragPressed], r.HandleFrame(pair(), t: 1.4));
        var y = 0.75;
        for (int i = 0; i < 40; i++)
        {
            y -= 0.01;
            Assert.Equal([], r.HandleFrame([touch(1, x: 0.42, y: y), touch(2, x: 0.58, y: y)],
                                           t: 1.5 + (double)i * 0.015, cursorMoving: true));
        }
        Assert.True(r.DragActive);
        Assert.Equal([TapEvent.DragCancelled], r.HandleFrame([], t: 2.5));
    }

    [Fact]
    public void testSettingOffMeansPoisonAsBefore()
    {
        var r = recognizer();
        r.Config.TwoFingerDrag = false;
        Assert.Equal([], r.HandleFrame(pair(), t: 1.0));
        Assert.Equal([], r.HandleFrame([], t: 1.1));   // "two fingers stay a poisoned non-gesture"
    }

    [Fact]
    public void testZoneGatesThePair()
    {
        var r = recognizer(zone: true);
        Assert.Equal([], r.HandleFrame(pair(0.3), t: 1.0));   // "behind the front zone: no arm"
        Assert.False(r.DragActive);
        Assert.Equal([], r.HandleFrame([], t: 1.1));
        Assert.Equal([TapEvent.DragBegan], r.HandleFrame(pair(0.8), t: 2.0));   // "in the zone: arms"
    }

    [Fact]
    public void testPhysicalClickCancelsPairDrag()
    {
        var r = recognizer();
        Assert.Equal([TapEvent.DragBegan], r.HandleFrame(pair(), t: 1.0));
        Assert.Equal([TapEvent.DragPressed], r.HandleFrame(pair(), t: 1.4));
        Assert.Equal([TapEvent.DragCancelled], r.PhysicalButton(isDown: true, t: 1.6));
        Assert.False(r.DragActive);
    }
}

// MARK: - Scroll-burst drag cancellation (drags must never trap a scrolling finger)

public sealed class ScrollBurstCancelTests
{
    private static TapRecognizer recognizer(bool twoFinger = false)
    {
        var c = new TapConfig(sensitivity: 0.5);
        c.Enabled = true;
        c.TapAndDrag = true;
        c.TwoFingerDrag = twoFinger;
        c.DragWindow = 0.5;
        c.ScrollBurstWindow = 0.3;
        c.ScrollBurstCancelThreshold = 80;
        return new TapRecognizer(config: c);
    }

    private static TouchSample touch(long id, double x = 0.5, double y = 0.5) =>
        new(id: id, x: x, y: y, size: 0.6);

    private static void armSingle(TapRecognizer r)
    {
        Assert.Equal([], r.HandleFrame([touch(1)], t: 1.0));
        Assert.Equal([TapEvent.Tap], r.HandleFrame([], t: 1.08));
        Assert.Equal([TapEvent.DragBegan], r.HandleFrame([touch(2)], t: 1.2));
    }

    /// <summary>
    /// The exact reported failure: tap, rest, then scroll WHILE the mouse is moving. Surface
    /// travel re-anchors (cursorMoving) so it can't cancel — the scroll burst must.
    /// </summary>
    [Fact]
    public void testScrollBurstCancelsArmedDragEvenWhileCursorMoves()
    {
        var r = recognizer();
        armSingle(r);
        r.HandleFrame([touch(2, y: 0.45)], t: 1.30, cursorMoving: true);
        var events = new List<TapEvent>();
        for (int i = 0; i < 5; i++)
        {
            events.AddRange(r.NoteScroll(deltaMagnitude: 20, t: 1.32 + (double)i * 0.02));
        }
        Assert.Equal([TapEvent.DragSwipeCancelled], events);   // "100 pt of scrolling inside the window"
        Assert.False(r.DragActive);
    }

    [Fact]
    public void testScrollBurstReleasesPressedTwoFingerDrag()
    {
        var r = recognizer(twoFinger: true);
        Assert.Equal([TapEvent.DragBegan], r.HandleFrame([touch(1, x: 0.42), touch(2, x: 0.58)], t: 1.0));
        Assert.Equal([TapEvent.DragPressed], r.HandleFrame([touch(1, x: 0.42), touch(2, x: 0.58)], t: 1.4));
        var events = new List<TapEvent>();
        for (int i = 0; i < 5; i++)
        {
            events.AddRange(r.NoteScroll(deltaMagnitude: 25, t: 1.5 + (double)i * 0.02));
        }
        Assert.Equal([TapEvent.DragSwipeCancelled], events);
        Assert.False(r.DragActive);
    }

    [Fact]
    public void testOccasionalJiggleSpikesDoNotCancelARealDrag()
    {
        var r = recognizer();
        armSingle(r);
        // A vigorous but genuine drag: isolated strong spikes, spread out past the window.
        Assert.Equal([], r.NoteScroll(deltaMagnitude: 25, t: 1.5));
        Assert.Equal([], r.NoteScroll(deltaMagnitude: 25, t: 1.9));
        Assert.Equal([], r.NoteScroll(deltaMagnitude: 25, t: 2.3));
        Assert.True(r.DragActive, "sub-burst jiggle must never drop a drag");
    }

    [Fact]
    public void testBurstWithoutAnyDragIsInert()
    {
        var r = recognizer();
        for (int i = 0; i < 6; i++)
        {
            Assert.Equal([], r.NoteScroll(deltaMagnitude: 30, t: 1.0 + (double)i * 0.02));
        }
    }

    [Fact]
    public void testBurstBeforeLongPressPreventsThePress()
    {
        var r = recognizer(twoFinger: true);
        Assert.Equal([TapEvent.DragBegan], r.HandleFrame([touch(1, x: 0.42), touch(2, x: 0.58)], t: 1.0));
        var events = new List<TapEvent>();
        for (int i = 0; i < 5; i++)
        {
            events.AddRange(r.NoteScroll(deltaMagnitude: 20, t: 1.05 + (double)i * 0.02));
        }
        Assert.Equal([TapEvent.DragSwipeCancelled], events);
        // The long-press clock must not fire afterwards.
        Assert.Equal([], r.HandleFrame([touch(1, x: 0.42), touch(2, x: 0.58)], t: 1.5));
        Assert.False(r.DragActive);
    }
}

// MARK: - Post-drag traps (the "after dragging, scroll is dead" family)

public sealed class PostDragTrapTests
{
    private static TapRecognizer recognizer()
    {
        var c = new TapConfig(sensitivity: 0.5);
        c.Enabled = true;
        c.TwoFingerDrag = true;
        c.TwoFingerLongPress = 0.35;
        c.PressQuietPeriod = 0.25;
        c.PairArmTTL = 1.5;
        c.PairRearmCooldown = 0.4;
        return new TapRecognizer(config: c);
    }

    private static TouchSample[] pair(double y = 0.75) =>
        [new TouchSample(id: 1, x: 0.42, y: y, size: 0.6), new TouchSample(id: 2, x: 0.58, y: y, size: 0.6)];

    private static TouchSample[] pair2(double y = 0.75) =>
        [new TouchSample(id: 7, x: 0.42, y: y, size: 0.6), new TouchSample(id: 8, x: 0.58, y: y, size: 0.6)];

    [Fact]
    public void testHandSettlingAfterAPairDragCannotRearmImmediately()
    {
        var r = recognizer();
        Assert.Equal([TapEvent.DragBegan], r.HandleFrame(pair(), t: 1.0));
        Assert.Equal([TapEvent.DragPressed], r.HandleFrame(pair(), t: 1.4));
        Assert.Equal([TapEvent.DragCancelled], r.HandleFrame([], t: 2.0));
        // The hand settles back 150 ms later — two fresh contacts landing together, exactly
        // like a deliberate arm. The cooldown must reject it.
        Assert.Equal([], r.HandleFrame(pair2(), t: 2.15));
        Assert.False(r.DragActive);
        // A deliberate pair after the cooldown arms normally.
        Assert.Equal([], r.HandleFrame([], t: 2.3));
        Assert.Equal([TapEvent.DragBegan], r.HandleFrame(pair(), t: 2.6));
    }

    [Fact]
    public void testSlowScrollingDefersTheLongPress()
    {
        var r = recognizer();
        Assert.Equal([TapEvent.DragBegan], r.HandleFrame(pair(), t: 1.0));
        // Gentle reading-scroll: 4 pt events — below the "strong" bar, so no burst cancel,
        // but the press must not fire into the middle of it.
        Assert.Equal([], r.NoteScroll(deltaMagnitude: 4, t: 1.30));
        Assert.Equal([], r.HandleFrame(pair(), t: 1.40));   // "long press due, but scrolling is live"
        Assert.Equal([], r.NoteScroll(deltaMagnitude: 4, t: 1.45));
        Assert.Equal([], r.HandleFrame(pair(), t: 1.55));   // "still scrolling"
        // Scrolling stops; after the quiet period the press fires.
        Assert.Equal([TapEvent.DragPressed], r.HandleFrame(pair(), t: 1.75));
    }

    [Fact]
    public void testStaleArmDisarmsInsteadOfPressingBySurprise()
    {
        var r = recognizer();
        Assert.Equal([TapEvent.DragBegan], r.HandleFrame(pair(), t: 1.0));
        // Continuous slow scrolling keeps deferring the press past the TTL...
        var t = 1.0;
        while (t < 2.6)
        {
            t += 0.1;
            r.NoteScroll(deltaMagnitude: 4, t: t);
            Assert.Equal([], r.HandleFrame(pair(), t: t));   // "no press may ever fire"
        }
        Assert.False(r.DragActive, "the arm expired silently");
        // And the fingers lifting afterwards emits nothing.
        Assert.Equal([], r.HandleFrame([], t: 3.0));
    }
}

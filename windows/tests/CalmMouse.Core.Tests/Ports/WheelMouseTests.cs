// Translated from Tests/CalmMouseCoreTests/WheelMouseTests.swift. XCTAssertEqual(actual,
// expected) is Assert.Equal(expected, actual); xUnit's Assert.Equal and Assert.Null take no
// message, so the Swift messages on those asserts are kept as comments. XCTAssertEqual(a, b,
// accuracy:) is Assert.Equal(b, a, tolerance), with the tolerance written as a double.
// XCTAssertGreaterThan / LessThan are Assert.True on the comparison. `reduce(0) { $0 + $1.y }` is
// Aggregate, so the sums add in the same order. `WheelSmoother.Step` is `WheelSmoother.StepResult`
// (see Ports/WheelSmoother.cs), and Swift's Int is long. JSONEncoder / JSONDecoder are
// System.Text.Json with the converters in Ports/Config.cs.

using System.Text.Json;

namespace CalmMouse.Core.Tests;

/// <summary>Wheel mice: which events count as one, and the two settings a wheel answers to.</summary>
public sealed class WheelMouseTests
{
    /// <summary>
    /// A wheel tick as captured from a real Bluetooth wheel mouse on macOS 26:
    /// not continuous, no phase, no momentum.
    /// </summary>
    private static ScrollEvent tick(double t, double dx = 0, double dy = -10,
                                    ModifierCombo mods = ModifierCombo.None, WheelSettings? wheel = null) =>
        new(timestamp: t, fromMagicMouse: false, isContinuous: false,
            deltaX: dx, deltaY: dy, modifiers: mods, fromWheelMouse: true, wheel: wheel);

    private static ScrollEvent swipe(double t, ScrollPhase phase, double dy = 3) =>
        new(timestamp: t, fromMagicMouse: true, isContinuous: true, phase: phase, deltaY: dy);

    private readonly long wheel = ScrollBlocker.WheelButton;
    private readonly ScrollDecision inverted = new ScrollDecision.Rewrite(new ScrollRewrite(phase: ScrollPhase.None, momentum: MomentumPhase.None, invert: true));

    // MARK: Off by default

    [Fact]
    public void testWheelIsLeftAloneUntilAWheelSettingIsTurnedOn()
    {
        var b = new ScrollBlocker();
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(tick(0)));
        // Even with the wheel pressed: blocking is opt-in for wheel mice.
        b.WheelMouseButton(wheel, isDown: true, t: 0.1);
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(tick(0.2)));
    }

    [Fact]
    public void testMagicMouseSettingsNeverReachTheWheel()
    {
        var c = new BlockerConfig();
        c.DisableScrollEntirely = true;
        c.BlockHorizontalScroll = true;
        c.DeadZone = 40;
        c.AxisLock = true;
        c.MomentumEnabled = false;
        c.ModifierActions = new Dictionary<ModifierCombo, ScrollAction>
        {
            [ModifierCombo.Option] = ScrollAction.ZoomCommand,
            [ModifierCombo.Shift] = ScrollAction.Block,
        };
        var b = new ScrollBlocker(config: c);
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(tick(0, dy: 1)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(tick(0.1, dx: 5, dy: 0)));   // "sideways wheel scrolling is always deliberate"
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(tick(0.2, mods: ModifierCombo.Option)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(tick(0.3, mods: ModifierCombo.Shift)));
    }

    [Fact]
    public void testPerAppMagicMouseRuleDoesNotSilenceTheWheel()
    {
        // "Ignore Magic Mouse scrolling in Figma" must leave a wheel mouse zooming in Figma.
        var rule = new AppRule(bundleID: "com.figma.Desktop", name: "Figma", disableScrollEntirely: true);
        var b = new ScrollBlocker(rules: new RuleSet(appRules: [rule]));
        b.SetActiveApp(bundleID: "com.figma.Desktop");
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(swipe(0, ScrollPhase.Began)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(tick(0.1)));
    }

    // MARK: Direction

    [Fact]
    public void testReverseWheelDirectionInvertsEveryTick()
    {
        var c = new BlockerConfig(); c.ReverseWheelScroll = true;
        var b = new ScrollBlocker(config: c);
        Assert.Equal(inverted, b.HandleScroll(tick(0, dy: -10)));
        Assert.Equal(inverted, b.HandleScroll(tick(0.1, dx: 4, dy: 0)));
    }

    [Fact]
    public void testReverseWheelDirectionLeavesTheMagicMouseAlone()
    {
        var c = new BlockerConfig(); c.ReverseWheelScroll = true;
        var b = new ScrollBlocker(config: c);
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(swipe(0, ScrollPhase.Began)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(swipe(0.01, ScrollPhase.Changed)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(swipe(0.02, ScrollPhase.Ended, dy: 0)));
    }

    // MARK: Pressing the wheel

    [Fact]
    public void testScrollIsSwallowedWhileTheWheelIsPressedAndBrieflyAfter()
    {
        var c = new BlockerConfig(); c.BlockScrollWhileWheelPressed = true;
        var b = new ScrollBlocker(config: c);
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(tick(0)));
        b.WheelMouseButton(wheel, isDown: true, t: 1.0);
        Assert.True(b.AnyButtonDown);
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(tick(1.05)));
        b.WheelMouseButton(wheel, isDown: false, t: 1.2);
        Assert.False(b.AnyButtonDown);
        // The wheel often turns one more notch as the finger lets go.
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(tick(1.2 + ScrollBlocker.WheelReleaseGrace - 0.01)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(tick(1.2 + ScrollBlocker.WheelReleaseGrace + 0.01)));
    }

    [Fact]
    public void testOnlyTheWheelButtonBlocks()
    {
        // Holding the left button and turning the wheel is deliberate: scrolling while
        // dragging a file, or extending a selection.
        var c = new BlockerConfig(); c.BlockScrollWhileWheelPressed = true;
        var b = new ScrollBlocker(config: c);
        foreach (long button in new long[] { 0, 1, 3, 4 })
        {
            b.WheelMouseButton(button, isDown: true, t: 0);
            Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(tick(0.1)));    // "button \(button)"
            b.WheelMouseButton(button, isDown: false, t: 0.2);
            Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(tick(0.21)));   // "button \(button) released"
        }
    }

    [Fact]
    public void testTheTwoMiceDoNotBlockEachOther()
    {
        var c = new BlockerConfig(); c.BlockScrollWhileWheelPressed = true;
        var b = new ScrollBlocker(config: c);
        // A Magic Mouse click does not stop the wheel…
        b.MagicMouseButton(0, isDown: true, t: 0);
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(tick(0.1)));
        b.MagicMouseButton(0, isDown: false, t: 0.2);
        // …and a pressed wheel does not stop a Magic Mouse swipe.
        b.WheelMouseButton(wheel, isDown: true, t: 5);
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(swipe(5.1, ScrollPhase.Began)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(swipe(5.11, ScrollPhase.Changed)));
    }

    [Fact]
    public void testWheelTicksDoNotDisturbAMagicMouseGesture()
    {
        // Dead zone state belongs to the swipe; a wheel tick in the middle must not reset it.
        var c = new BlockerConfig(); c.DeadZone = 10;
        var b = new ScrollBlocker(config: c);
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(swipe(0, ScrollPhase.Began, dy: 4)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(tick(0.01)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(swipe(0.02, ScrollPhase.Changed, dy: 4)));          // 8 < 10
        Assert.Equal(new ScrollDecision.Rewrite(new ScrollRewrite(phase: ScrollPhase.Began, momentum: MomentumPhase.None)),
                     b.HandleScroll(swipe(0.03, ScrollPhase.Changed, dy: 4)));
    }

    [Fact]
    public void testMissedWheelReleaseIsRecoveredByTheSafetyValve()
    {
        var c = new BlockerConfig(); c.BlockScrollWhileWheelPressed = true;
        var b = new ScrollBlocker(config: c);
        b.WheelMouseButton(wheel, isDown: true, t: 0);
        b.ForceReleaseAllButtons(t: 10);
        Assert.False(b.AnyButtonDown);
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(tick(10 + ScrollBlocker.WheelReleaseGrace + 0.01)));
    }

    [Fact]
    public void testWheelSettingsSurviveCoding()
    {
        var c = new BlockerConfig();
        c.ReverseWheelScroll = true;
        c.BlockScrollWhileWheelPressed = true;
        BlockerConfig decoded = JsonSerializer.Deserialize<BlockerConfig>(JsonSerializer.SerializeToUtf8Bytes(c));
        Assert.Equal(c, decoded);
        // A config saved before wheel support existed decodes with both off.
        BlockerConfig old = JsonSerializer.Deserialize<BlockerConfig>("""{"deadZone":4}"""u8);
        Assert.False(old.ReverseWheelScroll);
        Assert.False(old.BlockScrollWhileWheelPressed);
    }

    // MARK: Smooth scrolling

    private static ScrollBlocker smoothing(double speed = 3, bool reverse = false)
    {
        var c = new BlockerConfig();
        c.SmoothWheelScroll = true;
        c.WheelScrollSpeed = speed;
        c.ReverseWheelScroll = reverse;
        return new ScrollBlocker(config: c);
    }

    [Fact]
    public void testSmoothingTurnsATickIntoADistanceToGlide()
    {
        ScrollBlocker b = smoothing();
        // A slow single notch arrives from macOS as one pixel. The floor makes it worth turning.
        Assert.Equal(new ScrollDecision.Smooth(Dx: 0, Dy: -105), b.HandleScroll(tick(0, dy: -1)));
        // A fast spin keeps the acceleration macOS already gave it.
        Assert.Equal(new ScrollDecision.Smooth(Dx: 0, Dy: -153), b.HandleScroll(tick(0.03, dy: -51)));
        Assert.Equal(new ScrollDecision.Smooth(Dx: 105, Dy: 0), b.HandleScroll(tick(0.06, dx: 10, dy: 0)));
    }

    [Fact]
    public void testSmoothingFollowsTheSpeedAndDirectionSettings()
    {
        Assert.Equal(new ScrollDecision.Smooth(Dx: 0, Dy: -35), smoothing(speed: 1).HandleScroll(tick(0, dy: -1)));
        Assert.Equal(new ScrollDecision.Smooth(Dx: 0, Dy: 210), smoothing(speed: 6).HandleScroll(tick(0, dy: 1)));
        Assert.Equal(new ScrollDecision.Smooth(Dx: 0, Dy: 105), smoothing(reverse: true).HandleScroll(tick(0, dy: -1)));
    }

    [Fact]
    public void testZoomKeysBypassSmoothing()
    {
        // ⌘, ⌃ and ⌥ with the wheel mean zoom, which counts notches: one notch must stay one.
        ScrollBlocker b = smoothing();
        foreach (ModifierCombo key in new[] { ModifierCombo.Command, ModifierCombo.Control, ModifierCombo.Option, ModifierCombo.Command | ModifierCombo.Shift })
        {
            Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(tick(0, mods: key)));   // "\(key.label)"
        }
        // Direction still follows the setting when a tick is passed through whole.
        Assert.Equal(inverted, smoothing(reverse: true).HandleScroll(tick(0, mods: ModifierCombo.Command)));
    }

    [Fact]
    public void testShiftGlidesSideways()
    {
        ScrollBlocker b = smoothing();
        Assert.Equal(new ScrollDecision.Smooth(Dx: -105, Dy: 0), b.HandleScroll(tick(0, dy: -1, mods: ModifierCombo.Shift)));
        // Already sideways (a tilt wheel, or the system turned it): left as it is.
        Assert.Equal(new ScrollDecision.Smooth(Dx: -105, Dy: 0), b.HandleScroll(tick(0.1, dx: -1, dy: 0, mods: ModifierCombo.Shift)));
    }

    [Fact]
    public void testPressedWheelStillBlocksWhenSmoothing()
    {
        var c = new BlockerConfig();
        c.SmoothWheelScroll = true;
        c.BlockScrollWhileWheelPressed = true;
        var b = new ScrollBlocker(config: c);
        b.WheelMouseButton(wheel, isDown: true, t: 0);
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(tick(0.1)));
    }

    /// <summary>Runs the smoother at a fixed frame rate until it goes idle.</summary>
    private static List<WheelSmoother.StepResult> play(ref WheelSmoother s, double fps = 120, long maxFrames = 600)
    {
        var steps = new List<WheelSmoother.StepResult>();
        // Like Swift's `while let step = s.step(…), steps.count < maxFrames`: the step is taken
        // before the count is checked.
        while (s.Step(dt: 1 / fps) is WheelSmoother.StepResult step && steps.Count < maxFrames) { steps.Add(step); }
        return steps;
    }

    [Fact]
    public void testGlideScrollsExactlyTheDistanceAskedFor()
    {
        foreach (double fps in new[] { 60.0, 120.0 })
        {
            var s = new WheelSmoother();
            s.Add(dx: 0, dy: -105);
            List<WheelSmoother.StepResult> steps = play(ref s, fps: fps);
            Assert.Equal(-105L, steps.Aggregate(0L, (sum, step) => sum + step.Y));   // "\(fps) fps"
            Assert.Equal(0L, steps.Aggregate(0L, (sum, step) => sum + step.X));
            Assert.True(s.IsIdle);
            // Smooth means spread over frames, but not left dribbling: done well inside a second.
            Assert.True(steps.Count > 8, FormattableString.Invariant($"{fps} fps"));
            Assert.True((double)steps.Count / fps < 0.7, FormattableString.Invariant($"{fps} fps"));
        }
    }

    [Fact]
    public void testGlideStartsFastAndEasesOff()
    {
        var s = new WheelSmoother();
        s.Add(dx: 0, dy: 300);
        List<WheelSmoother.StepResult> steps = play(ref s);
        Assert.True(steps.Count > 30);
        Assert.True(steps[0].Y > steps[10].Y);
        Assert.True(steps[10].Y > steps[30].Y);
        Assert.True(steps.All(step => step.Y >= 0), "a glide never doubles back");
    }

    [Fact]
    public void testTicksAddUpWhileGliding()
    {
        var s = new WheelSmoother();
        s.Add(dx: 0, dy: -105);
        long total = 0;
        for (int i = 0; i < 5; i++) { total += s.Step(dt: 1 / 120.0)?.Y ?? 0; }
        s.Add(dx: 0, dy: -105);
        total += play(ref s).Aggregate(0L, (sum, step) => sum + step.Y);
        Assert.Equal(-210L, total);
    }

    [Fact]
    public void testTurningTheWheelBackStopsTheOldGlideAtOnce()
    {
        var s = new WheelSmoother();
        s.Add(dx: 0, dy: -400);
        for (int i = 0; i < 6; i++) { _ = s.Step(dt: 1 / 120.0); }
        s.Add(dx: 0, dy: 105);
        List<WheelSmoother.StepResult> after = play(ref s);
        Assert.True(after.All(step => step.Y >= 0), "nothing more goes the old way");
        Assert.Equal(105L, after.Aggregate(0L, (sum, step) => sum + step.Y));
    }

    [Fact]
    public void testFractionalDistanceSettlesAndGoesIdle()
    {
        var s = new WheelSmoother();
        s.Add(dx: 87.5, dy: 0);
        long total = play(ref s).Aggregate(0L, (sum, step) => sum + step.X);
        Assert.True(total == 87 || total == 88, FormattableString.Invariant($"{total}"));
        Assert.True(s.IsIdle);
    }

    [Fact]
    public void testCancelStopsThePageWhereItIs()
    {
        var s = new WheelSmoother();
        s.Add(dx: 0, dy: 500);
        _ = s.Step(dt: 1 / 120.0);
        s.Cancel();
        Assert.True(s.IsIdle);
        Assert.Null(s.Step(dt: 1 / 120.0));
    }

    [Fact]
    public void testAStalledFrameClockDoesNotFireOneHugeJump()
    {
        var s = new WheelSmoother();
        s.Add(dx: 0, dy: 1000);
        WheelSmoother.StepResult? first = s.Step(dt: 5);       // the frame clock was held up for five seconds
        Assert.True((first?.Y ?? 0) < 800);
        Assert.False(s.IsIdle);
    }

    [Fact]
    public void testSmoothSettingsSurviveCoding()
    {
        var c = new BlockerConfig();
        c.SmoothWheelScroll = true;
        c.WheelScrollSpeed = 4.5;
        Assert.Equal(c, JsonSerializer.Deserialize<BlockerConfig>(JsonSerializer.SerializeToUtf8Bytes(c)));
        BlockerConfig old = JsonSerializer.Deserialize<BlockerConfig>("""{"deadZone":4}"""u8);
        Assert.False(old.SmoothWheelScroll);
        Assert.Equal(3.0, old.WheelScrollSpeed);
    }

    // MARK: Each wheel mouse has its own settings

    [Fact]
    public void testEachWheelMouseCarriesItsOwnSettings()
    {
        var b = new ScrollBlocker();                       // the template: everything off
        var glides = new WheelSettings();
        glides.Smooth = true;
        var reversed = new WheelSettings();
        reversed.Reverse = true;
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(tick(0)));   // "a mouse with no settings of its own"
        if (b.HandleScroll(tick(1, wheel: glides)) is not ScrollDecision.Smooth)
        {
            Assert.Fail("this mouse glides");
            return;
        }
        Assert.Equal(inverted, b.HandleScroll(tick(2, wheel: reversed)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(tick(3, wheel: new WheelSettings())));
    }

    [Fact]
    public void testAPressedWheelBlocksOnlyForAMouseThatAskedForIt()
    {
        var b = new ScrollBlocker();
        var blocks = new WheelSettings();
        blocks.BlockWhilePressed = true;
        b.WheelMouseButton(wheel, isDown: true, t: 0);
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(tick(0.1, wheel: blocks)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(tick(0.2, wheel: new WheelSettings())));
    }

    [Fact]
    public void testTheTemplateIsTodaysWheelSettings()
    {
        var c = new BlockerConfig();
        c.ReverseWheelScroll = true;
        c.SmoothWheelScroll = true;
        c.WheelScrollSpeed = 5;
        Assert.Equal(new WheelSettings(reverse: true, blockWhilePressed: false, smooth: true, speed: 5),
                     c.WheelTemplate);
    }

    [Fact]
    public void testTheGlideTravelsAsFarAsThatMouseAsks()
    {
        var b = new ScrollBlocker();
        var slow = new WheelSettings();
        slow.Smooth = true;
        slow.Speed = 1;
        WheelSettings fast = slow;
        fast.Speed = 4;
        // `guard case … , case … else`: the second tick is only sent if the first one glides.
        if (b.HandleScroll(tick(0, wheel: slow)) is not ScrollDecision.Smooth(_, double slowY)
            || b.HandleScroll(tick(1, wheel: fast)) is not ScrollDecision.Smooth(_, double fastY))
        {
            Assert.Fail("both glide");
            return;
        }
        Assert.Equal(slowY * 4, fastY, 0.001);
    }

    // MARK: A zoom notch stops the glide

    [Fact]
    public void testAZoomNotchInterruptsAGlideInFlight()
    {
        // ⌘, ⌃ and ⌥ ticks are never smoothed. One arriving mid-glide means the user has
        // switched to zooming, so the page must stop gliding underneath it.
        Assert.True(ScrollBlocker.InterruptsGlide(tick(0, mods: ModifierCombo.Command)));
        Assert.True(ScrollBlocker.InterruptsGlide(tick(0, mods: ModifierCombo.Control)));
        Assert.True(ScrollBlocker.InterruptsGlide(tick(0, mods: ModifierCombo.Option)));
        // Plain and Shift ticks feed the glide instead.
        Assert.False(ScrollBlocker.InterruptsGlide(tick(0)));
        Assert.False(ScrollBlocker.InterruptsGlide(tick(0, mods: ModifierCombo.Shift)));
        // A Magic Mouse gesture has nothing to do with the wheel's glide.
        ScrollEvent magic = swipe(0, ScrollPhase.Changed);
        magic.Modifiers = ModifierCombo.Command;
        Assert.False(ScrollBlocker.InterruptsGlide(magic));
    }

    // MARK: Which kind of mouse is this?

    private readonly PointerIdentity magicMouse = new(isMagicMouse: true, isTrackpad: false, isMouse: true);
    private readonly PointerIdentity wheelMouse = new(isMagicMouse: false, isTrackpad: false, isMouse: true);
    private readonly PointerIdentity trackpad = new(isMagicMouse: false, isTrackpad: true, isMouse: true);
    private readonly PointerIdentity somethingElse = new(isMagicMouse: false, isTrackpad: false, isMouse: false);

    private static MouseKind? kind(PointerIdentity? id, PointerEventKind @event,
                                   MouseTypeSetting setting = MouseTypeSetting.Automatic, bool legacy = false) =>
        MouseClassifier.Kind(id, @event: @event, setting: setting, unknownContinuousIsMagicMouse: legacy);

    private readonly PointerEventKind gesture = new PointerEventKind.Scroll(IsContinuous: true);
    private readonly PointerEventKind ticks = new PointerEventKind.Scroll(IsContinuous: false);

    [Fact]
    public void testAutomaticTellsTheDevicesApart()
    {
        Assert.Equal((MouseKind?)MouseKind.MagicMouse, kind(magicMouse, gesture));
        Assert.Equal((MouseKind?)MouseKind.MagicMouse, kind(magicMouse, new PointerEventKind.Button()));
        Assert.Equal((MouseKind?)MouseKind.WheelMouse, kind(wheelMouse, ticks));
        Assert.Equal((MouseKind?)MouseKind.WheelMouse, kind(wheelMouse, new PointerEventKind.Button()));
        Assert.Null(kind(somethingElse, ticks));
        Assert.Null(kind(null, ticks));   // "an unidentified sender is left alone"
        Assert.Null(kind(null, gesture));
        Assert.Null(kind(null, new PointerEventKind.Button()));
    }

    [Fact]
    public void testATrackpadIsNeverManagedWhateverTheSetting()
    {
        foreach (MouseTypeSetting setting in MouseTypeSetting.AllCases)
        {
            foreach (PointerEventKind @event in new[] { gesture, ticks, new PointerEventKind.Button() })
            {
                Assert.Null(kind(trackpad, @event, setting));   // "\(setting) \(event)"
            }
        }
    }

    [Fact]
    public void testGestureScrollingFromANonMagicMouseIsLeftAlone()
    {
        // Some touch surface we don't know: not a wheel, so not ours.
        Assert.Null(kind(wheelMouse, gesture));
        Assert.Null(kind(wheelMouse, gesture, MouseTypeSetting.WheelMouse));
    }

    [Fact]
    public void testForcedMagicMouseTakesOverAMouseDetectionGotWrong()
    {
        Assert.Equal((MouseKind?)MouseKind.MagicMouse, kind(magicMouse, gesture, MouseTypeSetting.MagicMouse));
        // A Magic Mouse CalmMouse identified as some other mouse: the user's choice wins.
        Assert.Equal((MouseKind?)MouseKind.MagicMouse, kind(wheelMouse, gesture, MouseTypeSetting.MagicMouse));
        Assert.Equal((MouseKind?)MouseKind.MagicMouse, kind(wheelMouse, new PointerEventKind.Button(), MouseTypeSetting.MagicMouse));
        // A Magic Mouse never sends wheel ticks, so a real wheel mouse stays untouched.
        Assert.Null(kind(wheelMouse, ticks, MouseTypeSetting.MagicMouse));
        // The choice is about mice: anything that doesn't declare itself one is left alone.
        Assert.Null(kind(somethingElse, gesture, MouseTypeSetting.MagicMouse));
        Assert.Null(kind(somethingElse, new PointerEventKind.Button(), MouseTypeSetting.MagicMouse));
        Assert.Equal((MouseKind?)MouseKind.MagicMouse, kind(null, gesture, MouseTypeSetting.MagicMouse));
        Assert.Equal((MouseKind?)MouseKind.MagicMouse, kind(null, new PointerEventKind.Button(), MouseTypeSetting.MagicMouse));
        Assert.Null(kind(null, ticks, MouseTypeSetting.MagicMouse));   // "wheel ticks are not a Magic Mouse"
    }

    [Fact]
    public void testForcedWheelManagesOnlyThatAndAdoptsUnknownTicks()
    {
        Assert.Equal((MouseKind?)MouseKind.WheelMouse, kind(wheelMouse, ticks, MouseTypeSetting.WheelMouse));
        Assert.Null(kind(magicMouse, gesture, MouseTypeSetting.WheelMouse));
        Assert.Null(kind(magicMouse, new PointerEventKind.Button(), MouseTypeSetting.WheelMouse));
        Assert.Equal((MouseKind?)MouseKind.WheelMouse, kind(null, ticks, MouseTypeSetting.WheelMouse));
        Assert.Equal((MouseKind?)MouseKind.WheelMouse, kind(null, new PointerEventKind.Button(), MouseTypeSetting.WheelMouse));
        Assert.Null(kind(null, gesture, MouseTypeSetting.WheelMouse));
    }

    [Fact]
    public void testOldUnknownContinuousFallbackStillWorks()
    {
        // `defaults write … treatUnknownContinuousAsMagicMouse -bool true`, documented in the README.
        Assert.Equal((MouseKind?)MouseKind.MagicMouse, kind(null, gesture, legacy: true));
        Assert.Equal((MouseKind?)MouseKind.MagicMouse, kind(null, new PointerEventKind.Button(), legacy: true));
        Assert.Null(kind(null, ticks, legacy: true));
        Assert.Null(kind(trackpad, gesture, legacy: true));
    }

    // MARK: Tracking speed

    [Fact]
    public void testTrackingSpeedMapsToResolution()
    {
        Assert.Equal(400 << 16, TrackingSpeed.Resolution(speed: 0));    // "the middle changes nothing"
        Assert.Equal(100 << 16, TrackingSpeed.Resolution(speed: 1));    // "fastest: a lower DPI, four times the travel"
        Assert.Equal(1600 << 16, TrackingSpeed.Resolution(speed: -1));
        Assert.Equal(100 << 16, TrackingSpeed.Resolution(speed: 9));    // "out of range is clamped"
    }
}

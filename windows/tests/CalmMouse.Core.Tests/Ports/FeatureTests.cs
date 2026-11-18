// Translated from Tests/CalmMouseCoreTests/FeatureTests.swift, including the tests in its two
// `extension FeatureTests` blocks (same class here). XCTAssertEqual(actual, expected) is
// Assert.Equal(expected, actual); xUnit's Assert.Equal takes no message, so the Swift messages on
// those asserts are kept as comments. XCTAssertEqual(x, false/true) on a Bool is Assert.False/True
// (xUnit's analyzer rejects Assert.Equal with a bool literal). JSONEncoder / JSONDecoder are
// System.Text.Json with the converters in Ports/Config.cs.

using System.Text.Json;

namespace CalmMouse.Core.Tests;

/// <summary>Dead zone, momentum control, per-app rules and modifier actions.</summary>
public sealed class FeatureTests
{
    private static ScrollEvent ev(double t, ScrollPhase phase = ScrollPhase.None, MomentumPhase momentum = MomentumPhase.None,
                                  double dx = 0, double dy = 3, ModifierCombo mods = ModifierCombo.None) =>
        new(timestamp: t, fromMagicMouse: true, isContinuous: true,
            phase: phase, momentum: momentum, deltaX: dx, deltaY: dy, modifiers: mods);

    // MARK: Dead zone

    [Fact]
    public void testDeadZoneSwallowsMicroJitterAndRelabelsTheFirstRealEvent()
    {
        var c = new BlockerConfig(); c.DeadZone = 10;
        var b = new ScrollBlocker(config: c);
        // Tiny movements: nothing reaches the app.
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0, ScrollPhase.Began, dy: 1)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.01, ScrollPhase.Changed, dy: 2)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.02, ScrollPhase.Changed, dy: 3)));   // cumulative 6 < 10
        // Crossing the threshold: the app sees a well-formed `began`, not a stray `changed`.
        Assert.Equal(new ScrollDecision.Rewrite(new ScrollRewrite(phase: ScrollPhase.Began, momentum: MomentumPhase.None)),
                     b.HandleScroll(ev(0.03, ScrollPhase.Changed, dy: 5)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.04, ScrollPhase.Changed, dy: 5)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.05, ScrollPhase.Ended, dy: 0)));
    }

    [Fact]
    public void testDeadZoneGestureThatNeverCommitsIsSwallowedIncludingItsEndAndMomentum()
    {
        var c = new BlockerConfig(); c.DeadZone = 10;
        var b = new ScrollBlocker(config: c);
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0, ScrollPhase.Began, dy: 1)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.01, ScrollPhase.Changed, dy: 1)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.02, ScrollPhase.Ended, dy: 0)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.03, momentum: MomentumPhase.Began, dy: 1)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.04, momentum: MomentumPhase.Ended, dy: 0)));
        // Next gesture is judged on its own travel: 20 pt clears the zone on the `began` event
        // itself, so the event is already well-formed and passes untouched.
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(1.0, ScrollPhase.Began, dy: 20)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(1.01, ScrollPhase.Changed, dy: 5)));
    }

    [Fact]
    public void testDeadZoneOffByDefault()
    {
        var b = new ScrollBlocker();
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0, ScrollPhase.Began, dy: 1)));
    }

    // MARK: Momentum

    [Fact]
    public void testMomentumCanBeDisabledForTheMagicMouseOnly()
    {
        var c = new BlockerConfig(); c.MomentumEnabled = false;
        var b = new ScrollBlocker(config: c);
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0, ScrollPhase.Began)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.01, ScrollPhase.Changed)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.02, ScrollPhase.Ended, dy: 0)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.03, momentum: MomentumPhase.Began, dy: 20)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.04, momentum: MomentumPhase.Continued, dy: 10)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.05, momentum: MomentumPhase.Ended, dy: 0)));
        // A trackpad's momentum is untouched (not from the Magic Mouse).
        ScrollEvent trackpad = ev(0.06, momentum: MomentumPhase.Continued, dy: 10); trackpad.FromMagicMouse = false;
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(trackpad));
    }

    // MARK: Horizontal block

    [Fact]
    public void testHorizontalScrollCanBeIgnored()
    {
        var c = new BlockerConfig(); c.BlockHorizontalScroll = true;
        var b = new ScrollBlocker(config: c);
        Assert.Equal(new ScrollDecision.Rewrite(new ScrollRewrite(phase: ScrollPhase.Began, momentum: MomentumPhase.None, zeroX: true)),
                     b.HandleScroll(ev(0, ScrollPhase.Began, dx: 8, dy: 3)));
        // Pure vertical needs no rewriting.
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.01, ScrollPhase.Changed, dx: 0, dy: 3)));
    }

    // MARK: Modifier actions

    [Fact]
    public void testModifierActionHorizontalSwapsAxesAndStripsTheModifier()
    {
        var c = new BlockerConfig(); c.ModifierActions = new Dictionary<ModifierCombo, ScrollAction> { [ModifierCombo.Option] = ScrollAction.Horizontal };
        var b = new ScrollBlocker(config: c);
        Assert.Equal(new ScrollDecision.Rewrite(new ScrollRewrite(phase: ScrollPhase.Began, momentum: MomentumPhase.None, swapAxes: true, removeModifiers: ModifierCombo.Option)),
                     b.HandleScroll(ev(0, ScrollPhase.Began, dy: 5, mods: ModifierCombo.Option)));
        // Without the modifier, nothing changes.
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.01, ScrollPhase.Changed, dy: 5)));
    }

    [Fact]
    public void testModifierActionZoomRelabelsAsCommandScroll()
    {
        var c = new BlockerConfig(); c.ModifierActions = new Dictionary<ModifierCombo, ScrollAction> { [ModifierCombo.Option] = ScrollAction.ZoomCommand };
        var b = new ScrollBlocker(config: c);
        Assert.Equal(new ScrollDecision.Rewrite(new ScrollRewrite(phase: ScrollPhase.Began, momentum: MomentumPhase.None,
                                                                  addModifiers: ModifierCombo.Command, removeModifiers: ModifierCombo.Option)),
                     b.HandleScroll(ev(0, ScrollPhase.Began, dy: 5, mods: ModifierCombo.Option)));
    }

    [Fact]
    public void testModifierActionBlockSwallowsScroll()
    {
        var c = new BlockerConfig(); c.ModifierActions = new Dictionary<ModifierCombo, ScrollAction> { [ModifierCombo.Command | ModifierCombo.Shift] = ScrollAction.Block };
        var b = new ScrollBlocker(config: c);
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0, ScrollPhase.Began, dy: 5, mods: ModifierCombo.Command | ModifierCombo.Shift)));
        // A different combo is unaffected.
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.01, ScrollPhase.Began, dy: 5, mods: ModifierCombo.Command)));
    }

    [Fact]
    public void testModifierActionInvertNegatesDeltas()
    {
        var c = new BlockerConfig(); c.ModifierActions = new Dictionary<ModifierCombo, ScrollAction> { [ModifierCombo.Shift] = ScrollAction.Invert };
        var b = new ScrollBlocker(config: c);
        Assert.Equal(new ScrollDecision.Rewrite(new ScrollRewrite(phase: ScrollPhase.Began, momentum: MomentumPhase.None, invert: true, removeModifiers: ModifierCombo.Shift)),
                     b.HandleScroll(ev(0, ScrollPhase.Began, dy: 5, mods: ModifierCombo.Shift)));
    }

    // MARK: Per-app rules

    [Fact]
    public void testPerAppRuleOverridesBaseConfig()
    {
        var @base = new BlockerConfig(); @base.BlockScrollWhileClicked = true;
        var figma = new AppRule(bundleID: "com.figma.Desktop", name: "Figma", disableScrollEntirely: true);
        var b = new ScrollBlocker(rules: new RuleSet(@base: @base, appRules: [figma]));

        // Default app: normal behaviour.
        b.SetActiveApp(bundleID: "com.apple.Safari");
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0, ScrollPhase.Began)));

        // Figma: Magic Mouse scrolling is ignored entirely.
        b.SetActiveApp(bundleID: "com.figma.Desktop");
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(1.0, ScrollPhase.Began)));
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(1.01, ScrollPhase.Changed)));

        // Back to Safari: normal again.
        b.SetActiveApp(bundleID: "com.apple.Safari");
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(2.0, ScrollPhase.Began)));
    }

    [Fact]
    public void testPerAppRuleMatchingIsCaseInsensitiveAndRespectsEnabledFlag()
    {
        var rule = new AppRule(bundleID: "com.figma.desktop", name: "Figma", momentumEnabled: false);
        var set = new RuleSet(@base: new BlockerConfig(), appRules: [rule]);
        Assert.False(set.Config(bundleID: "COM.FIGMA.DESKTOP").MomentumEnabled);
        Assert.True(set.Config(bundleID: "com.apple.Safari").MomentumEnabled);
        Assert.True(set.Config(bundleID: null).MomentumEnabled);

        rule.Enabled = false;
        Assert.True(new RuleSet(@base: new BlockerConfig(), appRules: [rule])
            .Config(bundleID: "com.figma.desktop").MomentumEnabled);
    }

    [Fact]
    public void testPerAppRuleCanRelaxTheGlobalBlock()
    {
        var @base = new BlockerConfig(); @base.BlockScrollWhileClicked = true;
        var rule = new AppRule(bundleID: "com.apple.dt.Xcode", name: "Xcode", blockScrollWhileClicked: false);
        var b = new ScrollBlocker(rules: new RuleSet(@base: @base, appRules: [rule]));
        b.SetActiveApp(bundleID: "com.apple.dt.Xcode");
        b.MagicMouseButton(0, isDown: true, t: 0);
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.01, ScrollPhase.Began)));
    }

    [Fact]
    public void testEmptyRuleIsRecognised()
    {
        Assert.True(new AppRule(bundleID: "a", name: "A").IsEmpty);
        Assert.False(new AppRule(bundleID: "a", name: "A", axisLock: true).IsEmpty);
    }

    // MARK: Config round-trips (settings persistence)

    [Fact]
    public void testConfigCodableRoundTripIncludingModifierActions()
    {
        var c = new BlockerConfig();
        c.DeadZone = 12;
        c.MomentumEnabled = false;
        c.ModifierActions = new Dictionary<ModifierCombo, ScrollAction>
        {
            [ModifierCombo.Option] = ScrollAction.Horizontal,
            [ModifierCombo.Command | ModifierCombo.Shift] = ScrollAction.Block,
        };
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(new RuleSet(@base: c, appRules:
        [
            new AppRule(bundleID: "com.figma.Desktop", name: "Figma", disableScrollEntirely: true),
        ]));
        RuleSet decoded = JsonSerializer.Deserialize<RuleSet>(data);
        Assert.Equal(c, decoded.Base);
        Assert.Equal(ScrollAction.Horizontal, decoded.Base.Action(ModifierCombo.Option));
        Assert.Equal(ScrollAction.Block, decoded.Base.Action(ModifierCombo.Command | ModifierCombo.Shift));
        Assert.Equal(ScrollAction.Normal, decoded.Base.Action(ModifierCombo.Control));
        // `appRules.first?.disableScrollEntirely`: nil when there is no first rule.
        bool? firstDisables = decoded.AppRules.Count > 0 ? decoded.AppRules[0].DisableScrollEntirely : null;
        Assert.Equal<bool?>(true, firstDisables);
    }

    // MARK: Interactions

    [Fact]
    public void testBlockWhileClickedStillWinsOverModifierAction()
    {
        var c = new BlockerConfig(); c.ModifierActions = new Dictionary<ModifierCombo, ScrollAction> { [ModifierCombo.Option] = ScrollAction.Horizontal };
        var b = new ScrollBlocker(config: c);
        b.MagicMouseButton(0, isDown: true, t: 0);
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0.01, ScrollPhase.Began, dy: 5, mods: ModifierCombo.Option)));
    }

    [Fact]
    public void testDeadZoneAndAxisLockCoexist()
    {
        var c = new BlockerConfig(); c.DeadZone = 6; c.AxisLock = true; c.AxisLockThreshold = 10;
        var b = new ScrollBlocker(config: c);
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(0, ScrollPhase.Began, dx: 1, dy: 2)));      // travel 3
        // travel 3+5=8 ≥ 6 → released as `began`; axis lock hasn't committed yet (cum 4/2 dominant 4 < 10).
        Assert.Equal(new ScrollDecision.Rewrite(new ScrollRewrite(phase: ScrollPhase.Began, momentum: MomentumPhase.None)),
                     b.HandleScroll(ev(0.01, ScrollPhase.Changed, dx: 1, dy: 4)));
        // Now vertical dominates past the threshold → horizontal drift is zeroed.
        Assert.Equal(new ScrollDecision.Rewrite(new ScrollRewrite(phase: ScrollPhase.Changed, momentum: MomentumPhase.None, zeroX: true)),
                     b.HandleScroll(ev(0.02, ScrollPhase.Changed, dx: 1, dy: 10)));
    }

    // extension FeatureTests

    [Fact]
    public void testDiagnosticDeadZoneConfigReachesBlocker()
    {
        var c = new BlockerConfig(); c.DeadZone = 10;
        var b = new ScrollBlocker(config: c);
        Assert.Equal(10.0, b.Config.DeadZone);       // "config.deadZone"
        Assert.Equal(10.0, b.BaseConfig.DeadZone);   // "baseConfig.deadZone"
    }

    // extension FeatureTests

    /// <summary>
    /// A gesture already in flight when settings change (or when the tap starts) must not be
    /// held back by the dead zone — we don't know how far it has already travelled.
    /// </summary>
    [Fact]
    public void testGestureJoinedMidFlightIgnoresDeadZone()
    {
        var c = new BlockerConfig(); c.DeadZone = 20;
        var b = new ScrollBlocker(config: c);
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0, ScrollPhase.Changed, dy: 2)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.01, ScrollPhase.Changed, dy: 2)));
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(ev(0.02, ScrollPhase.Ended, dy: 0)));
        // But the next properly-started gesture is subject to it again.
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(ev(1.0, ScrollPhase.Began, dy: 2)));
    }

    /// <summary>
    /// Regression: mach-tick timestamps made the release grace ~42x too long. The blocker works
    /// in seconds, so a 200 ms grace must expire 200 ms later — not 8 seconds later.
    /// </summary>
    [Fact]
    public void testReleaseGraceIsInterpretedInSeconds()
    {
        var c = new BlockerConfig(); c.ReleaseGrace = 0.2;
        var b = new ScrollBlocker(config: c);
        b.MagicMouseButton(0, isDown: true, t: 100.0);
        b.MagicMouseButton(0, isDown: false, t: 100.5);
        Assert.True(b.IsBlocking(t: 100.6));    // 100 ms after release
        Assert.False(b.IsBlocking(t: 100.75));  // 250 ms after release
        Assert.False(b.IsBlocking(t: 101.0));
    }
}

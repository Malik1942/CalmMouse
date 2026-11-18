// Translated from Tests/CalmMouseCoreTests/MagicMouseReverseTests.swift. XCTAssertEqual(actual,
// expected) is Assert.Equal(expected, actual). Swift's `(inout BlockerConfig) -> Void` closure is
// the delegate ConfigEdit with a ref parameter. `guard case … else { return XCTFail(…) }` is a
// negated pattern match, Assert.Fail and return.

namespace CalmMouse.Core.Tests;

/// <summary>
/// Reverse direction for the Magic Mouse alone. macOS has one "natural scrolling" switch for
/// the trackpad and every mouse, so someone who wants it on the trackpad gets it here too.
/// </summary>
public sealed class MagicMouseReverseTests
{
    private static ScrollEvent swipe(double t, ScrollPhase phase, MomentumPhase momentum = MomentumPhase.None,
                                     double dy = 3, ModifierCombo mods = ModifierCombo.None) =>
        new(timestamp: t, fromMagicMouse: true, isContinuous: true, phase: phase,
            momentum: momentum, deltaY: dy, modifiers: mods);

    private delegate void ConfigEdit(ref BlockerConfig c);

    private static ScrollBlocker reversing(ConfigEdit? edit = null)
    {
        var c = new BlockerConfig();
        c.ReverseMagicMouseScroll = true;
        edit?.Invoke(ref c);
        return new ScrollBlocker(config: c);
    }

    private static ScrollDecision flipped(ScrollPhase phase, MomentumPhase momentum = MomentumPhase.None) =>
        new ScrollDecision.Rewrite(new ScrollRewrite(phase: phase, momentum: momentum, invert: true));

    [Fact]
    public void testOffByDefaultAndChangesNothing()
    {
        Assert.False(new BlockerConfig().ReverseMagicMouseScroll);
        Assert.Equal(new ScrollDecision.Pass(), new ScrollBlocker().HandleScroll(swipe(0, ScrollPhase.Began)));
    }

    [Fact]
    public void testASwipeComesOutTheOtherWay()
    {
        ScrollBlocker b = reversing();
        Assert.Equal(flipped(ScrollPhase.Began), b.HandleScroll(swipe(0, ScrollPhase.Began)));
        Assert.Equal(flipped(ScrollPhase.Changed), b.HandleScroll(swipe(0.02, ScrollPhase.Changed)));
        Assert.Equal(flipped(ScrollPhase.Ended), b.HandleScroll(swipe(0.04, ScrollPhase.Ended, dy: 0)));
    }

    [Fact]
    public void testTheGlideAfterASwipeIsReversedToo()
    {
        ScrollBlocker b = reversing();
        _ = b.HandleScroll(swipe(0, ScrollPhase.Began));
        _ = b.HandleScroll(swipe(0.02, ScrollPhase.Ended, dy: 0));
        Assert.Equal(flipped(ScrollPhase.None, MomentumPhase.Began), b.HandleScroll(swipe(0.05, ScrollPhase.None, momentum: MomentumPhase.Began)));
    }

    [Fact]
    public void testABlockedSwipeStaysBlocked()
    {
        ScrollBlocker b = reversing();
        b.MagicMouseButton(0, isDown: true, t: 0);
        Assert.Equal(new ScrollDecision.Drop(), b.HandleScroll(swipe(0.01, ScrollPhase.Began)));
    }

    [Fact]
    public void testHoldingTheInvertKeyCancelsItOut()
    {
        ScrollBlocker b = reversing((ref BlockerConfig c) =>
            c.ModifierActions = new Dictionary<ModifierCombo, ScrollAction> { [ModifierCombo.Option] = ScrollAction.Invert });
        if (b.HandleScroll(swipe(0, ScrollPhase.Began, mods: ModifierCombo.Option)) is not ScrollDecision.Rewrite(ScrollRewrite r))
        {
            Assert.Fail("still a rewrite: the held key is taken off");
            return;
        }
        Assert.False(r.Invert, "inverted twice is the normal direction");
    }

    [Fact]
    public void testWheelMiceAndTrackpadsAreLeftAlone()
    {
        ScrollBlocker b = reversing();
        var tick = new ScrollEvent(timestamp: 0, fromMagicMouse: false, isContinuous: false,
                                   deltaY: -10, fromWheelMouse: true);
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(tick));
        var trackpad = new ScrollEvent(timestamp: 0, fromMagicMouse: false, isContinuous: true,
                                       phase: ScrollPhase.Began, deltaY: 3);
        Assert.Equal(new ScrollDecision.Pass(), b.HandleScroll(trackpad));
    }
}

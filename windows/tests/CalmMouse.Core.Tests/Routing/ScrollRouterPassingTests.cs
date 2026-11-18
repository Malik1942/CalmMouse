using CalmMouse.Core.Routing;
using static CalmMouse.Core.Tests.Routing.RouterHarness;

namespace CalmMouse.Core.Tests.Routing;

/// <summary>
/// When the router keeps its hands off scrolling (nothing on, paused, elevated window, an app
/// ruled "leave alone"), and why the Raw Input twins of events it passed never get paired with
/// an unrelated event later.
/// </summary>
public sealed class ScrollRouterPassingTests
{
    private static readonly WheelSettings Reverse = new(reverse: true);
    private static readonly WheelSettings Smooth = new(smooth: true);

    // MARK: Nothing enabled

    [Fact]
    public void WithNothingOnEveryEventPasses()
    {
        var h = new RouterHarness();
        Assert.False(h.Router.IsSwallowing);
        Assert.Equal(HookAnswer.Pass, h.Turn(MouseA));
        Assert.Equal(HookAnswer.Pass, h.TurnRawFirst(MouseB));
        Assert.Equal(HookAnswer.Pass, h.Turn(Pad));
        h.Run(100);
        Assert.Empty(h.Sends());
        Assert.False(h.Router.NeedsTicks);
    }

    [Fact]
    public void ATouchpadsSettingsAloneDoNotStartSwallowing()
    {
        var router = new ScrollRouter();
        router.SetDevice(new RoutedDevice(Pad, DeviceKind.Touchpad, "pad") { Wheel = new WheelSettings(reverse: true, smooth: true) });
        Assert.False(router.IsSwallowing);
        Assert.Equal(HookAnswer.Pass, router.HookWheel(0, Notch, horizontal: false));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void AnyOneWheelFeatureStartsSwallowing(bool reverse, bool block, bool smooth)
    {
        var h = new RouterHarness(b: new WheelSettings(reverse: reverse, blockWhilePressed: block, smooth: smooth));
        Assert.True(h.Router.IsSwallowing);
    }

    [Fact]
    public void TurningTheLastFeatureOffStopsSwallowing()
    {
        var h = new RouterHarness(a: Reverse);
        h.Router.SetDevice(new RoutedDevice(MouseA, DeviceKind.WheelMouse, "A"));
        Assert.False(h.Router.IsSwallowing);
        Assert.Equal(HookAnswer.Pass, h.Turn(MouseA));
    }

    [Fact]
    public void RemovingTheOnlyConfiguredMouseStopsSwallowing()
    {
        var h = new RouterHarness(a: Reverse);
        h.Router.RemoveDevice(MouseA);
        Assert.False(h.Router.IsSwallowing);
    }

    // MARK: Paused, elevated, leave alone

    [Fact]
    public void PausedPassesEverything()
    {
        var h = new RouterHarness(a: Reverse);
        h.Router.SetPaused(true);
        Assert.Equal(HookAnswer.Pass, h.Turn(MouseA));
        Assert.Equal(HookAnswer.Pass, h.TurnRawFirst(MouseA));
        h.Run(100);
        Assert.Empty(h.Sends());

        h.Router.SetPaused(false);
        h.Wait(100);
        Assert.Equal(HookAnswer.Swallow, h.Turn(MouseA));
        Assert.Equal([Up(-Notch)], h.Sends());
    }

    [Fact]
    public void AnElevatedForegroundWindowPassesEverything()
    {
        var h = new RouterHarness(a: Reverse);
        h.Router.ForegroundChanged("Taskmgr.exe", elevated: true);
        Assert.False(h.Router.IsSwallowing);
        Assert.Equal(HookAnswer.Pass, h.Turn(MouseA));
        h.Run(100);
        Assert.Empty(h.Sends());

        h.Router.ForegroundChanged("notepad.exe", elevated: false);
        Assert.True(h.Router.IsSwallowing);
    }

    [Fact]
    public void AnAppRuledLeaveAloneGetsEveryEventUntouched_MatchedIgnoringCase()
    {
        var h = new RouterHarness(a: Reverse);
        h.Router.SetAppRules([new WheelAppRule("Figma.exe", "Figma") { LeaveAlone = true }]);
        h.Router.ForegroundChanged("figma.EXE", elevated: false);
        Assert.False(h.Router.IsSwallowing);
        Assert.Equal(HookAnswer.Pass, h.Turn(MouseA));
        h.Run(100);
        Assert.Empty(h.Sends());
    }

    [Fact]
    public void ADisabledLeaveAloneRuleDoesNothing()
    {
        var h = new RouterHarness(a: Reverse);
        h.Router.SetAppRules([new WheelAppRule("figma.exe", "Figma") { LeaveAlone = true, Enabled = false }]);
        h.Router.ForegroundChanged("figma.exe", elevated: false);
        Assert.True(h.Router.IsSwallowing);
    }

    [Fact]
    public void SwitchingToPassingSendsWaitingEventsOnUnchanged()
    {
        var h = new RouterHarness(a: Reverse);
        Assert.Equal(HookAnswer.Swallow, h.Hook());
        h.Router.ForegroundChanged("Taskmgr.exe", elevated: true);
        Assert.Equal([Up()], h.Sends());
        Assert.False(h.Router.NeedsTicks);

        h.Raw(MouseA);                            // its twin turns up: nothing more happens
        h.Run(100);
        Assert.Empty(h.Sends());
    }

    [Fact]
    public void PausingStopsARunningGlide()
    {
        var h = new RouterHarness(a: Smooth);
        h.Turn(MouseA);
        h.Run(17);
        Assert.NotEmpty(h.Sends());

        h.Router.SetPaused(true);
        Assert.False(h.Router.NeedsTicks);
        h.Run(500);
        Assert.Empty(h.Sends());
    }

    // MARK: Twins of passed events don't steal later ones

    [Fact]
    public void HookFirst_TheTwinOfAnEventPassedWhilePausedDoesNotStealTheNextOne()
    {
        var h = new RouterHarness(b: Reverse);   // A is plain, B reverses
        h.Router.SetPaused(true);
        Assert.Equal(HookAnswer.Pass, h.Hook());  // A's tick, passed
        h.Router.SetPaused(false);

        Assert.Equal(HookAnswer.Swallow, h.Hook());   // B's tick, a moment later
        h.Raw(MouseA);                            // A's twin, late: pairs with A's passed tick
        Assert.Empty(h.Sends());
        h.Raw(MouseB);                            // B's twin claims B's tick
        Assert.Equal([Up(-Notch)], h.Sends());
    }

    [Fact]
    public void RawFirst_TheTwinOfAnEventPassedWhilePausedDoesNotStealTheNextOne()
    {
        var h = new RouterHarness(b: Reverse);
        h.Router.SetPaused(true);
        h.Raw(MouseA);                            // A's twin first...
        Assert.Equal(HookAnswer.Pass, h.Hook());  // ...then A's tick, which takes it along
        h.Router.SetPaused(false);

        Assert.Equal(HookAnswer.Swallow, h.Hook());   // B's tick: waits, isn't paired with A's raw
        Assert.Empty(h.Sends());
        Assert.True(h.Router.NeedsTicks);
        h.Raw(MouseB);
        Assert.Equal([Up(-Notch)], h.Sends());
    }

    [Fact]
    public void TheTwinOfAnEventPassedInAnElevatedWindowDoesNotStealTheNextOne()
    {
        var h = new RouterHarness(b: Reverse);
        h.Router.ForegroundChanged("Taskmgr.exe", elevated: true);
        Assert.Equal(HookAnswer.Pass, h.Hook());
        h.Router.ForegroundChanged("notepad.exe", elevated: false);

        Assert.Equal(HookAnswer.Swallow, h.Hook());
        h.Raw(MouseA);
        h.Raw(MouseB);
        Assert.Equal([Up(-Notch)], h.Sends());
    }

    [Fact]
    public void TheTwinOfAnEventPassedInALeaveAloneAppDoesNotStealTheNextOne()
    {
        var h = new RouterHarness(b: Reverse);
        h.Router.SetAppRules([new WheelAppRule("figma.exe", "Figma") { LeaveAlone = true }]);
        h.Router.ForegroundChanged("figma.exe", elevated: false);
        h.Raw(MouseA);
        Assert.Equal(HookAnswer.Pass, h.Hook());
        h.Router.ForegroundChanged("notepad.exe", elevated: false);

        Assert.Equal(HookAnswer.Swallow, h.Hook());
        Assert.Empty(h.Sends());
        h.Raw(MouseB);
        Assert.Equal([Up(-Notch)], h.Sends());
    }

    [Fact]
    public void TheTwinOfAFlushedEventDoesNotStealTheNextOne()
    {
        var h = new RouterHarness(b: Reverse);
        h.Hook();                                 // A's tick, swallowed
        h.Router.SetPaused(true);                 // flushed unchanged
        Assert.Equal([Up()], h.Sends());
        h.Router.SetPaused(false);

        Assert.Equal(HookAnswer.Swallow, h.Hook());   // B's tick
        h.Raw(MouseA);
        h.Raw(MouseB);
        Assert.Equal([Up(-Notch)], h.Sends());
    }
}

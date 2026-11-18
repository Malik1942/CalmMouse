using CalmMouse.Core.Routing;
using static CalmMouse.Core.Tests.Routing.RouterHarness;

namespace CalmMouse.Core.Tests.Routing;

/// <summary>
/// Pairing hook events with their Raw Input twins: both arrival orders, the 50 ms window, our
/// own resends coming back, and the touchpad safety net.
/// </summary>
public sealed class ScrollRouterMatchingTests
{
    private static readonly WheelSettings Reverse = new(reverse: true);

    // MARK: Both orders

    [Fact]
    public void HookFirst_TheSwallowedEventWaitsForItsDevice()
    {
        var h = new RouterHarness(a: Reverse);
        Assert.Equal(HookAnswer.Swallow, h.Hook());
        Assert.Empty(h.Sends());
        Assert.True(h.Router.NeedsTicks);

        h.Raw(MouseA);
        Assert.Equal([Up(-Notch)], h.Sends());
        Assert.False(h.Router.NeedsTicks);
    }

    [Fact]
    public void RawFirst_TheHookEventIsDecidedOnTheSpot()
    {
        var h = new RouterHarness(a: Reverse);
        h.Raw(MouseA);
        Assert.Empty(h.Sends());

        Assert.Equal(HookAnswer.Swallow, h.Hook());
        Assert.Equal([Up(-Notch)], h.Sends());
        Assert.False(h.Router.NeedsTicks);
    }

    [Fact]
    public void HookFirst_ADeviceWithNothingToChangeGetsItsEventResentUnchanged()
    {
        var h = new RouterHarness(a: Reverse);   // B has nothing on
        Assert.Equal(HookAnswer.Swallow, h.Turn(MouseB));
        Assert.Equal([Up()], h.Sends());
    }

    [Fact]
    public void RawFirst_ADeviceWithNothingToChangeIsSimplyPassed()
    {
        var h = new RouterHarness(a: Reverse);
        Assert.Equal(HookAnswer.Pass, h.TurnRawFirst(MouseB));
        Assert.Empty(h.Sends());
    }

    [Fact]
    public void ARawWheelClaimsTheOldestWaitingEvent()
    {
        var h = new RouterHarness(a: Reverse);
        h.Hook(Notch);
        h.Hook(2 * Notch);

        h.Raw(MouseA);   // the first tick: A's
        Assert.Equal([Up(-Notch)], h.Sends());
        h.Raw(MouseB);   // the second: B's, unchanged
        Assert.Equal([Up(2 * Notch)], h.Sends());
    }

    [Fact]
    public void ARawWheelOnlyClaimsAnEventOnTheSameAxis()
    {
        var h = new RouterHarness(a: Reverse);
        h.Hook(Notch, horizontal: false);
        h.Raw(MouseA, Notch, horizontal: true);
        Assert.Empty(h.Sends());

        h.Raw(MouseA, Notch, horizontal: false);
        Assert.Equal([Up(-Notch)], h.Sends());
    }

    [Fact]
    public void ARawWheelOnlyClaimsAnEventGoingTheSameWay()
    {
        var h = new RouterHarness(a: Reverse);
        h.Hook(Notch);
        h.Raw(MouseA, -Notch);
        Assert.Empty(h.Sends());

        h.Raw(MouseA, Notch);
        Assert.Equal([Up(-Notch)], h.Sends());
    }

    // MARK: Timeouts and the touchpad safety net

    [Fact]
    public void AnEventNobodyClaimsIsSentOnUnchangedAfter50ms()
    {
        var h = new RouterHarness(a: Reverse);
        Assert.Equal(HookAnswer.Swallow, h.Hook());
        Assert.Equal(h.Now + ScrollRouter.MatchWindow, h.Router.NextDeadline);

        h.Wait(49.9);
        h.Tick();
        Assert.Empty(h.Sends());

        h.Wait(0.2);
        h.Tick();
        Assert.Equal([Up()], h.Sends());
        Assert.False(h.Router.NeedsTicks);
        Assert.Null(h.Router.NextDeadline);
        Assert.Equal(1, h.Router.Stats.UnclaimedResends);
    }

    [Fact]
    public void ARawWheelThatWaitedTooLongDoesNotClaimALaterEvent()
    {
        var h = new RouterHarness(a: Reverse);
        h.Raw(MouseA);
        h.Wait(60);

        Assert.Equal(HookAnswer.Swallow, h.Hook());   // not decided on the spot: the raw has gone
        Assert.Empty(h.Sends());
        h.Run(60);
        Assert.Equal([Up()], h.Sends());              // unclaimed, so unchanged
        Assert.Equal(1, h.Router.Stats.UnmatchedRaws);
    }

    [Fact]
    public void ARawWheelJustInsideTheWindowStillClaims()
    {
        var h = new RouterHarness(a: Reverse);
        h.Hook();
        h.Wait(49);
        h.Raw(MouseA);
        Assert.Equal([Up(-Notch)], h.Sends());
    }

    [Fact]
    public void TouchpadHookFirst_SentUnchangedAtOnce_NeverThroughTheCore()
    {
        var h = new RouterHarness(a: Reverse);   // the pad itself has every feature on
        Assert.Equal(HookAnswer.Swallow, h.Hook());
        h.Raw(Pad);
        Assert.Equal([Up()], h.Sends());
        Assert.False(h.Router.NeedsTicks);       // no glide either
    }

    [Fact]
    public void TouchpadRawFirst_TheHookPassesIt()
    {
        var h = new RouterHarness(a: Reverse);
        Assert.Equal(HookAnswer.Pass, h.TurnRawFirst(Pad, -Notch));
        Assert.Empty(h.Sends());
    }

    [Fact]
    public void AnUnknownDeviceIsSentOnUnchanged()
    {
        var h = new RouterHarness(a: Reverse);
        Assert.Equal(HookAnswer.Swallow, h.Turn(Stranger));
        Assert.Equal([Up()], h.Sends());
        Assert.Equal(HookAnswer.Pass, h.TurnRawFirst(Stranger));
    }

    // MARK: Our own resends coming back

    [Fact]
    public void ARawWheelWithNoDeviceIsIgnored()
    {
        var h = new RouterHarness(a: Reverse);
        h.Hook();
        h.Raw(0);                                // our resend from earlier, coming back
        Assert.Empty(h.Sends());
        Assert.True(h.Router.NeedsTicks);        // the real event still waits for its device
        Assert.Equal(1, h.Router.Stats.IgnoredRaws);

        h.Raw(MouseA);
        Assert.Equal([Up(-Notch)], h.Sends());
    }

    [Fact]
    public void ARawWheelWithOurMarkerIsIgnored()
    {
        var h = new RouterHarness(a: Reverse);
        h.Hook();
        h.Raw(MouseA, extraInfo: ScrollRouter.ResendMarker);
        Assert.Empty(h.Sends());

        h.Run(60);
        Assert.Equal([Up()], h.Sends());        // nobody real claimed it
    }

    [Fact]
    public void IgnoredRawWheelsAreNotKeptForALaterHookEvent()
    {
        var h = new RouterHarness(a: Reverse);
        h.Raw(0);
        h.Raw(MouseA, extraInfo: ScrollRouter.ResendMarker);
        Assert.Equal(HookAnswer.Swallow, h.Hook());
        Assert.Empty(h.Sends());                 // waiting, not decided with a resend's "device"
        Assert.True(h.Router.NeedsTicks);
    }

    [Fact]
    public void AZeroDeltaHookEventPasses()
    {
        var h = new RouterHarness(a: Reverse);
        Assert.Equal(HookAnswer.Pass, h.Hook(0));
        Assert.False(h.Router.NeedsTicks);
    }
}

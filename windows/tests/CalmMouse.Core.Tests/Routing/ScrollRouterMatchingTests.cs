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
        Assert.Equal(h.Now + ScrollRouter.UnclaimedDeadline, h.Router.NextDeadline);

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
        h.Wait(ScrollRouter.PairWindow * 1000 + 1);

        Assert.Equal(HookAnswer.Swallow, h.Hook());   // not decided on the spot: the raw has gone
        Assert.Empty(h.Sends());
        h.Run(60);
        Assert.Equal([Up()], h.Sends());              // unclaimed, so unchanged
        Assert.Equal(1, h.Router.Stats.UnmatchedRaws);
    }

    [Fact]
    public void ALateRawWheelWithTheExactDeltaStillClaimsJustInsideTheDeadline()
    {
        var h = new RouterHarness(a: Reverse);
        h.Hook();
        h.Wait(49);
        h.Raw(MouseA);
        Assert.Equal([Up(-Notch)], h.Sends());
    }

    [Fact]
    public void AtExactlyTheDeadlineTheEventIsResentAndCanNoLongerBeClaimed()
    {
        var h = new RouterHarness(a: Reverse);
        h.Hook();
        double deadline = h.Router.NextDeadline!.Value;

        h.At(Math.BitDecrement(deadline));
        h.Tick();
        Assert.Empty(h.Sends());

        h.At(deadline);
        h.Raw(MouseA);                                  // too late by a hair
        Assert.Equal([Up()], h.Sends());                // resent unchanged, not reversed
        Assert.False(h.Router.NeedsTicks);
    }

    [Fact]
    public void ARawWheelThatCameFirstWaitsOnlyThePairWindow()
    {
        var h = new RouterHarness(a: Reverse);
        h.Raw(MouseA);
        h.Wait(9.9);
        Assert.Equal(HookAnswer.Swallow, h.Hook());
        Assert.Equal([Up(-Notch)], h.Sends());          // decided on the spot
        Assert.False(h.Router.NeedsTicks);
    }

    [Fact]
    public void ADifferentDeltaOnlyPairsWhenClose()
    {
        var h = new RouterHarness(a: Reverse);
        h.Hook(Notch);
        h.Wait(5);
        h.Raw(MouseA, 90);                              // same way, close: the same tick
        Assert.Equal([Up(-Notch)], h.Sends());          // the hook event's delta is what goes out
        Assert.Equal(1, h.Router.Stats.InexactPairs);

        h.Wait(100);
        h.Hook(Notch);
        h.Wait(15);
        h.Raw(MouseA, 90);                              // same way, but 15 ms apart: not paired
        Assert.Empty(h.Sends());
        Assert.True(h.Router.NeedsTicks);
    }

    [Fact]
    public void AMouseNotchDoesNotClaimATouchpadTickWaitingBeforeIt_HookFirst()
    {
        var h = new RouterHarness(a: Reverse);
        h.Hook(40);                                     // the touchpad: no Raw Input will come
        h.Wait(1);
        h.Hook(Notch);                                  // mouse A
        h.Wait(0.1);
        h.Raw(MouseA);
        Assert.Equal([Up(-Notch)], h.Sends());          // A's notch, reversed

        h.Run(60);
        Assert.Equal([Up(40)], h.Sends());              // the touchpad's tick, unchanged
    }

    [Fact]
    public void AMouseNotchDoesNotClaimATouchpadTickWaitingBeforeIt_RawFirst()
    {
        var h = new RouterHarness(a: Reverse);
        h.Hook(40);                                     // the touchpad
        h.Wait(20);
        h.Raw(MouseA);                                  // A's Raw Input first, then its hook event
        Assert.Empty(h.Sends());
        Assert.Equal(HookAnswer.Swallow, h.Hook());
        Assert.Equal([Up(-Notch)], h.Sends());

        h.Run(60);
        Assert.Equal([Up(40)], h.Sends());
    }

    [Fact]
    public void TwoMiceInterleavedRawFirstEachKeepTheirOwnSettings()
    {
        var h = new RouterHarness(a: Reverse);
        h.Raw(MouseA);
        h.Raw(MouseB);
        Assert.Equal(HookAnswer.Swallow, h.Hook());     // A's
        Assert.Equal([Up(-Notch)], h.Sends());
        Assert.Equal(HookAnswer.Pass, h.Hook());        // B's: nothing to change
        Assert.Empty(h.Sends());
    }

    [Fact]
    public void ALateRawWheelDoesNotShiftTheRestOfABurstFromTwoMice()
    {
        var h = new RouterHarness(a: Reverse);
        double start = h.Now;
        void At(double ms) => h.At(start + ms / 1000);

        At(0); h.Hook();                                // A, whose Raw Input will come 55 ms late
        At(25); h.Hook(); At(25.1); h.Raw(MouseB);      // B
        Assert.Equal([Up()], h.Sends());
        At(50.5); h.Tick();
        Assert.Equal([Up()], h.Sends());                // A's tick, unclaimed, unchanged
        At(51); h.Hook(); At(51.1); h.Raw(MouseA);      // A
        Assert.Equal([Up(-Notch)], h.Sends());
        At(55); h.Raw(MouseA);                          // the late one
        At(76); h.Hook(); At(76.1); h.Raw(MouseB);      // B
        Assert.Equal([Up()], h.Sends());
        At(101); h.Hook(); At(101.1); h.Raw(MouseA);    // A
        Assert.Equal([Up(-Notch)], h.Sends());
        At(126); h.Hook(); At(126.1); h.Raw(MouseB);    // B
        Assert.Equal([Up()], h.Sends());
    }

    [Fact]
    public void ATwinLaterThanTheDeadlineIsAbsorbed_NotPairedWithTheNextTick()
    {
        var h = new RouterHarness(a: Reverse);
        double start = h.Now;
        void At(double ms) => h.At(start + ms / 1000);

        At(0); h.Hook();                                // A's tick
        At(50); h.Tick();
        Assert.Equal([Up()], h.Sends());                // unclaimed: resent unchanged
        At(55); h.Raw(MouseA);                          // its Raw Input, very late
        At(58); Assert.Equal(HookAnswer.Swallow, h.Hook());   // a touchpad tick (no Raw Input)
        Assert.Empty(h.Sends());                        // not paired with A's late twin
        h.Run(60);
        Assert.Equal([Up()], h.Sends());                // the touchpad's tick, unchanged
    }

    [Fact]
    public void TheRecordOfAPassedTouchpadTickDoesNotTakeAMousesTwin()
    {
        var h = new RouterHarness(a: Reverse);
        double start = h.Now;
        void At(double ms) => h.At(start + ms / 1000);

        h.Router.SetPaused(true);
        At(0); Assert.Equal(HookAnswer.Pass, h.Hook()); // the touchpad, passed (no twin will come)
        At(2); h.Router.SetPaused(false);
        At(4); h.Hook(); At(4.1); h.Raw(MouseA);        // A's tick, a moment later
        Assert.Equal([Up(-Notch)], h.Sends());          // paired with A's own tick, the nearer one
        Assert.False(h.Router.NeedsTicks);
    }

    [Fact]
    public void ABurstBunchedTogetherPairsInOrder()
    {
        var h = new RouterHarness(a: Reverse);
        double start = h.Now;
        void At(double ms) => h.At(start + ms / 1000);

        // The input thread was held up: three hook events, then their three twins, all within half a millisecond.
        At(0); h.Hook();
        At(0.01); h.Hook();
        At(0.02); h.Hook();
        At(0.3); h.Raw(MouseA);
        At(0.31); h.Raw(MouseB);
        At(0.32); h.Raw(MouseA);
        Assert.Equal([Up(-Notch), Up(), Up(-Notch)], h.Sends());
    }

    [Fact]
    public void AHookEventWithABrokenClockReadingPassesWithoutWaiting()
    {
        var h = new RouterHarness(a: Reverse);
        Assert.Equal(HookAnswer.Pass, h.Router.HookWheel(double.PositiveInfinity, Notch, false, WinModifiers.None, false, Notepad));
        Assert.Equal(HookAnswer.Pass, h.Router.HookWheel(double.NaN, Notch, false, WinModifiers.None, false, Notepad));
        Assert.False(h.Router.NeedsTicks);
        Assert.Null(h.Router.NextDeadline);
    }

    [Fact]
    public void ARawWheelWithABrokenClockReadingIsIgnored()
    {
        var h = new RouterHarness(a: Reverse);
        h.Router.RawWheel(double.PositiveInfinity, MouseA, Notch, false);
        Assert.Equal(HookAnswer.Swallow, h.Hook());     // waits: not paired with the broken one
        Assert.Empty(h.Sends());
        h.Raw(MouseA);
        Assert.Equal([Up(-Notch)], h.Sends());
    }

    [Fact]
    public void RemovingAnotherMouseWithAnEventWaitingLeavesItWaiting()
    {
        var h = new RouterHarness(a: Reverse, b: Reverse);
        Assert.Equal(HookAnswer.Swallow, h.Hook());     // B's tick
        h.Router.RemoveDevice(MouseB);                  // B unplugged; A still reverses
        Assert.Empty(h.Sends());
        Assert.True(h.Router.NeedsTicks);
        h.Raw(MouseB);                                  // now an unknown device
        Assert.Equal([Up()], h.Sends());
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

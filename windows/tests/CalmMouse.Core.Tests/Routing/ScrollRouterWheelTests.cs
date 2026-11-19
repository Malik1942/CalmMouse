using CalmMouse.Core.Routing;
using static CalmMouse.Core.Tests.Routing.RouterHarness;

namespace CalmMouse.Core.Tests.Routing;

/// <summary>
/// The three wheel features, each mouse on its own settings: reverse, don't scroll while the
/// wheel is pressed, and smooth scrolling (its units, its totals, what stops it), plus the
/// per-app "don't smooth here" rule.
/// </summary>
public sealed class ScrollRouterWheelTests
{
    private static readonly WheelSettings Reverse = new(reverse: true);
    private static readonly WheelSettings Block = new(blockWhilePressed: true);
    private static readonly WheelSettings Smooth = new(smooth: true);

    // MARK: Reverse

    [Fact]
    public void ReverseFlipsOnlyThatMouse()
    {
        var h = new RouterHarness(a: Reverse);
        h.Turn(MouseA);
        Assert.Equal([Up(-Notch)], h.Sends());
        h.Turn(MouseB);
        Assert.Equal([Up(Notch)], h.Sends());
    }

    [Fact]
    public void ReverseFlipsBothDirectionsAndBothAxes()
    {
        var h = new RouterHarness(a: Reverse);
        h.Turn(MouseA, -Notch);
        Assert.Equal([Up(Notch)], h.Sends());
        h.Turn(MouseA, Notch, horizontal: true);
        Assert.Equal([Right(-Notch)], h.Sends());
        h.TurnRawFirst(MouseA, -2 * Notch, horizontal: true);
        Assert.Equal([Right(2 * Notch)], h.Sends());
    }

    // MARK: Don't scroll while the wheel is pressed

    [Fact]
    public void PressingOneMousesWheelDoesNotBlockTheOther()
    {
        var h = new RouterHarness(a: Block, b: Block);
        h.PressWheel(MouseA);

        Assert.Equal(HookAnswer.Swallow, h.Turn(MouseA));
        Assert.Empty(h.Sends());                       // dropped
        Assert.Equal(HookAnswer.Swallow, h.TurnRawFirst(MouseA));
        Assert.Empty(h.Sends());

        h.Turn(MouseB);
        Assert.Equal([Up()], h.Sends());               // B scrolls as usual
        Assert.Equal(HookAnswer.Pass, h.TurnRawFirst(MouseB));
    }

    [Fact]
    public void AfterReleasingTheWheelTheGraceRunsOutAndScrollingComesBack()
    {
        var h = new RouterHarness(a: Block);
        h.PressWheel(MouseA);
        h.Wait(500);
        h.ReleaseWheel(MouseA);

        h.Wait(100);                                   // inside the 0.15 s grace
        h.Turn(MouseA);
        Assert.Empty(h.Sends());

        h.Wait(100);
        h.Turn(MouseA);
        Assert.Equal([Up()], h.Sends());
    }

    [Fact]
    public void ALostWheelReleaseIsRecoveredWhenTheHookSeesTheButtonUp()
    {
        var h = new RouterHarness(a: Block);
        h.PressWheel(MouseA);
        h.Turn(MouseA);
        Assert.Empty(h.Sends());                       // pressed: blocked

        h.Wait(500);
        h.ReleaseWheelUnseen(MouseA);                  // the button-up never reaches Raw Input
        h.Turn(MouseA);
        Assert.Empty(h.Sends());                       // released now, with the usual grace
        h.Wait(20);
        h.Tick();
        Assert.Equal(1, h.Router.Stats.StuckWheelReleases);   // no button-up followed: it was lost

        h.Wait(200);
        h.Turn(MouseA);
        Assert.Equal([Up()], h.Sends());
    }

    [Fact]
    public void AHookAheadOfTheButtonUpIsNotCountedAsLost()
    {
        var h = new RouterHarness(a: Block);
        h.PressWheel(MouseA);
        h.Wait(500);
        h.ReleaseWheelUnseen(MouseA);                  // the hook sees the button up first...
        h.Turn(MouseA);
        h.Wait(2);
        h.Router.RawButton(h.Now, MouseA, RawMouseButton.Middle, down: false);   // ...then Raw Input does
        h.Wait(20);
        h.Tick();
        Assert.Equal(0, h.Router.Stats.StuckWheelReleases);
    }

    [Fact]
    public void AWheelReallyHeldDownStaysBlocked()
    {
        var h = new RouterHarness(a: Block);
        h.PressWheel(MouseA);
        for (int i = 0; i < 5; i++)
        {
            h.Wait(300);
            h.Turn(MouseA);
        }
        Assert.Empty(h.Sends());
        Assert.Equal(0, h.Router.Stats.StuckWheelReleases);
    }

    [Fact]
    public void OnlyTheMiddleButtonBlocks()
    {
        var h = new RouterHarness(a: Block);
        h.Router.RawButton(h.Now, MouseA, RawMouseButton.Left, down: true);
        h.Router.RawButton(h.Now, MouseA, RawMouseButton.X1, down: true);
        h.Turn(MouseA);
        Assert.Equal([Up()], h.Sends());
    }

    [Fact]
    public void APressedWheelBlocksNothingWhenTheSettingIsOff()
    {
        var h = new RouterHarness(a: Reverse);
        h.PressWheel(MouseA);
        h.Turn(MouseA);
        Assert.Equal([Up(-Notch)], h.Sends());
    }

    [Fact]
    public void PressingTheWheelStopsThatMousesGlideOnly()
    {
        var both = new WheelSettings(blockWhilePressed: true, smooth: true);
        var h = new RouterHarness(a: both, b: both);
        h.Turn(MouseA);
        h.Turn(MouseB, -Notch);
        h.Run(17);
        List<WheelSend> early = h.Sends();

        h.PressWheel(MouseA);
        List<WheelSend> rest = h.RunToIdle();
        Assert.All(rest, s => Assert.True(s.Delta < 0));   // only B's glide carries on
        Assert.Equal(-Notch, early.Concat(rest).Where(s => s.Delta < 0).Sum(s => s.Delta));
        Assert.True(early.Concat(rest).Where(s => s.Delta > 0).Sum(s => s.Delta) < Notch);
    }

    // MARK: Smoothing: units and totals

    [Theory]
    [InlineData(Notch, false, 1)]
    [InlineData(-Notch, false, 1)]
    [InlineData(Notch, true, 1)]
    [InlineData(-Notch, true, 1)]
    [InlineData(Notch, false, 3)]
    [InlineData(-Notch, true, 5)]
    public void AGlideAtTheDefaultSpeedCoversExactlyWhatTheNotchesWould(int delta, bool horizontal, int notches)
    {
        var h = new RouterHarness(a: Smooth);
        var sent = new List<WheelSend>();
        for (int i = 0; i < notches; i++)
        {
            Assert.Equal(HookAnswer.Swallow, h.Turn(MouseA, delta, horizontal));
            h.Run(20);
            sent.AddRange(h.Sends());
        }
        sent.AddRange(h.RunToIdle());

        Assert.True(sent.Count > notches);               // it glided: many small steps
        Assert.All(sent, s => Assert.Equal(horizontal, s.Horizontal));
        Assert.All(sent, s => Assert.Equal(Math.Sign(delta), Math.Sign(s.Delta)));
        Assert.Equal(delta * notches, sent.Sum(s => s.Delta));
    }

    [Fact]
    public void AGlideStartsSmallerThanANotch()
    {
        var h = new RouterHarness(a: Smooth);
        h.Turn(MouseA);
        Assert.Empty(h.Sends());                         // nothing until the first frame
        h.Run(Frame * 1000);
        int first = h.Sends().Sum(s => s.Delta);
        Assert.InRange(first, 1, Notch - 1);
    }

    [Fact]
    public void RawFirstGlidesTheSameWay()
    {
        var h = new RouterHarness(a: Smooth);
        Assert.Equal(HookAnswer.Swallow, h.TurnRawFirst(MouseA));
        Assert.Equal(Notch, h.RunToIdle().Sum(s => s.Delta));
    }

    [Fact]
    public void SpeedScalesTheGlide()
    {
        var h = new RouterHarness(a: new WheelSettings(smooth: true, speed: 6));
        h.Turn(MouseA);
        Assert.Equal(2 * Notch, h.RunToIdle().Sum(s => s.Delta));
    }

    [Fact]
    public void ReverseAndSmoothGlideTheOtherWay()
    {
        var h = new RouterHarness(a: new WheelSettings(reverse: true, smooth: true));
        h.Turn(MouseA);
        List<WheelSend> sent = h.RunToIdle();
        Assert.All(sent, s => Assert.True(s.Delta < 0));
        Assert.Equal(-Notch, sent.Sum(s => s.Delta));
    }

    [Fact]
    public void TwoMiceGlideSideBySideEachToItsOwnTotal()
    {
        var h = new RouterHarness(a: Smooth, b: Smooth);
        h.Turn(MouseA, Notch);
        h.Turn(MouseB, Notch, horizontal: true);
        List<WheelSend> sent = h.RunToIdle();
        Assert.Equal(Notch, sent.Where(s => !s.Horizontal).Sum(s => s.Delta));
        Assert.Equal(Notch, sent.Where(s => s.Horizontal).Sum(s => s.Delta));
    }

    [Fact]
    public void OnlyTheSmoothMouseGlides()
    {
        var h = new RouterHarness(a: Smooth);
        h.Turn(MouseB);
        Assert.Equal([Up()], h.Sends());
        Assert.False(h.Router.NeedsTicks);
    }

    [Fact]
    public void FlushSendsTheRestOfAGlideWholeAndStopsIt()
    {
        var h = new RouterHarness(a: Smooth, b: Smooth);
        h.Turn(MouseA);
        h.Turn(MouseA);
        h.Turn(MouseB, horizontal: true);
        h.Run(40);
        List<WheelSend> early = h.Sends();
        Assert.True(h.Router.NeedsTicks);
        Assert.True(early.Where(s => !s.Horizontal).Sum(s => s.Delta) < 2 * Notch);

        h.Router.Flush();
        List<WheelSend> sent = early.Concat(h.Sends()).ToList();
        Assert.False(h.Router.NeedsTicks);
        Assert.Empty(h.RunToIdle());
        // The same distance as letting the glides play out: nothing lost to the shutdown.
        Assert.Equal(2 * Notch, sent.Where(s => !s.Horizontal).Sum(s => s.Delta));
        Assert.Equal(Notch, sent.Where(s => s.Horizontal).Sum(s => s.Delta));
    }

    [Fact]
    public void FlushWithNothingGlidingSendsNothing()
    {
        var h = new RouterHarness(a: Reverse);
        h.Turn(MouseA);
        h.Sends();
        h.Router.Flush();
        Assert.Empty(h.Sends());
    }

    // MARK: Smoothing: modifiers

    [Theory]
    [InlineData(WinModifiers.Ctrl)]
    [InlineData(WinModifiers.Alt)]
    [InlineData(WinModifiers.Win)]
    public void AZoomTypeTickIsNeverSmoothed(WinModifiers mods)
    {
        var h = new RouterHarness(a: Smooth);
        Assert.Equal(HookAnswer.Swallow, h.Turn(MouseA, mods: mods));
        Assert.Equal([Up()], h.Sends());                 // one whole notch, at once
        Assert.False(h.Router.NeedsTicks);
        Assert.Equal(HookAnswer.Pass, h.TurnRawFirst(MouseA, mods: mods));
    }

    [Fact]
    public void ACtrlTickStopsARunningGlide()
    {
        var h = new RouterHarness(a: Smooth);
        h.Turn(MouseA);
        h.Run(17);
        int glided = h.Sends().Sum(s => s.Delta);
        Assert.InRange(glided, 1, Notch - 1);

        h.Turn(MouseA, mods: WinModifiers.Ctrl);
        Assert.Equal([Up()], h.Sends());                 // the zoom notch, unchanged
        Assert.False(h.Router.NeedsTicks);               // the rest of the glide is dropped
        Assert.Empty(h.RunToIdle());
    }

    [Fact]
    public void AZoomTickFromAnotherMouseStopsTheGlideToo()
    {
        var h = new RouterHarness(a: Smooth, b: Smooth);
        h.Turn(MouseA);
        h.Run(17);
        h.Sends();
        Assert.Equal(HookAnswer.Pass, h.TurnRawFirst(MouseB, mods: WinModifiers.Alt));
        Assert.False(h.Router.NeedsTicks);
    }

    [Fact]
    public void AShiftTickGoesOutAsOneWholeVerticalNotch_ForWindowsToTurnSideways()
    {
        var h = new RouterHarness(a: new WheelSettings(reverse: true, smooth: true));
        Assert.Equal(HookAnswer.Swallow, h.Turn(MouseA, mods: WinModifiers.Shift));
        Assert.Equal([Up(-Notch)], h.Sends());           // reversed, not smoothed, not swapped
        Assert.False(h.Router.NeedsTicks);
    }

    [Fact]
    public void AShiftTickStopsARunningGlide()
    {
        var h = new RouterHarness(a: Smooth, b: Smooth);
        h.Turn(MouseA);
        h.Run(17);
        h.Sends();
        h.Turn(MouseB, mods: WinModifiers.Shift);
        Assert.Equal([Up()], h.Sends());
        Assert.False(h.Router.NeedsTicks);
    }

    [Theory]
    [InlineData(WinModifiers.Ctrl)]
    [InlineData(WinModifiers.Alt)]
    [InlineData(WinModifiers.Win)]
    [InlineData(WinModifiers.Shift)]
    public void AKeyGoingDownMidGlideStopsEveryGlide(WinModifiers key)
    {
        // Ctrl+click right after scrolling: without this, the rest of the glide would go out
        // as Ctrl+wheel, one zoom step per frame.
        var h = new RouterHarness(a: Smooth, b: Smooth);
        h.Turn(MouseA);
        h.Turn(MouseB, horizontal: true);
        h.Run(17);
        int glided = h.Sends().Sum(s => s.Delta);
        Assert.InRange(glided, 1, 2 * Notch - 1);

        h.Held = key;
        h.Tick();
        Assert.Empty(h.Sends());
        Assert.False(h.Router.NeedsTicks);

        h.Held = WinModifiers.None;
        h.Run(500);
        Assert.Empty(h.Sends());
    }

    // MARK: Smoothing: the window the glide plays into

    [Theory]
    [InlineData("oldapp.exe", false)]   // "don't smooth here"
    [InlineData("figma.exe", false)]    // "leave scrolling alone here"
    [InlineData("Taskmgr.exe", true)]   // elevated
    public void AGlideStopsWhenTheCursorMovesOverAWindowWhereItMayNotPlay(string exe, bool elevated)
    {
        var h = new RouterHarness(a: Smooth);
        h.Router.SetAppRules([
            new WheelAppRule("oldapp.exe", "Old App") { NoSmoothing = true },
            new WheelAppRule("figma.exe", "Figma") { LeaveAlone = true },
        ]);
        h.Turn(MouseA);
        h.Run(17);
        Assert.NotEmpty(h.Sends());

        h.Router.PointerTargetChanged(new WheelTarget(exe, elevated));
        Assert.False(h.Router.NeedsTicks);
        h.Run(500);
        Assert.Empty(h.Sends());
    }

    [Fact]
    public void AGlideCarriesOnOverAnotherOrdinaryWindow()
    {
        var h = new RouterHarness(a: Smooth);
        h.Turn(MouseA);
        h.Run(17);
        List<WheelSend> early = h.Sends();
        h.Router.PointerTargetChanged(new WheelTarget("chrome.exe", Elevated: false));
        Assert.Equal(Notch, early.Concat(h.RunToIdle()).Sum(s => s.Delta));
    }

    [Fact]
    public void AnyWheelEventOverAWindowWhereGlidesMayNotPlayStopsThem()
    {
        var h = new RouterHarness(a: Smooth);
        h.Router.SetAppRules([new WheelAppRule("oldapp.exe", "Old App") { NoSmoothing = true }]);
        h.Turn(MouseA);
        h.Run(17);
        h.Sends();

        h.Target = new WheelTarget("oldapp.exe", Elevated: false);
        Assert.Equal(HookAnswer.Pass, h.Turn(Pad));      // a touchpad scroll there: passed, and tells us where the cursor is
        Assert.False(h.Router.NeedsTicks);
        h.Run(500);
        Assert.Empty(h.Sends());
    }

    [Fact]
    public void AddingANoSmoothingRuleForTheGlidesWindowStopsIt()
    {
        var h = new RouterHarness(a: Smooth);
        h.Turn(MouseA);
        h.Run(17);
        h.Router.SetAppRules([new WheelAppRule("notepad.exe", "Notepad") { NoSmoothing = true }]);
        Assert.False(h.Router.NeedsTicks);
    }

    // MARK: Smoothing: odd speeds, high-resolution wheels, turning round

    [Theory]
    [InlineData(double.NaN, Notch)]
    [InlineData(double.PositiveInfinity, Notch)]
    [InlineData(double.NegativeInfinity, Notch)]
    [InlineData(1e300, 4 * Notch)]                     // clamped to 12
    [InlineData(100, 4 * Notch)]
    [InlineData(0, Notch / 3)]                         // clamped to 1
    [InlineData(-5, Notch / 3)]
    [InlineData(6, 2 * Notch)]
    public void AnySpeedGlidesAFiniteDistance_ClampedToTheMacsRange(double speed, int expected)
    {
        var h = new RouterHarness(a: new WheelSettings(smooth: true, speed: speed));
        h.Turn(MouseA);
        Assert.Equal(expected, h.RunToIdle().Sum(s => s.Delta));
        Assert.Equal(0, h.Router.Stats.Faults);
    }

    [Theory]
    [InlineData(8, 15)]
    [InlineData(4, 30)]
    [InlineData(1, 120)]
    [InlineData(12, 10)]
    public void AHighResolutionWheelGlidesWhatItsTicksAddUpTo(int ticks, int delta)
    {
        var h = new RouterHarness(a: Smooth);
        var sent = new List<WheelSend>();
        for (int i = 0; i < ticks; i++)
        {
            h.Turn(MouseA, delta);
            h.Run(5);
            sent.AddRange(h.Sends());
        }
        sent.AddRange(h.RunToIdle());
        Assert.Equal(Notch, sent.Sum(s => s.Delta));
    }

    [Theory]
    [InlineData(1, 30, 10)]     // speed 1: a third of the distance, nothing lost to rounding
    [InlineData(10, 30, 100)]
    [InlineData(15, 8, 40)]
    public void AtTheLowestSpeedSlowTinyTicksStillAddUp(int delta, int ticks, int expected)
    {
        var h = new RouterHarness(a: new WheelSettings(smooth: true, speed: 1));
        int total = 0;
        for (int i = 0; i < ticks; i++)
        {
            h.Turn(MouseA, delta);
            total += h.RunToIdle().Sum(s => s.Delta);   // each glide finishes before the next tick
            h.Wait(300);
        }
        Assert.Equal(expected, total);
    }

    [Fact]
    public void TurningRoundDropsTheCarriedFraction()
    {
        var h = new RouterHarness(a: new WheelSettings(smooth: true, speed: 1));
        int total = 0;
        foreach (int delta in new[] { 1, 1, -1, -1, -1 })
        {
            h.Turn(MouseA, delta);
            total += h.RunToIdle().Sum(s => s.Delta);
            h.Wait(300);
        }
        Assert.Equal(-1, total);   // two thirds up dropped at the turn; three thirds down make one
    }

    [Fact]
    public void AGlideNeverStartsIntoAWindowWhereItMayNotPlay()
    {
        var h = new RouterHarness(a: Smooth, b: new WheelSettings(reverse: true, smooth: true));
        h.Router.SetAppRules([new WheelAppRule("oldapp.exe", "Old App") { NoSmoothing = true }]);

        Assert.Equal(HookAnswer.Swallow, h.Hook());          // over Notepad...
        h.Router.PointerTargetChanged(TaskManager);          // ...the cursor moves onto Task Manager...
        h.Raw(MouseA);                                       // ...and then A's Raw Input arrives
        Assert.Equal([Up()], h.Sends());                     // one whole notch, no glide
        Assert.False(h.Router.NeedsTicks);

        h.Hook();
        h.Router.PointerTargetChanged(new WheelTarget("oldapp.exe", Elevated: false));
        h.Raw(MouseB);
        Assert.Equal([Up(-Notch)], h.Sends());               // whole and still reversed
        Assert.False(h.Router.NeedsTicks);
    }

    [Fact]
    public void ASlowHighResolutionTickGlidesItsOwnSize()
    {
        var h = new RouterHarness(a: Smooth);
        h.Turn(MouseA, 15);
        Assert.Equal(15, h.RunToIdle().Sum(s => s.Delta));
    }

    [Fact]
    public void TurningRoundMidGlideGlidesTheNewWayItsFullDistance()
    {
        var h = new RouterHarness(a: Smooth);
        h.Turn(MouseA, Notch);
        h.Run(25);
        int before = h.Sends().Sum(s => s.Delta);
        Assert.InRange(before, 1, Notch - 1);

        h.Turn(MouseA, -Notch);
        List<WheelSend> after = h.RunToIdle();
        Assert.All(after, s => Assert.True(s.Delta < 0));
        Assert.Equal(-Notch, after.Sum(s => s.Delta));
    }

    // MARK: "Don't smooth in this app"

    [Fact]
    public void NoSmoothingAppGetsWholeNotches_OtherFeaturesStillApply()
    {
        var h = new RouterHarness(a: new WheelSettings(reverse: true, smooth: true));
        h.Router.SetAppRules([new WheelAppRule("OldApp.exe", "Old App") { NoSmoothing = true }]);
        h.Target = new WheelTarget("oldapp.exe", Elevated: false);

        Assert.Equal(HookAnswer.Swallow, h.Turn(MouseA));
        Assert.Equal([Up(-Notch)], h.Sends());
        Assert.False(h.Router.NeedsTicks);

        h.Target = Notepad;
        h.Turn(MouseA);
        Assert.Equal(-Notch, h.RunToIdle().Sum(s => s.Delta));   // glides again elsewhere
    }

    [Fact]
    public void NoSmoothingAppWithOnlySmoothingOnIsLeftAlone()
    {
        var h = new RouterHarness(a: Smooth);
        h.Router.SetAppRules([new WheelAppRule("oldapp.exe", "Old App") { NoSmoothing = true }]);
        h.Target = new WheelTarget("oldapp.exe", Elevated: false);
        Assert.False(h.Swallowing);
        Assert.Equal(HookAnswer.Pass, h.Turn(MouseA));
    }

    [Fact]
    public void ANewAppRuleAppliesToTheWindowAlreadyUnderTheCursor()
    {
        var h = new RouterHarness(a: Smooth);
        h.Target = new WheelTarget("oldapp.exe", Elevated: false);
        Assert.True(h.Swallowing);
        h.Router.SetAppRules([new WheelAppRule("oldapp.exe", "Old App") { NoSmoothing = true }]);
        Assert.False(h.Swallowing);
    }
}

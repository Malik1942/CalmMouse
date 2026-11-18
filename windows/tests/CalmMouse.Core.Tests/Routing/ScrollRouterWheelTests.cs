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
    public void ShiftIsLeftToWindows_TheGlideStaysVertical()
    {
        var h = new RouterHarness(a: Smooth);
        h.Turn(MouseA, mods: WinModifiers.Shift);
        List<WheelSend> sent = h.RunToIdle();
        Assert.All(sent, s => Assert.False(s.Horizontal));
        Assert.Equal(Notch, sent.Sum(s => s.Delta));
    }

    // MARK: "Don't smooth in this app"

    [Fact]
    public void NoSmoothingAppGetsWholeNotches_OtherFeaturesStillApply()
    {
        var h = new RouterHarness(a: new WheelSettings(reverse: true, smooth: true));
        h.Router.SetAppRules([new WheelAppRule("OldApp.exe", "Old App") { NoSmoothing = true }]);
        h.Router.ForegroundChanged("oldapp.exe", elevated: false);

        Assert.Equal(HookAnswer.Swallow, h.Turn(MouseA));
        Assert.Equal([Up(-Notch)], h.Sends());
        Assert.False(h.Router.NeedsTicks);

        h.Router.ForegroundChanged("notepad.exe", elevated: false);
        h.Turn(MouseA);
        Assert.Equal(-Notch, h.RunToIdle().Sum(s => s.Delta));   // glides again elsewhere
    }

    [Fact]
    public void NoSmoothingAppWithOnlySmoothingOnIsLeftAlone()
    {
        var h = new RouterHarness(a: Smooth);
        h.Router.SetAppRules([new WheelAppRule("oldapp.exe", "Old App") { NoSmoothing = true }]);
        h.Router.ForegroundChanged("oldapp.exe", elevated: false);
        Assert.False(h.Router.IsSwallowing);
        Assert.Equal(HookAnswer.Pass, h.Turn(MouseA));
    }

    [Fact]
    public void ANewAppRuleAppliesToTheAppAlreadyInFront()
    {
        var h = new RouterHarness(a: Smooth);
        h.Router.ForegroundChanged("oldapp.exe", elevated: false);
        Assert.True(h.Router.IsSwallowing);
        h.Router.SetAppRules([new WheelAppRule("oldapp.exe", "Old App") { NoSmoothing = true }]);
        Assert.False(h.Router.IsSwallowing);
    }
}

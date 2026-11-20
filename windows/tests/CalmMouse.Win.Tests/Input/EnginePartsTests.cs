using CalmMouse.Win.Input;

namespace CalmMouse.Win.Tests.Input;

/// <summary>The input thread's pure parts: glide frame rate, the watchdog's and the resend breaker's decisions, the wheel log ring.</summary>
public sealed class EnginePartsTests
{
    // MARK: Frame timing

    [Theory]
    [InlineData(60, 60)]
    [InlineData(120, 120)]
    [InlineData(144, 144)]
    [InlineData(165, 144)]
    [InlineData(240, 144)]
    [InlineData(50, 60)]
    [InlineData(0, 60)]      // DEVMODE: "hardware default"
    [InlineData(1, 60)]
    [InlineData(double.NaN, 60)]
    public void GlidesAtTheDisplayRateWithinLimits(double displayHz, double glideHz)
    {
        Assert.Equal(glideHz, FrameTiming.GlideHz(displayHz));
        Assert.Equal(1 / glideHz, FrameTiming.IntervalSeconds(displayHz), 12);
    }

    [Fact]
    public void ReadsDwmRatios()
    {
        Assert.Equal(59.94, FrameTiming.FromRatio(60000, 1001), 2);
        Assert.Equal(120, FrameTiming.FromRatio(120, 1));
        Assert.True(double.IsNaN(FrameTiming.FromRatio(60, 0)));
        Assert.True(double.IsNaN(FrameTiming.FromRatio(0, 1)));
    }

    [Fact]
    public void ThisPCReportsADisplayRate()
    {
        double hz = FrameTiming.ReadDisplayHz();
        // Headless CI machines may have no display mode to read; then the default applies.
        Assert.True(double.IsNaN(hz) || hz > 1, $"read {hz}");
    }

    // MARK: Watchdog

    [Fact]
    public void AHookThatAnswersIsNeverReinstalled()
    {
        var w = new HookWatchdog(silenceLimit: 0.75, minimumGap: 2);
        w.Started(0);
        for (double t = 0; t < 10; t += 0.01)
        {
            w.PhysicalInput(t);
            w.HookActivity(t + 0.0001);
            Assert.False(w.Check(t + 0.001));
        }
        Assert.Equal(double.PositiveInfinity, w.NextCheckAt);
    }

    [Fact]
    public void PhysicalInputTheHookNeverHearsMeansReinstall()
    {
        var w = new HookWatchdog(silenceLimit: 0.75, minimumGap: 2);
        w.Started(0);
        for (int i = 0; i <= 70; i++)
        {
            double t = 5 + i * 0.01;
            w.PhysicalInput(t);
            Assert.False(w.Check(t));
        }
        w.PhysicalInput(5.76);
        Assert.Equal(5.75, w.NextCheckAt, 9);
        Assert.True(w.Check(5.76));
        Assert.False(w.Check(5.77));   // once
    }

    [Fact]
    public void OneStrayRawEventIsNotEnough()
    {
        var w = new HookWatchdog(silenceLimit: 0.75, minimumGap: 2);
        w.Started(0);
        w.PhysicalInput(5);
        // Input has to keep coming through the whole limit, not just once at its start.
        Assert.False(w.Check(10));
    }

    [Fact]
    public void ARawEventAfterItsHookCallDoesNotKeepTheEngineAwake()
    {
        // Windows calls the hook first and delivers the Raw Input after, so when the mouse stops
        // the last event leaves the watchdog "silent" with nothing more coming. A check time in
        // the past would make the input thread spin; it must wait for more input instead.
        var w = new HookWatchdog(silenceLimit: 0.75, minimumGap: 2);
        w.Started(0);
        for (double t = 5; t < 6; t += 0.01)
        {
            w.HookActivity(t);
            w.PhysicalInput(t + 0.0001);
        }
        Assert.Equal(double.PositiveInfinity, w.NextCheckAt);
        Assert.False(w.Check(60));
        Assert.Equal(double.PositiveInfinity, w.NextCheckAt);
    }

    [Fact]
    public void AStrayRawEventDoesNotKeepTheEngineAwake()
    {
        var w = new HookWatchdog(silenceLimit: 0.75, minimumGap: 2);
        w.Started(0);
        w.PhysicalInput(5);
        Assert.Equal(double.PositiveInfinity, w.NextCheckAt);
    }

    [Fact]
    public void ReinstallsBackOffWhileTheHookStaysSilentAndResetWhenItAnswers()
    {
        var w = new HookWatchdog(silenceLimit: 0.5, minimumGap: 2, maximumGap: 8);
        w.Started(0);
        var reinstalls = new List<double>();
        for (int i = 0; i < 640; i++)
        {
            double t = i * 0.0625;   // exact in binary, so the gaps compare exactly
            w.PhysicalInput(t);
            if (w.Check(t)) { reinstalls.Add(Math.Round(t, 2)); }
        }
        // 2, then 4, then 8, then 8 apart: a hook that is silent for a good reason isn't hammered.
        Assert.Equal([2, 6, 14, 22, 30, 38], reinstalls);
        w.HookActivity(40);
        Assert.Equal(2, w.CurrentGap);
    }

    [Fact]
    public void AnExcusedSilenceStartsOver()
    {
        var w = new HookWatchdog(silenceLimit: 0.5, minimumGap: 0);
        w.Started(0);
        w.PhysicalInput(1);
        w.PhysicalInput(1.6);
        w.Excuse();
        Assert.False(w.Check(1.6));
        w.PhysicalInput(1.7);
        Assert.False(w.Check(1.7));
    }

    // MARK: Resend breaker

    [Fact]
    public void ResendsThatComeBackKeepTheBreakerClosed()
    {
        var b = new ResendBreaker(tripAfter: 3, echoTimeout: 0.25);
        for (int i = 0; i < 100; i++)
        {
            double t = i * 0.01;
            Assert.False(b.Sent(t, accepted: true));
            b.Echo(t + 0.001);
            Assert.Equal(0, b.Check(t + 0.002));
        }
        Assert.False(b.IsOpen);
        Assert.Equal(100, b.Echoes);
    }

    [Fact]
    public void RefusedResendsInARowTripItAndItTriesAgainLater()
    {
        var b = new ResendBreaker(tripAfter: 3, firstPause: 30, maximumPause: 100);
        Assert.False(b.Sent(1, accepted: false));
        Assert.False(b.Sent(1, accepted: false));
        Assert.True(b.Sent(1, accepted: false));
        Assert.True(b.IsOpen);
        Assert.Equal(31, b.OpenUntil);
        Assert.Equal(0, b.Check(30.9));
        Assert.Equal(-1, b.Check(31));
        Assert.False(b.IsOpen);
        // Still broken: open again, for longer.
        b.Sent(32, false);
        b.Sent(32, false);
        Assert.True(b.Sent(32, false));
        Assert.Equal(32 + 60, b.OpenUntil);
        Assert.Equal(2, b.Trips);
    }

    [Fact]
    public void ResendsThatNeverComeBackTripItToo()
    {
        var b = new ResendBreaker(tripAfter: 3, echoTimeout: 0.25);
        b.Sent(1.0, true);
        b.Sent(1.1, true);
        b.Sent(1.2, true);
        Assert.Equal(0, b.Check(1.2));
        Assert.Equal(1.25, b.NextCheckAt, 9);
        Assert.Equal(1, b.Check(1.5));
        Assert.True(b.IsOpen);
    }

    [Fact]
    public void AnEchoInBetweenStartsTheCountAgainAndResetsThePause()
    {
        var b = new ResendBreaker(tripAfter: 3, firstPause: 30);
        b.Sent(1, false);
        b.Sent(1, false);
        b.Sent(1.1, true);
        b.Echo(1.11);
        Assert.False(b.Sent(2, false));
        Assert.False(b.Sent(2, false));
        Assert.True(b.Sent(2, false));
        Assert.Equal(-1, b.Check(40));
        b.Sent(41, true);
        b.Echo(41.01);
        Assert.Equal(30, b.CurrentPause);
    }

    // MARK: Wheel log

    [Fact]
    public void TheWheelLogKeepsOrderAndDropsWhenFull()
    {
        var log = new WheelLog(capacity: 3);
        for (int i = 1; i <= 5; i++) { log.Write(new WheelLogEntry(WheelLogKind.RawWheel, i, i * 120, false, 1, 0, default, false)); }
        Assert.Equal(2, log.Dropped);
        var deltas = new List<int>();
        while (log.TryRead(out WheelLogEntry e)) { deltas.Add(e.Delta); }
        Assert.Equal([120, 240, 360], deltas);
        log.Write(new WheelLogEntry(WheelLogKind.Sent, 6, -120, true, 0, 0, default, false));
        Assert.True(log.TryRead(out WheelLogEntry last));
        Assert.Equal(-120, last.Delta);
        Assert.False(log.TryRead(out _));
    }
}

using CalmMouse.Win.Input;

namespace CalmMouse.Win.Tests.Input;

/// <summary>The input thread's pure parts: glide frame rate, the watchdog's decisions, the wheel log ring.</summary>
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
    public void HookActivityMeansNoPing()
    {
        var w = new HookWatchdog(interval: 3, pingTimeout: 0.5);
        w.Started(0);
        w.HookActivity(1);
        Assert.Equal(WatchdogAction.None, w.Check(3, userActive: true));
        w.HookActivity(4);
        Assert.Equal(WatchdogAction.None, w.Check(6, userActive: true));
    }

    [Fact]
    public void ChecksOnlyEveryInterval()
    {
        var w = new HookWatchdog(interval: 3, pingTimeout: 0.5);
        w.Started(0);
        Assert.Equal(WatchdogAction.None, w.Check(2.9, userActive: true));
        Assert.Equal(3, w.NextCheckAt);
        Assert.Equal(WatchdogAction.Ping, w.Check(3, userActive: true));
    }

    [Fact]
    public void AnIdlePCIsNeverPinged()
    {
        var w = new HookWatchdog(interval: 3, pingTimeout: 0.5);
        w.Started(0);
        for (double t = 3; t < 60; t += 3) { Assert.Equal(WatchdogAction.None, w.Check(t, userActive: false)); }
    }

    [Fact]
    public void ASilentHookWhileTheUserIsBusyGetsPingedAndASeenPingIsEnough()
    {
        var w = new HookWatchdog(interval: 3, pingTimeout: 0.5);
        w.Started(0);
        Assert.Equal(WatchdogAction.Ping, w.Check(3, userActive: true));
        w.PingSent(3);
        Assert.True(w.PingOutstanding);
        Assert.Equal(3.5, w.NextCheckAt);
        w.HookActivity(3.001);   // the ping came through
        Assert.Equal(WatchdogAction.None, w.Check(3.2, userActive: true));
        Assert.False(w.PingOutstanding);
    }

    [Fact]
    public void AMissedPingMeansReinstall()
    {
        var w = new HookWatchdog(interval: 3, pingTimeout: 0.5);
        w.Started(0);
        Assert.Equal(WatchdogAction.Ping, w.Check(3, userActive: true));
        w.PingSent(3);
        Assert.Equal(WatchdogAction.None, w.Check(3.4, userActive: true));
        Assert.Equal(WatchdogAction.Reinstall, w.Check(3.5, userActive: true));
        w.Started(3.5);
        Assert.False(w.PingOutstanding);
        Assert.Equal(WatchdogAction.None, w.Check(4, userActive: true));
    }

    [Fact]
    public void APingThatCouldNotBeSentIsNotWaitedFor()
    {
        var w = new HookWatchdog(interval: 3, pingTimeout: 0.5);
        w.Started(0);
        Assert.Equal(WatchdogAction.Ping, w.Check(3, userActive: true));
        w.PingNotSent();   // a locked desktop: SendInput refused
        Assert.Equal(WatchdogAction.None, w.Check(3.6, userActive: true));
        Assert.Equal(WatchdogAction.Ping, w.Check(6, userActive: true));
    }

    [Theory]
    [InlineData(0, 0, 1920)]
    [InlineData(1, 0, 1920)]
    [InlineData(959, 0, 1920)]
    [InlineData(1919, 0, 1920)]
    [InlineData(-1280, -1280, 3200)]   // a monitor left of the main one
    [InlineData(1919, -1280, 3200)]
    [InlineData(1079, 0, 1080)]
    public void ThePingLandsOnTheCursorsOwnPixel(int pixel, int origin, int size)
    {
        int n = HookWatchdog.AbsoluteCoordinate(pixel, origin, size);
        Assert.InRange(n, 0, 65535);
        // Windows: origin + n × size / 65536, rounded down.
        Assert.Equal(pixel, origin + (int)((long)n * size / 65536));
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

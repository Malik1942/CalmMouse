using CalmMouse.Win.Input;

namespace CalmMouse.Win.Tests.Input;

/// <summary>The per-process cache behind target resolution, with a fake process table.</summary>
public sealed class ProcessInfoCacheTests
{
    private readonly Dictionary<uint, ProcessInfo> _table = new()
    {
        [100] = new ProcessInfo("notepad.exe", false),
        [200] = new ProcessInfo("Taskmgr.exe", true),
    };

    private int _lookups;

    private ProcessInfo Describe(uint pid)
    {
        _lookups++;
        // A fresh string every time, as reading the process name really gives.
        return _table.TryGetValue(pid, out ProcessInfo info)
            ? info with { ExeName = info.ExeName is { } n ? new string(n.AsSpan()) : null }
            : new ProcessInfo(null, true);
    }

    [Fact]
    public void AHitWithinItsLifetimeDoesNotLookAgain()
    {
        var cache = new ProcessInfoCache(Describe, lifetime: 2);
        ProcessInfo first = cache.Get(100, 10);
        ProcessInfo again = cache.Get(100, 11.9);
        Assert.Equal(1, _lookups);
        Assert.Same(first.ExeName, again.ExeName);
        Assert.Equal("notepad.exe", again.ExeName);
        Assert.False(again.Elevated);
    }

    [Fact]
    public void AnExpiredEntryIsLookedUpAgainButKeepsItsString()
    {
        var cache = new ProcessInfoCache(Describe, lifetime: 2);
        ProcessInfo first = cache.Get(100, 10);
        ProcessInfo later = cache.Get(100, 12);
        Assert.Equal(2, _lookups);
        // Same name: the old string, so the router's by-reference rule cache keeps hitting.
        Assert.Same(first.ExeName, later.ExeName);
    }

    [Fact]
    public void AReusedProcessIdIsNoticedWhenTheEntryExpires()
    {
        var cache = new ProcessInfoCache(Describe, lifetime: 2);
        Assert.Equal("notepad.exe", cache.Get(100, 10).ExeName);
        _table[100] = new ProcessInfo("Taskmgr.exe", true);
        Assert.Equal("notepad.exe", cache.Get(100, 11).ExeName);
        ProcessInfo reused = cache.Get(100, 12.5);
        Assert.Equal("Taskmgr.exe", reused.ExeName);
        Assert.True(reused.Elevated);
    }

    [Fact]
    public void AClockGoingBackwardsLooksAgain()
    {
        var cache = new ProcessInfoCache(Describe, lifetime: 2);
        cache.Get(200, 10);
        cache.Get(200, 9);
        Assert.Equal(2, _lookups);
    }

    [Fact]
    public void AnUnreadableProcessCountsAsElevated()
    {
        var cache = new ProcessInfoCache(Describe);
        ProcessInfo info = cache.Get(999, 0);
        Assert.Null(info.ExeName);
        Assert.True(info.Elevated);
    }

    [Fact]
    public void ItStaysSmall()
    {
        var cache = new ProcessInfoCache(Describe, lifetime: 1);
        for (uint pid = 1; pid <= ProcessInfoCache.MaxEntries; pid++) { cache.Get(pid, 0); }
        Assert.Equal(ProcessInfoCache.MaxEntries, cache.Count);
        cache.Get(10_000, 5);   // everything else has expired by now
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void TheFrameHostIsRecognisedIgnoringCase()
    {
        Assert.True(new ProcessInfo("applicationframehost.exe", false).IsFrameHost);
        Assert.False(new ProcessInfo("explorer.exe", false).IsFrameHost);
        Assert.False(new ProcessInfo(null, false).IsFrameHost);
    }

    [Fact]
    public void WheelRoutingFollowsTheSetting()
    {
        Assert.Equal(WheelRouting.Focus, WheelRoutingSetting.FromSpi(0));
        Assert.Equal(WheelRouting.Hybrid, WheelRoutingSetting.FromSpi(1));
        Assert.Equal(WheelRouting.Hover, WheelRoutingSetting.FromSpi(2));
        Assert.Equal(WheelRouting.Hover, WheelRoutingSetting.FromSpi(null));
        Assert.Equal(WheelRouting.Hover, WheelRoutingSetting.FromSpi(7));
    }

    // MARK: Against this PC

    [Fact]
    public void DescribesThisProcessAsReachable()
    {
        ProcessInfo self = ProcessDescriber.Describe((uint)Environment.ProcessId);
        Assert.EndsWith(".exe", self.ExeName, StringComparison.OrdinalIgnoreCase);
        Assert.False(self.Elevated);
    }

    [Fact]
    public void TheSystemProcessAndNoWindowAreLeftAlone()
    {
        Assert.True(ProcessDescriber.Describe(4).Elevated);
        Assert.True(ProcessDescriber.Describe(0).Elevated);
        Assert.Equal(WheelTargetResolver.Unknown, new WheelTargetResolver().ForWindow(0, 0));
    }
}

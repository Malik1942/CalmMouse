// Translated from Tests/CalmMouseCoreTests/SupportInviteTests.swift. Date → DateTimeOffset.

namespace CalmMouse.Core.Tests;

/// <summary>The one-time "Star on GitHub?" line in the menu: earned, shown once, never nagging.</summary>
public sealed class SupportInviteTests
{
    private const double day = 86_400;
    private readonly DateTimeOffset installed = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    private bool visible(int blocked, double daysIn, double? shownDaysAgo = null, bool done = false)
    {
        DateTimeOffset now = installed + TimeSpan.FromSeconds(daysIn * day);
        return SupportInvite.IsVisible(blockedTotal: blocked, firstLaunch: installed,
                                       firstShown: shownDaysAgo is double ago ? now + TimeSpan.FromSeconds(-ago * day) : null,
                                       done: done, now: now);
    }

    [Fact]
    public void testNeedsAThousandBlockedScrolls()
    {
        Assert.False(visible(blocked: 999, daysIn: 30));
        Assert.True(visible(blocked: 1_000, daysIn: 30));
    }

    [Fact]
    public void testNeverInTheFirstWeek()
    {
        // Blocked scrolls are counted per event, so a busy first day can pass a thousand.
        Assert.False(visible(blocked: 5_000, daysIn: 6.9));
        Assert.True(visible(blocked: 5_000, daysIn: 7));
    }

    [Fact]
    public void testGoneForGoodOnceAnswered()
    {
        Assert.False(visible(blocked: 5_000, daysIn: 30, done: true));
        Assert.False(visible(blocked: 5_000, daysIn: 30, shownDaysAgo: 1, done: true));
    }

    [Fact]
    public void testFadesAWeekAfterItFirstShowed()
    {
        Assert.True(visible(blocked: 5_000, daysIn: 30, shownDaysAgo: 6.9));
        Assert.False(visible(blocked: 5_000, daysIn: 30, shownDaysAgo: 7));
    }
}

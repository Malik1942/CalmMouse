// Translated from Tests/CalmMouseCoreTests/AppVersionTests.swift.

namespace CalmMouse.Core.Tests;

public sealed class AppVersionTests
{
    [Fact]
    public void testNewerAcrossEachComponent()
    {
        Assert.True(AppVersion.IsNewer("0.5.0", "0.4.0"));
        Assert.True(AppVersion.IsNewer("0.4.1", "0.4.0"));
        Assert.True(AppVersion.IsNewer("1.0.0", "0.9.9"));
    }

    [Fact]
    public void testOlderAndEqualAreNotNewer()
    {
        Assert.False(AppVersion.IsNewer("0.4.0", "0.5.0"));
        Assert.False(AppVersion.IsNewer("0.5.0", "0.5.0"));
    }

    [Fact]
    public void testTagPrefixAndPaddingAreTolerated()
    {
        Assert.True(AppVersion.IsNewer("v0.5.0", "0.4.0"), "git tags carry a leading v");
        Assert.False(AppVersion.IsNewer("0.5", "0.5.0"), "0.5 IS 0.5.0");
        Assert.True(AppVersion.IsNewer("0.5.1", "0.5"));
    }

    [Fact]
    public void testNumericNotLexicographicComparison()
    {
        Assert.True(AppVersion.IsNewer("0.10.0", "0.9.0"), "10 > 9, not \"1\" < \"9\"");
    }

    [Fact]
    public void testJunkNeverLooksNewer()
    {
        // A garbled feed must never trigger an "update available" nag.
        Assert.False(AppVersion.IsNewer("", "0.5.0"));
        Assert.False(AppVersion.IsNewer("banana", "0.5.0"));
        Assert.True(AppVersion.IsNewer("0.5.1-beta", "0.5.0"), "suffixes keep their digits");
    }
}

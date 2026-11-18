// Translated from Tests/CalmMouseCoreTests/DeviceRulesTests.swift. xUnit's Assert.Equal takes no
// message, so the Swift messages on those asserts are kept as comments.

namespace CalmMouse.Core.Tests;

/// <summary>
/// Which kind a device ends up handled as once the user has corrected it, and which other
/// mouse utilities overlap what CalmMouse is doing right now.
/// </summary>
public sealed class DeviceRulesTests
{
    // MARK: Treat as

    [Fact]
    public void testAutomaticKeepsWhatWasDetected()
    {
        Assert.Equal((MouseKind?)MouseKind.MagicMouse, MouseClassifier.EffectiveKind(detected: MouseKind.MagicMouse, @override: MouseTypeSetting.Automatic));
        Assert.Equal((MouseKind?)MouseKind.WheelMouse, MouseClassifier.EffectiveKind(detected: MouseKind.WheelMouse, @override: MouseTypeSetting.Automatic));
        Assert.Null(MouseClassifier.EffectiveKind(detected: null, @override: MouseTypeSetting.Automatic));
    }

    [Fact]
    public void testTreatAsMagicMouseWinsWhateverWasDetected()
    {
        foreach (MouseKind? detected in new MouseKind?[] { MouseKind.MagicMouse, MouseKind.WheelMouse, null })
        {
            Assert.Equal((MouseKind?)MouseKind.MagicMouse, MouseClassifier.EffectiveKind(detected: detected, @override: MouseTypeSetting.MagicMouse));
        }
    }

    [Fact]
    public void testTreatAsWheelLeavesARealMagicMouseAlone()
    {
        // The event path does the same: a Magic Mouse under "wheel mouse" is not managed.
        Assert.Null(MouseClassifier.EffectiveKind(detected: MouseKind.MagicMouse, @override: MouseTypeSetting.WheelMouse));
        Assert.Equal((MouseKind?)MouseKind.WheelMouse, MouseClassifier.EffectiveKind(detected: MouseKind.WheelMouse, @override: MouseTypeSetting.WheelMouse));
        Assert.Equal((MouseKind?)MouseKind.WheelMouse, MouseClassifier.EffectiveKind(detected: null, @override: MouseTypeSetting.WheelMouse));
    }

    // MARK: Other mouse utilities

    private static MouseUtilityConflicts.RunningApp app(string? bundleID, string? name = null) =>
        new(bundleID: bundleID, name: name);

    private static string[] names(MouseUtilityConflicts.RunningApp[] running,
                                  HashSet<MouseUtilityArea> active) =>
        MouseUtilityConflicts.Conflicts(running: running, active: active).Select(u => u.Name).ToArray();

    [Fact]
    public void testAUtilityCountsOnlyWhereCalmMouseIsBusyToo()
    {
        MouseUtilityConflicts.RunningApp[] mos = [app("com.caldis.Mos")];
        Assert.Equal(["Mos"], names(mos, [MouseUtilityArea.WheelScrolling]));
        // "Mos only touches wheels"
        Assert.Equal([], names(mos, [MouseUtilityArea.MagicMouseScrolling, MouseUtilityArea.PointerSpeed]));
        Assert.Equal([], names(mos, []));
    }

    [Fact]
    public void testABackgroundHelperIsEnough()
    {
        Assert.Equal(["Mac Mouse Fix"],
                     names([app("com.nuebling.mac-mouse-fix.helper")], [MouseUtilityArea.WheelScrolling]));
    }

    [Fact]
    public void testAnAppAndItsHelperAreListedOnce()
    {
        MouseUtilityConflicts.RunningApp[] running = [app("com.nuebling.mac-mouse-fix"), app("com.nuebling.mac-mouse-fix.helper")];
        Assert.Equal(["Mac Mouse Fix"], names(running, [MouseUtilityArea.WheelScrolling]));
    }

    [Fact]
    public void testClosedSourceUtilitiesAreFoundByName()
    {
        Assert.Equal(["Logi Options+"],
                     names([app("com.example.agent", "logioptionsplus_agent")], [MouseUtilityArea.PointerSpeed]));
        Assert.Equal(["BetterTouchTool"], names([app(null, "BetterTouchTool")], [MouseUtilityArea.MagicMouseScrolling]));
    }

    [Fact]
    public void testUnrelatedAppsAreIgnored()
    {
        MouseUtilityConflicts.RunningApp[] running = [app("com.apple.Safari", "Safari"), app("com.calmmouse.app", "CalmMouse")];
        Assert.Equal([], names(running, [.. MouseUtilityArea.AllCases]));
    }
}

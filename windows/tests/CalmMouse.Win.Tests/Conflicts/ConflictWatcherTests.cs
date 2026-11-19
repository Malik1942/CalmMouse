using CalmMouse.Core;
using CalmMouse.Win.Conflicts;
using CalmMouse.Win.Localization;
using static CalmMouse.Core.MouseUtilityConflicts;

namespace CalmMouse.Win.Tests.Conflicts;

public sealed class ConflictWatcherTests
{
    private static readonly IReadOnlySet<MouseUtilityArea> Wheel = new HashSet<MouseUtilityArea> { MouseUtilityArea.WheelScrolling };
    private static readonly IReadOnlySet<MouseUtilityArea> Nothing = new HashSet<MouseUtilityArea>();

    private static IReadOnlyList<string> Found(IReadOnlySet<MouseUtilityArea> active, params string[] processNames) =>
        MouseUtilityConflicts.Conflicts(WindowsMouseUtilities.Known, processNames.Select(WindowsMouseUtilities.FromProcessName).ToList(), active)
            .Select(u => u.Name).ToList();

    [Fact]
    public void TheListIsTheSixToolsThatChangeScrolling()
    {
        Assert.Equal(["SmoothScroll", "X-Mouse Button Control", "Logi Options+", "Razer Synapse", "Magic Utilities", "AutoHotkey"],
            WindowsMouseUtilities.Known.Select(u => u.Name));
        Assert.All(WindowsMouseUtilities.Known, u => Assert.Equal([MouseUtilityArea.WheelScrolling], u.Areas));
    }

    [Fact]
    public void NoNameFragmentIsEmptyAndAllAreLowerCase()
    {
        // .NET finds "" in every string (Foundation doesn't), so an empty fragment would match every process.
        foreach (MouseUtility utility in WindowsMouseUtilities.Known)
        {
            Assert.All(utility.NameMatches, m => Assert.False(string.IsNullOrWhiteSpace(m), $"{utility.Name} has an empty fragment"));
            Assert.All(utility.NameMatches, m => Assert.Equal(m.ToLowerInvariant(), m));
            Assert.All(utility.BundleIDs, id => Assert.Equal(id.ToLowerInvariant(), id));
            Assert.All(utility.BundleIDs, id => Assert.EndsWith(".exe", id));
        }
    }

    [Theory]
    [InlineData("SmoothScroll", "SmoothScroll")]
    [InlineData("XMouseButtonControl", "X-Mouse Button Control")]
    [InlineData("logioptionsplus_agent", "Logi Options+")]
    [InlineData("LogiOptionsPlus", "Logi Options+")]
    [InlineData("Razer Synapse 3", "Razer Synapse")]
    [InlineData("Razer Synapse Service", "Razer Synapse")]
    [InlineData("RazerAppEngine", "Razer Synapse")]
    [InlineData("RzSynapse", "Razer Synapse")]
    [InlineData("MagicUtilities_Service", "Magic Utilities")]
    [InlineData("MagicMouseUtilities", "Magic Utilities")]
    [InlineData("AutoHotkey", "AutoHotkey")]
    [InlineData("AutoHotkey64", "AutoHotkey")]
    [InlineData("AutoHotkeyU64", "AutoHotkey")]
    public void FindsEachToolByItsProcessName(string processName, string tool)
    {
        Assert.Equal([tool], Found(Wheel, "explorer", processName, "svchost"));
    }

    [Fact]
    public void OrdinaryProcessesAreNotConflicts()
    {
        Assert.Empty(Found(Wheel, "explorer", "svchost", "chrome", "Code", "msedge", "RazerCortex", "Logitech G HUB", "System", "Idle", ""));
    }

    [Fact]
    public void NothingCountsWhileCalmMouseChangesNothing()
    {
        Assert.Empty(Found(Nothing, "SmoothScroll", "AutoHotkey64"));
    }

    [Fact]
    public void EachToolIsListedOnceInListOrder()
    {
        Assert.Equal(["SmoothScroll", "AutoHotkey"], Found(Wheel, "AutoHotkey64", "AutoHotkey", "SmoothScroll"));
    }

    [Fact]
    public void CalmMouseChangesScrollingOnlyWhenAWheelOptionIsOnAndItIsNotPaused()
    {
        Assert.Empty(WindowsMouseUtilities.ActiveAreas(false, []));
        Assert.Empty(WindowsMouseUtilities.ActiveAreas(false, [new WheelSettings(), new WheelSettings(speed: 8)]));
        Assert.Equal(Wheel, WindowsMouseUtilities.ActiveAreas(false, [new WheelSettings(), new WheelSettings(reverse: true)]));
        Assert.Equal(Wheel, WindowsMouseUtilities.ActiveAreas(false, [new WheelSettings(smooth: true)]));
        Assert.Equal(Wheel, WindowsMouseUtilities.ActiveAreas(false, [new WheelSettings(blockWhilePressed: true)]));
        Assert.Empty(WindowsMouseUtilities.ActiveAreas(true, [new WheelSettings(smooth: true)]));
    }

    [Fact]
    public void TheWatcherReportsChanges()
    {
        string[] running = ["explorer"];
        IReadOnlySet<MouseUtilityArea> active = Wheel;
        using var watcher = new ConflictWatcher(() => active, () => running.Select(WindowsMouseUtilities.FromProcessName).ToList(), interval: TimeSpan.FromHours(1));
        int changes = 0;
        watcher.ConflictsChanged += (_, _) => changes++;

        watcher.Refresh();
        Assert.Empty(watcher.Conflicts);
        Assert.Null(watcher.Message);
        Assert.Equal(0, changes);

        running = ["explorer", "SmoothScroll"];
        watcher.Refresh();
        Assert.Equal(["SmoothScroll"], watcher.Conflicts.Select(c => c.Name));
        Assert.Equal(1, changes);
        watcher.Refresh();
        Assert.Equal(1, changes);   // same as before

        active = Nothing;           // every wheel option turned off
        watcher.Refresh();
        Assert.Empty(watcher.Conflicts);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void TheNoticeNamesEveryTool()
    {
        IReadOnlyList<MouseUtility> two = [WindowsMouseUtilities.Known[0], WindowsMouseUtilities.Known[5]];
        string expected = Localizer.Shared.Get("%@ can also change how your mouse scrolls. If scrolling feels odd, turn off one of them.",
            Localizer.Shared.JoinList(["SmoothScroll", "AutoHotkey"]));
        Assert.Equal(expected, ConflictWatcher.MessageFor(two));
        Assert.Contains("SmoothScroll", expected);
        Assert.Contains("AutoHotkey", expected);

        var english = new Localizer(Localizer.LoadEmbeddedTables(), UiLanguage.English);
        Assert.Equal("SmoothScroll and AutoHotkey can also change how your mouse scrolls. If scrolling feels odd, turn off one of them.",
            english.Get("%@ can also change how your mouse scrolls. If scrolling feels odd, turn off one of them.", english.JoinList(["SmoothScroll", "AutoHotkey"])));
        var hant = new Localizer(Localizer.LoadEmbeddedTables(), UiLanguage.TraditionalChinese);
        Assert.Equal("SmoothScroll 和 AutoHotkey 也可能改變滑鼠的捲動方式。如果捲動起來怪怪的，請關閉其中一個。",
            hant.Get("%@ can also change how your mouse scrolls. If scrolling feels odd, turn off one of them.", hant.JoinList(["SmoothScroll", "AutoHotkey"])));
    }

    [Fact]
    public void ReadsTheRealProcessList()
    {
        IReadOnlyList<RunningApp> apps = ConflictWatcher.RunningProcesses();
        Assert.Contains(apps, a => a.BundleID == "explorer.exe" || a.BundleID == "testhost.exe" || a.BundleID == "dotnet.exe" || a.Name == System.Diagnostics.Process.GetCurrentProcess().ProcessName);
    }
}

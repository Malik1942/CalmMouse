using System.IO;
using CalmMouse.Core;
using CalmMouse.Core.Routing;
using CalmMouse.Win.Input;
using CalmMouse.Win.Settings;
using CalmMouse.Win.SettingsUI;

namespace CalmMouse.Win.Tests.SettingsUI;

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly TempStore _temp = new();
    private readonly FakeHost _host = new();
    private readonly FakeApps _apps = new();

    private SettingsStore Store => _temp.Store;

    private static MouseDeviceInfo Wheel(long id, string key, string name) => new(id, DeviceKind.WheelMouse, key, name);

    private SettingsViewModel Model() => new(Store, _host, _apps, action => action());

    public void Dispose() => _temp.Dispose();

    // MARK: General

    [Fact]
    public void ALineUnderStartAtSignInOnlyWhenWindowsWontDoIt()
    {
        using SettingsViewModel model = Model();
        Assert.False(model.HasLaunchAtLoginNote);

        _host.LaunchAtLoginNote = "Turned off in Task Manager";
        model.RefreshStatus();
        Assert.True(model.HasLaunchAtLoginNote);
        Assert.Equal("Turned off in Task Manager", model.LaunchAtLoginNote);

        _host.LaunchAtLoginNote = null;
        model.RefreshStatus();
        Assert.False(model.HasLaunchAtLoginNote);
    }

    // MARK: Devices

    [Fact]
    public void OneCardPerMouseWheelMiceFirstThenUnknownThenTouchpads()
    {
        MouseDeviceInfo[] devices =
        [
            new(1, DeviceKind.Touchpad, "04F3:3282:", "ELAN Touchpad"),
            Wheel(2, "3554:F58A:2.4G Mouse", "2.4G Mouse"),
            new(3, DeviceKind.Unknown, "", "HID-compliant mouse"),
            Wheel(4, "046D:B015:M720", "Logitech M720"),
            Wheel(5, "3554:F58A:2.4G Mouse", "2.4G Mouse"),   // the same mouse listed twice by its receiver
            new(6, DeviceKind.Unknown, "", "HID-compliant mouse"),   // no key: told apart by handle
        ];
        IReadOnlyList<MouseDeviceInfo> cards = SettingsViewModel.CardsFor(devices);
        Assert.Equal([2L, 4, 3, 6, 1], cards.Select(c => c.DeviceId));
    }

    [Fact]
    public void CardsFollowTheEngineAsMiceComeAndGo()
    {
        _host.Devices = [Wheel(1, "A", "Alpha")];
        using SettingsViewModel model = Model();
        DeviceCardViewModel alpha = Assert.Single(model.Devices);
        Assert.False(model.HasNoDevices);

        _host.ChangeDevices(Wheel(1, "A", "Alpha"), Wheel(2, "B", "Beta"), new MouseDeviceInfo(3, DeviceKind.Touchpad, "T", "Pad"));
        Assert.Equal(["Alpha", "Beta", "Pad"], model.Devices.Select(c => c.Name));
        Assert.Same(alpha, model.Devices[0]);   // kept, not rebuilt (keeps the keyboard focus)
        Assert.True(model.Devices[2].IsTouchpad);
        Assert.False(model.Devices[2].IsWheelMouse);

        _host.ChangeDevices();
        Assert.Empty(model.Devices);
        Assert.True(model.HasNoDevices);
    }

    [Fact]
    public void EveryMouseStartsWithEverythingOff()
    {
        _host.Devices = [Wheel(1, "A", "Alpha")];
        using SettingsViewModel model = Model();
        DeviceCardViewModel card = model.Devices[0];
        Assert.False(card.Smooth);
        Assert.False(card.Reverse);
        Assert.False(card.BlockWhilePressed);
        Assert.Equal(ScrollRouter.DefaultSpeed, card.Speed);
        Assert.Empty(Store.Current.Mice);   // looking changes nothing
    }

    [Fact]
    public void AChangeIsSavedForThatMouseOnlyAndTheEngineIsTold()
    {
        _host.Devices = [Wheel(1, "A", "Alpha"), Wheel(2, "B", "Beta")];
        using SettingsViewModel model = Model();
        int engineReloads = 0;
        Store.Changed += (_, _) => engineReloads++;

        model.Devices[0].Smooth = true;
        model.Devices[0].Reverse = true;
        model.Devices[1].BlockWhilePressed = true;

        Assert.Equal(3, engineReloads);
        Assert.Equal(new WheelSettings(reverse: true, smooth: true), Store.SettingsFor("A", "Alpha"));
        Assert.Equal(new WheelSettings(blockWhilePressed: true), Store.SettingsFor("B", "Beta"));
        Assert.Equal("Alpha", Store.Current.Mice["A"].DisplayName);

        Store.Flush();
        string json = File.ReadAllText(Store.Path);
        Assert.Contains("\"A\"", json);
        Assert.Contains("\"smooth\": true", json);

        // Setting the same value again writes nothing.
        model.Devices[0].Smooth = true;
        Assert.Equal(3, engineReloads);
    }

    [Fact]
    public void SpeedStaysInTheRoutersRangeInQuarterSteps()
    {
        _host.Devices = [Wheel(1, "A", "Alpha")];
        using SettingsViewModel model = Model();
        DeviceCardViewModel card = model.Devices[0];
        card.Speed = 40;
        Assert.Equal(ScrollRouter.MaximumSpeed, Store.SettingsFor("A").Speed);
        card.Speed = 0;
        Assert.Equal(ScrollRouter.MinimumSpeed, card.Speed);
        card.Speed = 4.6;
        Assert.Equal(4.5, card.Speed);
        Assert.Equal(1, DeviceCardViewModel.MinimumSpeed);
        Assert.Equal(12, DeviceCardViewModel.MaximumSpeed);
    }

    [Fact]
    public void ACardShowsChangesMadeElsewhere()
    {
        _host.Devices = [Wheel(1, "A", "Alpha")];
        using SettingsViewModel model = Model();
        var changed = new List<string?>();
        model.Devices[0].PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Store.UpdateMouse("A", "Alpha", w => w with { Smooth = true, Speed = 7 });
        Assert.True(model.Devices[0].Smooth);
        Assert.Equal(7, model.Devices[0].Speed);
        Assert.Equal([nameof(DeviceCardViewModel.Smooth), nameof(DeviceCardViewModel.Speed)], changed);
    }

    [Fact]
    public void AMouseWithoutSettingsStartsFromASameNamedOne()
    {
        Store.UpdateMouse("USB:Alpha", "Alpha", w => w with { Reverse = true });
        _host.Devices = [Wheel(1, "Bluetooth:Alpha", "Alpha")];
        using SettingsViewModel model = Model();
        Assert.True(model.Devices[0].Reverse);
    }

    // MARK: General

    [Fact]
    public void PauseGoesThroughTheAppAsTheTraysDoes()
    {
        using SettingsViewModel model = Model();
        Assert.False(model.Paused);
        model.Paused = true;
        Assert.Equal([true], _host.PauseRequests);
        Store.SetPaused(true);   // what the app does in answer
        Assert.True(model.Paused);
        Store.SetPaused(false);  // the tray's Resume
        Assert.False(model.Paused);
        Assert.Equal([true], _host.PauseRequests);
    }

    [Fact]
    public void LaunchAtLoginAndLanguageAreSaved()
    {
        using SettingsViewModel model = Model();
        Assert.False(model.LaunchAtLogin);
        Assert.Equal(AppLanguage.System, model.Language);
        model.LaunchAtLogin = true;
        model.Language = AppLanguage.TraditionalChinese;
        Assert.True(Store.Current.LaunchAtLogin);
        Assert.Equal(AppLanguage.TraditionalChinese, Store.Current.Language);
        Assert.Equal([AppLanguage.System, AppLanguage.English, AppLanguage.SimplifiedChinese, AppLanguage.TraditionalChinese],
                     model.LanguageOptions.Select(o => o.Value));
        Assert.Equal(["English", "简体中文", "繁體中文"], model.LanguageOptions.Skip(1).Select(o => o.Name));
    }

    [Fact]
    public void ProblemAndConflictLinesShowOnlyWhenThereIsSomethingToSay()
    {
        using SettingsViewModel model = Model();
        Assert.False(model.HasProblem);
        Assert.False(model.HasConflict);

        _host.Health = new EngineHealth("It stopped.", CanRetry: true);
        model.RefreshStatus();
        Assert.True(model.HasProblem);
        Assert.True(model.CanRetry);
        Assert.Equal("It stopped.", model.ProblemText);
        model.RetryCommand.Execute(null);
        Assert.Equal(1, _host.Retries);

        _host.Health = new EngineHealth("Blocked for now.", CanRetry: false);
        model.RefreshStatus();
        Assert.False(model.CanRetry);

        _host.ChangeConflicts("SmoothScroll can also change how your mouse scrolls.");
        Assert.True(model.HasConflict);
        _host.ChangeConflicts(null);
        Assert.False(model.HasConflict);
    }

    [Fact]
    public void DisposingLetsGoOfTheEngine()
    {
        SettingsViewModel model = Model();
        Assert.Equal(1, _host.DevicesChangedHandlers);
        model.Dispose();
        Assert.Equal(0, _host.DevicesChangedHandlers);
        Store.SetPaused(true);   // no longer followed
        Assert.False(model.Paused);
    }

    // MARK: App rules

    [Fact]
    public void ANewRuleIsOnAndDoesntSmooth()
    {
        using SettingsViewModel model = Model();
        Assert.True(model.HasNoRules);
        AppRuleViewModel rule = model.AddRule(new AppCandidate("Figma.exe", "Figma", null));
        Assert.False(model.HasNoRules);
        Assert.True(rule.Enabled);
        Assert.True(rule.NoSmoothing);
        Assert.False(rule.LeaveAlone);
        WheelAppRule saved = Assert.Single(Store.Current.AppRules);
        Assert.Equal(new WheelAppRule("figma.exe", "Figma") { NoSmoothing = true }, saved);
    }

    [Fact]
    public void AddingAnAppTwiceGivesBackItsRule()
    {
        using SettingsViewModel model = Model();
        AppRuleViewModel first = model.AddRule(new AppCandidate("notepad.exe", "Notepad", null));
        AppRuleViewModel again = model.AddRule(new AppCandidate("NOTEPAD.EXE", "Notepad", null));
        Assert.Same(first, again);
        Assert.Single(Store.Current.AppRules);
    }

    [Fact]
    public void TheTwoChoicesExcludeEachOther()
    {
        using SettingsViewModel model = Model();
        AppRuleViewModel rule = model.AddRule(new AppCandidate("old.exe", "Old", null));
        rule.LeaveAlone = true;
        Assert.False(rule.NoSmoothing);
        Assert.Equal(new WheelAppRule("old.exe", "Old") { LeaveAlone = true }, Store.Current.AppRules[0]);
        rule.LeaveAlone = false;   // a radio button never unchecks itself: nothing changes
        Assert.True(rule.LeaveAlone);
        rule.NoSmoothing = true;
        Assert.False(rule.LeaveAlone);
        Assert.Equal(new WheelAppRule("old.exe", "Old") { NoSmoothing = true }, Store.Current.AppRules[0]);
        rule.Enabled = false;
        Assert.False(Store.Current.AppRules[0].Enabled);
    }

    [Fact]
    public void ARuleWithBothChoicesFromAnOldFileShowsLeaveAlone()
    {
        Store.SetAppRules([new WheelAppRule("both.exe", "Both") { NoSmoothing = true, LeaveAlone = true }]);
        using SettingsViewModel model = Model();
        Assert.True(model.Rules[0].LeaveAlone);
        Assert.False(model.Rules[0].NoSmoothing);
    }

    [Fact]
    public void RemovingARuleSavesTheRest()
    {
        using SettingsViewModel model = Model();
        AppRuleViewModel a = model.AddRule(new AppCandidate("a.exe", "A", null));
        model.AddRule(new AppCandidate("b.exe", "B", null));
        a.RemoveCommand.Execute(null);
        Assert.Equal(["b.exe"], Store.Current.AppRules.Select(r => r.ExeName));
        Assert.Equal(["b.exe"], model.Rules.Select(r => r.ExeName));
    }

    [Fact]
    public void RulesChangedElsewhereShowUp()
    {
        using SettingsViewModel model = Model();
        AppRuleViewModel a = model.AddRule(new AppCandidate("a.exe", "A", null));
        Store.SetAppRules([new WheelAppRule("c.exe", "C") { LeaveAlone = true }, new WheelAppRule("a.exe", "A") { Enabled = false, NoSmoothing = true }]);
        Assert.Equal(["c.exe", "a.exe"], model.Rules.Select(r => r.ExeName));
        Assert.Same(a, model.Rules[1]);
        Assert.False(a.Enabled);
        Assert.True(model.Rules[0].LeaveAlone);
    }

    [Fact]
    public void AddAnAppOffersRunningAppsWithoutARuleYet()
    {
        _apps.Running.AddRange([new AppCandidate("code.exe", "Visual Studio Code", null), new AppCandidate("notepad.exe", "Notepad", null)]);
        using SettingsViewModel model = Model();
        model.AddRule(new AppCandidate("notepad.exe", "Notepad", null));
        Assert.Equal(["code.exe"], model.AppsToAdd().Select(a => a.ExeName));
        Assert.Equal(new AppCandidate("tool.exe", "Tool", @"C:\Tools\Tool.exe"), model.DescribeProgram(@"C:\Tools\Tool.exe"));
    }

    // MARK: The explanation panel

    [Fact]
    public void ChangingPageStartsTheExplanationAfresh()
    {
        using SettingsViewModel model = Model();
        Assert.Equal(SettingsViewModel.Intro, model.Explanation.Text);
        model.Explanation.PointerEntered("row", "What it does.");
        Assert.Equal("What it does.", model.Explanation.Text);
        model.Page = SettingsPage.AppRules;
        Assert.Equal(SettingsViewModel.Intro, model.Explanation.Text);
        Assert.True(model.Explanation.ShowsIntro);
        Assert.DoesNotContain("\n", model.AppRulesSubtitle);
    }
}

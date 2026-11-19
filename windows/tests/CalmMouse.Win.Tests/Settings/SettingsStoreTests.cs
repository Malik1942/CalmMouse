using System.IO;
using System.Text.Json.Nodes;
using CalmMouse.Core;
using CalmMouse.Core.Routing;
using CalmMouse.Win.Settings;

namespace CalmMouse.Win.Tests.Settings;

/// <summary>The settings file, always in a throwaway folder under %TEMP% (never the real %AppData%).</summary>
public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"CalmMouse.Tests-{Guid.NewGuid():N}");
    private string FilePath => Path.Combine(_folder, SettingsStore.FileName);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    private SettingsStore Open(TimeSpan? delay = null, Func<DateTimeOffset>? clock = null) =>
        new(FilePath, delay ?? TimeSpan.FromHours(1), clock);

    [Fact]
    public void ANewStoreIsAFirstLaunchWithEverythingOff()
    {
        using SettingsStore store = Open();
        Assert.True(store.IsFirstLaunch);
        AppSettings s = store.Current;
        Assert.Equal(AppSettings.CurrentVersion, s.Version);
        Assert.False(s.Paused);
        Assert.False(s.LaunchAtLogin);
        Assert.Equal(AppLanguage.System, s.Language);
        Assert.Empty(s.Mice);
        Assert.Empty(s.AppRules);

        WheelSettings wheel = store.SettingsFor("3554:F58A:Mouse", "Mouse");
        Assert.False(wheel.Smooth);
        Assert.False(wheel.Reverse);
        Assert.False(wheel.BlockWhilePressed);
        Assert.Equal(ScrollRouter.DefaultSpeed, wheel.Speed);
        Assert.False(File.Exists(FilePath));   // nothing written until asked
    }

    [Fact]
    public void RoundTripsEverySetting()
    {
        var at = new DateTimeOffset(2026, 11, 18, 9, 30, 0, TimeSpan.FromHours(8));
        using (SettingsStore store = Open(clock: () => at))
        {
            store.SetPaused(true);
            store.SetLaunchAtLogin(true);
            store.SetLanguage(AppLanguage.TraditionalChinese);
            store.UpdateMouse("3554:F58A:2.4G Mouse", "2.4G Mouse", w => w with { Smooth = true, Speed = 5 });
            store.UpdateMouse("5253:1020:", "MCHOSE A7 Ultra", w => w with { Reverse = true, BlockWhilePressed = true });
            store.SetAppRules([new WheelAppRule("figma.exe", "Figma") { NoSmoothing = true }, new WheelAppRule("game.exe", "Game") { LeaveAlone = true, Enabled = false }]);
        }   // Dispose writes what's pending

        using SettingsStore again = Open();
        Assert.False(again.IsFirstLaunch);
        AppSettings s = again.Current;
        Assert.True(s.Paused);
        Assert.True(s.LaunchAtLogin);
        Assert.Equal(AppLanguage.TraditionalChinese, s.Language);
        Assert.Equal(2, s.Mice.Count);
        MouseSettings first = s.Mice["3554:F58A:2.4G Mouse"];
        Assert.Equal("2.4G Mouse", first.DisplayName);
        Assert.Equal(new WheelSettings(smooth: true, speed: 5), first.Wheel);
        Assert.Equal(at, first.UpdatedAt);
        Assert.Equal(new WheelSettings(reverse: true, blockWhilePressed: true), s.Mice["5253:1020:"].Wheel);
        Assert.Equal(
            [new WheelAppRule("figma.exe", "Figma") { NoSmoothing = true }, new WheelAppRule("game.exe", "Game") { LeaveAlone = true, Enabled = false }],
            s.AppRules);
    }

    [Fact]
    public void WritesTheDocumentedShape()
    {
        using (SettingsStore store = Open(clock: () => new DateTimeOffset(2026, 11, 18, 1, 2, 3, TimeSpan.Zero)))
        {
            store.SetLanguage(AppLanguage.SimplifiedChinese);
            store.UpdateMouse("K", "Name", w => w with { Smooth = true });
            store.SetAppRules([new WheelAppRule("figma.exe", "Figma")]);
        }
        JsonObject root = JsonNode.Parse(File.ReadAllText(FilePath))!.AsObject();
        Assert.Equal(["version", "paused", "launchAtLogin", "language", "mice", "appRules"], root.Select(p => p.Key));
        Assert.Equal(1, (int)root["version"]!);
        Assert.Equal("simplifiedChinese", (string)root["language"]!);
        JsonObject mouse = root["mice"]!["K"]!.AsObject();
        Assert.Equal("Name", (string)mouse["displayName"]!);
        Assert.Equal("""{"reverse":false,"blockWhilePressed":false,"smooth":true,"speed":3}""", mouse["wheel"]!.ToJsonString());
        Assert.Equal("2026-11-18T01:02:03+00:00", (string)mouse["updatedAt"]!);
        Assert.Equal("""[{"exeName":"figma.exe","displayName":"Figma","enabled":true,"noSmoothing":false,"leaveAlone":false}]""",
            root["appRules"]!.ToJsonString());
    }

    [Theory]
    [InlineData("{ this isn't json")]
    [InlineData("[1, 2, 3]")]
    [InlineData("")]
    [InlineData("\"just a string\"")]
    public void ACorruptFileIsKeptAsBakAndDefaultsAreUsed(string corrupt)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, corrupt);

        using (SettingsStore store = Open())
        {
            Assert.False(store.IsFirstLaunch);
            Assert.Equal(FilePath + ".bak", store.CorruptBackupPath);
            Assert.Equal(corrupt, File.ReadAllText(FilePath + ".bak"));
            Assert.Equal(new AppSettings().Paused, store.Current.Paused);
            Assert.Empty(store.Current.Mice);
            store.Flush();   // the corrupt file is replaced with defaults
        }
        Assert.NotNull(SettingsJson.Read(File.ReadAllText(FilePath)));
    }

    [Fact]
    public void BadFieldsGetDefaultsAndBadEntriesAreSkipped()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, """
            {
              "version": 1,
              "paused": "yes",
              "launchAtLogin": true,
              "language": "klingon",
              "mice": {
                "good": { "displayName": "Good", "wheel": { "reverse": true, "blockWhilePressed": false, "smooth": false, "speed": 3 }, "updatedAt": "2026-11-18T00:00:00+00:00" },
                "bad wheel": { "displayName": "Bad", "wheel": { "reverse": true } },
                "not an object": 7
              },
              "appRules": [ { "exeName": "a.exe", "displayName": "A" }, { "displayName": "no exe" }, 3, { "exeName": "b.exe", "leaveAlone": true } ],
              "somethingNewer": { "x": 1 }
            }
            """);
        using SettingsStore store = Open();
        AppSettings s = store.Current;
        Assert.Null(store.CorruptBackupPath);
        Assert.False(s.Paused);
        Assert.True(s.LaunchAtLogin);
        Assert.Equal(AppLanguage.System, s.Language);
        Assert.Equal(["good"], s.Mice.Keys);
        Assert.True(s.Mice["good"].Wheel.Reverse);
        Assert.Equal(["a.exe", "b.exe"], s.AppRules.Select(r => r.ExeName));
        Assert.True(s.AppRules[0].Enabled);   // missing → a rule that is added is on
        Assert.Equal("b.exe", s.AppRules[1].DisplayName);   // missing name → the exe name
        Assert.True(s.AppRules[1].LeaveAlone);
    }

    [Fact]
    public void WritesAreAtomicAndLeaveNoTempFiles()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, "old");
        SettingsStore.WriteAtomically(FilePath, "new contents");
        Assert.Equal("new contents", File.ReadAllText(FilePath));
        Assert.Equal([SettingsStore.FileName], Directory.GetFiles(_folder).Select(Path.GetFileName));

        // Into a folder that doesn't exist yet, with nothing to replace.
        string fresh = Path.Combine(_folder, "sub", "settings.json");
        SettingsStore.WriteAtomically(fresh, "first");
        Assert.Equal("first", File.ReadAllText(fresh));
        Assert.Equal(["settings.json"], Directory.GetFiles(Path.GetDirectoryName(fresh)!).Select(Path.GetFileName));
    }

    [Fact]
    public void AFailedWriteKeepsTheOldFile()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, "old");
        // Another program holding the file open without sharing: the swap fails, the old file stays whole.
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Exception error = Assert.ThrowsAny<Exception>(() => SettingsStore.WriteAtomically(FilePath, "new"));
            Assert.True(error is IOException or UnauthorizedAccessException, error.ToString());   // what SettingsStore catches
        }
        Assert.Equal("old", File.ReadAllText(FilePath));
        Assert.Equal([SettingsStore.FileName], Directory.GetFiles(_folder).Select(Path.GetFileName));
    }

    [Fact]
    public async Task ChangesAreWrittenOnceAfterTheDelay()
    {
        using SettingsStore store = Open(delay: TimeSpan.FromMilliseconds(200));
        store.SetPaused(true);
        store.SetLanguage(AppLanguage.English);
        store.SetLaunchAtLogin(true);
        Assert.False(File.Exists(FilePath));   // not yet

        DateTime giveUp = DateTime.UtcNow.AddSeconds(5);
        while (!File.Exists(FilePath) && DateTime.UtcNow < giveUp) { await Task.Delay(20, TestContext.Current.CancellationToken); }
        AppSettings? written = SettingsJson.Read(File.ReadAllText(FilePath));
        Assert.NotNull(written);
        Assert.True(written.Paused);
        Assert.True(written.LaunchAtLogin);
        Assert.Equal(AppLanguage.English, written.Language);
    }

    [Fact]
    public void FlushWritesNow()
    {
        using SettingsStore store = Open();
        store.SetPaused(true);
        store.Flush();
        Assert.True(SettingsJson.Read(File.ReadAllText(FilePath))!.Paused);
    }

    [Fact]
    public void TheEngineHearsOnlyAboutWheelAndRuleChanges()
    {
        using SettingsStore store = Open();
        int engine = 0, any = 0;
        store.Changed += (_, _) => engine++;
        store.SettingsChanged += (_, _) => any++;

        store.SetPaused(true);
        store.SetPaused(true);   // no change, no event
        store.SetLanguage(AppLanguage.English);
        Assert.Equal((0, 2), (engine, any));

        store.UpdateMouse("K", "N", w => w with { Reverse = true });
        store.SetAppRules([new WheelAppRule("a.exe", "A")]);
        Assert.Equal((2, 4), (engine, any));
    }

    [Fact]
    public void SpeedIsKeptInTheMacRange()
    {
        using SettingsStore store = Open();
        store.UpdateMouse("a", "A", w => w with { Speed = 40 });
        store.UpdateMouse("b", "B", w => w with { Speed = 0.1 });
        store.UpdateMouse("c", "C", w => w with { Speed = double.NaN });
        Assert.Equal(12, store.SettingsFor("a", "A").Speed);
        Assert.Equal(1, store.SettingsFor("b", "B").Speed);
        Assert.Equal(3, store.SettingsFor("c", "C").Speed);
    }

    // MARK: Device lookup (the Mac's DeviceSettingsIndex)

    [Fact]
    public void AMouseUsesItsOwnSettingsFirst()
    {
        using SettingsStore store = Open();
        store.UpdateMouse("usb", "Mouse", _ => new WheelSettings(reverse: true));
        store.UpdateMouse("bt", "Mouse", _ => new WheelSettings(smooth: true));
        Assert.Equal(new WheelSettings(reverse: true), store.SettingsFor("usb", "Mouse"));
        Assert.Equal(new WheelSettings(smooth: true), store.SettingsFor("bt", "Mouse"));
    }

    [Fact]
    public void AMouseWithoutSettingsStartsFromTheSameNamedOneChangedLast()
    {
        var now = new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);
        using SettingsStore store = Open(clock: () => now);
        store.UpdateMouse("receiver", "MX Master", _ => new WheelSettings(reverse: true));
        now = now.AddMinutes(1);
        store.UpdateMouse("bluetooth", "MX Master", _ => new WheelSettings(smooth: true));
        now = now.AddMinutes(1);
        store.UpdateMouse("other", "Other", w => w with { BlockWhilePressed = true });

        Assert.Equal(new WheelSettings(smooth: true), store.SettingsFor("new connection", "MX Master"));
        Assert.Equal(new WheelSettings(), store.SettingsFor("new connection", "Unknown"));
        Assert.Equal(new WheelSettings(), store.SettingsFor("new connection", ""));   // no name inherits nothing
        Assert.Equal(new WheelSettings(), store.SettingsFor("new connection"));
    }

    [Fact]
    public void AFirstChangeStartsFromWhatTheMouseWasUsing()
    {
        using SettingsStore store = Open();
        store.UpdateMouse("receiver", "MX Master", w => w with { Reverse = true, Speed = 6 });
        store.UpdateMouse("bluetooth", "MX Master", w => w with { Smooth = true });
        Assert.Equal(new WheelSettings(reverse: true, smooth: true, speed: 6), store.SettingsFor("bluetooth", "MX Master"));
        Assert.Equal(new WheelSettings(reverse: true, speed: 6), store.SettingsFor("receiver", "MX Master"));   // untouched
    }
}

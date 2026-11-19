using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using CalmMouse.Win.Localization;
using CalmMouse.Win.Settings;

namespace CalmMouse.Win.Tests.Localization;

public sealed partial class LocalizerTests
{
    private static readonly UiLanguage[] AllLanguages = Enum.GetValues<UiLanguage>();

    private static Localizer Sample(UiLanguage language) => new(new Dictionary<UiLanguage, IReadOnlyDictionary<string, string>>
    {
        [UiLanguage.English] = new Dictionary<string, string> { ["Mouse"] = "Mouse", ["Only English"] = "Only English (en)", ["%@ and %@"] = "%@ and %@" },
        [UiLanguage.SimplifiedChinese] = new Dictionary<string, string> { ["Mouse"] = "鼠标", ["Battery %ld%%"] = "电量 %ld%%", ["%@ and %@"] = "%@ 和 %@", ["Broken %@"] = "坏了 %@ %@" },
        [UiLanguage.TraditionalChinese] = new Dictionary<string, string> { ["Mouse"] = "滑鼠" },
    }, language);

    // MARK: Which language

    [Theory]
    [InlineData("zh-CN", UiLanguage.SimplifiedChinese)]
    [InlineData("zh-SG", UiLanguage.SimplifiedChinese)]
    [InlineData("zh-Hans", UiLanguage.SimplifiedChinese)]
    [InlineData("zh-Hans-HK", UiLanguage.SimplifiedChinese)]
    [InlineData("zh", UiLanguage.SimplifiedChinese)]
    [InlineData("zh-TW", UiLanguage.TraditionalChinese)]
    [InlineData("zh-HK", UiLanguage.TraditionalChinese)]
    [InlineData("zh-MO", UiLanguage.TraditionalChinese)]
    [InlineData("zh-Hant", UiLanguage.TraditionalChinese)]
    [InlineData("zh-Hant-CN", UiLanguage.TraditionalChinese)]
    [InlineData("en-US", UiLanguage.English)]
    [InlineData("en-GB", UiLanguage.English)]
    [InlineData("ja-JP", UiLanguage.English)]
    [InlineData("de-DE", UiLanguage.English)]
    [InlineData("", UiLanguage.English)]
    public void SystemFollowsTheWindowsDisplayLanguage(string culture, UiLanguage expected)
    {
        Assert.Equal(expected, Localizer.Resolve(AppLanguage.System, CultureInfo.GetCultureInfo(culture)));
    }

    [Theory]
    [InlineData(AppLanguage.English, UiLanguage.English)]
    [InlineData(AppLanguage.SimplifiedChinese, UiLanguage.SimplifiedChinese)]
    [InlineData(AppLanguage.TraditionalChinese, UiLanguage.TraditionalChinese)]
    public void AChosenLanguageWinsOverWindows(AppLanguage setting, UiLanguage expected)
    {
        Assert.Equal(expected, Localizer.Resolve(setting, CultureInfo.GetCultureInfo("zh-TW")));
        Assert.Equal(expected, Localizer.Resolve(setting, CultureInfo.GetCultureInfo("en-US")));
    }

    // MARK: Looking up

    [Fact]
    public void LooksUpTheCurrentLanguageThenEnglishThenTheKey()
    {
        Localizer l = Sample(UiLanguage.TraditionalChinese);
        Assert.Equal("滑鼠", l.Get("Mouse"));
        Assert.Equal("Only English (en)", l.Get("Only English"));
        Assert.Equal("Nowhere", l.Get("Nowhere"));
        Assert.Equal("Nowhere 7", l.Get("Nowhere %d", 7));
    }

    [Fact]
    public void FormatsArgumentsButLeavesTextWithoutThemAlone()
    {
        Localizer l = Sample(UiLanguage.SimplifiedChinese);
        Assert.Equal("电量 15%", l.Get("Battery %ld%%", 15));
        Assert.Equal("电量 %ld%%", l.Get("Battery %ld%%"));   // as the Mac's L() with no arguments
    }

    [Fact]
    public void ABrokenTranslationFallsBackToTheEnglishFormat()
    {
        Assert.Equal("Broken x", Sample(UiLanguage.SimplifiedChinese).Get("Broken %@", "x"));
    }

    [Fact]
    public void FormattingThatCanNeverWorkGivesTheTextUnformatted()
    {
        // Too few arguments for the English as well: no exception, just the text.
        Assert.Equal("%@ and %@", Sample(UiLanguage.English).Get("%@ and %@", "only one"));
        Assert.Equal("%@ 和 %@", Sample(UiLanguage.SimplifiedChinese).Get("%@ and %@", "only one"));
    }

    [Fact]
    public void SwitchingLanguageRaisesAnEventOnlyOnAChange()
    {
        Localizer l = Sample(UiLanguage.English);
        int changes = 0;
        l.LanguageChanged += (_, _) => changes++;
        l.SetLanguage(UiLanguage.English);
        Assert.Equal(0, changes);
        l.SetLanguage(UiLanguage.TraditionalChinese);
        Assert.Equal(1, changes);
        Assert.Equal("滑鼠", l.Get("Mouse"));
        l.Apply(AppLanguage.SimplifiedChinese);
        Assert.Equal(2, changes);
        Assert.Equal("鼠标", l.Get("Mouse"));
    }

    [Fact]
    public void JoinsListsTheWayEachLanguageDoes()
    {
        Assert.Equal("A", Sample(UiLanguage.English).JoinList(["A"]));
        Assert.Equal("A and B", Sample(UiLanguage.English).JoinList(["A", "B"]));
        Assert.Equal("A, B, and C", Sample(UiLanguage.English).JoinList(["A", "B", "C"]));
        Assert.Equal("A, B, C, and D", Sample(UiLanguage.English).JoinList(["A", "B", "C", "D"]));
        Assert.Equal("A、B 和 C", Sample(UiLanguage.SimplifiedChinese).JoinList(["A", "B", "C"]));
    }

    // MARK: The real tables

    [Fact]
    public void TheEmbeddedTablesHaveTheMacStringsAndTheWindowsOnes()
    {
        Dictionary<UiLanguage, IReadOnlyDictionary<string, string>> tables = Localizer.LoadEmbeddedTables();
        var hant = new Localizer(tables, UiLanguage.TraditionalChinese);
        var hans = new Localizer(tables, UiLanguage.SimplifiedChinese);
        Assert.Equal("結束 CalmMouse", hant.Get("Quit CalmMouse"));   // from the Mac table
        Assert.Equal("設定…", hant.Get("Settings…"));
        Assert.Equal("暂停 CalmMouse", hans.Get("Pause CalmMouse"));   // from the Windows table
        Assert.True(tables[UiLanguage.English].Count > 200);
    }

    [Fact]
    public void WindowsEntriesWinOverMacOnes()
    {
        // Every Windows entry ends up in the merged table as the Windows file has it.
        Dictionary<UiLanguage, IReadOnlyDictionary<string, string>> tables = Localizer.LoadEmbeddedTables();
        foreach (UiLanguage language in AllLanguages)
        {
            foreach ((string key, string value) in WindowsTable(language)) { Assert.Equal(value, tables[language][key]); }
        }
    }

    [Fact]
    public void EveryWindowsOnlyKeyIsInAllThreeTables()
    {
        Dictionary<string, string> english = WindowsTable(UiLanguage.English);
        Assert.NotEmpty(english);
        foreach (UiLanguage language in AllLanguages)
        {
            Dictionary<string, string> table = WindowsTable(language);
            Assert.Equal(english.Keys.Order(StringComparer.Ordinal), table.Keys.Order(StringComparer.Ordinal));
            Assert.All(table, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value), $"{language}: \"{entry.Key}\" is empty"));
        }
        // English values are the keys themselves, except where Windows words a Mac string its own way.
        IReadOnlyDictionary<string, string> mac = Localizer.ReadEmbedded(typeof(Localizer).Assembly, Localizer.ResourceNames(UiLanguage.English).Mac);
        Assert.All(english.Where(e => !mac.ContainsKey(e.Key)), entry => Assert.Equal(entry.Key, entry.Value));
        Assert.Equal("Start when I sign in", english["Launch at login"]);
    }

    [Fact]
    public void TranslationsKeepTheirFormatSpecifiers()
    {
        Dictionary<UiLanguage, IReadOnlyDictionary<string, string>> tables = Localizer.LoadEmbeddedTables();
        foreach (UiLanguage language in AllLanguages)
        {
            foreach ((string key, string value) in tables[language])
            {
                Assert.True(Placeholders(key).SetEquals(Placeholders(value)), $"{language}: \"{key}\" → \"{value}\" changes the format specifiers");
            }
        }
    }

    [Fact]
    public void EveryStringTheWindowsAppAsksForIsTranslated()
    {
        Dictionary<UiLanguage, IReadOnlyDictionary<string, string>> tables = Localizer.LoadEmbeddedTables();
        List<string> keys = KeysUsedInSource();
        Assert.NotEmpty(keys);
        foreach (string key in keys)
        {
            foreach (UiLanguage language in AllLanguages)
            {
                Assert.True(tables[language].ContainsKey(key), $"L(\"{key}\") is missing from the {Localizer.LprojName(language)} tables");
            }
        }
    }

    private static HashSet<string> Placeholders(string text) =>
        FormatItem().Matches(StringsFile.ConvertFormat(text)).Select(m => m.Groups[1].Value).ToHashSet();

    [GeneratedRegex(@"(?<!\{)\{(\d+)[^{}]*\}")]
    private static partial Regex FormatItem();

    [GeneratedRegex("""\bL\("((?:[^"\\]|\\.)*)"[,)]""")]
    private static partial Regex LCall();

    /// <summary>Every <c>L("…")</c> literal in windows/src/CalmMouse.Win, outside comments (one call per line).</summary>
    private static List<string> KeysUsedInSource()
    {
        string src = Path.Combine(RepoRoot(), "windows", "src", "CalmMouse.Win");
        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(File.ReadAllLines)
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))   // examples in comments
            .SelectMany(line => LCall().Matches(line))
            .Select(m => Regex.Unescape(m.Groups[1].Value))
            .Distinct()
            .ToList();
    }

    private static Dictionary<string, string> WindowsTable(UiLanguage language) =>
        StringsFile.Parse(File.ReadAllBytes(Path.Combine(RepoRoot(), "windows", "src", "CalmMouse.Win", "Strings", $"{Localizer.LprojName(language)}.lproj", "Localizable.strings")));

    private static string RepoRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "windows", "CalmMouse.Windows.sln"))) { return dir.FullName; }
        }
        throw new InvalidOperationException("Couldn't find the repository root above " + AppContext.BaseDirectory);
    }
}

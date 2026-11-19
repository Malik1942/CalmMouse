// The app's strings in English, 简体中文 and 繁體中文, as on the Mac: keys are the English text, so
// a string missing from a table falls back to English. Each language's table is the Mac app's
// Resources/<lang>.lproj/Localizable.strings with the Windows-only Strings/<lang>.lproj table on
// top (Windows entries win), both embedded in the exe (see CalmMouse.Win.csproj).
//
// `L("Quit CalmMouse")` reads the current language's table; `L("%@ is …", name)` formats too,
// with Apple's specifiers converted to .NET (StringsFile.ConvertFormat). Changing the language
// raises LanguageChanged, and every open piece of UI rebuilds its text from L() again.

using System.Globalization;
using System.IO;
using System.Reflection;
using CalmMouse.Win.Settings;

namespace CalmMouse.Win.Localization;

/// <summary>A language CalmMouse can show (what <see cref="AppLanguage.System"/> resolves to).</summary>
public enum UiLanguage
{
    English,
    SimplifiedChinese,
    TraditionalChinese,
}

public sealed class Localizer
{
    private readonly IReadOnlyDictionary<UiLanguage, IReadOnlyDictionary<string, string>> _tables;
    private UiLanguage _language;

    /// <summary>The app's own strings, loaded from the embedded tables. Starts in the Windows display language.</summary>
    public static Localizer Shared { get; } = new(LoadEmbeddedTables(), Resolve(AppLanguage.System, CultureInfo.CurrentUICulture));

    public Localizer(IReadOnlyDictionary<UiLanguage, IReadOnlyDictionary<string, string>> tables, UiLanguage language = UiLanguage.English)
    {
        _tables = tables;
        _language = language;
    }

    public UiLanguage Language => _language;

    /// <summary>The culture numbers and lists are formatted for.</summary>
    public CultureInfo Culture => CultureFor(_language);

    /// <summary>Raised on the thread that changed the language, after the change.</summary>
    public event EventHandler? LanguageChanged;

    /// <summary>Follows the setting: <see cref="AppLanguage.System"/> means the Windows display language.</summary>
    public void Apply(AppLanguage setting) => SetLanguage(Resolve(setting, CultureInfo.CurrentUICulture));

    public void SetLanguage(UiLanguage language)
    {
        if (language == _language) { return; }
        _language = language;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The current language's text for <paramref name="key"/> (the English text), or the key itself
    /// if no table has it. With arguments, Apple-style specifiers in it are filled in; without, the
    /// text comes back as it is (as the Mac's L() does).
    /// </summary>
    public string Get(string key, params object?[] args)
    {
        string text = Lookup(key);
        if (args.Length == 0) { return text; }
        try
        {
            return string.Format(Culture, StringsFile.ConvertFormat(text), args);
        }
        catch (FormatException)
        {
            // A translation with a broken specifier: try the English, and if even that can't take
            // these arguments, show the text unformatted rather than fail.
            try { return string.Format(Culture, StringsFile.ConvertFormat(key), args); }
            catch (FormatException) { return text; }
        }
    }

    private string Lookup(string key)
    {
        if (_tables.TryGetValue(_language, out IReadOnlyDictionary<string, string>? table) && table.TryGetValue(key, out string? text)) { return text; }
        if (_tables.TryGetValue(UiLanguage.English, out IReadOnlyDictionary<string, string>? english) && english.TryGetValue(key, out string? en)) { return en; }
        return key;
    }

    /// <summary>"A", "A and B", "A, B, and C" (as the Mac's ListFormatter), and 「A、B 和 C」 in Chinese.</summary>
    public string JoinList(IReadOnlyList<string> items)
    {
        if (items.Count == 0) { return ""; }
        if (items.Count == 1) { return items[0]; }
        bool english = _language == UiLanguage.English;
        string head = string.Join(english ? ", " : "、", items.Take(items.Count - 1));
        if (english && items.Count > 2) { head += ","; }   // the serial comma
        return Get("%@ and %@", head, items[^1]);
    }

    /// <summary>
    /// What <paramref name="setting"/> means with this Windows display language: Chinese written in
    /// Simplified characters (zh-CN, zh-SG, zh-Hans…) → 简体; Traditional (zh-TW, zh-HK, zh-MO,
    /// zh-Hant…) → 繁體; anything else → English.
    /// </summary>
    public static UiLanguage Resolve(AppLanguage setting, CultureInfo uiCulture) => setting switch
    {
        AppLanguage.English => UiLanguage.English,
        AppLanguage.SimplifiedChinese => UiLanguage.SimplifiedChinese,
        AppLanguage.TraditionalChinese => UiLanguage.TraditionalChinese,
        _ => FromCultureName(uiCulture.Name),
    };

    public static UiLanguage FromCultureName(string name)
    {
        string[] parts = name.Split('-', '_');
        if (parts.Length == 0 || !parts[0].Equals("zh", StringComparison.OrdinalIgnoreCase)) { return UiLanguage.English; }
        foreach (string part in parts.Skip(1))
        {
            switch (part.ToUpperInvariant())
            {
                case "HANT" or "CHT" or "TW" or "HK" or "MO": return UiLanguage.TraditionalChinese;
                case "HANS" or "CHS" or "CN" or "SG": return UiLanguage.SimplifiedChinese;
            }
        }
        return UiLanguage.SimplifiedChinese;   // plain "zh"
    }

    public static CultureInfo CultureFor(UiLanguage language) => language switch
    {
        UiLanguage.SimplifiedChinese => CultureInfo.GetCultureInfo("zh-CN"),
        UiLanguage.TraditionalChinese => CultureInfo.GetCultureInfo("zh-TW"),
        _ => CultureInfo.GetCultureInfo("en-US"),
    };

    /// <summary>The folder name of each language's table, as on the Mac.</summary>
    public static string LprojName(UiLanguage language) => language switch
    {
        UiLanguage.SimplifiedChinese => "zh-Hans",
        UiLanguage.TraditionalChinese => "zh-Hant",
        _ => "en",
    };

    /// <summary>The embedded resource names of a language's two tables (see the csproj).</summary>
    public static (string Mac, string Windows) ResourceNames(UiLanguage language) =>
        ($"CalmMouse.Strings.Mac.{LprojName(language)}", $"CalmMouse.Strings.Windows.{LprojName(language)}");

    /// <summary>The Mac table with the Windows one on top, for each language.</summary>
    public static Dictionary<UiLanguage, IReadOnlyDictionary<string, string>> LoadEmbeddedTables()
    {
        Assembly assembly = typeof(Localizer).Assembly;
        var tables = new Dictionary<UiLanguage, IReadOnlyDictionary<string, string>>();
        foreach (UiLanguage language in Enum.GetValues<UiLanguage>())
        {
            (string mac, string windows) = ResourceNames(language);
            var merged = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string name in new[] { mac, windows })
            {
                foreach ((string key, string value) in ReadEmbedded(assembly, name)) { merged[key] = value; }
            }
            tables[language] = merged;
        }
        return tables;
    }

    public static Dictionary<string, string> ReadEmbedded(Assembly assembly, string resourceName)
    {
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null) { return new Dictionary<string, string>(StringComparer.Ordinal); }
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return StringsFile.Parse(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }
}

/// <summary><c>using static CalmMouse.Win.Localization.Strings;</c> gives the Mac's <c>L("…")</c>.</summary>
public static class Strings
{
    public static string L(string key, params object?[] args) => Localizer.Shared.Get(key, args);
}

using System.Text;
using CalmMouse.Win.Localization;

namespace CalmMouse.Win.Tests.Localization;

public sealed class StringsFileTests
{
    [Fact]
    public void ReadsEntriesBetweenComments()
    {
        Dictionary<string, string> table = StringsFile.Parse("""
            /* A block comment,
               over two lines, with "quotes" and = and ; in it. */
            "Settings…" = "设置…";   // a line comment
            // another line comment
            "Quit CalmMouse"="退出 CalmMouse";/* tight */"A" = "B";
            """);
        Assert.Equal(new Dictionary<string, string>
        {
            ["Settings…"] = "设置…",
            ["Quit CalmMouse"] = "退出 CalmMouse",
            ["A"] = "B",
        }, table);
    }

    [Fact]
    public void ReadsEscapes()
    {
        Dictionary<string, string> table = StringsFile.Parse("""
            "Say \"hi\"" = "Back\\slash, new\nline, tab\there, \'single\', \U00E9 and \u4E2D";
            """);
        KeyValuePair<string, string> entry = Assert.Single(table);
        Assert.Equal("Say \"hi\"", entry.Key);
        Assert.Equal("Back\\slash, new\nline, tab\there, 'single', é and 中", entry.Value);
    }

    [Fact]
    public void AKeyAloneIsItsOwnValueAndBareWordsWork()
    {
        Dictionary<string, string> table = StringsFile.Parse("""
            "Only a key";
            bare_key = "value";
            """);
        Assert.Equal("Only a key", table["Only a key"]);
        Assert.Equal("value", table["bare_key"]);
    }

    [Fact]
    public void ALaterEntryWins()
    {
        Assert.Equal("second", StringsFile.Parse("\"k\" = \"first\";\n\"k\" = \"second\";")["k"]);
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf8-bom")]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    public void ReadsEveryEncodingAppleWrites(string encoding)
    {
        const string text = "/* 注释 */\n\"Mouse\" = \"滑鼠\";\n";
        byte[] bytes = encoding switch
        {
            "utf8" => Encoding.UTF8.GetBytes(text),
            "utf8-bom" => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)],
            "utf16le" => [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)],
            _ => [.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(text)],
        };
        Assert.Equal("滑鼠", StringsFile.Parse(bytes)["Mouse"]);
    }

    [Theory]
    [InlineData("\"a\" = \"b\"", "expected ';'")]
    [InlineData("\"a\" \"b\";", "expected '='")]
    [InlineData("\"a\" = \"never closed;", "never closed")]
    [InlineData("/* never closed", "never closed")]
    [InlineData("\"a\" = \"\\U12\";", "four hex digits")]
    [InlineData("\n\n= \"b\";", "line 3")]
    public void SaysWhatIsWrongAndWhere(string text, string message)
    {
        FormatException error = Assert.Throws<FormatException>(() => StringsFile.Parse(text));
        Assert.Contains(message, error.Message);
    }

    [Theory]
    [InlineData("Ignore scrolling in %@", "Ignore scrolling in {0}")]
    [InlineData("%@ and %@", "{0} and {1}")]
    [InlineData("%d mice, %ld taps, %i, %u, %lu, %lld", "{0} mice, {1} taps, {2}, {3}, {4}, {5}")]
    [InlineData("%ld%%", "{0}%")]
    [InlineData("100%% sure", "100% sure")]
    [InlineData("%2$@ before %1$@", "{1} before {0}")]
    [InlineData("%.1f ms, %f, %.0f", "{0:F1} ms, {1:F6}, {2:F0}")]
    [InlineData("%5d|%-5d|%05d", "{0,5}|{1,-5}|{2,5}")]
    [InlineData("0x%04X, %x", "0x{0,4:X}, {1:x}")]
    [InlineData("{braces} stay", "{{braces}} stay")]
    [InlineData("a % b, trailing %", "a % b, trailing %")]
    [InlineData("no specifiers", "no specifiers")]
    public void ConvertsAppleFormatSpecifiers(string apple, string dotnet)
    {
        Assert.Equal(dotnet, StringsFile.ConvertFormat(apple));
    }

    [Fact]
    public void ConvertedFormatsFormat()
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        Assert.Equal("Battery 15% · MX Master",
            string.Format(culture, StringsFile.ConvertFormat("Battery %ld%% · %@"), 15, "MX Master"));
        Assert.Equal("B then A", string.Format(culture, StringsFile.ConvertFormat("%2$@ then %1$@"), "A", "B"));
    }
}

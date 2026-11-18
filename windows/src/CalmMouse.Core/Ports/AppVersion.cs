// Port of Sources/CalmMouseCore/AppVersion.swift.
//
// Swift works on Characters (grapheme clusters) here: `hasPrefix`, `split` and
// `prefix(while: \.isNumber)` all step a whole Character at a time, so this port walks text
// elements rather than chars. Swift's Int is 64 bits, hence long.

using System.Globalization;
using System.Text;

namespace CalmMouse.Core;

/// <summary>
/// Dotted-version comparison for the update checker. Framework-free so it's unit-testable;
/// deliberately tiny — release tags here are always plain `MAJOR.MINOR.PATCH` (a leading `v`
/// is tolerated because the git tags carry one).
/// </summary>
public static class AppVersion
{
    /// <summary>
    /// "v0.10.1" → [0, 10, 1]. Non-numeric junk in a component keeps its leading digits
    /// ("1-beta" → 1); a fully non-numeric component is 0.
    /// </summary>
    public static long[] Components(string s)
    {
        List<string> t = TextElements(s.Trim());
        if (t.Count > 0 && (t[0] == "v" || t[0] == "V")) { t.RemoveAt(0); }

        // `split(separator: ".")` leaves out empty pieces ("1..2" → ["1", "2"], "" → []).
        var parts = new List<List<string>>();
        var current = new List<string>();
        foreach (string character in t)
        {
            if (character == ".")
            {
                if (current.Count > 0) { parts.Add(current); }
                current = [];
            }
            else
            {
                current.Add(character);
            }
        }
        if (current.Count > 0) { parts.Add(current); }

        return parts.Select(part =>
        {
            string digits = string.Concat(part.TakeWhile(IsNumber));
            // Swift's `Int(String)`: ASCII digits only, nil (here 0) when empty or too big.
            return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long value) ? value : 0;
        }).ToArray();
    }

    /// <summary>
    /// True when `candidate` is strictly newer than `current`. Missing components count as 0,
    /// so "0.5" == "0.5.0" and neither is newer than the other.
    /// </summary>
    public static bool IsNewer(string candidate, string current)
    {
        long[] a = Components(candidate);
        long[] b = Components(current);
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            long x = i < a.Length ? a[i] : 0;
            long y = i < b.Length ? b[i] : 0;
            if (x != y) { return x > y; }
        }
        return false;
    }

    private static List<string> TextElements(string s)
    {
        var elements = new List<string>();
        TextElementEnumerator e = StringInfo.GetTextElementEnumerator(s);
        while (e.MoveNext()) { elements.Add(e.GetTextElement()); }
        return elements;
    }

    /// <summary>
    /// Swift's `Character.isNumber`: the character's first scalar has a numeric value.
    /// (Swift also counts CJK numerals such as 五, which .NET's tables don't; either way such a
    /// component is not plain ASCII digits and parses as 0, unless it follows ASCII digits.)
    /// </summary>
    private static bool IsNumber(string character) =>
        Rune.TryGetRuneAt(character, 0, out Rune first) && Rune.GetNumericValue(first) != -1;
}

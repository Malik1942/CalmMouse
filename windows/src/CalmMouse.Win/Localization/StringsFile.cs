// Reads Apple's .strings tables (the Mac app's Resources/*.lproj/Localizable.strings and the
// Windows-only ones under Strings/), and turns their printf-style format specifiers into .NET
// composite format strings.
//
// The format: `"key" = "value";` entries, with /* block */ and // line comments anywhere between
// tokens. Strings take the escapes \" \\ \n \t \r \' \0 and \Uxxxx / \uxxxx. `"key";` alone means
// the value is the key. Files may be UTF-8 (with or without a BOM) or UTF-16 with a BOM.

using System.Globalization;
using System.Text;

namespace CalmMouse.Win.Localization;

public static class StringsFile
{
    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];
    private static ReadOnlySpan<byte> Utf16LeBom => [0xFF, 0xFE];
    private static ReadOnlySpan<byte> Utf16BeBom => [0xFE, 0xFF];

    /// <summary>Decodes a .strings file's bytes, honouring a UTF-8 or UTF-16 (LE or BE) byte-order mark.</summary>
    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(Utf8Bom)) { return Encoding.UTF8.GetString(bytes[3..]); }
        if (bytes.StartsWith(Utf16LeBom)) { return Encoding.Unicode.GetString(bytes[2..]); }
        if (bytes.StartsWith(Utf16BeBom)) { return Encoding.BigEndianUnicode.GetString(bytes[2..]); }
        return Encoding.UTF8.GetString(bytes);
    }

    public static Dictionary<string, string> Parse(ReadOnlySpan<byte> bytes) => Parse(Decode(bytes));

    /// <summary>
    /// Every entry in <paramref name="text"/>, later entries winning over earlier ones with the same
    /// key. Throws <see cref="FormatException"/> (with the line number) on anything malformed.
    /// </summary>
    public static Dictionary<string, string> Parse(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var p = new Parser(text);
        while (true)
        {
            p.SkipTrivia();
            if (p.AtEnd) { break; }
            string key = p.ReadToken();
            p.SkipTrivia();
            string value;
            if (p.TryRead(';'))
            {
                value = key;
            }
            else
            {
                p.Expect('=');
                p.SkipTrivia();
                value = p.ReadToken();
                p.SkipTrivia();
                p.Expect(';');
            }
            result[key] = value;
        }
        return result;
    }

    private ref struct Parser(string text)
    {
        private readonly string _text = text;
        private int _at = text.Length > 0 && text[0] == '﻿' ? 1 : 0;

        public readonly bool AtEnd => _at >= _text.Length;

        public void SkipTrivia()
        {
            while (_at < _text.Length)
            {
                char c = _text[_at];
                if (char.IsWhiteSpace(c)) { _at++; continue; }
                if (c == '/' && _at + 1 < _text.Length && _text[_at + 1] == '*')
                {
                    int end = _text.IndexOf("*/", _at + 2, StringComparison.Ordinal);
                    if (end < 0) { throw Error("a /* comment is never closed"); }
                    _at = end + 2;
                    continue;
                }
                if (c == '/' && _at + 1 < _text.Length && _text[_at + 1] == '/')
                {
                    int end = _text.IndexOf('\n', _at);
                    _at = end < 0 ? _text.Length : end + 1;
                    continue;
                }
                break;
            }
        }

        public bool TryRead(char c)
        {
            if (_at < _text.Length && _text[_at] == c) { _at++; return true; }
            return false;
        }

        public void Expect(char c)
        {
            if (!TryRead(c)) { throw Error(AtEnd ? $"expected '{c}' but the file ended" : $"expected '{c}' but found '{_text[_at]}'"); }
        }

        /// <summary>A quoted string, or a bare word of letters, digits and _ . - $ : /.</summary>
        public string ReadToken()
        {
            if (AtEnd) { throw Error("expected a string but the file ended"); }
            if (_text[_at] == '"') { return ReadQuoted(); }
            int start = _at;
            while (_at < _text.Length && IsBareChar(_text[_at])) { _at++; }
            if (_at == start) { throw Error($"expected a string but found '{_text[_at]}'"); }
            return _text[start.._at];
        }

        private static bool IsBareChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '.' or '-' or '$' or ':' or '/';

        private string ReadQuoted()
        {
            int openedAt = _at;
            _at++;   // the opening quote
            var sb = new StringBuilder();
            while (true)
            {
                if (_at >= _text.Length)
                {
                    _at = openedAt;
                    throw Error("a string is never closed");
                }
                char c = _text[_at++];
                if (c == '"') { return sb.ToString(); }
                if (c != '\\') { sb.Append(c); continue; }
                if (_at >= _text.Length) { continue; }   // reported as unclosed on the next turn
                char e = _text[_at++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case '0': sb.Append('\0'); break;
                    case 'U':
                    case 'u':
                        if (_at + 4 <= _text.Length && ushort.TryParse(_text.AsSpan(_at, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort code))
                        {
                            sb.Append((char)code);
                            _at += 4;
                        }
                        else
                        {
                            throw Error($"\\{e} must be followed by four hex digits");
                        }
                        break;
                    default: sb.Append(e); break;   // \" \\ \' and anything else: the character itself
                }
            }
        }

        private readonly FormatException Error(string message)
        {
            int line = 1;
            for (int i = 0; i < _at && i < _text.Length; i++) { if (_text[i] == '\n') { line++; } }
            return new FormatException($"Localizable.strings line {line}: {message}");
        }
    }

    /// <summary>
    /// Turns an Apple (printf-style) format string into a .NET composite format string:
    /// <c>%@ %d %ld %i %u %s %c</c> → <c>{n}</c>, positional <c>%1$@</c> → <c>{0}</c>, <c>%.1f</c> →
    /// <c>{n:F1}</c>, <c>%x</c> → <c>{n:x}</c>, a width → <c>{n,width}</c> (<c>-</c> left-aligns), <c>%%</c> → <c>%</c>.
    /// Literal braces are doubled. Anything that isn't a specifier stays as it is.
    /// </summary>
    public static string ConvertFormat(string apple)
    {
        var sb = new StringBuilder(apple.Length + 8);
        int next = 0;
        int i = 0;
        while (i < apple.Length)
        {
            char c = apple[i];
            if (c == '{') { sb.Append("{{"); i++; continue; }
            if (c == '}') { sb.Append("}}"); i++; continue; }
            if (c != '%') { sb.Append(c); i++; continue; }
            if (i + 1 < apple.Length && apple[i + 1] == '%') { sb.Append('%'); i += 2; continue; }
            if (TryReadSpecifier(apple, i, ref next, out string? converted, out int length))
            {
                sb.Append(converted);
                i += length;
            }
            else
            {
                sb.Append('%');
                i++;
            }
        }
        return sb.ToString();
    }

    private static bool TryReadSpecifier(string s, int start, ref int next, out string? converted, out int length)
    {
        converted = null;
        length = 0;
        int i = start + 1;

        // Optional position: digits then '$'.
        int? position = null;
        int digitsStart = i;
        while (i < s.Length && char.IsAsciiDigit(s[i])) { i++; }
        if (i > digitsStart && i < s.Length && s[i] == '$')
        {
            position = int.Parse(s.AsSpan(digitsStart, i - digitsStart), CultureInfo.InvariantCulture) - 1;
            i++;
            if (position < 0) { return false; }
        }
        else
        {
            i = digitsStart;
        }

        bool leftAlign = false, zeroPad = false;
        while (i < s.Length && s[i] is '-' or '+' or ' ' or '#' or '0')
        {
            if (s[i] == '-') { leftAlign = true; }
            if (s[i] == '0') { zeroPad = true; }
            i++;
        }
        int widthStart = i;
        while (i < s.Length && char.IsAsciiDigit(s[i])) { i++; }
        int? width = i > widthStart ? int.Parse(s.AsSpan(widthStart, i - widthStart), CultureInfo.InvariantCulture) : null;
        int? precision = null;
        if (i < s.Length && s[i] == '.')
        {
            i++;
            int precisionStart = i;
            while (i < s.Length && char.IsAsciiDigit(s[i])) { i++; }
            precision = i > precisionStart ? int.Parse(s.AsSpan(precisionStart, i - precisionStart), CultureInfo.InvariantCulture) : 0;
        }
        while (i < s.Length && s[i] is 'h' or 'l' or 'q' or 'L' or 'z' or 't' or 'j') { i++; }
        if (i >= s.Length) { return false; }

        // printf's 0 flag pads numbers with zeros to the width ("%02ld" → "05"); "-" overrides it.
        int? zeros = zeroPad && !leftAlign ? width : null;
        string? format = s[i] switch
        {
            'd' or 'i' or 'u' => zeros is { } z ? $"D{z}" : "",
            '@' or 's' or 'S' or 'c' or 'C' => "",
            'f' or 'F' => zeros is { } fz ? FixedWithZeros(fz, precision ?? 6) : $"F{precision ?? 6}",
            'e' or 'E' => $"{s[i]}{precision ?? 6}",
            'g' or 'G' => precision is { } g ? $"G{g}" : "G",
            'x' => zeros is { } xz ? $"x{xz}" : "x",
            'X' => zeros is { } bz ? $"X{bz}" : "X",
            _ => null,
        };
        if (format == null) { return false; }
        bool padded = zeros != null && s[i] is 'd' or 'i' or 'u' or 'f' or 'F' or 'x' or 'X';
        i++;

        int index = position ?? next;
        if (position == null) { next++; }
        string alignment = width is { } w && !padded ? $",{(leftAlign ? "-" : "")}{w}" : "";
        converted = format.Length == 0 ? $"{{{index}{alignment}}}" : $"{{{index}{alignment}:{format}}}";
        length = i - start;
        return true;
    }

    /// <summary>A custom format with leading zeros: width 6, precision 2 → "000.00".</summary>
    private static string FixedWithZeros(int width, int precision)
    {
        int whole = Math.Max(1, width - (precision > 0 ? precision + 1 : 0));
        return precision > 0 ? $"{new string('0', whole)}.{new string('0', precision)}" : new string('0', whole);
    }
}

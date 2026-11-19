using System.Globalization;
using System.Windows;
using System.Windows.Data;
using CalmMouse.Win.Localization;

namespace CalmMouse.Win.SettingsUI;

/// <summary>Visible when the value equals the parameter (a page, for example); collapsed otherwise.</summary>
public sealed class EqualsToVisibility : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value, parameter) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Visible for true (or, with the parameter "not", for false).</summary>
public sealed class BoolToVisibility : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is true) != (parameter as string == "not") ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// A caption written with the Mac's line breaks (tuned for its column) as running text that
/// wraps to whatever width it gets: a space between English lines; between Chinese ones nothing,
/// unless a Latin letter or digit touches the break ("登录 Windows 时 CalmMouse 自动启动").
/// </summary>
public sealed class OneLine : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Join(value as string ?? "", Localizer.Shared.Language);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();

    public static string Join(string text, UiLanguage language)
    {
        if (!text.Contains('\n')) { return text; }
        var sb = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') { sb.Append(text[i]); continue; }
            bool latinBefore = i > 0 && char.IsAsciiLetterOrDigit(text[i - 1]);
            bool latinAfter = i + 1 < text.Length && char.IsAsciiLetterOrDigit(text[i + 1]);
            if (language == UiLanguage.English || latinBefore || latinAfter) { sb.Append(' '); }
        }
        return sb.ToString();
    }
}

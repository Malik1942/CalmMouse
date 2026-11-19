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
/// wraps to whatever width it gets: a space between English lines, nothing between Chinese ones.
/// </summary>
public sealed class OneLine : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Join(value as string ?? "", Localizer.Shared.Language);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();

    public static string Join(string text, UiLanguage language) =>
        text.Replace("\n", language == UiLanguage.English ? " " : "", StringComparison.Ordinal);
}

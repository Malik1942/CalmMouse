// Localized text in XAML that follows the language as it changes:
//
//   xmlns:loc="clr-namespace:CalmMouse.Win.Localization"
//   <TextBlock Text="{loc:L 'Smooth scrolling'}" />
//   <Border ui:Explain.Text="{loc:L 'Flips only the wheel\'s direction.&#10;Other devices stay as they are.'}" />
//
// The key is the English text, as with L() in code (a ' inside it is written \', a line break
// &#10;). The extension hands back a one-way binding to LocalizedText.Shared, whose Language
// property changes with the localizer's; the converter looks the key up again each time, so any
// key works (no binding-path escaping) and an open window changes language in place.

using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace CalmMouse.Win.Localization;

/// <summary>Tells bindings that the language changed. One per <see cref="Localizer"/>.</summary>
public sealed class LocalizedText : INotifyPropertyChanged
{
    private static readonly PropertyChangedEventArgs s_languageChanged = new(nameof(Language));

    public LocalizedText(Localizer localizer)
    {
        Localizer = localizer;
        // The localizer lives as long as the app, and so does this; nothing to unsubscribe.
        localizer.LanguageChanged += (_, _) => PropertyChanged?.Invoke(this, s_languageChanged);
    }

    /// <summary>Follows <see cref="Localizer.Shared"/>.</summary>
    public static LocalizedText Shared { get; } = new(Localizer.Shared);

    public Localizer Localizer { get; }

    /// <summary>The language now; bindings watch it to know when to look their text up again.</summary>
    public UiLanguage Language => Localizer.Language;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>A binding whose value is <paramref name="key"/>'s text, kept up to date.</summary>
    public Binding Bind(string key) => new(nameof(Language))
    {
        Source = this,
        Mode = BindingMode.OneWay,
        Converter = KeyConverter.Instance,
        ConverterParameter = new LookUp(Localizer, key),
    };

    private sealed record LookUp(Localizer Localizer, string Key);

    private sealed class KeyConverter : IValueConverter
    {
        public static readonly KeyConverter Instance = new();

        public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
            parameter is LookUp l ? l.Localizer.Get(l.Key) : "";

        public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}

/// <summary><c>{loc:L 'English text'}</c>: the text in the current language, updated when it changes.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LExtension : MarkupExtension
{
    public LExtension() { }

    public LExtension(string key) => Key = key;

    /// <summary>The English text, which is also the key in the tables.</summary>
    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    /// <summary>Where the text comes from (tests); <see cref="LocalizedText.Shared"/> by default.</summary>
    public LocalizedText? Source { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        (Source ?? LocalizedText.Shared).Bind(Key).ProvideValue(serviceProvider);
}

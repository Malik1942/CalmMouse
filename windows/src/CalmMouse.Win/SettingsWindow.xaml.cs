using System.Windows;
using CalmMouse.Win.Localization;
using static CalmMouse.Win.Localization.Strings;

namespace CalmMouse.Win;

/// <summary>The settings window. One at a time: the app keeps it and brings it to the front (see App.ShowSettings).</summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        ApplyStrings();
        Localizer.Shared.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => Localizer.Shared.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(ApplyStrings);

    private void ApplyStrings()
    {
        Title = L("CalmMouse Settings");
    }
}

using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Markup;
using System.Windows.Threading;
using CalmMouse.Win.Localization;
using CalmMouse.Win.SettingsUI;

namespace CalmMouse.Win;

/// <summary>The settings window. One at a time: the app keeps it and brings it to the front (see App.ShowSettings).</summary>
public partial class SettingsWindow : Window
{
    /// <summary>Where the window was last closed this session, so it opens there again.</summary>
    private static Rect? s_lastBounds;

    private readonly DispatcherTimer _statusTimer;

    public SettingsWindow(SettingsViewModel model)
    {
        Model = model;
        InitializeComponent();
        DataContext = model;
        SettingsUI.Explain.SetPanel(this, model.Explanation);
        model.Explanation.PropertyChanged += OnExplanationChanged;

        ApplyLanguage();
        Localizer.Shared.LanguageChanged += OnLanguageChanged;
        FitToScreen();

        // The engine's health and the conflict notice have no events of their own worth adding: a
        // look every second while the window is open is enough.
        _statusTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => model.RefreshStatus(), Dispatcher);
        _statusTimer.Start();

        // The chosen page's entry has the keyboard focus to start with: arrows change page, Tab goes into it.
        Loaded += (_, _) => (Pages.ItemContainerGenerator.ContainerFromItem(Pages.SelectedItem) as UIElement ?? Pages).Focus();
        SizeChanged += (_, _) => FitPanel();
        Closing += (_, _) => s_lastBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        Closed += (_, _) =>
        {
            _statusTimer.Stop();
            Localizer.Shared.LanguageChanged -= OnLanguageChanged;
            model.Explanation.PropertyChanged -= OnExplanationChanged;
            model.Dispose();
        };
    }

    public SettingsViewModel Model { get; }

    private void OnLanguageChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(ApplyLanguage);

    /// <summary>Tells WPF the language, so Chinese text is drawn with the right font (Simplified or Traditional forms).</summary>
    private void ApplyLanguage() => Language = XmlLanguage.GetLanguage(Localizer.Shared.Culture.IetfLanguageTag);

    /// <summary>Where it was last time, else centred; never bigger than the screen's work area (high scaling on a small screen).</summary>
    private void FitToScreen()
    {
        Rect work = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, work.Width);
        MinHeight = Math.Min(MinHeight, work.Height);
        if (s_lastBounds is { } last && work.IntersectsWith(last))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = Math.Min(last.Width, work.Width);
            Height = Math.Min(last.Height, work.Height);
            Left = Math.Clamp(last.Left, work.Left, Math.Max(work.Left, work.Right - Width));
            Top = Math.Clamp(last.Top, work.Top, Math.Max(work.Top, work.Bottom - Height));
        }
        else
        {
            Width = Math.Min(Width, work.Width);
            Height = Math.Min(Height, work.Height);
        }
    }

    /// <summary>
    /// Narrower than the three columns need (high scaling on a small screen): the page keeps the
    /// room and the explanation panel steps aside.
    /// </summary>
    private void FitPanel()
    {
        bool narrow = ActualWidth < NarrowWidth;
        PanelColumn.Width = new GridLength(narrow ? 0 : 260);
        ExplanationPanel.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The window's width (frame included) below which the three columns don't fit: their minimum is 770.</summary>
    private const double NarrowWidth = 790;

    /// <summary>A screen reader hears the new explanation when the keyboard focus moves to another setting.</summary>
    private void OnExplanationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ExplainState.Text) || !AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged)) { return; }
        AutomationPeer peer = UIElementAutomationPeer.FromElement(ExplanationText) ?? UIElementAutomationPeer.CreatePeerForElement(ExplanationText);
        peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}

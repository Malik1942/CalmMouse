// The notification-area (tray) icon and its menu, the Windows counterpart of the Mac's menu bar
// icon (AppDelegate.refreshStatusIcon). Always the same mouse glyph, so it can be told apart in a
// crowded tray; the state shows in how it's drawn:
//
//   normal   filled                         tooltip "CalmMouse — active"
//   paused   outline only, dimmed           "CalmMouse is paused"
//   problem  filled, orange                 one line saying what's wrong, in plain words
//
// Only a deliberate pause dims it; something that needs attention stays at full strength.
// The icons (Assets/Tray, made by make-icons.ps1) come in a dark-taskbar and a light-taskbar
// version; the right one follows Windows' "SystemUsesLightTheme" and switches when it changes.
//
// Left click opens Settings; right click opens the menu: Pause / Resume (or Try again after a
// problem), Settings…, Quit. The menu is rebuilt whenever the language changes.

using System.Drawing;
using System.IO;
using System.Windows.Controls;
using H.NotifyIcon;
using Microsoft.Win32;
using static CalmMouse.Win.Localization.Strings;

namespace CalmMouse.Win.Tray;

public enum TrayState
{
    Normal,
    Paused,
    Problem,
}

/// <summary>What the tray shows: the state, the tooltip, and what the first menu item does.</summary>
/// <param name="ProblemText">The plain-language line for a problem, shown in the tooltip and at the top of the menu.</param>
/// <param name="CanRetry">The engine stopped: the first item says "Try again" rather than Pause.</param>
public sealed record TrayView(TrayState State, string? ProblemText, bool CanRetry);

internal sealed class TrayIcon : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly TaskbarIcon _icon;
    private readonly Action _togglePause;
    private readonly Action _retry;
    private readonly Action _openSettings;
    private readonly Action _quit;
    private TrayView _view = new(TrayState.Normal, null, false);
    private bool _lightTaskbar;
    private Icon? _currentIcon;
    private bool _disposed;

    public TrayIcon(Action togglePause, Action retry, Action openSettings, Action quit)
    {
        _togglePause = togglePause;
        _retry = retry;
        _openSettings = openSettings;
        _quit = quit;
        _lightTaskbar = IsTaskbarLight();
        _icon = new TaskbarIcon { NoLeftClickDelay = true };
        _icon.TrayLeftMouseUp += (_, _) => _openSettings();
        Refresh();
        _icon.ForceCreate(enablesEfficiencyMode: false);
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public TrayView View => _view;

    public void Show(TrayView view)
    {
        if (_disposed || view == _view) { return; }
        _view = view;
        Refresh();
    }

    /// <summary>Rebuilds the text (after a language change).</summary>
    public void Refresh()
    {
        if (_disposed) { return; }
        SetIcon();
        _icon.ToolTipText = Tooltip(_view);
        _icon.ContextMenu = BuildMenu();
    }

    public static string Tooltip(TrayView view) => view.State switch
    {
        TrayState.Problem => view.ProblemText ?? L("CalmMouse — active"),
        TrayState.Paused => L("CalmMouse is paused"),
        _ => L("CalmMouse — active"),
    };

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        if (_view.State == TrayState.Problem && _view.ProblemText is { } problem)
        {
            menu.Items.Add(new MenuItem { Header = new TextBlock { Text = problem, TextWrapping = System.Windows.TextWrapping.Wrap, MaxWidth = 280 }, IsEnabled = false });
            menu.Items.Add(new Separator());
        }

        MenuItem first = _view.CanRetry
            ? Item(L("Try again"), _retry)
            : Item(_view.State == TrayState.Paused ? L("Resume CalmMouse") : L("Pause CalmMouse"), _togglePause);
        menu.Items.Add(first);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(L("Settings…"), _openSettings));
        menu.Items.Add(Item(L("Quit CalmMouse"), _quit));
        return menu;
    }

    private static MenuItem Item(string text, Action action)
    {
        // Header as a TextBlock so an underscore in a translation isn't taken as an access key.
        var item = new MenuItem { Header = new TextBlock { Text = text } };
        item.Click += (_, _) => action();
        return item;
    }

    private void SetIcon()
    {
        Icon icon = LoadIcon(_view.State, _lightTaskbar);
        _icon.Icon = icon;
        _currentIcon?.Dispose();
        _currentIcon = icon;
    }

    /// <summary>The embedded resource name of a state's icon (see the csproj).</summary>
    public static string IconResourceName(TrayState state, bool lightTaskbar) =>
        $"CalmMouse.Tray.{state.ToString().ToLowerInvariant()}-on-{(lightTaskbar ? "light" : "dark")}.ico";

    /// <summary>The icon at the size Windows draws tray icons now (16 px at 100 %, 20 at 125 %, 24 at 150 %…).</summary>
    public static Icon LoadIcon(TrayState state, bool lightTaskbar)
    {
        using Stream stream = typeof(TrayIcon).Assembly.GetManifestResourceStream(IconResourceName(state, lightTaskbar))
            ?? throw new InvalidOperationException($"missing tray icon {IconResourceName(state, lightTaskbar)}");
        int size = SmallIconSize();
        return new Icon(stream, size, size);
    }

    private static int SmallIconSize()
    {
        int size = Windows.Win32.PInvoke.GetSystemMetrics(Windows.Win32.UI.WindowsAndMessaging.SYSTEM_METRICS_INDEX.SM_CXSMICON);
        return size > 0 ? size : 16;
    }

    /// <summary>Whether the taskbar is light (Settings › Personalization › Colors › "Choose your mode": Windows mode light).</summary>
    public static bool IsTaskbarLight()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Raised on SystemEvents' own thread; the colour mode arrives as category General.
        _icon.Dispatcher.BeginInvoke(() =>
        {
            bool light = IsTaskbarLight();
            if (light == _lightTaskbar || _disposed) { return; }
            _lightTaskbar = light;
            SetIcon();
        });
    }

    /// <summary>Removes the icon from the tray.</summary>
    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _icon.Dispose();
        _currentIcon?.Dispose();
    }
}

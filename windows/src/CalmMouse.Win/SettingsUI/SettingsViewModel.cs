// The settings window's state. Every change is saved at once through the SettingsStore, which
// also tells the running engine (its Changed event), so there is no Apply or OK button. Changes
// made elsewhere (the tray's Pause, a second window) come back through SettingsChanged and are
// shown here.
//
// The view model never touches WPF controls, and every outside event is handed to `post` (the
// window's Dispatcher), so the tests can drive it on any thread.

using System.Collections.ObjectModel;
using System.Windows.Media;
using CalmMouse.Core;
using CalmMouse.Core.Routing;
using CalmMouse.Win.Input;
using CalmMouse.Win.Localization;
using CalmMouse.Win.Settings;
using static CalmMouse.Win.Localization.Strings;

namespace CalmMouse.Win.SettingsUI;

public enum SettingsPage
{
    General,
    Devices,
    AppRules,
}

public sealed class SettingsViewModel : Observable, IDisposable
{
    private readonly SettingsStore _store;
    private readonly ISettingsHost _host;
    private readonly IAppCatalog _apps;
    private readonly Action<Action> _post;
    private SettingsPage _page;
    private bool _paused;
    private bool _launchAtLogin;
    private AppLanguage _language;
    private EngineHealth _health = EngineHealth.Fine;
    private string? _conflictMessage;
    private bool _disposed;

    /// <param name="post">Runs an action on the window's thread (Dispatcher.BeginInvoke); events from other threads go through it.</param>
    public SettingsViewModel(SettingsStore store, ISettingsHost host, IAppCatalog apps, Action<Action> post)
    {
        _store = store;
        _host = host;
        _apps = apps;
        _post = post;
        LanguageOptions =
        [
            new LanguageOption(AppLanguage.System, () => L("Same as Windows")),
            // Each language by its own name, as Windows lists them.
            new LanguageOption(AppLanguage.English, () => "English"),
            new LanguageOption(AppLanguage.SimplifiedChinese, () => "简体中文"),
            new LanguageOption(AppLanguage.TraditionalChinese, () => "繁體中文"),
        ];
        RetryCommand = new Command(_host.RetryEngine);

        LoadSettings();
        RefreshDevices();
        RefreshStatus();
        Explanation.Reset(Intro);

        _store.SettingsChanged += OnSettingsChanged;
        _host.DevicesChanged += OnDevicesChanged;
        _host.ConflictsChanged += OnConflictsChanged;
        Localizer.Shared.LanguageChanged += OnLanguageChanged;
    }

    // MARK: Pages and the explanation panel

    /// <summary>What the side panel shows.</summary>
    public ExplainState Explanation { get; } = new();

    public SettingsPage Page
    {
        get => _page;
        set
        {
            if (!Set(ref _page, value)) { return; }
            Explanation.Reset(Intro);
        }
    }

    /// <summary>What the panel says before anything is pointed at, on every page (the Mac's words).</summary>
    public static string Intro => L("Point at a setting\nto see what it does.");

    /// <summary>The App rules page's line under its title: how a rule picks its app.</summary>
    public string AppRulesSubtitle => OneLine.Join(AppRulesExplained, Localizer.Shared.Language);

    /// <summary>Rules follow the window under the pointer, not the app in front (spec 1.3).</summary>
    public static string AppRulesExplained =>
        L("Rules follow the window under the pointer,\nthe one the wheel scrolls,\neven when another app is in front.");

    // MARK: General

    /// <summary>Paused, as the tray's Pause: every mouse scrolls as usual.</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value) { return; }
            _paused = value;
            _host.SetPaused(value);
            Raise();
        }
    }

    public bool LaunchAtLogin
    {
        get => _launchAtLogin;
        set
        {
            if (_launchAtLogin == value) { return; }
            _launchAtLogin = value;
            _store.SetLaunchAtLogin(value);
            Raise();
        }
    }

    public IReadOnlyList<LanguageOption> LanguageOptions { get; }

    public AppLanguage Language
    {
        get => _language;
        set
        {
            if (_language == value) { return; }
            _language = value;
            _store.SetLanguage(value);   // the app applies it (App.OnSettingsChanged)
            Raise();
        }
    }

    /// <summary>The engine's problem in plain words, or null when it's running (or deliberately paused).</summary>
    public string? ProblemText => _health.Text;

    public bool HasProblem => _health.Text != null;

    public bool CanRetry => _health.Text != null && _health.CanRetry;

    public Command RetryCommand { get; }

    /// <summary>Another tool that changes scrolling is running, or null.</summary>
    public string? ConflictMessage => _conflictMessage;

    public bool HasConflict => _conflictMessage != null;

    /// <summary>Reads the engine's health and the conflict notice again (the window does every second).</summary>
    public void RefreshStatus()
    {
        EngineHealth health = _host.Health;
        if (health != _health)
        {
            _health = health;
            Raise(nameof(ProblemText));
            Raise(nameof(HasProblem));
            Raise(nameof(CanRetry));
        }
        string? conflict = _host.ConflictMessage;
        if (conflict != _conflictMessage)
        {
            _conflictMessage = conflict;
            Raise(nameof(ConflictMessage));
            Raise(nameof(HasConflict));
        }
    }

    // MARK: Devices

    /// <summary>One card per connected mouse: wheel mice first, then the rest, then touchpads.</summary>
    public ObservableCollection<DeviceCardViewModel> Devices { get; } = [];

    public bool HasNoDevices => Devices.Count == 0;

    /// <summary>
    /// The engine's list as cards: one per stable key (a receiver can list one mouse more than
    /// once), wheel mice, then unknown devices, then touchpads, each by name.
    /// </summary>
    public static IReadOnlyList<MouseDeviceInfo> CardsFor(IReadOnlyList<MouseDeviceInfo> devices) =>
        devices
            .GroupBy(CardKey, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(d => d.Kind switch { DeviceKind.WheelMouse => 0, DeviceKind.Unknown => 1, _ => 2 })
            .ThenBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(CardKey, StringComparer.Ordinal)
            .ToList();

    /// <summary>A card's identity: the stable key, or the handle for a device without one.</summary>
    public static string CardKey(MouseDeviceInfo device) =>
        device.StableKey.Length > 0 ? $"{device.Kind}:{device.StableKey}" : $"{device.Kind}#{device.DeviceId}";

    private void RefreshDevices()
    {
        IReadOnlyList<MouseDeviceInfo> wanted = CardsFor(_host.Devices);
        // Keep the cards that are still there (and with them the keyboard focus), in the new order.
        var existing = Devices.ToDictionary(c => c.CardKey, StringComparer.Ordinal);
        var keep = new HashSet<string>(wanted.Select(CardKey), StringComparer.Ordinal);
        for (int i = Devices.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(Devices[i].CardKey)) { Devices.RemoveAt(i); }
        }
        for (int i = 0; i < wanted.Count; i++)
        {
            string key = CardKey(wanted[i]);
            DeviceCardViewModel card = existing.TryGetValue(key, out DeviceCardViewModel? found) ? found : new DeviceCardViewModel(_store, wanted[i]);
            int at = Devices.IndexOf(card);
            if (at < 0) { Devices.Insert(i, card); }
            else if (at != i) { Devices.Move(at, i); }
        }
        Raise(nameof(HasNoDevices));
    }

    // MARK: App rules

    public ObservableCollection<AppRuleViewModel> Rules { get; } = [];

    public bool HasNoRules => Rules.Count == 0;

    /// <summary>The apps "Add an app…" offers: open now, without a rule yet.</summary>
    public IReadOnlyList<AppCandidate> AppsToAdd()
    {
        var ruled = new HashSet<string>(Rules.Select(r => r.ExeName), StringComparer.OrdinalIgnoreCase);
        return _apps.RunningApps().Where(a => !ruled.Contains(a.ExeName)).ToList();
    }

    /// <summary>The app in an .exe the person picked.</summary>
    public AppCandidate DescribeProgram(string exePath) => _apps.Describe(exePath);

    /// <summary>An app's icon for the menu, or null.</summary>
    public ImageSource? IconFor(AppCandidate app) => _apps.IconFor(app.ExeName, app.Path);

    /// <summary>
    /// Adds a rule for <paramref name="app"/>, or returns the one it already has. A new rule is on
    /// and set to "Don't smooth scrolling in this app": the person asked for it, and a rule that
    /// changes nothing would make no sense.
    /// </summary>
    public AppRuleViewModel AddRule(AppCandidate app)
    {
        AppRuleViewModel? existing = Rules.FirstOrDefault(r => r.ExeName.Equals(app.ExeName, StringComparison.OrdinalIgnoreCase));
        if (existing != null) { return existing; }
        var rule = new WheelAppRule(app.ExeName.ToLowerInvariant(), app.DisplayName) { NoSmoothing = true };
        var added = new AppRuleViewModel(this, rule, _apps.IconFor(rule.ExeName, app.Path));
        Rules.Add(added);
        Raise(nameof(HasNoRules));
        SaveRules();
        return added;
    }

    public void RemoveRule(AppRuleViewModel rule)
    {
        if (!Rules.Remove(rule)) { return; }
        Raise(nameof(HasNoRules));
        SaveRules();
    }

    internal void SaveRules() => _store.SetAppRules(Rules.Select(r => r.ToRule()));

    private void LoadRules(IReadOnlyList<WheelAppRule> rules)
    {
        var byExe = Rules.ToDictionary(r => r.ExeName, StringComparer.OrdinalIgnoreCase);
        var wanted = rules.GroupBy(r => r.ExeName, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        var keep = new HashSet<string>(wanted.Select(r => r.ExeName), StringComparer.OrdinalIgnoreCase);
        for (int i = Rules.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(Rules[i].ExeName)) { Rules.RemoveAt(i); }
        }
        for (int i = 0; i < wanted.Count; i++)
        {
            if (byExe.TryGetValue(wanted[i].ExeName, out AppRuleViewModel? vm))
            {
                vm.Load(wanted[i]);
                int at = Rules.IndexOf(vm);
                if (at != i) { Rules.Move(at, i); }
            }
            else
            {
                Rules.Insert(i, new AppRuleViewModel(this, wanted[i], _apps.IconFor(wanted[i].ExeName, null)));
            }
        }
        Raise(nameof(HasNoRules));
    }

    // MARK: Keeping up

    private void LoadSettings()
    {
        AppSettings s = _store.Current;
        Set(ref _paused, s.Paused, nameof(Paused));
        Set(ref _launchAtLogin, s.LaunchAtLogin, nameof(LaunchAtLogin));
        Set(ref _language, s.Language, nameof(Language));
        foreach (DeviceCardViewModel card in Devices) { card.Load(); }
        LoadRules(s.AppRules);
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => _post(() => { if (!_disposed) { LoadSettings(); } });

    private void OnDevicesChanged(object? sender, EventArgs e) => _post(() => { if (!_disposed) { RefreshDevices(); } });

    private void OnConflictsChanged(object? sender, EventArgs e) => _post(() => { if (!_disposed) { RefreshStatus(); } });

    private void OnLanguageChanged(object? sender, EventArgs e) => _post(() =>
    {
        if (_disposed) { return; }
        Explanation.Intro = Intro;
        Raise(nameof(AppRulesSubtitle));
        foreach (LanguageOption option in LanguageOptions) { option.Refresh(); }
        foreach (DeviceCardViewModel card in Devices) { card.Refresh(); }
        // The engine's line and the conflict notice are built from L(), so build them again.
        _health = EngineHealth.Fine;
        _conflictMessage = null;
        RefreshStatus();
    });

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        _store.SettingsChanged -= OnSettingsChanged;
        _host.DevicesChanged -= OnDevicesChanged;
        _host.ConflictsChanged -= OnConflictsChanged;
        Localizer.Shared.LanguageChanged -= OnLanguageChanged;
    }
}

/// <summary>One choice in Language. The name of "Same as Windows" follows the language.</summary>
public sealed class LanguageOption(AppLanguage value, Func<string> name) : Observable
{
    public AppLanguage Value { get; } = value;

    public string Name => name();

    internal void Refresh() => Raise(nameof(Name));

    public override string ToString() => Name;
}

/// <summary>A connected mouse and, for a wheel mouse, its own settings.</summary>
public sealed class DeviceCardViewModel : Observable
{
    private readonly SettingsStore _store;
    private WheelSettings _wheel;

    public DeviceCardViewModel(SettingsStore store, MouseDeviceInfo device)
    {
        _store = store;
        Device = device;
        CardKey = SettingsViewModel.CardKey(device);
        _wheel = store.SettingsFor(device.StableKey, device.DisplayName);
    }

    public MouseDeviceInfo Device { get; }

    public string CardKey { get; }

    public bool IsWheelMouse => Device.Kind == DeviceKind.WheelMouse;

    public bool IsTouchpad => Device.Kind == DeviceKind.Touchpad;

    public bool IsUnknown => Device.Kind == DeviceKind.Unknown;

    /// <summary>Its name, or what it is when Windows has no name for it.</summary>
    public string Name => Device.DisplayName.Length > 0 ? Device.DisplayName : KindLabel;

    public bool HasKindLabel => Device.Kind != DeviceKind.Unknown;

    /// <summary>"Wheel mouse", "Touchpad", or nothing for a device CalmMouse doesn't know.</summary>
    public string KindLabel => Device.Kind switch
    {
        DeviceKind.WheelMouse => L("Wheel mouse"),
        DeviceKind.Touchpad => L("Touchpad"),
        _ => "",
    };

    public bool Smooth
    {
        get => _wheel.Smooth;
        set => Change(w => w with { Smooth = value });
    }

    /// <summary>How far a notch glides, 1–12 (the router's range), only shown while Smooth is on.</summary>
    public double Speed
    {
        get => _wheel.Speed;
        set => Change(w => w with { Speed = Math.Round(Math.Clamp(value, MinimumSpeed, MaximumSpeed) * 4) / 4 });
    }

    public static double MinimumSpeed => ScrollRouter.MinimumSpeed;

    public static double MaximumSpeed => ScrollRouter.MaximumSpeed;

    /// <summary>Flips only this mouse's wheel.</summary>
    public bool Reverse
    {
        get => _wheel.Reverse;
        set => Change(w => w with { Reverse = value });
    }

    public bool BlockWhilePressed
    {
        get => _wheel.BlockWhilePressed;
        set => Change(w => w with { BlockWhilePressed = value });
    }

    private void Change(Func<WheelSettings, WheelSettings> change)
    {
        WheelSettings next = change(_wheel);
        if (next == _wheel) { return; }
        _store.UpdateMouse(Device.StableKey, Device.DisplayName, change);
        Load();
    }

    /// <summary>Reads this mouse's settings from the store again.</summary>
    internal void Load()
    {
        WheelSettings now = _store.SettingsFor(Device.StableKey, Device.DisplayName);
        if (now == _wheel) { return; }
        WheelSettings was = _wheel;
        _wheel = now;
        if (was.Smooth != now.Smooth) { Raise(nameof(Smooth)); }
        if (was.Speed != now.Speed) { Raise(nameof(Speed)); }
        if (was.Reverse != now.Reverse) { Raise(nameof(Reverse)); }
        if (was.BlockWhilePressed != now.BlockWhilePressed) { Raise(nameof(BlockWhilePressed)); }
    }

    /// <summary>The language changed.</summary>
    internal void Refresh()
    {
        Raise(nameof(Name));
        Raise(nameof(KindLabel));
    }
}

/// <summary>One app rule: on or off, and one of its two choices.</summary>
public sealed class AppRuleViewModel : Observable
{
    private readonly SettingsViewModel _owner;
    private WheelAppRule _rule;

    public AppRuleViewModel(SettingsViewModel owner, WheelAppRule rule, ImageSource? icon)
    {
        _owner = owner;
        _rule = rule;
        Icon = icon;
        RemoveCommand = new Command(() => _owner.RemoveRule(this));
    }

    public string ExeName => _rule.ExeName;

    public string DisplayName => string.IsNullOrWhiteSpace(_rule.DisplayName) ? _rule.ExeName : _rule.DisplayName;

    /// <summary>The app's icon, or null (the window draws a plain one).</summary>
    public ImageSource? Icon { get; }

    public bool HasIcon => Icon != null;

    public Command RemoveCommand { get; }

    public bool Enabled
    {
        get => _rule.Enabled;
        set => Change(_rule with { Enabled = value });
    }

    /// <summary>"Don't smooth scrolling in this app". Choosing it unchooses the other.</summary>
    public bool NoSmoothing
    {
        get => _rule.NoSmoothing && !_rule.LeaveAlone;
        set
        {
            if (value) { Change(_rule with { NoSmoothing = true, LeaveAlone = false }); }
        }
    }

    /// <summary>"Leave scrolling alone in this app". Choosing it unchooses the other.</summary>
    public bool LeaveAlone
    {
        get => _rule.LeaveAlone;
        set
        {
            if (value) { Change(_rule with { LeaveAlone = true, NoSmoothing = false }); }
        }
    }

    public WheelAppRule ToRule() => _rule;

    private void Change(WheelAppRule next)
    {
        if (next == _rule) { return; }
        Load(next);
        _owner.SaveRules();
    }

    internal void Load(WheelAppRule rule)
    {
        WheelAppRule was = _rule;
        _rule = rule;
        if (was.Enabled != rule.Enabled) { Raise(nameof(Enabled)); }
        if (was.DisplayName != rule.DisplayName) { Raise(nameof(DisplayName)); }
        Raise(nameof(NoSmoothing));
        Raise(nameof(LeaveAlone));
    }
}

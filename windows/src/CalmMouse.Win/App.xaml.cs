// CalmMouse for Windows: a tray app with no main window.
//
// Start: one instance per user (a second launch opens the first one's Settings and exits), the
// settings file, the language, launch at login kept pointing at this exe, the scroll engine, the
// tray icon, and the watch for other mouse software. The first launch ever opens Settings once.
// Quit (tray menu, `--quit`, or signing out): the icon goes, the engine stops (hook removed
// first, so scrolling is never left broken), settings are written.

using System.Windows;
using System.Windows.Threading;
using CalmMouse.Core;
using CalmMouse.Core.Routing;
using CalmMouse.Win.Conflicts;
using CalmMouse.Win.Input;
using CalmMouse.Win.Localization;
using CalmMouse.Win.Settings;
using CalmMouse.Win.Tray;
using static CalmMouse.Win.Localization.Strings;

namespace CalmMouse.Win;

public partial class App : Application
{
    public const string QuitSwitch = "--quit";

    private SingleInstance? _instance;
    private AppLog? _log;
    private SettingsStore? _store;
    private LaunchAtLogin? _launchAtLogin;
    private InputThread? _engine;
    private TrayIcon? _tray;
    private ConflictWatcher? _conflicts;
    private DispatcherTimer? _statusTimer;
    private SettingsWindow? _settingsWindow;
    private EngineProblem _problem;
    private bool _quitting;

    private enum EngineProblem
    {
        None,
        CouldNotStart,
        Faulted,
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Temporary developer switch (plan task 16): the engine with every feature off, logging to %TEMP%.
        if (e.Args.Contains(EngineTest.Switch, StringComparer.OrdinalIgnoreCase))
        {
            EngineTest.Run(this, e.Args);
            return;
        }

        string instanceName = SingleInstance.DefaultName;
        if (e.Args.Contains(QuitSwitch, StringComparer.OrdinalIgnoreCase))
        {
            Shutdown(SingleInstance.SignalQuit(instanceName, TimeSpan.Zero) ? 0 : 1);
            return;
        }
        _instance = SingleInstance.TryAcquire(instanceName);
        if (_instance == null)
        {
            // Already running: let it come to the front with its Settings window.
            global::Windows.Win32.PInvoke.AllowSetForegroundWindow(unchecked((uint)-1));   // ASFW_ANY
            SingleInstance.SignalShowSettings(instanceName);
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _log = new AppLog(AppLog.DefaultPath);
        _log.Write($"CalmMouse {typeof(App).Assembly.GetName().Version} starting, pid {Environment.ProcessId}, Windows {Environment.OSVersion.Version}");
        DispatcherUnhandledException += (_, args) => _log?.Write($"unhandled exception on the UI thread: {args.Exception}");

        _store = new SettingsStore(SettingsStore.DefaultPath, log: _log.Write);
        AppSettings settings = _store.Current;
        if (_store.IsFirstLaunch || _store.CorruptBackupPath != null) { _store.SaveNow(); }
        Localizer.Shared.Apply(settings.Language);
        Localizer.Shared.LanguageChanged += (_, _) => _tray?.Refresh();

        if (Environment.ProcessPath is { } exe)
        {
            _launchAtLogin = new LaunchAtLogin(exe);
            try
            {
                if (_launchAtLogin.Repair(settings.LaunchAtLogin)) { _log.Write($"launch at login now starts {exe}"); }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
            {
                _log.Write($"couldn't update launch at login: {ex.Message}");
            }
        }

        _engine = new InputThread(new InputThreadOptions { Settings = _store });
        _engine.Faulted += (_, ex) => Dispatcher.BeginInvoke(() => OnEngineFaulted(ex));
        _engine.DevicesChanged += (_, _) => _conflicts?.RefreshSoon();
        _engine.SetPaused(settings.Paused);
        StartEngine();

        _tray = new TrayIcon(TogglePause, RetryEngine, ShowSettings, Quit);
        UpdateTray();

        _conflicts = new ConflictWatcher(ActiveAreas);
        _conflicts.ConflictsChanged += (_, _) => _log?.Write(_conflicts.Conflicts.Count == 0
            ? "other mouse software: none"
            : $"other mouse software: {string.Join(", ", _conflicts.Conflicts.Select(c => c.Name))}");
        _conflicts.Start();

        _store.SettingsChanged += (_, _) => Dispatcher.BeginInvoke(OnSettingsChanged);
        _instance.ShowSettingsRequested += (_, _) => Dispatcher.BeginInvoke(ShowSettings);
        _instance.QuitRequested += (_, _) => Dispatcher.BeginInvoke(Quit);
        SessionEnding += (_, _) => Quit();

        _statusTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Poll(), Dispatcher);
        _statusTimer.Start();

        _log.Write($"started: settings {_store.Path}{(_store.IsFirstLaunch ? " (first launch)" : "")}, " +
                   $"language {Localizer.Shared.Language}, paused {settings.Paused}, engine {(_problem == EngineProblem.None ? "running" : "not running")}");
        if (_store.IsFirstLaunch) { ShowSettings(); }
    }

    // MARK: Engine

    private void StartEngine()
    {
        if (_engine == null) { return; }
        try
        {
            _engine.Start();
            _problem = EngineProblem.None;
        }
        catch (Exception ex)
        {
            _problem = EngineProblem.CouldNotStart;
            _log?.Write($"the engine couldn't start: {ex}");
        }
        FlushEngineLog();
    }

    private void OnEngineFaulted(Exception ex)
    {
        if (_quitting) { return; }
        _problem = EngineProblem.Faulted;
        FlushEngineLog();
        _log?.Write($"the engine stopped after a fault: {ex.Message}");
        UpdateTray();
    }

    private void RetryEngine()
    {
        _log?.Write("trying the engine again");
        StartEngine();
        UpdateTray();
    }

    private void TogglePause()
    {
        if (_store == null) { return; }
        bool paused = !_store.Current.Paused;
        _store.SetPaused(paused);   // OnSettingsChanged tells the engine and the tray
        // Resuming after a problem also tries the engine again.
        if (!paused && _problem != EngineProblem.None) { RetryEngine(); }
    }

    private void OnSettingsChanged()
    {
        if (_store == null || _engine == null || _quitting) { return; }
        AppSettings settings = _store.Current;
        if (_engine.IsPaused != settings.Paused)
        {
            _engine.SetPaused(settings.Paused);
            _log?.Write(settings.Paused ? "paused" : "resumed");
        }
        Localizer.Shared.Apply(settings.Language);
        if (_launchAtLogin is { } launch)
        {
            try { launch.Apply(settings.LaunchAtLogin); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
            {
                _log?.Write($"couldn't change launch at login: {ex.Message}");
            }
        }
        _conflicts?.RefreshSoon();
        UpdateTray();
    }

    /// <summary>What CalmMouse is changing now, for the conflict check (any thread).</summary>
    private IReadOnlySet<MouseUtilityArea> ActiveAreas()
    {
        if (_store is not { } store || _engine is not { } engine) { return new HashSet<MouseUtilityArea>(); }
        IEnumerable<WheelSettings> wheels = engine.Devices
            .Where(d => d.Kind == DeviceKind.WheelMouse)
            .Select(d => store.SettingsFor(d.StableKey, d.DisplayName));
        return WindowsMouseUtilities.ActiveAreas(store.Current.Paused || _problem != EngineProblem.None, wheels);
    }

    private void Poll()
    {
        FlushEngineLog();
        UpdateTray();
    }

    private void FlushEngineLog()
    {
        if (_engine == null || _log == null) { return; }
        foreach (string line in _engine.TakeMessages()) { _log.WriteEngine(line); }
    }

    private void UpdateTray()
    {
        if (_tray == null || _store == null || _engine == null) { return; }
        bool paused = _store.Current.Paused;
        TrayView view = _problem switch
        {
            EngineProblem.CouldNotStart when !paused => new(TrayState.Problem, L("CalmMouse couldn't start. Your mouse scrolls as usual."), CanRetry: true),
            EngineProblem.Faulted when !paused => new(TrayState.Problem, L("CalmMouse stopped because of a problem. Your mouse scrolls as usual."), CanRetry: true),
            _ when paused => new(TrayState.Paused, null, false),
            _ when _engine.Status.BreakerOpen =>
                new(TrayState.Problem, L("Something on this PC is blocking CalmMouse, so your mouse scrolls as usual for now. It will try again soon."), CanRetry: false),
            _ => new(TrayState.Normal, null, false),
        };
        if (view != _tray.View) { _tray.Show(view); }
    }

    // MARK: Windows

    private void ShowSettings()
    {
        if (_quitting) { return; }
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow();
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        if (_settingsWindow.WindowState == WindowState.Minimized) { _settingsWindow.WindowState = WindowState.Normal; }
        _settingsWindow.Activate();
        // Activate alone can leave it behind the foreground app; a moment on top brings it forward.
        _settingsWindow.Topmost = true;
        _settingsWindow.Topmost = false;
        _settingsWindow.Focus();
    }

    // MARK: Quitting

    private void Quit()
    {
        CleanUp();
        Shutdown();
    }

    private void CleanUp()
    {
        if (_quitting || _instance == null) { return; }
        _quitting = true;
        _log?.Write("quitting");
        _statusTimer?.Stop();
        _tray?.Dispose();
        _conflicts?.Dispose();
        if (_engine != null)
        {
            _engine.Stop();   // hook removed first, then anything held is sent
            FlushEngineLog();
            _engine.Dispose();
        }
        _settingsWindow?.Close();
        _store?.Dispose();    // writes anything pending
        _instance?.Dispose();
        _log?.Write("quit");
        _log?.Dispose();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        CleanUp();
        base.OnExit(e);
    }
}

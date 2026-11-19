// CalmMouse for Windows: a tray app with no main window.
//
// Start: one instance per user (a second launch opens the first one's Settings and exits), the
// settings file, the language, launch at login kept pointing at this exe, the scroll engine, the
// tray icon, and the watch for other mouse software. The first launch ever opens Settings once.
// Quit (tray menu, `--quit`, or signing out): settings are written, the icon goes, the engine
// stops (hook removed first, so scrolling is never left broken). A crash logs what happened,
// writes the settings and takes the icon away too; Windows removes the hook with the process.

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
    private bool _launchAtLoginFailed;
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
            Shutdown(QuitRunningCopy(instanceName));
            return;
        }
        _instance = SingleInstance.TryAcquire(instanceName);
        if (_instance == null)
        {
            // Already running: let it come to the front with its Settings window.
            global::Windows.Win32.PInvoke.AllowSetForegroundWindow(unchecked((uint)-1));   // ASFW_ANY
            if (SingleInstance.SignalShowSettings(instanceName))
            {
                Shutdown();
                return;
            }
            // No answer: it was on its way out. Take over once it's gone.
            _instance = SingleInstance.TryAcquire(instanceName, TimeSpan.FromSeconds(2));
            if (_instance == null)
            {
                Shutdown(1);
                return;
            }
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _log = new AppLog(AppLog.DefaultPath);
        _log.Write($"CalmMouse {typeof(App).Assembly.GetName().Version} starting, pid {Environment.ProcessId}, Windows {Environment.OSVersion.Version}");
        if (!_instance.EventsWereNew) { _log.Write("single instance: its events were already there (a launch that was exiting?)"); }
        DispatcherUnhandledException += (_, args) => _log?.Write($"unhandled exception on the UI thread: {args.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, args) => OnCrash(args.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            // Not fatal (the process goes on): note it and carry on.
            _log?.Write($"unobserved task exception: {args.Exception}");
            args.SetObserved();
        };

        _store = new SettingsStore(SettingsStore.DefaultPath, log: _log.Write);
        AppSettings settings = _store.Current;
        if (_store.IsFirstLaunch || _store.CorruptBackupPath != null) { _store.SaveNow(); }
        if (_store.IsReadOnly) { _log.Write("settings: changes this time won't be saved"); }
        Localizer.Shared.Apply(settings.Language);
        Localizer.Shared.LanguageChanged += (_, _) => _tray?.Refresh();

        if (Environment.ProcessPath is { } exe)
        {
            _launchAtLogin = new LaunchAtLogin(exe);
            SyncLaunchAtLogin(_launchAtLogin, settings.LaunchAtLogin, exe);
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

    /// <summary>`--quit`: asks the running copy to quit and waits until it's gone. 0 when it is (or none ran), 1 if it didn't answer, 2 if it's still there.</summary>
    private static int QuitRunningCopy(string instanceName)
    {
        if (!SingleInstance.IsRunning(instanceName)) { return 0; }
        if (!SingleInstance.SignalQuit(instanceName)) { return 1; }
        return SingleInstance.WaitForExit(instanceName, TimeSpan.FromSeconds(10)) ? 0 : 2;
    }

    /// <summary>At start: make the setting and Windows agree, without overriding the user's own choices in Windows.</summary>
    private void SyncLaunchAtLogin(LaunchAtLogin launch, bool enabled, string exe)
    {
        try
        {
            if (!enabled && launch.IsCurrent)
            {
                // The Run entry starts this exe (an installer, or an older settings file): show it as on.
                _store?.SetLaunchAtLogin(true);
                _log?.Write("start at sign-in: the Run entry starts this exe, so the setting is on");
            }
            else if (enabled && LaunchAtLogin.MayRepair(exe, IsDebugBuild) && launch.Repair(enabled))
            {
                _log?.Write($"start at sign-in now starts {exe}");
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            _log?.Write($"couldn't check start at sign-in: {ex.Message}");
        }
    }

    /// <summary>A debug build never moves the Run entry to itself (so running from the IDE leaves an installed copy's entry alone).</summary>
#if DEBUG
    private const bool IsDebugBuild = true;
#else
    private const bool IsDebugBuild = false;
#endif

    /// <summary>The line under "Start when I sign in" in Settings, or null.</summary>
    private string? LaunchAtLoginNote()
    {
        if (_launchAtLoginFailed) { return L("Couldn't set CalmMouse to start when you sign in."); }
        if (_store is not { } store || _launchAtLogin is not { } launch || !store.Current.LaunchAtLogin) { return null; }
        try
        {
            return launch.State == LaunchAtLoginState.TurnedOffInWindows ? L("Turned off in Task Manager") : null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            return null;
        }
    }

    private void ApplyLaunchAtLogin(bool enabled)
    {
        if (_launchAtLogin is not { } launch || _store is not { } store) { return; }
        try
        {
            if (launch.Apply(enabled)) { _log?.Write(enabled ? $"start at sign-in: on ({launch.Command})" : "start at sign-in: off"); }
            _launchAtLoginFailed = false;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            _log?.Write($"couldn't change start at sign-in: {ex.Message}");
            if (enabled)
            {
                // Don't claim it's on: switch it back off and say so under the switch.
                _launchAtLoginFailed = true;
                store.SetLaunchAtLogin(false);
            }
        }
    }

    // MARK: Crashes

    /// <summary>
    /// An exception nobody caught is about to end the process: log it, save the settings, and take
    /// the tray icon away (Windows would leave a dead one until the pointer passes over it). The
    /// hook goes with the process.
    /// </summary>
    private void OnCrash(object exception)
    {
        try { _log?.Write($"CRASH: {exception}"); } catch (Exception) { /* nothing more to do */ }
        try { _store?.Flush(); } catch (Exception) { /* as above */ }
        try
        {
            if (_tray is { } tray)
            {
                if (Dispatcher.CheckAccess()) { tray.Dispose(); }
                else { Dispatcher.Invoke(tray.Dispose, DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(1)); }
            }
        }
        catch (Exception) { /* the UI thread is gone or stuck */ }
        try { _log?.Dispose(); } catch (Exception) { }
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
        ApplyLaunchAtLogin(settings.LaunchAtLogin);
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
        TrayView view = CurrentTrayView(_store, _engine);
        if (view != _tray.View) { _tray.Show(view); }
    }

    /// <summary>What the tray shows now; Settings shows its problem line too.</summary>
    private TrayView CurrentTrayView(SettingsStore store, InputThread engine)
    {
        bool paused = store.Current.Paused;
        return _problem switch
        {
            EngineProblem.CouldNotStart when !paused => new(TrayState.Problem, L("CalmMouse couldn't start. Your mouse scrolls as usual."), CanRetry: true),
            EngineProblem.Faulted when !paused => new(TrayState.Problem, L("CalmMouse stopped because of a problem. Your mouse scrolls as usual."), CanRetry: true),
            _ when paused => new(TrayState.Paused, null, false),
            _ when engine.Status.BreakerOpen =>
                new(TrayState.Problem, L("Something on this PC is blocking CalmMouse, so your mouse scrolls as usual for now. It will try again soon."), CanRetry: false),
            _ => new(TrayState.Normal, null, false),
        };
    }

    // MARK: Windows

    private void ShowSettings()
    {
        if (_quitting) { return; }
        if (_settingsWindow == null)
        {
            if (_store == null || _engine == null) { return; }
            var model = new SettingsUI.SettingsViewModel(_store, new SettingsHost(this), new SettingsUI.WindowsAppCatalog(),
                                                         action => Dispatcher.BeginInvoke(action));
            _settingsWindow = new SettingsWindow(model);
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
        _store?.Flush();      // first, in case stopping the engine takes its full 5 seconds
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

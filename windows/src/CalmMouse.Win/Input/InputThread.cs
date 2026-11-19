// The input thread: spec 1.1 design A on a real PC. One dedicated background thread owns all of it:
//
//   - the WH_MOUSE_LL hook. Windows calls it on this thread, from inside PeekMessage in the loop
//     below. For a physical wheel event it resolves the window the event scrolls (WheelTargets.cs;
//     skipped when nothing could be swallowed anyway), reads the keys held and asks the
//     ScrollRouter: Swallow returns 1, Pass calls CallNextHookEx. Injected wheel events always
//     pass. Buttons and moves are only looked at, never swallowed. The callback never calls SendInput.
//   - a message-only window registered for Raw Input mice (RIDEV_INPUTSINK | RIDEV_DEVNOTIFY):
//     WM_INPUT feeds RawWheel / RawButton, the watchdog and the resend breaker;
//     WM_INPUT_DEVICE_CHANGE adds and removes mice.
//   - the ScrollRouter. Nothing else touches it; other threads post work through Post().
//   - SendInput. The loop drains the router's sends after every batch of messages and every tick,
//     so injection happens outside the hook callback, in the order the router asked. A batch of
//     messages is bounded (count and time), so a very fast mouse can't starve ticks and resends.
//   - one high-resolution waitable timer for glide frames at the display's refresh rate, armed
//     only while the router NeedsTicks (and for its NextDeadline), so an idle engine sleeps.
//   - the watchdog (HookWatchdog.cs), which reinstalls the hook if Windows removed it, judged from
//     Raw Input alone: it injects nothing.
//   - the resend breaker (ResendBreaker.cs): if our resends stop getting through, stop swallowing.
//
// Safety: an exception anywhere on this thread removes the hook first, then sends the rest of any
// glide and resends whatever the router still holds, and only then reports (Faulted). Windows also
// removes the hook by itself if the thread or process dies. Raw Input registration is per process,
// so only one InputThread may run at a time.

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using CalmMouse.Core;
using CalmMouse.Core.Routing;

namespace CalmMouse.Win.Input;

public sealed class InputThreadOptions
{
    /// <summary>Each mouse's settings and the app rules. Default: everything off.</summary>
    public IWheelSettingsProvider? Settings { get; init; }

    /// <summary>Record every wheel event and decision in <see cref="InputThread.WheelLog"/> (the --engine-test switch).</summary>
    public bool RecordWheelLog { get; init; }

    /// <summary>How long physical Raw Input may go unanswered by the hook before it is reinstalled.</summary>
    public double HookSilenceLimit { get; init; } = HookWatchdog.DefaultSilenceLimit;

    /// <summary>The least time between two reinstalls (doubling while the hook stays silent).</summary>
    public double HookReinstallGap { get; init; } = HookWatchdog.DefaultMinimumGap;

    // Tests only.

    /// <summary>Replaces SendInput for some of the router's resends: (delta, horizontal) → accepted, or null to send it for real.</summary>
    internal Func<int, bool, bool?>? ResendOverride { get; init; }

    /// <summary>Replaces the target lookup in the hook.</summary>
    internal Func<WheelTarget>? TargetOverride { get; init; }

    /// <summary>Replaces the clock the hook reads.</summary>
    internal Func<double>? ClockOverride { get; init; }

    /// <summary>Replaces GetAsyncKeyState for the modifiers and the middle button.</summary>
    internal Func<(WinModifiers Held, bool MiddleDown)>? KeysOverride { get; init; }

    /// <summary>An injected event with this dwExtraInfo is swallowed by the hook (0: off), so tests can see whether the hook is there.</summary>
    internal uint SwallowMarker { get; init; }
}

/// <summary>The input thread's own counts, next to the router's.</summary>
public readonly record struct EngineCounters(
    long HookCalls,
    long PhysicalWheels,
    long InjectedWheels,
    long RawWheels,
    long Sends,
    long SendFailures,
    long ResendEchoes,
    long BreakerTrips,
    long HookReinstalls);

/// <summary>A snapshot of the engine, refreshed by the input thread about twice a second.</summary>
/// <param name="BreakerOpen">Resends stopped getting through: everything passes untouched until it tries again.</param>
public sealed record EngineStatus(
    bool Running,
    bool HookInstalled,
    bool Paused,
    bool BreakerOpen,
    double GlideHz,
    WheelRouting Routing,
    RouterStats Router,
    EngineCounters Counters,
    string? LastRouterFault);

public sealed unsafe class InputThread : IDisposable
{
    private const uint WM_APP_INVOKE = Native.WM_APP + 1;
    private const double HousekeepingInterval = 0.5;
    private const double RoutingReadInterval = 2.0;
    private const double DisplayReadInterval = 5.0;
    /// <summary>A batch of messages ends after this many, or <see cref="PumpBudget"/>, so ticks and resends get their turn.</summary>
    private const int PumpMaxMessages = 64;
    private const double PumpBudget = 0.002;
    private const string WindowClassName = "CalmMouse.InputThread";

    private static readonly double s_secondsPerTick = 1.0 / Stopwatch.Frequency;
    private static readonly Lock s_classLock = new();
    private static bool s_classRegistered;
    private static readonly Lock s_activeLock = new();
    private static InputThread? s_active;

    [ThreadStatic] private static InputThread? t_current;

    private readonly InputThreadOptions _options;
    private readonly IWheelSettingsProvider _settings;
    private readonly ScrollRouter _router = new();
    private readonly WheelTargetResolver _targets = new();
    private readonly ConcurrentQueue<Action> _invokes = new();
    private readonly ConcurrentQueue<string> _messages = new();
    private readonly Dictionary<long, MouseDeviceInfo> _devices = [];
    private readonly Dictionary<long, int> _describing = [];
    private readonly ManualResetEventSlim _ready = new();
    private readonly Lock _startLock = new();

    private HookWatchdog _watchdog;
    private ResendBreaker _breaker = new();
    private Thread? _thread;
    private nint _hwnd;
    private nint _hook;
    private nint _timer;
    private volatile bool _stopRequested;
    private volatile bool _paused;
    private Exception? _startError;
    private Exception? _fault;
    private int _run;
    private int _describeGeneration;

    // Timing (input thread only).
    private double _frameInterval = FrameTiming.IntervalSeconds(FrameTiming.DefaultHz);
    private bool _ticking;
    private double _frameDue;
    private double _armedFor = double.NaN;
    private double _nextHousekeeping;
    private double _nextRoutingRead;
    private double _nextDisplayRead;
    private double _lastPointerCheck = double.NegativeInfinity;
    private WheelTarget _lastPointerTarget;

    // Counters (written on the input thread only).
    private long _hookCalls, _physicalWheels, _injectedWheels, _rawWheels, _sends, _sendFailures, _hookReinstalls;

    private EngineStatus _status;
    private IReadOnlyList<MouseDeviceInfo> _deviceSnapshot = [];

    public InputThread(InputThreadOptions? options = null)
    {
        _options = options ?? new InputThreadOptions();
        _settings = _options.Settings ?? new InMemoryWheelSettingsProvider();
        _watchdog = NewWatchdog();
        WheelLog = _options.RecordWheelLog ? new WheelLog() : null;
        _status = new EngineStatus(false, false, false, false, FrameTiming.DefaultHz, WheelRouting.Hover, default, default, null);
    }

    private HookWatchdog NewWatchdog() => new(_options.HookSilenceLimit, _options.HookReinstallGap);

    // MARK: Public surface (any thread)

    /// <summary>The wheel log, if <see cref="InputThreadOptions.RecordWheelLog"/> is on.</summary>
    public WheelLog? WheelLog { get; }

    public bool IsRunning => _thread is { IsAlive: true };

    public bool IsPaused => _paused;

    public EngineStatus Status => Volatile.Read(ref _status);

    /// <summary>The mice found so far, touchpads included (with <see cref="DeviceKind.Touchpad"/>).</summary>
    public IReadOnlyList<MouseDeviceInfo> Devices => Volatile.Read(ref _deviceSnapshot);

    /// <summary>A mouse was added or removed. Raised on a thread-pool thread.</summary>
    public event EventHandler<IReadOnlyList<MouseDeviceInfo>>? DevicesChanged;

    /// <summary>
    /// The input thread hit an exception and stopped. By the time this is raised the hook is
    /// already removed and anything held has been sent: the mouse scrolls as if CalmMouse weren't
    /// running. Raised on a thread-pool thread. <see cref="Start"/> may be called again afterwards.
    /// </summary>
    public event EventHandler<Exception>? Faulted;

    /// <summary>Takes the engine's log lines (hook installed, devices, reinstalls, breaker, faults), oldest first.</summary>
    public List<string> TakeMessages()
    {
        var lines = new List<string>();
        while (_messages.TryDequeue(out string? line)) { lines.Add(line); }
        return lines;
    }

    /// <summary>
    /// Starts the thread and returns once the hook and Raw Input are in place. Throws if they can't
    /// be. Calling it while running does nothing; after a fault (or Stop) it starts afresh.
    /// </summary>
    public void Start()
    {
        lock (_startLock)
        {
            if (_thread is { } previous)
            {
                if (previous.IsAlive) { return; }
                previous.Join();   // ended by a fault: clear it away
                _thread = null;
            }
            lock (s_activeLock)
            {
                if (s_active != null) { throw new InvalidOperationException("Another InputThread is running: Raw Input registration is per process, so only one may run at a time."); }
                s_active = this;
            }

            // A fresh run: nothing from the last one carries over (its thread is gone, so this is safe).
            _run++;
            _devices.Clear();
            _describing.Clear();
            while (_invokes.TryDequeue(out _)) { }
            _watchdog = NewWatchdog();
            _breaker = new ResendBreaker();
            _ticking = false;
            _armedFor = double.NaN;
            _stopRequested = false;
            _startError = null;
            _fault = null;
            _ready.Reset();

            var thread = new Thread(ThreadMain) { IsBackground = true, Name = "CalmMouse input", Priority = ThreadPriority.Highest };
            _thread = thread;
            thread.Start();
            if (!_ready.Wait(TimeSpan.FromSeconds(10)))
            {
                StopThread();
                throw new TimeoutException("The input thread didn't start within 10 seconds.");
            }
            if (_startError is { } error)
            {
                thread.Join(TimeSpan.FromSeconds(5));
                _thread = null;
                throw new InvalidOperationException($"The input thread couldn't start: {error.Message}", error);
            }
        }
    }

    /// <summary>
    /// Stops the thread: the hook is removed first (from then on every wheel event passes), then
    /// the rest of any glide and anything still held is sent, then Raw Input and the window go.
    /// Safe to call more than once, and from any thread.
    /// </summary>
    public void Stop()
    {
        if (Thread.CurrentThread == _thread)
        {
            _stopRequested = true;   // the loop sees the flag when this returns
            return;
        }
        lock (_startLock) { StopThread(); }
    }

    private void StopThread()
    {
        Thread? thread = _thread;
        if (thread == null) { return; }
        _stopRequested = true;
        nint hwnd = _hwnd;
        if (hwnd != 0) { Native.PostMessageW(hwnd, WM_APP_INVOKE, 0, 0); }
        if (!thread.Join(TimeSpan.FromSeconds(5))) { Message("the input thread didn't stop within 5 seconds"); }
        _thread = null;
    }

    /// <summary>Same as <see cref="Stop"/>.</summary>
    public void Dispose() => Stop();

    /// <summary>Paused: every wheel event passes untouched (the hook stays, so resuming is instant).</summary>
    public void SetPaused(bool paused)
    {
        _paused = paused;
        Post(() =>
        {
            ApplyPause();
            Message(paused ? "paused" : "resumed");
        });
    }

    /// <summary>Reads every mouse's settings and the app rules from the provider again.</summary>
    public void ApplySettings() => Post(ApplySettingsNow);

    // MARK: Test hooks (internal)

    /// <summary>Runs <paramref name="func"/> on the input thread and waits for its result.</summary>
    internal T Invoke<T>(Func<T> func, int timeoutMs = 2000)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try { done.SetResult(func()); }
            catch (Exception ex) { done.SetException(ex); }
        });
        if (!done.Task.Wait(timeoutMs)) { throw new TimeoutException("The input thread didn't answer."); }
        return done.Task.Result;
    }

    /// <summary>Works with the router on the input thread (its sends are drained right after).</summary>
    internal T WithRouter<T>(Func<ScrollRouter, T> func) => Invoke(() => func(_router));

    /// <summary>The router itself, for tests that drive an engine that isn't running.</summary>
    internal ScrollRouter RouterForTest => _router;

    /// <summary>The engine's clock (seconds), as the router sees it.</summary>
    internal static double ClockNow() => Now();

    /// <summary>Calls the hook's logic directly (no Windows hook), as for one hook message. True = swallow.</summary>
    internal bool HookForTest(uint msg, Native.MSLLHOOKSTRUCT s) => OnHook(msg, &s);

    /// <summary>Injects a wheel event with <paramref name="extraInfo"/>, from the input thread. False if SendInput refused.</summary>
    internal bool InjectWheel(int delta, nuint extraInfo, bool horizontal = false) =>
        Invoke(() => Inject(delta, horizontal, extraInfo));

    /// <summary>Removes the hook the way Windows does: quietly, leaving the handle in place.</summary>
    internal void LoseHookForTest() => Invoke(() => Native.UnhookWindowsHookEx(_hook));

    /// <summary>Tells the watchdog Raw Input saw physical mouse input now, as moving a real mouse would.</summary>
    internal void PhysicalInputForTest() => Invoke(() =>
    {
        _watchdog.PhysicalInput(Now());
        return true;
    });

    /// <summary>Throws on the input thread, as a bug would.</summary>
    internal void FaultForTest() => Post(() => throw new InvalidOperationException("test fault"));

    internal bool HookHandlePresent => Volatile.Read(ref _hook) != 0;

    /// <summary>Whether the glide timer is armed right now.</summary>
    internal bool TickingForTest => Invoke(() => _ticking);

    internal bool BreakerOpenForTest => Invoke(() => _breaker.IsOpen);

    /// <summary>The counters right now (the published status lags by up to half a second).</summary>
    internal EngineCounters CountersNow() => Invoke(Counters);

    // MARK: The thread

    private static double Now() => Stopwatch.GetTimestamp() * s_secondsPerTick;

    private double HookClock() => _options.ClockOverride is { } clock ? clock() : Now();

    private void ThreadMain()
    {
        t_current = this;
        bool started = false;
        try
        {
            try { Native.SetThreadDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); }
            catch (EntryPointNotFoundException) { /* before Windows 10 1607: process awareness applies */ }
            Setup();
            started = true;
            PublishStatus(running: true);
            _ready.Set();
            Loop();
        }
        catch (Exception ex)
        {
            if (started) { _fault ??= ex; } else { _startError = ex; }
        }
        finally
        {
            // The hook goes first: from here on the mouse scrolls normally whatever else happens.
            RemoveHook();
            try
            {
                // Then nothing already asked for is lost: the rest of each glide, whole, and every
                // swallowed event still waiting for its device (pausing resends those).
                _router.Flush();
                _router.SetPaused(true);
                Drain();
            }
            catch (Exception ex)
            {
                Message($"couldn't send what was held: {ex.Message}");
            }
            Teardown();
            t_current = null;
            PublishStatus(running: false);
            lock (s_activeLock)
            {
                if (s_active == this) { s_active = null; }
            }
            _ready.Set();
            if (_fault is { } fault)
            {
                Message($"FAULT, hook removed and engine stopped: {fault}");
                ThreadPool.QueueUserWorkItem(_ => Faulted?.Invoke(this, fault));
            }
            else if (started)
            {
                Message("stopped: hook removed");
            }
        }
    }

    private void Setup()
    {
        nint instance = Native.GetModuleHandleW(null);
        EnsureWindowClass(instance);
        fixed (char* className = WindowClassName)
        {
            _hwnd = Native.CreateWindowExW(0, className, null, 0, 0, 0, 0, 0, Native.HWND_MESSAGE, 0, instance, 0);
        }
        if (_hwnd == 0) { throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateWindowEx (message-only window) failed"); }

        var rid = new Native.RAWINPUTDEVICE
        {
            usUsagePage = 0x01,
            usUsage = 0x02,
            dwFlags = Native.RIDEV_INPUTSINK | Native.RIDEV_DEVNOTIFY,
            hwndTarget = _hwnd,
        };
        if (Native.RegisterRawInputDevices(&rid, 1, (uint)sizeof(Native.RAWINPUTDEVICE)) == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "RegisterRawInputDevices failed");
        }

        _timer = Native.CreateWaitableTimerExW(0, null, Native.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, Native.TIMER_ALL_ACCESS);
        if (_timer == 0) { _timer = Native.CreateWaitableTimerExW(0, null, 0, Native.TIMER_ALL_ACCESS); }
        if (_timer == 0) { throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateWaitableTimerEx failed"); }

        double now = Now();
        ReadDisplay(now);
        ReadRouting(now);

        foreach (MouseDeviceInfo device in MouseDevices.Enumerate())
        {
            _devices[device.DeviceId] = device;
            Message($"device: {device}");
        }
        ApplySettingsNow();
        PublishDevices();
        ApplyPause();
        _settings.Changed += OnSettingsChanged;

        WarmUp();
        InstallHook();
        _watchdog.Started(Now());
        Message($"started: hook installed, Raw Input registered, glide {FrameTiming.GlideHz(1 / _frameInterval):0.#} Hz, wheel routing {_targets.Routing}, " +
                $"integrity 0x{ProcessDescriber.OwnIntegrityLevel:X}");
    }

    /// <summary>
    /// Compiles and runs the hook's code paths once before the hook exists, so its first real call
    /// isn't slowed by the JIT (Windows drops a hook that is slow even once). Uses a throwaway router.
    /// </summary>
    private void WarmUp()
    {
        try
        {
            foreach (string name in new[] { nameof(OnHook), nameof(OnRawInput), nameof(Drain), nameof(TickIfDue), nameof(ScheduleTicks), nameof(Housekeeping) })
            {
                if (typeof(InputThread).GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) is { } method)
                {
                    RuntimeHelpers.PrepareMethod(method.MethodHandle);
                }
            }
            var router = new ScrollRouter();
            router.SetDevice(new RoutedDevice(1, DeviceKind.WheelMouse, "warm-up") { Wheel = new WheelSettings(reverse: true, smooth: true) });
            Native.POINT cursor;
            WheelTarget target = Native.GetCursorPos(&cursor) != 0 ? _targets.Resolve(cursor, Now()) : WheelTargetResolver.Unknown;
            target = target with { Elevated = false };
            double t = Now();
            router.HookWheel(t, 120, false, ReadModifiers(), MiddleDown(), target);
            router.RawWheel(t, 1, 120, false);
            router.RawButton(t, 1, RawMouseButton.Middle, false);
            router.Tick(t + 0.01, WinModifiers.None);
            router.PointerTargetChanged(target);
            router.Flush();
            while (router.TryTakeSend(out _)) { }
            _ = router.NeedsTicks;
            _ = router.NextDeadline;
        }
        catch (Exception ex)
        {
            Message($"warm-up failed (harmless): {ex.Message}");
        }
    }

    private void Teardown()
    {
        _settings.Changed -= OnSettingsChanged;
        if (_hwnd != 0)
        {
            var rid = new Native.RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x02, dwFlags = Native.RIDEV_REMOVE, hwndTarget = 0 };
            Native.RegisterRawInputDevices(&rid, 1, (uint)sizeof(Native.RAWINPUTDEVICE));
            Native.DestroyWindow(_hwnd);
            _hwnd = 0;
        }
        if (_timer != 0)
        {
            Native.CancelWaitableTimer(_timer);
            Native.CloseHandle(_timer);
            _timer = 0;
        }
        _ticking = false;
        _armedFor = double.NaN;
    }

    private static void EnsureWindowClass(nint instance)
    {
        lock (s_classLock)
        {
            if (s_classRegistered) { return; }
            fixed (char* name = WindowClassName)
            {
                var wc = new Native.WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(Native.WNDCLASSEXW),
                    lpfnWndProc = &WndProc,
                    hInstance = instance,
                    lpszClassName = name,
                };
                if (Native.RegisterClassExW(&wc) == 0)
                {
                    int error = Marshal.GetLastPInvokeError();
                    const int ERROR_CLASS_ALREADY_EXISTS = 1410;
                    if (error != ERROR_CLASS_ALREADY_EXISTS) { throw new Win32Exception(error, "RegisterClassEx failed"); }
                }
            }
            s_classRegistered = true;
        }
    }

    private void InstallHook()
    {
        nint hook = Native.SetWindowsHookExW(Native.WH_MOUSE_LL, &HookProc, Native.GetModuleHandleW(null), 0);
        if (hook == 0) { throw new Win32Exception(Marshal.GetLastPInvokeError(), "SetWindowsHookEx(WH_MOUSE_LL) failed"); }
        Volatile.Write(ref _hook, hook);
    }

    private void RemoveHook()
    {
        nint hook = Interlocked.Exchange(ref _hook, 0);
        // Fails harmlessly if Windows already removed it.
        if (hook != 0) { Native.UnhookWindowsHookEx(hook); }
    }

    private void Loop()
    {
        nint timer = _timer;
        while (true)
        {
            double now = Now();
            uint timeoutMs = WaitMilliseconds(now);
            uint woke = Native.MsgWaitForMultipleObjectsEx(1, &timer, timeoutMs, Native.QS_ALLINPUT, Native.MWMO_INPUTAVAILABLE);
            if (woke == Native.WAIT_FAILED) { throw new Win32Exception(Marshal.GetLastPInvokeError(), "MsgWaitForMultipleObjectsEx failed"); }
            if (woke == Native.WAIT_OBJECT_0) { _armedFor = double.NaN; }

            bool keepGoing = Pump();
            if (_fault is { } fault) { ExceptionDispatchInfo.Capture(fault).Throw(); }
            if (!keepGoing) { return; }

            now = Now();
            if (_ticking) { TickIfDue(now); }
            Drain();
            Housekeeping(now);
            ScheduleTicks(Now());
        }
    }

    /// <summary>
    /// Handles what is waiting: hook calls (inside PeekMessage), Raw Input, posted work. At most
    /// <see cref="PumpMaxMessages"/> messages or <see cref="PumpBudget"/> per batch: an 8 kHz mouse
    /// floods the queue with WM_INPUT, and glide frames and resends must still go out on time (the
    /// wait returns at once while messages remain). False to stop.
    /// </summary>
    private bool Pump()
    {
        Native.MSG msg;
        double end = Now() + PumpBudget;
        for (int count = 0; count < PumpMaxMessages; count++)
        {
            if (Native.PeekMessageW(&msg, 0, 0, 0, Native.PM_REMOVE) == 0) { break; }
            if (msg.message == Native.WM_QUIT) { return false; }
            Native.DispatchMessageW(&msg);
            if (_stopRequested || _fault != null) { return !_stopRequested; }
            if (Now() >= end) { break; }
        }
        return !_stopRequested;
    }

    private uint WaitMilliseconds(double now)
    {
        double next = Math.Min(_nextHousekeeping, Math.Min(_watchdog.NextCheckAt, _breaker.NextCheckAt));
        double ms = Math.Ceiling((next - now) * 1000);
        return (uint)Math.Clamp(double.IsFinite(ms) ? ms : 1000, 0, 1000);
    }

    // MARK: Glide timing

    private void ScheduleTicks(double now)
    {
        if (!_router.NeedsTicks)
        {
            if (_ticking)
            {
                _ticking = false;
                _armedFor = double.NaN;
                Native.CancelWaitableTimer(_timer);
            }
            return;
        }
        if (!_ticking)
        {
            _ticking = true;
            _frameDue = now + _frameInterval;
        }
        double due = _frameDue;
        if (_router.NextDeadline is double deadline && deadline < due) { due = deadline; }
        if (due == _armedFor) { return; }
        long relative = -(long)Math.Max(1, (due - now) * 1e7);   // 100 ns units, negative = relative
        Native.SetWaitableTimer(_timer, &relative, 0, 0, 0, 0);
        _armedFor = due;
    }

    private void TickIfDue(double now)
    {
        const double early = 0.0003;   // the timer may fire a hair early
        bool frame = now >= _frameDue - early;
        bool deadline = _router.NextDeadline is double d && now >= d - early;
        if (!frame && !deadline) { return; }
        _router.Tick(now, ReadModifiers());
        if (frame)
        {
            _frameDue += _frameInterval;
            if (_frameDue <= now) { _frameDue = now + _frameInterval; }   // fell behind: don't catch up in a burst
        }
        _armedFor = double.NaN;
    }

    // MARK: Hook

    [UnmanagedCallersOnly]
    private static nint HookProc(int nCode, nint wParam, nint lParam)
    {
        InputThread? self = t_current;
        if (nCode >= 0 && self != null)
        {
            try
            {
                if (self.OnHook((uint)wParam, (Native.MSLLHOOKSTRUCT*)lParam)) { return 1; }
            }
            catch (Exception ex)
            {
                self.OnFault(ex);
            }
        }
        return Native.CallNextHookEx(0, nCode, wParam, lParam);
    }

    /// <summary>True to swallow. Keep it short: Windows drops a hook that is slow even once.</summary>
    private bool OnHook(uint msg, Native.MSLLHOOKSTRUCT* s)
    {
        double t = HookClock();
        _hookCalls++;
        _watchdog.HookActivity(t);
        if (_fault != null) { return false; }

        bool injected = (s->flags & Native.LLMHF_INJECTED) != 0;
        if (injected && _options.SwallowMarker != 0 && (uint)s->dwExtraInfo == _options.SwallowMarker) { return true; }

        if (msg == Native.WM_MOUSEWHEEL || msg == Native.WM_MOUSEHWHEEL)
        {
            bool horizontal = msg == Native.WM_MOUSEHWHEEL;
            int delta = (short)(s->mouseData >> 16);
            if (injected)
            {
                _injectedWheels++;
                WheelLog?.Write(new WheelLogEntry(WheelLogKind.InjectedWheel, t, delta, horizontal, 0, s->dwExtraInfo, default, false));
                return false;
            }

            _physicalWheels++;
            // Nothing can be swallowed anywhere (paused, nothing on, the breaker open): the window
            // doesn't matter, so don't spend time finding it.
            WheelTarget target = _router.MightSwallow ? ResolveTarget(s) : WheelTargetResolver.Unknown;
            _lastPointerTarget = target;
            _lastPointerCheck = t;
            (WinModifiers held, bool middleDown) = Keys();
            HookAnswer answer = _router.HookWheel(t, delta, horizontal, held, middleDown, target);
            bool swallow = answer == HookAnswer.Swallow;
            WheelLog?.Write(new WheelLogEntry(WheelLogKind.HookWheel, t, delta, horizontal, 0, s->dwExtraInfo, target, swallow));
            return swallow;
        }

        // While a glide plays, follow the window under the cursor (once per frame at most), so the
        // glide stops if the cursor moves onto a window where it mustn't play.
        if (msg == Native.WM_MOUSEMOVE && t - _lastPointerCheck >= _frameInterval && _router.NeedsTicks)
        {
            _lastPointerCheck = t;
            WheelTarget target = ResolveTarget(s);
            if (!ReferenceEquals(target.ExeName, _lastPointerTarget.ExeName) || target.Elevated != _lastPointerTarget.Elevated)
            {
                _lastPointerTarget = target;
                _router.PointerTargetChanged(target);
            }
        }
        return false;
    }

    private WheelTarget ResolveTarget(Native.MSLLHOOKSTRUCT* s) =>
        _options.TargetOverride is { } target ? target() : _targets.Resolve(s->pt, HookClock());

    private (WinModifiers Held, bool MiddleDown) Keys() =>
        _options.KeysOverride is { } keys ? keys() : (ReadModifiers(), MiddleDown());

    private static WinModifiers ReadModifiers()
    {
        WinModifiers m = WinModifiers.None;
        if (Native.GetAsyncKeyState(Native.VK_CONTROL) < 0) { m |= WinModifiers.Ctrl; }
        if (Native.GetAsyncKeyState(Native.VK_MENU) < 0) { m |= WinModifiers.Alt; }
        if (Native.GetAsyncKeyState(Native.VK_LWIN) < 0 || Native.GetAsyncKeyState(Native.VK_RWIN) < 0) { m |= WinModifiers.Win; }
        if (Native.GetAsyncKeyState(Native.VK_SHIFT) < 0) { m |= WinModifiers.Shift; }
        return m;
    }

    private static bool MiddleDown() => Native.GetAsyncKeyState(Native.VK_MBUTTON) < 0;

    // MARK: Window messages

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        InputThread? self = t_current;
        if (self != null && hwnd == self._hwnd)
        {
            try
            {
                switch (msg)
                {
                    case Native.WM_INPUT:
                        self.OnRawInput(lParam);
                        break;   // DefWindowProc lets Windows clean up after WM_INPUT
                    case Native.WM_INPUT_DEVICE_CHANGE:
                        self.OnDeviceChange(wParam, lParam);
                        return 0;
                    case WM_APP_INVOKE:
                        self.RunInvokes();
                        return 0;
                }
            }
            catch (Exception ex)
            {
                self.OnFault(ex);
            }
        }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void OnRawInput(nint handle)
    {
        uint headerSize = (uint)sizeof(Native.RAWINPUTHEADER);
        byte* buffer = stackalloc byte[128];
        uint size = 128;
        uint got = Native.GetRawInputData(handle, Native.RID_INPUT, buffer, &size, headerSize);
        if (got == uint.MaxValue || got < headerSize + (uint)sizeof(Native.RAWMOUSE)) { return; }
        var header = (Native.RAWINPUTHEADER*)buffer;
        if (header->dwType != Native.RIM_TYPEMOUSE) { return; }
        var mouse = (Native.RAWMOUSE*)(buffer + headerSize);
        double t = Now();
        long device = header->hDevice;
        // Any physical mouse event, moves included, also goes through the hook: the watchdog's evidence.
        if (device != 0) { _watchdog.PhysicalInput(t); }
        ushort flags = mouse->usButtonFlags;
        if (flags == 0) { return; }   // movement only

        if ((flags & (Native.RI_MOUSE_WHEEL | Native.RI_MOUSE_HWHEEL)) != 0)
        {
            bool horizontal = (flags & Native.RI_MOUSE_WHEEL) == 0;
            int delta = (short)mouse->usButtonData;
            _rawWheels++;
            // One of our resends back again: it got through.
            if (mouse->ulExtraInformation == ScrollRouter.ResendMarker) { _breaker.Echo(t); }
            _router.RawWheel(t, device, delta, horizontal, mouse->ulExtraInformation);
            WheelLog?.Write(new WheelLogEntry(WheelLogKind.RawWheel, t, delta, horizontal, device, mouse->ulExtraInformation, default, false));
        }
        Button(Native.RI_MOUSE_MIDDLE_BUTTON_DOWN, Native.RI_MOUSE_MIDDLE_BUTTON_UP, RawMouseButton.Middle);
        Button(Native.RI_MOUSE_LEFT_BUTTON_DOWN, Native.RI_MOUSE_LEFT_BUTTON_UP, RawMouseButton.Left);
        Button(Native.RI_MOUSE_RIGHT_BUTTON_DOWN, Native.RI_MOUSE_RIGHT_BUTTON_UP, RawMouseButton.Right);
        Button(Native.RI_MOUSE_BUTTON_4_DOWN, Native.RI_MOUSE_BUTTON_4_UP, RawMouseButton.X1);
        Button(Native.RI_MOUSE_BUTTON_5_DOWN, Native.RI_MOUSE_BUTTON_5_UP, RawMouseButton.X2);

        void Button(ushort down, ushort up, RawMouseButton button)
        {
            if ((flags & down) != 0) { _router.RawButton(t, device, button, true); }
            if ((flags & up) != 0) { _router.RawButton(t, device, button, false); }
        }
    }

    // MARK: Devices

    private void OnDeviceChange(nint change, nint handle)
    {
        long id = handle;
        if (change == Native.GIDC_ARRIVAL)
        {
            // Registration announces every mouse already there: those are known from Setup.
            if (_devices.ContainsKey(id) || _describing.ContainsKey(id)) { return; }
            int generation = ++_describeGeneration;
            int run = _run;
            _describing[id] = generation;
            // Opening the device can take a while: not on this thread, whose hook must stay quick.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                MouseDeviceInfo? info = MouseDevices.Describe(handle);
                Post(() => DeviceDescribed(run, id, generation, info));
            });
        }
        else if (change == Native.GIDC_REMOVAL)
        {
            _describing.Remove(id);
            if (_devices.Remove(id, out MouseDeviceInfo? gone))
            {
                _router.RemoveDevice(id);
                Message($"device removed: {gone}");
                PublishDevices();
            }
        }
    }

    private void DeviceDescribed(int run, long id, int generation, MouseDeviceInfo? info)
    {
        // From an earlier run, or removed (or arrived again) while it was being looked at.
        if (run != _run || !_describing.TryGetValue(id, out int current) || current != generation) { return; }
        _describing.Remove(id);
        if (info == null) { return; }
        _devices[id] = info;
        _router.SetDevice(Routed(info));
        Message($"device added: {info}");
        PublishDevices();
    }

    private RoutedDevice Routed(MouseDeviceInfo info) =>
        new(info.DeviceId, info.Kind, info.StableKey)
        {
            Wheel = info.Kind == DeviceKind.WheelMouse ? _settings.SettingsFor(info.StableKey, info.DisplayName) : new WheelSettings(),
        };

    private void ApplySettingsNow()
    {
        _router.SetDevices(_devices.Values.Select(Routed).ToList());
        _router.SetAppRules(_settings.AppRules);
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => ApplySettings();

    private void PublishDevices()
    {
        IReadOnlyList<MouseDeviceInfo> list = _devices.Values.OrderBy(d => d.Kind).ThenBy(d => d.DisplayName, StringComparer.CurrentCulture).ToArray();
        Volatile.Write(ref _deviceSnapshot, list);
        if (DevicesChanged != null) { ThreadPool.QueueUserWorkItem(_ => DevicesChanged?.Invoke(this, list)); }
    }

    // MARK: Sending

    private void Drain()
    {
        while (_router.TryTakeSend(out WheelSend send))
        {
            double t = Now();
            bool ok = _options.ResendOverride?.Invoke(send.Delta, send.Horizontal)
                      ?? Inject(send.Delta, send.Horizontal, ScrollRouter.ResendMarker);
            if (ok) { _sends++; } else { _sendFailures++; }
            WheelLog?.Write(new WheelLogEntry(WheelLogKind.Sent, t, send.Delta, send.Horizontal, 0, ScrollRouter.ResendMarker, default, !ok));
            if (_breaker.Sent(t, ok)) { BreakerTripped(); }
        }
    }

    private static bool Inject(int delta, bool horizontal, nuint extraInfo)
    {
        var input = new Native.INPUT
        {
            type = Native.INPUT_MOUSE,
            mi = new Native.MOUSEINPUT
            {
                mouseData = unchecked((uint)delta),
                dwFlags = horizontal ? Native.MOUSEEVENTF_HWHEEL : Native.MOUSEEVENTF_WHEEL,
                dwExtraInfo = extraInfo,
            },
        };
        return Native.SendInput(1, &input, sizeof(Native.INPUT)) == 1;
    }

    // MARK: Pause and the breaker

    /// <summary>The router passes everything while the user paused it or the breaker is open.</summary>
    private void ApplyPause() => _router.SetPaused(_paused || _breaker.IsOpen);

    private void BreakerTripped()
    {
        ApplyPause();
        Message($"resends aren't getting through ({_breaker.TripAfter} in a row refused or never came back): " +
                $"passing every wheel event untouched for {_breaker.OpenUntil - Now():0} s, then trying again");
    }

    // MARK: Housekeeping

    private void Housekeeping(double now)
    {
        switch (_breaker.Check(now))
        {
            case 1:
                BreakerTripped();
                break;
            case -1:
                ApplyPause();
                Message("trying resends again");
                break;
        }

        if (now >= _watchdog.NextCheckAt)
        {
            // Input aimed at an elevated window may not reach our hook at all: not a dead hook.
            if (ForegroundIsElevated(now)) { _watchdog.Excuse(); }
            else if (_watchdog.Check(now))
            {
                RemoveHook();
                InstallHook();
                _hookReinstalls++;
                _watchdog.Started(Now());
                Message($"the hook heard nothing while the mouse was in use: reinstalled ({_hookReinstalls} so far)");
            }
        }

        if (now < _nextHousekeeping) { return; }
        _nextHousekeeping = now + HousekeepingInterval;
        if (now >= _nextRoutingRead) { ReadRouting(now); }
        if (now >= _nextDisplayRead) { ReadDisplay(now, inBackground: true); }
        PublishStatus(running: true);
    }

    private bool ForegroundIsElevated(double now) =>
        _targets.ForWindow((nint)Windows.Win32.PInvoke.GetForegroundWindow().Value, now).Elevated;

    private void ReadRouting(double now)
    {
        _nextRoutingRead = now + RoutingReadInterval;
        WheelRouting routing = WheelRoutingSetting.Read();
        if (routing != _targets.Routing && _hook != 0) { Message($"wheel routing is now {routing}"); }
        _targets.Routing = routing;
    }

    /// <summary>
    /// Reads the display's refresh rate: here at start (before the hook exists), later on a
    /// thread-pool thread, because display drivers (hybrid-graphics laptops especially) can take
    /// tens of milliseconds to answer and the hook must not wait behind that.
    /// </summary>
    private void ReadDisplay(double now, bool inBackground = false)
    {
        _nextDisplayRead = now + DisplayReadInterval;
        if (!inBackground)
        {
            ApplyDisplayHz(FrameTiming.ReadDisplayHz());
            return;
        }
        ThreadPool.QueueUserWorkItem(_ =>
        {
            double hz = FrameTiming.ReadDisplayHz();
            Post(() => ApplyDisplayHz(hz));
        });
    }

    private void ApplyDisplayHz(double hz)
    {
        double interval = FrameTiming.IntervalSeconds(hz);
        if (Math.Abs(1 / interval - 1 / _frameInterval) >= 1 && _hook != 0) { Message($"glide rate is now {1 / interval:0.#} Hz"); }
        _frameInterval = interval;
    }

    private EngineCounters Counters() =>
        new(_hookCalls, _physicalWheels, _injectedWheels, _rawWheels, _sends, _sendFailures,
            _breaker.Echoes, _breaker.Trips, _hookReinstalls);

    private void PublishStatus(bool running)
    {
        Volatile.Write(ref _status, new EngineStatus(
            running,
            HookInstalled: running && Volatile.Read(ref _hook) != 0,
            Paused: _paused,
            BreakerOpen: _breaker.IsOpen,
            GlideHz: 1 / _frameInterval,
            Routing: _targets.Routing,
            Router: _router.Stats,
            Counters: Counters(),
            LastRouterFault: _router.LastFault?.Message));
    }

    // MARK: Plumbing

    private void Post(Action action)
    {
        _invokes.Enqueue(action);
        nint hwnd = _hwnd;
        if (hwnd != 0) { Native.PostMessageW(hwnd, WM_APP_INVOKE, 0, 0); }
    }

    private void RunInvokes()
    {
        while (_invokes.TryDequeue(out Action? action)) { action(); }
    }

    private void OnFault(Exception ex)
    {
        _fault ??= ex;
    }

    private void Message(string text)
    {
        _messages.Enqueue($"{DateTime.Now:HH:mm:ss.fff} {text}");
        while (_messages.Count > 1000 && _messages.TryDequeue(out _)) { }
    }
}

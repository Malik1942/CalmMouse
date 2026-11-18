// The Windows scroll router: spec 1.1 design A, with no Windows calls, no clock and no threads.
//
// The low-level mouse hook can swallow a wheel event but doesn't know which mouse sent it; Raw
// Input knows the mouse but can't swallow anything. Phase 0 showed that every hook wheel event,
// swallowed or not, is followed by a Raw Input wheel from the real device, and that our own
// SendInput resends come back through Raw Input with no device (hDevice 0). So the hook swallows
// physical wheel events, each waits here until its Raw Input twin names the mouse, and the
// mouse's own settings decide what is sent in its place.
//
// Who calls what (all on the one input thread, in the order things happen):
//   - the WH_MOUSE_LL callback, for a wheel event without LLMHF_INJECTED: HookWheel, then return
//     its answer (Swallow = return 1). Injected events are passed without asking.
//   - WM_INPUT: RawWheel for a wheel (usButtonData as the delta, ulExtraInformation as extraInfo),
//     RawButton for a middle-button transition.
//   - a timer: Tick while NeedsTicks, every display frame while a glide runs and at NextDeadline
//     for the 50 ms timeouts.
//   - after every call: drain TryTakeSend and inject each one, marked with ResendMarker.
// Every t is seconds on one monotonic clock read when the event is received (not the hook's
// millisecond `time` field, which runs on a different clock than WM_INPUT).
//
// Units and axes. The router hands the core the raw Windows delta (120 per notch) as the event's
// delta: vertical WM_MOUSEWHEEL → DeltaY with the same sign (positive = away from the user),
// horizontal WM_MOUSEHWHEEL → DeltaX with the same sign (positive = right). Output maps back the
// same way: X → MOUSEEVENTF_HWHEEL, Y → MOUSEEVENTF_WHEEL, signs unchanged. Identity is safe
// because the core never looks at the sign of a wheel's X except through its Mac-only Shift
// swap, which the router never triggers (Shift isn't passed, see ToCore).
//
// The core glides max(|delta|, 35) × speed "Mac pixels" per tick; WheelUnitsPerCorePixel turns
// those back into wheel units. At 1/3 a 120 notch at the default speed (3) glides 360 pixels, which
// is exactly 120 units, the distance the notch would have scrolled unsmoothed. Known issue for
// tuning: a high-resolution wheel that sends deltas below 35 gets the core's 35 floor, so each
// of its small ticks glides further than it would have jumped (a 15 glides as 35).


namespace CalmMouse.Core.Routing;

/// <summary>
/// Decides, per physical wheel event, whether the hook swallows it and what is sent instead.
/// Pure and single-threaded: feed it events in the order they arrive, then drain the sends.
/// </summary>
public sealed class ScrollRouter
{
    /// <summary>
    /// Put in dwExtraInfo of every SendInput this router asks for. Raw Input wheels carrying it
    /// (or no device at all) are our own resends coming back and are ignored. "CMSM", as on the Mac.
    /// </summary>
    public const uint ResendMarker = 0x434D_534D;

    /// <summary>
    /// How long a hook event and a Raw Input wheel may be apart and still be the same physical
    /// tick, and so how long a swallowed event waits for its device before it is sent on
    /// unchanged. Seconds. Phase 0 measured 0.1 ms typical and 1 ms worst.
    /// </summary>
    public const double MatchWindow = 0.050;

    /// <summary>
    /// Wheel units per core "pixel" of glide. At the default speed (3) one 120 notch glides
    /// 360 pixels; a third of that is 120 again. To be tuned by hand on a real PC (plan task 16).
    /// </summary>
    public const double WheelUnitsPerCorePixel = 1.0 / 3.0;

    /// <summary>A hook event waiting for its Raw Input twin.</summary>
    /// <param name="Swallowed">
    /// True: the hook ate it and the twin decides what to send. False: it went through (we were
    /// passing), and the record is kept only so its twin has something to pair with, instead of
    /// waiting around and pairing with an unrelated hook event later.
    /// </param>
    private readonly record struct HookRecord(double T, int Delta, bool Horizontal, WinModifiers Modifiers, bool Swallowed);

    /// <summary>A Raw Input wheel that arrived before its hook event.</summary>
    private readonly record struct RawRecord(double T, long DeviceId, int Delta, bool Horizontal);

    /// <summary>One axis of a glide in wheel units: core pixels paid out so far and units already sent.</summary>
    private struct AxisUnits
    {
        private long _pixels;
        private long _sent;

        /// <summary>
        /// Adds this frame's pixels and returns the whole units now due. Working from the running
        /// totals means no fraction is ever lost; when the axis has settled, the total is rounded
        /// and the axis starts again from zero, so a finished glide sends exactly round(pixels / 3).
        /// </summary>
        public int Add(long pixels, bool settled)
        {
            _pixels += pixels;
            double exact = _pixels * WheelUnitsPerCorePixel;
            long target = (long)(settled ? Math.Round(exact, MidpointRounding.AwayFromZero) : Math.Truncate(exact));
            long due = target - _sent;
            _sent = target;
            if (settled) { this = default; }
            return (int)Math.Clamp(due, int.MinValue, int.MaxValue);
        }
    }

    private sealed class Device(RoutedDevice info)
    {
        public RoutedDevice Info = info;
        /// <summary>
        /// One per wheel mouse: the core keeps a single "wheel pressed" flag, so a shared blocker
        /// would let pressing the wheel on one mouse stop another from scrolling.
        /// </summary>
        public readonly ScrollBlocker Blocker = new();
        /// <summary>A mutable struct: only ever used through this field, never copied.</summary>
        public WheelSmoother Smoother = new();
        public double LastFrame;
        public AxisUnits X;
        public AxisUnits Y;

        public bool IsWheelMouse => Info.Kind == DeviceKind.WheelMouse;
        public bool IsGliding => !Smoother.IsIdle;

        public void CancelGlide()
        {
            Smoother.Cancel();
            X = default;
            Y = default;
        }
    }

    private readonly Dictionary<long, Device> _devices = [];
    private readonly List<HookRecord> _hooks = [];
    private readonly List<RawRecord> _raws = [];
    private readonly Queue<WheelSend> _sends = new();
    private WheelAppRule[] _appRules = [];
    private WheelAppRule? _activeRule;
    private string? _foregroundExe;
    private bool _foregroundElevated;
    private bool _paused;
    private bool _swallowing;
    private RouterStats _stats;

    // MARK: State the caller reads

    /// <summary>
    /// True while the hook swallows physical wheel events: not paused, the foreground window isn't
    /// elevated, its app isn't ruled "leave scrolling alone", and some wheel mouse has a feature on
    /// (with the foreground app's rule applied). Otherwise every wheel event passes untouched.
    /// </summary>
    public bool IsSwallowing => _swallowing;

    /// <summary>True while a glide is running or a swallowed event waits for its device: keep calling Tick.</summary>
    public bool NeedsTicks
    {
        get
        {
            foreach (HookRecord h in _hooks)
            {
                if (h.Swallowed) { return true; }
            }
            foreach (Device d in _devices.Values)
            {
                if (d.IsGliding) { return true; }
            }
            return false;
        }
    }

    /// <summary>When the oldest swallowed event times out, so a one-shot timer can call Tick then. Null if none waits.</summary>
    public double? NextDeadline
    {
        get
        {
            double? next = null;
            foreach (HookRecord h in _hooks)
            {
                if (h.Swallowed && (next == null || h.T + MatchWindow < next)) { next = h.T + MatchWindow; }
            }
            return next;
        }
    }

    public RouterStats Stats => _stats;

    /// <summary>Wheel events waiting to be injected.</summary>
    public int PendingSendCount => _sends.Count;

    /// <summary>Takes the next wheel event to inject, oldest first. Allocation-free.</summary>
    public bool TryTakeSend(out WheelSend send) => _sends.TryDequeue(out send);

    // MARK: Inputs: events

    /// <summary>
    /// A physical wheel event in the low-level hook. <paramref name="modifiers"/> is the keyboard
    /// state at that moment. Returns Pass when the event may go through as is (including when
    /// its device is already known and nothing would change it), Swallow when the router has
    /// taken it over.
    /// </summary>
    public HookAnswer HookWheel(double t, int delta, bool horizontal, WinModifiers modifiers = WinModifiers.None)
    {
        Expire(t);
        if (delta == 0) { return Passed(); }

        // A Raw Input wheel that came first is this event's twin, whether or not we swallow.
        int r = OldestMatch(_raws, horizontal, delta, t);
        long deviceId = 0;
        if (r >= 0)
        {
            deviceId = _raws[r].DeviceId;
            _raws.RemoveAt(r);
        }

        if (!_swallowing)
        {
            // Its twin hasn't come yet: leave a note so it pairs with this event when it does.
            if (r < 0) { _hooks.Add(new HookRecord(t, delta, horizontal, modifiers, Swallowed: false)); }
            return Passed();
        }

        if (r >= 0)
        {
            // The device is known already: decide now. "Unchanged" can simply go through.
            if (!Route(t, deviceId, delta, horizontal, modifiers)) { return Passed(); }
            _stats = _stats with { HooksSwallowed = _stats.HooksSwallowed + 1 };
            return HookAnswer.Swallow;
        }

        _hooks.Add(new HookRecord(t, delta, horizontal, modifiers, Swallowed: true));
        _stats = _stats with { HooksSwallowed = _stats.HooksSwallowed + 1 };
        return HookAnswer.Swallow;
    }

    /// <summary>
    /// A Raw Input wheel. <paramref name="deviceId"/> is hDevice (0 = none), <paramref name="delta"/>
    /// is usButtonData as a signed short, <paramref name="horizontal"/> is RI_MOUSE_HWHEEL, and
    /// <paramref name="extraInfo"/> is ulExtraInformation.
    /// </summary>
    public void RawWheel(double t, long deviceId, int delta, bool horizontal, ulong extraInfo = 0)
    {
        if (deviceId == 0 || extraInfo == ResendMarker)
        {
            _stats = _stats with { IgnoredRaws = _stats.IgnoredRaws + 1 };
            return;
        }
        Expire(t);
        if (delta == 0) { return; }

        int h = OldestMatch(_hooks, horizontal, delta, t);
        if (h < 0)
        {
            // Its hook event hasn't come yet (or never will, if the device's wheel isn't seen by the hook).
            _raws.Add(new RawRecord(t, deviceId, delta, horizontal));
            return;
        }

        HookRecord hook = _hooks[h];
        _hooks.RemoveAt(h);
        if (!hook.Swallowed) { return; }   // it already went through; nothing to do
        if (!Route(t, deviceId, hook.Delta, hook.Horizontal, hook.Modifiers))
        {
            Send(hook.Delta, hook.Horizontal);
        }
    }

    /// <summary>A Raw Input button transition. Only a wheel mouse's middle button (its wheel click) matters.</summary>
    public void RawButton(double t, long deviceId, RawMouseButton button, bool down)
    {
        if (button != RawMouseButton.Middle) { return; }
        if (!_devices.TryGetValue(deviceId, out Device? d) || !d.IsWheelMouse) { return; }
        d.Blocker.WheelMouseButton(ScrollBlocker.WheelButton, down, t);
        // Pressing the wheel means "hold still": this mouse's glide stops with it (as on the Mac).
        if (down && Effective(d.Info.Wheel).BlockWhilePressed) { d.CancelGlide(); }
    }

    /// <summary>
    /// Advances time: swallowed events nobody claimed within <see cref="MatchWindow"/> are sent on
    /// unchanged, and every running glide pays out its next frame.
    /// </summary>
    public void Tick(double t)
    {
        Expire(t);
        foreach (Device d in _devices.Values)
        {
            if (d.IsGliding) { StepGlide(d, t); }
        }
    }

    // MARK: Inputs: state

    /// <summary>The foreground window changed. <paramref name="exeName"/> is its process's file name (null if unknown).</summary>
    public void ForegroundChanged(string? exeName, bool elevated)
    {
        _foregroundExe = exeName;
        _foregroundElevated = elevated;
        _activeRule = FindRule(exeName);
        foreach (Device d in _devices.Values) { d.Blocker.SetActiveApp(exeName); }
        Reevaluate();
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;
        Reevaluate();
    }

    /// <summary>Adds a device or updates its kind and settings. Its button and glide state carry over.</summary>
    public void SetDevice(RoutedDevice device)
    {
        Upsert(device);
        Reevaluate();
    }

    /// <summary>Replaces the whole device list. Devices that stay keep their button and glide state.</summary>
    public void SetDevices(IEnumerable<RoutedDevice> devices)
    {
        var keep = new HashSet<long>();
        foreach (RoutedDevice device in devices)
        {
            keep.Add(device.DeviceId);
            Upsert(device);
        }
        foreach (long id in _devices.Keys.Where(id => !keep.Contains(id)).ToList())
        {
            _devices.Remove(id);
        }
        Reevaluate();
    }

    public void RemoveDevice(long deviceId)
    {
        _devices.Remove(deviceId);
        Reevaluate();
    }

    public void SetAppRules(IEnumerable<WheelAppRule> rules)
    {
        _appRules = rules.ToArray();
        _activeRule = FindRule(_foregroundExe);
        Reevaluate();
    }

    // MARK: Routing

    /// <summary>
    /// Runs one wheel event through its device's settings and queues whatever replaces it.
    /// Returns false when the event should go on unchanged (the caller passes or resends it).
    /// </summary>
    private bool Route(double t, long deviceId, int delta, bool horizontal, WinModifiers modifiers)
    {
        // Touchpads and unknown devices never go through the core.
        if (!_devices.TryGetValue(deviceId, out Device? d) || !d.IsWheelMouse) { return false; }

        var e = new ScrollEvent(
            timestamp: t,
            fromMagicMouse: false,
            isContinuous: false,
            phase: ScrollPhase.None,
            momentum: MomentumPhase.None,
            deltaX: horizontal ? delta : 0,
            deltaY: horizontal ? 0 : delta,
            modifiers: ToCore(modifiers),
            fromWheelMouse: true,
            wheel: Effective(d.Info.Wheel));

        // A zoom-type tick means the user has moved on. Every glide stops, not only this mouse's:
        // SendInput can't strip the held key from a glide frame, so any glide still running would
        // zoom (on the Mac there is one glide for all mice, and it stops the same way).
        if (ScrollBlocker.InterruptsGlide(e)) { CancelAllGlides(); }

        switch (d.Blocker.HandleScroll(e))
        {
            case ScrollDecision.Pass:
                return false;
            case ScrollDecision.Drop:
                return true;
            case ScrollDecision.Rewrite(ScrollRewrite rw):
                if (rw.IsNoop(e)) { return false; }
                // A wheel only ever gets Invert. The rest is applied as far as a resend can carry it;
                // modifier changes can't be (the keyboard state is what it is).
                double x = e.DeltaX, y = e.DeltaY;
                if (rw.SwapAxes) { (x, y) = (y, x); }
                if (rw.Invert) { x = -x; y = -y; }
                if (rw.ZeroX) { x = 0; }
                if (rw.ZeroY) { y = 0; }
                if (x != 0) { Send((int)x, horizontal: true); }
                if (y != 0) { Send((int)y, horizontal: false); }
                return true;
            case ScrollDecision.Smooth(double dx, double dy):
                if (!d.IsGliding) { d.LastFrame = t; }
                d.Smoother.Add(dx, dy);
                return true;
            default:
                return false;
        }
    }

    private void StepGlide(Device d, double t)
    {
        WheelSmoother.StepResult? step = d.Smoother.Step(t - d.LastFrame);
        d.LastFrame = t;
        if (step is not { } s) { return; }
        (double owedX, double owedY) = d.Smoother.Owed;
        int y = d.Y.Add(s.Y, settled: owedY == 0);
        int x = d.X.Add(s.X, settled: owedX == 0);
        if (y != 0) { Send(y, horizontal: false); }
        if (x != 0) { Send(x, horizontal: true); }
    }

    /// <summary>
    /// Ctrl, Alt and Win are the core's ⌘, ⌥ and ⌃ (spec 1.3), all three in its smoothing-bypass
    /// set, so zoom-type ticks are never smoothed. Shift is left out on purpose: Windows apps turn
    /// Shift+wheel sideways themselves, and the core's Mac-only Shift swap must not do it again.
    /// </summary>
    private static ModifierCombo ToCore(WinModifiers m)
    {
        ModifierCombo combo = ModifierCombo.None;
        if ((m & WinModifiers.Ctrl) != 0) { combo |= ModifierCombo.Command; }
        if ((m & WinModifiers.Alt) != 0) { combo |= ModifierCombo.Option; }
        if ((m & WinModifiers.Win) != 0) { combo |= ModifierCombo.Control; }
        return combo;
    }

    /// <summary>A device's settings with the foreground app's rule applied.</summary>
    private WheelSettings Effective(WheelSettings w) =>
        _activeRule is { NoSmoothing: true } ? w with { Smooth = false } : w;

    // MARK: Matching

    private static bool Live(double eventT, double t) => t < eventT + MatchWindow;

    private static bool SameTick(bool horizontalA, int deltaA, bool horizontalB, int deltaB) =>
        horizontalA == horizontalB && (deltaA < 0) == (deltaB < 0);

    private static int OldestMatch(List<RawRecord> raws, bool horizontal, int delta, double t)
    {
        for (int i = 0; i < raws.Count; i++)
        {
            RawRecord r = raws[i];
            if (Live(r.T, t) && SameTick(r.Horizontal, r.Delta, horizontal, delta)) { return i; }
        }
        return -1;
    }

    private static int OldestMatch(List<HookRecord> hooks, bool horizontal, int delta, double t)
    {
        for (int i = 0; i < hooks.Count; i++)
        {
            HookRecord h = hooks[i];
            if (Live(h.T, t) && SameTick(h.Horizontal, h.Delta, horizontal, delta)) { return i; }
        }
        return -1;
    }

    /// <summary>
    /// Forgets everything older than the match window. A swallowed event whose device never
    /// showed up is sent on unchanged, so a device Raw Input doesn't report still scrolls.
    /// </summary>
    private void Expire(double t)
    {
        for (int i = 0; i < _hooks.Count;)
        {
            HookRecord h = _hooks[i];
            if (Live(h.T, t)) { i++; continue; }
            _hooks.RemoveAt(i);
            if (h.Swallowed)
            {
                _stats = _stats with { UnclaimedResends = _stats.UnclaimedResends + 1 };
                Send(h.Delta, h.Horizontal);
            }
        }
        for (int i = 0; i < _raws.Count;)
        {
            if (Live(_raws[i].T, t)) { i++; continue; }
            _raws.RemoveAt(i);
            _stats = _stats with { UnmatchedRaws = _stats.UnmatchedRaws + 1 };
        }
    }

    // MARK: Helpers

    private HookAnswer Passed()
    {
        _stats = _stats with { HooksPassed = _stats.HooksPassed + 1 };
        return HookAnswer.Pass;
    }

    private void Send(int delta, bool horizontal) => _sends.Enqueue(new WheelSend(delta, horizontal));

    private void Upsert(RoutedDevice info)
    {
        if (_devices.TryGetValue(info.DeviceId, out Device? d))
        {
            d.Info = info;
            if (!d.IsWheelMouse) { d.CancelGlide(); }
            return;
        }
        d = new Device(info);
        d.Blocker.SetActiveApp(_foregroundExe);
        _devices[info.DeviceId] = d;
    }

    private WheelAppRule? FindRule(string? exeName)
    {
        if (exeName == null) { return null; }
        foreach (WheelAppRule rule in _appRules)
        {
            if (rule.Enabled && string.Equals(rule.ExeName, exeName, StringComparison.OrdinalIgnoreCase)) { return rule; }
        }
        return null;
    }

    private void CancelAllGlides()
    {
        foreach (Device d in _devices.Values) { d.CancelGlide(); }
    }

    /// <summary>
    /// Works out whether the hook should swallow. On the way from swallowing to passing, events
    /// still waiting for their device go on unchanged (their twins are still paired with them
    /// when they come) and every glide stops where it is.
    /// </summary>
    private void Reevaluate()
    {
        bool swallowing = !_paused && !_foregroundElevated && _activeRule is not { LeaveAlone: true }
                          && _devices.Values.Any(d => d.IsWheelMouse && HasFeature(Effective(d.Info.Wheel)));
        if (_swallowing && !swallowing)
        {
            for (int i = 0; i < _hooks.Count; i++)
            {
                HookRecord h = _hooks[i];
                if (!h.Swallowed) { continue; }
                Send(h.Delta, h.Horizontal);
                _hooks[i] = h with { Swallowed = false };
            }
            CancelAllGlides();
        }
        _swallowing = swallowing;
    }

    private static bool HasFeature(WheelSettings w) => w.Smooth || w.Reverse || w.BlockWhilePressed;
}

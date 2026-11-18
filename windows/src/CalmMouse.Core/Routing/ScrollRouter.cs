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
//     its answer (Swallow = return 1). Injected events are passed without asking. With it go the
//     keys held (GetAsyncKeyState), whether the middle button is physically down, and the window
//     the event will scroll (WindowFromPoint on the hook's cursor point: by default Windows
//     scrolls the window under the cursor, not the foreground one).
//   - WM_INPUT: RawWheel for a wheel (usButtonData as the delta, ulExtraInformation as extraInfo),
//     RawButton for a middle-button transition.
//   - a timer: Tick while NeedsTicks, every display frame while a glide runs and at NextDeadline
//     for the timeouts, with the keys held at that moment.
//   - when the window under the cursor changes between wheel events (optional, but it stops a
//     glide from playing into an app where it shouldn't): PointerTargetChanged.
//   - after every call: drain TryTakeSend and inject each one, marked with ResendMarker.
// Every t is seconds on one monotonic clock read when the event is received (not the hook's
// millisecond `time` field, which runs on a different clock than WM_INPUT).
//
// No exception leaves a public method: anything thrown inside is caught, counted in
// Stats.Faults, kept in LastFault, and the event concerned goes on unchanged (a glide that
// throws is dropped). Scrolling must never die because of a bug here.
//
// Allocation: the hook path allocates nothing of its own once the internal lists have grown, but
// every event that reaches the core costs one small ScrollDecision record (24 to 40 bytes,
// from the port). For a Raw-Input-first event that happens inside the hook callback. That is
// gen-0 garbage, accepted.
//
// Units and axes. The router hands the core the raw Windows delta (120 per notch) as the event's
// delta: vertical WM_MOUSEWHEEL → DeltaY with the same sign (positive = away from the user),
// horizontal WM_MOUSEHWHEEL → DeltaX with the same sign (positive = right). Output maps back the
// same way: X → MOUSEEVENTF_HWHEEL, Y → MOUSEEVENTF_WHEEL, signs unchanged. Identity is safe
// because the core never looks at the sign of a wheel's X except through its Mac-only Shift
// swap, which the router never triggers (Shift isn't passed, see ToCore).
//
// The core glides max(|delta|, 35) × speed "Mac pixels" per tick. The 35 floor makes a slow
// macOS notch (1 pixel) worth turning; Windows deltas are already notch-sized, so the router
// scales the glide back by |delta| / max(|delta|, 35): a high-resolution wheel's 8 ticks of 15
// glide the same 120 as one 120 notch. Then CorePixelsPerWheelUnit turns pixels back into wheel
// units: at the default speed (3) a 120 notch glides 360 pixels, which is 120 units again, the
// distance the notch would have scrolled unsmoothed.

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
    /// How long a swallowed hook event waits for its device before it is sent on unchanged
    /// (the touchpad safety net), and so how late a Raw Input wheel with exactly its delta may
    /// still claim it. Seconds.
    /// </summary>
    public const double UnclaimedDeadline = 0.050;

    /// <summary>
    /// How far apart a hook event and a Raw Input wheel may be for the looser pairings: a Raw
    /// Input wheel that came first waits this long for its hook event, and a pair with the same
    /// direction but a different delta must be this close. Phase 0 measured 0.1 ms typical and
    /// 1 ms worst. Seconds.
    /// </summary>
    public const double PairWindow = 0.010;

    /// <summary>
    /// Core "pixels" of glide per wheel unit. At the default speed (3) one 120 notch glides 360
    /// pixels, three per unit. To be tuned by hand on a real PC (plan task 16).
    /// </summary>
    public const double CorePixelsPerWheelUnit = 3.0;

    /// <summary>The Mac's range for a wheel's speed (Settings.swift); anything else is clamped into it, non-numbers become the default.</summary>
    public const double MinimumSpeed = 1, MaximumSpeed = 12, DefaultSpeed = 3;

    /// <summary>A hook event waiting for its Raw Input twin.</summary>
    /// <param name="Swallowed">
    /// True: the hook ate it and the twin decides what to send. False: it went through (we were
    /// passing), and the record is kept only so its twin has something to pair with, instead of
    /// waiting around and pairing with an unrelated hook event later.
    /// </param>
    private readonly record struct HookRecord(double T, int Delta, bool Horizontal, WinModifiers Modifiers,
                                              WheelTarget Target, bool Swallowed);

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
            double exact = _pixels / CorePixelsPerWheelUnit;
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
        /// <summary>The window the glide is playing into: where the last wheel event or pointer move said the cursor is.</summary>
        public WheelTarget GlideTarget;

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
    private bool _paused;
    /// <summary>Some wheel mouse has smoothing, reverse or blocking on.</summary>
    private bool _anyFeature;
    /// <summary>Some wheel mouse has reverse or blocking on (what is left in a "don't smooth here" app).</summary>
    private bool _anyDiscreteFeature;
    // The last rule lookup, by string reference: the caller hands the same exe-name string for
    // the same window, so the hook rarely compares strings.
    private string? _cachedExe;
    private WheelAppRule? _cachedRule;
    private RouterStats _stats;

    // MARK: State the caller reads

    /// <summary>
    /// Whether the hook swallows wheel events aimed at <paramref name="target"/>: not paused, the
    /// window isn't elevated, its app isn't ruled "leave scrolling alone", and some wheel mouse has
    /// a feature on there (with the app's rule applied). Otherwise every event passes untouched.
    /// </summary>
    public bool Swallows(WheelTarget target)
    {
        if (_paused || target.Elevated) { return false; }
        WheelAppRule? rule = RuleFor(target.ExeName);
        if (rule is { LeaveAlone: true }) { return false; }
        return rule is { NoSmoothing: true } ? _anyDiscreteFeature : _anyFeature;
    }

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
                if (h.Swallowed && (next == null || h.T + UnclaimedDeadline < next)) { next = h.T + UnclaimedDeadline; }
            }
            return next;
        }
    }

    public RouterStats Stats => _stats;

    /// <summary>The last exception caught inside the router, for the log. Null if none.</summary>
    public Exception? LastFault { get; private set; }

    /// <summary>Wheel events waiting to be injected.</summary>
    public int PendingSendCount => _sends.Count;

    /// <summary>Takes the next wheel event to inject, oldest first. Allocation-free.</summary>
    public bool TryTakeSend(out WheelSend send) => _sends.TryDequeue(out send);

    // MARK: Inputs: events

    /// <summary>
    /// A physical wheel event in the low-level hook. Returns Pass when the event may go through as
    /// is (including when its device is already known and nothing would change it), Swallow when
    /// the router has taken it over.
    /// </summary>
    /// <param name="modifiers">The keys held at that moment.</param>
    /// <param name="middleDown">
    /// Whether the middle button is physically down right now (GetAsyncKeyState(VK_MBUTTON)). If it
    /// isn't, any wheel press the router still believes in was a lost button-up and is released.
    /// </param>
    /// <param name="target">The window this event will scroll; its app rule and elevation decide.</param>
    public HookAnswer HookWheel(double t, int delta, bool horizontal, WinModifiers modifiers, bool middleDown, WheelTarget target)
    {
        try
        {
            Expire(t);
            if (!middleDown) { ReleaseStuckWheels(t); }
            TargetSeen(target);
            if (delta == 0) { return Passed(); }

            // A Raw Input wheel that came first is this event's twin, whether or not we swallow.
            int r = MatchRaw(horizontal, delta, t);
            long deviceId = 0;
            if (r >= 0)
            {
                deviceId = _raws[r].DeviceId;
                _raws.RemoveAt(r);
            }

            if (!Swallows(target))
            {
                // Its twin hasn't come yet: leave a note so it pairs with this event when it does.
                if (r < 0) { _hooks.Add(new HookRecord(t, delta, horizontal, modifiers, target, Swallowed: false)); }
                return Passed();
            }

            if (r >= 0)
            {
                // The device is known already: decide now. "Unchanged" can simply go through.
                if (!TryRoute(t, deviceId, delta, horizontal, modifiers, target)) { return Passed(); }
                return Swallowed();
            }

            _hooks.Add(new HookRecord(t, delta, horizontal, modifiers, target, Swallowed: true));
            return Swallowed();
        }
        catch (Exception ex)
        {
            Fault(ex);
            return HookAnswer.Pass;
        }
    }

    /// <summary>
    /// A Raw Input wheel. <paramref name="deviceId"/> is hDevice (0 = none), <paramref name="delta"/>
    /// is usButtonData as a signed short, <paramref name="horizontal"/> is RI_MOUSE_HWHEEL, and
    /// <paramref name="extraInfo"/> is ulExtraInformation.
    /// </summary>
    public void RawWheel(double t, long deviceId, int delta, bool horizontal, ulong extraInfo = 0)
    {
        try
        {
            if (deviceId == 0 || extraInfo == ResendMarker)
            {
                _stats = _stats with { IgnoredRaws = _stats.IgnoredRaws + 1 };
                return;
            }
            Expire(t);
            if (delta == 0) { return; }

            int h = MatchHook(horizontal, delta, t);
            if (h < 0)
            {
                // Its hook event hasn't come yet (or never will, if the device's wheel isn't seen by the hook).
                _raws.Add(new RawRecord(t, deviceId, delta, horizontal));
                return;
            }

            HookRecord hook = _hooks[h];
            _hooks.RemoveAt(h);
            if (!hook.Swallowed) { return; }   // it already went through; nothing to do
            if (!TryRoute(t, deviceId, hook.Delta, hook.Horizontal, hook.Modifiers, hook.Target))
            {
                Send(hook.Delta, hook.Horizontal);
            }
        }
        catch (Exception ex)
        {
            Fault(ex);
        }
    }

    /// <summary>A Raw Input button transition. Only a wheel mouse's middle button (its wheel click) matters.</summary>
    public void RawButton(double t, long deviceId, RawMouseButton button, bool down)
    {
        try
        {
            if (button != RawMouseButton.Middle) { return; }
            if (!_devices.TryGetValue(deviceId, out Device? d) || !d.IsWheelMouse) { return; }
            d.Blocker.WheelMouseButton(ScrollBlocker.WheelButton, down, t);
            // Pressing the wheel means "hold still": this mouse's glide stops with it (as on the Mac).
            if (down && d.Info.Wheel.BlockWhilePressed) { d.CancelGlide(); }
        }
        catch (Exception ex)
        {
            Fault(ex);
        }
    }

    /// <summary>
    /// Advances time: swallowed events nobody claimed within <see cref="UnclaimedDeadline"/> are
    /// sent on unchanged, and every running glide pays out its next frame.
    /// </summary>
    /// <param name="held">
    /// The keys held now. Any of them stops every glide: Windows applies the keys held at the moment
    /// each glide frame is injected, so Ctrl, Alt or Win would turn the rest of the glide into zoom
    /// steps, and Shift would turn it sideways. (A glide never starts with a key held: Ctrl, Alt
    /// and Win ticks are never smoothed by the core, and Shift ticks aren't by the router.)
    /// </param>
    public void Tick(double t, WinModifiers held)
    {
        try
        {
            if (!double.IsFinite(t)) { return; }   // a broken clock reading: wait for a real one
            Expire(t);
            if (held != WinModifiers.None) { CancelAllGlides(); }
            foreach (Device d in _devices.Values)
            {
                if (!d.IsGliding) { continue; }
                try
                {
                    StepGlide(d, t);
                }
                catch (Exception ex)
                {
                    Fault(ex);
                    d.CancelGlide();
                }
            }
        }
        catch (Exception ex)
        {
            Fault(ex);
        }
    }

    // MARK: Inputs: state

    /// <summary>
    /// The window under the cursor changed between wheel events. Glides stop if they mustn't play
    /// into the new one: elevated, ruled "leave alone" or "don't smooth", or paused.
    /// </summary>
    public void PointerTargetChanged(WheelTarget target)
    {
        try
        {
            TargetSeen(target);
        }
        catch (Exception ex)
        {
            Fault(ex);
        }
    }

    public void SetPaused(bool paused)
    {
        try
        {
            _paused = paused;
            Reevaluate();
        }
        catch (Exception ex)
        {
            Fault(ex);
        }
    }

    /// <summary>Adds a device or updates its kind and settings. Its button and glide state carry over.</summary>
    public void SetDevice(RoutedDevice device)
    {
        try
        {
            Upsert(device);
            Reevaluate();
        }
        catch (Exception ex)
        {
            Fault(ex);
        }
    }

    /// <summary>Replaces the whole device list. Devices that stay keep their button and glide state.</summary>
    public void SetDevices(IEnumerable<RoutedDevice> devices)
    {
        try
        {
            var keep = new HashSet<long>();
            foreach (RoutedDevice device in devices ?? [])
            {
                if (device == null) { continue; }
                keep.Add(device.DeviceId);
                Upsert(device);
            }
            foreach (long id in _devices.Keys.Where(id => !keep.Contains(id)).ToList())
            {
                _devices.Remove(id);
            }
            Reevaluate();
        }
        catch (Exception ex)
        {
            Fault(ex);
        }
    }

    public void RemoveDevice(long deviceId)
    {
        try
        {
            _devices.Remove(deviceId);
            Reevaluate();
        }
        catch (Exception ex)
        {
            Fault(ex);
        }
    }

    public void SetAppRules(IEnumerable<WheelAppRule> rules)
    {
        try
        {
            _appRules = (rules ?? []).Where(rule => rule != null).ToArray();
            _cachedExe = null;
            _cachedRule = null;
            Reevaluate();
        }
        catch (Exception ex)
        {
            Fault(ex);
        }
    }

    // MARK: Routing

    /// <summary><see cref="Route"/>, but a fault means "unchanged" rather than losing the event.</summary>
    private bool TryRoute(double t, long deviceId, int delta, bool horizontal, WinModifiers modifiers, WheelTarget target)
    {
        try
        {
            return Route(t, deviceId, delta, horizontal, modifiers, target);
        }
        catch (Exception ex)
        {
            Fault(ex);
            return false;
        }
    }

    /// <summary>
    /// Runs one wheel event through its device's settings and queues whatever replaces it.
    /// Returns false when the event should go on unchanged (the caller passes or resends it).
    /// </summary>
    private bool Route(double t, long deviceId, int delta, bool horizontal, WinModifiers modifiers, WheelTarget target)
    {
        // Touchpads and unknown devices never go through the core.
        if (!_devices.TryGetValue(deviceId, out Device? d) || !d.IsWheelMouse) { return false; }

        WheelSettings wheel = Effective(d.Info.Wheel, RuleFor(target.ExeName));
        // Windows turns a Shift tick sideways with the Shift state of the moment it arrives, and a
        // glide can't promise Shift stays down for its whole length (letting go half-way would turn
        // the rest vertical). So a Shift tick goes out as one whole notch, and stops any glide.
        bool shift = (modifiers & WinModifiers.Shift) != 0;
        if (shift) { wheel = wheel with { Smooth = false }; }
        d.Blocker.SetActiveApp(target.ExeName);

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
            wheel: wheel);

        // A zoom-type tick means the user has moved on. Every glide stops, not only this mouse's:
        // SendInput can't strip the held key from a glide frame, so any glide still running would
        // zoom (on the Mac there is one glide for all mice, and it stops the same way).
        if (shift || ScrollBlocker.InterruptsGlide(e)) { CancelAllGlides(); }

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
                if (x != 0) { Send(ToInt(x), horizontal: true); }
                if (y != 0) { Send(ToInt(y), horizontal: false); }
                return true;
            case ScrollDecision.Smooth(double dx, double dy):
                StartOrExtendGlide(d, t, target, delta, dx, dy);
                return true;
            default:
                return false;
        }
    }

    private static void StartOrExtendGlide(Device d, double t, WheelTarget target, int delta, double dx, double dy)
    {
        // Undo the core's 35 floor (see the top of the file): multiply first, so 105 × 15 / 35 is exactly 45.
        double magnitude = Math.Abs((double)delta);
        double floored = Math.Max(magnitude, ScrollBlocker.WheelMinimumStep);
        dx = dx * magnitude / floored;
        dy = dy * magnitude / floored;

        // Turning the other way: the smoother drops what it still owed on that axis, and the
        // unit count starts again with it, so the new direction glides its full distance.
        (double owedX, double owedY) = d.Smoother.Owed;
        if (dx != 0 && owedX != 0 && (owedX < 0) != (dx < 0)) { d.X = default; }
        if (dy != 0 && owedY != 0 && (owedY < 0) != (dy < 0)) { d.Y = default; }

        if (!d.IsGliding) { d.LastFrame = t; }
        d.GlideTarget = target;
        d.Smoother.Add(dx, dy);
    }

    private void StepGlide(Device d, double t)
    {
        // A glide started on a broken clock reading would never move (the smoother treats a NaN
        // frame time as zero): start its frames from now instead.
        if (!double.IsFinite(d.LastFrame)) { d.LastFrame = t; }
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

    /// <summary>
    /// A device's settings as they apply in one app: speed made sane (a hand-edited settings file
    /// could hold anything, and the smoother throws on an infinite glide), smoothing off where the
    /// app's rule says so.
    /// </summary>
    private static WheelSettings Effective(WheelSettings w, WheelAppRule? rule)
    {
        double speed = double.IsFinite(w.Speed) ? Math.Clamp(w.Speed, MinimumSpeed, MaximumSpeed) : DefaultSpeed;
        w = w with { Speed = speed };
        return rule is { NoSmoothing: true } ? w with { Smooth = false } : w;
    }

    /// <summary>Whether this mouse's glide may keep playing into <paramref name="target"/>.</summary>
    private bool GlideAllowed(Device d, WheelTarget target) =>
        d.IsWheelMouse && Swallows(target) && Effective(d.Info.Wheel, RuleFor(target.ExeName)).Smooth;

    /// <summary>The cursor is over <paramref name="target"/> now: glides go there, or stop if they mustn't.</summary>
    private void TargetSeen(WheelTarget target)
    {
        foreach (Device d in _devices.Values)
        {
            if (!d.IsGliding) { continue; }
            if (GlideAllowed(d, target)) { d.GlideTarget = target; } else { d.CancelGlide(); }
        }
    }

    // MARK: Matching
    //
    // A hook event and a Raw Input wheel on the same axis are the same tick, in this order of
    // preference (oldest first within each):
    //   1. exactly the same delta, at most PairWindow apart;
    //   2. exactly the same delta, the hook event at most UnclaimedDeadline older (a Raw Input
    //      wheel that was held up);
    //   3. the same direction, at most PairWindow apart.
    // A Raw Input wheel that comes first only waits PairWindow, so only 1 and 3 apply to it.
    // Exact deltas keep a mouse's notch from claiming a touchpad tick waiting next to it.
    // Preferring what is close in time keeps a late Raw Input wheel, or a waiting event whose
    // twin is late, from pairing with the wrong event and shifting every pairing after it.
    // Oldest first among close ones keeps the order when everything arrives in a burst after
    // the thread was held up.

    private static bool SameWay(int a, int b) => (a < 0) == (b < 0);

    /// <summary>The Raw Input wheel (all of them are at most PairWindow old) a new hook event pairs with.</summary>
    private int MatchRaw(bool horizontal, int delta, double t)
    {
        int sameWay = -1;
        for (int i = 0; i < _raws.Count; i++)
        {
            RawRecord r = _raws[i];
            if (r.Horizontal != horizontal || !(t < r.T + PairWindow)) { continue; }
            if (r.Delta == delta) { return i; }
            if (sameWay < 0 && SameWay(r.Delta, delta)) { sameWay = i; }
        }
        if (sameWay >= 0) { _stats = _stats with { InexactPairs = _stats.InexactPairs + 1 }; }
        return sameWay;
    }

    /// <summary>The waiting hook event a new Raw Input wheel pairs with.</summary>
    private int MatchHook(bool horizontal, int delta, double t)
    {
        int exactLate = -1, sameWay = -1;
        for (int i = 0; i < _hooks.Count; i++)
        {
            HookRecord h = _hooks[i];
            if (h.Horizontal != horizontal || !(t < h.T + UnclaimedDeadline)) { continue; }
            bool close = Math.Abs(t - h.T) < PairWindow;
            if (h.Delta == delta)
            {
                if (close) { return i; }
                if (exactLate < 0) { exactLate = i; }
            }
            else if (close && sameWay < 0 && SameWay(h.Delta, delta))
            {
                sameWay = i;
            }
        }
        if (exactLate >= 0) { return exactLate; }
        if (sameWay >= 0) { _stats = _stats with { InexactPairs = _stats.InexactPairs + 1 }; }
        return sameWay;
    }

    /// <summary>
    /// Forgets what is too old to pair. A swallowed event whose device never showed up is sent on
    /// unchanged, so a device Raw Input doesn't report still scrolls.
    /// </summary>
    private void Expire(double t)
    {
        for (int i = 0; i < _hooks.Count;)
        {
            HookRecord h = _hooks[i];
            if (t < h.T + UnclaimedDeadline) { i++; continue; }
            _hooks.RemoveAt(i);
            if (h.Swallowed)
            {
                _stats = _stats with { UnclaimedResends = _stats.UnclaimedResends + 1 };
                Send(h.Delta, h.Horizontal);
            }
        }
        for (int i = 0; i < _raws.Count;)
        {
            if (t < _raws[i].T + PairWindow) { i++; continue; }
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

    private HookAnswer Swallowed()
    {
        _stats = _stats with { HooksSwallowed = _stats.HooksSwallowed + 1 };
        return HookAnswer.Swallow;
    }

    private void Fault(Exception ex)
    {
        LastFault = ex;
        _stats = _stats with { Faults = _stats.Faults + 1 };
    }

    private void Send(int delta, bool horizontal) => _sends.Enqueue(new WheelSend(delta, horizontal));

    private static int ToInt(double value) =>
        double.IsNaN(value) ? 0 : (int)Math.Clamp(value, int.MinValue, int.MaxValue);

    /// <summary>
    /// The middle button is physically up, yet a mouse's blocker thinks its wheel is pressed: the
    /// button-up got lost. Release it, with the usual grace (the Mac's safety valve, EventTap.swift).
    /// </summary>
    private void ReleaseStuckWheels(double t)
    {
        foreach (Device d in _devices.Values)
        {
            if (!d.IsWheelMouse || !d.Blocker.AnyButtonDown) { continue; }
            d.Blocker.ForceReleaseAllButtons(t);
            _stats = _stats with { StuckWheelReleases = _stats.StuckWheelReleases + 1 };
        }
    }

    private void Upsert(RoutedDevice info)
    {
        if (_devices.TryGetValue(info.DeviceId, out Device? d))
        {
            d.Info = info;
            return;
        }
        _devices[info.DeviceId] = new Device(info);
    }

    private WheelAppRule? RuleFor(string? exeName)
    {
        if (exeName == null) { return null; }
        if (ReferenceEquals(exeName, _cachedExe)) { return _cachedRule; }
        WheelAppRule? found = null;
        foreach (WheelAppRule rule in _appRules)
        {
            if (rule.Enabled && string.Equals(rule.ExeName, exeName, StringComparison.OrdinalIgnoreCase))
            {
                found = rule;
                break;
            }
        }
        _cachedExe = exeName;
        _cachedRule = found;
        return found;
    }

    private void CancelAllGlides()
    {
        foreach (Device d in _devices.Values) { d.CancelGlide(); }
    }

    /// <summary>
    /// After pausing or a change of devices or rules: events still waiting for their device whose
    /// window is no longer handled go on unchanged (their twins are still paired with them when
    /// they come), and glides that mustn't continue stop where they are.
    /// </summary>
    private void Reevaluate()
    {
        _anyFeature = false;
        _anyDiscreteFeature = false;
        foreach (Device d in _devices.Values)
        {
            if (!d.IsWheelMouse) { continue; }
            WheelSettings w = d.Info.Wheel;
            _anyFeature |= w.Smooth || w.Reverse || w.BlockWhilePressed;
            _anyDiscreteFeature |= w.Reverse || w.BlockWhilePressed;
        }

        for (int i = 0; i < _hooks.Count; i++)
        {
            HookRecord h = _hooks[i];
            if (!h.Swallowed || Swallows(h.Target)) { continue; }
            Send(h.Delta, h.Horizontal);
            _hooks[i] = h with { Swallowed = false };
        }
        foreach (Device d in _devices.Values)
        {
            if (d.IsGliding && !GlideAllowed(d, d.GlideTarget)) { d.CancelGlide(); }
        }
    }
}

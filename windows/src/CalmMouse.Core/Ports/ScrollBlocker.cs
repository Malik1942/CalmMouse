// Port of Sources/CalmMouseCore/ScrollBlocker.swift.
//
// Names: methods keep Swift's base name and its internal parameter names, so
// `magicMouseButton(_:isDown:at:)` is `MagicMouseButton(button, isDown, t)`,
// `isBlocking(at:)` is `IsBlocking(t)`, `isBlockingWheel(at:settings:)` is
// `IsBlockingWheel(t, settings)`, `debugSnapshot(at:)` is `DebugSnapshot(t)`,
// `forceReleaseAllButtons(at:)` is `ForceReleaseAllButtons(t)`, `handleScroll(_:)` is
// `HandleScroll(e)`, `setActiveApp(bundleID:)` is `SetActiveApp(bundleID)` and
// `ScrollRewrite.isNoop(for:)` is `IsNoop(e)`. `rules`, `config`, `activeBundleID`,
// `baseConfig` and `anyButtonDown` are properties; `rules`' didSet is its setter.
//
// Types: ScrollPhase, MomentumPhase and ScrollAxis have no payloads and stay plain enums
// (`.none` is `None`). ScrollDecision has payloads, so it is an abstract record with one nested
// record per case: `Pass`, `Drop`, `Rewrite(Value)` and `Smooth(Dx, Dy)`. ScrollEvent and
// ScrollRewrite are mutable record structs. Swift has no zero-argument initializer for them, so
// `new ScrollEvent()` (or `default`) is all zeros and false, not the defaults of the real
// initializer; always pass a timestamp. ScrollBlocker is a class, as in Swift.
//
// Numbers: Swift's Int is 64 bits, hence long for button numbers; `Int(x)` in `DebugSnapshot`
// is `checked((long)x)` (Swift traps on NaN or out of range, C# throws). `min`/`max` are
// Swift's (see SwiftMath). `OptionSet.isDisjoint(with:)`, `contains` and `subtracting` are the
// matching bitwise operations.

using System.Globalization;

namespace CalmMouse.Core;

// MARK: - Event model (framework-free so it can be unit-tested)

/// <summary>Mirrors CGScrollPhase.</summary>
public enum ScrollPhase
{
    None, MayBegin, Began, Changed, Ended, Cancelled,
}

/// <summary>Mirrors CGMomentumScrollPhase.</summary>
public enum MomentumPhase
{
    None, Began, Continued, Ended,
}

public record struct ScrollEvent
{
    /// <summary>Seconds.</summary>
    public double Timestamp { get; set; }
    /// <summary>True when the event came from a device we identified as a Magic Mouse.</summary>
    public bool FromMagicMouse { get; set; }
    /// <summary>
    /// True when the event came from an ordinary mouse with a scroll wheel. Never set
    /// together with `FromMagicMouse`.
    /// </summary>
    public bool FromWheelMouse { get; set; }
    public bool IsContinuous { get; set; }
    public ScrollPhase Phase { get; set; }
    public MomentumPhase Momentum { get; set; }
    /// <summary>Horizontal point delta (CGEvent axis 2).</summary>
    public double DeltaX { get; set; }
    /// <summary>Vertical point delta (CGEvent axis 1).</summary>
    public double DeltaY { get; set; }
    public ModifierCombo Modifiers { get; set; }
    /// <summary>The settings of the wheel mouse that sent this tick. null: the config's template.</summary>
    public WheelSettings? Wheel { get; set; }

    public ScrollEvent(double timestamp, bool fromMagicMouse = true, bool isContinuous = true,
                       ScrollPhase phase = ScrollPhase.None, MomentumPhase momentum = MomentumPhase.None,
                       double deltaX = 0, double deltaY = 0, ModifierCombo modifiers = ModifierCombo.None,
                       bool fromWheelMouse = false, WheelSettings? wheel = null)
    {
        Timestamp = timestamp;
        FromMagicMouse = fromMagicMouse && !fromWheelMouse;
        FromWheelMouse = fromWheelMouse;
        IsContinuous = isContinuous;
        Phase = phase;
        Momentum = momentum;
        DeltaX = deltaX;
        DeltaY = deltaY;
        Modifiers = modifiers;
        Wheel = wheel;
    }
}

/// <summary>How the event tap should modify a scroll event before passing it on.</summary>
public record struct ScrollRewrite
{
    public ScrollPhase Phase { get; set; }
    public MomentumPhase Momentum { get; set; }
    public bool ZeroX { get; set; }
    public bool ZeroY { get; set; }
    /// <summary>Move the vertical delta onto the horizontal axis (and vice versa).</summary>
    public bool SwapAxes { get; set; }
    /// <summary>Negate both deltas.</summary>
    public bool Invert { get; set; }
    public ModifierCombo AddModifiers { get; set; }
    public ModifierCombo RemoveModifiers { get; set; }

    public ScrollRewrite(ScrollPhase phase, MomentumPhase momentum, bool zeroX = false, bool zeroY = false,
                         bool swapAxes = false, bool invert = false,
                         ModifierCombo addModifiers = ModifierCombo.None,
                         ModifierCombo removeModifiers = ModifierCombo.None)
    {
        Phase = phase;
        Momentum = momentum;
        ZeroX = zeroX;
        ZeroY = zeroY;
        SwapAxes = swapAxes;
        Invert = invert;
        AddModifiers = addModifiers;
        RemoveModifiers = removeModifiers;
    }

    /// <summary>True when applying this rewrite would leave the event exactly as it arrived.</summary>
    public readonly bool IsNoop(ScrollEvent e) =>
        Phase == e.Phase && Momentum == e.Momentum && !SwapAxes && !Invert
        && AddModifiers == ModifierCombo.None && RemoveModifiers == ModifierCombo.None
        && (!ZeroX || e.DeltaX == 0) && (!ZeroY || e.DeltaY == 0);
}

public abstract record ScrollDecision
{
    private ScrollDecision() { }

    public sealed record Pass : ScrollDecision;

    public sealed record Drop : ScrollDecision;

    public sealed record Rewrite(ScrollRewrite Value) : ScrollDecision;

    /// <summary>Swallow the event and glide this many pixels instead (wheel mice only).</summary>
    public sealed record Smooth(double Dx, double Dy) : ScrollDecision;
}

public enum ScrollAxis
{
    Horizontal, Vertical,
}

// MARK: - State machine

/// <summary>
/// Pure decision engine. Feed it button + scroll events in wall-clock order, get decisions back.
/// Not thread-safe by design: the event tap delivers everything on one run loop.
/// </summary>
public sealed class ScrollBlocker
{
    private RuleSet _rules;

    /// <summary>Base settings + per-app overrides.</summary>
    public RuleSet Rules
    {
        get => _rules;
        set
        {
            _rules = value;
            RecomputeConfig();   // Swift: didSet
        }
    }
    /// <summary>Bundle ID of the frontmost app (on Windows, its exe name); selects which per-app rule applies.</summary>
    public string? ActiveBundleID { get; private set; }
    /// <summary>The config actually in force right now (base merged with the active app's rule).</summary>
    public BlockerConfig Config { get; private set; }

    // Button state (Magic Mouse buttons only).
    private readonly HashSet<long> _buttonsDown = [];
    private double _blockUntil = 0;

    // Wheel mouse state, kept apart from the Magic Mouse's so neither blocks the other.
    /// <summary>The wheel's own click: CGEvent button 2 (centre).</summary>
    public const long WheelButton = 2;
    /// <summary>The wheel often turns one more notch as the finger lets go of it. Seconds.</summary>
    public const double WheelReleaseGrace = 0.15;
    /// <summary>
    /// Shortest distance one notch glides, in pixels, before the speed setting scales it.
    /// macOS itself moves a slow single notch by one pixel and a fast spin by fifty or more;
    /// the floor makes a slow notch worth turning, and the fast ones keep their acceleration.
    /// </summary>
    public const double WheelMinimumStep = 35;
    /// <summary>
    /// Held while scrolling, these mean zoom (or an app's own shortcut), which counts notches.
    /// Gliding would turn one notch into dozens of zoom steps, so those ticks are left as they are.
    /// </summary>
    public const ModifierCombo WheelSmoothingBypass = ModifierCombo.Command | ModifierCombo.Control | ModifierCombo.Option;
    private bool _wheelPressed = false;
    private double _wheelBlockUntil = 0;

    // Gesture bookkeeping.
    private enum Visible { MayBegin, Began }
    /// <summary>What the apps have been allowed to see for the current gesture.</summary>
    private Visible? _visible;
    /// <summary>
    /// True between a gesture's start and its end, whether or not apps can see it.
    /// (`_visible == null` is not the same thing: a suppressed gesture is active but invisible.)
    /// </summary>
    private bool _gestureActive = false;
    /// <summary>We swallowed this gesture's start; keep swallowing until it ends.</summary>
    private bool _suppressingGesture = false;
    /// <summary>Momentum belongs to a suppressed gesture (or a click landed during it) — swallow to the end.</summary>
    private bool _suppressingMomentum = false;
    /// <summary>Apps saw a momentum "began" and are entitled to a matching "ended".</summary>
    private bool _momentumVisible = false;

    // Dead zone.
    private double _deadZoneTravel = 0;
    private bool _deadZonePassed = true;

    // Axis lock.
    private double _cumX = 0;
    private double _cumY = 0;
    private ScrollAxis? _lockedAxis;

    /// <summary>Swift: `init(rules: RuleSet = RuleSet())`.</summary>
    public ScrollBlocker() : this(new RuleSet()) { }

    public ScrollBlocker(RuleSet rules)
    {
        _rules = rules;
        Config = rules.Base;
    }

    public ScrollBlocker(BlockerConfig config) : this(new RuleSet(@base: config)) { }

    /// <summary>Convenience for callers that only tweak the base config.</summary>
    public BlockerConfig BaseConfig
    {
        get => Rules.Base;
        set
        {
            RuleSet rules = Rules;
            rules.Base = value;
            Rules = rules;
        }
    }

    public void SetActiveApp(string? bundleID)
    {
        if (string.Equals(bundleID, ActiveBundleID, StringComparison.Ordinal)) { return; }
        ActiveBundleID = bundleID;
        RecomputeConfig();
    }

    private void RecomputeConfig()
    {
        Config = Rules.Config(ActiveBundleID);
    }

    // MARK: Inputs

    /// <summary>Report a Magic Mouse button transition. Non-Magic-Mouse buttons must not be reported.</summary>
    public void MagicMouseButton(long button, bool isDown, double t)
    {
        if (isDown)
        {
            _buttonsDown.Add(button);
        }
        else
        {
            _buttonsDown.Remove(button);
            if (_buttonsDown.Count == 0)
            {
                _blockUntil = t + Config.ReleaseGrace;
            }
        }
    }

    /// <summary>
    /// Report a button transition on a wheel mouse. Only the wheel's own click counts:
    /// pressing the wheel is what turns it by accident. Holding the left button and turning
    /// the wheel is deliberate (scrolling while dragging a file, extending a selection), so
    /// the other buttons never block anything.
    /// </summary>
    public void WheelMouseButton(long button, bool isDown, double t)
    {
        if (button != WheelButton) { return; }
        if (isDown)
        {
            _wheelPressed = true;
        }
        else if (_wheelPressed)
        {
            _wheelPressed = false;
            _wheelBlockUntil = t + WheelReleaseGrace;
        }
    }

    /// <summary>
    /// A tap-and-drag was cancelled because the finger started scroll-swiping: drop every trace
    /// of blocking RIGHT NOW — no release grace, and the scroll gesture already in flight gets
    /// unsuppressed so the user's swipe scrolls from its very next event.
    /// </summary>
    public void LiftBlockingImmediately()
    {
        _buttonsDown.Clear();
        _blockUntil = 0;
        _suppressingGesture = false;
        _suppressingMomentum = false;
    }

    /// <summary>Safety valve: the system reports no buttons pressed but we think some are (missed mouse-up).</summary>
    public void ForceReleaseAllButtons(double t)
    {
        if (_buttonsDown.Count != 0)
        {
            _buttonsDown.Clear();
            _blockUntil = t + Config.ReleaseGrace;
        }
        if (_wheelPressed)
        {
            _wheelPressed = false;
            _wheelBlockUntil = t + WheelReleaseGrace;
        }
    }

    public bool AnyButtonDown => _buttonsDown.Count != 0 || _wheelPressed;

    /// <summary>
    /// Live diagnostics for the status file — when scrolling "mysteriously" dies, this names
    /// the component holding it: stuck buttons, an unexpired grace, or gesture suppression.
    /// Formatted as Swift's string interpolation does: Bools as true/false, Ints as plain digits.
    /// </summary>
    public string DebugSnapshot(double t) => string.Create(CultureInfo.InvariantCulture,
        $"buttons={_buttonsDown.Count} graceRemainingMs={checked((long)(SwiftMath.Max(0, _blockUntil - t) * 1000))} " +
        $"suppressingGesture={SwiftBool(_suppressingGesture)} suppressingMomentum={SwiftBool(_suppressingMomentum)} " +
        $"blocking={SwiftBool(IsBlocking(t))} " +
        $"wheelPressed={SwiftBool(_wheelPressed)} wheelGraceRemainingMs={checked((long)(SwiftMath.Max(0, _wheelBlockUntil - t) * 1000))} " +
        $"wheelBlocking={SwiftBool(IsBlockingWheel(t))}");

    /// <summary>`\(bool)` in Swift: lower case.</summary>
    private static string SwiftBool(bool value) => value ? "true" : "false";

    public bool IsBlocking(double t)
    {
        if (!Config.BlockScrollWhileClicked) { return false; }
        return _buttonsDown.Count != 0 || t < _blockUntil;
    }

    /// <summary>
    /// The wheel mouse's equivalent of `IsBlocking`: the wheel is pressed, or was a moment ago.
    /// Without settings, the template's choice decides (that is what `--status` shows).
    /// </summary>
    public bool IsBlockingWheel(double t, WheelSettings? settings = null)
    {
        if (!(settings ?? Config.WheelTemplate).BlockWhilePressed) { return false; }
        return _wheelPressed || t < _wheelBlockUntil;
    }

    // MARK: Scroll decisions

    public ScrollDecision HandleScroll(ScrollEvent e)
    {
        if (e.FromWheelMouse) { return HandleWheel(e); }
        if (!e.FromMagicMouse) { return new ScrollDecision.Pass(); }
        ScrollDecision decision = MagicMouseDecision(e);
        return Config.ReverseMagicMouseScroll ? Reversed(decision, e) : decision;
    }

    private ScrollDecision MagicMouseDecision(ScrollEvent e)
    {
        if (Config.DisableScrollEntirely) { return new ScrollDecision.Drop(); }
        if (e.Momentum != MomentumPhase.None)
        {
            return HandleMomentum(e);
        }
        return HandleGesture(e);
    }

    /// <summary>
    /// The same decision with the direction flipped. What was going to be dropped stays
    /// dropped, and a rewrite that already inverts (a held invert key) ends up normal.
    /// </summary>
    private static ScrollDecision Reversed(ScrollDecision decision, ScrollEvent e)
    {
        switch (decision)
        {
            case ScrollDecision.Pass:
                return new ScrollDecision.Rewrite(new ScrollRewrite(phase: e.Phase, momentum: e.Momentum, invert: true));
            case ScrollDecision.Rewrite(ScrollRewrite r):
                r.Invert = !r.Invert;
                return new ScrollDecision.Rewrite(r);
            case ScrollDecision.Drop or ScrollDecision.Smooth:
                return decision;
            default:
                throw new ArgumentOutOfRangeException(nameof(decision));
        }
    }

    /// <summary>
    /// A wheel tick. Ticks have no phases and no momentum, so none of the gesture state is
    /// read or written here: a wheel turning in the middle of a Magic Mouse swipe must not
    /// disturb it.
    ///
    /// A wheel mouse answers only to its own two settings. Everything else in the config
    /// (dead zone, axis lock, momentum, sideways block, modifier actions, per-app rules) was
    /// set up for a touch surface and stays with the Magic Mouse, so turning wheel support on
    /// never changes how an existing setup behaves.
    /// </summary>
    private ScrollDecision HandleWheel(ScrollEvent e)
    {
        WheelSettings w = e.Wheel ?? Config.WheelTemplate;
        if (IsBlockingWheel(e.Timestamp, settings: w)) { return new ScrollDecision.Drop(); }

        ScrollDecision discrete = w.Reverse
            ? new ScrollDecision.Rewrite(new ScrollRewrite(phase: e.Phase, momentum: e.Momentum, invert: true))
            : new ScrollDecision.Pass();
        if (!(w.Smooth && (e.Modifiers & WheelSmoothingBypass) == 0))
        {
            return discrete;
        }

        double direction = w.Reverse ? -1 : 1;
        double dx = WheelDistance(e.DeltaX, speed: w.Speed) * direction;
        double dy = WheelDistance(e.DeltaY, speed: w.Speed) * direction;
        // Shift turns the wheel sideways. macOS does that itself for a wheel's own events but
        // not for the gliding ones that replace them, so it is done here.
        if ((e.Modifiers & ModifierCombo.Shift) == ModifierCombo.Shift && dx == 0)
        {
            dx = dy;
            dy = 0;
        }
        if (!(dx != 0 || dy != 0)) { return discrete; }
        return new ScrollDecision.Smooth(Dx: dx, Dy: dy);
    }

    /// <summary>
    /// A ⌘, ⌃ or ⌥ tick from the wheel is a zoom notch and is never smoothed. Arriving while a
    /// glide is still playing out, it means the user has moved on, so the glide stops.
    /// </summary>
    public static bool InterruptsGlide(ScrollEvent e) =>
        e.FromWheelMouse && (e.Modifiers & WheelSmoothingBypass) != 0;

    /// <summary>How far one tick glides along an axis, keeping its sign.</summary>
    private static double WheelDistance(double delta, double speed)
    {
        if (delta == 0) { return 0; }   // guard delta != 0 (the same for NaN)
        double distance = SwiftMath.Max(Math.Abs(delta), WheelMinimumStep) * speed;
        return delta < 0 ? -distance : distance;
    }

    private ScrollDecision HandleGesture(ScrollEvent e)
    {
        bool blocking = IsBlocking(e.Timestamp);

        switch (e.Phase)
        {
            case ScrollPhase.MayBegin:
                BeginNewGesture();
                if (blocking)
                {
                    _suppressingGesture = true;
                    return new ScrollDecision.Drop();
                }
                _visible = Visible.MayBegin;
                return new ScrollDecision.Pass();

            case ScrollPhase.Began:
                // `began` unambiguously starts a gesture. Unless we passed a `mayBegin` for this very
                // gesture, reset — this also recovers if a previous gesture's `ended` got lost.
                if (_visible != Visible.MayBegin) { BeginNewGesture(); }
                if (blocking)
                {
                    return SuppressVisibleGesture(e);
                }
                return PassGesture(e);

            case ScrollPhase.Changed:
                if (_suppressingGesture)
                {
                    if (blocking) { return new ScrollDecision.Drop(); }
                    // Blocking ended mid-gesture (button released, grace expired). The rest of the
                    // gesture is the user's deliberate scrolling — swallowing it whole meant "tap,
                    // then scroll" stayed dead for the entire swipe. Resume: apps join mid-gesture,
                    // with the dead zone waived since the finger has already travelled.
                    _suppressingGesture = false;
                    _gestureActive = false;
                    return PassGesture(e);
                }
                if (blocking)
                {
                    return SuppressVisibleGesture(e);
                }
                return PassGesture(e);

            case ScrollPhase.Ended or ScrollPhase.Cancelled:
                _gestureActive = false;
                if (_suppressingGesture)
                {
                    _suppressingGesture = false;
                    _suppressingMomentum = true; // any momentum tail belongs to the swallowed gesture
                    return new ScrollDecision.Drop();
                }
                if (!_deadZonePassed)
                {
                    // The whole gesture stayed inside the dead zone; apps never saw it start.
                    _deadZonePassed = true;
                    _suppressingMomentum = true;
                    _visible = null;
                    return new ScrollDecision.Drop();
                }
                _visible = null;
                return new ScrollDecision.Pass();

            case ScrollPhase.None:
                // Phase-less scroll (discrete wheel ticks or synthetic). Nothing to keep consistent.
                if (blocking) { return new ScrollDecision.Drop(); }
                return Finish(e, phase: e.Phase);

            default:
                throw new ArgumentOutOfRangeException(nameof(e));
        }
    }

    /// <summary>Common path for `began`/`changed` when nothing is blocking: dead zone, then transforms.</summary>
    private ScrollDecision PassGesture(ScrollEvent e)
    {
        if (!_gestureActive)
        {
            // Joining a gesture that started before we were watching (no `began` seen).
            // We can't know how far it has already travelled, so the dead zone doesn't apply.
            BeginNewGesture();
            _deadZonePassed = true;
        }

        if (!_deadZonePassed)
        {
            _deadZoneTravel += Math.Abs(e.DeltaX) + Math.Abs(e.DeltaY);
            if (_deadZoneTravel < Config.DeadZone) { return new ScrollDecision.Drop(); }
            _deadZonePassed = true;
            // Apps never saw the real `began`; relabel this event so the gesture is well-formed.
            _visible = Visible.Began;
            return Finish(e, phase: ScrollPhase.Began);
        }

        _visible = Visible.Began;
        return Finish(e, phase: e.Phase);
    }

    private ScrollDecision HandleMomentum(ScrollEvent e)
    {
        if (!Config.MomentumEnabled) { return new ScrollDecision.Drop(); }

        if (_suppressingMomentum)
        {
            if (e.Momentum == MomentumPhase.Ended) { _suppressingMomentum = false; }
            return new ScrollDecision.Drop();
        }

        if (IsBlocking(e.Timestamp))
        {
            // A click landed while the page was coasting: stop the coast.
            if (e.Momentum == MomentumPhase.Began)
            {
                _suppressingMomentum = true;
                return new ScrollDecision.Drop();
            }
            if (e.Momentum != MomentumPhase.Ended) { _suppressingMomentum = true; }
            if (_momentumVisible)
            {
                // Apps saw a "began" — hand them a clean "ended" with zero deltas.
                _momentumVisible = false;
                return new ScrollDecision.Rewrite(new ScrollRewrite(phase: ScrollPhase.None, momentum: MomentumPhase.Ended, zeroX: true, zeroY: true));
            }
            return new ScrollDecision.Drop();
        }

        if (e.Momentum == MomentumPhase.Began) { _momentumVisible = true; }
        if (e.Momentum == MomentumPhase.Ended) { _momentumVisible = false; }
        return Finish(e, phase: e.Phase);
    }

    // MARK: Helpers

    private void BeginNewGesture()
    {
        _gestureActive = true;
        _visible = null;
        _suppressingGesture = false;
        _suppressingMomentum = false; // a new gesture can only start after the previous momentum is over
        _momentumVisible = false;
        _deadZoneTravel = 0;
        _deadZonePassed = Config.DeadZone <= 0;
        _cumX = 0;
        _cumY = 0;
        _lockedAxis = null;
    }

    /// <summary>
    /// A click arrived mid-gesture. If apps have seen the gesture, hand them a clean ending
    /// (zero deltas) instead of leaving them hanging; then swallow the rest.
    /// </summary>
    private ScrollDecision SuppressVisibleGesture(ScrollEvent e)
    {
        _suppressingGesture = true;
        switch (_visible)
        {
            case Visible.MayBegin:
                _visible = null;
                return new ScrollDecision.Rewrite(new ScrollRewrite(phase: ScrollPhase.Cancelled, momentum: MomentumPhase.None, zeroX: true, zeroY: true));
            case Visible.Began:
                _visible = null;
                return new ScrollDecision.Rewrite(new ScrollRewrite(phase: ScrollPhase.Ended, momentum: MomentumPhase.None, zeroX: true, zeroY: true));
            case null:
                return new ScrollDecision.Drop();
            default:
                throw new ArgumentOutOfRangeException(nameof(e));
        }
    }

    /// <summary>Apply the transforms that act on a passing event: modifier action, axis lock, horizontal block.</summary>
    private ScrollDecision Finish(ScrollEvent e, ScrollPhase phase)
    {
        var r = new ScrollRewrite(phase: phase, momentum: e.Momentum);

        switch (Config.Action(e.Modifiers))
        {
            case ScrollAction.Normal:
                break;
            case ScrollAction.Block:
                return new ScrollDecision.Drop();
            case ScrollAction.Horizontal:
                r.SwapAxes = true;
                r.RemoveModifiers = e.Modifiers;
                break;
            case ScrollAction.Invert:
                r.Invert = true;
                r.RemoveModifiers = e.Modifiers;
                break;
            case ScrollAction.ZoomCommand:
                r.RemoveModifiers = e.Modifiers & ~ModifierCombo.Command;
                r.AddModifiers = ModifierCombo.Command;
                break;
            case ScrollAction.ZoomControl:
                r.RemoveModifiers = e.Modifiers & ~ModifierCombo.Control;
                r.AddModifiers = ModifierCombo.Control;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(e));
        }

        // Axis lock: once a gesture commits to an axis, zero the other one.
        if (Config.AxisLock)
        {
            if (_lockedAxis == null)
            {
                _cumX += Math.Abs(e.DeltaX);
                _cumY += Math.Abs(e.DeltaY);
                double dominant = SwiftMath.Max(_cumX, _cumY);
                double other = SwiftMath.Min(_cumX, _cumY);
                if (dominant >= Config.AxisLockThreshold && dominant >= other * Config.AxisLockRatio)
                {
                    _lockedAxis = _cumY >= _cumX ? ScrollAxis.Vertical : ScrollAxis.Horizontal;
                }
            }
            switch (_lockedAxis)
            {
                case ScrollAxis.Vertical: r.ZeroX = true; break;
                case ScrollAxis.Horizontal: r.ZeroY = true; break;
                case null: break;
            }
        }

        if (Config.BlockHorizontalScroll)
        {
            // After a swap the "horizontal" content lives on the axis the swap moved it to.
            if (r.SwapAxes) { r.ZeroY = true; } else { r.ZeroX = true; }
        }

        return r.IsNoop(e) ? new ScrollDecision.Pass() : new ScrollDecision.Rewrite(r);
    }
}

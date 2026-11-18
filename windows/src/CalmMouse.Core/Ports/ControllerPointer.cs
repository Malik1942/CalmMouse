// Port of Sources/CalmMouseCore/ControllerPointer.swift.
//
// Names: methods keep Swift's base name and its internal parameter names, so
// `setActive(_:)` is `SetActive(active)`, `isHeld(_:)` is `IsHeld(button)`,
// `button(_:pressed:at:)` is `Button(button, pressed, t)`,
// `touchpad(primary:secondary:at:)` is `Touchpad(primary, secondary, t)`,
// `sticks(left:right:)` is `Sticks(left, right)`, `tick(dt:at:)` is `Tick(dt, t)`,
// `ControllerConfig.factor(_:)` is `Factor(speed)`, `touchAcceleration(speed:)` is
// `TouchAcceleration(speed)`, `deflection(_:)` is `Deflection(p)` and
// `ControllerApps.isGameCategory(_:)` is `IsGameCategory(category)`. `config`, `isActive` and
// `needsTicks` are properties. Swift's internal members (`factor`, `touchAcceleration`,
// `deflection`) stay internal; the tests see them through InternalsVisibleTo.
//
// Types: PointerButton and ControllerButton have no payloads and stay plain enums. PointerAction
// has payloads, so it is an abstract record with one nested record per case: `Move(Dx, Dy)`,
// `Scroll(Vertical, Horizontal)`, `Press(Button)`, `Release(Button)`, `Click(Button)` and
// `Toggle`. ControllerConfig and TouchPoint are mutable record structs (both Equatable in Swift).
// TouchPoint has no zero-argument initializer in Swift, so `new TouchPoint()` is (0, 0).
// ControllerApps is a static class (a case-less Swift enum).
//
// ControllerPointer is a plain mutable struct, not a record struct: Swift's is not Equatable, so
// C# gets no equality either. Its sets and dictionaries are System.Collections.Immutable ones, so
// copying the struct copies its state as Swift's value semantics do (a copy never shares a
// collection that the other copy then mutates). Use `new ControllerPointer()` or
// `new ControllerPointer(config)`; `default(ControllerPointer)` has null collections and an
// all-zero config and must not be used. Callers keep it in a variable, as with a Swift `var`.
//
// Order: nothing here depends on a Set or Dictionary's iteration order. `setActive(false)` walks
// the fixed list [left, right]; the chord compares sets for equality.
//
// Numbers: `min`/`max` are Swift's (see SwiftMath); `pow` is Math.Pow, `squareRoot()` Math.Sqrt.
// Swift `guard x > y` is written `if (!(x > y))` so a NaN takes the same branch.

using System.Collections.Immutable;

namespace CalmMouse.Core;

/// <summary>
/// How a game controller drives the pointer. Speeds use the same -1...1 scale as the mice's
/// Tracking speed slider, 0 being normal.
/// </summary>
public record struct ControllerConfig
{
    public double PointerSpeed { get; set; } = 0;
    public double ScrollSpeed { get; set; } = 0;
    /// <summary>A light touch on the touchpad clicks, as on a laptop's trackpad.</summary>
    public bool TapToClick { get; set; } = true;
    /// <summary>The system's "Natural scrolling": two fingers move the page the way they slide.</summary>
    public bool NaturalScrolling { get; set; } = true;
    /// <summary>
    /// Pressing the left stick clicks and pressing the right stick right-clicks: the thumb that
    /// points also clicks, as a finger does on a mouse.
    /// </summary>
    public bool StickClicks { get; set; } = true;

    // Feel. Starting values, tuned on a DualSense.

    /// <summary>
    /// The touchpad is about 1920 × 1080 in its own units, but both axes report -1...1. Vertical
    /// travel is scaled by this so the same finger distance goes the same way in both.
    /// </summary>
    public double TouchpadAspect { get; set; } = 1080.0 / 1920.0;
    /// <summary>Points per touchpad unit (half the pad's width) at normal speed, before acceleration.</summary>
    public double TouchpadGain { get; set; } = 420;
    /// <summary>Pixels of scrolling per touchpad unit with two fingers.</summary>
    public double TouchpadScrollGain { get; set; } = 520;
    /// <summary>A touch at most this long, and moving at most `TapMaxTravel`, is a tap. Seconds.</summary>
    public double TapMaxDuration { get; set; } = 0.2;
    /// <summary>In touchpad units: 0.06 is 3% of the pad's width.</summary>
    public double TapMaxTravel { get; set; } = 0.06;
    /// <summary>Stick travel that does nothing, so a stick resting slightly off centre doesn't drift.</summary>
    public double StickDeadZone { get; set; } = 0.15;
    /// <summary>Points per second with the left stick pushed all the way, at normal speed.</summary>
    public double StickMaxSpeed { get; set; } = 1600;
    /// <summary>Pixels per second with the right stick pushed all the way, at normal speed.</summary>
    public double StickMaxScroll { get; set; } = 1500;
    /// <summary>
    /// Hold Options and Create (Xbox: Menu and View) together this long to turn the controller
    /// pointer on or off. Seconds.
    /// </summary>
    public double ToggleHold { get; set; } = 0.5;
    /// <summary>
    /// Pressing a stick down nudges it sideways, and so does letting go. For this long after
    /// either, that stick's movement is ignored so the pointer stays on what is being clicked.
    /// Holding the stick down longer and moving it still drags. Seconds.
    /// </summary>
    public double StickClickSettle { get; set; } = 0.15;

    /// <summary>The defaults. (Without it `new ControllerConfig()` would be all zeros and false.)</summary>
    public ControllerConfig() { }

    /// <summary>Each end of the -1...1 slider is this many times slower or faster.</summary>
    public const double SpeedRange = 2.5;

    internal static double Factor(double speed) =>
        Math.Pow(SpeedRange, SwiftMath.Min(SwiftMath.Max(speed, -1), 1));
}

public enum PointerButton
{
    Left, Right,
}

/// <summary>The controller's buttons CalmMouse uses, by position, so ✕ and A are the same button.</summary>
public enum ControllerButton
{
    /// <summary>✕ on PlayStation, A on Xbox: the bottom face button.</summary>
    South,
    /// <summary>○ on PlayStation, B on Xbox: the right face button.</summary>
    East,
    /// <summary>Pressing the touchpad down.</summary>
    Touchpad,
    /// <summary>Options on a DualSense, ≡ Menu on Xbox.</summary>
    Menu,
    /// <summary>Create on a DualSense, ⧉ View on Xbox.</summary>
    Options,
    /// <summary>Pressing the left stick down (L3).</summary>
    LeftStick,
    /// <summary>Pressing the right stick down (R3).</summary>
    RightStick,
}

/// <summary>
/// What the controller asks of the pointer. Movement is in screen points with y growing
/// downward. Scrolling uses the scroll-wheel convention: positive `Vertical` shows what is
/// above, positive `Horizontal` what is to the left.
/// </summary>
public abstract record PointerAction
{
    private PointerAction() { }

    public sealed record Move(double Dx, double Dy) : PointerAction;

    public sealed record Scroll(double Vertical, double Horizontal) : PointerAction;

    public sealed record Press(PointerButton Button) : PointerAction;

    public sealed record Release(PointerButton Button) : PointerAction;

    /// <summary>A press and release in one go (a tap on the touchpad).</summary>
    public sealed record Click(PointerButton Button) : PointerAction;

    /// <summary>Options + Create held: the user wants the controller pointer switched on or off.</summary>
    public sealed record Toggle : PointerAction;
}

/// <summary>
/// A finger on the touchpad, in the controller's units: x -1 (left) … 1 (right), y -1 (bottom)
/// … 1 (top).
/// </summary>
public record struct TouchPoint
{
    public double X { get; set; }
    public double Y { get; set; }

    public TouchPoint(double x, double y)
    {
        X = x;
        Y = y;
    }
}

/// <summary>
/// Turns a game controller's touchpad, sticks and buttons into pointer actions. Pure state and
/// arithmetic: the app layer reads the controller, supplies the clock, and posts the events.
///
/// While inactive it only watches for the on/off chord, so the controller can switch itself on.
/// </summary>
public struct ControllerPointer
{
    public ControllerConfig Config { get; set; }
    public bool IsActive { get; private set; } = false;

    /// <summary>Swift: `init(config: ControllerConfig = ControllerConfig())`.</summary>
    public ControllerPointer() : this(new ControllerConfig()) { }

    public ControllerPointer(ControllerConfig config)
    {
        Config = config;
    }

    // MARK: Buttons held, by what holds them

    /// <summary>
    /// Which inputs hold each button down. The button goes down with the first and up with the
    /// last, so ✕ and a pressed touchpad can overlap without a stray release.
    /// </summary>
    private ImmutableDictionary<PointerButton, ImmutableHashSet<ControllerButton>> _holders =
        ImmutableDictionary<PointerButton, ImmutableHashSet<ControllerButton>>.Empty;
    /// <summary>
    /// Which button each held input pressed, so its release lifts that one even if the finger
    /// count changed in between.
    /// </summary>
    private ImmutableDictionary<ControllerButton, PointerButton> _pressedBy =
        ImmutableDictionary<ControllerButton, PointerButton>.Empty;

    public readonly bool IsHeld(PointerButton button) =>
        !(_holders.TryGetValue(button, out ImmutableHashSet<ControllerButton>? set) ? set.IsEmpty : true);

    // MARK: Touchpad

    private long _fingers = 0;
    private TouchPoint? _lastPoint = null;
    private double _touchStartedAt = 0;
    private double _touchTravel = 0;
    private long _touchMostFingers = 0;
    /// <summary>The pad was pressed down during this touch: lifting it is not also a tap.</summary>
    private bool _touchPressed = false;
    private double? _lastTouchAt = null;
    private double _touchSpeed = 0;

    // MARK: Sticks

    private TouchPoint _leftStick = new(x: 0, y: 0);
    private TouchPoint _rightStick = new(x: 0, y: 0);
    /// <summary>Until when each stick's movement is ignored, after it was pressed or let go.</summary>
    private double _leftStickQuietUntil = double.NegativeInfinity;
    private double _rightStickQuietUntil = double.NegativeInfinity;

    // MARK: Chord

    private ImmutableHashSet<ControllerButton> _chordDown = ImmutableHashSet<ControllerButton>.Empty;
    private double? _chordStartedAt = null;
    private bool _chordFired = false;

    /// <summary>The clock must keep ticking: a stick is pushed or the chord is being held.</summary>
    public readonly bool NeedsTicks =>
        (IsActive && (Deflection(_leftStick) > 0 || Deflection(_rightStick) > 0))
        || (_chordStartedAt != null && !_chordFired);

    // MARK: On and off

    /// <summary>Switching off lets go of anything still held, so no button stays down.</summary>
    public List<PointerAction> SetActive(bool active)
    {
        if (active == IsActive) { return []; }
        IsActive = active;
        var actions = new List<PointerAction>();
        if (!active)
        {
            foreach (PointerButton button in new[] { PointerButton.Left, PointerButton.Right })
            {
                if (IsHeld(button)) { actions.Add(new PointerAction.Release(button)); }
            }
            _holders = ImmutableDictionary<PointerButton, ImmutableHashSet<ControllerButton>>.Empty;
            _pressedBy = ImmutableDictionary<ControllerButton, PointerButton>.Empty;
            _fingers = 0;
            _lastPoint = null;
            _leftStick = new TouchPoint(x: 0, y: 0);
            _rightStick = new TouchPoint(x: 0, y: 0);
        }
        return actions;
    }

    // MARK: Input

    public List<PointerAction> Button(ControllerButton button, bool pressed, double t)
    {
        if (button == ControllerButton.Menu || button == ControllerButton.Options)
        {
            UpdateChord(button, pressed, t);
            return [];
        }
        if (!IsActive) { return []; }
        if (button == ControllerButton.LeftStick || button == ControllerButton.RightStick)
        {
            // Released, the press it made must still be lifted even if the option went off.
            if (!(Config.StickClicks || _pressedBy.ContainsKey(button))) { return []; }
            if (button == ControllerButton.LeftStick) { _leftStickQuietUntil = t + Config.StickClickSettle; }
            else { _rightStickQuietUntil = t + Config.StickClickSettle; }
        }
        if (pressed)
        {
            if (_pressedBy.ContainsKey(button)) { return []; }
            PointerButton target;
            switch (button)
            {
                case ControllerButton.South: target = PointerButton.Left; break;
                case ControllerButton.East:
                case ControllerButton.RightStick: target = PointerButton.Right; break;
                case ControllerButton.LeftStick: target = PointerButton.Left; break;
                case ControllerButton.Touchpad:
                    // Two fingers on the pad when it goes down: a right click, as on a trackpad.
                    target = _fingers >= 2 ? PointerButton.Right : PointerButton.Left;
                    _touchPressed = true;
                    break;
                case ControllerButton.Menu:
                case ControllerButton.Options:
                default:
                    return [];
            }
            _pressedBy = _pressedBy.SetItem(button, target);
            bool wasHeld = IsHeld(target);
            ImmutableHashSet<ControllerButton> holders =
                _holders.TryGetValue(target, out ImmutableHashSet<ControllerButton>? existing)
                    ? existing
                    : ImmutableHashSet<ControllerButton>.Empty;
            _holders = _holders.SetItem(target, holders.Add(button));
            return wasHeld ? [] : [new PointerAction.Press(target)];
        }
        else
        {
            if (!_pressedBy.TryGetValue(button, out PointerButton target)) { return []; }
            _pressedBy = _pressedBy.Remove(button);
            if (_holders.TryGetValue(target, out ImmutableHashSet<ControllerButton>? holders))
            {
                _holders = _holders.SetItem(target, holders.Remove(button));
            }
            return IsHeld(target) ? [] : [new PointerAction.Release(target)];
        }
    }

    /// <summary>One touchpad report. null means no finger in that slot.</summary>
    public List<PointerAction> Touchpad(TouchPoint? primary, TouchPoint? secondary, double t)
    {
        if (!IsActive) { return []; }
        long count = (primary == null ? 0 : 1) + (secondary == null ? 0 : 1);
        // The finger that moves the pointer: the first one, or the second if only it is left.
        TouchPoint? point = primary ?? secondary;
        var actions = new List<PointerAction>();
        try
        {
            if (_fingers == 0)
            {
                if (!(count > 0) || point is not TouchPoint start) { return []; }
                // A new touch. Its first report sets where it starts, and moves nothing.
                _touchStartedAt = t;
                _touchTravel = 0;
                _touchMostFingers = count;
                _touchPressed = IsHeld(PointerButton.Left)
                    && _holders.TryGetValue(PointerButton.Left, out ImmutableHashSet<ControllerButton>? leftHolders)
                    && leftHolders.Contains(ControllerButton.Touchpad);
                _touchSpeed = 0;
                _lastPoint = start;
                return [];
            }

            if (count == 0)
            {
                // Lifted. A short, still touch is a tap.
                if (Config.TapToClick && !_touchPressed
                    && t - _touchStartedAt <= Config.TapMaxDuration && _touchTravel <= Config.TapMaxTravel)
                {
                    actions.Add(new PointerAction.Click(_touchMostFingers >= 2 ? PointerButton.Right : PointerButton.Left));
                }
                _lastPoint = null;
                return actions;
            }

            _touchMostFingers = Math.Max(_touchMostFingers, count);
            if (point is not TouchPoint current || _lastPoint is not TouchPoint last || count != _fingers)
            {
                // A finger joined or left: start measuring again from here, so the pointer
                // doesn't jump between the two fingers' positions.
                _lastPoint = point;
                return [];
            }
            _lastPoint = current;

            double dx = current.X - last.X;
            double dy = (current.Y - last.Y) * Config.TouchpadAspect;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            _touchTravel += distance;
            if (!(distance > 0)) { return []; }

            if (count >= 2)
            {
                double scrollGain = Config.TouchpadScrollGain * ControllerConfig.Factor(Config.ScrollSpeed);
                // Natural: the page follows the fingers, so fingers going up show what is below.
                double sign = Config.NaturalScrolling ? 1 : -1;
                actions.Add(new PointerAction.Scroll(Vertical: -dy * scrollGain * sign, Horizontal: dx * scrollGain * sign));
                return actions;
            }

            // Faster swipes go further per millimetre, as on a trackpad. The speed is smoothed
            // because reports can arrive in quick bursts.
            double dt = SwiftMath.Max(t - (_lastTouchAt ?? t), 0.004);
            _touchSpeed = _touchSpeed == 0 ? distance / dt : 0.7 * _touchSpeed + 0.3 * distance / dt;
            double gain = Config.TouchpadGain * ControllerConfig.Factor(Config.PointerSpeed)
                * TouchAcceleration(_touchSpeed);
            actions.Add(new PointerAction.Move(Dx: dx * gain, Dy: -dy * gain));
            return actions;
        }
        finally
        {
            // Swift: defer
            _fingers = count;
            _lastTouchAt = count > 0 ? t : null;
        }
    }

    /// <summary>
    /// Slow, careful strokes move less than their distance; fast flicks more. `speed` is in
    /// touchpad units per second.
    /// </summary>
    internal static double TouchAcceleration(double speed) =>
        SwiftMath.Min(SwiftMath.Max(0.45 + 0.35 * speed, 0.45), 2.4);

    /// <summary>Stick positions, -1...1 on each axis with up positive.</summary>
    public void Sticks(TouchPoint left, TouchPoint right)
    {
        if (!IsActive) { return; }
        _leftStick = left;
        _rightStick = right;
    }

    /// <summary>Advances the stick-driven motion and the chord's clock by one frame.</summary>
    public List<PointerAction> Tick(double dt, double t)
    {
        var actions = new List<PointerAction>();
        if (_chordStartedAt is double started && !_chordFired && t - started >= Config.ToggleHold)
        {
            _chordFired = true;
            actions.Add(new PointerAction.Toggle());
        }
        if (!IsActive) { return actions; }
        dt = SwiftMath.Max(0, SwiftMath.Min(dt, 0.1));  // a stalled clock must not fire one huge jump

        double move = t < _leftStickQuietUntil ? 0 : Deflection(_leftStick);
        if (move > 0)
        {
            double speed = move * move * Config.StickMaxSpeed * ControllerConfig.Factor(Config.PointerSpeed) * dt;
            double m = Magnitude(_leftStick);
            actions.Add(new PointerAction.Move(Dx: _leftStick.X / m * speed, Dy: -_leftStick.Y / m * speed));
        }
        double scroll = t < _rightStickQuietUntil ? 0 : Deflection(_rightStick);
        if (scroll > 0)
        {
            double speed = scroll * scroll * Config.StickMaxScroll * ControllerConfig.Factor(Config.ScrollSpeed) * dt;
            double m = Magnitude(_rightStick);
            // Pushing up shows what is above; pushing right shows what is to the right.
            actions.Add(new PointerAction.Scroll(Vertical: _rightStick.Y / m * speed, Horizontal: -_rightStick.X / m * speed));
        }
        return actions;
    }

    private static double Magnitude(TouchPoint p) => SwiftMath.Min(Math.Sqrt(p.X * p.X + p.Y * p.Y), 1);

    /// <summary>How far past the dead zone a stick is pushed, 0...1.</summary>
    internal readonly double Deflection(TouchPoint p)
    {
        double m = Magnitude(p);
        double zone = Config.StickDeadZone;
        if (!(m > zone)) { return 0; }
        return (m - zone) / (1 - zone);
    }

    private void UpdateChord(ControllerButton button, bool pressed, double t)
    {
        _chordDown = pressed ? _chordDown.Add(button) : _chordDown.Remove(button);
        if (_chordDown.SetEquals([ControllerButton.Menu, ControllerButton.Options]))
        {
            if (_chordStartedAt == null)
            {
                _chordStartedAt = t;
                _chordFired = false;
            }
        }
        else
        {
            _chordStartedAt = null;
            if (_chordDown.IsEmpty) { _chordFired = false; }
        }
    }
}

/// <summary>Which frontmost apps the controller pointer steps aside for.</summary>
public static class ControllerApps
{
    /// <summary>
    /// `LSApplicationCategoryType` values that mean a game: `public.app-category.games` and
    /// every `public.app-category.*-games` subcategory.
    /// </summary>
    public static bool IsGameCategory(string category)
    {
        string c = category.ToLowerInvariant();
        return c == "public.app-category.games"
            || (c.StartsWith("public.app-category.", StringComparison.Ordinal) && c.EndsWith("-games", StringComparison.Ordinal));
    }
}

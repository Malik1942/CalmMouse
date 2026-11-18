// Port of Sources/CalmMouseCore/TapRecognizer.swift. `RightClickMode`, which is declared in that
// Swift file too, lives on its own in Ports/RightClickMode.cs (Presets needed it first).
//
// Names: methods keep Swift's base name and its internal parameter names, so
// `physicalButton(isDown:at:)` is `PhysicalButton(isDown, t)`,
// `noteScroll(deltaMagnitude:at:)` is `NoteScroll(deltaMagnitude, t)`,
// `handleFrame(_:at:cursorMoving:)` is `HandleFrame(touches, t, cursorMoving)`, and
// `disarmDrag()` / `reset()` are `DisarmDrag()` / `Reset()`. `config`, `tapCount`, `dragActive`,
// `dragArmIsPair`, `lastRejection`, `rejectionCounts` and `lastRejectionX/Y` are properties.
//
// `config` is a ref-returning property: Swift's `r.config.enabled = false` mutates the class's
// stored struct in place, and so does `r.Config.Enabled = false` here (a plain get/set property
// would hand out a copy). `r.Config = c` replaces the whole config, as in Swift.
//
// Types: TouchSample and TapConfig are mutable record structs (both Equatable in Swift; equality
// compares every field, `sensitivity`'s backing field included). TapConfig's `sensitivity` didSet
// is its setter. `new TapConfig()` is Swift's `TapConfig()` (defaults, tuned for 0.5);
// `default(TapConfig)` is all zeros and false and must not be used. TouchSample has no
// zero-argument initializer in Swift, so `new TouchSample()` is all zeros. TapEvent has no
// payloads and is a plain enum; so is the nested `TapRecognizer.Rejection`, whose Swift raw values
// (the keys of `rejectionCounts`) are kept with [RawValue]. TapRecognizer is a class, as in Swift.
//
// Value semantics: `rejectionCounts` is a Swift dictionary handed out by value, so here it is an
// ImmutableDictionary: a caller's copy never changes under it. The private `tracks`, `dragIDs` and
// `scrollBurst` stay ordinary mutable collections inside the class, as Swift's stored properties of
// a class are mutated in place too.
//
// Order: Swift walks `tracks` (a Dictionary) in three places. Swift iterates a copy of the
// dictionary, so the loops here walk a snapshot (`ToArray()`), which matters in the lift loop: a
// pair partner demoted mid-loop is still seen with its old `isDrag`. Swift's dictionary order is
// random per process; C#'s is insertion order (with reused slots after removals). Only the lift
// loop's results can depend on it: when several touches lift in one frame, the order of the events
// they produce, and which one ends up in `LastRejection`/`LastRejectionX/Y`. Real frames produce
// at most one event per lift frame (two fingers poison each other), and the tests never depend on
// the order. `dragIDs` (a Set) is only ever walked to apply the same change to every member.
//
// Numbers: Swift's Int is 64 bits, so touch ids, `tapCount` and the rejection counts are longs,
// and the `+= 1`s are `checked` (Swift traps on overflow). `min`/`max` are Swift's (see
// SwiftMath); `squareRoot()` is Math.Sqrt. `-.infinity` is double.NegativeInfinity. Swift
// `guard a >= b` is written `if (!(a >= b))` so a NaN takes the same branch.

using System.Collections.Immutable;

namespace CalmMouse.Core;

// MARK: - Touch model (framework-free so it can be unit-tested)

/// <summary>
/// One touch on the Magic Mouse surface, as delivered by a contact frame.
/// Coordinates are normalized to the surface (0...1 on both axes).
/// </summary>
public record struct TouchSample
{
    public long Id { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>Contact size as reported by the hardware (≈0.1...2; a resting palm is large, a graze tiny).</summary>
    public double Size { get; set; }

    public TouchSample(long id, double x, double y, double size)
    {
        Id = id;
        X = x;
        Y = y;
        Size = size;
    }
}

// MARK: - Config

// RightClickMode (how a tap becomes a RIGHT click) is in Ports/RightClickMode.cs.

public record struct TapConfig
{
    public bool Enabled { get; set; } = false;

    private double _sensitivity = 0.5;

    /// <summary>
    /// 0 = firm (deliberate taps only) ... 1 = light (very easy to trigger). Everything below
    /// is derived from this unless you set it explicitly.
    /// </summary>
    public double Sensitivity
    {
        readonly get => _sensitivity;
        set
        {
            _sensitivity = value;
            Retune();   // Swift: didSet
        }
    }

    /// <summary>A tap must be shorter than this. Seconds.</summary>
    public double MaxDuration { get; set; } = 0;
    /// <summary>...and travel less than this across the surface (normalized units; the whole shell is 1.0).</summary>
    public double MaxMovement { get; set; } = 0;
    /// <summary>Contacts that never reach this size are grazes/ghosts, not taps.</summary>
    public double MinPeakSize { get; set; } = 0.05;
    /// <summary>No taps this soon after real scrolling — a finger coming off a swipe isn't clicking. Seconds.</summary>
    public double ScrollCooldown { get; set; } = 0;
    /// <summary>No taps this soon after a physical button release (fingers reposition after a real click). Seconds.</summary>
    public double ButtonCooldown { get; set; } = 0.25;
    /// <summary>
    /// A single scroll event with at least this much point delta counts as "real scrolling".
    /// Calibrated against a raw capture: a tapping finger's own drift produces 2–6 pt per event
    /// (which must NOT veto the tap), a deliberate swipe tens of points.
    /// </summary>
    public double ScrollActivityThreshold { get; set; } = 8;

    /// <summary>
    /// Restrict where taps may START. Off = the whole surface taps. On = only the front part,
    /// away from the side edges — where deliberate fingertip taps land, and where gripping
    /// fingers don't. (Applies to the tap's landing point; drags and clicks are unaffected.)
    /// </summary>
    public bool TapZoneEnabled { get; set; } = false;
    /// <summary>How deep the zone reaches from the front edge, as a fraction of the surface (0.25...0.75).</summary>
    public double TapZoneDepth { get; set; } = 0.5;
    /// <summary>
    /// Side strips excluded on the left and right when the zone is on. Kept narrow: real-world
    /// capture shows middle-finger taps landing at x 0.82–0.98 — hard against the right edge —
    /// so anything wider silently eats legitimate taps.
    /// </summary>
    public double TapZoneEdgeMargin { get; set; } = 0.04;
    /// <summary>
    /// Hardware orientation: whether the front of the mouse is y == 1 in normalized touch
    /// coordinates. Set by the app layer from a real-device calibration; both cases are tested.
    /// </summary>
    public bool FrontIsHighY { get; set; } = true;

    /// <summary>
    /// Tap-to-right-click: some taps post a right click instead of a left one, per
    /// `RightClickMode`. Off by default; requires `Enabled` like everything else here.
    /// </summary>
    public bool TapRightClick { get; set; } = false;
    public RightClickMode RightClickMode { get; set; } = RightClickMode.RightSide;
    /// <summary>
    /// Right-side mode: taps at x ≥ this (fraction of the surface from the left) right-click.
    /// 0.6 leaves the centre — where index-finger taps land — as left click, and catches the
    /// middle-finger territory (real captures put those at x 0.82–0.98).
    /// </summary>
    public double RightSideStart { get; set; } = 0.6;
    /// <summary>
    /// Double-tap mode: the follow-up tap must complete within this of the previous tap.
    /// The app layer sets it to the system double-click interval. Seconds.
    /// </summary>
    public double DoubleTapWindow { get; set; } = 0.35;
    /// <summary>
    /// ...and land within this of it (normalized surface units). Same figure as
    /// `DragMaxDistance`, for the same reason: a follow-up lands where the first tap was.
    /// </summary>
    public double DoubleTapMaxDistance { get; set; } = 0.18;

    /// <summary>
    /// Tap-and-drag: tap, then touch again within `DragWindow` and hold — the second touch
    /// presses the button down until it lifts. Off by default.
    /// </summary>
    public bool TapAndDrag { get; set; } = false;
    /// <summary>
    /// How soon after a tap the follow-up touch must land to start a drag. The app layer sets
    /// this to the system double-click interval. Seconds.
    /// </summary>
    public double DragWindow { get; set; } = 0.35;
    /// <summary>
    /// ...and how close to the tap it must land (normalized surface units). Tight on purpose:
    /// a drag follow-up lands where the tap was; a finger coming back to REST lands wherever.
    /// </summary>
    public double DragMaxDistance { get; set; } = 0.18;
    /// <summary>
    /// A drag finger that travels this far across the SURFACE isn't dragging — dragging moves
    /// the mouse under a planted finger. It's a scroll swipe: release the button immediately.
    /// </summary>
    public double DragSwipeCancelDistance { get; set; } = 0.12;

    /// <summary>
    /// Two-finger drag: rest two fingers on the surface together (no tap needed), move the
    /// mouse to drag, lift to drop. Independent of tap-and-drag. Off by default.
    /// </summary>
    public bool TwoFingerDrag { get; set; } = false;
    /// <summary>
    /// The two fingers must land within this of each other — deliberate placement lands as a
    /// pair; incidental grip contacts accumulate one by one. Seconds.
    /// </summary>
    public double TwoFingerPairWindow { get; set; } = 0.15;
    /// <summary>Hold the pair this long and the button presses down (that's the "long press"). Seconds.</summary>
    public double TwoFingerLongPress { get; set; } = 0.35;

    /// <summary>
    /// A sustained burst of strong scroll events means the finger is SCROLLING, whatever the
    /// cursor is doing — surface-travel checks go blind when the mouse moves (drift re-anchors),
    /// but a genuine swipe pumps out strong deltas that a planted drag finger never sustains.
    /// When the summed magnitude of strong events inside the window crosses the threshold, an
    /// armed drag disarms and a pressed drag releases immediately.
    /// </summary>
    public double ScrollBurstWindow { get; set; } = 0.3;
    public double ScrollBurstCancelThreshold { get; set; } = 80;

    /// <summary>
    /// The long press may not fire while ANY scroll activity (however weak) is this recent —
    /// slow reading-scrolls emit events below the "strong" threshold, and a press landing in
    /// the middle of one blocks it dead. Seconds.
    /// </summary>
    public double PressQuietPeriod { get; set; } = 0.25;
    /// <summary>
    /// An armed pair that hasn't managed to press within this long disarms silently. Without a
    /// TTL, a press deferred by ongoing scrolling would fire out of nowhere seconds later.
    /// </summary>
    public double PairArmTTL { get; set; } = 1.5;
    /// <summary>
    /// After any drag ends, no new PAIR may arm for this long — a two-finger drag ends with the
    /// hand settling back onto the shell, and those two landing contacts look exactly like a
    /// deliberate arm. (Single-finger re-arms already require a fresh tap, so no cooldown there.)
    /// </summary>
    public double PairRearmCooldown { get; set; } = 0.4;

    /// <summary>The defaults. (Without it `new TapConfig()` would be all zeros and false.)</summary>
    public TapConfig() { Retune(); }

    public TapConfig(double sensitivity)
    {
        // Swift's init sets the stored value without running didSet, then retunes once.
        _sensitivity = SwiftMath.Min(SwiftMath.Max(sensitivity, 0), 1);
        Retune();
    }

    private void Retune()
    {
        double s = SwiftMath.Min(SwiftMath.Max(_sensitivity, 0), 1);
        MaxDuration = 0.10 + 0.20 * s;        // 100 ms ... 300 ms
        MaxMovement = 0.012 + 0.038 * s;      // 1.2% ... 5% of the surface
        ScrollCooldown = 0.30 - 0.20 * s;     // 300 ms ... 100 ms
    }
}

// MARK: - Recognizer

/// <summary>What a processed frame (or button transition) asks the caller to do.</summary>
public enum TapEvent
{
    /// <summary>Post a click (down+up) at the cursor.</summary>
    Tap,
    /// <summary>Post a RIGHT click (down+up) at the cursor — tap-to-right-click matched this tap.</summary>
    RightTap,
    /// <summary>Tap-and-drag: press the button down and keep it down.</summary>
    DragBegan,
    /// <summary>
    /// The drag finger lifted. If the press was already posted, release the button; if the
    /// drag was still only armed, this was a quick second tap — post its click.
    /// </summary>
    DragEnded,
    /// <summary>
    /// The drag finger swiped across the surface — the user is scrolling, not dragging.
    /// If pressed, release the button and lift scroll blocking at once (no grace period);
    /// if only armed, do nothing at all — the swipe just scrolls.
    /// </summary>
    DragSwipeCancelled,
    /// <summary>
    /// The drag was cancelled by outside events (physical click, disable). Release the button
    /// if it was pressed; never emit a click.
    /// </summary>
    DragCancelled,
    /// <summary>
    /// A two-finger long press completed: press the button down NOW (the caller posts the
    /// mouseDown; motion is not required first).
    /// </summary>
    DragPressed,
}

/// <summary>
/// Pure tap detector. Feed it contact frames (plus button/scroll context) in wall-clock order;
/// it returns how many taps completed in each frame. Not thread-safe by design — the caller
/// serializes onto one queue.
/// </summary>
/// <remarks>
/// (In the Swift file this doc comment sits just above `TapEvent`, where it got attached to the
/// enum; it describes this class.)
/// </remarks>
public sealed class TapRecognizer
{
    private TapConfig _config;

    /// <summary>
    /// The settings. Returned by reference so `r.Config.Enabled = false` changes the recognizer's
    /// own config, as Swift's `r.config.enabled = false` does.
    /// </summary>
    public ref TapConfig Config => ref _config;

    private struct Track
    {
        public double StartTime { get; set; }
        public double StartX { get; set; }
        public double StartY { get; set; }
        public double MaxMovement { get; set; }
        public double PeakSize { get; set; }
        /// <summary>
        /// Poisoned: shared the surface with another finger, overlapped a physical click or
        /// real scrolling — whatever happens later, this touch can never become a tap.
        /// </summary>
        public bool Invalidated { get; set; }
        /// <summary>This touch IS the drag: exempt from tap evaluation and from poisoning.</summary>
        public bool IsDrag { get; set; }

        /// <summary>Swift's memberwise init; `maxMovement`, `invalidated` and `isDrag` have defaults there too.</summary>
        public Track(double startTime, double startX, double startY, double peakSize,
                     double maxMovement = 0, bool invalidated = false, bool isDrag = false)
        {
            StartTime = startTime;
            StartX = startX;
            StartY = startY;
            MaxMovement = maxMovement;
            PeakSize = peakSize;
            Invalidated = invalidated;
            IsDrag = isDrag;
        }
    }

    private readonly Dictionary<long, Track> _tracks = [];
    private bool _buttonIsDown = false;
    private double _lastButtonUp = double.NegativeInfinity;
    private double _lastScroll = double.NegativeInfinity;
    /// <summary>Any scroll activity at all, however weak — the long-press quiet gate.</summary>
    private double _lastAnyScroll = double.NegativeInfinity;
    /// <summary>When the last drag ended (lift, cancel, or burst) — the pair re-arm cooldown anchor.</summary>
    private double _lastDragEnded = double.NegativeInfinity;
    /// <summary>Recent strong scroll events (time, magnitude) inside `ScrollBurstWindow`.</summary>
    private readonly List<(double T, double Magnitude)> _scrollBurst = [];
    public long TapCount { get; private set; } = 0;
    /// <summary>Touches currently carrying the drag (one for tap-and-drag, two for two-finger drag).</summary>
    private readonly HashSet<long> _dragIDs = [];
    private bool _dragIsPair = false;
    private double _pairArmedAt = 0;
    private bool _pairPressSent = false;
    /// <summary>Where/when the last accepted tap happened — the anchor for tap-and-drag.</summary>
    private double _lastTapAt = double.NegativeInfinity;
    private double _lastTapX = 0;
    private double _lastTapY = 0;

    public bool DragActive => _dragIDs.Count != 0;
    /// <summary>
    /// True while the active drag arm is a two-finger one. The app layer must NOT apply its
    /// single-finger press-on-cursor-motion / arm-timeout logic to pair arms: the pair presses
    /// on its own long-press clock (`DragPressed`).
    /// </summary>
    public bool DragArmIsPair => _dragIsPair;

    /// <summary>Why the most recent lifted touch was NOT a tap — observability for live debugging.</summary>
    public enum Rejection
    {
        /// <summary>Multi-finger, mid-click, or born under scroll/click.</summary>
        [RawValue("poisoned")] Poisoned,
        [RawValue("buttonDown")] ButtonDown,
        [RawValue("tooLong")] TooLong,
        [RawValue("movedTooMuch")] MovedTooMuch,
        [RawValue("tooSmall")] TooSmall,
        [RawValue("buttonCooldown")] ButtonCooldown,
        [RawValue("scrollCooldown")] ScrollCooldown,
        [RawValue("outsideZone")] OutsideZone,
    }
    public Rejection? LastRejection { get; private set; }
    /// <summary>Counts per rejection, keyed by the rejection's Swift raw value (`"tooLong"`).</summary>
    public ImmutableDictionary<string, long> RejectionCounts { get; private set; } =
        ImmutableDictionary<string, long>.Empty.WithComparers(StringComparer.Ordinal);
    /// <summary>
    /// Landing point of the most recently rejected touch — turns "outsideZone" from a mystery
    /// into a coordinate you can compare against the zone bounds.
    /// </summary>
    public double LastRejectionX { get; private set; } = -1;
    public double LastRejectionY { get; private set; } = -1;

    /// <summary>Swift: `init(config: TapConfig = TapConfig())`.</summary>
    public TapRecognizer() : this(new TapConfig()) { }

    public TapRecognizer(TapConfig config)
    {
        _config = config;
    }

    // MARK: Context inputs

    /// <summary>
    /// Report Magic Mouse physical button transitions. While a button is down (and briefly
    /// after), nothing can tap — and every touch alive during the press is poisoned.
    /// A physical press also cancels an active tap-and-drag (returned as `.dragEnded` so the
    /// caller releases the synthetic button before the real one goes down).
    /// </summary>
    /// <remarks>The Swift comment says `.dragEnded`; the code (and its tests) return `DragCancelled`.</remarks>
    public List<TapEvent> PhysicalButton(bool isDown, double t)
    {
        _buttonIsDown = isDown;
        var events = new List<TapEvent>();
        if (isDown)
        {
            if (_dragIDs.Count != 0)
            {
                DemoteDragTracks();
                _lastDragEnded = t;
                events.Add(TapEvent.DragCancelled);
            }
            foreach (long id in _tracks.Keys.ToArray())
            {
                Track track = _tracks[id];
                track.Invalidated = true;
                _tracks[id] = track;
            }
        }
        else
        {
            _lastButtonUp = t;
        }
        return events;
    }

    /// <summary>
    /// Report a Magic Mouse scroll event's point delta. Small deltas are ignored — a tapping
    /// finger always jiggles the surface a little, and that must not veto the tap. The drag
    /// touch is exempt: a finger riding the shell while the mouse physically moves can jiggle
    /// past the threshold, and that must not drop what it's dragging.
    /// </summary>
    public List<TapEvent> NoteScroll(double deltaMagnitude, double t)
    {
        if (deltaMagnitude > 0) { _lastAnyScroll = t; }
        if (!(deltaMagnitude >= _config.ScrollActivityThreshold)) { return []; }
        _lastScroll = t;
        foreach ((long id, Track track) in _tracks.ToArray())
        {
            if (track.IsDrag) { continue; }
            Track stored = _tracks[id];
            stored.Invalidated = true;
            _tracks[id] = stored;
        }

        // Scroll-burst drag cancellation: see ScrollBurstWindow above.
        _scrollBurst.Add((t, deltaMagnitude));
        _scrollBurst.RemoveAll(e => t - e.T > _config.ScrollBurstWindow);
        if (_dragIDs.Count != 0 && ScrollBurstTotal() >= _config.ScrollBurstCancelThreshold)
        {
            DemoteDragTracks();
            _lastDragEnded = t;
            _scrollBurst.Clear();
            return [TapEvent.DragSwipeCancelled];
        }
        return [];
    }

    /// <summary>Swift's `scrollBurst.reduce(0, { $0 + $1.magnitude })`: summed front to back.</summary>
    private double ScrollBurstTotal()
    {
        double sum = 0;
        foreach ((double _, double magnitude) in _scrollBurst) { sum += magnitude; }
        return sum;
    }

    /// <summary>
    /// Silently drop an armed drag — no event, no click, the touch becomes an ordinary
    /// (poisoned) contact. The app layer calls this when the arm times out: the finger came
    /// back to REST, not to drag.
    /// </summary>
    public void DisarmDrag()
    {
        if (_dragIDs.Count == 0) { return; }
        DemoteDragTracks();
    }

    /// <summary>
    /// Strip drag status from every drag touch: they become ordinary poisoned contacts whose
    /// eventual lifts are inert.
    /// </summary>
    private void DemoteDragTracks()
    {
        foreach (long id in _dragIDs)
        {
            if (_tracks.TryGetValue(id, out Track track))
            {
                track.IsDrag = false;
                track.Invalidated = true;
                _tracks[id] = track;
            }
        }
        _dragIDs.Clear();
        _dragIsPair = false;
    }

    /// <summary>
    /// Forget everything (device reconnect, feature toggled). If a drag was active the caller
    /// must release the synthetic button first — check `DragActive` before calling.
    /// </summary>
    public void Reset()
    {
        _tracks.Clear();
        _dragIDs.Clear();
        _dragIsPair = false;
        _buttonIsDown = false;
    }

    // MARK: Frames

    /// <summary>
    /// Process one contact frame: every touch currently on the surface. Returns the events the
    /// caller should act on (taps complete when their finger lifts; a drag begins the moment its
    /// finger lands).
    /// `cursorMoving`: whether the mouse pointer is currently in motion (supplied by the app
    /// layer). While the MOUSE moves, the planted drag finger naturally drifts on the shell —
    /// that drift re-anchors instead of counting toward the swipe-cancel threshold. Only surface
    /// travel while the cursor is STILL reads as "the user is scrolling, not dragging".
    /// </summary>
    public List<TapEvent> HandleFrame(IReadOnlyList<TouchSample> touches, double t, bool cursorMoving = false)
    {
        if (!_config.Enabled)
        {
            bool wasDragging = _dragIDs.Count != 0;
            _tracks.Clear();
            _dragIDs.Clear();
            return wasDragging ? [TapEvent.DragCancelled] : [];
        }

        var events = new List<TapEvent>();
        var seen = new HashSet<long>();
        bool crowded = touches.Count > 1;

        // Two-finger drag: detect the pair BEFORE the update loop below poisons concurrent
        // touches. Exactly two contacts, landed together, still, deliberate.
        if (ArmTwoFingerPair(touches, t))
        {
            events.Add(TapEvent.DragBegan);
        }
        // The pair's long-press clock: hold it long enough and the button presses down —
        // but never mid-scroll (quiet gate), and never after the arm has grown stale (TTL).
        if (_dragIsPair && !_pairPressSent)
        {
            if (t - _pairArmedAt > _config.PairArmTTL)
            {
                DemoteDragTracks(); // silent: resting fingers, not a gesture
            }
            else if (t - _pairArmedAt >= _config.TwoFingerLongPress
                     && t - _lastAnyScroll >= _config.PressQuietPeriod)
            {
                _pairPressSent = true;
                events.Add(TapEvent.DragPressed);
            }
        }

        foreach (TouchSample touch in touches)
        {
            seen.Add(touch.Id);
            if (_tracks.TryGetValue(touch.Id, out Track track))
            {
                double dx = touch.X - track.StartX;
                double dy = touch.Y - track.StartY;
                track.MaxMovement = SwiftMath.Max(track.MaxMovement, Math.Sqrt(dx * dx + dy * dy));
                track.PeakSize = SwiftMath.Max(track.PeakSize, touch.Size);
                // The drag finger is allowed to share the surface and ride under the palm.
                if (!track.IsDrag && (crowded || _buttonIsDown)) { track.Invalidated = true; }
                _tracks[touch.Id] = track;
                if (track.IsDrag && _dragIDs.Contains(touch.Id))
                {
                    if (cursorMoving)
                    {
                        // The mouse is moving: shell drift is expected. Re-anchor so it never
                        // accumulates into a phantom swipe mid-drag.
                        Track stored = _tracks[touch.Id];
                        stored.StartX = touch.X;
                        stored.StartY = touch.Y;
                        _tracks[touch.Id] = stored;
                    }
                    else
                    {
                        // Cursor still + finger travelling = the user is scrolling, not dragging.
                        double sdx = touch.X - track.StartX;
                        double sdy = touch.Y - track.StartY;
                        if (Math.Sqrt(sdx * sdx + sdy * sdy) > _config.DragSwipeCancelDistance)
                        {
                            DemoteDragTracks(); // either finger sweeping ends the whole drag
                            _lastDragEnded = t;
                            events.Add(TapEvent.DragSwipeCancelled);
                        }
                    }
                }
            }
            else if (BeginsDrag(touch, t))
            {
                _dragIDs.Clear();
                _dragIDs.Add(touch.Id);
                _dragIsPair = false;
                _tracks[touch.Id] = new Track(startTime: t, startX: touch.X, startY: touch.Y,
                                              peakSize: touch.Size, isDrag: true);
                events.Add(TapEvent.DragBegan);
            }
            else
            {
                _tracks[touch.Id] = new Track(
                    startTime: t,
                    startX: touch.X,
                    startY: touch.Y,
                    peakSize: touch.Size,
                    // Born under a click, next to another finger, or right after real scrolling:
                    // never a tap. (The scroll check here catches "finger lands to stop coasting".)
                    invalidated: crowded || _buttonIsDown || (t - _lastScroll) < _config.ScrollCooldown
                );
            }
        }

        // Touches that vanished this frame have lifted — evaluate them. (A snapshot, as Swift
        // iterates a copy: entries changed during the loop are still seen as they were.)
        foreach ((long id, Track track) in _tracks.ToArray())
        {
            if (seen.Contains(id)) { continue; }
            _tracks.Remove(id);
            if (track.IsDrag)
            {
                if (_dragIDs.Contains(id))
                {
                    // Either finger lifting ends the drag. A lapsed single-finger arm was a
                    // quick second tap (click it); a lapsed two-finger arm is just a
                    // two-finger tap — not a left-click gesture, stay silent.
                    bool wasPair = _dragIsPair;
                    DemoteDragTracks();
                    _lastDragEnded = t;
                    events.Add(wasPair ? TapEvent.DragCancelled : TapEvent.DragEnded);
                }
            }
            else if (IsTap(track, t))
            {
                TapCount = checked(TapCount + 1);
                TapEvent kind = TapKind(track, t);
                if (kind == TapEvent.RightTap)
                {
                    // A right tap anchors nothing: it must not arm tap-and-drag, and — like a
                    // real right click — it breaks any tap chain (no left-right-left "double").
                    _lastTapAt = double.NegativeInfinity;
                }
                else
                {
                    _lastTapAt = t;
                    _lastTapX = track.StartX;
                    _lastTapY = track.StartY;
                }
                events.Add(kind);
            }
        }
        return events;
    }

    /// <summary>
    /// Two fingers arm a drag when they land as a deliberate pair: together in time, both
    /// still so far, surface otherwise quiet, and inside the tap zone when one is set.
    /// </summary>
    private bool ArmTwoFingerPair(IReadOnlyList<TouchSample> touches, double t)
    {
        if (!(_config.TwoFingerDrag && _dragIDs.Count == 0 && !_buttonIsDown)) { return false; }
        if (!(touches.Count == 2)) { return false; }
        if (!(t - _lastScroll >= _config.ScrollCooldown)) { return false; }
        if (!(t - _lastDragEnded >= _config.PairRearmCooldown)) { return false; }

        foreach (TouchSample touch in touches)
        {
            if (_config.TapZoneEnabled && !InTapZone(x: touch.X, y: touch.Y)) { return false; }
            if (_tracks.TryGetValue(touch.Id, out Track track))
            {
                // An existing contact may join a pair only if it's fresh, clean and still —
                // a finger that's been resting (or swiping) is grip, not gesture.
                if (track.Invalidated) { return false; }
                if (t - track.StartTime > _config.TwoFingerPairWindow) { return false; }
                if (track.MaxMovement > 0.05) { return false; }
            }
        }

        _dragIsPair = true;
        _pairArmedAt = t;
        _pairPressSent = false;
        foreach (TouchSample touch in touches)
        {
            _dragIDs.Add(touch.Id);
            if (_tracks.TryGetValue(touch.Id, out Track track))
            {
                track.IsDrag = true;
                _tracks[touch.Id] = track;
            }
            else
            {
                _tracks[touch.Id] = new Track(startTime: t, startX: touch.X, startY: touch.Y,
                                              peakSize: touch.Size, isDrag: true);
            }
        }
        return true;
    }

    /// <summary>
    /// A fresh touch starts a drag when it follows a tap closely in time and place, with the
    /// surface otherwise quiet. (One drag at a time; a physical press blocks it outright.)
    /// </summary>
    private bool BeginsDrag(TouchSample touch, double t)
    {
        if (!(_config.TapAndDrag && _dragIDs.Count == 0 && !_buttonIsDown)) { return false; }
        if (!(t - _lastTapAt <= _config.DragWindow)) { return false; }
        if (!(t - _lastScroll >= _config.ScrollCooldown)) { return false; }
        // With the tap zone on, drags may only start where taps do — a finger coming back to
        // rest outside the zone (the classic tap-then-rest rhythm) must never arm a drag.
        if (_config.TapZoneEnabled && !InTapZone(x: touch.X, y: touch.Y)) { return false; }
        double dx = touch.X - _lastTapX;
        double dy = touch.Y - _lastTapY;
        return Math.Sqrt(dx * dx + dy * dy) <= _config.DragMaxDistance;
    }

    /// <summary>
    /// An ACCEPTED tap is a left or a right click, decided here. Runs before the tap anchor
    /// updates, so in double-tap mode `_lastTap*` still describes the PREVIOUS tap.
    ///
    /// Double-tap mode with tap-and-drag on never reaches this for the follow-up: that touch
    /// arms a drag instead, and its quick lift surfaces as an unpressed `DragEnded` — the
    /// caller maps that to a right click when this mode is active (the arm gates — window and
    /// landing distance — already match the double-tap ones).
    /// </summary>
    private TapEvent TapKind(Track track, double t)
    {
        if (!_config.TapRightClick) { return TapEvent.Tap; }
        switch (_config.RightClickMode)
        {
            case RightClickMode.RightSide:
                return OnRightSide(x: track.StartX) ? TapEvent.RightTap : TapEvent.Tap;
            case RightClickMode.DoubleTap:
                double dx = track.StartX - _lastTapX;
                double dy = track.StartY - _lastTapY;
                bool isFollowUp = t - _lastTapAt <= _config.DoubleTapWindow
                    && Math.Sqrt(dx * dx + dy * dy) <= _config.DoubleTapMaxDistance;
                return isFollowUp ? TapEvent.RightTap : TapEvent.Tap;
            default:
                throw new ArgumentOutOfRangeException(nameof(track));
        }
    }

    /// <summary>
    /// `FrontIsHighY` is a 180° hardware-orientation calibration, so when it flips, left and
    /// right swap along with front and back.
    /// </summary>
    private bool OnRightSide(double x) =>
        _config.FrontIsHighY ? x >= _config.RightSideStart : x <= 1 - _config.RightSideStart;

    private bool IsTap(Track track, double t)
    {
        if (RejectionReason(track, t) is Rejection rejection)
        {
            LastRejection = rejection;
            string key = RawValues.Of(rejection);
            RejectionCounts = RejectionCounts.SetItem(key, checked(RejectionCounts.GetValueOrDefault(key, 0) + 1));
            LastRejectionX = track.StartX;
            LastRejectionY = track.StartY;
            return false;
        }
        return true;
    }

    private Rejection? RejectionReason(Track track, double t)
    {
        if (track.Invalidated) { return Rejection.Poisoned; }
        if (_buttonIsDown) { return Rejection.ButtonDown; }
        if (t - track.StartTime > _config.MaxDuration) { return Rejection.TooLong; }
        if (track.MaxMovement > _config.MaxMovement) { return Rejection.MovedTooMuch; }
        if (track.PeakSize < _config.MinPeakSize) { return Rejection.TooSmall; }
        if (t - _lastButtonUp < _config.ButtonCooldown) { return Rejection.ButtonCooldown; }
        if (t - _lastScroll < _config.ScrollCooldown) { return Rejection.ScrollCooldown; }
        if (_config.TapZoneEnabled && !InTapZone(x: track.StartX, y: track.StartY)) { return Rejection.OutsideZone; }
        return null;
    }

    /// <summary>Zone test on the tap's landing point.</summary>
    private bool InTapZone(double x, double y)
    {
        double distanceFromFront = _config.FrontIsHighY ? (1 - y) : y;
        if (distanceFromFront > _config.TapZoneDepth) { return false; }
        if (x < _config.TapZoneEdgeMargin || x > 1 - _config.TapZoneEdgeMargin) { return false; }
        return true;
    }
}

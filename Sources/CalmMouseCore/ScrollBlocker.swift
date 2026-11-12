import Foundation

// MARK: - Event model (framework-free so it can be unit-tested)

/// Mirrors CGScrollPhase.
public enum ScrollPhase: Equatable, Sendable {
    case none, mayBegin, began, changed, ended, cancelled
}

/// Mirrors CGMomentumScrollPhase.
public enum MomentumPhase: Equatable, Sendable {
    case none, began, continued, ended
}

public struct ScrollEvent: Equatable, Sendable {
    public var timestamp: TimeInterval
    /// True when the event came from a device we identified as a Magic Mouse.
    public var fromMagicMouse: Bool
    /// True when the event came from an ordinary mouse with a scroll wheel. Never set
    /// together with `fromMagicMouse`.
    public var fromWheelMouse: Bool
    public var isContinuous: Bool
    public var phase: ScrollPhase
    public var momentum: MomentumPhase
    /// Horizontal point delta (CGEvent axis 2).
    public var deltaX: Double
    /// Vertical point delta (CGEvent axis 1).
    public var deltaY: Double
    public var modifiers: ModifierCombo

    public init(timestamp: TimeInterval, fromMagicMouse: Bool = true, isContinuous: Bool = true,
                phase: ScrollPhase = .none, momentum: MomentumPhase = .none,
                deltaX: Double = 0, deltaY: Double = 0, modifiers: ModifierCombo = [],
                fromWheelMouse: Bool = false) {
        self.timestamp = timestamp
        self.fromMagicMouse = fromMagicMouse && !fromWheelMouse
        self.fromWheelMouse = fromWheelMouse
        self.isContinuous = isContinuous
        self.phase = phase
        self.momentum = momentum
        self.deltaX = deltaX
        self.deltaY = deltaY
        self.modifiers = modifiers
    }
}

/// How the event tap should modify a scroll event before passing it on.
public struct ScrollRewrite: Equatable, Sendable {
    public var phase: ScrollPhase
    public var momentum: MomentumPhase
    public var zeroX: Bool
    public var zeroY: Bool
    /// Move the vertical delta onto the horizontal axis (and vice versa).
    public var swapAxes: Bool
    /// Negate both deltas.
    public var invert: Bool
    public var addModifiers: ModifierCombo
    public var removeModifiers: ModifierCombo

    public init(phase: ScrollPhase, momentum: MomentumPhase, zeroX: Bool = false, zeroY: Bool = false,
                swapAxes: Bool = false, invert: Bool = false,
                addModifiers: ModifierCombo = [], removeModifiers: ModifierCombo = []) {
        self.phase = phase
        self.momentum = momentum
        self.zeroX = zeroX
        self.zeroY = zeroY
        self.swapAxes = swapAxes
        self.invert = invert
        self.addModifiers = addModifiers
        self.removeModifiers = removeModifiers
    }

    /// True when applying this rewrite would leave the event exactly as it arrived.
    public func isNoop(for e: ScrollEvent) -> Bool {
        phase == e.phase && momentum == e.momentum && !swapAxes && !invert
            && addModifiers.isEmpty && removeModifiers.isEmpty
            && (!zeroX || e.deltaX == 0) && (!zeroY || e.deltaY == 0)
    }
}

public enum ScrollDecision: Equatable, Sendable {
    case pass
    case drop
    case rewrite(ScrollRewrite)
    /// Swallow the event and glide this many pixels instead (wheel mice only).
    case smooth(dx: Double, dy: Double)
}

public enum ScrollAxis: Equatable, Sendable { case horizontal, vertical }

// MARK: - State machine

/// Pure decision engine. Feed it button + scroll events in wall-clock order, get decisions back.
/// Not thread-safe by design: the event tap delivers everything on one run loop.
public final class ScrollBlocker {
    /// Base settings + per-app overrides.
    public var rules: RuleSet {
        didSet { recomputeConfig() }
    }
    /// Bundle ID of the frontmost app; selects which per-app rule applies.
    public private(set) var activeBundleID: String?
    /// The config actually in force right now (base merged with the active app's rule).
    public private(set) var config: BlockerConfig

    // Button state (Magic Mouse buttons only).
    private var buttonsDown = Set<Int>()
    private var blockUntil: TimeInterval = 0

    // Wheel mouse state, kept apart from the Magic Mouse's so neither blocks the other.
    /// The wheel's own click: CGEvent button 2 (centre).
    public static let wheelButton = 2
    /// The wheel often turns one more notch as the finger lets go of it.
    public static let wheelReleaseGrace: TimeInterval = 0.15
    /// Shortest distance one notch glides, in pixels, before the speed setting scales it.
    /// macOS itself moves a slow single notch by one pixel and a fast spin by fifty or more;
    /// the floor makes a slow notch worth turning, and the fast ones keep their acceleration.
    public static let wheelMinimumStep: Double = 35
    /// Held while scrolling, these mean zoom (or an app's own shortcut), which counts notches.
    /// Gliding would turn one notch into dozens of zoom steps, so those ticks are left as they are.
    public static let wheelSmoothingBypass: ModifierCombo = [.command, .control, .option]
    private var wheelPressed = false
    private var wheelBlockUntil: TimeInterval = 0

    // Gesture bookkeeping.
    private enum Visible { case mayBegin, began }
    /// What the apps have been allowed to see for the current gesture.
    private var visible: Visible?
    /// True between a gesture's start and its end, whether or not apps can see it.
    /// (`visible == nil` is not the same thing: a suppressed gesture is active but invisible.)
    private var gestureActive = false
    /// We swallowed this gesture's start; keep swallowing until it ends.
    private var suppressingGesture = false
    /// Momentum belongs to a suppressed gesture (or a click landed during it) — swallow to the end.
    private var suppressingMomentum = false
    /// Apps saw a momentum "began" and are entitled to a matching "ended".
    private var momentumVisible = false

    // Dead zone.
    private var deadZoneTravel: Double = 0
    private var deadZonePassed = true

    // Axis lock.
    private var cumX: Double = 0
    private var cumY: Double = 0
    private var lockedAxis: ScrollAxis?

    public init(rules: RuleSet = RuleSet()) {
        self.rules = rules
        self.config = rules.base
    }

    public convenience init(config: BlockerConfig) {
        self.init(rules: RuleSet(base: config))
    }

    /// Convenience for callers that only tweak the base config.
    public var baseConfig: BlockerConfig {
        get { rules.base }
        set { rules.base = newValue }
    }

    public func setActiveApp(bundleID: String?) {
        guard bundleID != activeBundleID else { return }
        activeBundleID = bundleID
        recomputeConfig()
    }

    private func recomputeConfig() {
        config = rules.config(forBundleID: activeBundleID)
    }

    // MARK: Inputs

    /// Report a Magic Mouse button transition. Non-Magic-Mouse buttons must not be reported.
    public func magicMouseButton(_ button: Int, isDown: Bool, at t: TimeInterval) {
        if isDown {
            buttonsDown.insert(button)
        } else {
            buttonsDown.remove(button)
            if buttonsDown.isEmpty {
                blockUntil = t + config.releaseGrace
            }
        }
    }

    /// Report a button transition on a wheel mouse. Only the wheel's own click counts:
    /// pressing the wheel is what turns it by accident. Holding the left button and turning
    /// the wheel is deliberate (scrolling while dragging a file, extending a selection), so
    /// the other buttons never block anything.
    public func wheelMouseButton(_ button: Int, isDown: Bool, at t: TimeInterval) {
        guard button == Self.wheelButton else { return }
        if isDown {
            wheelPressed = true
        } else if wheelPressed {
            wheelPressed = false
            wheelBlockUntil = t + Self.wheelReleaseGrace
        }
    }

    /// A tap-and-drag was cancelled because the finger started scroll-swiping: drop every trace
    /// of blocking RIGHT NOW — no release grace, and the scroll gesture already in flight gets
    /// unsuppressed so the user's swipe scrolls from its very next event.
    public func liftBlockingImmediately() {
        buttonsDown.removeAll()
        blockUntil = 0
        suppressingGesture = false
        suppressingMomentum = false
    }

    /// Safety valve: the system reports no buttons pressed but we think some are (missed mouse-up).
    public func forceReleaseAllButtons(at t: TimeInterval) {
        if !buttonsDown.isEmpty {
            buttonsDown.removeAll()
            blockUntil = t + config.releaseGrace
        }
        if wheelPressed {
            wheelPressed = false
            wheelBlockUntil = t + Self.wheelReleaseGrace
        }
    }

    public var anyButtonDown: Bool { !buttonsDown.isEmpty || wheelPressed }

    /// Live diagnostics for the status file — when scrolling "mysteriously" dies, this names
    /// the component holding it: stuck buttons, an unexpired grace, or gesture suppression.
    public func debugSnapshot(at t: TimeInterval) -> String {
        "buttons=\(buttonsDown.count) graceRemainingMs=\(Int(max(0, blockUntil - t) * 1000)) " +
        "suppressingGesture=\(suppressingGesture) suppressingMomentum=\(suppressingMomentum) " +
        "blocking=\(isBlocking(at: t)) " +
        "wheelPressed=\(wheelPressed) wheelGraceRemainingMs=\(Int(max(0, wheelBlockUntil - t) * 1000)) " +
        "wheelBlocking=\(isBlockingWheel(at: t))"
    }

    public func isBlocking(at t: TimeInterval) -> Bool {
        guard config.blockScrollWhileClicked else { return false }
        return !buttonsDown.isEmpty || t < blockUntil
    }

    /// The wheel mouse's equivalent of `isBlocking`: the wheel is pressed, or was a moment ago.
    public func isBlockingWheel(at t: TimeInterval) -> Bool {
        guard config.blockScrollWhileWheelPressed else { return false }
        return wheelPressed || t < wheelBlockUntil
    }

    // MARK: Scroll decisions

    public func handleScroll(_ e: ScrollEvent) -> ScrollDecision {
        if e.fromWheelMouse { return handleWheel(e) }
        guard e.fromMagicMouse else { return .pass }
        if config.disableScrollEntirely { return .drop }

        if e.momentum != .none {
            return handleMomentum(e)
        }
        return handleGesture(e)
    }

    /// A wheel tick. Ticks have no phases and no momentum, so none of the gesture state is
    /// read or written here: a wheel turning in the middle of a Magic Mouse swipe must not
    /// disturb it.
    ///
    /// A wheel mouse answers only to its own two settings. Everything else in the config
    /// (dead zone, axis lock, momentum, sideways block, modifier actions, per-app rules) was
    /// set up for a touch surface and stays with the Magic Mouse, so turning wheel support on
    /// never changes how an existing setup behaves.
    private func handleWheel(_ e: ScrollEvent) -> ScrollDecision {
        if isBlockingWheel(at: e.timestamp) { return .drop }

        let discrete: ScrollDecision = config.reverseWheelScroll
            ? .rewrite(ScrollRewrite(phase: e.phase, momentum: e.momentum, invert: true))
            : .pass
        guard config.smoothWheelScroll, e.modifiers.isDisjoint(with: Self.wheelSmoothingBypass) else {
            return discrete
        }

        let direction: Double = config.reverseWheelScroll ? -1 : 1
        var dx = wheelDistance(e.deltaX) * direction
        var dy = wheelDistance(e.deltaY) * direction
        // Shift turns the wheel sideways. macOS does that itself for a wheel's own events but
        // not for the gliding ones that replace them, so it is done here.
        if e.modifiers.contains(.shift), dx == 0 {
            dx = dy
            dy = 0
        }
        guard dx != 0 || dy != 0 else { return discrete }
        return .smooth(dx: dx, dy: dy)
    }

    /// A ⌘, ⌃ or ⌥ tick from the wheel is a zoom notch and is never smoothed. Arriving while a
    /// glide is still playing out, it means the user has moved on, so the glide stops.
    public static func interruptsGlide(_ e: ScrollEvent) -> Bool {
        e.fromWheelMouse && !e.modifiers.isDisjoint(with: wheelSmoothingBypass)
    }

    /// How far one tick glides along an axis, keeping its sign.
    private func wheelDistance(_ delta: Double) -> Double {
        guard delta != 0 else { return 0 }
        let distance = max(abs(delta), Self.wheelMinimumStep) * config.wheelScrollSpeed
        return delta < 0 ? -distance : distance
    }

    private func handleGesture(_ e: ScrollEvent) -> ScrollDecision {
        let blocking = isBlocking(at: e.timestamp)

        switch e.phase {
        case .mayBegin:
            beginNewGesture()
            if blocking {
                suppressingGesture = true
                return .drop
            }
            visible = .mayBegin
            return .pass

        case .began:
            // `began` unambiguously starts a gesture. Unless we passed a `mayBegin` for this very
            // gesture, reset — this also recovers if a previous gesture's `ended` got lost.
            if visible != .mayBegin { beginNewGesture() }
            if blocking {
                return suppressVisibleGesture(e)
            }
            return passGesture(e)

        case .changed:
            if suppressingGesture {
                if blocking { return .drop }
                // Blocking ended mid-gesture (button released, grace expired). The rest of the
                // gesture is the user's deliberate scrolling — swallowing it whole meant "tap,
                // then scroll" stayed dead for the entire swipe. Resume: apps join mid-gesture,
                // with the dead zone waived since the finger has already travelled.
                suppressingGesture = false
                gestureActive = false
                return passGesture(e)
            }
            if blocking {
                return suppressVisibleGesture(e)
            }
            return passGesture(e)

        case .ended, .cancelled:
            gestureActive = false
            if suppressingGesture {
                suppressingGesture = false
                suppressingMomentum = true // any momentum tail belongs to the swallowed gesture
                return .drop
            }
            if !deadZonePassed {
                // The whole gesture stayed inside the dead zone; apps never saw it start.
                deadZonePassed = true
                suppressingMomentum = true
                visible = nil
                return .drop
            }
            visible = nil
            return .pass

        case .none:
            // Phase-less scroll (discrete wheel ticks or synthetic). Nothing to keep consistent.
            if blocking { return .drop }
            return finish(e, phase: e.phase)
        }
    }

    /// Common path for `began`/`changed` when nothing is blocking: dead zone, then transforms.
    private func passGesture(_ e: ScrollEvent) -> ScrollDecision {
        if !gestureActive {
            // Joining a gesture that started before we were watching (no `began` seen).
            // We can't know how far it has already travelled, so the dead zone doesn't apply.
            beginNewGesture()
            deadZonePassed = true
        }

        if !deadZonePassed {
            deadZoneTravel += abs(e.deltaX) + abs(e.deltaY)
            if deadZoneTravel < config.deadZone { return .drop }
            deadZonePassed = true
            // Apps never saw the real `began`; relabel this event so the gesture is well-formed.
            visible = .began
            return finish(e, phase: .began)
        }

        visible = .began
        return finish(e, phase: e.phase)
    }

    private func handleMomentum(_ e: ScrollEvent) -> ScrollDecision {
        if !config.momentumEnabled { return .drop }

        if suppressingMomentum {
            if e.momentum == .ended { suppressingMomentum = false }
            return .drop
        }

        if isBlocking(at: e.timestamp) {
            // A click landed while the page was coasting: stop the coast.
            if e.momentum == .began {
                suppressingMomentum = true
                return .drop
            }
            if e.momentum != .ended { suppressingMomentum = true }
            if momentumVisible {
                // Apps saw a "began" — hand them a clean "ended" with zero deltas.
                momentumVisible = false
                return .rewrite(ScrollRewrite(phase: .none, momentum: .ended, zeroX: true, zeroY: true))
            }
            return .drop
        }

        if e.momentum == .began { momentumVisible = true }
        if e.momentum == .ended { momentumVisible = false }
        return finish(e, phase: e.phase)
    }

    // MARK: Helpers

    private func beginNewGesture() {
        gestureActive = true
        visible = nil
        suppressingGesture = false
        suppressingMomentum = false // a new gesture can only start after the previous momentum is over
        momentumVisible = false
        deadZoneTravel = 0
        deadZonePassed = config.deadZone <= 0
        cumX = 0
        cumY = 0
        lockedAxis = nil
    }

    /// A click arrived mid-gesture. If apps have seen the gesture, hand them a clean ending
    /// (zero deltas) instead of leaving them hanging; then swallow the rest.
    private func suppressVisibleGesture(_ e: ScrollEvent) -> ScrollDecision {
        suppressingGesture = true
        switch visible {
        case .mayBegin:
            visible = nil
            return .rewrite(ScrollRewrite(phase: .cancelled, momentum: .none, zeroX: true, zeroY: true))
        case .began:
            visible = nil
            return .rewrite(ScrollRewrite(phase: .ended, momentum: .none, zeroX: true, zeroY: true))
        case nil:
            return .drop
        }
    }

    /// Apply the transforms that act on a passing event: modifier action, axis lock, horizontal block.
    private func finish(_ e: ScrollEvent, phase: ScrollPhase) -> ScrollDecision {
        var r = ScrollRewrite(phase: phase, momentum: e.momentum)

        switch config.action(for: e.modifiers) {
        case .normal:
            break
        case .block:
            return .drop
        case .horizontal:
            r.swapAxes = true
            r.removeModifiers = e.modifiers
        case .invert:
            r.invert = true
            r.removeModifiers = e.modifiers
        case .zoomCommand:
            r.removeModifiers = e.modifiers.subtracting(.command)
            r.addModifiers = .command
        case .zoomControl:
            r.removeModifiers = e.modifiers.subtracting(.control)
            r.addModifiers = .control
        }

        // Axis lock: once a gesture commits to an axis, zero the other one.
        if config.axisLock {
            if lockedAxis == nil {
                cumX += abs(e.deltaX)
                cumY += abs(e.deltaY)
                let dominant = max(cumX, cumY)
                let other = min(cumX, cumY)
                if dominant >= config.axisLockThreshold && dominant >= other * config.axisLockRatio {
                    lockedAxis = cumY >= cumX ? .vertical : .horizontal
                }
            }
            switch lockedAxis {
            case .vertical:   r.zeroX = true
            case .horizontal: r.zeroY = true
            case nil:         break
            }
        }

        if config.blockHorizontalScroll {
            // After a swap the "horizontal" content lives on the axis the swap moved it to.
            if r.swapAxes { r.zeroY = true } else { r.zeroX = true }
        }

        return r.isNoop(for: e) ? .pass : .rewrite(r)
    }
}

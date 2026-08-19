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
                deltaX: Double = 0, deltaY: Double = 0, modifiers: ModifierCombo = []) {
        self.timestamp = timestamp
        self.fromMagicMouse = fromMagicMouse
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

    /// Safety valve: the system reports no buttons pressed but we think some are (missed mouse-up).
    public func forceReleaseAllButtons(at t: TimeInterval) {
        guard !buttonsDown.isEmpty else { return }
        buttonsDown.removeAll()
        blockUntil = t + config.releaseGrace
    }

    public var anyButtonDown: Bool { !buttonsDown.isEmpty }

    public func isBlocking(at t: TimeInterval) -> Bool {
        guard config.blockScrollWhileClicked else { return false }
        return !buttonsDown.isEmpty || t < blockUntil
    }

    // MARK: Scroll decisions

    public func handleScroll(_ e: ScrollEvent) -> ScrollDecision {
        guard e.fromMagicMouse else { return .pass }
        if config.disableScrollEntirely { return .drop }

        if e.momentum != .none {
            return handleMomentum(e)
        }
        return handleGesture(e)
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
            if suppressingGesture { return .drop }
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

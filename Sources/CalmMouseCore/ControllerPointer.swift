import Foundation

/// How a game controller drives the pointer. Speeds use the same -1...1 scale as the mice's
/// Tracking speed slider, 0 being normal.
public struct ControllerConfig: Equatable, Sendable {
    public var pointerSpeed: Double = 0
    public var scrollSpeed: Double = 0
    /// A light touch on the touchpad clicks, as on a laptop's trackpad.
    public var tapToClick: Bool = true
    /// The system's "Natural scrolling": two fingers move the page the way they slide.
    public var naturalScrolling: Bool = true
    /// Pressing the left stick clicks and pressing the right stick right-clicks: the thumb that
    /// points also clicks, as a finger does on a mouse.
    public var stickClicks: Bool = true

    // Feel. Starting values, tuned on a DualSense.

    /// The touchpad is about 1920 × 1080 in its own units, but both axes report -1...1. Vertical
    /// travel is scaled by this so the same finger distance goes the same way in both.
    public var touchpadAspect: Double = 1080.0 / 1920.0
    /// Points per touchpad unit (half the pad's width) at normal speed, before acceleration.
    public var touchpadGain: Double = 420
    /// Pixels of scrolling per touchpad unit with two fingers.
    public var touchpadScrollGain: Double = 520
    /// A touch at most this long, and moving at most `tapMaxTravel`, is a tap.
    public var tapMaxDuration: TimeInterval = 0.2
    /// In touchpad units: 0.06 is 3% of the pad's width.
    public var tapMaxTravel: Double = 0.06
    /// Stick travel that does nothing, so a stick resting slightly off centre doesn't drift.
    public var stickDeadZone: Double = 0.15
    /// Points per second with the left stick pushed all the way, at normal speed.
    public var stickMaxSpeed: Double = 1600
    /// Pixels per second with the right stick pushed all the way, at normal speed.
    public var stickMaxScroll: Double = 1500
    /// Hold Options and Create (Xbox: Menu and View) together this long to turn the controller
    /// pointer on or off.
    public var toggleHold: TimeInterval = 0.5
    /// Pressing a stick down nudges it sideways, and so does letting go. For this long after
    /// either, that stick's movement is ignored so the pointer stays on what is being clicked.
    /// Holding the stick down longer and moving it still drags.
    public var stickClickSettle: TimeInterval = 0.15

    public init() {}

    /// Each end of the -1...1 slider is this many times slower or faster.
    public static let speedRange: Double = 2.5

    static func factor(_ speed: Double) -> Double {
        pow(speedRange, min(max(speed, -1), 1))
    }
}

public enum PointerButton: Equatable, Sendable { case left, right }

/// The controller's buttons CalmMouse uses, by position, so ✕ and A are the same button.
public enum ControllerButton: Equatable, Hashable, Sendable {
    /// ✕ on PlayStation, A on Xbox: the bottom face button.
    case south
    /// ○ on PlayStation, B on Xbox: the right face button.
    case east
    /// Pressing the touchpad down.
    case touchpad
    /// Options on a DualSense, ≡ Menu on Xbox.
    case menu
    /// Create on a DualSense, ⧉ View on Xbox.
    case options
    /// Pressing the left stick down (L3).
    case leftStick
    /// Pressing the right stick down (R3).
    case rightStick
}

/// What the controller asks of the pointer. Movement is in screen points with y growing
/// downward. Scrolling uses the scroll-wheel convention: positive `vertical` shows what is
/// above, positive `horizontal` what is to the left.
public enum PointerAction: Equatable, Sendable {
    case move(dx: Double, dy: Double)
    case scroll(vertical: Double, horizontal: Double)
    case press(PointerButton)
    case release(PointerButton)
    /// A press and release in one go (a tap on the touchpad).
    case click(PointerButton)
    /// Options + Create held: the user wants the controller pointer switched on or off.
    case toggle
}

/// A finger on the touchpad, in the controller's units: x -1 (left) … 1 (right), y -1 (bottom)
/// … 1 (top).
public struct TouchPoint: Equatable, Sendable {
    public var x: Double
    public var y: Double
    public init(x: Double, y: Double) { self.x = x; self.y = y }
}

/// Turns a game controller's touchpad, sticks and buttons into pointer actions. Pure state and
/// arithmetic: the app layer reads the controller, supplies the clock, and posts the events.
///
/// While inactive it only watches for the on/off chord, so the controller can switch itself on.
public struct ControllerPointer: Sendable {
    public var config: ControllerConfig
    public private(set) var isActive = false

    public init(config: ControllerConfig = ControllerConfig()) {
        self.config = config
    }

    // MARK: Buttons held, by what holds them

    /// Which inputs hold each button down. The button goes down with the first and up with the
    /// last, so ✕ and a pressed touchpad can overlap without a stray release.
    private var holders: [PointerButton: Set<ControllerButton>] = [:]
    /// Which button each held input pressed, so its release lifts that one even if the finger
    /// count changed in between.
    private var pressedBy: [ControllerButton: PointerButton] = [:]

    public func isHeld(_ button: PointerButton) -> Bool { !(holders[button]?.isEmpty ?? true) }

    // MARK: Touchpad

    private var fingers = 0
    private var lastPoint: TouchPoint?
    private var touchStartedAt: TimeInterval = 0
    private var touchTravel: Double = 0
    private var touchMostFingers = 0
    /// The pad was pressed down during this touch: lifting it is not also a tap.
    private var touchPressed = false
    private var lastTouchAt: TimeInterval?
    private var touchSpeed: Double = 0

    // MARK: Sticks

    private var leftStick = TouchPoint(x: 0, y: 0)
    private var rightStick = TouchPoint(x: 0, y: 0)
    /// Until when each stick's movement is ignored, after it was pressed or let go.
    private var leftStickQuietUntil: TimeInterval = -.infinity
    private var rightStickQuietUntil: TimeInterval = -.infinity

    // MARK: Chord

    private var chordDown: Set<ControllerButton> = []
    private var chordStartedAt: TimeInterval?
    private var chordFired = false

    /// The clock must keep ticking: a stick is pushed or the chord is being held.
    public var needsTicks: Bool {
        (isActive && (deflection(leftStick) > 0 || deflection(rightStick) > 0))
            || (chordStartedAt != nil && !chordFired)
    }

    // MARK: On and off

    /// Switching off lets go of anything still held, so no button stays down.
    public mutating func setActive(_ active: Bool) -> [PointerAction] {
        guard active != isActive else { return [] }
        isActive = active
        var actions: [PointerAction] = []
        if !active {
            for button in [PointerButton.left, .right] where isHeld(button) {
                actions.append(.release(button))
            }
            holders = [:]
            pressedBy = [:]
            fingers = 0
            lastPoint = nil
            leftStick = TouchPoint(x: 0, y: 0)
            rightStick = TouchPoint(x: 0, y: 0)
        }
        return actions
    }

    // MARK: Input

    public mutating func button(_ button: ControllerButton, pressed: Bool, at t: TimeInterval) -> [PointerAction] {
        if button == .menu || button == .options {
            updateChord(button, pressed: pressed, at: t)
            return []
        }
        guard isActive else { return [] }
        if button == .leftStick || button == .rightStick {
            // Released, the press it made must still be lifted even if the option went off.
            guard config.stickClicks || pressedBy[button] != nil else { return [] }
            if button == .leftStick { leftStickQuietUntil = t + config.stickClickSettle }
            else { rightStickQuietUntil = t + config.stickClickSettle }
        }
        if pressed {
            guard pressedBy[button] == nil else { return [] }
            let target: PointerButton
            switch button {
            case .south: target = .left
            case .east, .rightStick: target = .right
            case .leftStick: target = .left
            case .touchpad:
                // Two fingers on the pad when it goes down: a right click, as on a trackpad.
                target = fingers >= 2 ? .right : .left
                touchPressed = true
            case .menu, .options: return []
            }
            pressedBy[button] = target
            let wasHeld = isHeld(target)
            holders[target, default: []].insert(button)
            return wasHeld ? [] : [.press(target)]
        } else {
            guard let target = pressedBy.removeValue(forKey: button) else { return [] }
            holders[target]?.remove(button)
            return isHeld(target) ? [] : [.release(target)]
        }
    }

    /// One touchpad report. nil means no finger in that slot.
    public mutating func touchpad(primary: TouchPoint?, secondary: TouchPoint?, at t: TimeInterval) -> [PointerAction] {
        guard isActive else { return [] }
        let count = (primary == nil ? 0 : 1) + (secondary == nil ? 0 : 1)
        // The finger that moves the pointer: the first one, or the second if only it is left.
        let point = primary ?? secondary
        var actions: [PointerAction] = []
        defer {
            fingers = count
            lastTouchAt = count > 0 ? t : nil
        }

        if fingers == 0 {
            guard count > 0, let point else { return [] }
            // A new touch. Its first report sets where it starts, and moves nothing.
            touchStartedAt = t
            touchTravel = 0
            touchMostFingers = count
            touchPressed = isHeld(.left) && holders[.left]?.contains(.touchpad) == true
            touchSpeed = 0
            lastPoint = point
            return []
        }

        if count == 0 {
            // Lifted. A short, still touch is a tap.
            if config.tapToClick, !touchPressed,
               t - touchStartedAt <= config.tapMaxDuration, touchTravel <= config.tapMaxTravel {
                actions.append(.click(touchMostFingers >= 2 ? .right : .left))
            }
            lastPoint = nil
            return actions
        }

        touchMostFingers = max(touchMostFingers, count)
        guard let point, let last = lastPoint, count == fingers else {
            // A finger joined or left: start measuring again from here, so the pointer
            // doesn't jump between the two fingers' positions.
            lastPoint = point
            return []
        }
        lastPoint = point

        let dx = point.x - last.x
        let dy = (point.y - last.y) * config.touchpadAspect
        let distance = (dx * dx + dy * dy).squareRoot()
        touchTravel += distance
        guard distance > 0 else { return [] }

        if count >= 2 {
            let gain = config.touchpadScrollGain * ControllerConfig.factor(config.scrollSpeed)
            // Natural: the page follows the fingers, so fingers going up show what is below.
            let sign: Double = config.naturalScrolling ? 1 : -1
            actions.append(.scroll(vertical: -dy * gain * sign, horizontal: dx * gain * sign))
            return actions
        }

        // Faster swipes go further per millimetre, as on a trackpad. The speed is smoothed
        // because reports can arrive in quick bursts.
        let dt = max(t - (lastTouchAt ?? t), 0.004)
        touchSpeed = touchSpeed == 0 ? distance / dt : 0.7 * touchSpeed + 0.3 * distance / dt
        let gain = config.touchpadGain * ControllerConfig.factor(config.pointerSpeed)
            * Self.touchAcceleration(speed: touchSpeed)
        actions.append(.move(dx: dx * gain, dy: -dy * gain))
        return actions
    }

    /// Slow, careful strokes move less than their distance; fast flicks more. `speed` is in
    /// touchpad units per second.
    static func touchAcceleration(speed: Double) -> Double {
        min(max(0.45 + 0.35 * speed, 0.45), 2.4)
    }

    /// Stick positions, -1...1 on each axis with up positive.
    public mutating func sticks(left: TouchPoint, right: TouchPoint) {
        guard isActive else { return }
        leftStick = left
        rightStick = right
    }

    /// Advances the stick-driven motion and the chord's clock by one frame.
    public mutating func tick(dt: TimeInterval, at t: TimeInterval) -> [PointerAction] {
        var actions: [PointerAction] = []
        if let started = chordStartedAt, !chordFired, t - started >= config.toggleHold {
            chordFired = true
            actions.append(.toggle)
        }
        guard isActive else { return actions }
        let dt = max(0, min(dt, 0.1))  // a stalled clock must not fire one huge jump

        let move = t < leftStickQuietUntil ? 0 : deflection(leftStick)
        if move > 0 {
            let speed = move * move * config.stickMaxSpeed * ControllerConfig.factor(config.pointerSpeed) * dt
            let m = magnitude(leftStick)
            actions.append(.move(dx: leftStick.x / m * speed, dy: -leftStick.y / m * speed))
        }
        let scroll = t < rightStickQuietUntil ? 0 : deflection(rightStick)
        if scroll > 0 {
            let speed = scroll * scroll * config.stickMaxScroll * ControllerConfig.factor(config.scrollSpeed) * dt
            let m = magnitude(rightStick)
            // Pushing up shows what is above; pushing right shows what is to the right.
            actions.append(.scroll(vertical: rightStick.y / m * speed, horizontal: -rightStick.x / m * speed))
        }
        return actions
    }

    private func magnitude(_ p: TouchPoint) -> Double { min((p.x * p.x + p.y * p.y).squareRoot(), 1) }

    /// How far past the dead zone a stick is pushed, 0...1.
    func deflection(_ p: TouchPoint) -> Double {
        let m = magnitude(p)
        let zone = config.stickDeadZone
        guard m > zone else { return 0 }
        return (m - zone) / (1 - zone)
    }

    private mutating func updateChord(_ button: ControllerButton, pressed: Bool, at t: TimeInterval) {
        if pressed { chordDown.insert(button) } else { chordDown.remove(button) }
        if chordDown == [.menu, .options] {
            if chordStartedAt == nil {
                chordStartedAt = t
                chordFired = false
            }
        } else {
            chordStartedAt = nil
            if chordDown.isEmpty { chordFired = false }
        }
    }
}

/// Which frontmost apps the controller pointer steps aside for.
public enum ControllerApps {
    /// `LSApplicationCategoryType` values that mean a game: `public.app-category.games` and
    /// every `public.app-category.*-games` subcategory.
    public static func isGameCategory(_ category: String) -> Bool {
        let c = category.lowercased()
        return c == "public.app-category.games" || (c.hasPrefix("public.app-category.") && c.hasSuffix("-games"))
    }
}

import Foundation
import CoreGraphics
import AppKit
import os
import CalmMouseCore

/// Owns the CGEventTap and bridges CGEvents <-> the pure ScrollBlocker state machine.
final class EventTap {
    private let blocker: ScrollBlocker
    private let identifier = DeviceIdentifier()
    private var tap: CFMachPort?
    private var runLoopSource: CFRunLoopSource?
    private let log = Logger(subsystem: "com.calmmouse.app", category: "tap")

    var debugLogging = false
    var treatUnknownContinuousAsMagicMouse = false

    /// Tap-to-click context feeds. Called on the tap's run loop (main) for Magic Mouse events.
    var onMagicButton: ((_ isDown: Bool, _ t: TimeInterval) -> Void)?
    var onMagicScroll: ((_ deltaMagnitude: Double, _ t: TimeInterval) -> Void)?

    /// Non-nil while a synthetic drag holds the button down (value = that press's click state).
    /// macOS turns hardware motion into `leftMouseDragged` only for *physically* held buttons, so
    /// while this is set the tap rewrites every `mouseMoved` into a dragged event itself —
    /// without that, apps and window dragging only show the result when the button releases.
    var syntheticDragClickState: Int64?

    /// Set when we've seen at least one Magic Mouse event — surfaced in the menu as a sanity check.
    private(set) var lastSeenDevice: String?
    private(set) var swallowedCount = 0

    var isRunning: Bool { tap != nil }

    init(blocker: ScrollBlocker) {
        self.blocker = blocker
    }

    // MARK: Lifecycle

    @discardableResult
    func start() -> Bool {
        guard tap == nil else { return true }

        let mask: CGEventMask =
            (1 << CGEventType.leftMouseDown.rawValue) |
            (1 << CGEventType.leftMouseUp.rawValue) |
            (1 << CGEventType.rightMouseDown.rawValue) |
            (1 << CGEventType.rightMouseUp.rawValue) |
            (1 << CGEventType.otherMouseDown.rawValue) |
            (1 << CGEventType.otherMouseUp.rawValue) |
            (1 << CGEventType.scrollWheel.rawValue) |
            (1 << CGEventType.mouseMoved.rawValue)

        let callback: CGEventTapCallBack = { _, type, event, userInfo in
            guard let userInfo else { return Unmanaged.passUnretained(event) }
            let tap = Unmanaged<EventTap>.fromOpaque(userInfo).takeUnretainedValue()
            return tap.handle(type: type, event: event)
        }

        guard let port = CGEvent.tapCreate(
            tap: .cghidEventTap,
            place: .headInsertEventTap,
            options: .defaultTap,
            eventsOfInterest: mask,
            callback: callback,
            userInfo: Unmanaged.passUnretained(self).toOpaque()
        ) else {
            log.error("CGEvent.tapCreate failed (missing Accessibility permission?)")
            return false
        }
        tap = port
        let source = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, port, 0)
        runLoopSource = source
        CFRunLoopAddSource(CFRunLoopGetMain(), source, .commonModes)
        CGEvent.tapEnable(tap: port, enable: true)
        log.info("event tap started")
        return true
    }

    func stop() {
        guard let port = tap else { return }
        CGEvent.tapEnable(tap: port, enable: false)
        if let source = runLoopSource {
            CFRunLoopRemoveSource(CFRunLoopGetMain(), source, .commonModes)
        }
        runLoopSource = nil
        tap = nil
        log.info("event tap stopped")
    }

    // MARK: Event handling

    private func handle(type: CGEventType, event: CGEvent) -> Unmanaged<CGEvent>? {
        switch type {
        case .tapDisabledByTimeout, .tapDisabledByUserInput:
            // macOS disables taps that are slow or when a secure input field is focused. Re-arm.
            if let port = tap { CGEvent.tapEnable(tap: port, enable: true) }
            log.notice("tap was disabled (\(type.rawValue)); re-enabled")
            return Unmanaged.passUnretained(event)

        case .leftMouseDown, .rightMouseDown, .otherMouseDown:
            handleButton(event, isDown: true)
            return Unmanaged.passUnretained(event)

        case .leftMouseUp, .rightMouseUp, .otherMouseUp:
            handleButton(event, isDown: false)
            return Unmanaged.passUnretained(event)

        case .scrollWheel:
            return handleScroll(event)

        case .mouseMoved:
            // Fast path: motion is untouched unless a synthetic drag is holding the button.
            if let clickState = syntheticDragClickState {
                Self.convertMovedToDragged(event, clickState: clickState)
            }
            return Unmanaged.passUnretained(event)

        default:
            return Unmanaged.passUnretained(event)
        }
    }

    private func isMagicMouse(_ event: CGEvent, isContinuous: Bool) -> Bool {
        if let known = identifier.isMagicMouse(event) {
            if known, lastSeenDevice == nil {
                lastSeenDevice = identifier.info(forSenderID: identifier.senderID(of: event))?.product
            }
            return known
        }
        return treatUnknownContinuousAsMagicMouse && isContinuous
    }

    private func handleButton(_ event: CGEvent, isDown: Bool) {
        // Buttons: we can't ask a button event whether it's "continuous", so with the fallback
        // enabled every unidentified button counts. Identified non-Magic-Mouse buttons never do.
        let magic: Bool
        if let known = identifier.isMagicMouse(event) {
            magic = known
        } else {
            magic = treatUnknownContinuousAsMagicMouse
        }
        // CalmMouse's own synthetic events, by tag:
        //  - tap clicks (instant down+up) are invisible here. Feeding them to the blocker armed
        //    a release-grace window after every tap, which made "tap, then scroll" go dead.
        //  - drag presses DO feed the blocker: during a tap-drag the finger rides the shell, and
        //    its jiggle would scroll the page under whatever is being dragged.
        //  - none of them may feed the tap recognizer: a synthetic press would poison the very
        //    touch that produced it.
        let sourceTag = event.getIntegerValueField(.eventSourceUserData)
        if sourceTag == TapController.syntheticClickTag {
            return
        }
        if sourceTag == TapController.syntheticCancelTag {
            // A drag was cancelled because the finger is scroll-swiping RIGHT NOW: no release
            // grace, and the gesture already in flight must scroll from its next event.
            blocker.liftBlockingImmediately()
            return
        }
        if sourceTag == TapController.syntheticTag {
            let button = Int(event.getIntegerValueField(.mouseEventButtonNumber))
            // NEVER seconds(event) here. Hardware events carry mach ticks, which seconds()
            // converts; events WE post come back with a kernel-assigned stamp in a different
            // unit (CGEventTimestamp is nominally nanoseconds). Converted as ticks, a
            // nanosecond stamp lands ~41x in the future — the drag-release grace then ends
            // millions of seconds from now and every scroll is blocked until a physical
            // click's sane timestamp overwrites it. Stamp with our own clock instead.
            blocker.magicMouseButton(button, isDown: isDown, at: MachTime.now())
            return
        }
        guard magic else { return }
        let button = Int(event.getIntegerValueField(.mouseEventButtonNumber))
        blocker.magicMouseButton(button, isDown: isDown, at: seconds(event))
        onMagicButton?(isDown, seconds(event))
        if debugLogging {
            log.debug("button \(button) \(isDown ? "down" : "up") blocking=\(self.blocker.isBlocking(at: self.seconds(event)))")
        }
    }

    private func handleScroll(_ event: CGEvent) -> Unmanaged<CGEvent>? {
        let isContinuous = event.getIntegerValueField(.scrollWheelEventIsContinuous) != 0
        let magic = isMagicMouse(event, isContinuous: isContinuous)
        let t = seconds(event)

        // Safety valve against a missed mouse-up (e.g. tap was disabled mid-click).
        if blocker.anyButtonDown && !Self.anyPhysicalButtonPressed() {
            blocker.forceReleaseAllButtons(at: t)
            log.notice("no physical button pressed but state said otherwise — reset")
        }

        let e = ScrollEvent(
            timestamp: t,
            fromMagicMouse: magic,
            isContinuous: isContinuous,
            phase: Self.phase(event.getIntegerValueField(.scrollWheelEventScrollPhase)),
            momentum: Self.momentum(event.getIntegerValueField(.scrollWheelEventMomentumPhase)),
            deltaX: event.getDoubleValueField(.scrollWheelEventPointDeltaAxis2),
            deltaY: event.getDoubleValueField(.scrollWheelEventPointDeltaAxis1),
            modifiers: Self.combo(from: event.flags)
        )
        let decision = blocker.handleScroll(e)
        if magic {
            // Feed tap-to-click regardless of the decision: physically, the finger IS moving,
            // even when the scroll is being swallowed.
            onMagicScroll?(abs(e.deltaX) + abs(e.deltaY), t)
        }

        if debugLogging {
            let sender = identifier.senderID(of: event)
            log.debug("scroll sender=\(sender, format: .hex) magic=\(magic) phase=\(String(describing: e.phase)) mom=\(String(describing: e.momentum)) dx=\(e.deltaX, format: .fixed(precision: 1)) dy=\(e.deltaY, format: .fixed(precision: 1)) -> \(String(describing: decision))")
        }

        switch decision {
        case .pass:
            return Unmanaged.passUnretained(event)
        case .drop:
            swallowedCount += 1
            return nil
        case .rewrite(let r):
            apply(r, to: event)
            return Unmanaged.passUnretained(event)
        }
    }

    /// Writes a rewrite back onto the CGEvent. Deltas exist in three parallel representations
    /// (line, point, fixed-point); apps read different ones, so all three must stay consistent.
    internal func apply(_ r: ScrollRewrite, to event: CGEvent) {
        event.setIntegerValueField(.scrollWheelEventScrollPhase, value: Self.phaseRaw(r.phase))
        event.setIntegerValueField(.scrollWheelEventMomentumPhase, value: Self.momentumRaw(r.momentum))

        var pointY = event.getDoubleValueField(.scrollWheelEventPointDeltaAxis1)
        var pointX = event.getDoubleValueField(.scrollWheelEventPointDeltaAxis2)
        var lineY = Double(event.getIntegerValueField(.scrollWheelEventDeltaAxis1))
        var lineX = Double(event.getIntegerValueField(.scrollWheelEventDeltaAxis2))
        var fixedY = event.getDoubleValueField(.scrollWheelEventFixedPtDeltaAxis1)
        var fixedX = event.getDoubleValueField(.scrollWheelEventFixedPtDeltaAxis2)

        if r.swapAxes {
            swap(&pointX, &pointY); swap(&lineX, &lineY); swap(&fixedX, &fixedY)
        }
        if r.invert {
            pointX = -pointX; pointY = -pointY
            lineX = -lineX; lineY = -lineY
            fixedX = -fixedX; fixedY = -fixedY
        }
        if r.zeroX { pointX = 0; lineX = 0; fixedX = 0 }
        if r.zeroY { pointY = 0; lineY = 0; fixedY = 0 }

        // Write order matters: setting the *line* delta also overwrites the point delta (with
        // line × 8) and the fixed-point delta. Setting point or fixed-point touches only itself.
        // So line first, then point, then fixed — otherwise a swap or invert comes out 8x too big.
        event.setIntegerValueField(.scrollWheelEventDeltaAxis1, value: Int64(lineY.rounded()))
        event.setIntegerValueField(.scrollWheelEventDeltaAxis2, value: Int64(lineX.rounded()))
        event.setIntegerValueField(.scrollWheelEventPointDeltaAxis1, value: Int64(pointY.rounded()))
        event.setIntegerValueField(.scrollWheelEventPointDeltaAxis2, value: Int64(pointX.rounded()))
        event.setDoubleValueField(.scrollWheelEventFixedPtDeltaAxis1, value: fixedY)
        event.setDoubleValueField(.scrollWheelEventFixedPtDeltaAxis2, value: fixedX)

        if !r.addModifiers.isEmpty || !r.removeModifiers.isEmpty {
            var flags = event.flags
            flags.subtract(Self.flags(from: r.removeModifiers))
            flags.formUnion(Self.flags(from: r.addModifiers))
            event.flags = flags
        }
    }

    /// Rewrites a hardware `mouseMoved` into the `leftMouseDragged` the system would have
    /// produced had the button been physically held. Deltas and location are already right;
    /// only the type and button identity change.
    static func convertMovedToDragged(_ event: CGEvent, clickState: Int64) {
        event.type = .leftMouseDragged
        event.setIntegerValueField(.mouseEventButtonNumber, value: 0)
        event.setIntegerValueField(.mouseEventClickState, value: clickState)
        event.setDoubleValueField(.mouseEventPressure, value: 1)
    }

    // MARK: Conversions

    private func seconds(_ event: CGEvent) -> TimeInterval {
        MachTime.seconds(event.timestamp)
    }

    private static func anyPhysicalButtonPressed() -> Bool {
        CGEventSource.buttonState(.combinedSessionState, button: .left) ||
        CGEventSource.buttonState(.combinedSessionState, button: .right) ||
        CGEventSource.buttonState(.combinedSessionState, button: .center)
    }

    static func combo(from flags: CGEventFlags) -> ModifierCombo {
        var c: ModifierCombo = []
        if flags.contains(.maskCommand) { c.insert(.command) }
        if flags.contains(.maskAlternate) { c.insert(.option) }
        if flags.contains(.maskControl) { c.insert(.control) }
        if flags.contains(.maskShift) { c.insert(.shift) }
        return c
    }

    static func flags(from combo: ModifierCombo) -> CGEventFlags {
        var f: CGEventFlags = []
        if combo.contains(.command) { f.insert(.maskCommand) }
        if combo.contains(.option) { f.insert(.maskAlternate) }
        if combo.contains(.control) { f.insert(.maskControl) }
        if combo.contains(.shift) { f.insert(.maskShift) }
        return f
    }

    // CGScrollPhase / CGMomentumScrollPhase raw values.
    static func phase(_ raw: Int64) -> ScrollPhase {
        switch raw {
        case 1: return .began
        case 2: return .changed
        case 4: return .ended
        case 8: return .cancelled
        case 128: return .mayBegin
        default: return .none
        }
    }
    static func phaseRaw(_ p: ScrollPhase) -> Int64 {
        switch p {
        case .none: return 0
        case .began: return 1
        case .changed: return 2
        case .ended: return 4
        case .cancelled: return 8
        case .mayBegin: return 128
        }
    }
    static func momentum(_ raw: Int64) -> MomentumPhase {
        switch raw {
        case 1: return .began
        case 2: return .continued
        case 3: return .ended
        default: return .none
        }
    }
    static func momentumRaw(_ m: MomentumPhase) -> Int64 {
        switch m {
        case .none: return 0
        case .began: return 1
        case .continued: return 2
        case .ended: return 3
        }
    }
}

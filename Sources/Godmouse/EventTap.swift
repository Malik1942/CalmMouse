import Foundation
import CoreGraphics
import AppKit
import os
import GodmouseCore

/// Owns the CGEventTap and bridges CGEvents <-> the pure ScrollBlocker state machine.
final class EventTap {
    private let blocker: ScrollBlocker
    private let identifier = DeviceIdentifier()
    private var tap: CFMachPort?
    private var runLoopSource: CFRunLoopSource?
    private let log = Logger(subsystem: "com.godmouse.app", category: "tap")

    var debugLogging = false
    var treatUnknownContinuousAsMagicMouse = false

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
            (1 << CGEventType.scrollWheel.rawValue)

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
        guard magic else { return }
        let button = Int(event.getIntegerValueField(.mouseEventButtonNumber))
        blocker.magicMouseButton(button, isDown: isDown, at: seconds(event))
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

import AppKit
import CoreGraphics
import os
import CalmMouseCore

/// Owns tap-to-click: listens to Magic Mouse contact frames, runs the TapRecognizer, and posts
/// synthetic left clicks. All state is touched on the main queue only.
final class TapController {
    /// Synthetic events carry this in `.eventSourceUserData` so our own EventTap can tell them
    /// from physical input.
    static let syntheticTag: Int64 = 0x476D_5461 // "GmTa"
    /// Tag for the button-up that ends a swipe-cancelled drag: the EventTap must lift scroll
    /// blocking instantly for it (the user is mid-swipe and expects the page to move NOW).
    static let syntheticCancelTag: Int64 = 0x476D_5463 // "GmTc"
    /// Tag for tap clicks (instant down+up). These must NOT feed the scroll blocker: arming a
    /// release-grace window after every tap made "tap, then scroll" dead for 200 ms — and the
    /// whole-gesture swallow stretched that into the entire next swipe.
    static let syntheticClickTag: Int64 = 0x476D_546B // "GmTk"

    private let recognizer: TapRecognizer
    private let log = Logger(subsystem: "com.calmmouse.app", category: "tap-to-click")

    private var devices: [Multitouch.DeviceRef] = []
    /// Owns the device refs in `devices`; releasing it invalidates them (see magicMouseDevices).
    private var deviceListOwner: CFMutableArray?
    private var deviceIDs: Set<UInt64> = []
    private var rescanTimer: Timer?

    // Observability: exposed in the status file so a dead pipeline is diagnosable from outside.
    private(set) var framesReceived = 0
    private(set) var touchesSeen = 0

    // Double-click bookkeeping: macOS doesn't aggregate synthetic clicks for us.
    private var lastTapAt: TimeInterval = -.infinity
    private var lastTapLocation = CGPoint.zero
    private var clickState: Int64 = 1

    /// While a tap-and-drag holds the synthetic button down, this carries the click state the
    /// matching up must repeat. nil = no press in flight.
    private var dragClickState: Int64?

    // Deferred press: an armed drag posts NO event until intent is clear. Cursor motion = drag
    // (press now). Surface swipe = scroll (disarm, zero side effects). Quiet timeout = the
    // finger just came back to rest (disarm). Lift while still armed = quick second tap (click).
    private var armCursorPosition: CGPoint?
    private var armedAt: TimeInterval = 0
    /// Cursor travel that turns an armed drag into a real press.
    private let pressOnCursorTravel: CGFloat = 3
    /// How long an armed drag waits for intent before it's declared a resting finger.
    private let armWindow: TimeInterval = 0.6
    /// Cursor samples for the "is the mouse moving" signal fed to the recognizer.
    private var lastCursorPosition: CGPoint?

    private(set) var tapsPosted = 0
    private(set) var dragsPosted = 0

    /// Fired with the press's click state on drag-down and nil on release, so the EventTap can
    /// rewrite hardware motion into dragged events for exactly the pressed interval.
    var onDragPressChanged: ((Int64?) -> Void)?
    var isListening: Bool { !devices.isEmpty }
    static var isSupported: Bool { Multitouch.isAvailable }

    fileprivate static var shared: TapController?

    init(recognizer: TapRecognizer) {
        self.recognizer = recognizer
        TapController.shared = self
    }

    // MARK: Lifecycle

    func start() {
        guard Multitouch.isAvailable else { return }
        attachDevices()
        // The Magic Mouse drops off Bluetooth and comes back all the time; poll the device list
        // and re-attach when its membership changes. Cheap (a registry snapshot every few seconds).
        rescanTimer?.invalidate()
        rescanTimer = Timer.scheduledTimer(withTimeInterval: 5, repeats: true) { [weak self] _ in
            self?.rescanIfChanged()
        }
    }

    func stop() {
        rescanTimer?.invalidate()
        rescanTimer = nil
        detachDevices()
        endDragIfActive()
        recognizer.reset()
    }

    private func attachDevices() {
        detachDevices()
        let found = Multitouch.magicMouseDevices()
        deviceListOwner = found.owner
        devices = found.devices
        deviceIDs = Set(devices.map { Multitouch.deviceID($0) })
        for device in devices {
            Multitouch.listen(device, frameCallback)
        }
        if !devices.isEmpty {
            log.info("listening for touches on \(self.devices.count) Magic Mouse device(s)")
        }
    }

    private func detachDevices() {
        for device in devices {
            Multitouch.unlisten(device, frameCallback)
        }
        devices.removeAll()
        deviceIDs.removeAll()
        deviceListOwner = nil
    }

    private func rescanIfChanged() {
        let current = Set(Multitouch.magicMouseDevices().devices.map { Multitouch.deviceID($0) })
        if current != deviceIDs {
            log.info("Magic Mouse device set changed — re-attaching")
            attachDevices()
            endDragIfActive()
            recognizer.reset()
        }
    }

    // MARK: Context from the event tap (main thread)

    func physicalButton(isDown: Bool, at t: TimeInterval) {
        handle(recognizer.physicalButton(isDown: isDown, at: t))
    }

    func scrollActivity(deltaMagnitude: Double, at t: TimeInterval) {
        handle(recognizer.noteScroll(deltaMagnitude: deltaMagnitude, at: t))
    }

    // MARK: Frames (arrive on the MT framework's thread)

    fileprivate func handleFrame(_ touches: [TouchSample], at t: TimeInterval) {
        DispatchQueue.main.async { [self] in
            framesReceived += 1
            touchesSeen += touches.count

            let cursor = CGEvent(source: nil)?.location
            let cursorMoving: Bool
            if let cursor, let last = lastCursorPosition {
                cursorMoving = hypot(cursor.x - last.x, cursor.y - last.y) > 1.5
            } else {
                cursorMoving = false
            }
            lastCursorPosition = cursor

            handle(recognizer.handleFrame(touches, at: t, cursorMoving: cursorMoving))
            driveArmedDrag(cursor: cursor, at: t)
        }
    }

    /// Armed-but-unpressed SINGLE-finger drags live or die here, once per frame. Pair arms run
    /// on the recognizer's own long-press clock and must not be pressed or disarmed from here.
    private func driveArmedDrag(cursor: CGPoint?, at t: TimeInterval) {
        guard recognizer.dragActive, dragClickState == nil, !recognizer.dragArmIsPair else { return }
        if let cursor, let armedFrom = armCursorPosition,
           hypot(cursor.x - armedFrom.x, cursor.y - armedFrom.y) > pressOnCursorTravel {
            postDragDown()          // the mouse is moving: this IS a drag — press.
        } else if t - armedAt > armWindow {
            recognizer.disarmDrag() // nothing happened: the finger just came back to rest.
            armCursorPosition = nil
            log.debug("armed drag timed out — disarmed")
        }
    }

    private func handle(_ events: [TapEvent]) {
        for event in events {
            switch event {
            case .tap:
                postClick()
            case .dragBegan:
                // Arm only. The press is posted by driveArmedDrag when the cursor moves.
                armCursorPosition = CGEvent(source: nil)?.location
                armedAt = MachTime.now()
            case .dragEnded:
                if dragClickState != nil {
                    postDragUp()
                } else {
                    // Lifted while still armed: a quick second tap. postClick escalates the
                    // click state itself, so tap-tap lands as a genuine double-click.
                    postClick()
                }
                armCursorPosition = nil
            case .dragSwipeCancelled:
                // If unpressed, the press never happened — the swipe just scrolls, and no app
                // ever saw a button. That's the whole point of deferring.
                if dragClickState != nil { postDragUp(swipeCancelled: true) }
                armCursorPosition = nil
            case .dragCancelled:
                if dragClickState != nil { postDragUp() }
                armCursorPosition = nil
            case .dragPressed:
                // Two-finger long press completed: press right here, no motion required.
                postDragDown()
            }
        }
    }

    /// Escalates the click state (1, 2, 3) for consecutive presses in the same spot, so taps
    /// and tap-drags produce real double-/triple-clicks. Same rules macOS uses: time + radius.
    private func escalatedClickState(at location: CGPoint) -> Int64 {
        let now = ProcessInfo.processInfo.systemUptime
        let dx = location.x - lastTapLocation.x
        let dy = location.y - lastTapLocation.y
        if now - lastTapAt <= NSEvent.doubleClickInterval && (dx * dx + dy * dy).squareRoot() <= 5 {
            clickState = min(clickState + 1, 3)
        } else {
            clickState = 1
        }
        lastTapAt = now
        lastTapLocation = location
        return clickState
    }

    private func mouseEvent(_ type: CGEventType, at location: CGPoint, clickState: Int64) -> CGEvent? {
        let source = CGEventSource(stateID: .combinedSessionState)
        source?.userData = Self.syntheticTag
        // By default macOS suppresses local hardware events for 250 ms after a synthetic post —
        // that reads as stutter right when a drag starts. We're augmenting the mouse, not
        // fighting it: never suppress.
        source?.localEventsSuppressionInterval = 0
        let event = CGEvent(mouseEventSource: source, mouseType: type,
                            mouseCursorPosition: location, mouseButton: .left)
        event?.setIntegerValueField(.mouseEventClickState, value: clickState)
        return event
    }

    private func postClick() {
        guard dragClickState == nil else { return } // never click while holding a drag
        guard let location = CGEvent(source: nil)?.location else { return }
        let state = escalatedClickState(at: location)
        guard
            let down = mouseEvent(.leftMouseDown, at: location, clickState: state),
            let up = mouseEvent(.leftMouseUp, at: location, clickState: state)
        else { return }
        // Tap clicks are invisible to the scroll blocker (see syntheticClickTag).
        down.setIntegerValueField(.eventSourceUserData, value: Self.syntheticClickTag)
        up.setIntegerValueField(.eventSourceUserData, value: Self.syntheticClickTag)
        down.post(tap: .cghidEventTap)
        up.post(tap: .cghidEventTap)
        tapsPosted += 1
        log.debug("tap → click (state \(state)) at \(location.x, format: .fixed(precision: 0)),\(location.y, format: .fixed(precision: 0))")
    }

    private func postDragDown() {
        guard dragClickState == nil else { return }
        guard let location = CGEvent(source: nil)?.location else { return }
        // Inherits the tap's click chain: tap-then-hold presses with state 2, which is also what
        // makes a quick tap-tap read as a double-click.
        let state = escalatedClickState(at: location)
        guard let down = mouseEvent(.leftMouseDown, at: location, clickState: state) else { return }
        down.post(tap: .cghidEventTap)
        dragClickState = state
        onDragPressChanged?(state)
        dragsPosted += 1
        log.debug("drag began (state \(state))")
    }

    private func postDragUp(swipeCancelled: Bool = false) {
        guard let state = dragClickState else { return }
        // Location lookup can transiently fail; the up must be posted regardless, or the
        // synthetic button stays down forever. Fall back to the last known point.
        let location = CGEvent(source: nil)?.location ?? lastTapLocation
        guard let up = mouseEvent(.leftMouseUp, at: location, clickState: state) else { return }
        if swipeCancelled {
            up.setIntegerValueField(.eventSourceUserData, value: Self.syntheticCancelTag)
        }
        dragClickState = nil
        onDragPressChanged?(nil)
        up.post(tap: .cghidEventTap)
        log.debug("drag ended\(swipeCancelled ? " (swipe-cancelled)" : "")")
    }

    /// Safety: a synthetic button must never stay down past a stop/disable/device loss.
    private func endDragIfActive() {
        if dragClickState != nil { postDragUp() }
        armCursorPosition = nil
    }
}

/// C callback — no captured context allowed, so it goes through the shared instance.
private let frameCallback: Multitouch.FrameCallback = { _, rawTouches, count, timestamp, _ in
    guard let controller = TapController.shared, count >= 0 else { return 0 }
    // An empty frame (fingers all lifted) is meaningful — it's what completes a tap. The touches
    // pointer may be nil then, so an empty sample list must still reach the recognizer.
    var samples: [TouchSample] = []
    if count > 0, let rawTouches {
        let n = Int(count)
        samples.reserveCapacity(n)
        let touches = rawTouches.bindMemory(to: MTTouch.self, capacity: n)
        for i in 0..<n {
            let t = touches[i]
            // Size 0 = proximity hover (approach before contact); it would stretch measured
            // tap durations, so only actual contact counts.
            guard Multitouch.touchingStates.contains(t.state), t.size > 0 else { continue }
            samples.append(TouchSample(
                id: Int(t.identifier),
                x: Double(t.normX),
                y: Double(t.normY),
                size: Double(t.size)
            ))
        }
    }
    // Ignore the frame's own timestamp: the recognizer compares touch times against button and
    // scroll times stamped from CGEvents (mach ticks → seconds). Stamping here with the same
    // clock keeps every source comparable; callback latency is well under the tap thresholds.
    controller.handleFrame(samples, at: MachTime.now())
    return 0
}

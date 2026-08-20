import AppKit
import CoreGraphics
import os
import GodmouseCore

/// Owns tap-to-click: listens to Magic Mouse contact frames, runs the TapRecognizer, and posts
/// synthetic left clicks. All state is touched on the main queue only.
final class TapController {
    /// Synthetic events carry this in `.eventSourceUserData` so our own EventTap ignores them.
    static let syntheticTag: Int64 = 0x476D_5461 // "GmTa"

    private let recognizer: TapRecognizer
    private let log = Logger(subsystem: "com.godmouse.app", category: "tap-to-click")

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

    private(set) var tapsPosted = 0
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
            recognizer.reset()
        }
    }

    // MARK: Context from the event tap (main thread)

    func physicalButton(isDown: Bool, at t: TimeInterval) {
        recognizer.physicalButton(isDown: isDown, at: t)
    }

    func scrollActivity(deltaMagnitude: Double, at t: TimeInterval) {
        recognizer.noteScroll(deltaMagnitude: deltaMagnitude, at: t)
    }

    // MARK: Frames (arrive on the MT framework's thread)

    fileprivate func handleFrame(_ touches: [TouchSample], at t: TimeInterval) {
        DispatchQueue.main.async { [self] in
            framesReceived += 1
            touchesSeen += touches.count
            let taps = recognizer.handleFrame(touches, at: t)
            for _ in 0..<taps { postClick() }
        }
    }

    private func postClick() {
        guard let location = CGEvent(source: nil)?.location else { return }
        let now = ProcessInfo.processInfo.systemUptime

        // Consecutive taps in the same spot escalate the click state (1, 2, 3) so apps see real
        // double- and triple-clicks. Same rules macOS uses: time window + small radius.
        let interval = NSEvent.doubleClickInterval
        let dx = location.x - lastTapLocation.x
        let dy = location.y - lastTapLocation.y
        if now - lastTapAt <= interval && (dx * dx + dy * dy).squareRoot() <= 5 {
            clickState = min(clickState + 1, 3)
        } else {
            clickState = 1
        }
        lastTapAt = now
        lastTapLocation = location

        let source = CGEventSource(stateID: .combinedSessionState)
        source?.userData = Self.syntheticTag
        guard
            let down = CGEvent(mouseEventSource: source, mouseType: .leftMouseDown,
                               mouseCursorPosition: location, mouseButton: .left),
            let up = CGEvent(mouseEventSource: source, mouseType: .leftMouseUp,
                             mouseCursorPosition: location, mouseButton: .left)
        else { return }
        down.setIntegerValueField(.mouseEventClickState, value: clickState)
        up.setIntegerValueField(.mouseEventClickState, value: clickState)
        down.post(tap: .cghidEventTap)
        up.post(tap: .cghidEventTap)
        tapsPosted += 1
        log.debug("tap → click (state \(self.clickState)) at \(location.x, format: .fixed(precision: 0)),\(location.y, format: .fixed(precision: 0))")
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

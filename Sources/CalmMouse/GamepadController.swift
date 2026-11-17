import AppKit
import Combine
import CoreGraphics
import GameController
import IOKit.hid
import os
import CalmMouseCore

/// A game controller that is connected right now.
struct ConnectedController: Equatable {
    let name: String
    /// DualSense and DualShock 4: the touchpad moves the pointer.
    let hasTouchpad: Bool
    /// 0...100, nil when the controller doesn't say.
    let battery: Int?
    /// "USB" or "Bluetooth" as the HID device reports it; a 2.4G receiver is a USB device.
    /// nil when it couldn't be matched up.
    var transport: String? = nil
    /// Tells two controllers with the same name apart (one on a cable and one over Bluetooth,
    /// or two of the same model). Every controller shares one set of settings, so this only
    /// keeps the device list in order.
    var ordinal = 0

    var settingsKey: String { ordinal == 0 ? "controller:\(name)" : "controller:\(name)#\(ordinal + 1)" }
    var summary: String { "\(name) touchpad=\(hasTouchpad) transport=\(transport ?? "?")" }
}

/// The game controllers macOS lists as HID devices, whether or not GameController can read them.
///
/// GameController reads PlayStation, Xbox, Switch Pro and MFi controllers and, since macOS 13,
/// most generic HID ones (`GCProductCategoryHID`), over a cable, Bluetooth or a 2.4G receiver
/// alike. This scan never opens a device, so it needs no permission. It says how each controller
/// is connected, and names the ones GameController can't read so `--status` can tell "not
/// connected" from "connected but unsupported". A controller in Xbox 360 (XInput) mode, or on
/// Microsoft's Xbox Wireless Adapter, has no macOS driver and doesn't appear at all.
enum GamepadHID {
    struct Device: Equatable {
        let name: String
        let transport: String
        let supported: Bool
    }

    static func scan() -> [Device] {
        let manager = IOHIDManagerCreate(kCFAllocatorDefault, IOOptionBits(kIOHIDOptionsTypeNone))
        let usages = [kHIDUsage_GD_GamePad, kHIDUsage_GD_Joystick, kHIDUsage_GD_MultiAxisController]
        IOHIDManagerSetDeviceMatchingMultiple(manager, usages.map {
            [kIOHIDDeviceUsagePageKey: kHIDPage_GenericDesktop, kIOHIDDeviceUsageKey: $0] as CFDictionary
        } as CFArray)
        let devices = (IOHIDManagerCopyDevices(manager) as? Set<IOHIDDevice>) ?? []
        return devices.compactMap { device in
            // GameController's own stand-ins for the controllers it already reads.
            if IOHIDDeviceGetProperty(device, "GCSyntheticDevice" as CFString) as? Bool == true { return nil }
            let name = (IOHIDDeviceGetProperty(device, kIOHIDProductKey as CFString) as? String) ?? ""
            let transport = (IOHIDDeviceGetProperty(device, kIOHIDTransportKey as CFString) as? String) ?? ""
            return Device(name: name, transport: transport, supported: GCController.supportsHIDDevice(device))
        }
        .sorted { ($0.name, $0.transport) < ($1.name, $1.transport) }
    }
}

/// Lets a game controller drive the pointer: the touchpad (on PlayStation controllers) and the
/// left stick move it, the right stick and two fingers scroll, ✕/A and ○/B click.
///
/// Reads controllers through Apple's GameController framework with background monitoring on,
/// so input arrives while CalmMouse, a menu-bar app, is never in front. Everything runs on the
/// main queue.
final class GamepadController: ObservableObject {
    static let shared = GamepadController()

    @Published private(set) var controllers: [ConnectedController] = []

    /// Called when the controller asks to be switched on or off (Options + Create held).
    var onToggleRequested: (() -> Void)?

    private let log = Logger(subsystem: "com.calmmouse.app", category: "controller")
    private var engine = ControllerPointer()
    private var observers: [NSObjectProtocol] = []
    private var started = false

    /// Wanted on by the settings, before the game check.
    private var wanted = false
    /// The frontmost app is a game and the settings say to stay out of its way.
    private var pausedForGame = false
    var pauseInGames = true { didSet { applyActive() } }

    private var timer: DispatchSourceTimer?
    private var lastTick: TimeInterval?

    /// Previous pressed state per controller and button: GameController reports every analog
    /// change, and the engine wants edges.
    private var pressed: [ObjectIdentifier: [ControllerButton: Bool]] = [:]
    /// Controllers connected but unreadable, from the last `refreshList`.
    private(set) var unsupported: [GamepadHID.Device] = []

    /// The pointer position as we last put it, kept in fractions of a point so slow motion
    /// isn't rounded away. Dropped whenever something else moved the pointer.
    private var cursor: CGPoint?
    private var scrollCarry = (v: 0.0, h: 0.0)

    // Double-click bookkeeping: macOS doesn't count clicks for synthetic events.
    private var lastPressAt: TimeInterval = -.infinity
    private var lastPressLocation = CGPoint.zero
    private var lastPressButton: PointerButton = .left
    private var clickState: Int64 = 1
    private var heldClickState: [PointerButton: Int64] = [:]

    private(set) var eventsPosted = 0
    /// Reports received from controllers: zero with a controller in hand means input isn't
    /// reaching CalmMouse at all.
    private(set) var inputsReceived = 0
    var isActive: Bool { engine.isActive }

    // MARK: Lifecycle

    /// Starts listening for controllers. Cheap and permission-free; nothing is posted until
    /// `setWanted(true)`.
    func start() {
        guard !started else { return }
        started = true
        GCController.shouldMonitorBackgroundEvents = true
        let center = NotificationCenter.default
        observers.append(center.addObserver(forName: .GCControllerDidConnect, object: nil, queue: .main) { [weak self] note in
            if let controller = note.object as? GCController { self?.attach(controller) }
            self?.refreshList()
        })
        observers.append(center.addObserver(forName: .GCControllerDidDisconnect, object: nil, queue: .main) { [weak self] _ in
            // A controller can drop out with a button held: unplugging the cable to go on over
            // Bluetooth does exactly that. Let go of everything rather than leave it down.
            self?.releaseEverything()
            self?.refreshList()
        })
        observers.append(NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didActivateApplicationNotification, object: nil, queue: .main) { [weak self] _ in
            self?.frontmostAppChanged()
        })
        GCController.controllers().forEach(attach)
        refreshList()
        frontmostAppChanged()
    }

    func setWanted(_ on: Bool) {
        wanted = on
        applyActive()
    }

    func update(config: ControllerConfig) {
        engine.config = config
    }

    /// Re-reads names and battery levels. GameController has no battery notification, so the
    /// menu and the settings window call this when they open.
    func refreshList() {
        let hid = GamepadHID.scan()
        var list = GCController.controllers().compactMap { controller -> ConnectedController? in
            guard controller.extendedGamepad != nil else { return nil }
            let battery = controller.battery.flatMap { b -> Int? in
                b.batteryState == .unknown && b.batteryLevel == 0 ? nil : Int((b.batteryLevel * 100).rounded())
            }
            return ConnectedController(name: controller.vendorName ?? L("Game controller"),
                                       hasTouchpad: Self.touchpad(of: controller) != nil,
                                       battery: battery)
        }
        list = Self.matched(list, with: hid)
        if list != controllers { controllers = list }
        let unreadable = hid.filter { !$0.supported }
        if unreadable != unsupported { unsupported = unreadable }
    }

    /// Gives each controller its connection, from the HID device of the same name, and numbers
    /// controllers that share a name.
    static func matched(_ controllers: [ConnectedController], with hid: [GamepadHID.Device]) -> [ConnectedController] {
        var unclaimed = hid.filter(\.supported)
        var seen: [String: Int] = [:]
        return controllers.map { controller in
            var c = controller
            if let i = unclaimed.firstIndex(where: { $0.name == controller.name }) {
                c.transport = unclaimed.remove(at: i).transport
            }
            c.ordinal = seen[controller.name, default: 0]
            seen[controller.name] = c.ordinal + 1
            return c
        }
    }

    private func applyActive() {
        let active = wanted && !(pauseInGames && pausedForGame)
        perform(engine.setActive(active))
        if !active { stopTimer() }
        cursor = nil
    }

    private func releaseEverything() {
        let wasActive = engine.isActive
        perform(engine.setActive(false))
        pressed = [:]
        stopTimer()
        if wasActive { applyActive() }
    }

    // MARK: Games

    private var gameCache: [String: Bool] = [:]

    private func frontmostAppChanged() {
        let app = NSWorkspace.shared.frontmostApplication
        pausedForGame = app.map(isGame) ?? false
        applyActive()
    }

    /// Whether the app calls itself a game (`LSApplicationCategoryType`). Unity, Unreal and the
    /// App Store all set it for games.
    private func isGame(_ app: NSRunningApplication) -> Bool {
        let key = app.bundleIdentifier ?? app.bundleURL?.path ?? ""
        if let known = gameCache[key] { return known }
        let category = app.bundleURL.flatMap { Bundle(url: $0)?.infoDictionary?["LSApplicationCategoryType"] as? String }
        let game = category.map(ControllerApps.isGameCategory) ?? false
        gameCache[key] = game
        return game
    }

    // MARK: Reading the controller

    private static func touchpad(of controller: GCController) -> (primary: GCControllerDirectionPad,
                                                                  secondary: GCControllerDirectionPad,
                                                                  button: GCControllerButtonInput)? {
        if let pad = controller.extendedGamepad as? GCDualSenseGamepad {
            return (pad.touchpadPrimary, pad.touchpadSecondary, pad.touchpadButton)
        }
        if let pad = controller.extendedGamepad as? GCDualShockGamepad {
            return (pad.touchpadPrimary, pad.touchpadSecondary, pad.touchpadButton)
        }
        return nil
    }

    private func attach(_ controller: GCController) {
        guard let gamepad = controller.extendedGamepad else { return }
        let touchpad = Self.touchpad(of: controller)
        let id = ObjectIdentifier(controller)
        gamepad.valueChangedHandler = { [weak self] gamepad, element in
            self?.changed(gamepad, from: id, element: element, touchpad: touchpad)
        }
        log.info("controller attached: \(controller.vendorName ?? "?", privacy: .public) touchpad=\(touchpad != nil)")
    }

    private func changed(_ gamepad: GCExtendedGamepad, from id: ObjectIdentifier, element: GCControllerElement,
                         touchpad: (primary: GCControllerDirectionPad, secondary: GCControllerDirectionPad,
                                    button: GCControllerButtonInput)?) {
        let t = MachTime.now()
        inputsReceived += 1
        var actions: [PointerAction] = []

        func edge(_ button: ControllerButton, _ input: GCControllerButtonInput?) {
            guard let input else { return }
            let isDown = input.isPressed
            if pressed[id, default: [:]][button, default: false] != isDown {
                pressed[id, default: [:]][button] = isDown
                actions += engine.button(button, pressed: isDown, at: t)
            }
        }
        edge(.south, gamepad.buttonA)
        edge(.east, gamepad.buttonB)
        edge(.menu, gamepad.buttonMenu)
        edge(.options, gamepad.buttonOptions)
        edge(.leftStick, gamepad.leftThumbstickButton)
        edge(.rightStick, gamepad.rightThumbstickButton)
        if let touchpad {
            edge(.touchpad, touchpad.button)
            if element === touchpad.primary || element === touchpad.secondary {
                actions += engine.touchpad(primary: Self.finger(touchpad.primary),
                                           secondary: Self.finger(touchpad.secondary), at: t)
            }
        }
        engine.sticks(left: TouchPoint(x: Double(gamepad.leftThumbstick.xAxis.value), y: Double(gamepad.leftThumbstick.yAxis.value)),
                      right: TouchPoint(x: Double(gamepad.rightThumbstick.xAxis.value), y: Double(gamepad.rightThumbstick.yAxis.value)))
        perform(actions)
        if engine.needsTicks { startTimer() }
    }

    /// GameController puts a lifted finger back at exactly 0, 0. A real finger lands there
    /// only by chance and for a single report, which costs nothing.
    private static func finger(_ pad: GCControllerDirectionPad) -> TouchPoint? {
        let x = Double(pad.xAxis.value), y = Double(pad.yAxis.value)
        return x == 0 && y == 0 ? nil : TouchPoint(x: x, y: y)
    }

    // MARK: Frame clock (only while a stick is pushed or the on/off chord is held)

    private func startTimer() {
        guard timer == nil else { return }
        let timer = DispatchSource.makeTimerSource(queue: .main)
        timer.schedule(deadline: .now(), repeating: .milliseconds(8), leeway: .milliseconds(1))
        timer.setEventHandler { [weak self] in self?.tick() }
        lastTick = nil
        self.timer = timer
        timer.resume()
    }

    private func stopTimer() {
        timer?.cancel()
        timer = nil
        lastTick = nil
    }

    private func tick() {
        let t = MachTime.now()
        let dt = lastTick.map { t - $0 } ?? 0.008
        lastTick = t
        perform(engine.tick(dt: dt, at: t))
        if !engine.needsTicks { stopTimer() }
    }

    // MARK: Posting

    private func perform(_ actions: [PointerAction]) {
        for action in actions {
            switch action {
            case let .move(dx, dy): move(dx: dx, dy: dy)
            case let .scroll(v, h): scroll(vertical: v, horizontal: h)
            case let .press(button): press(button)
            case let .release(button): release(button)
            case let .click(button): press(button); release(button)
            case .toggle:
                onToggleRequested?()
            }
        }
    }

    private func currentLocation() -> CGPoint {
        let actual = CGEvent(source: nil)?.location ?? .zero
        // Something else (a real mouse) moved the pointer: start again from where it is.
        if let cursor, abs(cursor.x - actual.x) < 2, abs(cursor.y - actual.y) < 2 { return cursor }
        return actual
    }

    private func move(dx: Double, dy: Double) {
        let from = currentLocation()
        let to = Self.clampToDisplays(CGPoint(x: from.x + dx, y: from.y + dy), from: from)
        cursor = to
        let type: CGEventType
        let button: CGMouseButton
        if engine.isHeld(.left) { (type, button) = (.leftMouseDragged, .left) }
        else if engine.isHeld(.right) { (type, button) = (.rightMouseDragged, .right) }
        else { (type, button) = (.mouseMoved, .left) }
        guard let event = mouseEvent(type, at: to, button: button) else { return }
        event.setIntegerValueField(.mouseEventDeltaX, value: Int64((to.x - from.x).rounded()))
        event.setIntegerValueField(.mouseEventDeltaY, value: Int64((to.y - from.y).rounded()))
        if let state = heldClickState[button == .right ? .right : .left], type != .mouseMoved {
            event.setIntegerValueField(.mouseEventClickState, value: state)
        }
        event.post(tap: .cghidEventTap)
        eventsPosted += 1
    }

    private func scroll(vertical: Double, horizontal: Double) {
        scrollCarry.v += vertical
        scrollCarry.h += horizontal
        let v = scrollCarry.v.rounded(.towardZero), h = scrollCarry.h.rounded(.towardZero)
        guard v != 0 || h != 0 else { return }
        scrollCarry.v -= v
        scrollCarry.h -= h
        guard let event = CGEvent(scrollWheelEvent2Source: nil, units: .pixel, wheelCount: 2,
                                  wheel1: Int32(clamping: Int(v)), wheel2: Int32(clamping: Int(h)), wheel3: 0) else { return }
        event.setIntegerValueField(.eventSourceUserData, value: WheelSmoothDriver.eventTag)
        // Below CalmMouse's own tap, so the Magic Mouse rules never see it.
        event.post(tap: .cgSessionEventTap)
        eventsPosted += 1
    }

    private func press(_ button: PointerButton) {
        let location = currentLocation()
        let state = escalatedClickState(button, at: location)
        heldClickState[button] = state
        guard let event = mouseEvent(button == .left ? .leftMouseDown : .rightMouseDown, at: location,
                                     button: button == .left ? .left : .right) else { return }
        event.setIntegerValueField(.mouseEventClickState, value: state)
        event.post(tap: .cghidEventTap)
        eventsPosted += 1
    }

    private func release(_ button: PointerButton) {
        let location = currentLocation()
        let state = heldClickState.removeValue(forKey: button) ?? 1
        guard let event = mouseEvent(button == .left ? .leftMouseUp : .rightMouseUp, at: location,
                                     button: button == .left ? .left : .right) else { return }
        event.setIntegerValueField(.mouseEventClickState, value: state)
        event.post(tap: .cghidEventTap)
        eventsPosted += 1
    }

    /// 1, 2, 3 for presses of the same button in the same spot within the double-click time.
    private func escalatedClickState(_ button: PointerButton, at location: CGPoint) -> Int64 {
        let now = ProcessInfo.processInfo.systemUptime
        let near = hypot(location.x - lastPressLocation.x, location.y - lastPressLocation.y) <= 5
        if button == lastPressButton, near, now - lastPressAt <= NSEvent.doubleClickInterval {
            clickState = min(clickState + 1, 3)
        } else {
            clickState = 1
        }
        lastPressAt = now
        lastPressLocation = location
        lastPressButton = button
        return clickState
    }

    private func mouseEvent(_ type: CGEventType, at location: CGPoint, button: CGMouseButton) -> CGEvent? {
        let source = CGEventSource(stateID: .combinedSessionState)
        // A synthetic post would otherwise mute the real mouse for 250 ms.
        source?.localEventsSuppressionInterval = 0
        let event = CGEvent(mouseEventSource: source, mouseType: type, mouseCursorPosition: location, mouseButton: button)
        // CalmMouse's event tap lets events with this tag straight through.
        event?.setIntegerValueField(.eventSourceUserData, value: TapController.syntheticClickTag)
        return event
    }

    /// Keeps the pointer on a screen. Off every screen, it slides along the edge of the one it
    /// was on instead of stopping dead.
    static func clampToDisplays(_ point: CGPoint, from: CGPoint) -> CGPoint {
        var count: UInt32 = 0
        var display = CGDirectDisplayID()
        if CGGetDisplaysWithPoint(point, 1, &display, &count) == .success, count > 0 { return point }
        var bounds: CGRect?
        if CGGetDisplaysWithPoint(from, 1, &display, &count) == .success, count > 0 {
            bounds = CGDisplayBounds(display)
        } else {
            bounds = CGDisplayBounds(CGMainDisplayID())
        }
        guard let b = bounds else { return from }
        return CGPoint(x: min(max(point.x, b.minX), b.maxX - 1), y: min(max(point.y, b.minY), b.maxY - 1))
    }
}

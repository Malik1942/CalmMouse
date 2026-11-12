import Foundation
import IOKit
import CalmMouseCore

/// Sets each mouse's tracking speed on its own, the way a mouse with a DPI button would feel.
///
/// macOS has one tracking speed for every mouse. Per device there is `HIDPointerResolution`: the
/// DPI the system takes the sensor to have, so telling it a lower number makes that mouse faster.
/// It lives on the HID event system's service clients, which have no public header, so the calls
/// are looked up at run time like MultitouchSupport's. The value is lost when a mouse reconnects,
/// which is why a matching callback applies it again.
final class PointerSpeedController {
    private typealias Client = UnsafeMutableRawPointer
    private typealias Service = UnsafeRawPointer
    private typealias MatchCallback = @convention(c) (UnsafeMutableRawPointer?, UnsafeMutableRawPointer?, Service?) -> Void

    private struct API {
        let setMatching: @convention(c) (Client, CFDictionary?) -> Void
        let copyServices: @convention(c) (Client) -> Unmanaged<CFArray>?
        let conformsTo: @convention(c) (Service, UInt32, UInt32) -> Bool
        let copyProperty: @convention(c) (Service, CFString) -> Unmanaged<CFTypeRef>?
        let setProperty: @convention(c) (Service, CFString, CFTypeRef) -> Bool
        let registerMatching: @convention(c) (Client, MatchCallback, UnsafeMutableRawPointer?, UnsafeMutableRawPointer?) -> Void
        let schedule: @convention(c) (Client, DispatchQueue) -> Void
    }

    private static let resolutionKey = "HIDPointerResolution" as CFString
    private static let genericDesktop: UInt32 = 0x01
    private static let mouseUsage: UInt32 = 0x02

    private var api: API?
    private var client: Client?

    /// nil for a mouse kind whose speed has never been chosen: it is left completely alone.
    var magicMouseSpeed: Double?
    var wheelMouseSpeed: Double?
    var mouseType: MouseTypeSetting = .automatic

    /// Called when a mouse appears, after its speed has been applied.
    var onMouseAppeared: (() -> Void)?

    /// What the last `apply` wrote and whether it took, for `--status`.
    private(set) var lastWrites: [String] = []

    init() {
        guard let handle = dlopen("/System/Library/Frameworks/IOKit.framework/IOKit", RTLD_LAZY) else { return }
        func sym<T>(_ name: String, as type: T.Type) -> T? {
            dlsym(handle, name).map { unsafeBitCast($0, to: T.self) }
        }
        guard
            // Not the "simple" client: it can read properties but its writes are refused.
            let create = sym("IOHIDEventSystemClientCreate", as: (@convention(c) (CFAllocator?) -> Client?).self),
            let setMatching = sym("IOHIDEventSystemClientSetMatching", as: (@convention(c) (Client, CFDictionary?) -> Void).self),
            let copyServices = sym("IOHIDEventSystemClientCopyServices", as: (@convention(c) (Client) -> Unmanaged<CFArray>?).self),
            let conformsTo = sym("IOHIDServiceClientConformsTo", as: (@convention(c) (Service, UInt32, UInt32) -> Bool).self),
            let copyProperty = sym("IOHIDServiceClientCopyProperty", as: (@convention(c) (Service, CFString) -> Unmanaged<CFTypeRef>?).self),
            let setProperty = sym("IOHIDServiceClientSetProperty", as: (@convention(c) (Service, CFString, CFTypeRef) -> Bool).self),
            let registerMatching = sym("IOHIDEventSystemClientRegisterDeviceMatchingCallback", as: (@convention(c) (Client, MatchCallback, UnsafeMutableRawPointer?, UnsafeMutableRawPointer?) -> Void).self),
            let schedule = sym("IOHIDEventSystemClientScheduleWithDispatchQueue", as: (@convention(c) (Client, DispatchQueue) -> Void).self),
            let client = create(kCFAllocatorDefault)
        else { return }

        self.api = API(setMatching: setMatching, copyServices: copyServices, conformsTo: conformsTo,
                       copyProperty: copyProperty, setProperty: setProperty,
                       registerMatching: registerMatching, schedule: schedule)
        self.client = client

        let matching = ["DeviceUsagePage": Int(Self.genericDesktop), "DeviceUsage": Int(Self.mouseUsage)] as CFDictionary
        setMatching(client, matching)
        registerMatching(client, { target, _, _ in
            guard let target else { return }
            let controller = Unmanaged<PointerSpeedController>.fromOpaque(target).takeUnretainedValue()
            // A new service is still being set up when this fires.
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) {
                controller.apply()
                controller.onMouseAppeared?()
            }
        }, Unmanaged.passUnretained(self).toOpaque(), nil)
        schedule(client, .main)
    }

    /// The speed for one mouse. It follows the kind the event tap gives this mouse's clicks,
    /// so Mouse type decides here too: the slider on the pane the user can see is the one
    /// that reaches the mouse.
    static func speed(for info: DeviceIdentifier.DeviceInfo, mouseType: MouseTypeSetting,
                      magicMouse: Double?, wheelMouse: Double?) -> Double? {
        let identity = PointerIdentity(isMagicMouse: info.isMagicMouse, isTrackpad: info.isTrackpad,
                                       isMouse: true)
        switch MouseClassifier.kind(of: identity, event: .button, setting: mouseType) {
        case .magicMouse: return magicMouse
        case .wheelMouse: return wheelMouse
        case nil:
            // Left alone under this setting (a Magic Mouse while Mouse type says Scroll Wheel).
            // If its speed was ever changed, it goes back to normal.
            return info.isTrackpad ? nil : magicMouse.map { _ in 0 }
        }
    }

    /// While CalmMouse is paused, a mouse whose speed was changed moves at its normal speed.
    /// One whose speed was never chosen is still never written.
    static func effectiveSpeed(chosen: Double?, enabled: Bool) -> Double? {
        enabled ? chosen : chosen.map { _ in 0 }
    }

    /// Writes the chosen speed to every connected mouse.
    func apply() {
        guard let api, let client,
              let services = api.copyServices(client)?.takeRetainedValue() as? [AnyObject] else { return }
        var writes: [String] = []
        defer { lastWrites = writes }
        for object in services {
            let service = UnsafeRawPointer(Unmanaged.passUnretained(object).toOpaque())
            guard api.conformsTo(service, Self.genericDesktop, Self.mouseUsage) else { continue }
            func property(_ key: String) -> Any? {
                api.copyProperty(service, key as CFString)?.takeRetainedValue()
            }
            let info = DeviceIdentifier.DeviceInfo(
                product: (property("Product") as? String) ?? "",
                vendorID: (property("VendorID") as? Int) ?? 0,
                productID: (property("ProductID") as? Int) ?? 0,
                transport: "",
                builtIn: (property("Built-In") as? Bool) ?? false,
                usages: DeviceIdentifier.usages(fromPairs: property("DeviceUsagePairs")))
            guard !info.isTrackpad, let speed = Self.speed(for: info, mouseType: mouseType,
                                                           magicMouse: magicMouseSpeed,
                                                           wheelMouse: wheelMouseSpeed) else { continue }
            let resolution = TrackingSpeed.resolution(forSpeed: speed)
            let accepted = api.setProperty(service, Self.resolutionKey, resolution as CFNumber)
            writes.append("\(info.product): \(resolution >> 16) DPI \(accepted ? "accepted" : "refused")")
        }
    }
}

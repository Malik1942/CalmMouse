import Foundation
import Combine
import IOKit
import IOKit.hid
import CalmMouseCore

/// A mouse that is connected right now, and what CalmMouse makes of it.
struct ConnectedMouse: Equatable {
    let name: String
    /// nil for a mouse CalmMouse has nothing to do for (no touch surface and no wheel).
    let kind: MouseKind?
    let info: DeviceIdentifier.DeviceInfo

    var summary: String { "\(info.summary) kind=\(kind?.rawValue ?? "none")" }
}

/// Finds the mice connected right now, without waiting for any of them to send an event.
///
/// It asks IOHIDManager for the device list but never opens a device, so no permission is
/// involved. A scroll wheel is sensed from the elements the device declares. Measured on
/// macOS 26: a wheel mouse declares one Generic Desktop / Wheel element, a Magic Mouse and the
/// built-in trackpad none.
enum MouseScanner {
    static func scan() -> [ConnectedMouse] {
        let manager = IOHIDManagerCreate(kCFAllocatorDefault, IOOptionBits(kIOHIDOptionsTypeNone))
        IOHIDManagerSetDeviceMatching(manager, nil)   // everything; filtered below
        let devices = (IOHIDManagerCopyDevices(manager) as? Set<IOHIDDevice>) ?? []

        var result: [ConnectedMouse] = []
        for device in devices {
            guard let name = IOHIDDeviceGetProperty(device, kIOHIDProductKey as CFString) as? String else { continue }
            func int(_ key: String) -> Int { (IOHIDDeviceGetProperty(device, key as CFString) as? Int) ?? 0 }
            var usages = DeviceIdentifier.usages(
                fromPairs: IOHIDDeviceGetProperty(device, kIOHIDDeviceUsagePairsKey as CFString))
            if let page = IOHIDDeviceGetProperty(device, kIOHIDPrimaryUsagePageKey as CFString) as? Int,
               let usage = IOHIDDeviceGetProperty(device, kIOHIDPrimaryUsageKey as CFString) as? Int {
                usages.insert(.init(page: page, usage: usage))
            }
            let info = DeviceIdentifier.DeviceInfo(
                product: name,
                vendorID: int(kIOHIDVendorIDKey),
                productID: int(kIOHIDProductIDKey),
                transport: (IOHIDDeviceGetProperty(device, kIOHIDTransportKey as CFString) as? String) ?? "",
                builtIn: (IOHIDDeviceGetProperty(device, "Built-In" as CFString) as? Bool) ?? false,
                usages: usages)

            let kind: MouseKind?
            if info.isMagicMouse {
                kind = .magicMouse
            } else if info.isTrackpad || !usages.contains(.mouse) {
                continue                      // not a mouse at all
            } else {
                kind = hasWheel(device) ? .wheelMouse : nil
            }
            let mouse = ConnectedMouse(name: name, kind: kind, info: info)
            // One physical mouse often shows up as several HID devices. Keep one entry per
            // name, preferring the one that told us what kind of mouse it is.
            if let i = result.firstIndex(where: { $0.name == name }) {
                if result[i].kind == nil && kind != nil { result[i] = mouse }
            } else {
                result.append(mouse)
            }
        }

        // A Magic Mouse is also findable the way the battery reader finds it, which is the
        // path already proven in the field. Use it as a second opinion.
        if !result.contains(where: { $0.kind == .magicMouse }), let reading = BatteryMonitor.read() {
            result.append(ConnectedMouse(
                name: reading.deviceName, kind: .magicMouse,
                info: .init(product: reading.deviceName, vendorID: 0, productID: 0, transport: "")))
        }
        return result.sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
    }

    /// Whether the device's HID descriptor declares a scroll wheel. Read from the IORegistry
    /// because `IOHIDDeviceCopyMatchingElements` comes back empty for a wheel mouse in an app
    /// that has no Accessibility grant yet, which is exactly the app's first launch. All the
    /// properties are fetched together because asking for `Elements` alone returns only its top
    /// level, and the wheel is nested two collections down.
    private static func hasWheel(_ device: IOHIDDevice) -> Bool {
        var properties: Unmanaged<CFMutableDictionary>?
        guard IORegistryEntryCreateCFProperties(IOHIDDeviceGetService(device), &properties,
                                                kCFAllocatorDefault, 0) == KERN_SUCCESS,
              let all = properties?.takeRetainedValue() as? [String: Any] else { return false }
        return declaresWheel(all[kIOHIDElementKey])
    }

    /// Searches HID's `Elements` tree: an array of dictionaries, each with a usage and possibly
    /// more `Elements` inside it.
    static func declaresWheel(_ elements: Any?) -> Bool {
        guard let elements = elements as? [[String: Any]] else { return false }
        return elements.contains { element in
            let isWheel = element[kIOHIDElementUsagePageKey] as? Int == kHIDPage_GenericDesktop
                && element[kIOHIDElementUsageKey] as? Int == kHIDUsage_GD_Wheel
            return isWheel || declaresWheel(element[kIOHIDElementKey])
        }
    }
}

/// The connected mice, published for the settings window and the menu.
///
/// There is no background polling. `refresh()` runs at launch, when the menu or the settings
/// window opens, every few seconds while the settings window is open, and whenever the event
/// tap meets a device it hasn't seen before.
final class MouseEnvironment: ObservableObject {
    static let shared = MouseEnvironment()

    @Published private(set) var connected: [ConnectedMouse] = []

    var connectedKinds: Set<MouseKind> { Set(connected.compactMap(\.kind)) }

    /// Which kinds the UI should show options for (see `MouseClassifier.kindsToShow`).
    func kindsToShow(for settings: Settings) -> Set<MouseKind> {
        MouseClassifier.kindsToShow(setting: settings.mouseType, connected: connectedKinds,
                                    remembered: settings.rememberedMouseKinds)
    }

    func refresh(settings: Settings = .shared) {
        let found = MouseScanner.scan()
        if found != connected { connected = found }
        let kinds = Set(found.compactMap(\.kind))
        if !kinds.isSubset(of: settings.rememberedMouseKinds) {
            settings.rememberedMouseKinds.formUnion(kinds)
            objectWillChange.send()
        }
    }
}

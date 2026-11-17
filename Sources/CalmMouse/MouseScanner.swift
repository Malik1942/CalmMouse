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
            // One physical mouse often shows up as several HID devices with the same IDs and
            // name. Keep one entry for them, preferring the one that told us what kind it is.
            if let i = result.firstIndex(where: { $0.info.settingsKey == info.settingsKey }) {
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

/// One row of the device switcher in Settings.
struct DeviceEntry: Identifiable, Equatable {
    /// Stands in for a Magic Mouse that is asleep or not connected yet.
    static let magicMouseStandInKey = "magic-mouse"

    let key: String
    let name: String
    let detected: MouseKind?
    /// What it is handled as, after "Treat as". nil: CalmMouse leaves it alone.
    let kind: MouseKind?
    let connected: Bool
    /// How it connects, shown only when another listed device has the same name.
    var detail: String? = nil
    /// Set for a game controller, which is listed after the mice.
    var controller: ConnectedController? = nil
    var id: String { key }
}

extension MouseEnvironment {
    /// The devices Settings lists, in order: Magic Mice, wheel mice by name, then mice
    /// CalmMouse leaves alone. A Magic Mouse drops off Bluetooth whenever it sleeps, so one
    /// that has been seen stays listed; with nothing connected at all it is listed too, since
    /// that is the mouse CalmMouse is mostly for.
    static func deviceList(connected: [ConnectedMouse], typeOverride: (_ key: String, _ name: String) -> MouseTypeSetting,
                           rememberedMagicMouse: Bool) -> [DeviceEntry] {
        let names = connected.map(\.name)
        var entries = connected.map { mouse -> DeviceEntry in
            let key = mouse.info.settingsKey
            let shared = names.filter { $0 == mouse.name }.count > 1
            return DeviceEntry(key: key, name: mouse.name, detected: mouse.kind,
                               kind: MouseClassifier.effectiveKind(detected: mouse.kind,
                                                                   override: typeOverride(key, mouse.name)),
                               connected: true,
                               detail: shared ? connectionName(mouse.info.transport) : nil)
        }
        let hasMagicMouse = entries.contains { $0.kind == .magicMouse || $0.detected == .magicMouse }
        if !hasMagicMouse && (rememberedMagicMouse || entries.isEmpty) {
            entries.append(DeviceEntry(key: DeviceEntry.magicMouseStandInKey, name: L("Magic Mouse"),
                                       detected: .magicMouse, kind: .magicMouse, connected: false))
        }
        func rank(_ entry: DeviceEntry) -> Int {
            switch entry.kind {
            case .magicMouse: return 0
            case .wheelMouse: return 1
            case nil: return 2
            }
        }
        return entries.sorted {
            if rank($0) != rank($1) { return rank($0) < rank($1) }
            let byName = $0.name.localizedCaseInsensitiveCompare($1.name)
            return byName != .orderedSame ? byName == .orderedAscending : $0.key < $1.key
        }
    }

    /// "USB" or "Bluetooth": enough to tell two same-named mice apart.
    static func connectionName(_ transport: String) -> String? {
        if transport.localizedCaseInsensitiveContains("bluetooth") { return L("Bluetooth") }
        return transport.isEmpty ? nil : transport
    }

    func devices(for settings: Settings, controllers: [ConnectedController] = []) -> [DeviceEntry] {
        Self.deviceList(connected: connected, typeOverride: settings.typeOverride(key:name:),
                        rememberedMagicMouse: settings.rememberedMouseKinds.contains(.magicMouse))
            + Self.controllerEntries(controllers)
    }

    /// Game controllers, listed after the mice. How one connects is shown only when another
    /// controller has the same name.
    static func controllerEntries(_ controllers: [ConnectedController]) -> [DeviceEntry] {
        controllers.map { controller in
            let shared = controllers.filter { $0.name == controller.name }.count > 1
            return DeviceEntry(key: controller.settingsKey, name: controller.name, detected: nil, kind: nil,
                               connected: true,
                               detail: shared ? controller.transport.flatMap(connectionName) : nil,
                               controller: controller)
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
    /// Battery level by mouse name, for the mice that report one.
    @Published private(set) var batteries: [String: Int] = [:]

    var connectedKinds: Set<MouseKind> { Set(connected.compactMap(\.kind)) }

    func refresh(settings: Settings = .shared) {
        let found = MouseScanner.scan()
        if found != connected { connected = found }
        let magicMice = BatteryMonitor.readAll()
        var levels: [String: Int] = [:]
        for mouse in found {
            let level = mouse.kind == .magicMouse
                ? BatteryMonitor.percent(of: mouse.name, in: magicMice)
                : MouseBattery.percent(named: mouse.name)
            if let level { levels[mouse.name] = level }
        }
        if levels != batteries { batteries = levels }
        let kinds = Set(found.compactMap(\.kind))
        if !kinds.isSubset(of: settings.rememberedMouseKinds) {
            settings.rememberedMouseKinds.formUnion(kinds)
            objectWillChange.send()
        }
    }
}

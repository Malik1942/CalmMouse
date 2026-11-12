import Foundation
import IOKit
import UserNotifications
import AppKit
import os

/// Polls the Magic Mouse's battery level straight out of the IORegistry
/// (AppleDeviceManagementHIDEventService exposes `BatteryPercent`) and nudges you before it dies.
final class BatteryMonitor {
    struct Reading: Equatable {
        var percent: Int
        var deviceName: String
    }

    private let log = Logger(subsystem: "com.calmmouse.app", category: "battery")
    private var timer: Timer?
    private var warnedAt: Int?          // the threshold we already warned about
    private var notificationsAuthorized = false

    /// The emptiest connected Magic Mouse.
    private(set) var latest: Reading?
    /// Every connected Magic Mouse that reports a level.
    private(set) var readings: [Reading] = []
    var onUpdate: ((Reading?) -> Void)?

    var enabled = true
    var threshold = 15

    func start() {
        requestNotificationAuthorization()
        poll()
        timer?.invalidate()
        // Battery level moves slowly; every 5 minutes is plenty and costs nothing.
        timer = Timer.scheduledTimer(withTimeInterval: 300, repeats: true) { [weak self] _ in
            self?.poll()
        }
    }

    func stop() {
        timer?.invalidate()
        timer = nil
    }

    func poll() {
        let all = Self.readAll()
        let reading = Self.lowest(all)
        let changed = all != readings
        readings = all
        latest = reading
        if changed { onUpdate?(reading) }

        guard enabled, let reading else { return }
        if reading.percent <= threshold {
            if warnedAt != threshold {
                warnedAt = threshold
                notifyLowBattery(reading)
            }
        } else if reading.percent >= threshold + 10 {
            // Recharged (with hysteresis so a wobbling reading doesn't re-arm constantly).
            warnedAt = nil
        }
    }

    // MARK: Reading the level

    /// The emptiest connected Magic Mouse.
    static func read() -> Reading? { lowest(readAll()) }

    static func readAll() -> [Reading] {
        guard let matching = IOServiceMatching("AppleDeviceManagementHIDEventService") else { return [] }
        var iterator: io_iterator_t = 0
        guard IOServiceGetMatchingServices(kIOMainPortDefault, matching, &iterator) == KERN_SUCCESS else { return [] }
        defer { IOObjectRelease(iterator) }

        var result: [Reading] = []
        while case let service = IOIteratorNext(iterator), service != 0 {
            defer { IOObjectRelease(service) }
            func int(_ key: String) -> Int {
                (IORegistryEntryCreateCFProperty(service, key as CFString, kCFAllocatorDefault, 0)?
                    .takeRetainedValue() as? Int) ?? 0
            }
            guard let product = IORegistryEntryCreateCFProperty(service, "Product" as CFString, kCFAllocatorDefault, 0)?
                    .takeRetainedValue() as? String,
                  // Same rule as the event tap: by name or by hardware ID, so a renamed mouse
                  // still gets its battery read.
                  DeviceIdentifier.DeviceInfo(product: product, vendorID: int("VendorID"),
                                              productID: int("ProductID"), transport: "").isMagicMouse
            else { continue }
            guard let percent = IORegistryEntryCreateCFProperty(service, "BatteryPercent" as CFString, kCFAllocatorDefault, 0)?
                    .takeRetainedValue() as? Int else { continue }
            result.append(Reading(percent: percent, deviceName: product))
        }
        return result
    }

    /// The level of the Magic Mouse with this name. With only one Magic Mouse reporting, it is
    /// that one even if its battery service and its HID device spell the name differently.
    static func percent(of name: String, in readings: [Reading]) -> Int? {
        if let match = readings.first(where: { $0.deviceName == name }) { return match.percent }
        return readings.count == 1 ? readings[0].percent : nil
    }

    /// The emptiest Magic Mouse: the one the warning and the menu-bar icon are about.
    static func lowest(_ readings: [Reading]) -> Reading? {
        readings.min { $0.percent < $1.percent }
    }

    // MARK: Notifying

    private func requestNotificationAuthorization() {
        guard Bundle.main.bundleIdentifier != nil else { return }
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { [weak self] granted, error in
            self?.notificationsAuthorized = granted
            if let error { self?.log.error("notification auth failed: \(error.localizedDescription)") }
        }
    }

    private func notifyLowBattery(_ reading: Reading) {
        log.notice("magic mouse battery at \(reading.percent)%")

        if notificationsAuthorized {
            let content = UNMutableNotificationContent()
            content.title = L("Magic Mouse battery low")
            content.body = L("%ld%% left — worth plugging it in before it dies mid-click.", reading.percent)
            content.sound = .default
            let request = UNNotificationRequest(identifier: "calmmouse.battery.\(reading.percent)",
                                                content: content, trigger: nil)
            UNUserNotificationCenter.current().add(request)
        }
        // The menu bar always reflects it, whether or not notifications are permitted.
        NotificationCenter.default.post(name: BatteryMonitor.didWarn, object: reading.percent)
    }

    static let didWarn = Notification.Name("CalmMouseBatteryDidWarn")
}

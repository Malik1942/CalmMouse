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

    private(set) var latest: Reading?
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
        let reading = Self.read()
        let changed = reading != latest
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

    static func read() -> Reading? {
        guard let matching = IOServiceMatching("AppleDeviceManagementHIDEventService") else { return nil }
        var iterator: io_iterator_t = 0
        guard IOServiceGetMatchingServices(kIOMainPortDefault, matching, &iterator) == KERN_SUCCESS else { return nil }
        defer { IOObjectRelease(iterator) }

        var result: Reading?
        while case let service = IOIteratorNext(iterator), service != 0 {
            defer { IOObjectRelease(service) }
            guard let product = IORegistryEntryCreateCFProperty(service, "Product" as CFString, kCFAllocatorDefault, 0)?
                    .takeRetainedValue() as? String,
                  product.lowercased().contains("magic mouse") else { continue }
            guard let percent = IORegistryEntryCreateCFProperty(service, "BatteryPercent" as CFString, kCFAllocatorDefault, 0)?
                    .takeRetainedValue() as? Int else { continue }
            result = Reading(percent: percent, deviceName: product)
            break
        }
        return result
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
            content.title = "Magic Mouse battery low"
            content.body = "\(reading.percent)% left — worth plugging it in before it dies mid-click."
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

import Foundation
import IOKit
import CoreGraphics

/// Resolves the undocumented CGEvent "sender ID" field (87) — an IORegistry entry ID —
/// to a device identity, so we can tell Magic Mouse events apart from trackpad ones.
/// Technique borrowed from Mac Mouse Fix (EventUtility.m), minus the slow IOHIDDeviceCreate.
final class DeviceIdentifier {
    /// CGEventField 87. Not in the public headers; verified against Mac Mouse Fix's constants.
    static let senderIDField = CGEventField(rawValue: 87)!

    /// Apple's vendor ID as reported over Bluetooth (0x004C) and over USB (0x05AC).
    static let appleVendorIDs: Set<Int> = [0x004C, 0x05AC]

    /// Magic Mouse product IDs: 0x030D first generation, 0x0269 Lightning, 0x0323 USB-C.
    /// 0x0269 is confirmed on real hardware. 0x030D and 0x0323 come from the Linux kernel's
    /// hid-ids.h and have not been checked against a device here.
    static let magicMouseProductIDs: Set<Int> = [0x030D, 0x0269, 0x0323]

    struct DeviceInfo: Equatable {
        let product: String
        let vendorID: Int
        let productID: Int
        let transport: String

        /// `Product` is the Bluetooth device name, which macOS generates ("Malik's Magic
        /// Mouse") and the owner can change to anything. So the name alone misses a renamed
        /// mouse; the hardware IDs catch it whatever it is called. Either match is enough.
        var isMagicMouse: Bool {
            if product.lowercased().contains("magic mouse") { return true }
            return DeviceIdentifier.appleVendorIDs.contains(vendorID)
                && DeviceIdentifier.magicMouseProductIDs.contains(productID)
        }

        /// The hardware identity without the name, for `--status`.
        var idSummary: String {
            String(format: "vendor=0x%04X product=0x%04X transport=%@",
                   vendorID, productID, transport.isEmpty ? "unknown" : transport)
        }

        var summary: String { "\(product) (\(idSummary))" }
    }

    private var cache: [UInt64: DeviceInfo?] = [:]

    /// Every distinct device that has sent an event so far, in first-seen order. Surfaced by
    /// `--status` so a "nothing is blocked" report shows what the mouse actually identifies as.
    private(set) var seenDevices: [DeviceInfo] = []
    private static let seenDevicesLimit = 12

    func senderID(of event: CGEvent) -> UInt64 {
        UInt64(bitPattern: event.getIntegerValueField(Self.senderIDField))
    }

    /// nil = synthetic event / unknown device.
    func info(forSenderID id: UInt64) -> DeviceInfo? {
        if id == 0 { return nil }
        if let cached = cache[id] { return cached }
        let resolved = Self.resolve(id)
        cache[id] = resolved
        if let resolved { remember(resolved) }
        return resolved
    }

    /// Runs once per sender ID (results are cached), so this costs nothing per event.
    func remember(_ info: DeviceInfo) {
        guard !seenDevices.contains(info), seenDevices.count < Self.seenDevicesLimit else { return }
        seenDevices.append(info)
    }

    func isMagicMouse(_ event: CGEvent) -> Bool? {
        let id = senderID(of: event)
        if id == 0 { return nil }
        guard let info = info(forSenderID: id) else { return nil }
        return info.isMagicMouse
    }

    func invalidateCache() { cache.removeAll() }

    // MARK: IORegistry walk

    private static func resolve(_ id: UInt64) -> DeviceInfo? {
        guard let matching = IORegistryEntryIDMatching(id) else { return nil }
        var entry = IOServiceGetMatchingService(kIOMainPortDefault, matching) // consumes `matching`
        // Walk up the service plane until we hit an entry that carries HID identity properties.
        var hops = 0
        while entry != 0 && hops < 12 {
            if let product = property(entry, "Product") as? String {
                let info = DeviceInfo(
                    product: product,
                    vendorID: (property(entry, "VendorID") as? Int) ?? 0,
                    productID: (property(entry, "ProductID") as? Int) ?? 0,
                    transport: (property(entry, "Transport") as? String) ?? ""
                )
                IOObjectRelease(entry)
                return info
            }
            var parent: io_registry_entry_t = 0
            let kr = IORegistryEntryGetParentEntry(entry, kIOServicePlane, &parent)
            IOObjectRelease(entry)
            guard kr == KERN_SUCCESS else { return nil }
            entry = parent
            hops += 1
        }
        if entry != 0 { IOObjectRelease(entry) }
        return nil
    }

    private static func property(_ entry: io_registry_entry_t, _ key: String) -> Any? {
        IORegistryEntryCreateCFProperty(entry, key as CFString, kCFAllocatorDefault, 0)?
            .takeRetainedValue()
    }
}

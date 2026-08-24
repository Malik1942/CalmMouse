import Foundation
import IOKit
import CoreGraphics

/// Resolves the undocumented CGEvent "sender ID" field (87) — an IORegistry entry ID —
/// to a device identity, so we can tell Magic Mouse events apart from trackpad ones.
/// Technique borrowed from Mac Mouse Fix (EventUtility.m), minus the slow IOHIDDeviceCreate.
final class DeviceIdentifier {
    /// CGEventField 87. Not in the public headers; verified against Mac Mouse Fix's constants.
    static let senderIDField = CGEventField(rawValue: 87)!

    struct DeviceInfo {
        let product: String
        let vendorID: Int
        let productID: Int
        let transport: String
        var isMagicMouse: Bool {
            product.lowercased().contains("magic mouse")
        }
    }

    private var cache: [UInt64: DeviceInfo?] = [:]

    func senderID(of event: CGEvent) -> UInt64 {
        UInt64(bitPattern: event.getIntegerValueField(Self.senderIDField))
    }

    /// nil = synthetic event / unknown device.
    func info(forSenderID id: UInt64) -> DeviceInfo? {
        if id == 0 { return nil }
        if let cached = cache[id] { return cached }
        let resolved = Self.resolve(id)
        cache[id] = resolved
        return resolved
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

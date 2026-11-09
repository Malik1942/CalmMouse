import Foundation
import IOKit
import CoreGraphics
import CalmMouseCore

/// Resolves the undocumented CGEvent "sender ID" field (87) — an IORegistry entry ID —
/// to a device identity, so we can tell a Magic Mouse, a wheel mouse and a trackpad apart.
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

    /// Magic Trackpad product IDs (first generation, Lightning, USB-C), same source. They
    /// keep a renamed Magic Trackpad from ever being taken for a mouse.
    static let magicTrackpadProductIDs: Set<Int> = [0x030E, 0x0265, 0x0324]

    /// One HID usage a device declares, e.g. Generic Desktop / Mouse.
    struct Usage: Hashable {
        let page: Int
        let usage: Int
        static let mouse = Usage(page: 0x01, usage: 0x02)
        /// Every usage on this page is a touch surface of some kind.
        static let digitizerPage = 0x0D
    }

    struct DeviceInfo: Equatable {
        let product: String
        let vendorID: Int
        let productID: Int
        let transport: String
        /// Part of the Mac itself (the built-in trackpad).
        var builtIn = false
        /// The HID usages the device declares. Empty when the registry didn't say.
        var usages: Set<Usage> = []

        /// `Product` is the Bluetooth device name, which macOS generates ("Malik's Magic
        /// Mouse") and the owner can change to anything. So the name alone misses a renamed
        /// mouse; the hardware IDs catch it whatever it is called. Either match is enough.
        var isMagicMouse: Bool {
            if product.lowercased().contains("magic mouse") { return true }
            return DeviceIdentifier.appleVendorIDs.contains(vendorID)
                && DeviceIdentifier.magicMouseProductIDs.contains(productID)
        }

        /// Any one sign is enough, because a trackpad taken for a mouse would have its
        /// scrolling interfered with: built into the Mac, called a trackpad, a known Magic
        /// Trackpad, or declaring a touch digitizer.
        var isTrackpad: Bool {
            if isMagicMouse { return false }
            if builtIn || product.lowercased().contains("trackpad") { return true }
            if DeviceIdentifier.appleVendorIDs.contains(vendorID)
                && DeviceIdentifier.magicTrackpadProductIDs.contains(productID) { return true }
            return usages.contains { $0.page == Usage.digitizerPage }
        }

        /// Declares itself a mouse. A device that declares nothing at all gets the benefit of
        /// the doubt: trackpads are already ruled out above, and only wheel-style scrolling
        /// is ever acted on.
        var declaresMouse: Bool { usages.isEmpty || usages.contains(.mouse) }

        var identity: PointerIdentity {
            PointerIdentity(isMagicMouse: isMagicMouse, isTrackpad: isTrackpad, isMouse: declaresMouse)
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

    /// Called the first time a device is seen, so whoever tracks connected mice can look again.
    var onNewDevice: (() -> Void)?

    func senderID(of event: CGEvent) -> UInt64 {
        UInt64(bitPattern: event.getIntegerValueField(Self.senderIDField))
    }

    /// nil = synthetic event / unknown device.
    func info(forSenderID id: UInt64) -> DeviceInfo? {
        if id == 0 { return nil }
        if let cached = cache[id] { return cached }
        let resolved = Self.resolve(id)
        cache[id] = resolved
        if let resolved {
            remember(resolved)
            onNewDevice?()
        }
        return resolved
    }

    func isMagicMouse(_ event: CGEvent) -> Bool? {
        let id = senderID(of: event)
        if id == 0 { return nil }
        guard let info = info(forSenderID: id) else { return nil }
        return info.isMagicMouse
    }

    func invalidateCache() { cache.removeAll() }

    /// Runs once per sender ID (results are cached), so this costs nothing per event.
    func remember(_ info: DeviceInfo) {
        guard !seenDevices.contains(info), seenDevices.count < Self.seenDevicesLimit else { return }
        seenDevices.append(info)
    }

    // MARK: IORegistry walk

    /// Finds the device behind a sender ID.
    ///
    /// The sender is the bottom of a short stack of registry entries that all describe the
    /// same device and carry the same `Product`, but not the same properties. Measured on
    /// macOS 26: a Bluetooth wheel mouse has everything on the sender itself, while the
    /// built-in trackpad's sender (`AppleMultitouchDevice`) has the name but zero IDs, with
    /// `Built-In` and the usages only one entry up. So this reads the whole stack: it starts
    /// at the first entry with a `Product` and keeps climbing while the name stays the same.
    private static func resolve(_ id: UInt64) -> DeviceInfo? {
        guard let matching = IORegistryEntryIDMatching(id) else { return nil }
        var entry = IOServiceGetMatchingService(kIOMainPortDefault, matching) // consumes `matching`
        var product: String?
        var vendorID = 0, productID = 0, transport = ""
        var builtIn = false
        var usages = Set<Usage>()

        var hops = 0
        while entry != 0 && hops < 12 {
            let name = property(entry, "Product") as? String
            if let product, name != product { break }       // climbed out of the device's stack
            if let name {
                product = name
                if vendorID == 0, let v = property(entry, "VendorID") as? Int { vendorID = v }
                if productID == 0, let p = property(entry, "ProductID") as? Int { productID = p }
                if transport.isEmpty, let t = property(entry, "Transport") as? String { transport = t }
                if (property(entry, "Built-In") as? Bool) == true { builtIn = true }
                usages.formUnion(Self.usages(of: entry))
            }
            var parent: io_registry_entry_t = 0
            let kr = IORegistryEntryGetParentEntry(entry, kIOServicePlane, &parent)
            IOObjectRelease(entry)
            entry = kr == KERN_SUCCESS ? parent : 0
            hops += 1
        }
        if entry != 0 { IOObjectRelease(entry) }

        guard let product else { return nil }
        return DeviceInfo(product: product, vendorID: vendorID, productID: productID,
                          transport: transport, builtIn: builtIn, usages: usages)
    }

    private static func usages(of entry: io_registry_entry_t) -> Set<Usage> {
        var result = Set<Usage>()
        if let page = property(entry, "PrimaryUsagePage") as? Int,
           let usage = property(entry, "PrimaryUsage") as? Int {
            result.insert(Usage(page: page, usage: usage))
        }
        result.formUnion(usages(fromPairs: property(entry, "DeviceUsagePairs")))
        return result
    }

    /// Decodes HID's `DeviceUsagePairs`: an array of {DeviceUsagePage, DeviceUsage} dictionaries.
    static func usages(fromPairs raw: Any?) -> Set<Usage> {
        guard let pairs = raw as? [[String: Any]] else { return [] }
        return Set(pairs.compactMap { pair in
            guard let page = pair["DeviceUsagePage"] as? Int,
                  let usage = pair["DeviceUsage"] as? Int else { return nil }
            return Usage(page: page, usage: usage)
        })
    }

    private static func property(_ entry: io_registry_entry_t, _ key: String) -> Any? {
        IORegistryEntryCreateCFProperty(entry, key as CFString, kCFAllocatorDefault, 0)?
            .takeRetainedValue()
    }
}

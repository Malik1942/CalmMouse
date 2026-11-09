import Foundation
import IOKit.ps

/// Battery levels of connected accessories, by name, from the same list `pmset -g accps` prints.
/// A Bluetooth mouse that reports its charge appears here even when its driver doesn't publish
/// the `BatteryPercent` that `BatteryMonitor` reads for a Magic Mouse.
enum MouseBattery {
    private typealias CopyByType = @convention(c) (Int32) -> Unmanaged<CFTypeRef>?
    /// The public `IOPSCopyPowerSourcesInfo` lists only the Mac's own battery; accessories need
    /// this call, which has no public header. nil on a macOS that doesn't have it.
    private static let copyByType: CopyByType? = {
        guard let handle = dlopen("/System/Library/Frameworks/IOKit.framework/IOKit", RTLD_LAZY),
              let symbol = dlsym(handle, "IOPSCopyPowerSourcesByType") else { return nil }
        return unsafeBitCast(symbol, to: CopyByType.self)
    }()
    private static let accessories: Int32 = 0x4

    static func percent(named name: String) -> Int? {
        guard let info = copyByType?(accessories)?.takeRetainedValue(),
              let list = IOPSCopyPowerSourcesList(info)?.takeRetainedValue() as? [CFTypeRef] else { return nil }
        for source in list {
            guard let description = IOPSGetPowerSourceDescription(info, source)?.takeUnretainedValue() as? [String: Any],
                  description[kIOPSNameKey] as? String == name,
                  let current = description[kIOPSCurrentCapacityKey] as? Int else { continue }
            return current
        }
        return nil
    }
}

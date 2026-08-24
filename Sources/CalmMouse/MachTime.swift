import Foundation

/// CGEvent.timestamp is mach absolute time (ticks), **not** nanoseconds — on Apple Silicon one
/// tick is 41.67 ns, so treating ticks as nanoseconds makes every duration ~42x too long.
/// (A 200 ms grace window silently became ~8 seconds.) Convert properly.
enum MachTime {
    private static let scale: Double = {
        var info = mach_timebase_info_data_t()
        mach_timebase_info(&info)
        return Double(info.numer) / Double(info.denom) / 1_000_000_000
    }()

    /// Ticks → seconds.
    static func seconds(_ ticks: UInt64) -> TimeInterval {
        Double(ticks) * scale
    }

    static func now() -> TimeInterval {
        seconds(mach_absolute_time())
    }
}

import Foundation

/// Maps the Tracking speed slider to the resolution macOS is told a mouse has.
public enum TrackingSpeed {
    /// What macOS assumes for a mouse that doesn't say (`HIDPointerResolution` is unset).
    public static let defaultDPI: Double = 400
    /// The slider runs from -1 to 1; each end is this many times slower or faster.
    public static let maximumFactor: Double = 4

    /// `speed` -1...1, where 0 is the mouse's normal speed. Returns 16.16 fixed-point DPI. A lower
    /// DPI makes the system move the pointer further per count, so faster means a smaller number.
    public static func resolution(forSpeed speed: Double) -> Int {
        let clamped = min(max(speed, -1), 1)
        let factor = pow(maximumFactor, clamped)
        return Int((defaultDPI / factor * 65536).rounded())
    }
}

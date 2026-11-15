import Foundation

/// What one wheel mouse asks for. Each wheel mouse keeps its own, so a mouse at home and one
/// at the office can scroll differently; a mouse CalmMouse hasn't been told about uses the
/// template in `BlockerConfig`.
public struct WheelSettings: Equatable, Codable, Sendable {
    /// Flip the wheel's direction on its own (macOS has one natural-scrolling switch for all).
    public var reverse: Bool
    /// Swallow wheel ticks while the wheel itself is pressed down as a button.
    public var blockWhilePressed: Bool
    /// Glide between notches instead of jumping (see `WheelSmoother`).
    public var smooth: Bool
    /// How far a notch travels when gliding, as a multiple of the base step.
    public var speed: Double

    public init(reverse: Bool = false, blockWhilePressed: Bool = false, smooth: Bool = false,
                speed: Double = 3) {
        self.reverse = reverse
        self.blockWhilePressed = blockWhilePressed
        self.smooth = smooth
        self.speed = speed
    }
}

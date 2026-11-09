import Foundation

/// Turns the jumps of a scroll wheel into a glide.
///
/// A wheel reports whole notches, and macOS scrolls each one in a single frame, so the page
/// lurches. This keeps the distance still owed on each axis and pays it out a little every
/// frame, fastest at first and easing off, which is what reads as smooth. Pure arithmetic:
/// the app layer supplies the frame clock and posts the result.
public struct WheelSmoother: Equatable, Sendable {
    /// How quickly the owed distance is paid out. After this long, about 63% of it is done;
    /// a longer time glides further and feels softer, a shorter one feels tighter.
    public var timeConstant: TimeInterval
    /// Slowest the glide may crawl, in pixels per second. Easing alone never quite arrives:
    /// the last few pixels would dribble out one at a time for a third of a second.
    public var minimumSpeed: Double

    private var owedX: Double = 0
    private var owedY: Double = 0
    /// Sub-pixel remainder carried between frames, so whole pixels go out and none are lost.
    private var carryX: Double = 0
    private var carryY: Double = 0

    public init(timeConstant: TimeInterval = 0.09, minimumSpeed: Double = 150) {
        self.timeConstant = timeConstant
        self.minimumSpeed = minimumSpeed
    }

    /// One frame's worth of scrolling, in whole pixels.
    public struct Step: Equatable, Sendable {
        public var x: Int
        public var y: Int
        public init(x: Int, y: Int) { self.x = x; self.y = y }
    }

    /// True when nothing is owed: the frame clock can stop.
    public var isIdle: Bool { owedX == 0 && owedY == 0 && carryX == 0 && carryY == 0 }

    /// Distance still to scroll, in pixels.
    public var owed: (x: Double, y: Double) { (owedX + carryX, owedY + carryY) }

    /// Adds a wheel tick's distance. Turning the wheel the other way drops whatever was still
    /// owed in the old direction, so the page turns round at once instead of finishing its
    /// glide first.
    public mutating func add(dx: Double, dy: Double) {
        Self.add(dx, to: &owedX, carry: &carryX)
        Self.add(dy, to: &owedY, carry: &carryY)
    }

    private static func add(_ delta: Double, to owed: inout Double, carry: inout Double) {
        guard delta != 0 else { return }
        if owed != 0, (owed < 0) != (delta < 0) {
            owed = 0
            carry = 0
        }
        owed += delta
    }

    /// Drops everything owed: the page stops where it is.
    public mutating func cancel() {
        owedX = 0; owedY = 0; carryX = 0; carryY = 0
    }

    /// Advances by `dt` seconds and returns the pixels to scroll this frame. Nil when idle.
    /// A frame can come back as zero pixels on both axes while a fraction is still building up.
    public mutating func step(dt: TimeInterval) -> Step? {
        guard !isIdle else { return nil }
        let dt = max(0, min(dt, 0.1))        // a stalled frame clock must not fire one huge jump
        let x = Self.advance(&owedX, carry: &carryX, dt: dt, timeConstant: timeConstant, minimumSpeed: minimumSpeed)
        let y = Self.advance(&owedY, carry: &carryY, dt: dt, timeConstant: timeConstant, minimumSpeed: minimumSpeed)
        return Step(x: x, y: y)
    }

    private static func advance(_ owed: inout Double, carry: inout Double, dt: TimeInterval,
                                timeConstant: TimeInterval, minimumSpeed: Double) -> Int {
        if owed != 0 {
            let sign: Double = owed < 0 ? -1 : 1
            let magnitude = abs(owed)
            let eased = timeConstant > 0 ? magnitude * (1 - exp(-dt / timeConstant)) : magnitude
            let paid = min(magnitude, max(eased, minimumSpeed * dt))
            owed = magnitude - paid < 1e-9 ? 0 : owed - sign * paid
            carry += sign * paid
        }
        guard owed != 0 else {
            // Nothing more is coming: settle the fraction to the nearest pixel and finish.
            let last = carry.rounded()
            carry = 0
            return Int(last)
        }
        let whole = carry.rounded(.towardZero)
        carry -= whole
        return Int(whole)
    }
}

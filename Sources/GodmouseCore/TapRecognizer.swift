import Foundation

// MARK: - Touch model (framework-free so it can be unit-tested)

/// One touch on the Magic Mouse surface, as delivered by a contact frame.
/// Coordinates are normalized to the surface (0...1 on both axes).
public struct TouchSample: Equatable, Sendable {
    public var id: Int
    public var x: Double
    public var y: Double
    /// Contact size as reported by the hardware (≈0.1...2; a resting palm is large, a graze tiny).
    public var size: Double

    public init(id: Int, x: Double, y: Double, size: Double) {
        self.id = id; self.x = x; self.y = y; self.size = size
    }
}

// MARK: - Config

public struct TapConfig: Equatable, Sendable {
    public var enabled: Bool = false

    /// 0 = firm (deliberate taps only) ... 1 = light (very easy to trigger). Everything below
    /// is derived from this unless you set it explicitly.
    public var sensitivity: Double = 0.5 {
        didSet { retune() }
    }

    /// A tap must be shorter than this.
    public var maxDuration: TimeInterval = 0
    /// ...and travel less than this across the surface (normalized units; the whole shell is 1.0).
    public var maxMovement: Double = 0
    /// Contacts that never reach this size are grazes/ghosts, not taps.
    public var minPeakSize: Double = 0.05
    /// No taps this soon after real scrolling — a finger coming off a swipe isn't clicking.
    public var scrollCooldown: TimeInterval = 0
    /// No taps this soon after a physical button release (fingers reposition after a real click).
    public var buttonCooldown: TimeInterval = 0.25
    /// A single scroll event with at least this much point delta counts as "real scrolling".
    /// Calibrated against a raw capture: a tapping finger's own drift produces 2–6 pt per event
    /// (which must NOT veto the tap), a deliberate swipe tens of points.
    public var scrollActivityThreshold: Double = 8

    public init() { retune() }

    public init(sensitivity: Double) {
        self.sensitivity = min(max(sensitivity, 0), 1)
        retune()
    }

    private mutating func retune() {
        let s = min(max(sensitivity, 0), 1)
        maxDuration = 0.10 + 0.20 * s        // 100 ms ... 300 ms
        maxMovement = 0.012 + 0.038 * s      // 1.2% ... 5% of the surface
        scrollCooldown = 0.30 - 0.20 * s     // 300 ms ... 100 ms
    }
}

// MARK: - Recognizer

/// Pure tap detector. Feed it contact frames (plus button/scroll context) in wall-clock order;
/// it returns how many taps completed in each frame. Not thread-safe by design — the caller
/// serializes onto one queue.
public final class TapRecognizer {
    public var config: TapConfig

    private struct Track {
        var startTime: TimeInterval
        var startX: Double
        var startY: Double
        var maxMovement: Double = 0
        var peakSize: Double
        /// Poisoned: shared the surface with another finger, overlapped a physical click or
        /// real scrolling — whatever happens later, this touch can never become a tap.
        var invalidated = false
    }

    private var tracks: [Int: Track] = [:]
    private var buttonIsDown = false
    private var lastButtonUp: TimeInterval = -.infinity
    private var lastScroll: TimeInterval = -.infinity
    public private(set) var tapCount = 0

    /// Why the most recent lifted touch was NOT a tap — observability for live debugging.
    public enum Rejection: String, Sendable {
        case poisoned          // multi-finger, mid-click, or born under scroll/click
        case buttonDown
        case tooLong
        case movedTooMuch
        case tooSmall
        case buttonCooldown
        case scrollCooldown
    }
    public private(set) var lastRejection: Rejection?
    public private(set) var rejectionCounts: [String: Int] = [:]

    public init(config: TapConfig = TapConfig()) {
        self.config = config
    }

    // MARK: Context inputs

    /// Report Magic Mouse physical button transitions. While a button is down (and briefly
    /// after), nothing can tap — and every touch alive during the press is poisoned.
    public func physicalButton(isDown: Bool, at t: TimeInterval) {
        buttonIsDown = isDown
        if isDown {
            for id in tracks.keys { tracks[id]?.invalidated = true }
        } else {
            lastButtonUp = t
        }
    }

    /// Report a Magic Mouse scroll event's point delta. Small deltas are ignored — a tapping
    /// finger always jiggles the surface a little, and that must not veto the tap.
    public func noteScroll(deltaMagnitude: Double, at t: TimeInterval) {
        guard deltaMagnitude >= config.scrollActivityThreshold else { return }
        lastScroll = t
        for id in tracks.keys { tracks[id]?.invalidated = true }
    }

    /// Forget everything (device reconnect, feature toggled).
    public func reset() {
        tracks.removeAll()
        buttonIsDown = false
    }

    // MARK: Frames

    /// Process one contact frame: every touch currently on the surface. Returns the number of
    /// taps that completed with this frame (a tap completes when its finger lifts).
    @discardableResult
    public func handleFrame(_ touches: [TouchSample], at t: TimeInterval) -> Int {
        guard config.enabled else {
            tracks.removeAll()
            return 0
        }

        var seen = Set<Int>()
        let crowded = touches.count > 1

        for touch in touches {
            seen.insert(touch.id)
            if var track = tracks[touch.id] {
                let dx = touch.x - track.startX
                let dy = touch.y - track.startY
                track.maxMovement = max(track.maxMovement, (dx * dx + dy * dy).squareRoot())
                track.peakSize = max(track.peakSize, touch.size)
                if crowded || buttonIsDown { track.invalidated = true }
                tracks[touch.id] = track
            } else {
                tracks[touch.id] = Track(
                    startTime: t,
                    startX: touch.x,
                    startY: touch.y,
                    peakSize: touch.size,
                    // Born under a click, next to another finger, or right after real scrolling:
                    // never a tap. (The scroll check here catches "finger lands to stop coasting".)
                    invalidated: crowded || buttonIsDown || (t - lastScroll) < config.scrollCooldown
                )
            }
        }

        // Touches that vanished this frame have lifted — evaluate them.
        var taps = 0
        for (id, track) in tracks where !seen.contains(id) {
            tracks.removeValue(forKey: id)
            if isTap(track, liftedAt: t) { taps += 1 }
        }
        tapCount += taps
        return taps
    }

    private func isTap(_ track: Track, liftedAt t: TimeInterval) -> Bool {
        if let rejection = rejectionReason(track, liftedAt: t) {
            lastRejection = rejection
            rejectionCounts[rejection.rawValue, default: 0] += 1
            return false
        }
        return true
    }

    private func rejectionReason(_ track: Track, liftedAt t: TimeInterval) -> Rejection? {
        if track.invalidated { return .poisoned }
        if buttonIsDown { return .buttonDown }
        if t - track.startTime > config.maxDuration { return .tooLong }
        if track.maxMovement > config.maxMovement { return .movedTooMuch }
        if track.peakSize < config.minPeakSize { return .tooSmall }
        if t - lastButtonUp < config.buttonCooldown { return .buttonCooldown }
        if t - lastScroll < config.scrollCooldown { return .scrollCooldown }
        return nil
    }
}

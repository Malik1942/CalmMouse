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

/// How a tap becomes a RIGHT click when tap-right-click is on.
public enum RightClickMode: String, Codable, CaseIterable, Sendable {
    /// Taps landing on the right part of the surface right-click — the same place a
    /// physical Magic Mouse right click lives, and the default for exactly that reason.
    case rightSide
    /// A second quick tap in the same spot right-clicks. Chosen explicitly: it takes over
    /// the tap-tap gesture, so double-clicks need a physical press (or slower taps).
    case doubleTap
}

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

    /// Restrict where taps may START. Off = the whole surface taps. On = only the front part,
    /// away from the side edges — where deliberate fingertip taps land, and where gripping
    /// fingers don't. (Applies to the tap's landing point; drags and clicks are unaffected.)
    public var tapZoneEnabled: Bool = false
    /// How deep the zone reaches from the front edge, as a fraction of the surface (0.25...0.75).
    public var tapZoneDepth: Double = 0.5
    /// Side strips excluded on the left and right when the zone is on. Kept narrow: real-world
    /// capture shows middle-finger taps landing at x 0.82–0.98 — hard against the right edge —
    /// so anything wider silently eats legitimate taps.
    public var tapZoneEdgeMargin: Double = 0.04
    /// Hardware orientation: whether the front of the mouse is y == 1 in normalized touch
    /// coordinates. Set by the app layer from a real-device calibration; both cases are tested.
    public var frontIsHighY: Bool = true

    /// Tap-to-right-click: some taps post a right click instead of a left one, per
    /// `rightClickMode`. Off by default; requires `enabled` like everything else here.
    public var tapRightClick: Bool = false
    public var rightClickMode: RightClickMode = .rightSide
    /// Right-side mode: taps at x ≥ this (fraction of the surface from the left) right-click.
    /// 0.6 leaves the centre — where index-finger taps land — as left click, and catches the
    /// middle-finger territory (real captures put those at x 0.82–0.98).
    public var rightSideStart: Double = 0.6
    /// Double-tap mode: the follow-up tap must complete within this of the previous tap.
    /// The app layer sets it to the system double-click interval.
    public var doubleTapWindow: TimeInterval = 0.35
    /// ...and land within this of it (normalized surface units). Same figure as
    /// `dragMaxDistance`, for the same reason: a follow-up lands where the first tap was.
    public var doubleTapMaxDistance: Double = 0.18

    /// Tap-and-drag: tap, then touch again within `dragWindow` and hold — the second touch
    /// presses the button down until it lifts. Off by default.
    public var tapAndDrag: Bool = false
    /// How soon after a tap the follow-up touch must land to start a drag. The app layer sets
    /// this to the system double-click interval.
    public var dragWindow: TimeInterval = 0.35
    /// ...and how close to the tap it must land (normalized surface units). Tight on purpose:
    /// a drag follow-up lands where the tap was; a finger coming back to REST lands wherever.
    public var dragMaxDistance: Double = 0.18
    /// A drag finger that travels this far across the SURFACE isn't dragging — dragging moves
    /// the mouse under a planted finger. It's a scroll swipe: release the button immediately.
    public var dragSwipeCancelDistance: Double = 0.12

    /// Two-finger drag: rest two fingers on the surface together (no tap needed), move the
    /// mouse to drag, lift to drop. Independent of tap-and-drag. Off by default.
    public var twoFingerDrag: Bool = false
    /// The two fingers must land within this of each other — deliberate placement lands as a
    /// pair; incidental grip contacts accumulate one by one.
    public var twoFingerPairWindow: TimeInterval = 0.15
    /// Hold the pair this long and the button presses down (that's the "long press").
    public var twoFingerLongPress: TimeInterval = 0.35

    /// A sustained burst of strong scroll events means the finger is SCROLLING, whatever the
    /// cursor is doing — surface-travel checks go blind when the mouse moves (drift re-anchors),
    /// but a genuine swipe pumps out strong deltas that a planted drag finger never sustains.
    /// When the summed magnitude of strong events inside the window crosses the threshold, an
    /// armed drag disarms and a pressed drag releases immediately.
    public var scrollBurstWindow: TimeInterval = 0.3
    public var scrollBurstCancelThreshold: Double = 80

    /// The long press may not fire while ANY scroll activity (however weak) is this recent —
    /// slow reading-scrolls emit events below the "strong" threshold, and a press landing in
    /// the middle of one blocks it dead.
    public var pressQuietPeriod: TimeInterval = 0.25
    /// An armed pair that hasn't managed to press within this long disarms silently. Without a
    /// TTL, a press deferred by ongoing scrolling would fire out of nowhere seconds later.
    public var pairArmTTL: TimeInterval = 1.5
    /// After any drag ends, no new PAIR may arm for this long — a two-finger drag ends with the
    /// hand settling back onto the shell, and those two landing contacts look exactly like a
    /// deliberate arm. (Single-finger re-arms already require a fresh tap, so no cooldown there.)
    public var pairRearmCooldown: TimeInterval = 0.4

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
/// What a processed frame (or button transition) asks the caller to do.
public enum TapEvent: Equatable, Sendable {
    /// Post a click (down+up) at the cursor.
    case tap
    /// Post a RIGHT click (down+up) at the cursor — tap-to-right-click matched this tap.
    case rightTap
    /// Tap-and-drag: press the button down and keep it down.
    case dragBegan
    /// The drag finger lifted. If the press was already posted, release the button; if the
    /// drag was still only armed, this was a quick second tap — post its click.
    case dragEnded
    /// The drag finger swiped across the surface — the user is scrolling, not dragging.
    /// If pressed, release the button and lift scroll blocking at once (no grace period);
    /// if only armed, do nothing at all — the swipe just scrolls.
    case dragSwipeCancelled
    /// The drag was cancelled by outside events (physical click, disable). Release the button
    /// if it was pressed; never emit a click.
    case dragCancelled
    /// A two-finger long press completed: press the button down NOW (the caller posts the
    /// mouseDown; motion is not required first).
    case dragPressed
}

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
        /// This touch IS the drag: exempt from tap evaluation and from poisoning.
        var isDrag = false
    }

    private var tracks: [Int: Track] = [:]
    private var buttonIsDown = false
    private var lastButtonUp: TimeInterval = -.infinity
    private var lastScroll: TimeInterval = -.infinity
    /// Any scroll activity at all, however weak — the long-press quiet gate.
    private var lastAnyScroll: TimeInterval = -.infinity
    /// When the last drag ended (lift, cancel, or burst) — the pair re-arm cooldown anchor.
    private var lastDragEnded: TimeInterval = -.infinity
    /// Recent strong scroll events (time, magnitude) inside `scrollBurstWindow`.
    private var scrollBurst: [(t: TimeInterval, magnitude: Double)] = []
    public private(set) var tapCount = 0
    /// Touches currently carrying the drag (one for tap-and-drag, two for two-finger drag).
    private var dragIDs: Set<Int> = []
    private var dragIsPair = false
    private var pairArmedAt: TimeInterval = 0
    private var pairPressSent = false
    /// Where/when the last accepted tap happened — the anchor for tap-and-drag.
    private var lastTapAt: TimeInterval = -.infinity
    private var lastTapX: Double = 0
    private var lastTapY: Double = 0

    public var dragActive: Bool { !dragIDs.isEmpty }
    /// True while the active drag arm is a two-finger one. The app layer must NOT apply its
    /// single-finger press-on-cursor-motion / arm-timeout logic to pair arms: the pair presses
    /// on its own long-press clock (`.dragPressed`).
    public var dragArmIsPair: Bool { dragIsPair }

    /// Why the most recent lifted touch was NOT a tap — observability for live debugging.
    public enum Rejection: String, Sendable {
        case poisoned          // multi-finger, mid-click, or born under scroll/click
        case buttonDown
        case tooLong
        case movedTooMuch
        case tooSmall
        case buttonCooldown
        case scrollCooldown
        case outsideZone
    }
    public private(set) var lastRejection: Rejection?
    public private(set) var rejectionCounts: [String: Int] = [:]
    /// Landing point of the most recently rejected touch — turns "outsideZone" from a mystery
    /// into a coordinate you can compare against the zone bounds.
    public private(set) var lastRejectionX: Double = -1
    public private(set) var lastRejectionY: Double = -1

    public init(config: TapConfig = TapConfig()) {
        self.config = config
    }

    // MARK: Context inputs

    /// Report Magic Mouse physical button transitions. While a button is down (and briefly
    /// after), nothing can tap — and every touch alive during the press is poisoned.
    /// A physical press also cancels an active tap-and-drag (returned as `.dragEnded` so the
    /// caller releases the synthetic button before the real one goes down).
    @discardableResult
    public func physicalButton(isDown: Bool, at t: TimeInterval) -> [TapEvent] {
        buttonIsDown = isDown
        var events: [TapEvent] = []
        if isDown {
            if !dragIDs.isEmpty {
                demoteDragTracks()
                lastDragEnded = t
                events.append(.dragCancelled)
            }
            for id in tracks.keys { tracks[id]?.invalidated = true }
        } else {
            lastButtonUp = t
        }
        return events
    }

    /// Report a Magic Mouse scroll event's point delta. Small deltas are ignored — a tapping
    /// finger always jiggles the surface a little, and that must not veto the tap. The drag
    /// touch is exempt: a finger riding the shell while the mouse physically moves can jiggle
    /// past the threshold, and that must not drop what it's dragging.
    @discardableResult
    public func noteScroll(deltaMagnitude: Double, at t: TimeInterval) -> [TapEvent] {
        if deltaMagnitude > 0 { lastAnyScroll = t }
        guard deltaMagnitude >= config.scrollActivityThreshold else { return [] }
        lastScroll = t
        for (id, track) in tracks where !track.isDrag { tracks[id]?.invalidated = true }

        // Scroll-burst drag cancellation: see scrollBurstWindow above.
        scrollBurst.append((t, deltaMagnitude))
        scrollBurst.removeAll { t - $0.t > config.scrollBurstWindow }
        if !dragIDs.isEmpty && scrollBurst.reduce(0, { $0 + $1.magnitude }) >= config.scrollBurstCancelThreshold {
            demoteDragTracks()
            lastDragEnded = t
            scrollBurst.removeAll()
            return [.dragSwipeCancelled]
        }
        return []
    }

    /// Silently drop an armed drag — no event, no click, the touch becomes an ordinary
    /// (poisoned) contact. The app layer calls this when the arm times out: the finger came
    /// back to REST, not to drag.
    public func disarmDrag() {
        guard !dragIDs.isEmpty else { return }
        demoteDragTracks()
    }

    /// Strip drag status from every drag touch: they become ordinary poisoned contacts whose
    /// eventual lifts are inert.
    private func demoteDragTracks() {
        for id in dragIDs {
            tracks[id]?.isDrag = false
            tracks[id]?.invalidated = true
        }
        dragIDs.removeAll()
        dragIsPair = false
    }

    /// Forget everything (device reconnect, feature toggled). If a drag was active the caller
    /// must release the synthetic button first — check `dragActive` before calling.
    public func reset() {
        tracks.removeAll()
        dragIDs.removeAll()
        dragIsPair = false
        buttonIsDown = false
    }

    // MARK: Frames

    /// Process one contact frame: every touch currently on the surface. Returns the events the
    /// caller should act on (taps complete when their finger lifts; a drag begins the moment its
    /// finger lands).
    /// `cursorMoving`: whether the mouse pointer is currently in motion (supplied by the app
    /// layer). While the MOUSE moves, the planted drag finger naturally drifts on the shell —
    /// that drift re-anchors instead of counting toward the swipe-cancel threshold. Only surface
    /// travel while the cursor is STILL reads as "the user is scrolling, not dragging".
    @discardableResult
    public func handleFrame(_ touches: [TouchSample], at t: TimeInterval,
                            cursorMoving: Bool = false) -> [TapEvent] {
        guard config.enabled else {
            let wasDragging = !dragIDs.isEmpty
            tracks.removeAll()
            dragIDs.removeAll()
            return wasDragging ? [.dragCancelled] : []
        }

        var events: [TapEvent] = []
        var seen = Set<Int>()
        let crowded = touches.count > 1

        // Two-finger drag: detect the pair BEFORE the update loop below poisons concurrent
        // touches. Exactly two contacts, landed together, still, deliberate.
        if armTwoFingerPair(touches, at: t) {
            events.append(.dragBegan)
        }
        // The pair's long-press clock: hold it long enough and the button presses down —
        // but never mid-scroll (quiet gate), and never after the arm has grown stale (TTL).
        if dragIsPair && !pairPressSent {
            if t - pairArmedAt > config.pairArmTTL {
                demoteDragTracks() // silent: resting fingers, not a gesture
            } else if t - pairArmedAt >= config.twoFingerLongPress
                        && t - lastAnyScroll >= config.pressQuietPeriod {
                pairPressSent = true
                events.append(.dragPressed)
            }
        }

        for touch in touches {
            seen.insert(touch.id)
            if var track = tracks[touch.id] {
                let dx = touch.x - track.startX
                let dy = touch.y - track.startY
                track.maxMovement = max(track.maxMovement, (dx * dx + dy * dy).squareRoot())
                track.peakSize = max(track.peakSize, touch.size)
                // The drag finger is allowed to share the surface and ride under the palm.
                if !track.isDrag && (crowded || buttonIsDown) { track.invalidated = true }
                tracks[touch.id] = track
                if track.isDrag && dragIDs.contains(touch.id) {
                    if cursorMoving {
                        // The mouse is moving: shell drift is expected. Re-anchor so it never
                        // accumulates into a phantom swipe mid-drag.
                        tracks[touch.id]?.startX = touch.x
                        tracks[touch.id]?.startY = touch.y
                    } else {
                        // Cursor still + finger travelling = the user is scrolling, not dragging.
                        let dx = touch.x - track.startX
                        let dy = touch.y - track.startY
                        if (dx * dx + dy * dy).squareRoot() > config.dragSwipeCancelDistance {
                            demoteDragTracks() // either finger sweeping ends the whole drag
                            lastDragEnded = t
                            events.append(.dragSwipeCancelled)
                        }
                    }
                }
            } else if beginsDrag(touch, at: t) {
                dragIDs = [touch.id]
                dragIsPair = false
                tracks[touch.id] = Track(startTime: t, startX: touch.x, startY: touch.y,
                                         peakSize: touch.size, isDrag: true)
                events.append(.dragBegan)
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
        for (id, track) in tracks where !seen.contains(id) {
            tracks.removeValue(forKey: id)
            if track.isDrag {
                if dragIDs.contains(id) {
                    // Either finger lifting ends the drag. A lapsed single-finger arm was a
                    // quick second tap (click it); a lapsed two-finger arm is just a
                    // two-finger tap — not a left-click gesture, stay silent.
                    let wasPair = dragIsPair
                    demoteDragTracks()
                    lastDragEnded = t
                    events.append(wasPair ? .dragCancelled : .dragEnded)
                }
            } else if isTap(track, liftedAt: t) {
                tapCount += 1
                let kind = tapKind(track, at: t)
                if kind == .rightTap {
                    // A right tap anchors nothing: it must not arm tap-and-drag, and — like a
                    // real right click — it breaks any tap chain (no left-right-left "double").
                    lastTapAt = -.infinity
                } else {
                    lastTapAt = t
                    lastTapX = track.startX
                    lastTapY = track.startY
                }
                events.append(kind)
            }
        }
        return events
    }

    /// Two fingers arm a drag when they land as a deliberate pair: together in time, both
    /// still so far, surface otherwise quiet, and inside the tap zone when one is set.
    private func armTwoFingerPair(_ touches: [TouchSample], at t: TimeInterval) -> Bool {
        guard config.twoFingerDrag, dragIDs.isEmpty, !buttonIsDown else { return false }
        guard touches.count == 2 else { return false }
        guard t - lastScroll >= config.scrollCooldown else { return false }
        guard t - lastDragEnded >= config.pairRearmCooldown else { return false }

        for touch in touches {
            if config.tapZoneEnabled && !inTapZone(x: touch.x, y: touch.y) { return false }
            if let track = tracks[touch.id] {
                // An existing contact may join a pair only if it's fresh, clean and still —
                // a finger that's been resting (or swiping) is grip, not gesture.
                if track.invalidated { return false }
                if t - track.startTime > config.twoFingerPairWindow { return false }
                if track.maxMovement > 0.05 { return false }
            }
        }

        dragIsPair = true
        pairArmedAt = t
        pairPressSent = false
        for touch in touches {
            dragIDs.insert(touch.id)
            if tracks[touch.id] != nil {
                tracks[touch.id]?.isDrag = true
            } else {
                tracks[touch.id] = Track(startTime: t, startX: touch.x, startY: touch.y,
                                         peakSize: touch.size, isDrag: true)
            }
        }
        return true
    }

    /// A fresh touch starts a drag when it follows a tap closely in time and place, with the
    /// surface otherwise quiet. (One drag at a time; a physical press blocks it outright.)
    private func beginsDrag(_ touch: TouchSample, at t: TimeInterval) -> Bool {
        guard config.tapAndDrag, dragIDs.isEmpty, !buttonIsDown else { return false }
        guard t - lastTapAt <= config.dragWindow else { return false }
        guard t - lastScroll >= config.scrollCooldown else { return false }
        // With the tap zone on, drags may only start where taps do — a finger coming back to
        // rest outside the zone (the classic tap-then-rest rhythm) must never arm a drag.
        if config.tapZoneEnabled && !inTapZone(x: touch.x, y: touch.y) { return false }
        let dx = touch.x - lastTapX
        let dy = touch.y - lastTapY
        return (dx * dx + dy * dy).squareRoot() <= config.dragMaxDistance
    }

    /// An ACCEPTED tap is a left or a right click, decided here. Runs before the tap anchor
    /// updates, so in double-tap mode `lastTap*` still describes the PREVIOUS tap.
    ///
    /// Double-tap mode with tap-and-drag on never reaches this for the follow-up: that touch
    /// arms a drag instead, and its quick lift surfaces as an unpressed `.dragEnded` — the
    /// caller maps that to a right click when this mode is active (the arm gates — window and
    /// landing distance — already match the double-tap ones).
    private func tapKind(_ track: Track, at t: TimeInterval) -> TapEvent {
        guard config.tapRightClick else { return .tap }
        switch config.rightClickMode {
        case .rightSide:
            return onRightSide(x: track.startX) ? .rightTap : .tap
        case .doubleTap:
            let dx = track.startX - lastTapX
            let dy = track.startY - lastTapY
            let isFollowUp = t - lastTapAt <= config.doubleTapWindow
                && (dx * dx + dy * dy).squareRoot() <= config.doubleTapMaxDistance
            return isFollowUp ? .rightTap : .tap
        }
    }

    /// `frontIsHighY` is a 180° hardware-orientation calibration, so when it flips, left and
    /// right swap along with front and back.
    private func onRightSide(x: Double) -> Bool {
        config.frontIsHighY ? x >= config.rightSideStart : x <= 1 - config.rightSideStart
    }

    private func isTap(_ track: Track, liftedAt t: TimeInterval) -> Bool {
        if let rejection = rejectionReason(track, liftedAt: t) {
            lastRejection = rejection
            rejectionCounts[rejection.rawValue, default: 0] += 1
            lastRejectionX = track.startX
            lastRejectionY = track.startY
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
        if config.tapZoneEnabled && !inTapZone(x: track.startX, y: track.startY) { return .outsideZone }
        return nil
    }

    /// Zone test on the tap's landing point.
    private func inTapZone(x: Double, y: Double) -> Bool {
        let distanceFromFront = config.frontIsHighY ? (1 - y) : y
        if distanceFromFront > config.tapZoneDepth { return false }
        if x < config.tapZoneEdgeMargin || x > 1 - config.tapZoneEdgeMargin { return false }
        return true
    }
}

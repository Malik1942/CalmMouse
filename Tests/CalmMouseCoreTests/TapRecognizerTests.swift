import XCTest
@testable import CalmMouseCore

private extension TapRecognizer {
    /// Compatibility shim for assertions that only care about click count.
    func taps(_ touches: [TouchSample], at t: TimeInterval) -> Int {
        handleFrame(touches, at: t).filter { $0 == .tap }.count
    }
}

final class TapRecognizerTests: XCTestCase {

    private func recognizer(sensitivity: Double = 0.5) -> TapRecognizer {
        var c = TapConfig(sensitivity: sensitivity)
        c.enabled = true
        return TapRecognizer(config: c)
    }

    private func touch(_ id: Int, x: Double = 0.5, y: Double = 0.5, size: Double = 0.6) -> TouchSample {
        TouchSample(id: id, x: x, y: y, size: size)
    }

    // MARK: The basic tap

    func testCleanTapClicks() {
        let r = recognizer()
        XCTAssertEqual(r.taps([touch(1)], at: 0), 0)
        XCTAssertEqual(r.taps([touch(1)], at: 0.05), 0)
        XCTAssertEqual(r.taps([], at: 0.09), 1)
    }

    func testTwoSequentialTapsClickTwice() {
        let r = recognizer()
        r.taps([touch(1)], at: 0)
        XCTAssertEqual(r.taps([], at: 0.08), 1)
        r.taps([touch(2)], at: 0.25)
        XCTAssertEqual(r.taps([], at: 0.33), 1)
        XCTAssertEqual(r.tapCount, 2)
    }

    func testDisabledDoesNothing() {
        let r = recognizer()
        r.config.enabled = false
        r.taps([touch(1)], at: 0)
        XCTAssertEqual(r.taps([], at: 0.08), 0)
    }

    // MARK: Accidental-click prevention

    func testRestingFingerNeverClicks() {
        let r = recognizer()
        var t = 0.0
        r.taps([touch(1)], at: t)
        while t < 2.0 { t += 0.1; r.taps([touch(1)], at: t) }
        XCTAssertEqual(r.taps([], at: t + 0.05), 0, "a long rest is not a tap")
    }

    func testSwipeAcrossSurfaceNeverClicks() {
        let r = recognizer()
        r.taps([touch(1, x: 0.3, y: 0.2)], at: 0)
        r.taps([touch(1, x: 0.32, y: 0.45)], at: 0.05)
        XCTAssertEqual(r.taps([], at: 0.09), 0, "moved way past the movement threshold")
    }

    func testTwoFingersNeverClick() {
        let r = recognizer()
        r.taps([touch(1), touch(2, x: 0.7)], at: 0)
        XCTAssertEqual(r.taps([], at: 0.08), 0)
    }

    func testSecondFingerLandingMidTouchPoisonsTheFirst() {
        let r = recognizer()
        r.taps([touch(1)], at: 0)
        r.taps([touch(1), touch(2, x: 0.8)], at: 0.03)
        r.taps([touch(1)], at: 0.05)
        XCTAssertEqual(r.taps([], at: 0.08), 0)
    }

    func testGhostGrazeBelowMinimumSizeIsIgnored() {
        let r = recognizer()
        r.taps([touch(1, size: 0.02)], at: 0)
        XCTAssertEqual(r.taps([], at: 0.06), 0)
    }

    // MARK: Physical-click interplay

    func testTouchDuringPhysicalClickNeverTaps() {
        let r = recognizer()
        r.physicalButton(isDown: true, at: 0)
        r.taps([touch(1)], at: 0.01)          // the finger doing the clicking
        r.physicalButton(isDown: false, at: 0.06)
        XCTAssertEqual(r.taps([], at: 0.09), 0)
    }

    func testTapRightAfterPhysicalClickIsSuppressed() {
        let r = recognizer()
        r.physicalButton(isDown: true, at: 0)
        r.physicalButton(isDown: false, at: 0.1)
        r.taps([touch(1)], at: 0.15)          // repositioning finger, not a click
        XCTAssertEqual(r.taps([], at: 0.2), 0)
        // Well after the cooldown: taps work again.
        r.taps([touch(2)], at: 0.6)
        XCTAssertEqual(r.taps([], at: 0.66), 1)
    }

    func testButtonPressMidTouchPoisonsIt() {
        let r = recognizer()
        r.taps([touch(1)], at: 0)
        r.physicalButton(isDown: true, at: 0.02)     // the "tap" turned into a real click
        r.physicalButton(isDown: false, at: 0.05)
        XCTAssertEqual(r.taps([], at: 0.07), 0)
    }

    // MARK: Scroll interplay

    func testTapDuringScrollingIsSuppressed() {
        let r = recognizer()
        r.taps([touch(1)], at: 0)
        r.noteScroll(deltaMagnitude: 12, at: 0.02)   // real scrolling while the touch is alive
        XCTAssertEqual(r.taps([], at: 0.05), 0)
    }

    func testTapRightAfterScrollingIsSuppressedButLaterTapWorks() {
        let r = recognizer(sensitivity: 0.5)         // scrollCooldown = 200 ms
        r.noteScroll(deltaMagnitude: 20, at: 1.0)
        r.taps([touch(1)], at: 1.05)          // finger lands to stop the coast
        XCTAssertEqual(r.taps([], at: 1.12), 0)
        r.taps([touch(2)], at: 1.5)
        XCTAssertEqual(r.taps([], at: 1.57), 1)
    }

    func testMicroJitterScrollDoesNotVetoTheTap() {
        let r = recognizer()
        r.taps([touch(1)], at: 0)
        r.noteScroll(deltaMagnitude: 1.5, at: 0.02)  // sub-threshold: the tap's own jiggle
        XCTAssertEqual(r.taps([], at: 0.06), 1)
    }

    // MARK: Sensitivity

    func testFirmSettingRejectsWhatLightAccepts() {
        // 220 ms touch with a little wobble: fine on light, rejected on firm.
        let firm = recognizer(sensitivity: 0)
        firm.taps([touch(1, x: 0.50)], at: 0)
        firm.taps([touch(1, x: 0.52)], at: 0.1)
        XCTAssertEqual(firm.taps([], at: 0.22), 0)

        let light = recognizer(sensitivity: 1)
        light.taps([touch(1, x: 0.50)], at: 0)
        light.taps([touch(1, x: 0.52)], at: 0.1)
        XCTAssertEqual(light.taps([], at: 0.22), 1)
    }

    func testSensitivityTuningIsMonotonic() {
        let firm = TapConfig(sensitivity: 0)
        let mid = TapConfig(sensitivity: 0.5)
        let light = TapConfig(sensitivity: 1)
        XCTAssertLessThan(firm.maxDuration, mid.maxDuration)
        XCTAssertLessThan(mid.maxDuration, light.maxDuration)
        XCTAssertLessThan(firm.maxMovement, light.maxMovement)
        XCTAssertGreaterThan(firm.scrollCooldown, light.scrollCooldown)
    }
}

extension TapRecognizerTests {
    /// Replays a real tap captured from a Magic Mouse on macOS 26 (raw MultitouchSupport frames,
    /// 2026-08-20): 15 ms frame cadence, ~150 ms of contact, ~2.6% surface drift — and the tap's
    /// own drift emitting small scroll events the whole time. This exact trace produced zero
    /// clicks before the state-band + scroll-threshold fixes.
    func testRealCapturedTapFromMacOS26Clicks() {
        var c = TapConfig(sensitivity: 0.5)
        c.enabled = true
        let r = TapRecognizer(config: c)

        // (t, x, y, size) — touch id 7 from the capture, contact states only (2...5 band).
        let frames: [(Double, Double, Double, Double)] = [
            (111.032, 0.854, 0.671, 0.375),
            (111.047, 0.862, 0.674, 0.500),
            (111.062, 0.864, 0.679, 0.625),
            (111.077, 0.868, 0.681, 0.500),
            (111.092, 0.865, 0.685, 0.500),
            (111.107, 0.864, 0.689, 0.625),
            (111.122, 0.866, 0.690, 0.875),
            (111.137, 0.868, 0.692, 0.875), // state 3
            (111.152, 0.867, 0.693, 0.750), // state 4
            (111.167, 0.864, 0.692, 0.500), // state 5
        ]
        var taps = 0
        for (t, x, y, size) in frames {
            // The drift scrolls the page a little on every frame — sub-threshold deltas.
            r.noteScroll(deltaMagnitude: 4, at: t)
            taps += r.taps([TouchSample(id: 7, x: x, y: y, size: size)], at: t)
        }
        // States 6/7 are lingering/gone: the touch vanishes from contact frames.
        taps += r.taps([], at: 111.182)
        XCTAssertEqual(taps, 1, "the captured tap must produce exactly one click")
    }

    /// The same capture's firm press-and-hold (17+ frames of sustained contact) must not click.
    func testRealCapturedPressAndHoldDoesNotClick() {
        var c = TapConfig(sensitivity: 1)   // even at the lightest setting
        c.enabled = true
        let r = TapRecognizer(config: c)
        var t = 111.707
        var taps = r.taps([TouchSample(id: 3, x: 0.37, y: 0.76, size: 0.5)], at: t)
        for _ in 0..<28 {                    // ≈ 420 ms of contact
            t += 0.015
            taps += r.taps([TouchSample(id: 3, x: 0.336, y: 0.775, size: 1.0)], at: t)
        }
        taps += r.taps([], at: t + 0.015)
        XCTAssertEqual(taps, 0)
    }
}

// MARK: - Tap and drag

final class TapAndDragTests: XCTestCase {

    private func recognizer() -> TapRecognizer {
        var c = TapConfig(sensitivity: 0.5)
        c.enabled = true
        c.tapAndDrag = true
        c.dragWindow = 0.5
        return TapRecognizer(config: c)
    }

    private func touch(_ id: Int, x: Double = 0.5, y: Double = 0.5, size: Double = 0.6) -> TouchSample {
        TouchSample(id: id, x: x, y: y, size: size)
    }

    /// Complete one clean tap ending at time `t`.
    private func tap(_ r: TapRecognizer, id: Int, endingAt t: TimeInterval) {
        XCTAssertEqual(r.handleFrame([touch(id)], at: t - 0.08), [])
        XCTAssertEqual(r.handleFrame([], at: t), [.tap])
    }

    func testTapThenTouchAndHoldDrags() {
        let r = recognizer()
        tap(r, id: 1, endingAt: 1.0)
        // Second touch lands within the window: drag begins immediately.
        XCTAssertEqual(r.handleFrame([touch(2)], at: 1.2), [.dragBegan])
        XCTAssertTrue(r.dragActive)
        // It can live arbitrarily long, and the planted finger may jiggle a little while the
        // mouse moves under it. (Wandering FAR is a scroll swipe and cancels — tested separately.)
        XCTAssertEqual(r.handleFrame([touch(2, x: 0.55, y: 0.46)], at: 3.0), [])
        // Lift = drop. Never evaluated as a tap, however short or long it was.
        XCTAssertEqual(r.handleFrame([], at: 4.0), [.dragEnded])
        XCTAssertFalse(r.dragActive)
    }

    func testQuickSecondTapStillClicksThroughDragPath() {
        // tap-tap fast: the second touch becomes down...up, which the controller posts with
        // escalated click state — that's what makes it a double-click.
        let r = recognizer()
        tap(r, id: 1, endingAt: 1.0)
        XCTAssertEqual(r.handleFrame([touch(2)], at: 1.1), [.dragBegan])
        XCTAssertEqual(r.handleFrame([], at: 1.18), [.dragEnded])
    }

    func testLateSecondTouchIsJustATap() {
        let r = recognizer()
        tap(r, id: 1, endingAt: 1.0)
        XCTAssertEqual(r.handleFrame([touch(2)], at: 1.8), [], "outside the drag window")
        XCTAssertEqual(r.handleFrame([], at: 1.88), [.tap])
    }

    func testFarawaySecondTouchIsJustATap() {
        let r = recognizer()
        tap(r, id: 1, endingAt: 1.0)
        XCTAssertEqual(r.handleFrame([touch(2, x: 0.05, y: 0.95)], at: 1.1), [],
                       "other end of the surface — not a drag follow-up")
        XCTAssertEqual(r.handleFrame([], at: 1.18), [.tap])
    }

    func testDisabledSettingNeverDrags() {
        let r = recognizer()
        r.config.tapAndDrag = false
        tap(r, id: 1, endingAt: 1.0)
        XCTAssertEqual(r.handleFrame([touch(2)], at: 1.1), [])
        XCTAssertEqual(r.handleFrame([], at: 1.18), [.tap], "still a plain tap")
    }

    func testPhysicalClickCancelsTheDrag() {
        let r = recognizer()
        tap(r, id: 1, endingAt: 1.0)
        XCTAssertEqual(r.handleFrame([touch(2)], at: 1.2), [.dragBegan])
        XCTAssertEqual(r.physicalButton(isDown: true, at: 1.5), [.dragCancelled],
                       "release the synthetic button (if pressed) before the real press — never a click")
        XCTAssertFalse(r.dragActive)
        r.physicalButton(isDown: false, at: 1.6)
        XCTAssertEqual(r.handleFrame([], at: 1.7), [], "lift of the ex-drag finger is inert")
    }

    func testSecondFingerDuringDragDoesNotDropIt() {
        let r = recognizer()
        tap(r, id: 1, endingAt: 1.0)
        XCTAssertEqual(r.handleFrame([touch(2)], at: 1.2), [.dragBegan])
        XCTAssertEqual(r.handleFrame([touch(2), touch(3, x: 0.8)], at: 1.5), [])
        XCTAssertTrue(r.dragActive)
        // The bystander finger lifts: nothing. The drag finger lifts: drop.
        XCTAssertEqual(r.handleFrame([touch(2)], at: 1.9), [])
        XCTAssertEqual(r.handleFrame([], at: 2.2), [.dragEnded])
    }

    func testScrollJiggleDuringDragDoesNotDropIt() {
        let r = recognizer()
        tap(r, id: 1, endingAt: 1.0)
        XCTAssertEqual(r.handleFrame([touch(2)], at: 1.2), [.dragBegan])
        r.noteScroll(deltaMagnitude: 25, at: 1.5)  // shell jiggle while the mouse moves
        XCTAssertEqual(r.handleFrame([touch(2)], at: 1.6), [])
        XCTAssertEqual(r.handleFrame([], at: 2.0), [.dragEnded])
    }

    func testDisablingMidDragReleasesTheButton() {
        let r = recognizer()
        tap(r, id: 1, endingAt: 1.0)
        XCTAssertEqual(r.handleFrame([touch(2)], at: 1.2), [.dragBegan])
        r.config.enabled = false
        XCTAssertEqual(r.handleFrame([touch(2)], at: 1.3), [.dragCancelled],
                       "a held synthetic button must never leak past a disable")
    }
}

// MARK: - Tap zone

final class TapZoneTests: XCTestCase {

    private func recognizer(frontIsHighY: Bool = true) -> TapRecognizer {
        var c = TapConfig(sensitivity: 0.5)
        c.enabled = true
        c.tapZoneEnabled = true
        c.tapZoneDepth = 0.5
        c.tapZoneEdgeMargin = 0.12
        c.frontIsHighY = frontIsHighY
        return TapRecognizer(config: c)
    }

    private func tapResult(_ r: TapRecognizer, x: Double, y: Double) -> Int {
        let taps = r.handleFrame([TouchSample(id: 9, x: x, y: y, size: 0.6)], at: 100)
            .filter { $0 == .tap }.count
        return taps + r.handleFrame([], at: 100.08).filter { $0 == .tap }.count
    }

    func testFrontCenterTaps() {
        XCTAssertEqual(tapResult(recognizer(), x: 0.5, y: 0.9), 1)
    }

    func testBackHalfDoesNotTap() {
        XCTAssertEqual(tapResult(recognizer(), x: 0.5, y: 0.2), 0)
    }

    func testSideEdgesDoNotTap() {
        XCTAssertEqual(tapResult(recognizer(), x: 0.05, y: 0.9), 0, "left edge (thumb territory)")
        XCTAssertEqual(tapResult(recognizer(), x: 0.95, y: 0.9), 0, "right edge (ring finger)")
    }

    func testOrientationFlipMirrorsTheZone() {
        XCTAssertEqual(tapResult(recognizer(frontIsHighY: false), x: 0.5, y: 0.1), 1)
        XCTAssertEqual(tapResult(recognizer(frontIsHighY: false), x: 0.5, y: 0.9), 0)
    }

    func testZoneOffMeansWholeSurface() {
        let r = recognizer()
        r.config.tapZoneEnabled = false
        XCTAssertEqual(tapResult(r, x: 0.05, y: 0.1), 1)
    }

    func testDepthSliderWidensTheZone() {
        let r = recognizer()
        r.config.tapZoneDepth = 0.75
        XCTAssertEqual(tapResult(r, x: 0.5, y: 0.3), 1, "y=0.3 is 0.7 from the front — inside 0.75")
    }

    func testZoneDoesNotBlockDragFollowUp() {
        // The drag follow-up touch may land outside the zone; only the TAP is zone-gated.
        let r = recognizer()
        r.config.tapAndDrag = true
        r.config.dragWindow = 0.5
        r.config.dragMaxDistance = 0.5
        XCTAssertEqual(r.handleFrame([TouchSample(id: 1, x: 0.5, y: 0.9, size: 0.6)], at: 1.0), [])
        XCTAssertEqual(r.handleFrame([], at: 1.08), [.tap])
        XCTAssertEqual(r.handleFrame([TouchSample(id: 2, x: 0.5, y: 0.55, size: 0.6)], at: 1.2), [.dragBegan])
        XCTAssertEqual(r.handleFrame([], at: 2.0), [.dragEnded])
    }
}

// MARK: - Drag swipe-cancel (the "tap-then-rest locks scrolling" fix)

final class DragSwipeCancelTests: XCTestCase {

    private func recognizer() -> TapRecognizer {
        var c = TapConfig(sensitivity: 0.5)
        c.enabled = true
        c.tapAndDrag = true
        c.dragWindow = 0.5
        c.dragSwipeCancelDistance = 0.12
        return TapRecognizer(config: c)
    }

    private func touch(_ id: Int, x: Double = 0.5, y: Double = 0.5) -> TouchSample {
        TouchSample(id: id, x: x, y: y, size: 0.6)
    }

    private func tapThenRest(_ r: TapRecognizer) {
        XCTAssertEqual(r.handleFrame([touch(1)], at: 1.0), [])
        XCTAssertEqual(r.handleFrame([], at: 1.08), [.tap])
        XCTAssertEqual(r.handleFrame([touch(2)], at: 1.2), [.dragBegan], "resting finger arms a drag")
    }

    func testSwipingTheRestingFingerCancelsTheDragImmediately() {
        let r = recognizer()
        tapThenRest(r)
        // The user swipes to scroll: the finger travels across the surface.
        XCTAssertEqual(r.handleFrame([touch(2, x: 0.5, y: 0.42)], at: 1.4), [])
        XCTAssertEqual(r.handleFrame([touch(2, x: 0.5, y: 0.35)], at: 1.45), [.dragSwipeCancelled])
        XCTAssertFalse(r.dragActive)
        // The rest of the swipe is inert for tapping — and its lift is not a tap.
        XCTAssertEqual(r.handleFrame([touch(2, x: 0.5, y: 0.2)], at: 1.5), [])
        XCTAssertEqual(r.handleFrame([], at: 1.6), [])
    }

    func testPlantedFingerJiggleDoesNotCancelTheDrag() {
        let r = recognizer()
        tapThenRest(r)
        // Dragging = moving the mouse; the planted finger only wobbles a few percent.
        for i in 0..<40 {
            let wobble = Double(i % 2) * 0.03
            XCTAssertEqual(r.handleFrame([touch(2, x: 0.5 + wobble, y: 0.5 - wobble)],
                                         at: 1.3 + Double(i) * 0.015), [])
        }
        XCTAssertTrue(r.dragActive, "a real drag survives shell jiggle")
        XCTAssertEqual(r.handleFrame([], at: 2.0), [.dragEnded])
    }

    func testZoneGatedDragEntry() {
        var c = TapConfig(sensitivity: 0.5)
        c.enabled = true
        c.tapAndDrag = true
        c.dragWindow = 0.5
        c.tapZoneEnabled = true
        c.tapZoneDepth = 0.5
        c.frontIsHighY = true
        let r = TapRecognizer(config: c)
        // Tap in the front zone...
        XCTAssertEqual(r.handleFrame([touch(1, y: 0.9)], at: 1.0), [])
        XCTAssertEqual(r.handleFrame([], at: 1.08), [.tap])
        // ...then the finger comes back to REST behind the zone: must NOT arm a drag.
        XCTAssertEqual(r.handleFrame([touch(2, y: 0.4)], at: 1.2), [])
        XCTAssertFalse(r.dragActive)
        // Its scroll swipe is just a swipe.
        XCTAssertEqual(r.handleFrame([touch(2, y: 0.2)], at: 1.4), [])
        XCTAssertEqual(r.handleFrame([], at: 1.5), [])
    }
}

// MARK: - Deferred-press drag mechanics

final class DeferredDragTests: XCTestCase {

    private func recognizer() -> TapRecognizer {
        var c = TapConfig(sensitivity: 0.5)
        c.enabled = true
        c.tapAndDrag = true
        c.dragWindow = 0.5
        c.dragSwipeCancelDistance = 0.12
        return TapRecognizer(config: c)
    }

    private func touch(_ id: Int, x: Double = 0.5, y: Double = 0.5) -> TouchSample {
        TouchSample(id: id, x: x, y: y, size: 0.6)
    }

    private func armDrag(_ r: TapRecognizer) {
        XCTAssertEqual(r.handleFrame([touch(1)], at: 1.0), [])
        XCTAssertEqual(r.handleFrame([], at: 1.08), [.tap])
        XCTAssertEqual(r.handleFrame([touch(2)], at: 1.2), [.dragBegan])
    }

    func testShellDriftWhileCursorMovesNeverCancels() {
        let r = recognizer()
        armDrag(r)
        // A long window drag: the mouse moves and the planted finger drifts a LOT cumulatively —
        // far past the swipe threshold — but each frame re-anchors while the cursor moves.
        var y = 0.5
        for i in 0..<60 {
            y -= 0.01   // 0.6 of the surface over the whole drag
            XCTAssertEqual(r.handleFrame([touch(2, y: y)], at: 1.3 + Double(i) * 0.015,
                                         cursorMoving: true), [])
        }
        XCTAssertTrue(r.dragActive, "re-anchoring must absorb drift during real mouse motion")
        XCTAssertEqual(r.handleFrame([], at: 2.5), [.dragEnded])
    }

    func testSurfaceSweepWithCursorStillCancels() {
        let r = recognizer()
        armDrag(r)
        XCTAssertEqual(r.handleFrame([touch(2, y: 0.42)], at: 1.4, cursorMoving: false), [])
        XCTAssertEqual(r.handleFrame([touch(2, y: 0.34)], at: 1.45, cursorMoving: false),
                       [.dragSwipeCancelled])
    }

    func testDriftAnchorResetsWhenCursorStops() {
        let r = recognizer()
        armDrag(r)
        // Drift far while dragging (cursor moving) — anchor follows.
        for i in 0..<20 {
            _ = r.handleFrame([touch(2, y: 0.5 - Double(i) * 0.01)], at: 1.3 + Double(i) * 0.015,
                              cursorMoving: true)
        }
        // Cursor stops at y≈0.31; a small settle must NOT cancel...
        XCTAssertEqual(r.handleFrame([touch(2, y: 0.29)], at: 1.7, cursorMoving: false), [])
        // ...but a real sweep from the new anchor still does.
        XCTAssertEqual(r.handleFrame([touch(2, y: 0.15)], at: 1.75, cursorMoving: false),
                       [.dragSwipeCancelled])
    }

    func testDisarmIsCompletelySilent() {
        let r = recognizer()
        armDrag(r)
        r.disarmDrag()
        XCTAssertFalse(r.dragActive)
        // The ex-drag finger can sit, swipe, lift — nothing comes out.
        XCTAssertEqual(r.handleFrame([touch(2, y: 0.3)], at: 1.5), [])
        XCTAssertEqual(r.handleFrame([], at: 2.0), [])
    }

    func testDisarmedTouchCannotRearmItself() {
        let r = recognizer()
        armDrag(r)
        r.disarmDrag()
        // Still within the drag window of the original tap — but this touch already lost its
        // drag status and must not win it back on later frames.
        XCTAssertEqual(r.handleFrame([touch(2)], at: 1.3), [])
        XCTAssertFalse(r.dragActive)
    }
}

// MARK: - Two-finger drag (long press)

final class TwoFingerDragTests: XCTestCase {

    private func recognizer(zone: Bool = false) -> TapRecognizer {
        var c = TapConfig(sensitivity: 0.5)
        c.enabled = true
        c.twoFingerDrag = true
        c.twoFingerPairWindow = 0.15
        c.twoFingerLongPress = 0.35
        if zone {
            c.tapZoneEnabled = true
            c.tapZoneDepth = 0.5
            c.frontIsHighY = true
        }
        return TapRecognizer(config: c)
    }

    private func touch(_ id: Int, x: Double = 0.5, y: Double = 0.75) -> TouchSample {
        TouchSample(id: id, x: x, y: y, size: 0.6)
    }

    private func pair(_ y: Double = 0.75) -> [TouchSample] {
        [touch(1, x: 0.42, y: y), touch(2, x: 0.58, y: y)]
    }

    func testPairArmsThenLongPressPressesThenLiftDrops() {
        let r = recognizer()
        XCTAssertEqual(r.handleFrame(pair(), at: 1.0), [.dragBegan])
        XCTAssertTrue(r.dragArmIsPair)
        // Holding still: nothing until the long-press clock fires.
        XCTAssertEqual(r.handleFrame(pair(), at: 1.2), [])
        XCTAssertEqual(r.handleFrame(pair(), at: 1.4), [.dragPressed])
        // Held drag survives arbitrarily long.
        XCTAssertEqual(r.handleFrame(pair(), at: 3.0), [])
        // Lifting (either finger — here both) drops. Never a click.
        XCTAssertEqual(r.handleFrame([], at: 3.5), [.dragCancelled])
        XCTAssertFalse(r.dragActive)
    }

    func testQuickTwoFingerTapIsCompletelySilent() {
        let r = recognizer()
        XCTAssertEqual(r.handleFrame(pair(), at: 1.0), [.dragBegan])
        // Lifted before the long press: no click, no press, nothing.
        XCTAssertEqual(r.handleFrame([], at: 1.15), [.dragCancelled])
    }

    func testStaggeredFingersNeverArm() {
        let r = recognizer()
        XCTAssertEqual(r.handleFrame([touch(1)], at: 1.0), [])
        // Second finger arrives 0.3 s later — that's a grip, not a gesture.
        XCTAssertEqual(r.handleFrame([touch(1), touch(2, x: 0.7)], at: 1.3), [])
        XCTAssertFalse(r.dragActive)
        XCTAssertEqual(r.handleFrame([], at: 2.0), [], "poisoned rest, no taps either")
    }

    func testOneFingerLiftingEndsThePairDrag() {
        let r = recognizer()
        XCTAssertEqual(r.handleFrame(pair(), at: 1.0), [.dragBegan])
        XCTAssertEqual(r.handleFrame(pair(), at: 1.4), [.dragPressed])
        XCTAssertEqual(r.handleFrame([touch(1, x: 0.42)], at: 2.0), [.dragCancelled])
        XCTAssertFalse(r.dragActive)
        XCTAssertEqual(r.handleFrame([], at: 2.5), [], "the survivor's lift is inert")
    }

    func testPairSwipeWhileCursorStillCancelsSilently() {
        let r = recognizer()
        XCTAssertEqual(r.handleFrame(pair(), at: 1.0), [.dragBegan])
        // Both fingers sweep down (a scroll motion) before the press.
        XCTAssertEqual(r.handleFrame([touch(1, x: 0.42, y: 0.55), touch(2, x: 0.58, y: 0.55)],
                                     at: 1.1), [.dragSwipeCancelled])
        XCTAssertFalse(r.dragActive)
    }

    func testDragDriftWithCursorMovingSurvivesForPairs() {
        let r = recognizer()
        XCTAssertEqual(r.handleFrame(pair(), at: 1.0), [.dragBegan])
        XCTAssertEqual(r.handleFrame(pair(), at: 1.4), [.dragPressed])
        var y = 0.75
        for i in 0..<40 {
            y -= 0.01
            XCTAssertEqual(r.handleFrame([touch(1, x: 0.42, y: y), touch(2, x: 0.58, y: y)],
                                         at: 1.5 + Double(i) * 0.015, cursorMoving: true), [])
        }
        XCTAssertTrue(r.dragActive)
        XCTAssertEqual(r.handleFrame([], at: 2.5), [.dragCancelled])
    }

    func testSettingOffMeansPoisonAsBefore() {
        let r = recognizer()
        r.config.twoFingerDrag = false
        XCTAssertEqual(r.handleFrame(pair(), at: 1.0), [])
        XCTAssertEqual(r.handleFrame([], at: 1.1), [], "two fingers stay a poisoned non-gesture")
    }

    func testZoneGatesThePair() {
        let r = recognizer(zone: true)
        XCTAssertEqual(r.handleFrame(pair(0.3), at: 1.0), [], "behind the front zone: no arm")
        XCTAssertFalse(r.dragActive)
        XCTAssertEqual(r.handleFrame([], at: 1.1), [])
        XCTAssertEqual(r.handleFrame(pair(0.8), at: 2.0), [.dragBegan], "in the zone: arms")
    }

    func testPhysicalClickCancelsPairDrag() {
        let r = recognizer()
        XCTAssertEqual(r.handleFrame(pair(), at: 1.0), [.dragBegan])
        XCTAssertEqual(r.handleFrame(pair(), at: 1.4), [.dragPressed])
        XCTAssertEqual(r.physicalButton(isDown: true, at: 1.6), [.dragCancelled])
        XCTAssertFalse(r.dragActive)
    }
}

import XCTest
@testable import GodmouseCore

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
        XCTAssertEqual(r.handleFrame([touch(1)], at: 0), 0)
        XCTAssertEqual(r.handleFrame([touch(1)], at: 0.05), 0)
        XCTAssertEqual(r.handleFrame([], at: 0.09), 1)
    }

    func testTwoSequentialTapsClickTwice() {
        let r = recognizer()
        r.handleFrame([touch(1)], at: 0)
        XCTAssertEqual(r.handleFrame([], at: 0.08), 1)
        r.handleFrame([touch(2)], at: 0.25)
        XCTAssertEqual(r.handleFrame([], at: 0.33), 1)
        XCTAssertEqual(r.tapCount, 2)
    }

    func testDisabledDoesNothing() {
        let r = recognizer()
        r.config.enabled = false
        r.handleFrame([touch(1)], at: 0)
        XCTAssertEqual(r.handleFrame([], at: 0.08), 0)
    }

    // MARK: Accidental-click prevention

    func testRestingFingerNeverClicks() {
        let r = recognizer()
        var t = 0.0
        r.handleFrame([touch(1)], at: t)
        while t < 2.0 { t += 0.1; r.handleFrame([touch(1)], at: t) }
        XCTAssertEqual(r.handleFrame([], at: t + 0.05), 0, "a long rest is not a tap")
    }

    func testSwipeAcrossSurfaceNeverClicks() {
        let r = recognizer()
        r.handleFrame([touch(1, x: 0.3, y: 0.2)], at: 0)
        r.handleFrame([touch(1, x: 0.32, y: 0.45)], at: 0.05)
        XCTAssertEqual(r.handleFrame([], at: 0.09), 0, "moved way past the movement threshold")
    }

    func testTwoFingersNeverClick() {
        let r = recognizer()
        r.handleFrame([touch(1), touch(2, x: 0.7)], at: 0)
        XCTAssertEqual(r.handleFrame([], at: 0.08), 0)
    }

    func testSecondFingerLandingMidTouchPoisonsTheFirst() {
        let r = recognizer()
        r.handleFrame([touch(1)], at: 0)
        r.handleFrame([touch(1), touch(2, x: 0.8)], at: 0.03)
        r.handleFrame([touch(1)], at: 0.05)
        XCTAssertEqual(r.handleFrame([], at: 0.08), 0)
    }

    func testGhostGrazeBelowMinimumSizeIsIgnored() {
        let r = recognizer()
        r.handleFrame([touch(1, size: 0.02)], at: 0)
        XCTAssertEqual(r.handleFrame([], at: 0.06), 0)
    }

    // MARK: Physical-click interplay

    func testTouchDuringPhysicalClickNeverTaps() {
        let r = recognizer()
        r.physicalButton(isDown: true, at: 0)
        r.handleFrame([touch(1)], at: 0.01)          // the finger doing the clicking
        r.physicalButton(isDown: false, at: 0.06)
        XCTAssertEqual(r.handleFrame([], at: 0.09), 0)
    }

    func testTapRightAfterPhysicalClickIsSuppressed() {
        let r = recognizer()
        r.physicalButton(isDown: true, at: 0)
        r.physicalButton(isDown: false, at: 0.1)
        r.handleFrame([touch(1)], at: 0.15)          // repositioning finger, not a click
        XCTAssertEqual(r.handleFrame([], at: 0.2), 0)
        // Well after the cooldown: taps work again.
        r.handleFrame([touch(2)], at: 0.6)
        XCTAssertEqual(r.handleFrame([], at: 0.66), 1)
    }

    func testButtonPressMidTouchPoisonsIt() {
        let r = recognizer()
        r.handleFrame([touch(1)], at: 0)
        r.physicalButton(isDown: true, at: 0.02)     // the "tap" turned into a real click
        r.physicalButton(isDown: false, at: 0.05)
        XCTAssertEqual(r.handleFrame([], at: 0.07), 0)
    }

    // MARK: Scroll interplay

    func testTapDuringScrollingIsSuppressed() {
        let r = recognizer()
        r.handleFrame([touch(1)], at: 0)
        r.noteScroll(deltaMagnitude: 12, at: 0.02)   // real scrolling while the touch is alive
        XCTAssertEqual(r.handleFrame([], at: 0.05), 0)
    }

    func testTapRightAfterScrollingIsSuppressedButLaterTapWorks() {
        let r = recognizer(sensitivity: 0.5)         // scrollCooldown = 200 ms
        r.noteScroll(deltaMagnitude: 20, at: 1.0)
        r.handleFrame([touch(1)], at: 1.05)          // finger lands to stop the coast
        XCTAssertEqual(r.handleFrame([], at: 1.12), 0)
        r.handleFrame([touch(2)], at: 1.5)
        XCTAssertEqual(r.handleFrame([], at: 1.57), 1)
    }

    func testMicroJitterScrollDoesNotVetoTheTap() {
        let r = recognizer()
        r.handleFrame([touch(1)], at: 0)
        r.noteScroll(deltaMagnitude: 1.5, at: 0.02)  // sub-threshold: the tap's own jiggle
        XCTAssertEqual(r.handleFrame([], at: 0.06), 1)
    }

    // MARK: Sensitivity

    func testFirmSettingRejectsWhatLightAccepts() {
        // 220 ms touch with a little wobble: fine on light, rejected on firm.
        let firm = recognizer(sensitivity: 0)
        firm.handleFrame([touch(1, x: 0.50)], at: 0)
        firm.handleFrame([touch(1, x: 0.52)], at: 0.1)
        XCTAssertEqual(firm.handleFrame([], at: 0.22), 0)

        let light = recognizer(sensitivity: 1)
        light.handleFrame([touch(1, x: 0.50)], at: 0)
        light.handleFrame([touch(1, x: 0.52)], at: 0.1)
        XCTAssertEqual(light.handleFrame([], at: 0.22), 1)
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
            taps += r.handleFrame([TouchSample(id: 7, x: x, y: y, size: size)], at: t)
        }
        // States 6/7 are lingering/gone: the touch vanishes from contact frames.
        taps += r.handleFrame([], at: 111.182)
        XCTAssertEqual(taps, 1, "the captured tap must produce exactly one click")
    }

    /// The same capture's firm press-and-hold (17+ frames of sustained contact) must not click.
    func testRealCapturedPressAndHoldDoesNotClick() {
        var c = TapConfig(sensitivity: 1)   // even at the lightest setting
        c.enabled = true
        let r = TapRecognizer(config: c)
        var t = 111.707
        var taps = r.handleFrame([TouchSample(id: 3, x: 0.37, y: 0.76, size: 0.5)], at: t)
        for _ in 0..<28 {                    // ≈ 420 ms of contact
            t += 0.015
            taps += r.handleFrame([TouchSample(id: 3, x: 0.336, y: 0.775, size: 1.0)], at: t)
        }
        taps += r.handleFrame([], at: t + 0.015)
        XCTAssertEqual(taps, 0)
    }
}

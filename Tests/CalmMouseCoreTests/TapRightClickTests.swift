import XCTest
@testable import CalmMouseCore

/// Tap-to-right-click, in both modes.
final class TapRightClickTests: XCTestCase {

    private func recognizer(mode: RightClickMode = .rightSide,
                            enabled: Bool = true,
                            frontIsHighY: Bool = true) -> TapRecognizer {
        var c = TapConfig(sensitivity: 0.5)
        c.enabled = true
        c.tapRightClick = enabled
        c.rightClickMode = mode
        c.frontIsHighY = frontIsHighY
        c.doubleTapWindow = 0.35
        return TapRecognizer(config: c)
    }

    private func touch(_ id: Int, x: Double = 0.5, y: Double = 0.5) -> TouchSample {
        TouchSample(id: id, x: x, y: y, size: 0.6)
    }

    /// Land and lift one clean tap at (x, y), lift completing at `t`; returns the events of the lift.
    private func tap(_ r: TapRecognizer, id: Int, x: Double, y: Double = 0.5,
                     endingAt t: TimeInterval) -> [TapEvent] {
        XCTAssertEqual(r.handleFrame([touch(id, x: x, y: y)], at: t - 0.08), [])
        return r.handleFrame([], at: t)
    }

    // MARK: The default

    /// Pins the product decision: right-side is the default mode, because it mirrors where the
    /// physical Magic Mouse right click already lives and steals no existing gesture.
    func testRightSideIsTheDefaultMode() {
        XCTAssertEqual(TapConfig().rightClickMode, .rightSide)
        XCTAssertFalse(TapConfig().tapRightClick, "and the feature itself ships off")
    }

    // MARK: Right-side mode

    func testTapOnTheRightSideRightClicks() {
        let r = recognizer()
        XCTAssertEqual(tap(r, id: 1, x: 0.85, endingAt: 1.0), [.rightTap])
    }

    func testTapAtTheCentreAndLeftStaysALeftClick() {
        let r = recognizer()
        XCTAssertEqual(tap(r, id: 1, x: 0.5, endingAt: 1.0), [.tap])
        XCTAssertEqual(tap(r, id: 2, x: 0.1, endingAt: 2.0), [.tap])
    }

    func testFeatureOffMeansEveryTapIsALeftClick() {
        let r = recognizer(enabled: false)
        XCTAssertEqual(tap(r, id: 1, x: 0.9, endingAt: 1.0), [.tap])
    }

    func testOrientationFlipMirrorsTheRightSide() {
        // frontIsHighY == false is a 180° rotation: the right side lands at LOW x.
        let r = recognizer(frontIsHighY: false)
        XCTAssertEqual(tap(r, id: 1, x: 0.1, endingAt: 1.0), [.rightTap])
        XCTAssertEqual(tap(r, id: 2, x: 0.9, endingAt: 2.0), [.tap])
    }

    func testTapZoneStillGatesRightTaps() {
        let r = recognizer()
        r.config.tapZoneEnabled = true
        r.config.tapZoneDepth = 0.5
        r.config.tapZoneEdgeMargin = 0.04
        // Right side but BEHIND the front zone: rejected outright, no click of either kind.
        XCTAssertEqual(tap(r, id: 1, x: 0.8, y: 0.2, endingAt: 1.0), [])
        // Right side inside the zone: right click.
        XCTAssertEqual(tap(r, id: 2, x: 0.8, y: 0.9, endingAt: 2.0), [.rightTap])
    }

    func testRightTapNeverArmsTapAndDrag() {
        let r = recognizer()
        r.config.tapAndDrag = true
        r.config.dragWindow = 0.5
        XCTAssertEqual(tap(r, id: 1, x: 0.85, endingAt: 1.0), [.rightTap])
        // A follow-up touch right after a RIGHT tap is not a drag arm — drags are left-drags.
        XCTAssertEqual(r.handleFrame([touch(2, x: 0.85)], at: 1.1), [])
        XCTAssertFalse(r.dragActive)
        XCTAssertEqual(r.handleFrame([], at: 1.18), [.rightTap], "it's just another right tap")
    }

    func testLeftTapStillArmsTapAndDragInRightSideMode() {
        let r = recognizer()
        r.config.tapAndDrag = true
        r.config.dragWindow = 0.5
        XCTAssertEqual(tap(r, id: 1, x: 0.4, endingAt: 1.0), [.tap])
        XCTAssertEqual(r.handleFrame([touch(2, x: 0.4)], at: 1.2), [.dragBegan])
    }

    // MARK: Double-tap mode

    func testQuickSecondTapInTheSameSpotRightClicks() {
        let r = recognizer(mode: .doubleTap)
        XCTAssertEqual(tap(r, id: 1, x: 0.5, endingAt: 1.0), [.tap])
        XCTAssertEqual(tap(r, id: 2, x: 0.52, endingAt: 1.25), [.rightTap])
    }

    func testSlowSecondTapIsJustAnotherLeftClick() {
        let r = recognizer(mode: .doubleTap)
        XCTAssertEqual(tap(r, id: 1, x: 0.5, endingAt: 1.0), [.tap])
        XCTAssertEqual(tap(r, id: 2, x: 0.5, endingAt: 1.6), [.tap], "outside the double-tap window")
    }

    func testFarawaySecondTapIsJustAnotherLeftClick() {
        let r = recognizer(mode: .doubleTap)
        XCTAssertEqual(tap(r, id: 1, x: 0.2, endingAt: 1.0), [.tap])
        XCTAssertEqual(tap(r, id: 2, x: 0.8, endingAt: 1.2), [.tap], "other end of the surface")
    }

    func testThirdQuickTapStartsAFreshChain() {
        // tap-tap-tap = left, right, left — the right tap resets the chain, so it can't
        // alternate into an accidental second right click.
        let r = recognizer(mode: .doubleTap)
        XCTAssertEqual(tap(r, id: 1, x: 0.5, endingAt: 1.0), [.tap])
        XCTAssertEqual(tap(r, id: 2, x: 0.5, endingAt: 1.25), [.rightTap])
        XCTAssertEqual(tap(r, id: 3, x: 0.5, endingAt: 1.5), [.tap])
    }

    func testRightSidePlacementIsIrrelevantInDoubleTapMode() {
        let r = recognizer(mode: .doubleTap)
        XCTAssertEqual(tap(r, id: 1, x: 0.9, endingAt: 1.0), [.tap],
                       "a lone tap on the right side stays a left click in this mode")
    }

    /// With tap-and-drag on, the quick follow-up touch arms a drag instead of completing as a
    /// tap — its unpressed lift surfaces as `.dragEnded`, and the app layer maps that to a
    /// right click while double-tap mode is active. This pins the recognizer half of that
    /// contract: the arm still happens, and the lift is still an unpressed `.dragEnded`.
    func testDoubleTapWithTapAndDragRoutesThroughTheDragPath() {
        let r = recognizer(mode: .doubleTap)
        r.config.tapAndDrag = true
        r.config.dragWindow = 0.35
        XCTAssertEqual(tap(r, id: 1, x: 0.5, endingAt: 1.0), [.tap])
        XCTAssertEqual(r.handleFrame([touch(2)], at: 1.1), [.dragBegan])
        XCTAssertEqual(r.handleFrame([], at: 1.18), [.dragEnded])
    }
}

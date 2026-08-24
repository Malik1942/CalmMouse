import XCTest
@testable import CalmMouseCore

final class ScrollBlockerTests: XCTestCase {

    private func ev(_ t: TimeInterval, _ phase: ScrollPhase = .none, momentum: MomentumPhase = .none,
                    dx: Double = 0, dy: Double = 3, magic: Bool = true) -> ScrollEvent {
        ScrollEvent(timestamp: t, fromMagicMouse: magic, isContinuous: true,
                    phase: phase, momentum: momentum, deltaX: dx, deltaY: dy)
    }

    private let endedZero = ScrollDecision.rewrite(.init(phase: .ended, momentum: .none, zeroX: true, zeroY: true))
    private let momentumEndedZero = ScrollDecision.rewrite(.init(phase: .none, momentum: .ended, zeroX: true, zeroY: true))

    // MARK: Baseline

    func testScrollPassesWhenNoButtonIsDown() {
        let b = ScrollBlocker()
        XCTAssertEqual(b.handleScroll(ev(0, .began)), .pass)
        XCTAssertEqual(b.handleScroll(ev(0.01, .changed)), .pass)
        XCTAssertEqual(b.handleScroll(ev(0.02, .ended)), .pass)
        XCTAssertEqual(b.handleScroll(ev(0.03, momentum: .began)), .pass)
        XCTAssertEqual(b.handleScroll(ev(0.04, momentum: .continued)), .pass)
        XCTAssertEqual(b.handleScroll(ev(0.05, momentum: .ended)), .pass)
    }

    func testNonMagicMouseScrollIsNeverTouched() {
        let b = ScrollBlocker()
        b.magicMouseButton(0, isDown: true, at: 0)
        XCTAssertEqual(b.handleScroll(ev(0.01, .began, magic: false)), .pass)
        XCTAssertEqual(b.handleScroll(ev(0.02, .changed, magic: false)), .pass)
    }

    func testFeatureCanBeDisabled() {
        var c = BlockerConfig(); c.blockScrollWhileClicked = false
        let b = ScrollBlocker(config: c)
        b.magicMouseButton(0, isDown: true, at: 0)
        XCTAssertEqual(b.handleScroll(ev(0.01, .began)), .pass)
        XCTAssertEqual(b.handleScroll(ev(0.02, .changed)), .pass)
    }

    // MARK: The headline feature

    func testGestureStartingWhileClickedResumesOnceBlockingEnds() {
        let b = ScrollBlocker()
        b.magicMouseButton(0, isDown: true, at: 0)
        XCTAssertEqual(b.handleScroll(ev(0.01, .began)), .drop)
        XCTAssertEqual(b.handleScroll(ev(0.02, .changed)), .drop)
        b.magicMouseButton(0, isDown: false, at: 0.03)
        // Still inside the release grace: keep swallowing.
        XCTAssertEqual(b.handleScroll(ev(0.10, .changed)), .drop)
        // Grace expired mid-gesture: the rest of the swipe is the user's deliberate scrolling
        // and must flow. (Swallowing it whole made "click, then scroll" dead for the entire
        // swipe, however long the finger stayed down.) Apps join mid-gesture — same contract as
        // a gesture that started before the tap was watching — then everything is normal,
        // momentum included.
        XCTAssertEqual(b.handleScroll(ev(0.50, .changed)), .pass)
        XCTAssertEqual(b.handleScroll(ev(0.505, .changed)), .pass)
        XCTAssertEqual(b.handleScroll(ev(0.51, .ended)), .pass)
        XCTAssertEqual(b.handleScroll(ev(0.52, momentum: .began)), .pass)
        XCTAssertEqual(b.handleScroll(ev(0.53, momentum: .continued)), .pass)
        XCTAssertEqual(b.handleScroll(ev(0.54, momentum: .ended)), .pass)
        // Next gesture: normal from the start.
        XCTAssertEqual(b.handleScroll(ev(1.0, .began)), .pass)
        XCTAssertEqual(b.handleScroll(ev(1.01, .changed)), .pass)
        XCTAssertEqual(b.handleScroll(ev(1.02, .ended)), .pass)
    }

    func testGestureEndingWhileStillBlockedStaysSwallowedWithItsMomentum() {
        let b = ScrollBlocker()
        b.magicMouseButton(0, isDown: true, at: 0)
        XCTAssertEqual(b.handleScroll(ev(0.01, .began)), .drop)
        XCTAssertEqual(b.handleScroll(ev(0.02, .changed)), .drop)
        // The whole gesture ends while the button is still down: nothing may leak, and the
        // momentum tail belongs to the swallowed gesture.
        XCTAssertEqual(b.handleScroll(ev(0.03, .ended)), .drop)
        XCTAssertEqual(b.handleScroll(ev(0.04, momentum: .began)), .drop)
        XCTAssertEqual(b.handleScroll(ev(0.05, momentum: .ended)), .drop)
        b.magicMouseButton(0, isDown: false, at: 0.06)
    }

    func testReleaseGraceBlocksTrailingScrollAfterMouseUp() {
        var c = BlockerConfig(); c.releaseGrace = 0.2
        let b = ScrollBlocker(config: c)
        b.magicMouseButton(0, isDown: true, at: 0)
        b.magicMouseButton(0, isDown: false, at: 0.10)
        // Finger lifts, produces a little scroll 50ms later: swallowed.
        XCTAssertEqual(b.handleScroll(ev(0.15, .began)), .drop)
        XCTAssertEqual(b.handleScroll(ev(0.16, .ended)), .drop)
        // After the grace window a new gesture is fine.
        XCTAssertEqual(b.handleScroll(ev(0.31, .began)), .pass)
    }

    func testMultipleButtonsOnlyUnblockWhenAllReleased() {
        let b = ScrollBlocker()
        b.magicMouseButton(0, isDown: true, at: 0)
        b.magicMouseButton(1, isDown: true, at: 0.01)
        b.magicMouseButton(0, isDown: false, at: 0.02)
        XCTAssertTrue(b.isBlocking(at: 1.0))
        b.magicMouseButton(1, isDown: false, at: 1.0)
        XCTAssertTrue(b.isBlocking(at: 1.1))   // grace
        XCTAssertFalse(b.isBlocking(at: 1.3))
    }

    func testForceReleaseClearsStuckButtons() {
        let b = ScrollBlocker()
        b.magicMouseButton(0, isDown: true, at: 0)
        b.forceReleaseAllButtons(at: 5)
        XCTAssertFalse(b.anyButtonDown)
        XCTAssertTrue(b.isBlocking(at: 5.1))
        XCTAssertFalse(b.isBlocking(at: 5.3))
    }

    // MARK: Click landing mid-gesture

    func testClickDuringVisibleGestureEndsItCleanlyThenSwallows() {
        let b = ScrollBlocker()
        XCTAssertEqual(b.handleScroll(ev(0, .began)), .pass)
        XCTAssertEqual(b.handleScroll(ev(0.01, .changed)), .pass)
        b.magicMouseButton(0, isDown: true, at: 0.02)
        // The first event after the click is rewritten into a zero-delta "ended".
        XCTAssertEqual(b.handleScroll(ev(0.03, .changed)), endedZero)
        // Everything else in this gesture — including its real ended and momentum — is dropped.
        XCTAssertEqual(b.handleScroll(ev(0.04, .changed)), .drop)
        XCTAssertEqual(b.handleScroll(ev(0.05, .ended)), .drop)
        XCTAssertEqual(b.handleScroll(ev(0.06, momentum: .began)), .drop)
        XCTAssertEqual(b.handleScroll(ev(0.07, momentum: .ended)), .drop)
    }

    func testClickAfterMayBeginCancelsIt() {
        let b = ScrollBlocker()
        XCTAssertEqual(b.handleScroll(ev(0, .mayBegin, dy: 0)), .pass)
        b.magicMouseButton(0, isDown: true, at: 0.01)
        XCTAssertEqual(b.handleScroll(ev(0.02, .began)),
                       .rewrite(.init(phase: .cancelled, momentum: .none, zeroX: true, zeroY: true)))
        XCTAssertEqual(b.handleScroll(ev(0.03, .changed)), .drop)
        XCTAssertEqual(b.handleScroll(ev(0.04, .ended)), .drop)
    }

    func testMayBeginWhileClickedIsSwallowedWithTheRest() {
        let b = ScrollBlocker()
        b.magicMouseButton(0, isDown: true, at: 0)
        XCTAssertEqual(b.handleScroll(ev(0.01, .mayBegin, dy: 0)), .drop)
        XCTAssertEqual(b.handleScroll(ev(0.02, .began)), .drop)
        XCTAssertEqual(b.handleScroll(ev(0.03, .changed)), .drop)
        XCTAssertEqual(b.handleScroll(ev(0.04, .ended)), .drop)
    }

    // MARK: Click during momentum

    func testClickDuringVisibleMomentumStopsCoasting() {
        let b = ScrollBlocker()
        XCTAssertEqual(b.handleScroll(ev(0, .began)), .pass)
        XCTAssertEqual(b.handleScroll(ev(0.01, .ended)), .pass)
        XCTAssertEqual(b.handleScroll(ev(0.02, momentum: .began)), .pass)
        XCTAssertEqual(b.handleScroll(ev(0.03, momentum: .continued)), .pass)
        b.magicMouseButton(0, isDown: true, at: 0.04)
        XCTAssertEqual(b.handleScroll(ev(0.05, momentum: .continued)), momentumEndedZero)
        XCTAssertEqual(b.handleScroll(ev(0.06, momentum: .continued)), .drop)
        XCTAssertEqual(b.handleScroll(ev(0.07, momentum: .ended)), .drop)
        // The click is released, then a fresh gesture: passes normally, momentum included.
        b.magicMouseButton(0, isDown: false, at: 0.08)
        XCTAssertEqual(b.handleScroll(ev(1.0, .began)), .pass)
        XCTAssertEqual(b.handleScroll(ev(1.01, .ended)), .pass)
        XCTAssertEqual(b.handleScroll(ev(1.02, momentum: .began)), .pass)
        XCTAssertEqual(b.handleScroll(ev(1.03, momentum: .ended)), .pass)
    }

    func testMomentumBeginningWhileClickedIsSwallowedWhole() {
        let b = ScrollBlocker()
        XCTAssertEqual(b.handleScroll(ev(0, .began)), .pass)
        XCTAssertEqual(b.handleScroll(ev(0.01, .ended)), .pass)
        b.magicMouseButton(0, isDown: true, at: 0.015)
        XCTAssertEqual(b.handleScroll(ev(0.02, momentum: .began)), .drop)
        XCTAssertEqual(b.handleScroll(ev(0.03, momentum: .continued)), .drop)
        XCTAssertEqual(b.handleScroll(ev(0.04, momentum: .ended)), .drop)
    }

    // MARK: Phase-less scrolls

    func testPhaselessScrollIsSimplyDroppedWhileClicked() {
        let b = ScrollBlocker()
        XCTAssertEqual(b.handleScroll(ev(0)), .pass)
        b.magicMouseButton(0, isDown: true, at: 0.01)
        XCTAssertEqual(b.handleScroll(ev(0.02)), .drop)
        b.magicMouseButton(0, isDown: false, at: 0.03)
        XCTAssertEqual(b.handleScroll(ev(0.5)), .pass)
    }

    // MARK: Axis lock (opt-in)

    func testAxisLockZeroesMinorAxisOnceCommitted() {
        var c = BlockerConfig(); c.axisLock = true; c.axisLockThreshold = 10; c.axisLockRatio = 1.4
        let b = ScrollBlocker(config: c)
        // Small diagonal start: untouched until threshold reached.
        XCTAssertEqual(b.handleScroll(ev(0, .began, dx: 1, dy: 4)), .pass)
        XCTAssertEqual(b.handleScroll(ev(0.01, .changed, dx: 1, dy: 4)), .pass)
        // cumY = 12 >= 10 and 12 >= 3*1.4 → locked vertical; horizontal drift zeroed from now on.
        XCTAssertEqual(b.handleScroll(ev(0.02, .changed, dx: 1, dy: 4)),
                       .rewrite(.init(phase: .changed, momentum: .none, zeroX: true, zeroY: false)))
        // Purely vertical events don't need rewriting.
        XCTAssertEqual(b.handleScroll(ev(0.03, .changed, dx: 0, dy: 4)), .pass)
        XCTAssertEqual(b.handleScroll(ev(0.04, .ended, dx: 0, dy: 0)), .pass)
        // Momentum inherits the lock.
        XCTAssertEqual(b.handleScroll(ev(0.05, momentum: .began, dx: 2, dy: 6)),
                       .rewrite(.init(phase: .none, momentum: .began, zeroX: true, zeroY: false)))
        // A new gesture starts fresh (horizontal this time).
        XCTAssertEqual(b.handleScroll(ev(1.0, .began, dx: 8, dy: 1)), .pass)
        XCTAssertEqual(b.handleScroll(ev(1.01, .changed, dx: 8, dy: 1)),
                       .rewrite(.init(phase: .changed, momentum: .none, zeroX: false, zeroY: true)))
    }

    func testAxisLockDoesNotEngageForGenuinelyDiagonalScroll() {
        var c = BlockerConfig(); c.axisLock = true
        let b = ScrollBlocker(config: c)
        for i in 0..<10 {
            XCTAssertEqual(b.handleScroll(ev(Double(i) * 0.01, i == 0 ? .began : .changed, dx: 5, dy: 5)), .pass)
        }
    }

    func testAxisLockIsOffByDefault() {
        let b = ScrollBlocker()
        XCTAssertEqual(b.handleScroll(ev(0, .began, dx: 1, dy: 40)), .pass)
        XCTAssertEqual(b.handleScroll(ev(0.01, .changed, dx: 1, dy: 40)), .pass)
    }
}

extension ScrollBlockerTests {
    func testLostEndedDoesNotPoisonTheNextGesture() {
        let b = ScrollBlocker()
        b.magicMouseButton(0, isDown: true, at: 0)
        XCTAssertEqual(b.handleScroll(ScrollEvent(timestamp: 0.01, phase: .began, deltaY: 3)), .drop)
        XCTAssertEqual(b.handleScroll(ScrollEvent(timestamp: 0.02, phase: .changed, deltaY: 3)), .drop)
        b.magicMouseButton(0, isDown: false, at: 0.03)
        // No `ended` ever arrives; a brand-new gesture after the grace window must pass.
        XCTAssertEqual(b.handleScroll(ScrollEvent(timestamp: 1.0, phase: .began, deltaY: 3)), .pass)
        XCTAssertEqual(b.handleScroll(ScrollEvent(timestamp: 1.01, phase: .changed, deltaY: 3)), .pass)
    }
}

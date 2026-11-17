import XCTest
@testable import CalmMouseCore

final class ControllerPointerTests: XCTestCase {
    private func active(_ change: (inout ControllerConfig) -> Void = { _ in }) -> ControllerPointer {
        var config = ControllerConfig()
        change(&config)
        var pointer = ControllerPointer(config: config)
        _ = pointer.setActive(true)
        return pointer
    }

    private func p(_ x: Double, _ y: Double) -> TouchPoint { TouchPoint(x: x, y: y) }

    private func moves(_ actions: [PointerAction]) -> (dx: Double, dy: Double) {
        actions.reduce(into: (0.0, 0.0)) { sum, action in
            if case let .move(dx, dy) = action { sum.0 += dx; sum.1 += dy }
        }
    }

    private func scrolls(_ actions: [PointerAction]) -> (v: Double, h: Double) {
        actions.reduce(into: (0.0, 0.0)) { sum, action in
            if case let .scroll(v, h) = action { sum.0 += v; sum.1 += h }
        }
    }

    // MARK: Touchpad

    func testFirstReportOfATouchMovesNothing() {
        var pointer = active()
        XCTAssertEqual(pointer.touchpad(primary: p(0.5, 0.5), secondary: nil, at: 0), [])
    }

    func testSlidingRightAndUpMovesThePointerRightAndUp() {
        var pointer = active()
        _ = pointer.touchpad(primary: p(-0.5, 0), secondary: nil, at: 0)
        var all: [PointerAction] = []
        for i in 1...10 {
            all += pointer.touchpad(primary: p(-0.5 + Double(i) * 0.05, Double(i) * 0.02), secondary: nil, at: Double(i) * 0.008)
        }
        let m = moves(all)
        XCTAssertGreaterThan(m.dx, 0)
        XCTAssertLessThan(m.dy, 0, "up on the pad is up on the screen, where y grows downward")
        XCTAssertTrue(all.allSatisfy { if case .move = $0 { return true } else { return false } })
    }

    func testVerticalTravelIsScaledToThePadsShape() {
        var a = active(), b = active()
        _ = a.touchpad(primary: p(0, 0.1), secondary: nil, at: 0)
        _ = b.touchpad(primary: p(0, 0.1), secondary: nil, at: 0)
        let across = moves(a.touchpad(primary: p(0.1, 0.1), secondary: nil, at: 0.01))
        let upward = moves(b.touchpad(primary: p(0, 0.2), secondary: nil, at: 0.01))
        // The same 0.1 units: shorter vertically by the pad's aspect (and the slower speed
        // that comes with the shorter distance).
        XCTAssertLessThan(abs(upward.dy), abs(across.dx) * 0.6)
    }

    func testFasterSwipesGoFurtherForTheSameDistance() {
        func travel(seconds: Double) -> Double {
            var pointer = active()
            _ = pointer.touchpad(primary: p(-0.5, 0), secondary: nil, at: 0)
            var all: [PointerAction] = []
            for i in 1...10 {
                all += pointer.touchpad(primary: p(-0.5 + Double(i) * 0.05, 0), secondary: nil, at: Double(i) * seconds / 10)
            }
            return moves(all).dx
        }
        XCTAssertGreaterThan(travel(seconds: 0.1), travel(seconds: 1) * 1.5)
    }

    func testPointerSpeedScalesMovement() {
        func travel(_ speed: Double) -> Double {
            var pointer = active { $0.pointerSpeed = speed }
            _ = pointer.touchpad(primary: p(0, 0), secondary: nil, at: 0)
            return moves(pointer.touchpad(primary: p(0.1, 0), secondary: nil, at: 0.02)).dx
        }
        XCTAssertEqual(travel(1) / travel(0), ControllerConfig.speedRange, accuracy: 1e-9)
        XCTAssertEqual(travel(0) / travel(-1), ControllerConfig.speedRange, accuracy: 1e-9)
    }

    func testQuickStillTouchIsATap() {
        var pointer = active()
        _ = pointer.touchpad(primary: p(0.2, 0.2), secondary: nil, at: 0)
        _ = pointer.touchpad(primary: p(0.21, 0.2), secondary: nil, at: 0.05)
        XCTAssertEqual(pointer.touchpad(primary: nil, secondary: nil, at: 0.1), [.click(.left)])
    }

    func testTwoFingerTapIsARightClick() {
        var pointer = active()
        _ = pointer.touchpad(primary: p(0.2, 0.2), secondary: nil, at: 0)
        _ = pointer.touchpad(primary: p(0.2, 0.2), secondary: p(-0.2, 0.2), at: 0.03)
        XCTAssertEqual(pointer.touchpad(primary: nil, secondary: nil, at: 0.1), [.click(.right)])
    }

    func testLongOrMovingTouchIsNotATap() {
        var slow = active()
        _ = slow.touchpad(primary: p(0, 0), secondary: nil, at: 0)
        XCTAssertEqual(slow.touchpad(primary: nil, secondary: nil, at: 0.5), [])

        var moving = active()
        _ = moving.touchpad(primary: p(0, 0), secondary: nil, at: 0)
        _ = moving.touchpad(primary: p(0.2, 0), secondary: nil, at: 0.05)
        XCTAssertEqual(moving.touchpad(primary: nil, secondary: nil, at: 0.1), [])
    }

    func testTapToClickOffMeansNoTaps() {
        var pointer = active { $0.tapToClick = false }
        _ = pointer.touchpad(primary: p(0, 0), secondary: nil, at: 0)
        XCTAssertEqual(pointer.touchpad(primary: nil, secondary: nil, at: 0.05), [])
    }

    func testTwoFingersScrollInsteadOfMoving() {
        var pointer = active()
        _ = pointer.touchpad(primary: p(0, 0), secondary: p(0.3, 0), at: 0)
        let actions = pointer.touchpad(primary: p(0, 0.1), secondary: p(0.3, 0.1), at: 0.01)
        XCTAssertEqual(moves(actions).dx, 0)
        XCTAssertEqual(moves(actions).dy, 0)
        // Natural scrolling: fingers going up push the page up, showing what is below.
        XCTAssertLessThan(scrolls(actions).v, 0)
    }

    func testTwoFingerScrollFollowsTheSystemDirection() {
        var pointer = active { $0.naturalScrolling = false }
        _ = pointer.touchpad(primary: p(0, 0), secondary: p(0.3, 0), at: 0)
        let actions = pointer.touchpad(primary: p(0, 0.1), secondary: p(0.3, 0.1), at: 0.01)
        XCTAssertGreaterThan(scrolls(actions).v, 0)
    }

    func testAFingerJoiningOrLeavingDoesNotJumpThePointer() {
        var pointer = active()
        _ = pointer.touchpad(primary: p(0.5, 0), secondary: nil, at: 0)
        // The first finger lifts while a second, far away, stays: the pointer stays put.
        _ = pointer.touchpad(primary: p(0.5, 0), secondary: p(-0.8, 0), at: 0.01)
        XCTAssertEqual(pointer.touchpad(primary: nil, secondary: p(-0.8, 0), at: 0.02), [])
        let next = pointer.touchpad(primary: nil, secondary: p(-0.75, 0), at: 0.03)
        XCTAssertGreaterThan(moves(next).dx, 0)
        XCTAssertLessThan(moves(next).dx, 60)
    }

    // MARK: Pressing

    func testPressingThePadHoldsTheLeftButtonAndIsNotAlsoATap() {
        var pointer = active()
        _ = pointer.touchpad(primary: p(0, 0), secondary: nil, at: 0)
        XCTAssertEqual(pointer.button(.touchpad, pressed: true, at: 0.02), [.press(.left)])
        XCTAssertTrue(pointer.isHeld(.left))
        // Sliding while pressed still moves: the app sends it as a drag.
        XCTAssertGreaterThan(moves(pointer.touchpad(primary: p(0.2, 0), secondary: nil, at: 0.05)).dx, 0)
        XCTAssertEqual(pointer.button(.touchpad, pressed: false, at: 0.08), [.release(.left)])
        XCTAssertEqual(pointer.touchpad(primary: nil, secondary: nil, at: 0.1), [])
    }

    func testPressingThePadWithTwoFingersIsARightClick() {
        var pointer = active()
        _ = pointer.touchpad(primary: p(0, 0), secondary: p(0.4, 0), at: 0)
        XCTAssertEqual(pointer.button(.touchpad, pressed: true, at: 0.02), [.press(.right)])
        // The release lifts the right button even if a finger has gone by then.
        _ = pointer.touchpad(primary: p(0, 0), secondary: nil, at: 0.03)
        XCTAssertEqual(pointer.button(.touchpad, pressed: false, at: 0.05), [.release(.right)])
    }

    func testFaceButtonsAreLeftAndRight() {
        var pointer = active()
        XCTAssertEqual(pointer.button(.south, pressed: true, at: 0), [.press(.left)])
        XCTAssertEqual(pointer.button(.south, pressed: false, at: 0.1), [.release(.left)])
        XCTAssertEqual(pointer.button(.east, pressed: true, at: 0.2), [.press(.right)])
        XCTAssertEqual(pointer.button(.east, pressed: false, at: 0.3), [.release(.right)])
    }

    func testOverlappingPressesOfOneButtonReleaseOnce() {
        var pointer = active()
        XCTAssertEqual(pointer.button(.south, pressed: true, at: 0), [.press(.left)])
        XCTAssertEqual(pointer.button(.touchpad, pressed: true, at: 0.1), [])
        XCTAssertEqual(pointer.button(.south, pressed: false, at: 0.2), [])
        XCTAssertEqual(pointer.button(.touchpad, pressed: false, at: 0.3), [.release(.left)])
    }

    func testSwitchingOffLetsGoOfHeldButtons() {
        var pointer = active()
        _ = pointer.button(.south, pressed: true, at: 0)
        XCTAssertEqual(pointer.setActive(false), [.release(.left)])
        XCTAssertEqual(pointer.button(.south, pressed: false, at: 0.1), [])
    }

    func testNothingHappensWhileInactive() {
        var pointer = ControllerPointer()
        XCTAssertEqual(pointer.button(.south, pressed: true, at: 0), [])
        _ = pointer.touchpad(primary: p(0, 0), secondary: nil, at: 0)
        XCTAssertEqual(pointer.touchpad(primary: p(0.3, 0), secondary: nil, at: 0.01), [])
        pointer.sticks(left: p(1, 0), right: p(0, 0))
        XCTAssertFalse(pointer.needsTicks)
        XCTAssertEqual(pointer.tick(dt: 0.01, at: 0.02), [])
    }

    // MARK: Sticks

    func testSticksInsideTheDeadZoneDoNothing() {
        var pointer = active()
        pointer.sticks(left: p(0.1, 0.05), right: p(-0.05, 0.1))
        XCTAssertFalse(pointer.needsTicks)
        XCTAssertEqual(pointer.tick(dt: 0.01, at: 0), [])
    }

    func testLeftStickMovesAndRightStickScrolls() {
        var pointer = active()
        pointer.sticks(left: p(1, 0), right: p(0, 1))
        XCTAssertTrue(pointer.needsTicks)
        let actions = pointer.tick(dt: 0.01, at: 0)
        XCTAssertEqual(moves(actions).dx, ControllerConfig().stickMaxSpeed * 0.01, accuracy: 1e-6)
        XCTAssertEqual(moves(actions).dy, 0, accuracy: 1e-9)
        XCTAssertEqual(scrolls(actions).v, ControllerConfig().stickMaxScroll * 0.01, accuracy: 1e-6,
                       "pushing up shows what is above")
    }

    func testStickUpMovesThePointerUp() {
        var pointer = active()
        pointer.sticks(left: p(0, 1), right: p(0, 0))
        XCTAssertLessThan(moves(pointer.tick(dt: 0.01, at: 0)).dy, 0)
    }

    func testStickSpeedRisesGentlyFromCentre() {
        var pointer = active()
        pointer.sticks(left: p(0.575, 0), right: p(0, 0))  // halfway between dead zone and edge
        let half = moves(pointer.tick(dt: 0.01, at: 0)).dx
        XCTAssertEqual(half, ControllerConfig().stickMaxSpeed * 0.01 * 0.25, accuracy: 1e-6)
    }

    func testAStalledClockDoesNotJump() {
        var pointer = active()
        pointer.sticks(left: p(1, 0), right: p(0, 0))
        XCTAssertEqual(moves(pointer.tick(dt: 5, at: 0)).dx, ControllerConfig().stickMaxSpeed * 0.1, accuracy: 1e-6)
    }

    // MARK: Stick clicks

    func testPressingTheSticksClicksLeftAndRight() {
        var pointer = active()
        XCTAssertEqual(pointer.button(.leftStick, pressed: true, at: 0), [.press(.left)])
        XCTAssertEqual(pointer.button(.leftStick, pressed: false, at: 0.1), [.release(.left)])
        XCTAssertEqual(pointer.button(.rightStick, pressed: true, at: 0.5), [.press(.right)])
        XCTAssertEqual(pointer.button(.rightStick, pressed: false, at: 0.6), [.release(.right)])
    }

    func testStickClicksCanBeTurnedOff() {
        var pointer = active { $0.stickClicks = false }
        XCTAssertEqual(pointer.button(.leftStick, pressed: true, at: 0), [])
        XCTAssertEqual(pointer.button(.rightStick, pressed: true, at: 0), [])
    }

    func testTurningStickClicksOffMidPressStillLetsGo() {
        var pointer = active()
        _ = pointer.button(.leftStick, pressed: true, at: 0)
        pointer.config.stickClicks = false
        XCTAssertEqual(pointer.button(.leftStick, pressed: false, at: 0.1), [.release(.left)])
    }

    func testTheNudgeOfPressingAStickDoesNotMoveThePointer() {
        var pointer = active()
        _ = pointer.button(.leftStick, pressed: true, at: 1)
        pointer.sticks(left: p(0.4, 0.2), right: p(0, 0))
        XCTAssertEqual(moves(pointer.tick(dt: 0.01, at: 1.05)).dx, 0)
        // Held on and pushed on purpose: it drags.
        XCTAssertGreaterThan(moves(pointer.tick(dt: 0.01, at: 1.2)).dx, 0)
        XCTAssertTrue(pointer.isHeld(.left))
    }

    func testTheNudgeOfLettingGoDoesNotMoveThePointer() {
        var pointer = active()
        _ = pointer.button(.leftStick, pressed: true, at: 1)
        _ = pointer.button(.leftStick, pressed: false, at: 1.1)
        pointer.sticks(left: p(0.4, 0), right: p(0, 0))
        XCTAssertEqual(pointer.tick(dt: 0.01, at: 1.2), [])
        XCTAssertGreaterThan(moves(pointer.tick(dt: 0.01, at: 1.3)).dx, 0)
    }

    func testPressingTheRightStickDoesNotScroll() {
        var pointer = active()
        _ = pointer.button(.rightStick, pressed: true, at: 1)
        pointer.sticks(left: p(0, 0), right: p(0, 0.5))
        XCTAssertEqual(scrolls(pointer.tick(dt: 0.01, at: 1.05)).v, 0)
        // The left stick isn't held still by a right-stick press.
        pointer.sticks(left: p(1, 0), right: p(0, 0.5))
        XCTAssertGreaterThan(moves(pointer.tick(dt: 0.01, at: 1.06)).dx, 0)
    }

    func testAStickClickAndAFaceButtonShareTheLeftButton() {
        var pointer = active()
        XCTAssertEqual(pointer.button(.leftStick, pressed: true, at: 0), [.press(.left)])
        XCTAssertEqual(pointer.button(.south, pressed: true, at: 0.1), [])
        XCTAssertEqual(pointer.button(.leftStick, pressed: false, at: 0.2), [])
        XCTAssertEqual(pointer.button(.south, pressed: false, at: 0.3), [.release(.left)])
    }

    // MARK: On/off chord

    func testHoldingBothMenuButtonsTogglesOnceEvenWhileInactive() {
        var pointer = ControllerPointer()
        _ = pointer.button(.menu, pressed: true, at: 0)
        _ = pointer.button(.options, pressed: true, at: 0.1)
        XCTAssertTrue(pointer.needsTicks)
        XCTAssertEqual(pointer.tick(dt: 0.01, at: 0.3), [])
        XCTAssertEqual(pointer.tick(dt: 0.01, at: 0.61), [.toggle])
        XCTAssertFalse(pointer.needsTicks)
        XCTAssertEqual(pointer.tick(dt: 0.01, at: 1.5), [], "held on, it fires once")
        _ = pointer.button(.menu, pressed: false, at: 2)
        _ = pointer.button(.options, pressed: false, at: 2)
        _ = pointer.button(.menu, pressed: true, at: 3)
        _ = pointer.button(.options, pressed: true, at: 3)
        XCTAssertEqual(pointer.tick(dt: 0.01, at: 3.6), [.toggle])
    }

    func testOneMenuButtonAloneNeverToggles() {
        var pointer = ControllerPointer()
        _ = pointer.button(.menu, pressed: true, at: 0)
        XCTAssertFalse(pointer.needsTicks)
        XCTAssertEqual(pointer.tick(dt: 0.01, at: 2), [])
    }

    func testLettingGoEarlyCancelsTheToggle() {
        var pointer = ControllerPointer()
        _ = pointer.button(.menu, pressed: true, at: 0)
        _ = pointer.button(.options, pressed: true, at: 0)
        _ = pointer.button(.options, pressed: false, at: 0.3)
        XCTAssertEqual(pointer.tick(dt: 0.01, at: 1), [])
    }
}

final class ControllerAppsTests: XCTestCase {
    func testGameCategories() {
        XCTAssertTrue(ControllerApps.isGameCategory("public.app-category.games"))
        XCTAssertTrue(ControllerApps.isGameCategory("public.app-category.action-games"))
        XCTAssertTrue(ControllerApps.isGameCategory("public.app-category.Role-Playing-Games"))
        XCTAssertFalse(ControllerApps.isGameCategory("public.app-category.productivity"))
        XCTAssertFalse(ControllerApps.isGameCategory("public.app-category.entertainment"))
    }
}

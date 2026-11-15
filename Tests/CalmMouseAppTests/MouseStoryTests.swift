import XCTest
@testable import CalmMouse

/// The previews in the Devices column play on the rendered mouse: each one is a pure function of
/// the loop time saying where fingers are on the lid (u across, v along; front is v −1) and what
/// the page beside it does. Same stories and timings as the small previews elsewhere.
final class MouseStoryTests: XCTestCase {

    private func visible(_ f: StoryFrame) -> [StoryTouch] { f.touches.filter { $0.alpha > 0.5 } }

    func testDontScrollWhileClickingHoldsThePageUntilTheFingerLetsGo() {
        let s = SettingPreview.blockWhileClick
        let pressedSlide = s.frame(at: 1.5)
        XCTAssertTrue(pressedSlide.pressed)
        XCTAssertEqual(visible(pressedSlide).first?.pressing, true)
        XCTAssertLessThan(visible(pressedSlide).first?.v ?? 9, 0.15 - 0.2, "the finger slides toward the front")
        XCTAssertEqual(pressedSlide.page.scrollY, 0, "the page holds still under the click")
        XCTAssertGreaterThan(pressedSlide.page.cross, 0.9)
        let swipeAfter = s.frame(at: 3.2)
        XCTAssertFalse(swipeAfter.pressed)
        XCTAssertGreaterThan(swipeAfter.page.scrollY, 20, "released, the same swipe scrolls")
    }

    func testGlidingKeepsThePageMovingOnlyWhenOn() {
        let on = SettingPreview.momentum(on: true), off = SettingPreview.momentum(on: false)
        XCTAssertGreaterThan(on.frame(at: 2.0).page.scrollY, on.frame(at: 1.0).page.scrollY + 5)
        XCTAssertEqual(off.frame(at: 2.0).page.scrollY, off.frame(at: 1.0).page.scrollY, accuracy: 0.001)
        XCTAssertGreaterThan(off.frame(at: 1.3).page.cross, 0.9)
        XCTAssertTrue(visible(on.frame(at: 1.0)).isEmpty, "the finger has left: the page coasts alone")
    }

    func testStraightLinesLetTheFingerDriftButNotThePage() {
        let f = SettingPreview.straightLines.frame(at: 2.0)
        XCTAssertGreaterThan(visible(f).first?.u ?? 0, 0.2, "the finger drifts sideways")
        XCTAssertEqual(f.page.scrollX, 0)
        XCTAssertGreaterThan(f.page.scrollY, 20)
    }

    func testUpAndDownOnlyIgnoresTheSidewaysPart() {
        let s = SettingPreview.ignoreSideways
        XCTAssertEqual(s.frame(at: 1.2).page.scrollY, 0, "sideways first: nothing moves")
        XCTAssertGreaterThan(s.frame(at: 1.2).page.cross, 0.5)
        XCTAssertGreaterThan(s.frame(at: 2.6).page.scrollY, 20, "then up: it scrolls")
    }

    func testReverseFlipsWhichWayTheSameSwipeScrolls() {
        let normal = SettingPreview.reverseScroll(on: false).frame(at: 1.8)
        let reversed = SettingPreview.reverseScroll(on: true).frame(at: 1.8)
        XCTAssertGreaterThan(normal.page.scrollY, 20)
        XCTAssertLessThan(reversed.page.scrollY, -20)
    }

    func testTapToClickTapsTwiceWhereTheFingerRests() {
        let s = SettingPreview.tapToClick
        let tap = s.frame(at: 0.5)
        XCTAssertEqual(visible(tap).count, 1)
        XCTAssertEqual(visible(tap).first?.pressing, false, "a tap, not a press")
        XCTAssertGreaterThan(s.frame(at: 0.65).page.flash, 0.5, "the button under the pointer clicks")
        XCTAssertTrue(visible(s.frame(at: 1.0)).isEmpty)
        XCTAssertEqual(visible(s.frame(at: 1.6)).count, 1, "and again")
        let ripple = s.frame(at: 0.75).ripples.first { $0.progress > 0 && $0.progress < 1 }
        XCTAssertNotNil(ripple)
    }

    func testRightSideTapOpensTheMenuAndALeftTapIsANormalClick() {
        let s = SettingPreview.tapRightClick(doubleTap: false)
        XCTAssertNotNil(s.frame(at: 1.0).zone)
        let right = visible(s.frame(at: 0.5))
        XCTAssertEqual(right.count, 1)
        guard let tap = right.first else { return }
        XCTAssertGreaterThan(tap.u, 0.3)
        XCTAssertLessThan(tap.v, 0, "near the front, where a finger taps")
        XCTAssertGreaterThan(s.frame(at: 1.0).page.menu, 0.9)
        let left = visible(s.frame(at: 2.3))
        XCTAssertLessThan(left.first?.u ?? 1, -0.3)
        XCTAssertGreaterThan(s.frame(at: 2.55).page.flash, 0.5)
        XCTAssertLessThan(s.frame(at: 2.55).page.menu, 0.01)
    }

    func testDoubleTapRightClickTapsTwiceInOnePlace() {
        let s = SettingPreview.tapRightClick(doubleTap: true)
        XCTAssertEqual(visible(s.frame(at: 0.4)).count, 1)
        XCTAssertTrue(visible(s.frame(at: 0.6)).isEmpty)
        XCTAssertEqual(visible(s.frame(at: 0.8)).count, 1)
        XCTAssertGreaterThan(s.frame(at: 1.5).page.menu, 0.9)
    }

    func testFrontOfTheMouseOnlyCountsFrontTapsAndIgnoresTheGrip() {
        let s = SettingPreview.tapZone(depth: 0.45)
        guard case .front(let depth)? = s.frame(at: 0.5).zone else { return XCTFail("no front zone") }
        XCTAssertEqual(depth, 0.45)
        let front = visible(s.frame(at: 0.52))
        XCTAssertEqual(front.count, 1)
        XCTAssertLessThan(front.first?.v ?? 9, -1 + 2 * 0.45, "inside the front zone")
        let grip = s.frame(at: 1.95).touches.filter { $0.alpha > 0.5 }
        XCTAssertEqual(grip.first?.ignored, true)
        XCTAssertGreaterThan(grip.first?.v ?? 0, 0.3, "near the back, where the hand rests")
    }

    func testDoubleTapDragHoldsAndMovesTheMouseWithTheObject() {
        let s = SettingPreview.tapAndDrag
        XCTAssertFalse(s.frame(at: 1.0).pressed)
        let dragging = s.frame(at: 2.0)
        XCTAssertTrue(dragging.pressed)
        XCTAssertGreaterThan(dragging.slide, 0.3, "the mouse itself moves")
        XCTAssertGreaterThan(dragging.page.object ?? 0, 5, "and the object comes along")
        XCTAssertFalse(s.frame(at: 3.5).pressed, "lift to drop")
    }

    func testTwoFingerDragRestsTwoFingersAndShowsTheHoldDial() {
        let s = SettingPreview.twoFingerDrag
        XCTAssertEqual(visible(s.frame(at: 0.8)).count, 2)
        XCTAssertGreaterThan(s.frame(at: 0.8).dial?.progress ?? 0, 0.2)
        XCTAssertTrue(s.frame(at: 2.0).pressed)
    }

    func testSettleAfterReleaseIgnoresTheFlickAsTheFingerLifts() {
        let s = SettingPreview.settleAfterRelease
        XCTAssertTrue(s.frame(at: 0.6).pressed)
        XCTAssertEqual(s.frame(at: 1.3).page.scrollY, 0)
        XCTAssertGreaterThan(s.frame(at: 1.3).page.cross, 0.5)
        XCTAssertGreaterThan(s.frame(at: 3.1).page.scrollY, 20)
    }

    func testIgnoreNudgesWaitsForARealSwipe() {
        let s = SettingPreview.ignoreNudges
        XCTAssertEqual(s.frame(at: 1.0).page.scrollY, 0)
        XCTAssertGreaterThan(s.frame(at: 1.0).page.cross, 0.5)
        XCTAssertGreaterThan(s.frame(at: 2.95).page.scrollY, 20)
    }

    func testKeyZoomHoldsTheKeyAndZoomsThePage() {
        let s = SettingPreview.keyZoom
        XCTAssertGreaterThan(s.frame(at: 1.5).page.key ?? 0, 0.9)
        XCTAssertGreaterThan(s.frame(at: 2.0).page.zoom, 1.4)
    }

    func testEveryStoryStaysOnTheLid() {
        let all: [SettingPreview] = [.blockWhileClick, .settleAfterRelease, .ignoreNudges, .straightLines,
                                     .momentum(on: true), .ignoreSideways, .tapToClick, .tapRightClick(doubleTap: false),
                                     .tapRightClick(doubleTap: true), .tapZone(depth: 0.45), .tapAndDrag, .twoFingerDrag,
                                     .keyZoom, .reverseScroll(on: true)]
        for s in all {
            var shown = 0
            for i in 0..<Int(s.loop * 20) {
                for t in s.frame(at: Double(i) / 20).touches where t.alpha > 0.01 {
                    shown += 1
                    XCTAssert((-1...1).contains(t.u) && (-1...1).contains(t.v), "\(s) puts a finger off the lid")
                }
            }
            XCTAssertGreaterThan(shown, 0, "\(s) never shows a finger")
        }
    }
}

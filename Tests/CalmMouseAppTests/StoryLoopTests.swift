import XCTest
@testable import CalmMouse

/// The Devices stage loops a story for as long as the pointer rests on its row. A loop may not end
/// with a jump: whatever shows as one loop ends still shows as the next begins.
final class StoryLoopTests: XCTestCase {
    static let all: [SettingPreview] = [
        .blockWhileClick, .settleAfterRelease, .ignoreNudges, .straightLines,
        .momentum(on: true), .momentum(on: false), .ignoreSideways, .tapToClick,
        .tapRightClick(doubleTap: false), .tapRightClick(doubleTap: true), .tapZone(depth: 0.5),
        .tapAndDrag, .twoFingerDrag, .keyZoom, .reverseScroll(on: true), .reverseScroll(on: false),
    ]

    /// The frames 1 ms either side of the end of each of the first three loops.
    func boundaries(_ p: SettingPreview) -> [(before: StoryFrame, after: StoryFrame, name: String)] {
        (1...3).map { k in
            let b = Double(k) * p.loop
            return (p.frame(elapsed: b - 0.001), p.frame(elapsed: b + 0.001), "\(p) at the end of loop \(k)")
        }
    }

    func testThePageScrollsOnInsteadOfJumpingBack() {
        for p in Self.all {
            for (a, b, name) in boundaries(p) {
                XCTAssertEqual(a.page.scrollY, b.page.scrollY, accuracy: 0.5, name)
            }
        }
    }

    func testEachLoopScrollsAsFarAsTheFirst() {
        let p = SettingPreview.momentum(on: true)
        let first = p.frame(elapsed: p.loop - 0.001).page.scrollY
        XCTAssertGreaterThan(first, 60)
        XCTAssertEqual(p.frame(elapsed: 3 * p.loop - 0.001).page.scrollY, 3 * first, accuracy: 0.5)
        let up = SettingPreview.reverseScroll(on: true)
        XCTAssertLessThan(up.frame(elapsed: 2 * up.loop + 1.7).page.scrollY,
                          up.frame(elapsed: up.loop + 1.7).page.scrollY - 20, "reversed, the page keeps going up")
    }

    func testNothingJumpsAtTheEndOfALoop() {
        for p in Self.all {
            for (a, b, name) in boundaries(p) {
                XCTAssertEqual(a.slide, b.slide, accuracy: 0.005, "\(name): the mouse")
                XCTAssertEqual(a.pressed, b.pressed, name)
                XCTAssertEqual(a.page.fade, b.page.fade, accuracy: 0.01, "\(name): the page's contents")
                XCTAssertEqual(a.page.menu, b.page.menu, accuracy: 0.01, name)
                XCTAssertEqual(a.page.flash, b.page.flash, accuracy: 0.01, name)
                XCTAssertEqual(a.page.cross, b.page.cross, accuracy: 0.01, name)
                XCTAssertEqual(a.page.arrows?.page ?? 0, b.page.arrows?.page ?? 0, accuracy: 0.01, name)
                XCTAssertEqual(a.crossOnLid?.alpha ?? 0, b.crossOnLid?.alpha ?? 0, accuracy: 0.01, name)
                if a.page.fade > 0.05 || b.page.fade > 0.05 {
                    XCTAssertEqual(a.page.zoom, b.page.zoom, accuracy: 0.01, "\(name): zoom")
                    XCTAssertEqual(a.page.object ?? 0, b.page.object ?? 0, accuracy: 0.5, "\(name): the dragged item")
                }
                XCTAssertEqual(a.touches.count, b.touches.count, name)
                for (x, y) in zip(a.touches, b.touches) {
                    XCTAssertEqual(x.alpha, y.alpha, accuracy: 0.01, "\(name): a finger")
                    if x.alpha > 0.01 {
                        XCTAssertEqual(x.u, y.u, accuracy: 0.01, name)
                        XCTAssertEqual(x.v, y.v, accuracy: 0.01, name)
                    }
                }
                let moving = { (f: StoryFrame) in f.ripples.filter { $0.progress > 0 && $0.progress < 1 }.count }
                XCTAssertEqual(moving(a), moving(b), "\(name): ripples")
            }
        }
    }

    func testTheStoriesThatCantCarryOnStartOverUnseen() {
        for (p, before) in [(SettingPreview.tapAndDrag, 4.0), (.twoFingerDrag, 3.8), (.keyZoom, 3.2)] {
            XCTAssertEqual(p.frame(at: 0).page.fade, 0, accuracy: 0.001, "\(p) starts unseen")
            XCTAssertEqual(p.frame(at: p.loop).page.fade, 0, accuracy: 0.001, "\(p) ends unseen")
            XCTAssertEqual(p.frame(at: p.loop).slide, 0, accuracy: 0.001, "\(p): the mouse is back in the middle")
            XCTAssertLessThanOrEqual(p.loop, before + 0.4, "\(p) grows by at most 0.4 s")
        }
        let drag = SettingPreview.tapAndDrag
        XCTAssertEqual(drag.frame(at: 2.6).page.fade, 1, accuracy: 0.001, "the drop is shown in full")
        XCTAssertGreaterThan(drag.frame(at: 3.1).slide, 1.2, "the mouse stays put until the finger has lifted")
        XCTAssertEqual(SettingPreview.keyZoom.frame(at: 2.5).page.fade, 1, accuracy: 0.001, "the zoom is shown in full")
    }
}

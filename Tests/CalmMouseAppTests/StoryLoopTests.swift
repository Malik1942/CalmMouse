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
}

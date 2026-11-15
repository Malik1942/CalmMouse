import XCTest
@testable import CalmMouseCore

/// Reverse direction for the Magic Mouse alone. macOS has one "natural scrolling" switch for
/// the trackpad and every mouse, so someone who wants it on the trackpad gets it here too.
final class MagicMouseReverseTests: XCTestCase {

    private func swipe(_ t: TimeInterval, _ phase: ScrollPhase, momentum: MomentumPhase = .none,
                       dy: Double = 3, mods: ModifierCombo = []) -> ScrollEvent {
        ScrollEvent(timestamp: t, fromMagicMouse: true, isContinuous: true, phase: phase,
                    momentum: momentum, deltaY: dy, modifiers: mods)
    }

    private func reversing(_ edit: (inout BlockerConfig) -> Void = { _ in }) -> ScrollBlocker {
        var c = BlockerConfig()
        c.reverseMagicMouseScroll = true
        edit(&c)
        return ScrollBlocker(config: c)
    }

    private func flipped(_ phase: ScrollPhase, _ momentum: MomentumPhase = .none) -> ScrollDecision {
        .rewrite(ScrollRewrite(phase: phase, momentum: momentum, invert: true))
    }

    func testOffByDefaultAndChangesNothing() {
        XCTAssertFalse(BlockerConfig().reverseMagicMouseScroll)
        XCTAssertEqual(ScrollBlocker().handleScroll(swipe(0, .began)), .pass)
    }

    func testASwipeComesOutTheOtherWay() {
        let b = reversing()
        XCTAssertEqual(b.handleScroll(swipe(0, .began)), flipped(.began))
        XCTAssertEqual(b.handleScroll(swipe(0.02, .changed)), flipped(.changed))
        XCTAssertEqual(b.handleScroll(swipe(0.04, .ended, dy: 0)), flipped(.ended))
    }

    func testTheGlideAfterASwipeIsReversedToo() {
        let b = reversing()
        _ = b.handleScroll(swipe(0, .began))
        _ = b.handleScroll(swipe(0.02, .ended, dy: 0))
        XCTAssertEqual(b.handleScroll(swipe(0.05, .none, momentum: .began)), flipped(.none, .began))
    }

    func testABlockedSwipeStaysBlocked() {
        let b = reversing()
        b.magicMouseButton(0, isDown: true, at: 0)
        XCTAssertEqual(b.handleScroll(swipe(0.01, .began)), .drop)
    }

    func testHoldingTheInvertKeyCancelsItOut() {
        let b = reversing { $0.modifierActions = [.option: .invert] }
        guard case .rewrite(let r) = b.handleScroll(swipe(0, .began, mods: .option)) else {
            return XCTFail("still a rewrite: the held key is taken off")
        }
        XCTAssertFalse(r.invert, "inverted twice is the normal direction")
    }

    func testWheelMiceAndTrackpadsAreLeftAlone() {
        let b = reversing()
        let tick = ScrollEvent(timestamp: 0, fromMagicMouse: false, isContinuous: false,
                               deltaY: -10, fromWheelMouse: true)
        XCTAssertEqual(b.handleScroll(tick), .pass)
        let trackpad = ScrollEvent(timestamp: 0, fromMagicMouse: false, isContinuous: true,
                                   phase: .began, deltaY: 3)
        XCTAssertEqual(b.handleScroll(trackpad), .pass)
    }
}

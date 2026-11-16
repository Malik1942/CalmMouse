import XCTest
@testable import CalmMouse

/// Pointing at a row fades its story in on the stage, leaving fades it out, and moving to another
/// row hands one over to the other. Nothing a story adds appears or vanishes in a single frame.
final class MouseStageFadeTests: XCTestCase {
    func testAStoryFadesInWhenPointedAt() {
        let model = MouseStageModel()
        XCTAssertEqual(model.advance(to: 100, preview: .tapToClick).storyAlpha, 0, accuracy: 0.001)
        XCTAssertEqual(model.advance(to: 100.1, preview: .tapToClick).storyAlpha, 0.75, accuracy: 0.001)
        XCTAssertEqual(model.advance(to: 100.3, preview: .tapToClick).storyAlpha, 1, accuracy: 0.001)
    }

    func testLeavingFadesTheLastFrameOut() {
        let model = MouseStageModel()
        _ = model.advance(to: 100, preview: .momentum(on: true))
        let last = model.advance(to: 101, preview: .momentum(on: true)).story
        let left = model.advance(to: 101.5, preview: nil)
        XCTAssertNil(left.story)
        XCTAssertEqual(left.outgoing, last, "it freezes on what was showing")
        XCTAssertEqual(left.outgoingAlpha, 1, accuracy: 0.001)
        XCTAssertEqual(model.advance(to: 101.6, preview: nil).outgoingAlpha, 0.25, accuracy: 0.001)
        XCTAssertNil(model.advance(to: 101.75, preview: nil).outgoing)
    }

    func testMovingToAnotherRowHandsOver() {
        let model = MouseStageModel()
        _ = model.advance(to: 100, preview: .tapToClick)
        _ = model.advance(to: 101, preview: .tapToClick)
        for dt in [0.0, 0.05, 0.1, 0.15] {
            let s = model.advance(to: 101.5 + dt, preview: .keyZoom)
            XCTAssertNotNil(s.outgoing)
            XCTAssertNotNil(s.story)
            XCTAssertEqual(s.outgoingAlpha + s.storyAlpha, 1, accuracy: 0.001)
        }
    }

    func testTheClockRunsUntilAFadeOutEnds() {
        let model = MouseStageModel()
        _ = model.advance(to: 100, preview: .tapToClick)
        _ = model.advance(to: 101, preview: .tapToClick)
        _ = model.advance(to: 101.5, preview: nil)
        spin()
        XCTAssertTrue(model.animating, "the stage keeps drawing while the story fades out")
        _ = model.advance(to: 101.6, preview: nil)
        spin()
        XCTAssertTrue(model.animating)
        _ = model.advance(to: 101.8, preview: nil)
        spin()
        XCTAssertFalse(model.animating, "then it stops")
    }

    func testTheCardUnderneathKeepsThePairSolidDuringAHandOver() {
        // The page card is opaque: drawn at their own opacities, the old card under the new one
        // would let the background show through mid-swap. Drawn at `underlay`, they cover fully.
        for k in stride(from: 0.0, through: 0.2, by: 0.02) {
            let o = StoryFade.fadeOut(age: k), i = StoryFade.fadeIn(age: k)
            let u = StoryFade.underlay(outgoing: o, incoming: i)
            XCTAssertEqual(i + u * (1 - i), 1, accuracy: 0.001)
        }
        XCTAssertEqual(StoryFade.underlay(outgoing: 0.4, incoming: 0), 0.4, accuracy: 0.001, "alone, it fades as it is")
    }

    private func spin() { RunLoop.main.run(until: Date().addingTimeInterval(0.02)) }
}

import XCTest
@testable import CalmMouseCore

/// Wheel mice: which events count as one, and the two settings a wheel answers to.
final class WheelMouseTests: XCTestCase {

    /// A wheel tick as captured from a real Bluetooth wheel mouse on macOS 26:
    /// not continuous, no phase, no momentum.
    private func tick(_ t: TimeInterval, dx: Double = 0, dy: Double = -10,
                      mods: ModifierCombo = []) -> ScrollEvent {
        ScrollEvent(timestamp: t, fromMagicMouse: false, isContinuous: false,
                    deltaX: dx, deltaY: dy, modifiers: mods, fromWheelMouse: true)
    }

    private func swipe(_ t: TimeInterval, _ phase: ScrollPhase, dy: Double = 3) -> ScrollEvent {
        ScrollEvent(timestamp: t, fromMagicMouse: true, isContinuous: true, phase: phase, deltaY: dy)
    }

    private let wheel = ScrollBlocker.wheelButton
    private let inverted = ScrollDecision.rewrite(.init(phase: .none, momentum: .none, invert: true))

    // MARK: Off by default

    func testWheelIsLeftAloneUntilAWheelSettingIsTurnedOn() {
        let b = ScrollBlocker()
        XCTAssertEqual(b.handleScroll(tick(0)), .pass)
        // Even with the wheel pressed: blocking is opt-in for wheel mice.
        b.wheelMouseButton(wheel, isDown: true, at: 0.1)
        XCTAssertEqual(b.handleScroll(tick(0.2)), .pass)
    }

    func testMagicMouseSettingsNeverReachTheWheel() {
        var c = BlockerConfig()
        c.disableScrollEntirely = true
        c.blockHorizontalScroll = true
        c.deadZone = 40
        c.axisLock = true
        c.momentumEnabled = false
        c.modifierActions = [.option: .zoomCommand, .shift: .block]
        let b = ScrollBlocker(config: c)
        XCTAssertEqual(b.handleScroll(tick(0, dy: 1)), .pass)
        XCTAssertEqual(b.handleScroll(tick(0.1, dx: 5, dy: 0)), .pass, "sideways wheel scrolling is always deliberate")
        XCTAssertEqual(b.handleScroll(tick(0.2, mods: .option)), .pass)
        XCTAssertEqual(b.handleScroll(tick(0.3, mods: .shift)), .pass)
    }

    func testPerAppMagicMouseRuleDoesNotSilenceTheWheel() {
        // "Ignore Magic Mouse scrolling in Figma" must leave a wheel mouse zooming in Figma.
        let rule = AppRule(bundleID: "com.figma.Desktop", name: "Figma", disableScrollEntirely: true)
        let b = ScrollBlocker(rules: RuleSet(appRules: [rule]))
        b.setActiveApp(bundleID: "com.figma.Desktop")
        XCTAssertEqual(b.handleScroll(swipe(0, .began)), .drop)
        XCTAssertEqual(b.handleScroll(tick(0.1)), .pass)
    }

    // MARK: Direction

    func testReverseWheelDirectionInvertsEveryTick() {
        var c = BlockerConfig(); c.reverseWheelScroll = true
        let b = ScrollBlocker(config: c)
        XCTAssertEqual(b.handleScroll(tick(0, dy: -10)), inverted)
        XCTAssertEqual(b.handleScroll(tick(0.1, dx: 4, dy: 0)), inverted)
    }

    func testReverseWheelDirectionLeavesTheMagicMouseAlone() {
        var c = BlockerConfig(); c.reverseWheelScroll = true
        let b = ScrollBlocker(config: c)
        XCTAssertEqual(b.handleScroll(swipe(0, .began)), .pass)
        XCTAssertEqual(b.handleScroll(swipe(0.01, .changed)), .pass)
        XCTAssertEqual(b.handleScroll(swipe(0.02, .ended, dy: 0)), .pass)
    }

    // MARK: Pressing the wheel

    func testScrollIsSwallowedWhileTheWheelIsPressedAndBrieflyAfter() {
        var c = BlockerConfig(); c.blockScrollWhileWheelPressed = true
        let b = ScrollBlocker(config: c)
        XCTAssertEqual(b.handleScroll(tick(0)), .pass)
        b.wheelMouseButton(wheel, isDown: true, at: 1.0)
        XCTAssertTrue(b.anyButtonDown)
        XCTAssertEqual(b.handleScroll(tick(1.05)), .drop)
        b.wheelMouseButton(wheel, isDown: false, at: 1.2)
        XCTAssertFalse(b.anyButtonDown)
        // The wheel often turns one more notch as the finger lets go.
        XCTAssertEqual(b.handleScroll(tick(1.2 + ScrollBlocker.wheelReleaseGrace - 0.01)), .drop)
        XCTAssertEqual(b.handleScroll(tick(1.2 + ScrollBlocker.wheelReleaseGrace + 0.01)), .pass)
    }

    func testOnlyTheWheelButtonBlocks() {
        // Holding the left button and turning the wheel is deliberate: scrolling while
        // dragging a file, or extending a selection.
        var c = BlockerConfig(); c.blockScrollWhileWheelPressed = true
        let b = ScrollBlocker(config: c)
        for button in [0, 1, 3, 4] {
            b.wheelMouseButton(button, isDown: true, at: 0)
            XCTAssertEqual(b.handleScroll(tick(0.1)), .pass, "button \(button)")
            b.wheelMouseButton(button, isDown: false, at: 0.2)
            XCTAssertEqual(b.handleScroll(tick(0.21)), .pass, "button \(button) released")
        }
    }

    func testTheTwoMiceDoNotBlockEachOther() {
        var c = BlockerConfig(); c.blockScrollWhileWheelPressed = true
        let b = ScrollBlocker(config: c)
        // A Magic Mouse click does not stop the wheel…
        b.magicMouseButton(0, isDown: true, at: 0)
        XCTAssertEqual(b.handleScroll(tick(0.1)), .pass)
        b.magicMouseButton(0, isDown: false, at: 0.2)
        // …and a pressed wheel does not stop a Magic Mouse swipe.
        b.wheelMouseButton(wheel, isDown: true, at: 5)
        XCTAssertEqual(b.handleScroll(swipe(5.1, .began)), .pass)
        XCTAssertEqual(b.handleScroll(swipe(5.11, .changed)), .pass)
    }

    func testWheelTicksDoNotDisturbAMagicMouseGesture() {
        // Dead zone state belongs to the swipe; a wheel tick in the middle must not reset it.
        var c = BlockerConfig(); c.deadZone = 10
        let b = ScrollBlocker(config: c)
        XCTAssertEqual(b.handleScroll(swipe(0, .began, dy: 4)), .drop)
        XCTAssertEqual(b.handleScroll(tick(0.01)), .pass)
        XCTAssertEqual(b.handleScroll(swipe(0.02, .changed, dy: 4)), .drop)          // 8 < 10
        XCTAssertEqual(b.handleScroll(swipe(0.03, .changed, dy: 4)),
                       .rewrite(.init(phase: .began, momentum: .none)))
    }

    func testMissedWheelReleaseIsRecoveredByTheSafetyValve() {
        var c = BlockerConfig(); c.blockScrollWhileWheelPressed = true
        let b = ScrollBlocker(config: c)
        b.wheelMouseButton(wheel, isDown: true, at: 0)
        b.forceReleaseAllButtons(at: 10)
        XCTAssertFalse(b.anyButtonDown)
        XCTAssertEqual(b.handleScroll(tick(10 + ScrollBlocker.wheelReleaseGrace + 0.01)), .pass)
    }

    func testWheelSettingsSurviveCoding() throws {
        var c = BlockerConfig()
        c.reverseWheelScroll = true
        c.blockScrollWhileWheelPressed = true
        let decoded = try JSONDecoder().decode(BlockerConfig.self, from: JSONEncoder().encode(c))
        XCTAssertEqual(decoded, c)
        // A config saved before wheel support existed decodes with both off.
        let old = try JSONDecoder().decode(BlockerConfig.self, from: Data(#"{"deadZone":4}"#.utf8))
        XCTAssertFalse(old.reverseWheelScroll)
        XCTAssertFalse(old.blockScrollWhileWheelPressed)
    }

    // MARK: Smooth scrolling

    private func smoothing(speed: Double = 3, reverse: Bool = false) -> ScrollBlocker {
        var c = BlockerConfig()
        c.smoothWheelScroll = true
        c.wheelScrollSpeed = speed
        c.reverseWheelScroll = reverse
        return ScrollBlocker(config: c)
    }

    func testSmoothingTurnsATickIntoADistanceToGlide() {
        let b = smoothing()
        // A slow single notch arrives from macOS as one pixel. The floor makes it worth turning.
        XCTAssertEqual(b.handleScroll(tick(0, dy: -1)), .smooth(dx: 0, dy: -105))
        // A fast spin keeps the acceleration macOS already gave it.
        XCTAssertEqual(b.handleScroll(tick(0.03, dy: -51)), .smooth(dx: 0, dy: -153))
        XCTAssertEqual(b.handleScroll(tick(0.06, dx: 10, dy: 0)), .smooth(dx: 105, dy: 0))
    }

    func testSmoothingFollowsTheSpeedAndDirectionSettings() {
        XCTAssertEqual(smoothing(speed: 1).handleScroll(tick(0, dy: -1)), .smooth(dx: 0, dy: -35))
        XCTAssertEqual(smoothing(speed: 6).handleScroll(tick(0, dy: 1)), .smooth(dx: 0, dy: 210))
        XCTAssertEqual(smoothing(reverse: true).handleScroll(tick(0, dy: -1)), .smooth(dx: 0, dy: 105))
    }

    func testZoomKeysBypassSmoothing() {
        // ⌘, ⌃ and ⌥ with the wheel mean zoom, which counts notches: one notch must stay one.
        let b = smoothing()
        for key: ModifierCombo in [.command, .control, .option, [.command, .shift]] {
            XCTAssertEqual(b.handleScroll(tick(0, mods: key)), .pass, "\(key.label)")
        }
        // Direction still follows the setting when a tick is passed through whole.
        XCTAssertEqual(smoothing(reverse: true).handleScroll(tick(0, mods: .command)), inverted)
    }

    func testShiftGlidesSideways() {
        let b = smoothing()
        XCTAssertEqual(b.handleScroll(tick(0, dy: -1, mods: .shift)), .smooth(dx: -105, dy: 0))
        // Already sideways (a tilt wheel, or the system turned it): left as it is.
        XCTAssertEqual(b.handleScroll(tick(0.1, dx: -1, dy: 0, mods: .shift)), .smooth(dx: -105, dy: 0))
    }

    func testPressedWheelStillBlocksWhenSmoothing() {
        var c = BlockerConfig()
        c.smoothWheelScroll = true
        c.blockScrollWhileWheelPressed = true
        let b = ScrollBlocker(config: c)
        b.wheelMouseButton(wheel, isDown: true, at: 0)
        XCTAssertEqual(b.handleScroll(tick(0.1)), .drop)
    }

    /// Runs the smoother at a fixed frame rate until it goes idle.
    private func play(_ s: inout WheelSmoother, fps: Double = 120, maxFrames: Int = 600) -> [WheelSmoother.Step] {
        var steps: [WheelSmoother.Step] = []
        while let step = s.step(dt: 1 / fps), steps.count < maxFrames { steps.append(step) }
        return steps
    }

    func testGlideScrollsExactlyTheDistanceAskedFor() {
        for fps in [60.0, 120.0] {
            var s = WheelSmoother()
            s.add(dx: 0, dy: -105)
            let steps = play(&s, fps: fps)
            XCTAssertEqual(steps.reduce(0) { $0 + $1.y }, -105, "\(fps) fps")
            XCTAssertEqual(steps.reduce(0) { $0 + $1.x }, 0)
            XCTAssertTrue(s.isIdle)
            // Smooth means spread over frames, but not left dribbling: done well inside a second.
            XCTAssertGreaterThan(steps.count, 8, "\(fps) fps")
            XCTAssertLessThan(Double(steps.count) / fps, 0.7, "\(fps) fps")
        }
    }

    func testGlideStartsFastAndEasesOff() {
        var s = WheelSmoother()
        s.add(dx: 0, dy: 300)
        let steps = play(&s)
        XCTAssertGreaterThan(steps.count, 30)
        XCTAssertGreaterThan(steps[0].y, steps[10].y)
        XCTAssertGreaterThan(steps[10].y, steps[30].y)
        XCTAssertTrue(steps.allSatisfy { $0.y >= 0 }, "a glide never doubles back")
    }

    func testTicksAddUpWhileGliding() {
        var s = WheelSmoother()
        s.add(dx: 0, dy: -105)
        var total = 0
        for _ in 0..<5 { total += s.step(dt: 1 / 120.0)?.y ?? 0 }
        s.add(dx: 0, dy: -105)
        total += play(&s).reduce(0) { $0 + $1.y }
        XCTAssertEqual(total, -210)
    }

    func testTurningTheWheelBackStopsTheOldGlideAtOnce() {
        var s = WheelSmoother()
        s.add(dx: 0, dy: -400)
        for _ in 0..<6 { _ = s.step(dt: 1 / 120.0) }
        s.add(dx: 0, dy: 105)
        let after = play(&s)
        XCTAssertTrue(after.allSatisfy { $0.y >= 0 }, "nothing more goes the old way")
        XCTAssertEqual(after.reduce(0) { $0 + $1.y }, 105)
    }

    func testFractionalDistanceSettlesAndGoesIdle() {
        var s = WheelSmoother()
        s.add(dx: 87.5, dy: 0)
        let total = play(&s).reduce(0) { $0 + $1.x }
        XCTAssertTrue(total == 87 || total == 88, "\(total)")
        XCTAssertTrue(s.isIdle)
    }

    func testCancelStopsThePageWhereItIs() {
        var s = WheelSmoother()
        s.add(dx: 0, dy: 500)
        _ = s.step(dt: 1 / 120.0)
        s.cancel()
        XCTAssertTrue(s.isIdle)
        XCTAssertNil(s.step(dt: 1 / 120.0))
    }

    func testAStalledFrameClockDoesNotFireOneHugeJump() {
        var s = WheelSmoother()
        s.add(dx: 0, dy: 1000)
        let first = s.step(dt: 5)       // the frame clock was held up for five seconds
        XCTAssertLessThan(first?.y ?? 0, 800)
        XCTAssertFalse(s.isIdle)
    }

    func testSmoothSettingsSurviveCoding() throws {
        var c = BlockerConfig()
        c.smoothWheelScroll = true
        c.wheelScrollSpeed = 4.5
        XCTAssertEqual(try JSONDecoder().decode(BlockerConfig.self, from: JSONEncoder().encode(c)), c)
        let old = try JSONDecoder().decode(BlockerConfig.self, from: Data(#"{"deadZone":4}"#.utf8))
        XCTAssertFalse(old.smoothWheelScroll)
        XCTAssertEqual(old.wheelScrollSpeed, 3)
    }

    // MARK: A zoom notch stops the glide

    func testAZoomNotchInterruptsAGlideInFlight() {
        // ⌘, ⌃ and ⌥ ticks are never smoothed. One arriving mid-glide means the user has
        // switched to zooming, so the page must stop gliding underneath it.
        XCTAssertTrue(ScrollBlocker.interruptsGlide(tick(0, mods: .command)))
        XCTAssertTrue(ScrollBlocker.interruptsGlide(tick(0, mods: .control)))
        XCTAssertTrue(ScrollBlocker.interruptsGlide(tick(0, mods: .option)))
        // Plain and Shift ticks feed the glide instead.
        XCTAssertFalse(ScrollBlocker.interruptsGlide(tick(0)))
        XCTAssertFalse(ScrollBlocker.interruptsGlide(tick(0, mods: .shift)))
        // A Magic Mouse gesture has nothing to do with the wheel's glide.
        var magic = swipe(0, .changed)
        magic.modifiers = .command
        XCTAssertFalse(ScrollBlocker.interruptsGlide(magic))
    }

    // MARK: Which kind of mouse is this?

    private let magicMouse = PointerIdentity(isMagicMouse: true, isTrackpad: false, isMouse: true)
    private let wheelMouse = PointerIdentity(isMagicMouse: false, isTrackpad: false, isMouse: true)
    private let trackpad = PointerIdentity(isMagicMouse: false, isTrackpad: true, isMouse: true)
    private let somethingElse = PointerIdentity(isMagicMouse: false, isTrackpad: false, isMouse: false)

    private func kind(_ id: PointerIdentity?, _ event: PointerEventKind,
                      _ setting: MouseTypeSetting = .automatic, legacy: Bool = false) -> MouseKind? {
        MouseClassifier.kind(of: id, event: event, setting: setting, unknownContinuousIsMagicMouse: legacy)
    }
    private let gesture = PointerEventKind.scroll(isContinuous: true)
    private let ticks = PointerEventKind.scroll(isContinuous: false)

    func testAutomaticTellsTheDevicesApart() {
        XCTAssertEqual(kind(magicMouse, gesture), .magicMouse)
        XCTAssertEqual(kind(magicMouse, .button), .magicMouse)
        XCTAssertEqual(kind(wheelMouse, ticks), .wheelMouse)
        XCTAssertEqual(kind(wheelMouse, .button), .wheelMouse)
        XCTAssertNil(kind(somethingElse, ticks))
        XCTAssertNil(kind(nil, ticks), "an unidentified sender is left alone")
        XCTAssertNil(kind(nil, gesture))
        XCTAssertNil(kind(nil, .button))
    }

    func testATrackpadIsNeverManagedWhateverTheSetting() {
        for setting in MouseTypeSetting.allCases {
            for event in [gesture, ticks, .button] {
                XCTAssertNil(kind(trackpad, event, setting), "\(setting) \(event)")
            }
        }
    }

    func testGestureScrollingFromANonMagicMouseIsLeftAlone() {
        // Some touch surface we don't know: not a wheel, so not ours.
        XCTAssertNil(kind(wheelMouse, gesture))
        XCTAssertNil(kind(wheelMouse, gesture, .wheelMouse))
    }

    func testForcedMagicMouseTakesOverAMouseDetectionGotWrong() {
        XCTAssertEqual(kind(magicMouse, gesture, .magicMouse), .magicMouse)
        // A Magic Mouse CalmMouse identified as some other mouse: the user's choice wins.
        XCTAssertEqual(kind(wheelMouse, gesture, .magicMouse), .magicMouse)
        XCTAssertEqual(kind(wheelMouse, .button, .magicMouse), .magicMouse)
        // A Magic Mouse never sends wheel ticks, so a real wheel mouse stays untouched.
        XCTAssertNil(kind(wheelMouse, ticks, .magicMouse))
        // The choice is about mice: anything that doesn't declare itself one is left alone.
        XCTAssertNil(kind(somethingElse, gesture, .magicMouse))
        XCTAssertNil(kind(somethingElse, .button, .magicMouse))
        XCTAssertEqual(kind(nil, gesture, .magicMouse), .magicMouse)
        XCTAssertEqual(kind(nil, .button, .magicMouse), .magicMouse)
        XCTAssertNil(kind(nil, ticks, .magicMouse), "wheel ticks are not a Magic Mouse")
    }

    func testForcedWheelManagesOnlyThatAndAdoptsUnknownTicks() {
        XCTAssertEqual(kind(wheelMouse, ticks, .wheelMouse), .wheelMouse)
        XCTAssertNil(kind(magicMouse, gesture, .wheelMouse))
        XCTAssertNil(kind(magicMouse, .button, .wheelMouse))
        XCTAssertEqual(kind(nil, ticks, .wheelMouse), .wheelMouse)
        XCTAssertEqual(kind(nil, .button, .wheelMouse), .wheelMouse)
        XCTAssertNil(kind(nil, gesture, .wheelMouse))
    }

    func testOldUnknownContinuousFallbackStillWorks() {
        // `defaults write … treatUnknownContinuousAsMagicMouse -bool true`, documented in the README.
        XCTAssertEqual(kind(nil, gesture, legacy: true), .magicMouse)
        XCTAssertEqual(kind(nil, .button, legacy: true), .magicMouse)
        XCTAssertNil(kind(nil, ticks, legacy: true))
        XCTAssertNil(kind(trackpad, gesture, legacy: true))
    }

    // MARK: Tracking speed

    func testTrackingSpeedMapsToResolution() {
        XCTAssertEqual(TrackingSpeed.resolution(forSpeed: 0), 400 << 16, "the middle changes nothing")
        XCTAssertEqual(TrackingSpeed.resolution(forSpeed: 1), 100 << 16, "fastest: a lower DPI, four times the travel")
        XCTAssertEqual(TrackingSpeed.resolution(forSpeed: -1), 1600 << 16)
        XCTAssertEqual(TrackingSpeed.resolution(forSpeed: 9), 100 << 16, "out of range is clamped")
    }

    // MARK: What the settings window shows

    func testSettingsDefaultToTheMagicMouseAndAddTheWheelWhenOneIsConnected() {
        func shown(_ setting: MouseTypeSetting, _ connected: Set<MouseKind>, _ remembered: Set<MouseKind>) -> Set<MouseKind> {
            MouseClassifier.kindsToShow(setting: setting, connected: connected, remembered: remembered)
        }
        XCTAssertEqual(shown(.automatic, [], []), [.magicMouse], "nothing ever seen: the Magic Mouse it is")
        XCTAssertEqual(shown(.automatic, [.wheelMouse], []), [.wheelMouse])
        // A sleeping Magic Mouse is off Bluetooth; its settings must not vanish.
        XCTAssertEqual(shown(.automatic, [.wheelMouse], [.magicMouse]), [.magicMouse, .wheelMouse])
        XCTAssertEqual(shown(.automatic, [], [.magicMouse]), [.magicMouse])
        // A wheel mouse that is switched off takes its page with it.
        XCTAssertEqual(shown(.automatic, [], [.wheelMouse]), [.magicMouse])
        XCTAssertEqual(shown(.magicMouse, [.wheelMouse], [.wheelMouse]), [.magicMouse])
        XCTAssertEqual(shown(.wheelMouse, [.magicMouse], [.magicMouse]), [.wheelMouse])
    }
}

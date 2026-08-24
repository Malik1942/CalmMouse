import XCTest
@testable import CalmMouseCore

/// Dead zone, momentum control, per-app rules and modifier actions.
final class FeatureTests: XCTestCase {

    private func ev(_ t: TimeInterval, _ phase: ScrollPhase = .none, momentum: MomentumPhase = .none,
                    dx: Double = 0, dy: Double = 3, mods: ModifierCombo = []) -> ScrollEvent {
        ScrollEvent(timestamp: t, fromMagicMouse: true, isContinuous: true,
                    phase: phase, momentum: momentum, deltaX: dx, deltaY: dy, modifiers: mods)
    }

    // MARK: Dead zone

    func testDeadZoneSwallowsMicroJitterAndRelabelsTheFirstRealEvent() {
        var c = BlockerConfig(); c.deadZone = 10
        let b = ScrollBlocker(config: c)
        // Tiny movements: nothing reaches the app.
        XCTAssertEqual(b.handleScroll(ev(0, .began, dy: 1)), .drop)
        XCTAssertEqual(b.handleScroll(ev(0.01, .changed, dy: 2)), .drop)
        XCTAssertEqual(b.handleScroll(ev(0.02, .changed, dy: 3)), .drop)   // cumulative 6 < 10
        // Crossing the threshold: the app sees a well-formed `began`, not a stray `changed`.
        XCTAssertEqual(b.handleScroll(ev(0.03, .changed, dy: 5)),
                       .rewrite(.init(phase: .began, momentum: .none)))
        XCTAssertEqual(b.handleScroll(ev(0.04, .changed, dy: 5)), .pass)
        XCTAssertEqual(b.handleScroll(ev(0.05, .ended, dy: 0)), .pass)
    }

    func testDeadZoneGestureThatNeverCommitsIsSwallowedIncludingItsEndAndMomentum() {
        var c = BlockerConfig(); c.deadZone = 10
        let b = ScrollBlocker(config: c)
        XCTAssertEqual(b.handleScroll(ev(0, .began, dy: 1)), .drop)
        XCTAssertEqual(b.handleScroll(ev(0.01, .changed, dy: 1)), .drop)
        XCTAssertEqual(b.handleScroll(ev(0.02, .ended, dy: 0)), .drop)
        XCTAssertEqual(b.handleScroll(ev(0.03, momentum: .began, dy: 1)), .drop)
        XCTAssertEqual(b.handleScroll(ev(0.04, momentum: .ended, dy: 0)), .drop)
        // Next gesture is judged on its own travel: 20 pt clears the zone on the `began` event
        // itself, so the event is already well-formed and passes untouched.
        XCTAssertEqual(b.handleScroll(ev(1.0, .began, dy: 20)), .pass)
        XCTAssertEqual(b.handleScroll(ev(1.01, .changed, dy: 5)), .pass)
    }

    func testDeadZoneOffByDefault() {
        let b = ScrollBlocker()
        XCTAssertEqual(b.handleScroll(ev(0, .began, dy: 1)), .pass)
    }

    // MARK: Momentum

    func testMomentumCanBeDisabledForTheMagicMouseOnly() {
        var c = BlockerConfig(); c.momentumEnabled = false
        let b = ScrollBlocker(config: c)
        XCTAssertEqual(b.handleScroll(ev(0, .began)), .pass)
        XCTAssertEqual(b.handleScroll(ev(0.01, .changed)), .pass)
        XCTAssertEqual(b.handleScroll(ev(0.02, .ended, dy: 0)), .pass)
        XCTAssertEqual(b.handleScroll(ev(0.03, momentum: .began, dy: 20)), .drop)
        XCTAssertEqual(b.handleScroll(ev(0.04, momentum: .continued, dy: 10)), .drop)
        XCTAssertEqual(b.handleScroll(ev(0.05, momentum: .ended, dy: 0)), .drop)
        // A trackpad's momentum is untouched (not from the Magic Mouse).
        var trackpad = ev(0.06, momentum: .continued, dy: 10); trackpad.fromMagicMouse = false
        XCTAssertEqual(b.handleScroll(trackpad), .pass)
    }

    // MARK: Horizontal block

    func testHorizontalScrollCanBeIgnored() {
        var c = BlockerConfig(); c.blockHorizontalScroll = true
        let b = ScrollBlocker(config: c)
        XCTAssertEqual(b.handleScroll(ev(0, .began, dx: 8, dy: 3)),
                       .rewrite(.init(phase: .began, momentum: .none, zeroX: true)))
        // Pure vertical needs no rewriting.
        XCTAssertEqual(b.handleScroll(ev(0.01, .changed, dx: 0, dy: 3)), .pass)
    }

    // MARK: Modifier actions

    func testModifierActionHorizontalSwapsAxesAndStripsTheModifier() {
        var c = BlockerConfig(); c.modifierActions = [.option: .horizontal]
        let b = ScrollBlocker(config: c)
        XCTAssertEqual(b.handleScroll(ev(0, .began, dy: 5, mods: .option)),
                       .rewrite(.init(phase: .began, momentum: .none, swapAxes: true, removeModifiers: .option)))
        // Without the modifier, nothing changes.
        XCTAssertEqual(b.handleScroll(ev(0.01, .changed, dy: 5)), .pass)
    }

    func testModifierActionZoomRelabelsAsCommandScroll() {
        var c = BlockerConfig(); c.modifierActions = [.option: .zoomCommand]
        let b = ScrollBlocker(config: c)
        XCTAssertEqual(b.handleScroll(ev(0, .began, dy: 5, mods: .option)),
                       .rewrite(.init(phase: .began, momentum: .none,
                                      addModifiers: .command, removeModifiers: .option)))
    }

    func testModifierActionBlockSwallowsScroll() {
        var c = BlockerConfig(); c.modifierActions = [[.command, .shift]: .block]
        let b = ScrollBlocker(config: c)
        XCTAssertEqual(b.handleScroll(ev(0, .began, dy: 5, mods: [.command, .shift])), .drop)
        // A different combo is unaffected.
        XCTAssertEqual(b.handleScroll(ev(0.01, .began, dy: 5, mods: .command)), .pass)
    }

    func testModifierActionInvertNegatesDeltas() {
        var c = BlockerConfig(); c.modifierActions = [.shift: .invert]
        let b = ScrollBlocker(config: c)
        XCTAssertEqual(b.handleScroll(ev(0, .began, dy: 5, mods: .shift)),
                       .rewrite(.init(phase: .began, momentum: .none, invert: true, removeModifiers: .shift)))
    }

    // MARK: Per-app rules

    func testPerAppRuleOverridesBaseConfig() {
        var base = BlockerConfig(); base.blockScrollWhileClicked = true
        let figma = AppRule(bundleID: "com.figma.Desktop", name: "Figma", disableScrollEntirely: true)
        let b = ScrollBlocker(rules: RuleSet(base: base, appRules: [figma]))

        // Default app: normal behaviour.
        b.setActiveApp(bundleID: "com.apple.Safari")
        XCTAssertEqual(b.handleScroll(ev(0, .began)), .pass)

        // Figma: Magic Mouse scrolling is ignored entirely.
        b.setActiveApp(bundleID: "com.figma.Desktop")
        XCTAssertEqual(b.handleScroll(ev(1.0, .began)), .drop)
        XCTAssertEqual(b.handleScroll(ev(1.01, .changed)), .drop)

        // Back to Safari: normal again.
        b.setActiveApp(bundleID: "com.apple.Safari")
        XCTAssertEqual(b.handleScroll(ev(2.0, .began)), .pass)
    }

    func testPerAppRuleMatchingIsCaseInsensitiveAndRespectsEnabledFlag() {
        var rule = AppRule(bundleID: "com.figma.desktop", name: "Figma", momentumEnabled: false)
        let set = RuleSet(base: BlockerConfig(), appRules: [rule])
        XCTAssertEqual(set.config(forBundleID: "COM.FIGMA.DESKTOP").momentumEnabled, false)
        XCTAssertEqual(set.config(forBundleID: "com.apple.Safari").momentumEnabled, true)
        XCTAssertEqual(set.config(forBundleID: nil).momentumEnabled, true)

        rule.enabled = false
        XCTAssertEqual(RuleSet(base: BlockerConfig(), appRules: [rule])
            .config(forBundleID: "com.figma.desktop").momentumEnabled, true)
    }

    func testPerAppRuleCanRelaxTheGlobalBlock() {
        var base = BlockerConfig(); base.blockScrollWhileClicked = true
        let rule = AppRule(bundleID: "com.apple.dt.Xcode", name: "Xcode", blockScrollWhileClicked: false)
        let b = ScrollBlocker(rules: RuleSet(base: base, appRules: [rule]))
        b.setActiveApp(bundleID: "com.apple.dt.Xcode")
        b.magicMouseButton(0, isDown: true, at: 0)
        XCTAssertEqual(b.handleScroll(ev(0.01, .began)), .pass)
    }

    func testEmptyRuleIsRecognised() {
        XCTAssertTrue(AppRule(bundleID: "a", name: "A").isEmpty)
        XCTAssertFalse(AppRule(bundleID: "a", name: "A", axisLock: true).isEmpty)
    }

    // MARK: Config round-trips (settings persistence)

    func testConfigCodableRoundTripIncludingModifierActions() throws {
        var c = BlockerConfig()
        c.deadZone = 12
        c.momentumEnabled = false
        c.modifierActions = [.option: .horizontal, [.command, .shift]: .block]
        let data = try JSONEncoder().encode(RuleSet(base: c, appRules: [
            AppRule(bundleID: "com.figma.Desktop", name: "Figma", disableScrollEntirely: true)
        ]))
        let decoded = try JSONDecoder().decode(RuleSet.self, from: data)
        XCTAssertEqual(decoded.base, c)
        XCTAssertEqual(decoded.base.action(for: .option), .horizontal)
        XCTAssertEqual(decoded.base.action(for: [.command, .shift]), .block)
        XCTAssertEqual(decoded.base.action(for: .control), .normal)
        XCTAssertEqual(decoded.appRules.first?.disableScrollEntirely, true)
    }

    // MARK: Interactions

    func testBlockWhileClickedStillWinsOverModifierAction() {
        var c = BlockerConfig(); c.modifierActions = [.option: .horizontal]
        let b = ScrollBlocker(config: c)
        b.magicMouseButton(0, isDown: true, at: 0)
        XCTAssertEqual(b.handleScroll(ev(0.01, .began, dy: 5, mods: .option)), .drop)
    }

    func testDeadZoneAndAxisLockCoexist() {
        var c = BlockerConfig(); c.deadZone = 6; c.axisLock = true; c.axisLockThreshold = 10
        let b = ScrollBlocker(config: c)
        XCTAssertEqual(b.handleScroll(ev(0, .began, dx: 1, dy: 2)), .drop)      // travel 3
        // travel 3+5=8 ≥ 6 → released as `began`; axis lock hasn't committed yet (cum 4/2 dominant 4 < 10).
        XCTAssertEqual(b.handleScroll(ev(0.01, .changed, dx: 1, dy: 4)),
                       .rewrite(.init(phase: .began, momentum: .none)))
        // Now vertical dominates past the threshold → horizontal drift is zeroed.
        XCTAssertEqual(b.handleScroll(ev(0.02, .changed, dx: 1, dy: 10)),
                       .rewrite(.init(phase: .changed, momentum: .none, zeroX: true)))
    }
}

extension FeatureTests {
    func testDiagnosticDeadZoneConfigReachesBlocker() {
        var c = BlockerConfig(); c.deadZone = 10
        let b = ScrollBlocker(config: c)
        XCTAssertEqual(b.config.deadZone, 10, "config.deadZone")
        XCTAssertEqual(b.baseConfig.deadZone, 10, "baseConfig.deadZone")
    }
}

extension FeatureTests {
    /// A gesture already in flight when settings change (or when the tap starts) must not be
    /// held back by the dead zone — we don't know how far it has already travelled.
    func testGestureJoinedMidFlightIgnoresDeadZone() {
        var c = BlockerConfig(); c.deadZone = 20
        let b = ScrollBlocker(config: c)
        XCTAssertEqual(b.handleScroll(ev(0, .changed, dy: 2)), .pass)
        XCTAssertEqual(b.handleScroll(ev(0.01, .changed, dy: 2)), .pass)
        XCTAssertEqual(b.handleScroll(ev(0.02, .ended, dy: 0)), .pass)
        // But the next properly-started gesture is subject to it again.
        XCTAssertEqual(b.handleScroll(ev(1.0, .began, dy: 2)), .drop)
    }

    /// Regression: mach-tick timestamps made the release grace ~42x too long. The blocker works
    /// in seconds, so a 200 ms grace must expire 200 ms later — not 8 seconds later.
    func testReleaseGraceIsInterpretedInSeconds() {
        var c = BlockerConfig(); c.releaseGrace = 0.2
        let b = ScrollBlocker(config: c)
        b.magicMouseButton(0, isDown: true, at: 100.0)
        b.magicMouseButton(0, isDown: false, at: 100.5)
        XCTAssertTrue(b.isBlocking(at: 100.6))    // 100 ms after release
        XCTAssertFalse(b.isBlocking(at: 100.75))  // 250 ms after release
        XCTAssertFalse(b.isBlocking(at: 101.0))
    }
}

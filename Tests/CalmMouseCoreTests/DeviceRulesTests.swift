import XCTest
@testable import CalmMouseCore

/// Which kind a device ends up handled as once the user has corrected it, and which other
/// mouse utilities overlap what CalmMouse is doing right now.
final class DeviceRulesTests: XCTestCase {

    // MARK: Treat as

    func testAutomaticKeepsWhatWasDetected() {
        XCTAssertEqual(MouseClassifier.effectiveKind(detected: .magicMouse, override: .automatic), .magicMouse)
        XCTAssertEqual(MouseClassifier.effectiveKind(detected: .wheelMouse, override: .automatic), .wheelMouse)
        XCTAssertNil(MouseClassifier.effectiveKind(detected: nil, override: .automatic))
    }

    func testTreatAsMagicMouseWinsWhateverWasDetected() {
        for detected: MouseKind? in [.magicMouse, .wheelMouse, nil] {
            XCTAssertEqual(MouseClassifier.effectiveKind(detected: detected, override: .magicMouse), .magicMouse)
        }
    }

    func testTreatAsWheelLeavesARealMagicMouseAlone() {
        // The event path does the same: a Magic Mouse under "wheel mouse" is not managed.
        XCTAssertNil(MouseClassifier.effectiveKind(detected: .magicMouse, override: .wheelMouse))
        XCTAssertEqual(MouseClassifier.effectiveKind(detected: .wheelMouse, override: .wheelMouse), .wheelMouse)
        XCTAssertEqual(MouseClassifier.effectiveKind(detected: nil, override: .wheelMouse), .wheelMouse)
    }

    // MARK: Other mouse utilities

    private func app(_ bundleID: String?, _ name: String? = nil) -> MouseUtilityConflicts.RunningApp {
        MouseUtilityConflicts.RunningApp(bundleID: bundleID, name: name)
    }

    private func names(_ running: [MouseUtilityConflicts.RunningApp],
                       _ active: Set<MouseUtilityArea>) -> [String] {
        MouseUtilityConflicts.conflicts(running: running, active: active).map(\.name)
    }

    func testAUtilityCountsOnlyWhereCalmMouseIsBusyToo() {
        let mos = [app("com.caldis.Mos")]
        XCTAssertEqual(names(mos, [.wheelScrolling]), ["Mos"])
        XCTAssertEqual(names(mos, [.magicMouseScrolling, .pointerSpeed]), [], "Mos only touches wheels")
        XCTAssertEqual(names(mos, []), [])
    }

    func testABackgroundHelperIsEnough() {
        XCTAssertEqual(names([app("com.nuebling.mac-mouse-fix.helper")], [.wheelScrolling]),
                       ["Mac Mouse Fix"])
    }

    func testAnAppAndItsHelperAreListedOnce() {
        let running = [app("com.nuebling.mac-mouse-fix"), app("com.nuebling.mac-mouse-fix.helper")]
        XCTAssertEqual(names(running, [.wheelScrolling]), ["Mac Mouse Fix"])
    }

    func testClosedSourceUtilitiesAreFoundByName() {
        XCTAssertEqual(names([app("com.example.agent", "logioptionsplus_agent")], [.pointerSpeed]),
                       ["Logi Options+"])
        XCTAssertEqual(names([app(nil, "BetterTouchTool")], [.magicMouseScrolling]), ["BetterTouchTool"])
    }

    func testUnrelatedAppsAreIgnored() {
        let running = [app("com.apple.Safari", "Safari"), app("com.calmmouse.app", "CalmMouse")]
        XCTAssertEqual(names(running, Set(MouseUtilityArea.allCases)), [])
    }
}

import XCTest
import CoreGraphics
import CalmMouseCore
@testable import CalmMouse

/// Decisions the app makes around the event tap: what a glide posts, when its frame clock has
/// stalled, which speed a mouse gets, when tap-to-click runs, and whose battery a line shows.
final class AppBehaviourTests: XCTestCase {

    // MARK: Smooth scrolling

    func testGlideFramesNeverCarryModifierKeys() {
        // A glide only starts from a tick with no ⌘, ⌃ or ⌥ (those are zoom notches and pass
        // through), and its Shift has already become sideways travel. Any of those keys held
        // now was pressed after the glide began and must not turn the rest of it into zoom.
        let live: CGEventFlags = [.maskCommand, .maskControl, .maskAlternate, .maskShift, .maskAlphaShift]
        let flags = WheelSmoothDriver.glideFlags(from: live)
        XCTAssertFalse(flags.contains(.maskCommand))
        XCTAssertFalse(flags.contains(.maskControl))
        XCTAssertFalse(flags.contains(.maskAlternate))
        XCTAssertFalse(flags.contains(.maskShift))
        XCTAssertTrue(flags.contains(.maskAlphaShift), "keys that don't change scrolling are kept")
    }

    func testFrameClockCountsAsStalledOnlyAfterALongSilence() {
        // Frames arrive every 8–17 ms. A quarter of a second without one means the display
        // link has stopped (its display went away), and waiting for it would leave the wheel dead.
        XCTAssertFalse(WheelSmoothDriver.clockStalled(lastFrame: 10, now: 10.017))
        XCTAssertFalse(WheelSmoothDriver.clockStalled(lastFrame: 10, now: 10.2))
        XCTAssertTrue(WheelSmoothDriver.clockStalled(lastFrame: 10, now: 10.3))
    }

    // MARK: Tracking speed

    private let magicMouse = DeviceIdentifier.DeviceInfo(product: "Magic Mouse", vendorID: 0x004C,
                                                          productID: 0x0269, transport: "Bluetooth")
    private let otherMouse = DeviceIdentifier.DeviceInfo(product: "MCHOSE A7 Ultra", vendorID: 0x5D,
                                                          productID: 0x4762, transport: "Bluetooth Low Energy",
                                                          usages: [.mouse])

    private func speed(_ info: DeviceIdentifier.DeviceInfo, _ type: MouseTypeSetting) -> Double? {
        PointerSpeedController.speed(for: info, mouseType: type, magicMouse: 0.5, wheelMouse: -0.5)
    }

    func testAutomaticGivesEachMouseItsOwnSpeed() {
        XCTAssertEqual(speed(magicMouse, .automatic), 0.5)
        XCTAssertEqual(speed(otherMouse, .automatic), -0.5)
    }

    func testMagicMouseTypeGivesAMisdetectedMouseTheMagicMouseSpeed() {
        // Mouse type → Magic Mouse shows only the Magic Mouse pane, so its slider is the one
        // the user can move, and it has to reach the mouse they told us about.
        XCTAssertEqual(speed(otherMouse, .magicMouse), 0.5)
        XCTAssertEqual(speed(magicMouse, .magicMouse), 0.5)
    }

    func testWheelTypeLeavesTheMagicMouseAtItsNormalSpeed() {
        // The event tap leaves a Magic Mouse alone under this setting. A speed chosen for it
        // earlier would otherwise stay on it until it reconnects, so it goes back to normal.
        XCTAssertEqual(speed(magicMouse, .wheelMouse), 0)
        XCTAssertEqual(speed(otherMouse, .wheelMouse), -0.5)
        // Never chosen: never written.
        XCTAssertNil(PointerSpeedController.speed(for: magicMouse, mouseType: .wheelMouse,
                                                  magicMouse: nil, wheelMouse: -0.5))
    }

    func testPausedCalmMouseRestoresNormalSpeedOnlyWhereItChangedIt() {
        XCTAssertEqual(PointerSpeedController.effectiveSpeed(chosen: 0.5, enabled: true), 0.5)
        XCTAssertEqual(PointerSpeedController.effectiveSpeed(chosen: 0.5, enabled: false), 0)
        // Never chosen: never written, paused or not.
        XCTAssertNil(PointerSpeedController.effectiveSpeed(chosen: nil, enabled: true))
        XCTAssertNil(PointerSpeedController.effectiveSpeed(chosen: nil, enabled: false))
    }

    // MARK: Tap to click

    func testTapToClickRunsOnlyWhereItsSwitchIsShown() {
        XCTAssertTrue(TapController.shouldRun(enabled: true, tapToClick: true, mouseType: .automatic))
        XCTAssertTrue(TapController.shouldRun(enabled: true, tapToClick: true, mouseType: .magicMouse))
        // Mouse type → Scroll Wheel hides the Magic Mouse pane and the menu item, so the
        // switch can't be reached; the Magic Mouse is left alone like everywhere else.
        XCTAssertFalse(TapController.shouldRun(enabled: true, tapToClick: true, mouseType: .wheelMouse))
        XCTAssertFalse(TapController.shouldRun(enabled: false, tapToClick: true, mouseType: .automatic))
        XCTAssertFalse(TapController.shouldRun(enabled: true, tapToClick: false, mouseType: .automatic))
    }

    // MARK: Battery

    private let home = BatteryMonitor.Reading(percent: 80, deviceName: "Home Magic Mouse")
    private let office = BatteryMonitor.Reading(percent: 12, deviceName: "Office Magic Mouse")

    func testEachMagicMouseShowsItsOwnBattery() {
        XCTAssertEqual(BatteryMonitor.percent(of: "Home Magic Mouse", in: [home, office]), 80)
        XCTAssertEqual(BatteryMonitor.percent(of: "Office Magic Mouse", in: [home, office]), 12)
        XCTAssertNil(BatteryMonitor.percent(of: "Someone Else's Mouse", in: [home, office]))
        // One Magic Mouse whose two registry names differ still gets its level.
        XCTAssertEqual(BatteryMonitor.percent(of: "Magic Mouse", in: [office]), 12)
    }

    func testTheLowBatteryWarningWatchesTheEmptiestMouse() {
        XCTAssertEqual(BatteryMonitor.lowest([home, office]), office)
        XCTAssertNil(BatteryMonitor.lowest([]))
    }
}

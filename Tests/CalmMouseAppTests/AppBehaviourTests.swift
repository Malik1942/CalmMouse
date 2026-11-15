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

    func testAMouseKeepsItsSettingsOverEitherConnection() {
        // Measured 2026-11-15: the same MCHOSE A7 Ultra is 005D:4762 over Bluetooth LE and
        // 5253:0011 through its USB receiver. Only the name stays.
        let receiver = DeviceIdentifier.DeviceInfo(product: "MCHOSE A7 Ultra", vendorID: 0x5253,
                                                   productID: 0x0011, transport: "USB", usages: [.mouse])
        XCTAssertEqual(otherMouse.settingsKey, receiver.settingsKey)
        XCTAssertEqual(otherMouse.settingsKey, "MCHOSE A7 Ultra")
    }

    func testAMouseWithNoNameIsKeptApartByItsIDs() {
        let unnamed = DeviceIdentifier.DeviceInfo(product: "", vendorID: 0x046D, productID: 0xC077, transport: "USB")
        XCTAssertEqual(unnamed.settingsKey, "046D:C077")
    }

    private let officeMouse = DeviceIdentifier.DeviceInfo(product: "Office Mouse", vendorID: 0x046D,
                                                           productID: 0xB034, transport: "Bluetooth Low Energy",
                                                           usages: [.mouse])

    private func serviceSpeed(_ info: DeviceIdentifier.DeviceInfo,
                              overrides: [String: MouseTypeSetting] = [:],
                              wheelSpeeds: [String: Double] = [:]) -> Double? {
        PointerSpeedController.speed(forService: info, legacyType: .automatic, overrides: overrides,
                                     magicMouse: 0.5, wheelTemplate: -0.5, wheelSpeeds: wheelSpeeds)
    }

    func testEachWheelMouseGetsItsOwnTrackingSpeed() {
        let speeds = [otherMouse.settingsKey: 0.75]
        XCTAssertEqual(serviceSpeed(otherMouse, wheelSpeeds: speeds), 0.75)
        XCTAssertEqual(serviceSpeed(officeMouse, wheelSpeeds: speeds), -0.5, "no speed of its own: the template")
    }

    func testTreatAsChangesOnlyThatMouse() {
        let overrides = [officeMouse.settingsKey: MouseTypeSetting.magicMouse]
        XCTAssertEqual(serviceSpeed(officeMouse, overrides: overrides), 0.5, "now a Magic Mouse")
        XCTAssertEqual(serviceSpeed(otherMouse, overrides: overrides), -0.5)
    }

    func testPausedCalmMouseRestoresNormalSpeedOnlyWhereItChangedIt() {
        XCTAssertEqual(PointerSpeedController.effectiveSpeed(chosen: 0.5, enabled: true), 0.5)
        XCTAssertEqual(PointerSpeedController.effectiveSpeed(chosen: 0.5, enabled: false), 0)
        // Never chosen: never written, paused or not.
        XCTAssertNil(PointerSpeedController.effectiveSpeed(chosen: nil, enabled: true))
        XCTAssertNil(PointerSpeedController.effectiveSpeed(chosen: nil, enabled: false))
    }

    // MARK: The device list in Settings

    private func connected(_ name: String, _ kind: MouseKind?, vendor: Int = 0x5D, product: Int = 0x4762)
        -> ConnectedMouse {
        ConnectedMouse(name: name, kind: kind,
                       info: .init(product: name, vendorID: vendor, productID: product, transport: "Bluetooth"))
    }

    private func list(_ mice: [ConnectedMouse], overrides: [String: MouseTypeSetting] = [:],
                      remembered: Bool = false) -> [String] {
        MouseEnvironment.deviceList(connected: mice, typeOverride: { overrides[$0] ?? .automatic },
                                    rememberedMagicMouse: remembered)
            .map { "\($0.name)|\($0.kind?.rawValue ?? "none")|\($0.connected ? "on" : "off")" }
    }

    func testWithNothingConnectedTheMagicMouseShows() {
        XCTAssertEqual(list([]), ["Magic Mouse|magicMouse|off"])
    }

    func testOnlyAWheelMouseShowsAlone() {
        XCTAssertEqual(list([connected("Zed Mouse", .wheelMouse)]), ["Zed Mouse|wheelMouse|on"])
    }

    func testARememberedMagicMouseStaysWhileAsleep() {
        XCTAssertEqual(list([connected("Zed Mouse", .wheelMouse)], remembered: true),
                       ["Magic Mouse|magicMouse|off", "Zed Mouse|wheelMouse|on"])
    }

    func testMagicMiceFirstThenWheelMiceByNameThenTheRest() {
        let mice = [connected("Zed Mouse", .wheelMouse), connected("Old Mouse", nil),
                    connected("Ann Mouse", .wheelMouse, product: 0x1), connected("My Magic Mouse", .magicMouse)]
        XCTAssertEqual(list(mice), ["My Magic Mouse|magicMouse|on", "Ann Mouse|wheelMouse|on",
                                    "Zed Mouse|wheelMouse|on", "Old Mouse|none|on"])
    }

    func testTreatAsMovesAMouseToItsNewKind() {
        let zed = connected("Zed Mouse", .wheelMouse)
        XCTAssertEqual(list([zed], overrides: [zed.info.settingsKey: .magicMouse], remembered: true),
                       ["Zed Mouse|magicMouse|on"], "it is the Magic Mouse now, so no stand-in")
    }

    func testAMagicMouseSetToWheelIsLeftAloneAndNotDuplicated() {
        let magic = connected("My Magic Mouse", .magicMouse, vendor: 0x4C, product: 0x269)
        XCTAssertEqual(list([magic], overrides: [magic.info.settingsKey: .wheelMouse], remembered: true),
                       ["My Magic Mouse|none|on"])
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

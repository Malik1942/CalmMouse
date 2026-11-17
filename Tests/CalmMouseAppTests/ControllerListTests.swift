import XCTest
@testable import CalmMouse

/// How connected controllers are named, numbered and listed, whatever they connect over.
final class ControllerListTests: XCTestCase {
    private func controller(_ name: String) -> ConnectedController {
        ConnectedController(name: name, hasTouchpad: name.contains("DualSense"), battery: nil)
    }

    private func hid(_ name: String, _ transport: String, supported: Bool = true) -> GamepadHID.Device {
        GamepadHID.Device(name: name, transport: transport, supported: supported)
    }

    func testEachControllerGetsItsConnection() {
        let list = GamepadController.matched(
            [controller("DualSense Wireless Controller"), controller("Xbox Wireless Controller")],
            with: [hid("Xbox Wireless Controller", "Bluetooth Low Energy"), hid("DualSense Wireless Controller", "USB")])
        XCTAssertEqual(list.map(\.transport), ["USB", "Bluetooth Low Energy"])
    }

    func testTheSameControllerOnACableAndOverBluetoothIsListedTwiceWithItsOwnKey() {
        let name = "DualSense Wireless Controller"
        let list = GamepadController.matched([controller(name), controller(name)],
                                             with: [hid(name, "USB"), hid(name, "Bluetooth")])
        XCTAssertEqual(list.map(\.transport), ["USB", "Bluetooth"])
        XCTAssertEqual(Set(list.map(\.settingsKey)).count, 2)
        XCTAssertEqual(list[0].settingsKey, "controller:\(name)", "the first keeps the plain key")
    }

    func testAnUnreadableControllerIsNotMatchedToAReadableOne() {
        let list = GamepadController.matched([controller("Gamepad")],
                                             with: [hid("Gamepad", "USB", supported: false)])
        XCTAssertNil(list[0].transport)
    }

    func testAControllerWithNoHIDMatchStillCounts() {
        let list = GamepadController.matched([controller("Game controller")], with: [])
        XCTAssertEqual(list.count, 1)
        XCTAssertNil(list[0].transport)
    }

    func testTwinControllersSayHowTheyConnectAndOthersDoNot() {
        let name = "DualSense Wireless Controller"
        var usb = controller(name); usb.transport = "USB"
        var bt = controller(name); bt.transport = "Bluetooth"; bt.ordinal = 1
        var xbox = controller("Xbox Wireless Controller"); xbox.transport = "Bluetooth"
        let entries = MouseEnvironment.controllerEntries([usb, bt, xbox])
        XCTAssertEqual(entries.map(\.detail), ["USB", L("Bluetooth"), nil])
        XCTAssertEqual(Set(entries.map(\.id)).count, 3, "every row has its own id")
        XCTAssertTrue(entries.allSatisfy { $0.controller != nil && $0.connected })
    }
}

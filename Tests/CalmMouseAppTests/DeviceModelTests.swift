import XCTest
@testable import CalmMouse
import CalmMouseCore

final class DeviceModelTests: XCTestCase {
    func testFolders() {
        XCTAssertEqual(DeviceModel.magicMouse.folder(.white), "MagicMouse/white")
        XCTAssertEqual(DeviceModel.magicMouse.folder(.black), "MagicMouse/black")
        // One colour each: the finish doesn't change them.
        XCTAssertEqual(DeviceModel.wheelMouse.folder(.black), "Devices/wheel")
        XCTAssertEqual(DeviceModel.controller.folder(.white), "Devices/controller")
    }

    func testFromDeviceKind() {
        XCTAssertEqual(DeviceModel(.magicMouse), .magicMouse)
        XCTAssertEqual(DeviceModel(.wheelMouse), .wheelMouse)
        XCTAssertEqual(DeviceModel(.controller), .controller)
    }
}

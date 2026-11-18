import XCTest
@testable import CalmMouse
import CalmMouseCore

final class DeviceDetectorTests: XCTestCase {
    private func mouse(_ name: String, _ kind: MouseKind?) -> ConnectedMouse {
        ConnectedMouse(name: name, kind: kind,
                       info: .init(product: name, vendorID: 0, productID: 0, transport: "Bluetooth"))
    }

    func testOneOfEachInAFixedOrder() {
        let found = DeviceDetector.detected(
            mice: [mouse("MX Master 3S", .wheelMouse), mouse("Magic Mouse", .magicMouse)],
            controllers: [ConnectedController(name: "DualSense Wireless Controller", hasTouchpad: true, battery: 80)],
            batteries: ["Magic Mouse": 64])
        XCTAssertEqual(found.map(\.kind), [.magicMouse, .wheelMouse, .controller])
        XCTAssertEqual(found[0].battery, 64)
        XCTAssertEqual(found[2].battery, 80)
    }

    func testSameKindTwiceShowsTheFirstAndCountsTheRest() {
        let found = DeviceDetector.detected(
            mice: [mouse("A Mouse", .wheelMouse), mouse("B Mouse", .wheelMouse)], controllers: [], batteries: [:])
        XCTAssertEqual(found, [DetectedDevice(kind: .wheelMouse, name: "A Mouse", battery: nil, more: 1)])
    }

    func testMiceCalmMouseLeavesAloneAreNotFound() {
        XCTAssertEqual(DeviceDetector.detected(mice: [mouse("Plain", nil)], controllers: [], batteries: [:]), [])
    }
}

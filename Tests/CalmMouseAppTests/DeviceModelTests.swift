import XCTest
@testable import CalmMouse
import CalmMouseCore

final class DeviceModelTests: XCTestCase {
    func testFolders() {
        XCTAssertEqual(DeviceModel.magicMouse.folder(.white), "MagicMouse/white")
        XCTAssertEqual(DeviceModel.magicMouse.folder(.black), "MagicMouse/black")
        // The wheel mouse comes in pale grey (white) and graphite (black), like the Magic Mouse.
        XCTAssertEqual(DeviceModel.wheelMouse.folder(.white), "Devices/wheel/white")
        XCTAssertEqual(DeviceModel.wheelMouse.folder(.black), "Devices/wheel/black")
        // The controller has one colour: the finish doesn't change it.
        XCTAssertEqual(DeviceModel.controller.folder(.white), "Devices/controller")
        XCTAssertEqual(DeviceModel.controller.folder(.black), "Devices/controller")
    }

    func testEachMouseRemembersItsOwnColor() {
        // A white Magic Mouse and a graphite wheel mouse can both be shown as they are.
        XCTAssertEqual(DeviceModel.magicMouse.finishKey, "magicMouseFinish")
        XCTAssertEqual(DeviceModel.wheelMouse.finishKey, "wheelMouseFinish")
        XCTAssertNil(DeviceModel.controller.finishKey)
    }

    func testFromDeviceKind() {
        XCTAssertEqual(DeviceModel(.magicMouse), .magicMouse)
        XCTAssertEqual(DeviceModel(.wheelMouse), .wheelMouse)
        XCTAssertEqual(DeviceModel(.controller), .controller)
    }
}

/// The rendered devices the app draws: a still and 72 turn frames each.
final class DeviceAssetTests: XCTestCase {
    private let resources = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        .appendingPathComponent("Resources")

    func testEveryDeviceHasAStillAndSeventyTwoFrames() {
        let folders = DeviceModel.allCases.flatMap { model in
            Set(MouseFinish.allCases.map { model.folder($0) }).sorted()
        }
        XCTAssertEqual(folders.count, 5)   // Magic Mouse ×2, wheel mouse ×2, controller
        for folder in folders {
            let dir = resources.appendingPathComponent(folder)
            XCTAssertTrue(FileManager.default.fileExists(atPath: dir.appendingPathComponent("home.webp").path), folder)
            for i in 0..<72 {
                let name = String(format: "turn-%02d.webp", i)
                XCTAssertTrue(FileManager.default.fileExists(atPath: dir.appendingPathComponent(name).path), "\(folder)/\(name)")
            }
        }
    }
}

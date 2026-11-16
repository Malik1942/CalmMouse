import XCTest
import AppKit
@testable import CalmMouse

/// Switching Settings tabs resizes the window (Devices is 770 pt wide, General and Advanced 480).
/// The tab buttons sit centred in the toolbar, so the window must grow and shrink about its
/// middle, or the button just clicked slides out from under the pointer.
final class SettingsWindowFrameTests: XCTestCase {
    private let screen = NSRect(x: 0, y: 0, width: 1512, height: 944)
    private let general = NSRect(x: 475, y: 300, width: 480, height: 500)

    func testWideningKeepsTheMiddleAndTheTop() {
        let devices = SettingsWindowFrame.resized(general, to: NSSize(width: 770, height: 640), within: screen)
        XCTAssertEqual(devices.midX, general.midX, "the tab buttons stay where they were")
        XCTAssertEqual(devices.maxY, general.maxY, "the title bar stays put; the window grows downwards")
        XCTAssertEqual(devices.size, NSSize(width: 770, height: 640))
    }

    func testGoingBackReturnsToTheSameFrame() {
        let devices = SettingsWindowFrame.resized(general, to: NSSize(width: 770, height: 640), within: screen)
        XCTAssertEqual(SettingsWindowFrame.resized(devices, to: general.size, within: screen), general)
    }

    func testAWindowNearTheLeftEdgeStaysOnScreen() {
        let atEdge = NSRect(x: 20, y: 300, width: 480, height: 500)
        let devices = SettingsWindowFrame.resized(atEdge, to: NSSize(width: 770, height: 640), within: screen)
        XCTAssertEqual(devices.minX, screen.minX)
        XCTAssertEqual(devices.maxY, atEdge.maxY)
    }

    func testAWindowNearTheRightEdgeStaysOnScreen() {
        let atEdge = NSRect(x: 1012, y: 300, width: 480, height: 500)
        let devices = SettingsWindowFrame.resized(atEdge, to: NSSize(width: 770, height: 640), within: screen)
        XCTAssertEqual(devices.maxX, screen.maxX)
    }

    func testWithoutAScreenItStillCentres() {
        let devices = SettingsWindowFrame.resized(general, to: NSSize(width: 770, height: 640), within: nil)
        XCTAssertEqual(devices.midX, general.midX)
    }
}

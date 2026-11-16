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

/// What the window does while it switches tabs, not just where it ends up. In 1.2.1 AppKit first
/// resized it from its left edge (the tab buttons jumped 145 pt sideways), then it slid back to
/// the middle over a third of a second. A switch should be one step: the new size, around the
/// same middle, under the same title bar.
final class SettingsTabSwitchTests: XCTestCase {
    private var controller: SettingsWindowController!
    private var frames: [NSRect] = []
    private var observers: [NSObjectProtocol] = []

    override func setUp() {
        super.setUp()
        controller = SettingsWindowController(settings: .shared, updater: UpdateChecker())
        let window = controller.window!
        // On screen, as AppKit only animates a visible window, but transparent: running the
        // tests shouldn't flash a Settings window.
        window.alphaValue = 0
        window.orderFront(nil)
        controller.select(.general)
        pump()
        observers = [NSWindow.didResizeNotification, NSWindow.didMoveNotification].map { name in
            NotificationCenter.default.addObserver(forName: name, object: window, queue: nil) { [unowned self] _ in
                frames.append(window.frame)
            }
        }
    }

    override func tearDown() {
        observers.forEach(NotificationCenter.default.removeObserver)
        controller.window?.close()
        controller = nil
        super.tearDown()
    }

    private func pump() { RunLoop.main.run(until: Date().addingTimeInterval(0.2)) }

    private func assertOneStep(to pane: SettingsPane, file: StaticString = #filePath, line: UInt = #line) {
        let before = controller.window!.frame
        frames = []
        controller.select(pane)
        let settled = controller.window!.frame
        let changes = frames.count
        pump()
        XCTAssertEqual(settled.width, pane.width, "fits the pane", file: file, line: line)
        XCTAssertGreaterThan(changes, 0, "the window changed size", file: file, line: line)
        XCTAssertEqual(frames.count, changes, "nothing moves once the switch is done: no animation, no correction", file: file, line: line)
        XCTAssertEqual(controller.window!.frame, settled, file: file, line: line)
        for frame in frames {
            XCTAssertEqual(frame.midX, before.midX, "the tab buttons never move, not even for a moment", file: file, line: line)
            XCTAssertEqual(frame.maxY, before.maxY, "the title bar never moves", file: file, line: line)
        }
    }

    func testGoingToDevicesIsOneStep() { assertOneStep(to: .devices) }

    func testComingBackFromDevicesIsOneStep() {
        controller.select(.devices)
        pump()
        assertOneStep(to: .advanced)
        assertOneStep(to: .devices)
        assertOneStep(to: .general)
    }
}

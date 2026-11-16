import XCTest
import AppKit
@testable import CalmMouse

/// Devices opens at about 600 pt, the height of the mouse and what it explains, and its list
/// scrolls beside it. People can drag it taller or shorter (never narrower), and it remembers.
/// General and Advanced keep the size their content needs.
final class DevicesPaneSizeTests: XCTestCase {
    private let heightKey = "settingsDevicesHeight"
    private var controller: SettingsWindowController!
    private var window: NSWindow { controller.window! }

    override func setUp() {
        super.setUp()
        UserDefaults.standard.removeObject(forKey: heightKey)
    }

    override func tearDown() {
        controller?.window?.close()
        controller = nil
        UserDefaults.standard.removeObject(forKey: heightKey)
        super.tearDown()
    }

    private func pump() { RunLoop.main.run(until: Date().addingTimeInterval(0.2)) }

    /// On screen, as AppKit only sizes a visible window, but transparent.
    private func open(on pane: SettingsPane) {
        controller = SettingsWindowController(settings: .shared, updater: UpdateChecker())
        window.alphaValue = 0
        window.orderFront(nil)
        controller.select(pane == .general ? .advanced : .general)
        pump()
        controller.select(pane)
        pump()
    }

    private var contentHeight: CGFloat { window.contentRect(forFrameRect: window.frame).height }

    func testDevicesOpensAboutSixHundredPointsTall() {
        open(on: .devices)
        XCTAssertEqual(window.frame.height, 600, accuracy: 1)
        XCTAssertEqual(window.frame.width, SettingsPane.devices.width)
    }

    func testDevicesCanBeDraggedTallerOrShorterButNotNarrower() {
        open(on: .devices)
        XCTAssertEqual(window.contentMinSize.width, SettingsPane.devices.width)
        XCTAssertEqual(window.contentMaxSize.width, SettingsPane.devices.width)
        // The column's own height: a Magic Mouse's is about 450 pt, a plain mouse's (as on CI,
        // which has no Magic Mouse) about 290.
        XCTAssertGreaterThan(window.contentMinSize.height, 200, "the left column always fits")
        XCTAssertLessThanOrEqual(window.contentMinSize.height, contentHeight)
        XCTAssertGreaterThan(window.contentMaxSize.height, contentHeight, "room to grow")
        XCTAssertTrue(window.styleMask.contains(.resizable))
        XCTAssertFalse(window.standardWindowButton(.zoomButton)?.isEnabled ?? true, "Settings doesn't zoom")
    }

    func testGeneralAndAdvancedKeepTheSizeTheirContentNeeds() {
        for pane in [SettingsPane.general, .advanced] {
            open(on: pane)
            XCTAssertEqual(window.contentMinSize, window.contentMaxSize, "\(pane) can't be resized")
            XCTAssertEqual(window.contentMinSize.height, contentHeight, accuracy: 1)
            controller.window?.close()
        }
    }

    func testDevicesRemembersTheHeightItWasDraggedTo() {
        open(on: .devices)
        var frame = window.frame
        frame.size.height += 80
        frame.origin.y -= 80
        window.setFrame(frame, display: true)
        NotificationCenter.default.post(name: NSWindow.didEndLiveResizeNotification, object: window)
        let dragged = contentHeight
        XCTAssertEqual(UserDefaults.standard.double(forKey: heightKey), Double(dragged), accuracy: 1)

        controller.window?.close()
        open(on: .devices)
        XCTAssertEqual(contentHeight, dragged, accuracy: 1, "opens again at the dragged height")
    }

    func testARememberedHeightTooShortForTheMouseIsRaised() {
        UserDefaults.standard.set(100.0, forKey: heightKey)
        open(on: .devices)
        XCTAssertEqual(contentHeight, window.contentMinSize.height, accuracy: 1)
    }

    func testTheHeightRule() {
        // What the person chose wins, within what fits.
        XCTAssertEqual(DevicesPaneHeight.content(saved: 650, standard: 512, minimum: 460, maximum: 800), 650)
        XCTAssertEqual(DevicesPaneHeight.content(saved: nil, standard: 512, minimum: 460, maximum: 800), 512)
        XCTAssertEqual(DevicesPaneHeight.content(saved: 300, standard: 512, minimum: 460, maximum: 800), 460)
        XCTAssertEqual(DevicesPaneHeight.content(saved: 900, standard: 512, minimum: 460, maximum: 800), 800)
        // A small screen: the mouse column still gets its height.
        XCTAssertEqual(DevicesPaneHeight.content(saved: nil, standard: 512, minimum: 460, maximum: 400), 460)
    }

    func testATurnedMouseHasRoomEitherSide() {
        // Side on, the mouse fills its 290 pt stage. The column gives it 15 pt more each side.
        XCTAssertGreaterThanOrEqual(Theme.Size.deviceColumn - MouseStage.size.width, 30)
        XCTAssertEqual(SettingsPane.devices.width, Theme.Size.deviceColumn + Theme.Size.paneWidth)
    }
}

/// The explanation under the mouse always has room for three lines, so pointing at another
/// setting moves nothing. Every caption, in every language, must fit in them.
final class DeviceCaptionTests: XCTestCase {
    func testEveryCaptionFitsThreeLines() throws {
        let root = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
            .deletingLastPathComponent().deletingLastPathComponent()
        let source = try String(contentsOf: root.appendingPathComponent("Sources/CalmMouse/SettingsWindow.swift"), encoding: .utf8)
        let call = try NSRegularExpression(pattern: #"caption:\s*L\("((?:[^"\\]|\\.)*)"\)"#)
        let keys = call.matches(in: source, range: NSRange(source.startIndex..., in: source)).map {
            String(source[Range($0.range(at: 1), in: source)!])
        } + [#"Point at a setting\nto see what it does."#]
        XCTAssertGreaterThan(keys.count, 10)

        let font = NSFont.systemFont(ofSize: NSFont.systemFontSize)
        let style = NSMutableParagraphStyle()
        style.lineSpacing = Theme.Space.small
        style.alignment = .center
        func height(_ text: String) -> CGFloat {
            NSAttributedString(string: text, attributes: [.font: font, .paragraphStyle: style])
                .boundingRect(with: NSSize(width: 250, height: 10_000), options: [.usesLineFragmentOrigin]).height
        }
        let threeLines = height("A\nA\nA")
        for language in ["en", "zh-Hans", "zh-Hant"] {
            let table = try String(contentsOf: root.appendingPathComponent("Resources/\(language).lproj/Localizable.strings"), encoding: .utf8)
            for key in keys {
                let escaped = NSRegularExpression.escapedPattern(for: key)
                let entry = try NSRegularExpression(pattern: "(?m)^\"\(escaped)\"\\s*=\\s*\"((?:[^\"\\\\]|\\\\.)*)\"")
                guard let match = entry.firstMatch(in: table, range: NSRange(table.startIndex..., in: table)) else { continue }
                let value = String(table[Range(match.range(at: 1), in: table)!]).replacingOccurrences(of: #"\n"#, with: "\n")
                XCTAssertLessThanOrEqual(height(value), threeLines + 0.5, "\(language): \(value)")
            }
        }
    }
}

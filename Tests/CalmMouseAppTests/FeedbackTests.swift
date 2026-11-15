import XCTest
import CalmMouseCore
@testable import CalmMouse

/// "Send Feedback…" opens a GitHub issue with the details a bug report needs. The issue is
/// public, so it must identify the hardware without carrying anything personal.
final class FeedbackTests: XCTestCase {

    private let magicMouse = ConnectedMouse(
        name: "Malik's Magic Mouse", kind: .magicMouse,
        info: .init(product: "Malik's Magic Mouse", vendorID: 0x004C, productID: 0x0269, transport: "Bluetooth"))
    private let wheelMouse = ConnectedMouse(
        name: "MCHOSE A7 Ultra", kind: .wheelMouse,
        info: .init(product: "MCHOSE A7 Ultra", vendorID: 0x5D, productID: 0x4762, transport: "Bluetooth Low Energy"))
    private let otherMouse = ConnectedMouse(
        name: "Jane's Gaming Mouse", kind: nil,
        info: .init(product: "Jane's Gaming Mouse", vendorID: 0x046D, productID: 0xC52B, transport: "USB"))

    private func details(mice: [ConnectedMouse]) -> Feedback.Details {
        Feedback.Details(version: "1.1.2", build: "10", macOS: "macOS 26.1 (25B78)", model: "Mac15,3",
                         accessibilityTrusted: true, enabled: true, tapRunning: true,
                         mouseType: .automatic, tapToClick: false, mice: mice)
    }

    func testDiagnosticsIdentifyEachMouseByKindAndHardwareID() {
        let text = Feedback.diagnostics(details(mice: [magicMouse, wheelMouse, otherMouse]))
        XCTAssertTrue(text.contains("CalmMouse 1.1.2 (10)"))
        XCTAssertTrue(text.contains("macOS 26.1 (25B78)"))
        XCTAssertTrue(text.contains("Mac15,3"))
        XCTAssertTrue(text.contains("Magic Mouse · 0x004C:0x0269 · Bluetooth"))
        XCTAssertTrue(text.contains("Wheel mouse · 0x005D:0x4762 · Bluetooth Low Energy"))
        XCTAssertTrue(text.contains("Not managed · 0x046D:0xC52B · USB"))
        XCTAssertTrue(text.contains("Mouse type: automatic"))
    }

    func testDiagnosticsNeverCarryADeviceName() {
        // A Magic Mouse's name is the one its owner gave it in Bluetooth settings.
        let text = Feedback.diagnostics(details(mice: [magicMouse, wheelMouse, otherMouse]))
        XCTAssertFalse(text.contains("Malik"))
        XCTAssertFalse(text.contains("Jane"))
        XCTAssertFalse(text.contains("MCHOSE"))
    }

    func testNoMouseConnectedSaysSo() {
        XCTAssertTrue(Feedback.diagnostics(details(mice: [])).contains("Mice: none connected"))
    }

    func testThisMacsVersionAndModelAreReadable() {
        XCTAssertTrue(Feedback.macOSVersion.hasPrefix("macOS "))
        XCTAssertTrue(Feedback.macOSVersion.contains("("), "build number from kern.osversion")
        XCTAssertNotEqual(Feedback.macModel, "unknown Mac")
    }

    func testIssueLinkOpensTheBugFormWithTheDetailsFilledIn() throws {
        let d = details(mice: [magicMouse, wheelMouse])
        let url = Feedback.issueURL(d)
        XCTAssertEqual(url.host, "github.com")
        XCTAssertEqual(url.path, "/Malik1942/CalmMouse/issues/new")
        let items = try XCTUnwrap(URLComponents(url: url, resolvingAgainstBaseURL: false)?.queryItems)
        func value(_ name: String) -> String? { items.first { $0.name == name }?.value }
        XCTAssertEqual(value("template"), "bug_report.yml")
        XCTAssertEqual(value("version"), "1.1.2")
        XCTAssertEqual(value("macos"), "macOS 26.1 (25B78)")
        XCTAssertEqual(value("diagnostics"), Feedback.diagnostics(d))
        XCTAssertFalse(url.absoluteString.contains("Malik%27s"))
        XCTAssertLessThan(url.absoluteString.count, 4000, "browsers and GitHub cut long links")
    }
}

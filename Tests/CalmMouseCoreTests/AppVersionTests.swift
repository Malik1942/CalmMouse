import XCTest
@testable import CalmMouseCore

final class AppVersionTests: XCTestCase {

    func testNewerAcrossEachComponent() {
        XCTAssertTrue(AppVersion.isNewer("0.5.0", than: "0.4.0"))
        XCTAssertTrue(AppVersion.isNewer("0.4.1", than: "0.4.0"))
        XCTAssertTrue(AppVersion.isNewer("1.0.0", than: "0.9.9"))
    }

    func testOlderAndEqualAreNotNewer() {
        XCTAssertFalse(AppVersion.isNewer("0.4.0", than: "0.5.0"))
        XCTAssertFalse(AppVersion.isNewer("0.5.0", than: "0.5.0"))
    }

    func testTagPrefixAndPaddingAreTolerated() {
        XCTAssertTrue(AppVersion.isNewer("v0.5.0", than: "0.4.0"), "git tags carry a leading v")
        XCTAssertFalse(AppVersion.isNewer("0.5", than: "0.5.0"), "0.5 IS 0.5.0")
        XCTAssertTrue(AppVersion.isNewer("0.5.1", than: "0.5"))
    }

    func testNumericNotLexicographicComparison() {
        XCTAssertTrue(AppVersion.isNewer("0.10.0", than: "0.9.0"), "10 > 9, not \"1\" < \"9\"")
    }

    func testJunkNeverLooksNewer() {
        // A garbled feed must never trigger an "update available" nag.
        XCTAssertFalse(AppVersion.isNewer("", than: "0.5.0"))
        XCTAssertFalse(AppVersion.isNewer("banana", than: "0.5.0"))
        XCTAssertTrue(AppVersion.isNewer("0.5.1-beta", than: "0.5.0"), "suffixes keep their digits")
    }
}

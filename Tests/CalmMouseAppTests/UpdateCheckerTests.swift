import XCTest
@testable import CalmMouse

/// The release-feed parser, calibrated against a real sample: this fixture is the actual
/// `releases/latest` payload GitHub served for v0.5.0 (2026-08-27), trimmed to the fields the
/// parser reads. If GitHub ever reshapes the feed, re-capture — don't hand-edit.
final class UpdateCheckerTests: XCTestCase {

    private let capturedFeed = #"""
    {
      "tag_name": "v0.5.0",
      "body": "Tapping learns the right button.\n\n## What's new since 0.4.0\n\n- **Tap to right-click.**\n\n## Install\n\nDownload `CalmMouse.zip`.",
      "html_url": "https://github.com/Malik1942/CalmMouse/releases/tag/v0.5.0",
      "assets": [
        {
          "name": "CalmMouse.zip",
          "browser_download_url": "https://github.com/Malik1942/CalmMouse/releases/download/v0.5.0/CalmMouse.zip",
          "size": 955940
        }
      ]
    }
    """#

    func testParsesTheCapturedFeed() throws {
        let release = try XCTUnwrap(UpdateChecker.parseRelease(Data(capturedFeed.utf8)))
        XCTAssertEqual(release.version, "0.5.0", "leading v stripped")
        XCTAssertEqual(release.pageURL.absoluteString,
                       "https://github.com/Malik1942/CalmMouse/releases/tag/v0.5.0")
        XCTAssertEqual(release.zipURL?.absoluteString,
                       "https://github.com/Malik1942/CalmMouse/releases/download/v0.5.0/CalmMouse.zip")
        XCTAssertTrue(release.notes.contains("Tap to right-click"))
    }

    func testReleaseWithoutTheZipAssetStillParses() throws {
        // A release published without the asset attached yet: the updater must surface it
        // (with the manual fallback) rather than choke.
        let feed = capturedFeed.replacingOccurrences(of: "CalmMouse.zip", with: "Other.dmg")
        let release = try XCTUnwrap(UpdateChecker.parseRelease(Data(feed.utf8)))
        XCTAssertNil(release.zipURL)
    }

    func testGarbageFeedParsesToNothing() {
        XCTAssertNil(UpdateChecker.parseRelease(Data("not json".utf8)))
        XCTAssertNil(UpdateChecker.parseRelease(Data("{}".utf8)))
        XCTAssertNil(UpdateChecker.parseRelease(Data()))
    }
}

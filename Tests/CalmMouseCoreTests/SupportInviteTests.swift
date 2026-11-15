import XCTest
@testable import CalmMouseCore

/// The one-time "Star on GitHub?" line in the menu: earned, shown once, never nagging.
final class SupportInviteTests: XCTestCase {
    private let day: TimeInterval = 86_400
    private let installed = Date(timeIntervalSince1970: 1_800_000_000)

    private func visible(blocked: Int, daysIn: Double, shownDaysAgo: Double? = nil,
                         done: Bool = false) -> Bool {
        let now = installed.addingTimeInterval(daysIn * day)
        return SupportInvite.isVisible(blockedTotal: blocked, firstLaunch: installed,
                                       firstShown: shownDaysAgo.map { now.addingTimeInterval(-$0 * day) },
                                       done: done, now: now)
    }

    func testNeedsAThousandBlockedScrolls() {
        XCTAssertFalse(visible(blocked: 999, daysIn: 30))
        XCTAssertTrue(visible(blocked: 1_000, daysIn: 30))
    }

    func testNeverInTheFirstWeek() {
        // Blocked scrolls are counted per event, so a busy first day can pass a thousand.
        XCTAssertFalse(visible(blocked: 5_000, daysIn: 6.9))
        XCTAssertTrue(visible(blocked: 5_000, daysIn: 7))
    }

    func testGoneForGoodOnceAnswered() {
        XCTAssertFalse(visible(blocked: 5_000, daysIn: 30, done: true))
        XCTAssertFalse(visible(blocked: 5_000, daysIn: 30, shownDaysAgo: 1, done: true))
    }

    func testFadesAWeekAfterItFirstShowed() {
        XCTAssertTrue(visible(blocked: 5_000, daysIn: 30, shownDaysAgo: 6.9))
        XCTAssertFalse(visible(blocked: 5_000, daysIn: 30, shownDaysAgo: 7))
    }
}

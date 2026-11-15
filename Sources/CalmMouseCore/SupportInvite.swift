import Foundation

/// The one-time "Star on GitHub?" line in the menu-bar menu.
///
/// It has to be earned and must never nag: it appears only once CalmMouse has blocked a
/// thousand scrolls and has been installed for a week, it goes away for good once answered
/// (the line clicked, or the star button in Settings used), and it fades on its own a week
/// after it first showed.
public enum SupportInvite {
    /// Blocked scrolls are counted per event, the way the welcome tour counts them.
    public static let blockedScrollsNeeded = 1_000
    public static let daysOfUseNeeded: TimeInterval = 7 * 86_400
    public static let shownFor: TimeInterval = 7 * 86_400

    public static func isVisible(blockedTotal: Int, firstLaunch: Date, firstShown: Date?,
                                 done: Bool, now: Date) -> Bool {
        guard !done,
              blockedTotal >= blockedScrollsNeeded,
              now.timeIntervalSince(firstLaunch) >= daysOfUseNeeded else { return false }
        guard let firstShown else { return true }
        return now.timeIntervalSince(firstShown) < shownFor
    }
}

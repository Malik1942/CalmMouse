// Port of Sources/CalmMouseCore/SupportInvite.swift. Foundation's Date → DateTimeOffset.

namespace CalmMouse.Core;

/// <summary>
/// The one-time "Star on GitHub?" line in the menu-bar menu.
///
/// It has to be earned and must never nag: it appears only once CalmMouse has blocked a
/// thousand scrolls and has been installed for a week, it goes away for good once answered
/// (the line clicked, or the star button in Settings used), and it fades on its own a week
/// after it first showed.
/// </summary>
public static class SupportInvite
{
    /// <summary>Blocked scrolls are counted per event, the way the welcome tour counts them.</summary>
    public const int BlockedScrollsNeeded = 1_000;
    public const double DaysOfUseNeeded = 7 * 86_400;
    public const double ShownFor = 7 * 86_400;

    public static bool IsVisible(int blockedTotal, DateTimeOffset firstLaunch, DateTimeOffset? firstShown,
                                 bool done, DateTimeOffset now)
    {
        if (done
            || blockedTotal < BlockedScrollsNeeded
            || !((now - firstLaunch).TotalSeconds >= DaysOfUseNeeded)) { return false; }
        if (firstShown is not DateTimeOffset shown) { return true; }
        return (now - shown).TotalSeconds < ShownFor;
    }
}

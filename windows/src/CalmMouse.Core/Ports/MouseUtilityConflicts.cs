// Port of Sources/CalmMouseCore/MouseUtilityConflicts.swift: the logic and the Mac list as they
// are. (The Windows list lives in the app, which is why MouseUtility's initializer is public
// here; Swift's memberwise one is internal.)
//
// `known` is `Known`, `conflicts(running:active:)` is `Conflicts`. Bundle IDs are compared
// ordinally; names are lower-cased with the invariant culture and searched ordinally.

namespace CalmMouse.Core;

/// <summary>The parts of a mouse's behaviour another utility can also be changing.</summary>
public enum MouseUtilityArea
{
    [RawValue("magicMouseScrolling")] MagicMouseScrolling,
    [RawValue("wheelScrolling")] WheelScrolling,
    [RawValue("pointerSpeed")] PointerSpeed,
}

public static class MouseUtilityAreaExtensions
{
    extension(MouseUtilityArea)
    {
        public static IReadOnlyList<MouseUtilityArea> AllCases =>
            [MouseUtilityArea.MagicMouseScrolling, MouseUtilityArea.WheelScrolling, MouseUtilityArea.PointerSpeed];
    }
}

/// <summary>
/// Another app that changes mouse input the way CalmMouse does. Two of them on the same
/// events can double a reverse or a glide, so the user is told rather than left guessing.
/// Equality compares the sets and the list by content, as Swift's Equatable does.
/// </summary>
public sealed record MouseUtility
{
    public string Name { get; }
    public IReadOnlySet<string> BundleIDs { get; }
    /// <summary>Lower-cased fragments of the app's name, for apps whose bundle ID couldn't be checked.</summary>
    public IReadOnlyList<string> NameMatches { get; }
    public IReadOnlySet<MouseUtilityArea> Areas { get; }

    public MouseUtility(string name, IEnumerable<string> bundleIDs, IEnumerable<string> nameMatches,
                        IEnumerable<MouseUtilityArea> areas)
    {
        Name = name;
        BundleIDs = bundleIDs.ToHashSet(StringComparer.Ordinal);
        NameMatches = nameMatches.ToArray();
        Areas = areas.ToHashSet();
    }

    public bool Equals(MouseUtility? other) =>
        other is not null
        && Name == other.Name
        && BundleIDs.SetEquals(other.BundleIDs)
        && NameMatches.SequenceEqual(other.NameMatches)
        && Areas.SetEquals(other.Areas);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name, StringComparer.Ordinal);
        hash.Add(BundleIDs.Count);
        foreach (string match in NameMatches) { hash.Add(match, StringComparer.Ordinal); }
        hash.Add(Areas.Count);
        return hash.ToHashCode();
    }
}

public static class MouseUtilityConflicts
{
    /// <summary>
    /// Bundle IDs are from each project's own source (Xcode project or build settings, read
    /// 2026-11-15). The closed-source ones are matched by name only, and those names are
    /// unverified: no copy was at hand to read.
    /// </summary>
    public static readonly IReadOnlyList<MouseUtility> Known =
    [
        new("Mac Mouse Fix",
            bundleIDs: ["com.nuebling.mac-mouse-fix", "com.nuebling.mac-mouse-fix.helper"],
            nameMatches: [], areas: [MouseUtilityArea.WheelScrolling]),
        new("LinearMouse", bundleIDs: ["com.lujjjh.LinearMouse"],
            nameMatches: [], areas: [MouseUtilityArea.WheelScrolling, MouseUtilityArea.MagicMouseScrolling, MouseUtilityArea.PointerSpeed]),
        new("Mos", bundleIDs: ["com.caldis.Mos"],
            nameMatches: [], areas: [MouseUtilityArea.WheelScrolling]),
        new("Scroll Reverser", bundleIDs: ["com.pilotmoon.scroll-reverser"],
            nameMatches: [], areas: [MouseUtilityArea.WheelScrolling, MouseUtilityArea.MagicMouseScrolling]),
        new("UnnaturalScrollWheels", bundleIDs: ["com.theron.UnnaturalScrollWheels"],
            nameMatches: [], areas: [MouseUtilityArea.WheelScrolling]),
        new("BetterTouchTool", bundleIDs: [],
            nameMatches: ["bettertouchtool"], areas: [MouseUtilityArea.MagicMouseScrolling]),
        new("SteerMouse", bundleIDs: [],
            nameMatches: ["steermouse"], areas: [MouseUtilityArea.WheelScrolling, MouseUtilityArea.PointerSpeed]),
        new("Logi Options+", bundleIDs: [],
            nameMatches: ["logioptionsplus", "logi options"], areas: [MouseUtilityArea.WheelScrolling, MouseUtilityArea.PointerSpeed]),
        new("SmoothScroll", bundleIDs: [],
            nameMatches: ["smoothscroll"], areas: [MouseUtilityArea.WheelScrolling]),
    ];

    /// <summary>On Windows `BundleID` holds the exe name.</summary>
    public record struct RunningApp
    {
        public string? BundleID { get; set; }
        public string? Name { get; set; }

        public RunningApp(string? bundleID, string? name)
        {
            BundleID = bundleID;
            Name = name;
        }
    }

    /// <summary>
    /// The known utilities among `running` that change something CalmMouse is changing too
    /// (`active`), each once, in the order of `Known`.
    /// </summary>
    public static IReadOnlyList<MouseUtility> Conflicts(IEnumerable<RunningApp> running, IReadOnlySet<MouseUtilityArea> active) =>
        Known.Where(utility =>
            utility.Areas.Overlaps(active) && running.Any(app =>
            {
                if (app.BundleID is string id && utility.BundleIDs.Contains(id)) { return true; }
                string name = (app.Name ?? "").ToLowerInvariant();
                return name.Length != 0 && utility.NameMatches.Any(match => name.Contains(match, StringComparison.Ordinal));
            })).ToList();
}

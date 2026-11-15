import Foundation

/// The parts of a mouse's behaviour another utility can also be changing.
public enum MouseUtilityArea: String, CaseIterable, Sendable {
    case magicMouseScrolling, wheelScrolling, pointerSpeed
}

/// Another app that changes mouse input the way CalmMouse does. Two of them on the same
/// events can double a reverse or a glide, so the user is told rather than left guessing.
public struct MouseUtility: Equatable, Sendable {
    public let name: String
    public let bundleIDs: Set<String>
    /// Lower-cased fragments of the app's name, for apps whose bundle ID couldn't be checked.
    public let nameMatches: [String]
    public let areas: Set<MouseUtilityArea>
}

public enum MouseUtilityConflicts {
    /// Bundle IDs are from each project's own source (Xcode project or build settings, read
    /// 2026-11-15). The closed-source ones are matched by name only, and those names are
    /// unverified: no copy was at hand to read.
    public static let known: [MouseUtility] = [
        MouseUtility(name: "Mac Mouse Fix",
                     bundleIDs: ["com.nuebling.mac-mouse-fix", "com.nuebling.mac-mouse-fix.helper"],
                     nameMatches: [], areas: [.wheelScrolling]),
        MouseUtility(name: "LinearMouse", bundleIDs: ["com.lujjjh.LinearMouse"],
                     nameMatches: [], areas: [.wheelScrolling, .magicMouseScrolling, .pointerSpeed]),
        MouseUtility(name: "Mos", bundleIDs: ["com.caldis.Mos"],
                     nameMatches: [], areas: [.wheelScrolling]),
        MouseUtility(name: "Scroll Reverser", bundleIDs: ["com.pilotmoon.scroll-reverser"],
                     nameMatches: [], areas: [.wheelScrolling, .magicMouseScrolling]),
        MouseUtility(name: "UnnaturalScrollWheels", bundleIDs: ["com.theron.UnnaturalScrollWheels"],
                     nameMatches: [], areas: [.wheelScrolling]),
        MouseUtility(name: "BetterTouchTool", bundleIDs: [],
                     nameMatches: ["bettertouchtool"], areas: [.magicMouseScrolling]),
        MouseUtility(name: "SteerMouse", bundleIDs: [],
                     nameMatches: ["steermouse"], areas: [.wheelScrolling, .pointerSpeed]),
        MouseUtility(name: "Logi Options+", bundleIDs: [],
                     nameMatches: ["logioptionsplus", "logi options"], areas: [.wheelScrolling, .pointerSpeed]),
        MouseUtility(name: "SmoothScroll", bundleIDs: [],
                     nameMatches: ["smoothscroll"], areas: [.wheelScrolling]),
    ]

    public struct RunningApp: Equatable, Sendable {
        public var bundleID: String?
        public var name: String?
        public init(bundleID: String?, name: String?) {
            self.bundleID = bundleID
            self.name = name
        }
    }

    /// The known utilities among `running` that change something CalmMouse is changing too
    /// (`active`), each once, in the order of `known`.
    public static func conflicts(running: [RunningApp], active: Set<MouseUtilityArea>) -> [MouseUtility] {
        known.filter { utility in
            !utility.areas.isDisjoint(with: active) && running.contains { app in
                if let id = app.bundleID, utility.bundleIDs.contains(id) { return true }
                let name = (app.name ?? "").lowercased()
                return !name.isEmpty && utility.nameMatches.contains { name.contains($0) }
            }
        }
    }
}

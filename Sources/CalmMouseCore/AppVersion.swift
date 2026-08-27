import Foundation

/// Dotted-version comparison for the update checker. Framework-free so it's unit-testable;
/// deliberately tiny — release tags here are always plain `MAJOR.MINOR.PATCH` (a leading `v`
/// is tolerated because the git tags carry one).
public enum AppVersion {
    /// "v0.10.1" → [0, 10, 1]. Non-numeric junk in a component keeps its leading digits
    /// ("1-beta" → 1); a fully non-numeric component is 0.
    public static func components(_ s: String) -> [Int] {
        var t = s.trimmingCharacters(in: .whitespacesAndNewlines)
        if t.hasPrefix("v") || t.hasPrefix("V") { t.removeFirst() }
        return t.split(separator: ".").map { part in
            Int(part.prefix(while: \.isNumber)) ?? 0
        }
    }

    /// True when `candidate` is strictly newer than `current`. Missing components count as 0,
    /// so "0.5" == "0.5.0" and neither is newer than the other.
    public static func isNewer(_ candidate: String, than current: String) -> Bool {
        let a = components(candidate)
        let b = components(current)
        for i in 0..<max(a.count, b.count) {
            let x = i < a.count ? a[i] : 0
            let y = i < b.count ? b[i] : 0
            if x != y { return x > y }
        }
        return false
    }
}

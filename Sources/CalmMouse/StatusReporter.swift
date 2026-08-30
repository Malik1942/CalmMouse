import Foundation

/// Writes a small JSON snapshot of runtime state to
/// ~/Library/Application Support/CalmMouse/status.json so `CalmMouse --status` (and you, and any
/// script) can see what the running app actually thinks — no guessing whether the tap is live.
///
/// Snapshots are written on demand: `--status` posts `refreshRequest` as a distributed
/// notification, the running app answers with a fresh write, and the CLI watches the file's
/// modification date to know the answer arrived. The app also writes one whenever the tap
/// lifecycle changes, so the file never sits around going stale by two-second increments.
enum StatusReporter {
    /// Posted (distributed) by the CLI to ask the running app for a fresh snapshot.
    static let refreshRequest = Notification.Name("com.calmmouse.app.refreshStatus")

    struct Snapshot: Codable {
        var updatedAt: Date
        var version: String
        var accessibilityTrusted: Bool
        var enabled: Bool
        var tapRunning: Bool
        var lastSeenDevice: String?
        var eventsSwallowed: Int
        var activeApp: String?
        var activeRule: String?
        var batteryPercent: Int?
        var blockScrollWhileClicked: Bool
        var releaseGraceMs: Int
        var deadZone: Double
        var momentumEnabled: Bool
        var tapToClick: Bool
        var tapToClickListening: Bool
        var tapsPosted: Int
        var tapRightClick: Bool
        var rightTapsPosted: Int
        var tapAndDrag: Bool
        var dragsPosted: Int
        var tapFramesReceived: Int
        var tapTouchesSeen: Int
        var tapLastRejection: String?
        var tapLastRejectionAt: String?
        var tapRejections: [String: Int]
        var settingsWindowOpen: Bool
        var scrollBlocker: String
        var dragConversionActive: Bool
    }

    static var fileURL: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("CalmMouse", isDirectory: true)
        try? FileManager.default.createDirectory(at: base, withIntermediateDirectories: true)
        return base.appendingPathComponent("status.json")
    }

    static func write(_ snapshot: Snapshot) {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        encoder.dateEncodingStrategy = .iso8601
        guard let data = try? encoder.encode(snapshot) else { return }
        try? data.write(to: fileURL, options: .atomic)
    }

    static func read() -> String? {
        guard let data = try? Data(contentsOf: fileURL) else { return nil }
        return String(data: data, encoding: .utf8)
    }

    static func lastUpdated() -> Date? {
        (try? FileManager.default.attributesOfItem(atPath: fileURL.path))?[.modificationDate] as? Date
    }
}

import Foundation

/// Writes a small JSON snapshot of runtime state to
/// ~/Library/Application Support/CalmMouse/status.json so `CalmMouse --status` (and you, and any
/// script) can see what the running app actually thinks — no guessing whether the tap is live.
enum StatusReporter {
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
        var tapAndDrag: Bool
        var dragsPosted: Int
        var tapFramesReceived: Int
        var tapTouchesSeen: Int
        var tapLastRejection: String?
        var tapLastRejectionAt: String?
        var tapRejections: [String: Int]
        var settingsWindowOpen: Bool
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
}

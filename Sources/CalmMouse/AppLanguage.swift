import AppKit

/// The language CalmMouse shows, picked in Settings → General instead of following macOS.
///
/// No translation code of our own: a bundle picks its .lproj from `AppleLanguages`, and a value
/// in the app's own defaults domain overrides the system list — the same key System Settings →
/// General → Language & Region → Applications writes. Choosing a language writes that key,
/// "System" removes it. The bundle reads it once at launch, so a change applies after a relaunch.
enum AppLanguage {
    /// Every bundled localization, each named in its own language so anyone can find theirs.
    /// Order: English first, then by code. A test keeps this in step with Resources/*.lproj.
    static let choices: [(code: String, name: String)] = [
        ("en", "English"),
        ("tr", "Türkçe"),
        ("zh-Hans", "简体中文"),
        ("zh-Hant", "繁體中文"),
    ]

    private static let key = "AppleLanguages"
    private static var domain: String { Bundle.main.bundleIdentifier ?? "com.calmmouse.app" }

    /// The picked language code, or "" while CalmMouse follows the system. Read from the app's own
    /// domain only: `UserDefaults.standard` would fall through to the global list and report the
    /// system's languages as if they were a choice.
    static var current: String {
        let own = UserDefaults.standard.persistentDomain(forName: domain)?[key] as? [String]
        return own?.first ?? ""
    }

    /// The choice the running process was launched with — what the window shows right now.
    /// Captured on first use, which is before the picker can change anything.
    static let atLaunch = current

    static func set(_ code: String) {
        if code.isEmpty {
            UserDefaults.standard.removeObject(forKey: key)
        } else {
            UserDefaults.standard.set([code], forKey: key)
        }
    }

    /// Quit and open again, as the updater does after swapping the bundle: `open` waits out our
    /// exit, then launches fresh with the new language.
    static func relaunch() {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/bin/sh")
        p.arguments = ["-c", "sleep 0.5; /usr/bin/open \"$0\"", Bundle.main.bundleURL.path]
        try? p.run()
        NSApp.terminate(nil)
    }
}

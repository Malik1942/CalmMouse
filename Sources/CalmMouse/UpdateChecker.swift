import AppKit
import Combine
import os
import CalmMouseCore

/// Checks GitHub Releases for a newer build and installs it in place.
///
/// The website's download button already points at `releases/latest`, so that feed IS the
/// version the website offers — one source of truth, no update server to run. Anonymous API
/// access is rate-limited per IP at 60 requests/hour; two scheduled checks a day don't dent it.
///
/// The install path mirrors install.sh: unzip, verify, swap the bundle, relaunch. Replacing a
/// running app's bundle is safe — the executing binary stays mapped — and downloads made by
/// the app itself carry no quarantine attribute, so the swapped-in build launches without the
/// right-click-Open dance a browser download needs.
final class UpdateChecker: ObservableObject {
    struct Release: Equatable {
        var version: String     // "0.5.0" — tag with the leading v stripped
        var notes: String       // release body, shown as the in-app "what's new"
        var pageURL: URL        // the release page, as the manual fallback
        var zipURL: URL?        // the CalmMouse.zip asset
    }

    enum State: Equatable {
        case idle
        case checking
        case upToDate
        case available(Release)
        case downloading(Double)   // 0...1
        case installing
        case failed(String)
    }

    @Published private(set) var state: State = .idle
    @Published private(set) var lastChecked: Date?

    /// Fires whenever an update appears or goes away, so the menu bar can refresh.
    var onAvailabilityChanged: (() -> Void)?

    static let feedURL = URL(string: "https://api.github.com/repos/Malik1942/CalmMouse/releases/latest")!
    static let fallbackPageURL = URL(string: "https://calmmouse.malikzhang.com/#download")!
    static var currentVersion: String {
        (Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String) ?? "0"
    }

    var availableRelease: Release? {
        if case .available(let r) = state { return r }
        return nil
    }

    private let log = Logger(subsystem: "com.calmmouse.app", category: "updates")
    private var timer: Timer?
    private var progressObservation: NSKeyValueObservation?

    // MARK: Checking

    func start() {
        // A quiet check shortly after launch, then twice a day. Background failures are
        // silent — the user only ever sees an error for a check they asked for.
        DispatchQueue.main.asyncAfter(deadline: .now() + 10) { [weak self] in
            self?.check(userInitiated: false)
        }
        timer = Timer.scheduledTimer(withTimeInterval: 12 * 3600, repeats: true) { [weak self] _ in
            self?.check(userInitiated: false)
        }
    }

    func check(userInitiated: Bool) {
        switch state {
        case .checking, .downloading, .installing: return
        default: break
        }
        if userInitiated { state = .checking }
        var request = URLRequest(url: Self.feedURL)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        URLSession.shared.dataTask(with: request) { [weak self] data, _, error in
            DispatchQueue.main.async {
                self?.finishCheck(data: data, error: error, userInitiated: userInitiated)
            }
        }.resume()
    }

    private func finishCheck(data: Data?, error: Error?, userInitiated: Bool) {
        lastChecked = Date()
        let hadUpdate = availableRelease != nil
        guard let release = data.flatMap(Self.parseRelease) else {
            if userInitiated {
                state = .failed(error?.localizedDescription ?? "Couldn't read the releases feed.")
            } else if !hadUpdate {
                state = .idle
            }
            return
        }
        if AppVersion.isNewer(release.version, than: Self.currentVersion) {
            state = .available(release)
            log.info("update available: \(release.version, privacy: .public)")
        } else {
            state = userInitiated ? .upToDate : .idle
        }
        if (availableRelease != nil) != hadUpdate { onAvailabilityChanged?() }
    }

    /// GitHub's `releases/latest` payload, reduced to what the updater needs.
    static func parseRelease(_ data: Data) -> Release? {
        struct Feed: Decodable {
            struct Asset: Decodable { var name: String; var browser_download_url: URL }
            var tag_name: String
            var body: String?
            var html_url: URL
            var assets: [Asset]
        }
        guard let feed = try? JSONDecoder().decode(Feed.self, from: data) else { return nil }
        var version = feed.tag_name
        if version.hasPrefix("v") { version.removeFirst() }
        return Release(
            version: version,
            notes: feed.body ?? "",
            pageURL: feed.html_url,
            zipURL: feed.assets.first { $0.name == "CalmMouse.zip" }?.browser_download_url)
    }

    // MARK: Installing

    func install() {
        guard case .available(let release) = state else { return }
        guard let zip = release.zipURL else {
            state = .failed("This release has no download attached — grab it from the website.")
            return
        }
        state = .downloading(0)
        let task = URLSession.shared.downloadTask(with: zip) { [weak self] url, _, error in
            guard let self else { return }
            DispatchQueue.main.async { self.progressObservation = nil }
            guard let url, error == nil else {
                DispatchQueue.main.async {
                    self.state = .failed(error?.localizedDescription ?? "Download failed.")
                }
                return
            }
            // The temp download is deleted when this callback returns — move it out first.
            let held = url.deletingLastPathComponent()
                .appendingPathComponent("CalmMouse-\(release.version).zip")
            try? FileManager.default.removeItem(at: held)
            do { try FileManager.default.moveItem(at: url, to: held) } catch {
                DispatchQueue.main.async { self.state = .failed(error.localizedDescription) }
                return
            }
            DispatchQueue.main.async { self.state = .installing }
            DispatchQueue.global(qos: .userInitiated).async {
                self.installDownloaded(zipAt: held, release: release)
            }
        }
        progressObservation = task.progress.observe(\.fractionCompleted) { [weak self] p, _ in
            DispatchQueue.main.async {
                if case .downloading = self?.state { self?.state = .downloading(p.fractionCompleted) }
            }
        }
        task.resume()
    }

    private struct InstallError: LocalizedError {
        var message: String
        var errorDescription: String? { message }
    }

    /// Background queue. Unzip → verify → swap → relaunch, with a rollback if the swap fails.
    private func installDownloaded(zipAt zip: URL, release: Release) {
        let fm = FileManager.default
        do {
            let work = fm.temporaryDirectory
                .appendingPathComponent("CalmMouseUpdate-\(UUID().uuidString)", isDirectory: true)
            try fm.createDirectory(at: work, withIntermediateDirectories: true)
            defer { try? fm.removeItem(at: work); try? fm.removeItem(at: zip) }

            // Same unzip tool as release.sh, so xattr handling round-trips identically.
            try run("/usr/bin/ditto", "-x", "-k", zip.path, work.path)
            let newApp = work.appendingPathComponent("CalmMouse.app")
            guard fm.fileExists(atPath: newApp.path) else {
                throw InstallError(message: "The downloaded archive didn't contain CalmMouse.app.")
            }

            // The download came over HTTPS from the pinned repo, but verify anyway: a truncated
            // download or a mangled asset must fail HERE, not as a half-broken installed app.
            try run("/usr/bin/codesign", "--verify", "--strict", newApp.path)
            let info = NSDictionary(contentsOf: newApp.appendingPathComponent("Contents/Info.plist"))
            guard info?["CFBundleIdentifier"] as? String == "com.calmmouse.app" else {
                throw InstallError(message: "The downloaded app isn't CalmMouse.")
            }

            let target = Bundle.main.bundleURL
            let parked = work.appendingPathComponent("previous-CalmMouse.app")
            do {
                try fm.moveItem(at: target, to: parked)
            } catch {
                throw InstallError(message: "Couldn't replace \(target.path) — "
                    + "download the update from the website instead. (\(error.localizedDescription))")
            }
            do {
                try fm.copyItem(at: newApp, to: target)
            } catch {
                try? fm.moveItem(at: parked, to: target) // roll back: never leave no app at all
                throw error
            }

            log.info("installed \(release.version, privacy: .public), relaunching")
            DispatchQueue.main.async { self.relaunch(target) }
        } catch {
            DispatchQueue.main.async { self.state = .failed(error.localizedDescription) }
        }
    }

    /// `open` waits out our exit, then launches the swapped-in bundle fresh.
    private func relaunch(_ appURL: URL) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/bin/sh")
        p.arguments = ["-c", "sleep 0.5; /usr/bin/open \"$0\"", appURL.path]
        try? p.run()
        NSApp.terminate(nil)
    }

    private func run(_ tool: String, _ args: String...) throws {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: tool)
        p.arguments = args
        let err = Pipe()
        p.standardError = err
        try p.run()
        p.waitUntilExit()
        guard p.terminationStatus == 0 else {
            let detail = String(data: err.fileHandleForReading.readDataToEndOfFile(),
                                encoding: .utf8) ?? ""
            throw InstallError(message: "\((tool as NSString).lastPathComponent) failed: "
                + detail.trimmingCharacters(in: .whitespacesAndNewlines))
        }
    }
}

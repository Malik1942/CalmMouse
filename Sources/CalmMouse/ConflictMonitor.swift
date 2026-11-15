import AppKit
import Combine
import CalmMouseCore

/// Watches for other apps that change mouse input the way CalmMouse does (see
/// `MouseUtilityConflicts`), so Settings can say so instead of leaving a doubled reverse or
/// glide unexplained. Only overlaps with something CalmMouse is doing right now count.
final class ConflictMonitor: ObservableObject {
    static let shared = ConflictMonitor()

    @Published private(set) var conflicts: [MouseUtility] = []

    private weak var settings: Settings?
    private weak var mice: MouseEnvironment?
    private var observers: [NSObjectProtocol] = []

    func start(settings: Settings, mice: MouseEnvironment) {
        self.settings = settings
        self.mice = mice
        let center = NSWorkspace.shared.notificationCenter
        for name in [NSWorkspace.didLaunchApplicationNotification, NSWorkspace.didTerminateApplicationNotification] {
            observers.append(center.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                self?.refresh()
            })
        }
        refresh()
    }

    /// Call after settings change or mice come and go: what CalmMouse is busy with may change.
    func refresh() {
        guard let settings, let mice else { return }
        let devices = mice.devices(for: settings)
        let wheelKeys = devices.filter { $0.kind == .wheelMouse }.map(\.key)
        let hasMagicMouse = devices.contains { $0.kind == .magicMouse }
        let speedChosen = (hasMagicMouse && settings.chosenMagicMouseTrackingSpeed != nil)
            || wheelKeys.contains { settings.wheelTrackingSpeed(for: $0) != nil }
        let areas = Self.activeAreas(enabled: settings.enabled, hasMagicMouse: hasMagicMouse,
                                     wheels: wheelKeys.map(settings.wheelSettings(for:)),
                                     trackingSpeedChosen: speedChosen)
        let running = NSWorkspace.shared.runningApplications.map {
            MouseUtilityConflicts.RunningApp(bundleID: $0.bundleIdentifier, name: $0.localizedName)
        }
        let found = MouseUtilityConflicts.conflicts(running: running, active: areas)
        if found != conflicts { conflicts = found }
    }

    /// What CalmMouse is changing right now. A Magic Mouse is always being looked after; a
    /// wheel mouse only once one of its options is on; pointer speed once one was chosen.
    static func activeAreas(enabled: Bool, hasMagicMouse: Bool, wheels: [WheelSettings],
                            trackingSpeedChosen: Bool) -> Set<MouseUtilityArea> {
        guard enabled else { return [] }
        var areas: Set<MouseUtilityArea> = []
        if hasMagicMouse { areas.insert(.magicMouseScrolling) }
        if wheels.contains(where: { $0.smooth || $0.reverse || $0.blockWhilePressed }) { areas.insert(.wheelScrolling) }
        if trackingSpeedChosen { areas.insert(.pointerSpeed) }
        return areas
    }

    /// "Mos is changing your mouse too. …", naming every overlapping app.
    var message: String? {
        guard !conflicts.isEmpty else { return nil }
        let names = ListFormatter.localizedString(byJoining: conflicts.map(\.name))
        return L("%@ is changing your mouse too. If something feels doubled, turn it off in one of them.", names)
    }
}

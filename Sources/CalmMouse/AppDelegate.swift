import AppKit
import ServiceManagement
import CalmMouseCore

final class AppDelegate: NSObject, NSApplicationDelegate {
    private let settings = Settings.shared
    private let blocker = ScrollBlocker()
    private lazy var tap = EventTap(blocker: blocker)
    private let battery = BatteryMonitor()
    private let tapRecognizer = TapRecognizer()
    private lazy var tapToClick = TapController(recognizer: tapRecognizer)

    private var statusItem: NSStatusItem!
    private var settingsWindow: SettingsWindowController?
    private var permissionTimer: Timer?
    private var statusTimer: Timer?
    private var lowBatteryFlag = false

    // Menu items refreshed on open.
    private let statusLine = NSMenuItem(title: "", action: nil, keyEquivalent: "")
    private let batteryLine = NSMenuItem(title: "", action: nil, keyEquivalent: "")
    private let permissionItem = NSMenuItem(title: "Grant Accessibility permission…", action: #selector(openAccessibility), keyEquivalent: "")
    private let enabledItem = NSMenuItem(title: "CalmMouse enabled", action: #selector(toggleEnabled), keyEquivalent: "")
    private let blockItem = NSMenuItem(title: "Don't scroll while clicking", action: #selector(toggleBlock), keyEquivalent: "")
    private let momentumItem = NSMenuItem(title: "Keep gliding after a swipe", action: #selector(toggleMomentum), keyEquivalent: "")
    private let axisLockItem = NSMenuItem(title: "Scroll in straight lines", action: #selector(toggleAxisLock), keyEquivalent: "")
    private let tapToClickItem = NSMenuItem(title: "Tap to click", action: #selector(toggleTapToClick), keyEquivalent: "")
    private let perAppItem = NSMenuItem(title: "Ignore scrolling in this app", action: #selector(togglePerApp), keyEquivalent: "")

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        buildStatusItem()

        // Tap-to-click context: physical Magic Mouse buttons and real scrolling both veto taps.
        tap.onMagicButton = { [weak self] isDown, t in self?.tapToClick.physicalButton(isDown: isDown, at: t) }
        tap.onMagicScroll = { [weak self] magnitude, t in self?.tapToClick.scrollActivity(deltaMagnitude: magnitude, at: t) }

        applySettings()

        NotificationCenter.default.addObserver(self, selector: #selector(settingsChanged),
                                               name: Settings.didChange, object: nil)
        NotificationCenter.default.addObserver(self, selector: #selector(batteryWarned(_:)),
                                               name: BatteryMonitor.didWarn, object: nil)
        // Per-app rules follow the frontmost app.
        NSWorkspace.shared.notificationCenter.addObserver(
            self, selector: #selector(activeAppChanged),
            name: NSWorkspace.didActivateApplicationNotification, object: nil)
        updateActiveApp()

        battery.onUpdate = { [weak self] _ in
            DispatchQueue.main.async { self?.refreshStatusIcon() }
        }
        battery.start()

        ensurePermissionThenStart()

        // First run (or permission revoked): don't sit silently in the menu bar doing nothing —
        // show the window that explains what's missing.
        if !AXIsProcessTrusted() {
            openSettings()
        }

        statusTimer = Timer.scheduledTimer(withTimeInterval: 2, repeats: true) { [weak self] _ in
            self?.writeStatusFile()
        }
    }

    /// Re-opening the app from Finder/Spotlight while it's already running shows the settings
    /// window (a menu-bar app with no windows otherwise looks like nothing happened).
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        openSettings()
        return true
    }

    func applicationWillTerminate(_ notification: Notification) {
        tap.stop()
        tapToClick.stop()
        battery.stop()
    }

    // MARK: Permission + tap lifecycle

    private func ensurePermissionThenStart() {
        let opts = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        if AXIsProcessTrustedWithOptions(opts) {
            startTapIfWanted()
            return
        }
        refreshStatusIcon()
        // Poll until the user flips the switch in System Settings; no relaunch needed.
        permissionTimer?.invalidate()
        permissionTimer = Timer.scheduledTimer(withTimeInterval: 1.5, repeats: true) { [weak self] timer in
            guard let self else { return }
            if AXIsProcessTrusted() {
                timer.invalidate()
                self.permissionTimer = nil
                self.startTapIfWanted()
            }
        }
    }

    private func startTapIfWanted() {
        if settings.enabled {
            tap.start()
        } else {
            tap.stop()
        }
        // Posting the synthetic clicks needs the same Accessibility grant as the event tap,
        // so tap-to-click rides the same lifecycle.
        if settings.enabled && settings.tapToClick {
            tapToClick.start()
        } else {
            tapToClick.stop()
        }
        refreshStatusIcon()
        writeStatusFile()
    }

    private func applySettings() {
        blocker.rules = settings.ruleSet
        tapRecognizer.config = settings.tapConfig
        tap.debugLogging = settings.debugLogging
        tap.treatUnknownContinuousAsMagicMouse = settings.treatUnknownContinuousAsMagicMouse
        battery.enabled = settings.batteryWarningEnabled
        battery.threshold = settings.batteryWarningThreshold
    }

    @objc private func settingsChanged() {
        applySettings()
        if AXIsProcessTrusted() { startTapIfWanted() } else { refreshStatusIcon() }
    }

    @objc private func activeAppChanged() { updateActiveApp() }

    private func updateActiveApp() {
        let app = NSWorkspace.shared.frontmostApplication
        blocker.setActiveApp(bundleID: app?.bundleIdentifier)
        refreshStatusIcon()
    }

    @objc private func batteryWarned(_ note: Notification) {
        lowBatteryFlag = true
        refreshStatusIcon()
    }

    // MARK: Status item

    private func buildStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        statusItem.button?.image = Self.icon(named: "magicmouse.fill")

        let menu = NSMenu()
        menu.delegate = self

        statusLine.isEnabled = false
        batteryLine.isEnabled = false
        menu.addItem(statusLine)
        menu.addItem(batteryLine)
        menu.addItem(permissionItem)
        menu.addItem(.separator())

        // Frequently-flipped switches live here; everything else is in the settings window.
        menu.addItem(enabledItem)
        menu.addItem(.separator())
        menu.addItem(blockItem)
        menu.addItem(momentumItem)
        menu.addItem(axisLockItem)
        menu.addItem(tapToClickItem)
        menu.addItem(perAppItem)
        menu.addItem(.separator())

        let settingsItem = NSMenuItem(title: "Settings…", action: #selector(openSettings), keyEquivalent: ",")
        menu.addItem(settingsItem)
        menu.addItem(NSMenuItem(title: "Quit CalmMouse", action: #selector(quit), keyEquivalent: "q"))

        for item in menu.items where item.action != nil { item.target = self }
        statusItem.menu = menu
    }

    private static func icon(named name: String) -> NSImage? {
        let image = NSImage(systemSymbolName: name, accessibilityDescription: "CalmMouse")
        image?.isTemplate = true
        return image
    }

    private func refreshStatusIcon() {
        guard let button = statusItem.button else { return }
        let trusted = AXIsProcessTrusted()

        // Always a Magic Mouse silhouette — in a crowded menu bar you need to know *which* app is
        // asking for something. State is carried by fill, tint and dimming instead of a new glyph.
        if !trusted {
            button.image = Self.icon(named: "magicmouse.fill")
            button.contentTintColor = .systemOrange
            button.toolTip = "CalmMouse needs Accessibility permission"
        } else if !settings.enabled {
            button.image = Self.icon(named: "magicmouse")
            button.contentTintColor = nil
            button.toolTip = "CalmMouse is paused"
        } else if !tap.isRunning {
            button.image = Self.icon(named: "magicmouse.fill")
            button.contentTintColor = .systemOrange
            button.toolTip = "CalmMouse couldn't start its event tap"
        } else if lowBatteryFlag {
            button.image = Self.icon(named: "magicmouse.fill")
            button.contentTintColor = .systemYellow
            button.toolTip = "CalmMouse — active · Magic Mouse battery low"
        } else {
            button.image = Self.icon(named: "magicmouse.fill")
            button.contentTintColor = nil
            button.toolTip = "CalmMouse — active"
        }
        // Only a deliberate pause dims the icon. Something that needs the user's attention must
        // stay at full strength — a faint warning in a crowded menu bar is no warning at all.
        button.appearsDisabled = trusted && !settings.enabled
    }

    private func refreshMenu() {
        let trusted = AXIsProcessTrusted()
        if !trusted {
            statusLine.title = "⚠︎ Accessibility permission needed"
        } else if !settings.enabled {
            statusLine.title = "Paused"
        } else if !tap.isRunning {
            statusLine.title = "⚠︎ Event tap failed to start"
        } else if let dev = tap.lastSeenDevice {
            statusLine.title = "Active · \(dev) · \(tap.swallowedCount) scrolls blocked"
        } else {
            statusLine.title = "Active · waiting for Magic Mouse input"
        }
        permissionItem.isHidden = trusted

        if let reading = battery.latest {
            batteryLine.title = "Battery \(reading.percent)%"
            batteryLine.isHidden = false
        } else {
            batteryLine.isHidden = true
        }

        enabledItem.state = settings.enabled ? .on : .off
        blockItem.state = settings.blockScrollWhileClicked ? .on : .off
        momentumItem.state = settings.momentumEnabled ? .on : .off
        axisLockItem.state = settings.axisLock ? .on : .off
        tapToClickItem.state = settings.tapToClick ? .on : .off
        tapToClickItem.isHidden = !TapController.isSupported

        if let app = NSWorkspace.shared.frontmostApplication,
           let bundleID = app.bundleIdentifier, bundleID != Bundle.main.bundleIdentifier {
            let name = app.localizedName ?? bundleID
            perAppItem.title = "Ignore scrolling in \(name)"
            perAppItem.isEnabled = true
            perAppItem.state = (settings.rule(for: bundleID)?.disableScrollEntirely == true) ? .on : .off
            perAppItem.representedObject = [bundleID, name]
        } else {
            perAppItem.title = "Ignore scrolling in this app"
            perAppItem.isEnabled = false
            perAppItem.state = .off
            perAppItem.representedObject = nil
        }

        refreshStatusIcon()
    }

    private func writeStatusFile() {
        let app = NSWorkspace.shared.frontmostApplication
        let bundleID = app?.bundleIdentifier
        StatusReporter.write(.init(
            updatedAt: Date(),
            version: (Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String) ?? "dev",
            accessibilityTrusted: AXIsProcessTrusted(),
            enabled: settings.enabled,
            tapRunning: tap.isRunning,
            lastSeenDevice: tap.lastSeenDevice,
            eventsSwallowed: tap.swallowedCount,
            activeApp: bundleID,
            activeRule: bundleID.flatMap { settings.rule(for: $0) }?.name,
            batteryPercent: battery.latest?.percent,
            blockScrollWhileClicked: blocker.config.blockScrollWhileClicked,
            releaseGraceMs: Int(blocker.config.releaseGrace * 1000),
            deadZone: blocker.config.deadZone,
            momentumEnabled: blocker.config.momentumEnabled,
            tapToClick: settings.tapToClick,
            tapToClickListening: tapToClick.isListening,
            tapsPosted: tapToClick.tapsPosted,
            tapAndDrag: settings.tapAndDrag,
            dragsPosted: tapToClick.dragsPosted,
            tapFramesReceived: tapToClick.framesReceived,
            tapTouchesSeen: tapToClick.touchesSeen,
            tapLastRejection: tapRecognizer.lastRejection?.rawValue,
            tapLastRejectionAt: tapRecognizer.lastRejection == nil ? nil :
                String(format: "x=%.2f y=%.2f", tapRecognizer.lastRejectionX, tapRecognizer.lastRejectionY),
            tapRejections: tapRecognizer.rejectionCounts,
            settingsWindowOpen: settingsWindow?.window?.isVisible ?? false
        ))
    }

    // MARK: Actions

    @objc private func toggleEnabled() { settings.enabled.toggle() }
    @objc private func toggleBlock() { settings.blockScrollWhileClicked.toggle() }
    @objc private func toggleMomentum() { settings.momentumEnabled.toggle() }
    @objc private func toggleAxisLock() { settings.axisLock.toggle() }
    @objc private func toggleTapToClick() { settings.tapToClick.toggle() }

    @objc private func togglePerApp(_ sender: NSMenuItem) {
        guard let pair = sender.representedObject as? [String], pair.count == 2 else { return }
        let (bundleID, name) = (pair[0], pair[1])
        let currentlyDisabled = settings.rule(for: bundleID)?.disableScrollEntirely == true
        settings.setScrollDisabled(!currentlyDisabled, forBundleID: bundleID, name: name)
    }

    @objc private func openSettings() {
        if settingsWindow == nil {
            settingsWindow = SettingsWindowController(settings: settings, battery: battery)
        }
        NSApp.activate(ignoringOtherApps: true)
        settingsWindow?.showWindow(nil)
        settingsWindow?.window?.makeKeyAndOrderFront(nil)
        lowBatteryFlag = false
        refreshStatusIcon()
    }

    @objc private func openAccessibility() {
        let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")!
        NSWorkspace.shared.open(url)
    }

    @objc private func quit() { NSApp.terminate(nil) }
}

extension AppDelegate: NSMenuDelegate {
    func menuWillOpen(_ menu: NSMenu) {
        battery.poll()
        refreshMenu()
    }
}

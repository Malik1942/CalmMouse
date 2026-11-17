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
    private let updater = UpdateChecker()
    private let mice = MouseEnvironment.shared
    private let pointerSpeed = PointerSpeedController()
    private let gamepad = GamepadController.shared

    private var statusItem: NSStatusItem!
    private var settingsWindow: SettingsWindowController?
    private var onboardingWindow: OnboardingWindowController?
    private var permissionTimer: Timer?
    private var lowBatteryFlag = false

    // Menu items refreshed on open.
    private let statusLine = NSMenuItem(title: "", action: nil, keyEquivalent: "")
    /// One line per connected mouse that reports its charge; rebuilt each time the menu opens.
    private var batteryLines: [NSMenuItem] = []
    private let permissionItem = NSMenuItem(title: L("Grant Accessibility permission…"), action: #selector(openAccessibility), keyEquivalent: "")
    private let updateItem = NSMenuItem(title: "", action: #selector(openUpdate), keyEquivalent: "")
    private let enabledItem = NSMenuItem(title: L("CalmMouse enabled"), action: #selector(toggleEnabled), keyEquivalent: "")
    private let perAppItem = NSMenuItem(title: L("Ignore scrolling in this app"), action: #selector(togglePerApp), keyEquivalent: "")
    private let controllerItem = NSMenuItem(title: L("Use controller as mouse"), action: #selector(toggleControllerPointer), keyEquivalent: "")
    private let starInviteItem = NSMenuItem(title: "", action: #selector(acceptStarInvite), keyEquivalent: "")
    /// How much of `tap.swallowedCount` is already in `settings.blockedScrollsTotal`.
    private var blockedScrollsSaved = 0

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        buildStatusItem()

        // Tap-to-click context: physical Magic Mouse buttons and real scrolling both veto taps.
        tap.onMagicButton = { [weak self] isDown, t in self?.tapToClick.physicalButton(isDown: isDown, at: t) }
        tap.onMagicScroll = { [weak self] magnitude, t in self?.tapToClick.scrollActivity(deltaMagnitude: magnitude, at: t) }
        tapToClick.onDragPressChanged = { [weak self] clickState in self?.tap.syntheticDragClickState = clickState }
        // A device just sent its first event: a mouse was connected or woke up. Look again,
        // but not inside the tap callback, which the WindowServer is waiting on.
        tap.onNewDevice = { [weak self] in DispatchQueue.main.async { self?.mice.refresh() } }
        mice.refresh()
        // A mouse waking up or being connected with no window open.
        pointerSpeed.onMouseAppeared = { [weak self] in self?.mice.refresh() }

        // Options + Create held on the controller: the same switch as the menu's.
        gamepad.onToggleRequested = { [weak self] in
            guard let self else { return }
            self.settings.controllerPointer.toggle()
            NSSound(named: self.settings.controllerPointer ? "Tink" : "Pop")?.play()
        }
        gamepad.start()

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
        battery.otherMouseNames = { [weak self] in
            self?.mice.connected.filter { $0.kind != .magicMouse }.map(\.name) ?? []
        }
        battery.start()
        ConflictMonitor.shared.start(settings: settings, mice: mice)

        updater.onAvailabilityChanged = { [weak self] in self?.refreshMenu() }
        updater.start()

        ensurePermissionThenStart()

        NotificationCenter.default.addObserver(self, selector: #selector(sendFeedback),
                                               name: Feedback.sendRequested, object: nil)
        _ = settings.firstLaunchDate        // starts the invite's one-week clock
        NotificationCenter.default.addObserver(self, selector: #selector(openOnboarding),
                                               name: .calmMouseShowOnboarding, object: nil)

        // First run: the welcome tour walks through permission, presets and a try-out.
        // Later runs with permission revoked: don't sit silently in the menu bar doing
        // nothing — show the window that explains what's missing.
        if !settings.onboardingCompleted {
            openOnboarding()
        } else if !AXIsProcessTrusted() {
            openSettings()
        }

        // `CalmMouse --status` asks for a snapshot from outside the process.
        DistributedNotificationCenter.default().addObserver(
            self, selector: #selector(statusRequested),
            name: StatusReporter.refreshRequest, object: nil)
    }

    /// Re-opening the app from Finder/Spotlight while it's already running shows the settings
    /// window (a menu-bar app with no windows otherwise looks like nothing happened).
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        openSettings()
        return true
    }

    func applicationWillTerminate(_ notification: Notification) {
        saveBlockedScrolls()
        // Hand every mouse back at its normal speed; the next launch sets it again.
        applyPointerSpeeds(enabled: false)
        gamepad.setWanted(false)
        tap.stop()
        tapToClick.stop()
        battery.stop()
    }

    // MARK: Permission + tap lifecycle

    private func ensurePermissionThenStart() {
        if AXIsProcessTrusted() {
            startTapIfWanted()
            return
        }
        // Ask for the system's grant dialog only on the first ungranted launch ever.
        // Each `prompt: true` call queues a fresh dialog that survives until dismissed,
        // so prompting every launch piles them up. After the one ask, our own windows
        // (onboarding, Settings → Permission) carry the "Open System Settings…" path.
        if !settings.accessibilityPromptShown {
            settings.accessibilityPromptShown = true
            let opts = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
            _ = AXIsProcessTrustedWithOptions(opts)
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
        if TapController.shouldRun(enabled: settings.enabled, tapToClick: settings.tapToClick,
                                   mouseType: settings.mouseType) {
            tapToClick.start()
        } else {
            tapToClick.stop()
        }
        // Moving the pointer and clicking need the same grant.
        gamepad.setWanted(settings.enabled && settings.controllerPointer && AXIsProcessTrusted())
        refreshStatusIcon()
        writeStatusFile()
    }

    /// Every mouse's chosen speed, or its normal speed while CalmMouse is paused or quitting.
    private func applyPointerSpeeds(enabled: Bool) {
        let effective = { (chosen: Double?) in PointerSpeedController.effectiveSpeed(chosen: chosen, enabled: enabled) }
        pointerSpeed.mouseType = settings.mouseType
        pointerSpeed.devices = settings.index
        pointerSpeed.enabled = enabled
        pointerSpeed.magicMouseSpeed = effective(settings.chosenMagicMouseTrackingSpeed)
        pointerSpeed.wheelMouseSpeed = effective(settings.chosenWheelTrackingSpeed)
        pointerSpeed.apply()
    }

    private func applySettings() {
        blocker.rules = settings.ruleSet
        tapRecognizer.config = settings.tapConfig
        tap.treatUnknownContinuousAsMagicMouse = settings.treatUnknownContinuousAsMagicMouse
        tap.mouseType = settings.mouseType
        tap.devices = settings.index
        applyPointerSpeeds(enabled: settings.enabled)
        battery.enabled = settings.batteryWarningEnabled
        battery.threshold = settings.batteryWarningThreshold
        gamepad.update(config: settings.controllerConfig)
        gamepad.pauseInGames = settings.controllerPauseInGames
    }

    @objc private func settingsChanged() {
        applySettings()
        ConflictMonitor.shared.refresh()
        if AXIsProcessTrusted() { startTapIfWanted() } else { refreshStatusIcon() }
    }

    @objc private func activeAppChanged() { updateActiveApp() }

    private func updateActiveApp() {
        let app = NSWorkspace.shared.frontmostApplication
        blocker.setActiveApp(bundleID: app?.bundleIdentifier)
        refreshStatusIcon()
    }

    @objc private func statusRequested() { writeStatusFile() }

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
        menu.addItem(statusLine)
        menu.addItem(permissionItem)
        updateItem.isHidden = true
        menu.addItem(updateItem)
        starInviteItem.isHidden = true
        menu.addItem(starInviteItem)

        // Only what gets switched often lives here: everything set once is in Settings.
        menu.addItem(enabledItem)
        menu.addItem(perAppItem)
        menu.addItem(controllerItem)
        menu.addItem(.separator())

        let settingsItem = NSMenuItem(title: L("Settings…"), action: #selector(openSettings), keyEquivalent: ",")
        menu.addItem(settingsItem)
        menu.addItem(NSMenuItem(title: L("Send Feedback…"), action: #selector(sendFeedback), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: L("Quit CalmMouse"), action: #selector(quit), keyEquivalent: "q"))

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
            button.toolTip = L("CalmMouse needs Accessibility permission")
        } else if !settings.enabled {
            button.image = Self.icon(named: "magicmouse")
            button.contentTintColor = nil
            button.toolTip = L("CalmMouse is paused")
        } else if !tap.isRunning {
            button.image = Self.icon(named: "magicmouse.fill")
            button.contentTintColor = .systemOrange
            button.toolTip = L("CalmMouse couldn't start its event tap")
        } else if lowBatteryFlag {
            button.image = Self.icon(named: "magicmouse.fill")
            button.contentTintColor = .systemYellow
            button.toolTip = L("CalmMouse — active · Mouse battery low")
        } else {
            button.image = Self.icon(named: "magicmouse.fill")
            button.contentTintColor = nil
            button.toolTip = L("CalmMouse — active")
        }
        // Only a deliberate pause dims the icon. Something that needs the user's attention must
        // stay at full strength — a faint warning in a crowded menu bar is no warning at all.
        button.appearsDisabled = trusted && !settings.enabled
    }

    private func refreshMenu() {
        let trusted = AXIsProcessTrusted()
        // The quick switches are all Magic Mouse ones; with only a wheel mouse around they
        // would do nothing, so they step aside.
        let hasMagicMouse = mice.devices(for: settings).contains { $0.kind == .magicMouse }
        // Words only when something is wrong. The switches below already say the rest.
        let tapFailed = trusted && settings.enabled && !tap.isRunning
        statusLine.title = L("⚠︎ Event tap failed to start")
        statusLine.isHidden = !tapFailed
        permissionItem.isHidden = trusted

        if let release = updater.availableRelease {
            updateItem.title = L("Update available — CalmMouse %@…", release.version)
            updateItem.isHidden = false
        } else {
            updateItem.isHidden = true
        }

        refreshBatteryLines()
        refreshStarInvite()

        enabledItem.state = settings.enabled ? .on : .off
        // Only with a controller connected: everyone else never needs it.
        controllerItem.isHidden = gamepad.controllers.isEmpty
        controllerItem.state = settings.controllerPointer ? .on : .off
        // App rules are about Magic Mouse scrolling.
        perAppItem.isHidden = !hasMagicMouse

        if let app = NSWorkspace.shared.frontmostApplication,
           let bundleID = app.bundleIdentifier, bundleID != Bundle.main.bundleIdentifier {
            let name = app.localizedName ?? bundleID
            perAppItem.title = L("Ignore scrolling in %@", name)
            perAppItem.isEnabled = true
            perAppItem.state = (settings.rule(for: bundleID)?.disableScrollEntirely == true) ? .on : .off
            perAppItem.representedObject = [bundleID, name]
        } else {
            perAppItem.title = L("Ignore scrolling in this app")
            perAppItem.isEnabled = false
            perAppItem.state = .off
            perAppItem.representedObject = nil
        }

        refreshStatusIcon()
    }

    /// Adds this launch's newly blocked scrolls to the running total.
    private func saveBlockedScrolls() {
        let session = tap.swallowedCount
        guard session > blockedScrollsSaved else { return }
        settings.blockedScrollsTotal += session - blockedScrollsSaved
        blockedScrollsSaved = session
    }

    /// The one-time "Star on GitHub?" line (see `SupportInvite`).
    private func refreshStarInvite() {
        saveBlockedScrolls()
        let now = Date()
        let total = settings.blockedScrollsTotal
        let visible = SupportInvite.isVisible(
            blockedTotal: total, firstLaunch: settings.firstLaunchDate,
            firstShown: settings.starInviteFirstShown, done: settings.starInviteDone, now: now)
        if visible, settings.starInviteFirstShown == nil { settings.starInviteFirstShown = now }
        let count = NumberFormatter.localizedString(from: NSNumber(value: total), number: .decimal)
        starInviteItem.title = L("⭐ %@ accidental scrolls stopped. Star on GitHub?", count)
        starInviteItem.isHidden = !visible
    }

    @objc private func acceptStarInvite() {
        settings.starInviteDone = true
        NSWorkspace.shared.open(Feedback.repository)
    }

    /// "Malik's Magic Mouse 96%" at the top of the menu, for each mouse that reports one.
    private func refreshBatteryLines() {
        guard let menu = statusItem.menu else { return }
        batteryLines.forEach(menu.removeItem)
        batteryLines = mice.connected.compactMap { mouse in
            let percent = mouse.kind == .magicMouse
                ? BatteryMonitor.percent(of: mouse.name, in: battery.readings)
                : MouseBattery.percent(named: mouse.name)
            guard let percent else { return nil }
            let line = NSMenuItem(title: "\(mouse.name) \(percent)%", action: nil, keyEquivalent: "")
            line.isEnabled = false
            return line
        }
        batteryLines += gamepad.controllers.compactMap { controller in
            guard let percent = controller.battery else { return nil }
            let line = NSMenuItem(title: "\(controller.name) \(percent)%", action: nil, keyEquivalent: "")
            line.isEnabled = false
            return line
        }
        for (index, line) in batteryLines.enumerated() { menu.insertItem(line, at: index) }
    }

    private func writeStatusFile() {
        mice.refresh()
        let app = NSWorkspace.shared.frontmostApplication
        let bundleID = app?.bundleIdentifier
        StatusReporter.write(.init(
            updatedAt: Date(),
            version: (Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String) ?? "dev",
            accessibilityTrusted: AXIsProcessTrusted(),
            enabled: settings.enabled,
            tapRunning: tap.isRunning,
            lastSeenDevice: tap.lastSeenDevice,
            lastSeenDeviceID: tap.lastSeenDeviceID,
            lastSeenDeviceKind: tap.lastSeenDeviceKind?.rawValue,
            mouseType: settings.mouseType.rawValue,
            connectedMice: mice.connected.map(\.summary),
            otherDevicesSeen: tap.otherDevicesSeen,
            unidentifiedScrolls: tap.unidentifiedScrolls,
            trackingSpeedWrites: pointerSpeed.lastWrites,
            eventsSwallowed: tap.swallowedCount,
            activeApp: bundleID,
            activeRule: bundleID.flatMap { settings.rule(for: $0) }?.name,
            batteryPercent: battery.latest?.percent,
            blockScrollWhileClicked: blocker.config.blockScrollWhileClicked,
            releaseGraceMs: Int(blocker.config.releaseGrace * 1000),
            deadZone: blocker.config.deadZone,
            momentumEnabled: blocker.config.momentumEnabled,
            reverseWheelScroll: blocker.config.reverseWheelScroll,
            blockScrollWhileWheelPressed: blocker.config.blockScrollWhileWheelPressed,
            smoothWheelScroll: blocker.config.smoothWheelScroll,
            wheelScrollSpeed: blocker.config.wheelScrollSpeed,
            wheelTicksSmoothed: tap.wheelTicksSmoothed,
            smoothEventsPosted: tap.smoothEventsPosted,
            tapToClick: settings.tapToClick,
            tapToClickListening: tapToClick.isListening,
            tapsPosted: tapToClick.tapsPosted,
            tapRightClick: settings.tapRightClick,
            rightTapsPosted: tapToClick.rightTapsPosted,
            tapAndDrag: settings.tapAndDrag,
            dragsPosted: tapToClick.dragsPosted,
            tapFramesReceived: tapToClick.framesReceived,
            tapTouchesSeen: tapToClick.touchesSeen,
            tapLastRejection: tapRecognizer.lastRejection?.rawValue,
            tapLastRejectionAt: tapRecognizer.lastRejection == nil ? nil :
                String(format: "x=%.2f y=%.2f", tapRecognizer.lastRejectionX, tapRecognizer.lastRejectionY),
            tapRejections: tapRecognizer.rejectionCounts,
            settingsWindowOpen: settingsWindow?.window?.isVisible ?? false,
            scrollBlocker: blocker.debugSnapshot(at: MachTime.now()),
            dragConversionActive: tap.syntheticDragClickState != nil,
            otherMouseApps: ConflictMonitor.shared.conflicts.map(\.name),
            controllers: gamepad.controllers.map(\.summary),
            unsupportedControllers: gamepad.unsupported.map { "\($0.name) transport=\($0.transport)" },
            controllerPointer: settings.controllerPointer,
            controllerPointerActive: gamepad.isActive,
            controllerEventsPosted: gamepad.eventsPosted,
            controllerInputsReceived: gamepad.inputsReceived
        ))
    }

    // MARK: Actions

    @objc private func toggleEnabled() { settings.enabled.toggle() }

    @objc private func toggleControllerPointer() { settings.controllerPointer.toggle() }

    @objc private func togglePerApp(_ sender: NSMenuItem) {
        guard let pair = sender.representedObject as? [String], pair.count == 2 else { return }
        let (bundleID, name) = (pair[0], pair[1])
        let currentlyDisabled = settings.rule(for: bundleID)?.disableScrollEntirely == true
        settings.setScrollDisabled(!currentlyDisabled, forBundleID: bundleID, name: name)
    }

    /// Menu-bar entry for a pending update: open Settings on General, where the Updates
    /// section shows the notes and the Update Now button.
    @objc private func openUpdate() {
        showSettings(on: .general)
    }

    @objc private func openSettings() {
        showSettings(on: nil)
    }

    private func showSettings(on pane: SettingsPane?) {
        // Before the window exists, so it opens with the right pages.
        mice.refresh()
        if settingsWindow == nil {
            settingsWindow = SettingsWindowController(settings: settings, updater: updater)
        }
        if let pane { settingsWindow?.select(pane) }
        NSApp.activate(ignoringOtherApps: true)
        settingsWindow?.showWindow(nil)
        settingsWindow?.window?.makeKeyAndOrderFront(nil)
        lowBatteryFlag = false
        refreshStatusIcon()
    }

    @objc private func openOnboarding() {
        // A fresh controller per showing, so a re-run starts at the first step
        // instead of wherever the last tour was closed.
        if onboardingWindow?.window?.isVisible != true {
            onboardingWindow = OnboardingWindowController(
                settings: settings,
                blockedCount: { [weak self] in self?.tap.swallowedCount ?? 0 })
        }
        NSApp.activate(ignoringOtherApps: true)
        onboardingWindow?.showWindow(nil)
        onboardingWindow?.window?.makeKeyAndOrderFront(nil)
    }

    @objc private func openAccessibility() {
        let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")!
        NSWorkspace.shared.open(url)
    }

    /// A new GitHub issue on the bug form, with this Mac's details filled in (see `Feedback`).
    @objc private func sendFeedback() {
        mice.refresh()
        let info = Bundle.main.infoDictionary
        let details = Feedback.Details(
            version: (info?["CFBundleShortVersionString"] as? String) ?? "dev",
            build: (info?["CFBundleVersion"] as? String) ?? "dev",
            macOS: Feedback.macOSVersion,
            model: Feedback.macModel,
            accessibilityTrusted: AXIsProcessTrusted(),
            enabled: settings.enabled,
            tapRunning: tap.isRunning,
            mouseType: settings.mouseType,
            tapToClick: settings.tapToClick,
            mice: mice.connected)
        NSWorkspace.shared.open(Feedback.issueURL(details))
    }

    @objc private func quit() { NSApp.terminate(nil) }
}

extension AppDelegate: NSMenuDelegate {
    func menuWillOpen(_ menu: NSMenu) {
        battery.poll()
        mice.refresh()
        gamepad.refreshList()
        refreshMenu()
    }
}

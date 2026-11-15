import AppKit
import SwiftUI
import Combine
import ServiceManagement
import CalmMouseCore

/// The settings window, built the way a Mac app's settings usually are: a toolbar of panes
/// along the top, the window titled after the pane on show, and a height that follows that
/// pane's content.
///
/// SwiftUI's `TabView` only draws toolbar tabs inside a `Settings` scene. Hosted in an AppKit
/// window it falls back to the boxed tab strip, so the chrome here is AppKit's
/// `NSTabViewController` and each pane is a SwiftUI form. Type, spacing and sizes come from
/// `Theme` in SettingsDesign.swift.
final class SettingsWindowController: NSWindowController {
    private let panes: SettingsPanesController

    init(settings: Settings, updater: UpdateChecker) {
        let panes = SettingsPanesController(settings: settings, updater: updater)
        self.panes = panes
        let window = NSWindow(contentViewController: panes)
        window.styleMask = [.titled, .closable, .miniaturizable]
        window.toolbarStyle = .preference
        window.isReleasedWhenClosed = false
        super.init(window: window)
        panes.sizeWindowToSelectedPane(animated: false)
        window.center()
    }

    /// Shows this pane, if it is one of the panes on show.
    func select(_ pane: SettingsPane) { panes.select(pane) }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError() }
}

// MARK: - Panes

enum SettingsPane: String, CaseIterable {
    case general, devices, advanced

    var title: String {
        switch self {
        case .general: return L("General")
        case .devices: return L("Devices")
        case .advanced: return L("Advanced")
        }
    }

    var symbol: String {
        switch self {
        case .general: return "gearshape"
        case .devices: return "computermouse"
        case .advanced: return "gearshape.2"
        }
    }

    /// Devices adds the column that shows the device and what each setting does.
    var width: CGFloat {
        self == .devices ? Theme.Size.deviceColumn + Theme.Size.paneWidth : Theme.Size.paneWidth
    }

    /// Settings saved by 1.1, which had a page per kind of mouse.
    init?(saved raw: String) {
        if let pane = SettingsPane(rawValue: raw) { self = pane; return }
        guard raw == "magicMouse" || raw == "wheel" else { return nil }
        self = .devices
    }
}

/// Lays a pane out at the window's fixed width, at the height its form needs, up to what fits
/// on screen. Beyond that the form scrolls.
private struct PaneFrame<Content: View>: View {
    let width: CGFloat
    let maxHeight: CGFloat
    @ViewBuilder let content: () -> Content

    var body: some View {
        content()
            .frame(width: width)
            .frame(maxHeight: maxHeight)
            .fixedSize(horizontal: false, vertical: true)
    }
}

private final class SettingsPanesController: NSTabViewController {
    private let settings: Settings
    private let mice = MouseEnvironment.shared
    private var items: [SettingsPane: NSTabViewItem] = [:]
    private var hosts: [SettingsPane: NSHostingController<AnyView>] = [:]
    private var sizeObservers: [NSKeyValueObservation] = []
    private var cancellables = Set<AnyCancellable>()
    private var mouseTimer: Timer?
    private static let selectionKey = "settingsPane"

    init(settings: Settings, updater: UpdateChecker) {
        self.settings = settings
        super.init(nibName: nil, bundle: nil)
        tabStyle = .toolbar

        // Leave room for the menu bar, the Dock and the window's own toolbar.
        let screenHeight = NSScreen.main?.visibleFrame.height ?? 800
        let maxHeight = max(360, min(720, screenHeight - 110))
        let mice = self.mice
        func pane<V: View>(_ pane: SettingsPane, @ViewBuilder _ content: @escaping () -> V) {
            let host = NSHostingController(rootView: AnyView(PaneFrame(width: pane.width, maxHeight: maxHeight, content: content)))
            host.sizingOptions = [.preferredContentSize]
            host.title = pane.title     // AppKit titles the window after the selected pane
            let item = NSTabViewItem(viewController: host)
            item.label = pane.title
            item.image = NSImage(systemSymbolName: pane.symbol, accessibilityDescription: pane.title)
            hosts[pane] = host
            items[pane] = item
            // A pane can grow or shrink while it is on show (an update appears, a mouse is
            // plugged in); the window follows.
            sizeObservers.append(host.observe(\.preferredContentSize) { [weak self] host, _ in
                guard let self, self.selectedHost === host else { return }
                self.sizeWindowToSelectedPane(animated: true)
            })
        }
        pane(.general) { GeneralPane(settings: settings, updater: updater, mice: mice) }
        pane(.devices) { DevicesPane(settings: settings, mice: mice) }
        pane(.advanced) { AdvancedPane(settings: settings, mice: mice) }

        showPanesForCurrentMice()
        let saved = SettingsPane(saved: UserDefaults.standard.string(forKey: Self.selectionKey) ?? "") ?? .general
        select(visiblePanes.contains(saved) ? saved : .general)

        // The set of panes follows the mice in use and the Mouse type setting.
        Publishers.Merge(mice.objectWillChange.map { _ in () }, settings.objectWillChange.map { _ in () })
            .receive(on: DispatchQueue.main)
            .sink { [weak self] in self?.showPanesForCurrentMice() }
            .store(in: &cancellables)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError() }

    // MARK: Which panes

    /// Every pane, always: the devices themselves are listed inside Devices.
    private var visiblePanes: [SettingsPane] { SettingsPane.allCases }

    private var shownPanes: [SettingsPane] {
        tabViewItems.compactMap { item in items.first { $0.value === item }?.key }
    }

    private var selectedPane: SettingsPane? {
        let index = selectedTabViewItemIndex
        return shownPanes.indices.contains(index) ? shownPanes[index] : nil
    }

    private var selectedHost: NSHostingController<AnyView>? { selectedPane.flatMap { hosts[$0] } }

    private func showPanesForCurrentMice() {
        let wanted = visiblePanes
        let shown = shownPanes
        guard wanted != shown else { return }
        let previous = selectedPane
        // One at a time: assigning `tabViewItems` throws an exception once the toolbar exists.
        for pane in shown where !wanted.contains(pane) {
            if let item = items[pane] { removeTabViewItem(item) }
        }
        for (index, pane) in wanted.enumerated() where !shown.contains(pane) {
            if let item = items[pane] { insertTabViewItem(item, at: index) }
        }
        select(previous.flatMap { wanted.contains($0) ? $0 : nil } ?? .general)
    }

    func select(_ pane: SettingsPane) {
        if let index = shownPanes.firstIndex(of: pane) { selectedTabViewItemIndex = index }
    }

    // MARK: Following the selected pane

    override func tabView(_ tabView: NSTabView, didSelect tabViewItem: NSTabViewItem?) {
        super.tabView(tabView, didSelect: tabViewItem)
        if let pane = selectedPane { UserDefaults.standard.set(pane.rawValue, forKey: Self.selectionKey) }
        sizeWindowToSelectedPane(animated: true)
    }

    /// Names the window after the pane and fits it to the pane's height, keeping the title
    /// bar where it is so the window grows and shrinks downwards.
    func sizeWindowToSelectedPane(animated: Bool) {
        guard let window = view.window, let pane = selectedPane, let host = hosts[pane] else { return }
        window.title = pane.title
        let content = host.sizeThatFits(in: NSSize(width: pane.width, height: 10_000))
        guard content.width > 0, content.height > 0 else { return }
        var frame = window.frameRect(forContentRect: NSRect(origin: .zero, size: content))
        frame.origin = NSPoint(x: window.frame.minX, y: window.frame.maxY - frame.height)
        guard frame.size != window.frame.size else { return }
        window.setFrame(frame, display: true, animate: animated && window.isVisible)
    }

    // MARK: Watching for mice

    override func viewDidAppear() {
        super.viewDidAppear()
        mice.refresh()
        sizeWindowToSelectedPane(animated: false)
        // Catch a mouse being plugged in or waking up while the window is open.
        mouseTimer?.invalidate()
        mouseTimer = Timer.scheduledTimer(withTimeInterval: 3, repeats: true) { [weak self] _ in
            self?.mice.refresh()
        }
    }

    override func viewDidDisappear() {
        super.viewDidDisappear()
        mouseTimer?.invalidate()
        mouseTimer = nil
    }
}

// MARK: - General

/// The rule for every pane, from Locant's Settings: a row is a title and a control. No line under
/// a row, no footnote under a group. Anything worth explaining is on hover.
private struct GeneralPane: View {
    @ObservedObject var settings: Settings
    @ObservedObject var updater: UpdateChecker
    @ObservedObject var mice: MouseEnvironment
    @State private var launchAtLogin = SettingsHelpers.launchAtLoginEnabled()
    @State private var trusted = AXIsProcessTrusted()
    private let timer = Timer.publish(every: 2, on: .main, in: .common).autoconnect()

    var body: some View {
        Form {
            // Only while it is missing: a granted permission has no row.
            if !trusted {
                Section {
                    LabeledContent("Accessibility") {
                        HStack(spacing: Theme.Space.medium) {
                            Image(systemName: "exclamationmark.circle.fill").foregroundStyle(.orange)
                            Button("Reset grant…") { SettingsHelpers.resetAccessibilityGrant() }
                                .help("Toggle already ON in System Settings but still not working? The stored grant is stale (usually after a rebuild). Reset it and grant again.")
                            Button("Open Settings…") { SettingsHelpers.openAccessibilityPane() }
                        }
                    }
                }
            }

            Section {
                Toggle("Enable CalmMouse", isOn: $settings.enabled)
                Toggle("Launch at login", isOn: Binding(
                    get: { launchAtLogin },
                    set: { launchAtLogin = SettingsHelpers.setLaunchAtLogin($0) }))
                // One choice instead of a switch plus a slider: off, or the level to warn at.
                // Every mouse that reports a level is watched, so it is always here.
                Picker("Low battery warning", selection: lowBatteryChoice) {
                    Text("Off").tag(0)
                    Divider()
                    ForEach(lowBatteryLevels, id: \.self) { Text(L("Below %ld%%", $0)).tag($0) }
                }
            }

            UpdatesSection(updater: updater)

            Section("Support CalmMouse") {
                LabeledContent("Something not right?") {
                    Button("Send Feedback…") {
                        NotificationCenter.default.post(name: Feedback.sendRequested, object: nil)
                    }
                }
                LabeledContent("Like CalmMouse?") {
                    Button {
                        settings.starInviteDone = true      // no need to ask in the menu later
                        NSWorkspace.shared.open(Feedback.repository)
                    } label: {
                        Label("Star on GitHub", systemImage: "star")
                    }
                }
            }
        }
        .formStyle(.grouped)
        .onReceive(timer) { _ in trusted = AXIsProcessTrusted() }
    }

    private var lowBatteryLevels: [Int] {
        Set([10, 15, 20, 30, 50, settings.batteryWarningThreshold]).sorted()
    }

    private var lowBatteryChoice: Binding<Int> {
        Binding(
            get: { settings.batteryWarningEnabled ? settings.batteryWarningThreshold : 0 },
            set: { level in
                if level == 0 {
                    settings.batteryWarningEnabled = false
                } else {
                    settings.batteryWarningThreshold = level
                    settings.batteryWarningEnabled = true
                }
            })
    }
}

// MARK: - Updates

/// The version, a way to check, and a row more only while there is an update or a problem.
private struct UpdatesSection: View {
    @ObservedObject var updater: UpdateChecker

    var body: some View {
        Section {
            LabeledContent("Version \(UpdateChecker.currentVersion)") {
                HStack(spacing: Theme.Space.medium) {
                    switch updater.state {
                    case .checking:
                        ProgressView().controlSize(.small)
                    case .downloading, .installing:
                        EmptyView()
                    case .upToDate:
                        Image(systemName: "checkmark.circle.fill")
                            .foregroundStyle(.green)
                            .help("You're up to date.")
                        Button("Check Now") { updater.check(userInitiated: true) }
                    default:
                        Button("Check Now") { updater.check(userInitiated: true) }
                    }
                }
            }

            switch updater.state {
            case .idle, .checking, .upToDate:
                EmptyView()
            case .available(let release):
                LabeledContent("CalmMouse \(release.version) is available.") {
                    HStack(spacing: Theme.Space.medium) {
                        Link("Full release notes ›", destination: release.pageURL)
                        Button("Update Now") { updater.install() }
                            .keyboardShortcut(.defaultAction)
                    }
                }
            case .downloading(let progress):
                ProgressView("Downloading…", value: progress)
            case .installing:
                Label("Installing — CalmMouse will relaunch itself in a moment.",
                      systemImage: "arrow.triangle.2.circlepath")
            case .failed(let message):
                LabeledContent {
                    Link("Download from the website instead ›", destination: UpdateChecker.fallbackPageURL)
                } label: {
                    Label(message, systemImage: "exclamationmark.triangle.fill")
                        .foregroundStyle(.orange)
                }
            }

            LabeledContent("Welcome Tour") {
                Button("Show") {
                    NotificationCenter.default.post(name: .calmMouseShowOnboarding, object: nil)
                }
            }
        }
    }
}

// MARK: - Magic Mouse

// MARK: Devices

/// One device at a time: on the left the switcher, the device, and what the setting under the
/// pointer does; on the right that device's settings.
private struct DevicesPane: View {
    @ObservedObject var settings: Settings
    @ObservedObject var mice: MouseEnvironment
    @StateObject private var focus = PreviewFocus()
    @ObservedObject private var conflicts = ConflictMonitor.shared

    var body: some View {
        let devices = mice.devices(for: settings)
        let selected = devices.first { $0.key == settings.selectedDeviceKey } ?? devices.first
        VStack(spacing: 0) {
            if let message = conflicts.message {
                HStack(alignment: .firstTextBaseline, spacing: Theme.Space.medium) {
                    Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(.orange)
                    Text(message).fixedSize(horizontal: false, vertical: true)
                    Spacer(minLength: 0)
                }
                .padding(.horizontal, Theme.Space.large * 2)
                .padding(.vertical, Theme.Space.large)
                .background(Color.orange.opacity(0.1))
                Divider()
            }
            columns(devices: devices, selected: selected)
        }
        .onAppear { conflicts.refresh() }
    }

    private func columns(devices: [DeviceEntry], selected: DeviceEntry?) -> some View {
        HStack(alignment: .top, spacing: 0) {
            DeviceColumn(devices: devices, selected: selected, settings: settings, mice: mice, focus: focus)
                .frame(width: Theme.Size.deviceColumn)
            Divider()
            Group {
                if let selected {
                    switch selected.kind {
                    case .magicMouse: MagicMouseForm(settings: settings, device: selected)
                    case .wheelMouse: WheelDeviceForm(settings: settings, device: selected)
                    case nil: UnmanagedDeviceForm(settings: settings, device: selected)
                    }
                }
            }
            .frame(width: Theme.Size.paneWidth)
            .environment(\.previewFocus, focus)
            .onHover { inside in if !inside { focus.clear() } }
        }
    }
}

/// The left column. It explains instead of popovers: point at a row and its animation and
/// caption show here.
private struct DeviceColumn: View {
    let devices: [DeviceEntry]
    let selected: DeviceEntry?
    @ObservedObject var settings: Settings
    @ObservedObject var mice: MouseEnvironment
    @ObservedObject var focus: PreviewFocus

    var body: some View {
        VStack(spacing: Theme.Space.large) {
            if devices.count > 1 { switcher }
            if let preview = focus.preview {
                PreviewCanvas(preview: preview)
                    .frame(width: 250, height: 150)
            } else if let selected {
                hero(selected)
            }
            Text(focus.caption ?? L("Point at a setting\nto see what it does."))
                .multilineTextAlignment(.center)
                .lineSpacing(Theme.Space.small)
                .foregroundStyle(focus.caption == nil ? .secondary : .primary)
                .fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: 0)
        }
        .padding(.horizontal, (Theme.Size.deviceColumn - 250) / 2)
        .padding(.vertical, Theme.Space.large * 2)
        .frame(maxHeight: .infinity, alignment: .top)
    }

    private func symbol(_ device: DeviceEntry) -> String {
        device.kind == .magicMouse || device.detected == .magicMouse ? "magicmouse" : "computermouse"
    }

    private func batteryText(_ device: DeviceEntry) -> String? {
        if let level = mice.batteries[device.name] { return "\(level)%" }
        return device.connected ? nil : L("Not connected")
    }

    private func hero(_ device: DeviceEntry) -> some View {
        VStack(spacing: Theme.Space.medium) {
            Image(systemName: symbol(device))
                .font(.system(size: 72, weight: .ultraLight))
                .foregroundStyle(.secondary)
                .frame(height: 150)
            Text(device.name)
                .font(.headline)
                .lineLimit(1)
            if let detail = device.detail {
                Text(detail).foregroundStyle(.secondary)
            }
            if let battery = batteryText(device) {
                Text(battery).foregroundStyle(.secondary)
            }
        }
    }

    /// One row per device, so long names and many devices both fit.
    private var switcher: some View {
        VStack(spacing: 2) {
            ForEach(devices) { device in
                let isSelected = device.key == selected?.key
                Button {
                    settings.selectedDeviceKey = device.key
                    focus.clear()
                } label: {
                    HStack(spacing: Theme.Space.medium) {
                        Image(systemName: symbol(device))
                            .frame(width: Theme.Size.rowIcon)
                        Text(device.detail.map { "\(device.name) · \($0)" } ?? device.name)
                            .lineLimit(1)
                            .truncationMode(.middle)
                        Spacer(minLength: Theme.Space.small)
                        if let battery = batteryText(device) {
                            Text(battery).foregroundStyle(.secondary)
                        }
                    }
                    .padding(.horizontal, Theme.Space.medium)
                    .frame(height: 30)
                    .background(RoundedRectangle(cornerRadius: 7)
                        .fill(isSelected ? Color.accentColor.opacity(0.16) : .clear))
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .accessibilityAddTraits(isSelected ? .isSelected : [])
            }
        }
    }
}

/// "Treat as", at the foot of every connected device's settings.
private struct TreatAsSection: View {
    @ObservedObject var settings: Settings
    let device: DeviceEntry

    var body: some View {
        if device.connected {
            Section {
                ExplainedRow(caption: L("Only if CalmMouse took this mouse\nfor the wrong kind.")) {
                    Picker("Treat as", selection: Binding(
                        get: { settings.typeOverride(key: device.key, name: device.name) },
                        set: { settings.setTypeOverride($0, key: device.key, name: device.name) })) {
                        Text("Automatic").tag(MouseTypeSetting.automatic)
                        Divider()
                        Text("Magic Mouse").tag(MouseTypeSetting.magicMouse)
                        Text("Wheel mouse").tag(MouseTypeSetting.wheelMouse)
                    }
                }
            }
        }
    }
}

/// A mouse CalmMouse leaves alone: only "Treat as".
private struct UnmanagedDeviceForm: View {
    @ObservedObject var settings: Settings
    let device: DeviceEntry

    var body: some View {
        Form {
            Section {
                Text("CalmMouse has nothing to change on this mouse.")
                    .foregroundStyle(.secondary)
            }
            TreatAsSection(settings: settings, device: device)
        }
        .formStyle(.grouped)
    }
}

/// Settings every Magic Mouse shares.
private struct MagicMouseForm: View {
    @ObservedObject var settings: Settings
    let device: DeviceEntry
    @AppStorage("showFineTuning") private var showFineTuning = false

    var body: some View {
        Form {
            Section("Presets") {
                ForEach(Preset.builtIn) { preset in
                    PresetCard(preset: preset,
                               isActive: settings.currentPresetValues == preset.values,
                               apply: { settings.apply(preset) })
                }
            }

            Section("Pointer") {
                SliderRow("Tracking speed", value: $settings.magicMouseTrackingSpeed, in: -1...1,
                          step: 0.25, from: "Slow", to: "Fast")
            }

            Section("Scrolling") {
                ExplainedRow(preview: .blockWhileClick,
                             caption: L("The page stays put while you click.")) {
                    Toggle("Don't scroll while clicking", isOn: $settings.blockScrollWhileClicked)
                }
                ExplainedRow(preview: settings.blockHorizontalScroll ? .ignoreSideways : .straightLines,
                             caption: L("Straight lines: a swipe can't drift.\nUp and down only: no sideways at all.")) {
                    Picker("Direction", selection: direction) {
                        Text("Any direction").tag(0)
                        Text("Straight lines").tag(1)
                        Text("Up and down only").tag(2)
                    }
                }
                ExplainedRow(preview: .momentum(on: settings.momentumEnabled),
                             caption: L("On: the page glides after a swipe.\nOff: it stops when your finger does.")) {
                    Toggle("Keep gliding after a swipe", isOn: $settings.momentumEnabled)
                }
                ExplainedRow(caption: L("Flips only the Magic Mouse.\nThe trackpad stays as it is.")) {
                    Toggle("Reverse direction", isOn: $settings.reverseMagicMouseScroll)
                }
            }

            if TapController.isSupported { tapSection }

            Section {
                if showFineTuning { fineTuningRows }
            } header: {
                DisclosureHeader(isExpanded: $showFineTuning) { Text("Fine-tuning") }
            }

            TreatAsSection(settings: settings, device: device)
        }
        .formStyle(.grouped)
    }

    /// Straight lines and up-and-down-only were two switches; they are one choice now.
    private var direction: Binding<Int> {
        Binding(
            get: { settings.blockHorizontalScroll ? 2 : (settings.axisLock ? 1 : 0) },
            set: { choice in
                settings.blockHorizontalScroll = choice == 2
                if choice != 2 { settings.axisLock = choice == 1 }
            })
    }

    /// The three-way choice from 1.1, kept only for someone who picked "A double-tap" there.
    private var rightClickChoice: Binding<Int> {
        Binding(
            get: { !settings.tapRightClick ? 0 : (settings.tapRightClickMode == .rightSide ? 1 : 2) },
            set: { choice in
                settings.tapRightClick = choice != 0
                if choice == 1 { settings.tapRightClickMode = .rightSide }
                if choice == 2 { settings.tapRightClickMode = .doubleTap }
            })
    }

    private var tapsRightSide: Binding<Bool> {
        Binding(
            get: { settings.tapRightClick },
            set: { on in
                settings.tapRightClick = on
                if on { settings.tapRightClickMode = .rightSide }
            })
    }

    private var tapSection: some View {
        Section("Clicking") {
            ExplainedRow(preview: .tapToClick,
                         caption: L("A light tap clicks.\nPressing the mouse still works.")) {
                Toggle("Tap to click", isOn: $settings.tapToClick)
            }

            // The rest only means something once taps click.
            if settings.tapToClick {
                if settings.tapRightClick && settings.tapRightClickMode == .doubleTap {
                    ExplainedRow(preview: .tapRightClick(doubleTap: true),
                                 caption: L("Tap twice quickly to right-click.\nPress the mouse to double-click.")) {
                        Picker("Tap to right-click", selection: rightClickChoice) {
                            Text("Off").tag(0)
                            Text("A tap on the right side").tag(1)
                            Text("A double-tap").tag(2)
                        }
                    }
                } else {
                    ExplainedRow(preview: .tapRightClick(doubleTap: false),
                                 caption: L("Tap the right side to right-click.\nTaps elsewhere are normal clicks.")) {
                        Toggle("Tap the right side to right-click", isOn: tapsRightSide)
                    }
                }
                ExplainedRow(preview: .tapAndDrag,
                             caption: L("Tap twice and leave your finger down.\nMove the mouse right away to drag.\nLift your finger to drop.")
                                + (settings.tapRightClick && settings.tapRightClickMode == .rightSide
                                   ? "\n" + L("Use the left or middle of the mouse.") : "")) {
                    Toggle("Double Tap Drag", isOn: $settings.tapAndDrag)
                }
                ExplainedRow(preview: .tapZone(depth: settings.tapZoneDepth),
                             caption: L("Only taps near the front count.\nYour grip can't click by accident.")) {
                    Toggle("Front of the mouse only", isOn: $settings.tapZoneEnabled)
                }
            }
        }
    }

    /// The sliders, and the rarer second way to drag. Each slider only shows while the switch
    /// it tunes is on.
    @ViewBuilder private var fineTuningRows: some View {
        if settings.blockScrollWhileClicked {
            ExplainedRow(preview: .settleAfterRelease,
                         caption: L("A short pause after you let go,\nso a lifting finger can't scroll.")) {
                SliderRow("Hold still after you let go",
                          value: Binding(get: { Double(settings.releaseGraceMs) },
                                         set: { settings.releaseGraceMs = Int($0.rounded()) }),
                          in: 0...800, step: 50,
                          reading: settings.releaseGraceMs == 0 ? L("Off") : String(format: "%.2g s", Double(settings.releaseGraceMs) / 1000))
            }
        }
        ExplainedRow(preview: .ignoreNudges,
                     caption: L("Scrolling waits for this much travel.\nA resting finger can't move the page.")) {
            SliderRow("Ignore small finger nudges", value: $settings.deadZone, in: 0...40, step: 1,
                      reading: settings.deadZone == 0 ? L("Off") : "\(Int(settings.deadZone)) pt")
        }
        if settings.axisLock && !settings.blockHorizontalScroll {
            ExplainedRow(caption: L("How far a swipe goes before it locks.")) {
                SliderRow("Lock direction after sliding", value: $settings.axisLockThreshold, in: 2...40, step: 1,
                          reading: "\(Int(settings.axisLockThreshold)) pt")
            }
        }
        if settings.tapToClick {
            ExplainedRow(preview: .tapToClick,
                         caption: L("Firm: only quick, deliberate taps.\nLight: gentler, slower taps too.")) {
                SliderRow("Tap firmness", value: $settings.tapSensitivity, in: 0...1, from: "Firm", to: "Light")
            }
            if settings.tapZoneEnabled {
                SliderRow("Size of the tap area", value: $settings.tapZoneDepth, in: 0.25...0.75, step: 0.05,
                          reading: "\(Int((settings.tapZoneDepth * 100).rounded()))%")
            }
            ExplainedRow(preview: .twoFingerDrag,
                         caption: L("Rest two fingers and hold a moment.\nMove the mouse to drag.\nLift your fingers to drop.")) {
                Toggle("Two-finger drag", isOn: $settings.twoFingerDrag)
            }
        }
    }
}

/// One preset card: icon, name, and a Use button or a check. What it does is on hover.
private struct PresetCard: View {
    let preset: Preset
    let isActive: Bool
    let apply: () -> Void
    var remove: (() -> Void)? = nil

    var body: some View {
        ExplainedRow(caption: L(preset.summary)) {
            LabeledContent {
                HStack(spacing: Theme.Space.medium) {
                    if let remove {
                        Button(action: remove) { Image(systemName: "trash") }
                            .buttonStyle(.borderless)
                            .foregroundStyle(.secondary)
                            .help("Delete this preset")
                    }
                    if isActive {
                        Image(systemName: "checkmark.circle.fill").foregroundStyle(.green)
                    } else {
                        Button("Use", action: apply)
                    }
                }
            } label: {
                Label {
                    // Built-in preset names have .strings entries; a custom preset's name misses
                    // the table and falls back to itself, which is what we want.
                    Text(LocalizedStringKey(preset.name))
                } icon: {
                    // One width for every symbol, or each preset's name starts somewhere different.
                    Image(systemName: preset.symbolName)
                        .font(Theme.Text.rowSymbol)
                        .foregroundStyle(Color.accentColor)
                        .frame(width: Theme.Size.rowIcon)
                }
            }
        }
    }
}

// MARK: - Wheel mice

/// One wheel mouse's own settings.
private struct WheelDeviceForm: View {
    @ObservedObject var settings: Settings
    let device: DeviceEntry

    var body: some View {
        let wheel = settings.wheelSettings(key: device.key, name: device.name)
        Form {
            Section("Pointer") {
                SliderRow("Tracking speed",
                          value: Binding(get: { settings.wheelTrackingSpeed(key: device.key, name: device.name) ?? 0 },
                                         set: { settings.setWheelTrackingSpeed($0, key: device.key, name: device.name) }),
                          in: -1...1, step: 0.25, from: "Slow", to: "Fast")
            }
            Section("Scrolling") {
                ExplainedRow(caption: L("The page glides instead of jumping.\nZooming stays one step per notch.")) {
                    Toggle("Smooth scrolling", isOn: binding(\.smooth))
                }
                if wheel.smooth {
                    SliderRow("Scrolling speed", value: binding(\.speed),
                              in: 1...Settings.maximumWheelScrollSpeed, from: "Slow", to: "Fast")
                }
                ExplainedRow(caption: L("Flips only the wheel's direction.\nOther devices stay as they are.")) {
                    Toggle("Reverse direction", isOn: binding(\.reverse))
                }
                ExplainedRow(caption: L("Pressing a wheel often turns it too.\nThat won't scroll the page.")) {
                    Toggle("Don't scroll while pressed", isOn: binding(\.blockWhilePressed))
                }
            }
            TreatAsSection(settings: settings, device: device)
        }
        .formStyle(.grouped)
    }

    private func binding<Value>(_ path: WritableKeyPath<WheelSettings, Value>) -> Binding<Value> {
        Binding(get: { settings.wheelSettings(key: device.key, name: device.name)[keyPath: path] },
                set: { value in settings.updateWheel(key: device.key, name: device.name) { $0[keyPath: path] = value } })
    }
}

// MARK: - Advanced

private struct AdvancedPane: View {
    @ObservedObject var settings: Settings
    @ObservedObject var mice: MouseEnvironment
    @State private var expandedRule: UUID?
    @State private var showCombinations = false

    private let singleKeys: [ModifierCombo] = [.command, .option, .control, .shift]
    private let combinations: [ModifierCombo] = [[.command, .option], [.command, .shift], [.control, .option]]

    var body: some View {
        Form {
            appsSection
            shortcutsSection
        }
        .formStyle(.grouped)
        .onAppear {
            // Don't hide a combination that is already doing something.
            if combinations.contains(where: { settings.modifierActions[$0] != nil }) { showCombinations = true }
        }
    }

    private var appsSection: some View {
        Section {
            ForEach($settings.appRules) { $rule in
                DisclosureHeader(isExpanded: Binding(
                    get: { expandedRule == rule.id },
                    set: { expandedRule = $0 ? rule.id : nil })) {
                        Image(nsImage: Self.icon(forBundleID: rule.bundleID))
                            .resizable().frame(width: Theme.Size.rowIcon, height: Theme.Size.rowIcon)
                        Text(rule.name.isEmpty ? rule.bundleID : rule.name)
                        if !rule.enabled {
                            Image(systemName: "pause.circle").foregroundStyle(.secondary)
                        }
                    }
                if expandedRule == rule.id {
                    Toggle("Rule enabled", isOn: $rule.enabled)
                    TriToggle(title: L("Ignore Magic Mouse scrolling"), value: $rule.disableScrollEntirely)
                    TriToggle(title: L("Don't scroll while clicking"), value: $rule.blockScrollWhileClicked)
                    TriToggle(title: L("Ignore sideways scrolling"), value: $rule.blockHorizontalScroll)
                    TriToggle(title: L("Keep gliding after a swipe"), value: $rule.momentumEnabled)
                    TriToggle(title: L("Scroll in straight lines"), value: $rule.axisLock)
                    HStack {
                        Spacer()
                        Button("Remove", role: .destructive) { settings.removeRule(id: rule.id) }
                    }
                }
            }
            LabeledContent("Add an App") {
                Button("Choose…") { addApp() }
            }
        } header: {
            ExplainedRow(caption: L("Rules apply while that app is in front.\n“Default” follows the Magic Mouse page.")) {
                Text("Apps")
                Spacer()
            }
        }
    }

    private static func icon(forBundleID bundleID: String) -> NSImage {
        if let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: bundleID) {
            return NSWorkspace.shared.icon(forFile: url.path)
        }
        return NSImage(systemSymbolName: "app.dashed", accessibilityDescription: nil) ?? NSImage()
    }

    private func addApp() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.application]
        panel.directoryURL = URL(fileURLWithPath: "/Applications")
        panel.allowsMultipleSelection = false
        guard panel.runModal() == .OK, let url = panel.url,
              let bundle = Bundle(url: url), let bundleID = bundle.bundleIdentifier else { return }
        let name = FileManager.default.displayName(atPath: url.path).replacingOccurrences(of: ".app", with: "")
        if let existing = settings.rule(for: bundleID) {
            expandedRule = existing.id
            return
        }
        var rule = AppRule(bundleID: bundleID, name: name)
        rule.disableScrollEntirely = false
        settings.upsert(rule)
        expandedRule = rule.id
    }

    private var shortcutsSection: some View {
        Section {
            ForEach(singleKeys, id: \.rawValue) { actionPicker($0) }
            DisclosureHeader(isExpanded: $showCombinations) { Text("More key combinations") }
            if showCombinations {
                ForEach(combinations, id: \.rawValue) { actionPicker($0) }
            }
        } header: {
            ExplainedRow(preview: .keyZoom,
                         caption: L("Hold a key while you scroll\nto do the action you picked.")) {
                Text("Shortcuts")
                Spacer()
            }
        }
    }

    private func actionPicker(_ combo: ModifierCombo) -> some View {
        Picker(L("While holding %@", combo.label), selection: Binding(
            get: { settings.modifierActions[combo] ?? .normal },
            set: { action in
                var m = settings.modifierActions
                if action == .normal { m.removeValue(forKey: combo) } else { m[combo] = action }
                settings.modifierActions = m
            })) {
                ForEach(ScrollAction.allCases, id: \.self) { Text(LocalizedStringKey($0.label)).tag($0) }
            }
    }
}

/// Off / On / Inherit picker for an optional override.
private struct TriToggle: View {
    let title: String
    @Binding var value: Bool?

    var body: some View {
        Picker(title, selection: Binding(
            get: { value == nil ? 0 : (value! ? 1 : 2) },
            set: { value = $0 == 0 ? nil : ($0 == 1) })) {
                Text("Default").tag(0)
                Text("On").tag(1)
                Text("Off").tag(2)
            }
    }
}

// MARK: - Helpers

enum SettingsHelpers {
    static func launchAtLoginEnabled() -> Bool {
        guard Bundle.main.bundleIdentifier != nil else { return false }
        return SMAppService.mainApp.status == .enabled
    }

    /// Returns the state actually achieved (so a failed toggle snaps back in the UI).
    static func setLaunchAtLogin(_ enable: Bool) -> Bool {
        guard Bundle.main.bundleIdentifier != nil else { return false }
        do {
            if enable { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
        } catch {
            let alert = NSAlert()
            alert.messageText = L("Couldn't change the login item")
            alert.informativeText = error.localizedDescription + "\n\n"
                + L("Launch at login only works when CalmMouse runs from an app bundle (e.g. /Applications).")
            alert.runModal()
        }
        return SMAppService.mainApp.status == .enabled
    }

    /// Removes our own TCC row (`tccutil reset Accessibility <bundle id>`) — allowed for one's own
    /// bundle without admin rights — then re-prompts so the fresh grant matches the current signature.
    static func resetAccessibilityGrant() {
        let bundleID = Bundle.main.bundleIdentifier ?? "com.calmmouse.app"
        let proc = Process()
        proc.executableURL = URL(fileURLWithPath: "/usr/bin/tccutil")
        proc.arguments = ["reset", "Accessibility", bundleID]
        try? proc.run()
        proc.waitUntilExit()
        // Prompt again; the app's normal polling picks the grant up as soon as it's flipped.
        // This deliberate click is the one system dialog for the freshly-reset grant — mark it
        // shown so the next launch doesn't queue a second dialog on top.
        Settings.shared.accessibilityPromptShown = true
        let opts = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(opts)
        openAccessibilityPane()
    }

    static func openAccessibilityPane() {
        let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")!
        NSWorkspace.shared.open(url)
    }
}

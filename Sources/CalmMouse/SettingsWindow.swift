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
    init(settings: Settings, updater: UpdateChecker) {
        let panes = SettingsPanesController(settings: settings, updater: updater)
        let window = NSWindow(contentViewController: panes)
        window.styleMask = [.titled, .closable, .miniaturizable]
        window.toolbarStyle = .preference
        window.isReleasedWhenClosed = false
        super.init(window: window)
        panes.sizeWindowToSelectedPane(animated: false)
        window.center()
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError() }
}

// MARK: - Panes

private enum SettingsPane: String, CaseIterable {
    case general, magicMouse, wheel, advanced

    var title: String {
        switch self {
        case .general: return L("General")
        case .magicMouse: return L("Magic Mouse")
        case .wheel: return L("Scroll Wheel")
        case .advanced: return L("Advanced")
        }
    }

    var symbol: String {
        switch self {
        case .general: return "gearshape"
        case .magicMouse: return "magicmouse"
        case .wheel: return "computermouse"
        case .advanced: return "gearshape.2"
        }
    }

    /// Which kind of mouse the pane is for. General and Advanced are always there: Advanced
    /// holds the Mouse type override, the way out when detection gets a mouse wrong.
    var needs: MouseKind? {
        switch self {
        case .general, .advanced: return nil
        case .magicMouse: return .magicMouse
        case .wheel: return .wheelMouse
        }
    }
}

/// Lays a pane out at the window's fixed width, at the height its form needs, up to what fits
/// on screen. Beyond that the form scrolls.
private struct PaneFrame<Content: View>: View {
    let maxHeight: CGFloat
    @ViewBuilder let content: () -> Content

    var body: some View {
        content()
            .frame(width: Theme.Size.paneWidth)
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
            let host = NSHostingController(rootView: AnyView(PaneFrame(maxHeight: maxHeight, content: content)))
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
        pane(.magicMouse) { MagicMousePane(settings: settings) }
        pane(.wheel) { WheelPane(settings: settings) }
        pane(.advanced) { AdvancedPane(settings: settings, mice: mice) }

        showPanesForCurrentMice()
        let saved = SettingsPane(rawValue: UserDefaults.standard.string(forKey: Self.selectionKey) ?? "") ?? .general
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

    private var visiblePanes: [SettingsPane] {
        let kinds = mice.kindsToShow(for: settings)
        return SettingsPane.allCases.filter { pane in pane.needs.map(kinds.contains) ?? true }
    }

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
        guard wanted != shownPanes else { return }
        let previous = selectedPane
        tabViewItems = wanted.compactMap { items[$0] }
        select(previous.flatMap { wanted.contains($0) ? $0 : nil } ?? .general)
    }

    private func select(_ pane: SettingsPane) {
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
        let content = host.sizeThatFits(in: NSSize(width: Theme.Size.paneWidth, height: 10_000))
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
                if mice.kindsToShow(for: settings).contains(.magicMouse) {
                    // One choice instead of a switch plus a slider: off, or the level to warn at.
                    Picker("Low battery warning", selection: lowBatteryChoice) {
                        Text("Off").tag(0)
                        Divider()
                        ForEach(lowBatteryLevels, id: \.self) { Text(L("Below %ld%%", $0)).tag($0) }
                    }
                }
            }

            UpdatesSection(updater: updater)
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

private struct MagicMousePane: View {
    @ObservedObject var settings: Settings
    @State private var newPresetName = ""
    @State private var namingPreset = false
    @AppStorage("showFineTuning") private var showFineTuning = false

    var body: some View {
        Form {
            presetSection

            Section("Scrolling") {
                ExplainedRow(preview: .blockWhileClick,
                             caption: L("While a mouse button is held down, your finger resting on the surface can't scroll the page out from under your click.")) {
                    Toggle("Don't scroll while clicking", isOn: $settings.blockScrollWhileClicked)
                }
                ExplainedRow(preview: .straightLines,
                             caption: L("Scrolling sticks to straight up-down or left-right. A swipe that drifts a little diagonally won't wander sideways.")) {
                    Toggle("Scroll in straight lines", isOn: $settings.axisLock)
                }
                ExplainedRow(preview: .momentum(on: settings.momentumEnabled),
                             caption: L("On: the page keeps gliding after a quick swipe, like a trackpad. Off: it stops the moment your finger does. Only affects the Magic Mouse — macOS's own setting changes every device.")) {
                    Toggle("Keep gliding after a swipe", isOn: $settings.momentumEnabled)
                }
                ExplainedRow(preview: .ignoreSideways,
                             caption: L("Sideways swipes are ignored completely. Scrolling up and down still works.")) {
                    Toggle("Ignore sideways scrolling", isOn: $settings.blockHorizontalScroll)
                }
            }

            if TapController.isSupported { tapSection }

            Section {
                if showFineTuning { fineTuningRows }
            } header: {
                DisclosureHeader(isExpanded: $showFineTuning) { Text("Fine-tuning") }
            }
        }
        .formStyle(.grouped)
        .alert("Save as Preset", isPresented: $namingPreset) {
            TextField("Preset name", text: $newPresetName)
            Button("Save") {
                settings.saveCurrentAsPreset(named: newPresetName)
                newPresetName = ""
            }
            .disabled(newPresetName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
            Button("Cancel", role: .cancel) { newPresetName = "" }
        }
    }

    private var presetSection: some View {
        Section("Presets") {
            ForEach(Preset.builtIn) { preset in
                PresetCard(preset: preset,
                           isActive: settings.currentPresetValues == preset.values,
                           apply: { settings.apply(preset) })
            }
            ForEach(settings.customPresets) { preset in
                PresetCard(preset: preset,
                           isActive: settings.currentPresetValues == preset.values,
                           apply: { settings.apply(preset) },
                           remove: { settings.removePreset(id: preset.id) })
            }
            if settings.activePreset == nil {
                HStack {
                    Spacer()
                    Button("Save as Preset…") { namingPreset = true }
                }
            }
        }
    }

    /// Off, or one of the two ways to right-click with a tap, as a single choice.
    private var rightClickChoice: Binding<Int> {
        Binding(
            get: { !settings.tapRightClick ? 0 : (settings.tapRightClickMode == .rightSide ? 1 : 2) },
            set: { choice in
                settings.tapRightClick = choice != 0
                if choice == 1 { settings.tapRightClickMode = .rightSide }
                if choice == 2 { settings.tapRightClickMode = .doubleTap }
            })
    }

    private var tapSection: some View {
        Section("Clicking") {
            ExplainedRow(preview: .tapToClick,
                         caption: L("A light single-finger tap on the surface clicks — no need to press the mouse down. Pressing to click keeps working as before.")) {
                Toggle("Tap to click", isOn: $settings.tapToClick)
            }

            // The rest only means something once taps click.
            if settings.tapToClick {
                ExplainedRow(preview: .tapRightClick(doubleTap: settings.tapRightClickMode == .doubleTap),
                             caption: settings.tapRightClickMode == .doubleTap
                                ? L("One tap clicks; a second quick tap in the same spot opens the right-click menu instead.") + " "
                                    + L("Heads-up: with double-tap chosen, tap-tap no longer double-clicks. Double-click by pressing the mouse down, or by tapping twice a little slower.")
                                : L("A tap on the right part of the surface right-clicks — the same side a physical right click uses. Taps everywhere else stay normal clicks.")) {
                    Picker("Tap to right-click", selection: rightClickChoice) {
                        Text("Off").tag(0)
                        Text("A tap on the right side").tag(1)
                        Text("A double-tap").tag(2)
                    }
                }
                ExplainedRow(preview: .tapAndDrag,
                             caption: L("Tap, then touch again and move the mouse — whatever is under the cursor comes along, and lifting your finger drops it. A quick second tap is still a double-click.")) {
                    Toggle("Tap and drag", isOn: $settings.tapAndDrag)
                }
                ExplainedRow(preview: .twoFingerDrag,
                             caption: L("Rest two fingers on the surface and hold for a moment — then move the mouse to drag, and lift to drop. Fingers that land one after the other are just your grip, and never trigger it.")) {
                    Toggle("Two-finger drag", isOn: $settings.twoFingerDrag)
                }
                ExplainedRow(preview: .tapZone(depth: settings.tapZoneDepth),
                             caption: L("Taps only count on the front part of the surface — where deliberate fingertip taps land. The fingers gripping the sides can't click by accident. Pressing to click and dragging still work everywhere.")) {
                    Toggle("Front of the mouse only", isOn: $settings.tapZoneEnabled)
                }
            }
        }
    }

    /// The sliders. Each one only shows while the switch it tunes is on.
    @ViewBuilder private var fineTuningRows: some View {
        if settings.blockScrollWhileClicked {
            ExplainedRow(preview: .settleAfterRelease,
                         caption: L("As your finger lifts off after a click it usually drags the page a tiny bit. This gives it a moment to settle before scrolling works again.")) {
                SliderRow("Hold still after you let go",
                          value: Binding(get: { Double(settings.releaseGraceMs) },
                                         set: { settings.releaseGraceMs = Int($0.rounded()) }),
                          in: 0...800, step: 50,
                          reading: settings.releaseGraceMs == 0 ? L("Off") : String(format: "%.2g s", Double(settings.releaseGraceMs) / 1000))
            }
        }
        ExplainedRow(preview: .ignoreNudges,
                     caption: L("The page only starts scrolling once your finger has moved at least this far — so a finger just resting on the mouse can't nudge anything.")) {
            SliderRow("Ignore small finger nudges", value: $settings.deadZone, in: 0...40, step: 1,
                      reading: settings.deadZone == 0 ? L("Off") : "\(Int(settings.deadZone)) pt")
        }
        if settings.axisLock {
            ExplainedRow(caption: L("A distance, not a time — once your finger has slid this far, the swipe commits to one direction. Lower locks in sooner.")) {
                SliderRow("Lock direction after sliding", value: $settings.axisLockThreshold, in: 2...40, step: 1,
                          reading: "\(Int(settings.axisLockThreshold)) pt")
            }
        }
        if settings.tapToClick {
            ExplainedRow(preview: .tapToClick,
                         caption: L("Firm: only quick, deliberate taps count — the fewest accidental clicks. Light: gentler, slower taps work too.")) {
                SliderRow("Tap firmness", value: $settings.tapSensitivity, in: 0...1, from: "Firm", to: "Light")
            }
            if settings.tapZoneEnabled {
                SliderRow("Size of the tap area", value: $settings.tapZoneDepth, in: 0.25...0.75, step: 0.05,
                          reading: "\(Int((settings.tapZoneDepth * 100).rounded()))%")
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

// MARK: - Scroll Wheel

private struct WheelPane: View {
    @ObservedObject var settings: Settings

    var body: some View {
        Form {
            Section {
                ExplainedRow(caption: L("The page glides between notches instead of jumping. Zooming with ⌘, ⌃ or ⌥ held stays one step per notch.")) {
                    Toggle("Smooth scrolling", isOn: $settings.smoothWheelScroll)
                }
                if settings.smoothWheelScroll {
                    SliderRow("Scrolling speed", value: $settings.wheelScrollSpeed, in: 1...6, from: "Slow", to: "Fast")
                }
                ExplainedRow(caption: L("Turns the wheel's scrolling the other way round, without touching your trackpad or Magic Mouse. macOS itself has one direction setting for every device.")) {
                    Toggle("Reverse direction", isOn: $settings.reverseWheelScroll)
                }
                ExplainedRow(caption: L("Pressing the wheel down almost always turns it a little. While it's pressed, and for a moment after, that can't scroll the page.")) {
                    Toggle("Don't scroll while pressed", isOn: $settings.blockScrollWhileWheelPressed)
                }
            }
        }
        .formStyle(.grouped)
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
            if mice.kindsToShow(for: settings).contains(.magicMouse) {
                appsSection
                shortcutsSection
            }
            Section {
                Picker("Mouse type", selection: $settings.mouseType) {
                    Text("Automatic").tag(MouseTypeSetting.automatic)
                    Divider()
                    Text("Magic Mouse").tag(MouseTypeSetting.magicMouse)
                    Text("Scroll Wheel").tag(MouseTypeSetting.wheelMouse)
                }
            }
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
            ExplainedRow(caption: L("Rules apply while that app is in front. Anything left as “Default” follows the Magic Mouse page.")) {
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
                         caption: L("Hold the key and scroll on the Magic Mouse to get the action you picked — here, zooming instead of scrolling.")) {
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

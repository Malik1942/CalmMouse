import AppKit
import SwiftUI
import ServiceManagement
import CalmMouseCore

final class SettingsWindowController: NSWindowController {
    init(settings: Settings, battery: BatteryMonitor) {
        let root = SettingsView(settings: settings, battery: battery)
        let hosting = NSHostingController(rootView: root)
        let window = NSWindow(contentViewController: hosting)
        window.title = "CalmMouse Settings"
        window.styleMask = [.titled, .closable, .miniaturizable]
        window.setContentSize(NSSize(width: 620, height: 520))
        window.center()
        window.isReleasedWhenClosed = false
        super.init(window: window)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError() }
}

// MARK: - Root

struct SettingsView: View {
    @ObservedObject var settings: Settings
    let battery: BatteryMonitor
    /// Remembers which tab was open last time the window was shown.
    @AppStorage("settingsTab") private var tab = 0

    var body: some View {
        TabView(selection: $tab) {
            GeneralTab(settings: settings).tabItem { Label("General", systemImage: "gearshape") }.tag(0)
            ScrollingTab(settings: settings).tabItem { Label("Scrolling", systemImage: "arrow.up.and.down") }.tag(1)
            ClickingTab(settings: settings).tabItem { Label("Clicking", systemImage: "hand.tap") }.tag(2)
            AppsTab(settings: settings).tabItem { Label("Apps", systemImage: "square.grid.2x2") }.tag(3)
            ModifiersTab(settings: settings).tabItem { Label("Shortcuts", systemImage: "command") }.tag(4)
            BatteryTab(settings: settings, battery: battery).tabItem { Label("Battery", systemImage: "battery.25") }.tag(5)
        }
        .padding(20)
        .frame(width: 620, height: 520)
    }
}

// MARK: - General

private struct GeneralTab: View {
    @ObservedObject var settings: Settings
    @State private var launchAtLogin = SettingsHelpers.launchAtLoginEnabled()
    @State private var trusted = AXIsProcessTrusted()
    private let timer = Timer.publish(every: 2, on: .main, in: .common).autoconnect()

    var body: some View {
        Form {
            Section {
                Toggle("Enable CalmMouse", isOn: $settings.enabled)
                    .toggleStyle(.switch)
                Toggle("Launch at login", isOn: Binding(
                    get: { launchAtLogin },
                    set: { launchAtLogin = SettingsHelpers.setLaunchAtLogin($0) }))
            }

            Section("Permission") {
                HStack {
                    Image(systemName: trusted ? "checkmark.circle.fill" : "exclamationmark.triangle.fill")
                        .foregroundStyle(trusted ? .green : .orange)
                    Text(trusted
                         ? "Accessibility access granted."
                         : "CalmMouse needs Accessibility access to see mouse events.")
                    Spacer()
                    if !trusted {
                        Button("Open Settings…") { SettingsHelpers.openAccessibilityPane() }
                    }
                }
                if !trusted {
                    // The classic trap: the toggle in System Settings shows ON but macOS is
                    // matching an old signature (rebuild / re-sign). Give the user a way out.
                    HStack(alignment: .top) {
                        Text("Toggle already ON in System Settings but still not working? The stored grant is stale (usually after a rebuild). Reset it and grant again.")
                            .font(.caption).foregroundStyle(.secondary)
                        Spacer()
                        Button("Reset grant…") { SettingsHelpers.resetAccessibilityGrant() }
                    }
                }
            }

            Section("Troubleshooting") {
                Toggle("Assume unrecognised touch scrolling is the Magic Mouse", isOn: $settings.treatUnknownContinuousAsMagicMouse)
                Text("Only needed if a macOS update stops CalmMouse recognising your mouse. Leave off — otherwise trackpad scrolling can get caught too.")
                    .font(.caption).foregroundStyle(.secondary)
                Toggle("Debug logging", isOn: $settings.debugLogging)
                Text("log stream --predicate 'subsystem == \"com.calmmouse.app\"' --level debug")
                    .font(.system(.caption, design: .monospaced)).foregroundStyle(.secondary)
                    .textSelection(.enabled)
            }
        }
        .formStyle(.grouped)
        .onReceive(timer) { _ in trusted = AXIsProcessTrusted() }
    }
}

// MARK: - Scrolling

private struct ScrollingTab: View {
    @ObservedObject var settings: Settings

    var body: some View {
        Form {
            Section("Clicking") {
                ExplainedRow(preview: .blockWhileClick,
                             caption: "While a mouse button is held down, your finger resting on the surface can't scroll the page out from under your click.") {
                    Toggle("Don't scroll while clicking", isOn: $settings.blockScrollWhileClicked)
                }
                ExplainedRow(preview: .settleAfterRelease,
                             caption: "As your finger lifts off after a click it usually drags the page a tiny bit. This gives it a moment to settle before scrolling works again.") {
                    HStack {
                        Text("Hold still after you let go")
                        Slider(value: Binding(
                            get: { Double(settings.releaseGraceMs) },
                            set: { settings.releaseGraceMs = Int($0.rounded()) }),
                               in: 0...800, step: 50)
                        Text(settings.releaseGraceMs == 0 ? "Off" : String(format: "%.2g s", Double(settings.releaseGraceMs) / 1000))
                            .monospacedDigit().frame(width: 60, alignment: .trailing)
                    }
                }
                .disabled(!settings.blockScrollWhileClicked)
            }

            Section("Steadiness") {
                ExplainedRow(preview: .ignoreNudges,
                             caption: "The page only starts scrolling once your finger has moved at least this far — so a finger just resting on the mouse can't nudge anything.") {
                    HStack {
                        Text("Ignore small finger nudges")
                        Slider(value: $settings.deadZone, in: 0...40, step: 1)
                        Text(settings.deadZone == 0 ? "Off" : "\(Int(settings.deadZone)) pt")
                            .monospacedDigit().frame(width: 60, alignment: .trailing)
                    }
                }

                ExplainedRow(preview: .straightLines,
                             caption: "Scrolling sticks to straight up-down or left-right. A swipe that drifts a little diagonally won't wander sideways.") {
                    Toggle("Scroll in straight lines", isOn: $settings.axisLock)
                }
                HStack {
                    Text("Lock direction after sliding")
                    Slider(value: $settings.axisLockThreshold, in: 2...40, step: 1)
                    Text("\(Int(settings.axisLockThreshold)) pt").monospacedDigit().frame(width: 60, alignment: .trailing)
                }
                .disabled(!settings.axisLock)
                Text("A distance, not a time — once your finger has slid this far, the swipe commits to one direction. Lower locks in sooner.")
                    .font(.caption).foregroundStyle(.secondary)
            }

            Section("Feel") {
                ExplainedRow(preview: .momentum(on: settings.momentumEnabled),
                             caption: "On: the page keeps gliding after a quick swipe, like a trackpad. Off: it stops the moment your finger does. Only affects the Magic Mouse — macOS's own setting changes every device.") {
                    Toggle("Keep gliding after a swipe", isOn: $settings.momentumEnabled)
                }
                ExplainedRow(preview: .ignoreSideways,
                             caption: "Sideways swipes are ignored completely. Scrolling up and down still works.") {
                    Toggle("Ignore sideways scrolling", isOn: $settings.blockHorizontalScroll)
                }
            }
        }
        .formStyle(.grouped)
    }
}

// MARK: - Clicking

private struct ClickingTab: View {
    @ObservedObject var settings: Settings

    var body: some View {
        Form {
            Section("Tap to click") {
                ExplainedRow(preview: .tapToClick,
                             caption: "A light single-finger tap on the surface clicks — no need to press the mouse down. Pressing to click keeps working as before.") {
                    Toggle("Tap to click", isOn: $settings.tapToClick)
                }
                ExplainedRow(preview: .tapToClick,
                             caption: "Firm: only quick, deliberate taps count — the fewest accidental clicks. Light: gentler, slower taps work too.") {
                    HStack {
                        Text("Firm")
                            .font(.caption).foregroundStyle(.secondary)
                        Slider(value: $settings.tapSensitivity, in: 0...1)
                        Text("Light")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                }
                .disabled(!settings.tapToClick)
            }

            Section("Where taps count") {
                ExplainedRow(preview: .tapZone(depth: settings.tapZoneDepth),
                             caption: "Taps only count on the front part of the surface — where deliberate fingertip taps land. The fingers gripping the sides can't click by accident. Pressing to click and dragging still work everywhere.") {
                    Toggle("Front of the mouse only", isOn: $settings.tapZoneEnabled)
                }
                .disabled(!settings.tapToClick)
                HStack {
                    Text("Size of the tap area")
                    Slider(value: $settings.tapZoneDepth, in: 0.25...0.75, step: 0.05)
                    Text("front \(Int((settings.tapZoneDepth * 100).rounded()))%")
                        .monospacedDigit().frame(width: 76, alignment: .trailing)
                }
                .disabled(!settings.tapToClick || !settings.tapZoneEnabled)
            }

            Section("Dragging") {
                ExplainedRow(preview: .tapAndDrag,
                             caption: "Tap, then touch again and move the mouse — whatever is under the cursor comes along, and lifting your finger drops it. A quick second tap is still a double-click.") {
                    Toggle("Tap and drag", isOn: $settings.tapAndDrag)
                }
                .disabled(!settings.tapToClick)
                ExplainedRow(preview: .twoFingerDrag,
                             caption: "Rest two fingers on the surface and hold for a moment — then move the mouse to drag, and lift to drop. Fingers that land one after the other are just your grip, and never trigger it.") {
                    Toggle("Two-finger drag", isOn: $settings.twoFingerDrag)
                }
                .disabled(!settings.tapToClick)
            }

            Section("Accidental tap protection") {
                Text("Taps are ignored automatically:")
                    .font(.caption).foregroundStyle(.secondary)
                VStack(alignment: .leading, spacing: 4) {
                    bullet("while — and just after — you're scrolling")
                    bullet("while a button is held down, and right after a real click")
                    bullet("when two or more fingers are on the surface")
                    bullet("for fingers that rest too long, or barely brush the surface")
                }
                Text("Double- and triple-taps become real double- and triple-clicks.")
                    .font(.caption).foregroundStyle(.secondary)
            }

            if !TapController.isSupported {
                Section {
                    Label("This macOS build doesn't expose the multitouch framework — tap-to-click is unavailable.", systemImage: "exclamationmark.triangle.fill")
                        .foregroundStyle(.orange)
                }
            }
        }
        .formStyle(.grouped)
    }

    private func bullet(_ text: String) -> some View {
        HStack(alignment: .top, spacing: 6) {
            Text("•")
            Text(text)
        }
        .font(.caption).foregroundStyle(.secondary)
    }
}

// MARK: - Per-app rules

private struct AppsTab: View {
    @ObservedObject var settings: Settings
    @State private var selection: UUID?

    private var selectedRule: Binding<AppRule>? {
        guard let id = selection, let index = settings.appRules.firstIndex(where: { $0.id == id }) else { return nil }
        return Binding(
            get: { settings.appRules[index] },
            set: { settings.appRules[index] = $0 })
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Rules apply while that app is in front. Anything left as “Default” follows the Scrolling tab.")
                .font(.caption).foregroundStyle(.secondary)

            HStack(alignment: .top, spacing: 12) {
                VStack(spacing: 0) {
                    List(selection: $selection) {
                        ForEach(settings.appRules) { rule in
                            HStack {
                                Text(rule.name.isEmpty ? rule.bundleID : rule.name)
                                Spacer()
                                if !rule.enabled { Image(systemName: "pause.circle").foregroundStyle(.secondary) }
                            }
                            .tag(rule.id)
                        }
                    }
                    .frame(width: 200)
                    HStack {
                        Button { addApp() } label: { Image(systemName: "plus") }
                        Button { if let id = selection { settings.removeRule(id: id); selection = nil } }
                            label: { Image(systemName: "minus") }
                            .disabled(selection == nil)
                        Spacer()
                    }
                    .buttonStyle(.borderless)
                    .padding(6)
                }
                .background(RoundedRectangle(cornerRadius: 6).strokeBorder(.separator))

                if let rule = selectedRule {
                    RuleEditor(rule: rule)
                } else {
                    VStack {
                        Spacer()
                        Text("Select or add an app").foregroundStyle(.secondary)
                        Spacer()
                    }
                    .frame(maxWidth: .infinity)
                }
            }
        }
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
            selection = existing.id
            return
        }
        var rule = AppRule(bundleID: bundleID, name: name)
        rule.disableScrollEntirely = false
        settings.upsert(rule)
        selection = rule.id
    }
}

private struct RuleEditor: View {
    @Binding var rule: AppRule

    var body: some View {
        Form {
            Section {
                Toggle("Rule enabled", isOn: $rule.enabled)
                LabeledContent("App ID") {
                    Text(rule.bundleID).font(.system(.caption, design: .monospaced))
                        .textSelection(.enabled)
                }
            }
            Section("In this app") {
                TriToggle(title: "Ignore Magic Mouse scrolling", value: $rule.disableScrollEntirely)
                TriToggle(title: "Don't scroll while clicking", value: $rule.blockScrollWhileClicked)
                TriToggle(title: "Ignore sideways scrolling", value: $rule.blockHorizontalScroll)
                TriToggle(title: "Keep gliding after a swipe", value: $rule.momentumEnabled)
                TriToggle(title: "Scroll in straight lines", value: $rule.axisLock)
            }
        }
        .formStyle(.grouped)
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
            .pickerStyle(.segmented)
    }
}

// MARK: - Modifiers

private struct ModifiersTab: View {
    @ObservedObject var settings: Settings

    private let combos: [ModifierCombo] = [.command, .option, .control, .shift,
                                           [.command, .option], [.command, .shift], [.control, .option]]

    var body: some View {
        Form {
            Section {
                ForEach(combos, id: \.rawValue) { combo in
                    Picker("While holding " + combo.label, selection: Binding(
                        get: { settings.modifierActions[combo] ?? .normal },
                        set: { action in
                            var m = settings.modifierActions
                            if action == .normal { m.removeValue(forKey: combo) } else { m[combo] = action }
                            settings.modifierActions = m
                        })) {
                            ForEach(ScrollAction.allCases, id: \.self) { Text($0.label).tag($0) }
                        }
                }
            } header: {
                ExplainedRow(preview: .keyZoom,
                             caption: "Hold the key and scroll on the Magic Mouse to get the action you picked — here, zooming instead of scrolling.") {
                    Text("Hold a key while you scroll to change what it does")
                    Spacer()
                }
            }
            Section {
                Text("“Zoom in apps” is the zoom Figma, Sketch, Preview and most design apps use. “Zoom the whole screen” magnifies everything on screen (the macOS zoom). Only the Magic Mouse is affected — your trackpad and other mice keep working normally.")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
    }
}

// MARK: - Battery

private struct BatteryTab: View {
    @ObservedObject var settings: Settings
    let battery: BatteryMonitor
    @State private var reading: BatteryMonitor.Reading?
    private let timer = Timer.publish(every: 30, on: .main, in: .common).autoconnect()

    var body: some View {
        Form {
            Section("Magic Mouse") {
                HStack {
                    Image(systemName: symbol)
                    if let reading {
                        Text("\(reading.deviceName) — \(reading.percent)%")
                    } else {
                        Text("No Magic Mouse detected").foregroundStyle(.secondary)
                    }
                    Spacer()
                    Button("Refresh") { battery.poll(); reading = battery.latest }
                }
            }
            Section("Low battery warning") {
                Toggle("Warn me when the battery gets low", isOn: $settings.batteryWarningEnabled)
                HStack {
                    Text("Warn below")
                    Slider(value: Binding(
                        get: { Double(settings.batteryWarningThreshold) },
                        set: { settings.batteryWarningThreshold = Int($0.rounded()) }),
                           in: 5...50, step: 5)
                    Text("\(settings.batteryWarningThreshold)%").monospacedDigit().frame(width: 50, alignment: .trailing)
                }
                .disabled(!settings.batteryWarningEnabled)
                Text("Checked every five minutes. The menu bar icon turns yellow once you've been warned, and clears when you open Settings.")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
        .onAppear { battery.poll(); reading = battery.latest }
        .onReceive(timer) { _ in reading = battery.latest }
    }

    private var symbol: String {
        guard let p = reading?.percent else { return "questionmark.circle" }
        switch p {
        case ..<15: return "battery.0percent"
        case ..<40: return "battery.25percent"
        case ..<75: return "battery.50percent"
        default: return "battery.100percent"
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
            alert.messageText = "Couldn't change the login item"
            alert.informativeText = "\(error.localizedDescription)\n\nLaunch at login only works when CalmMouse runs from an app bundle (e.g. /Applications)."
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
        let opts = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(opts)
        openAccessibilityPane()
    }

    static func openAccessibilityPane() {
        let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")!
        NSWorkspace.shared.open(url)
    }
}

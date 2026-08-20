import AppKit
import SwiftUI
import ServiceManagement
import GodmouseCore

final class SettingsWindowController: NSWindowController {
    init(settings: Settings, battery: BatteryMonitor) {
        let root = SettingsView(settings: settings, battery: battery)
        let hosting = NSHostingController(rootView: root)
        let window = NSWindow(contentViewController: hosting)
        window.title = "Godmouse Settings"
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

    var body: some View {
        TabView {
            GeneralTab(settings: settings).tabItem { Label("General", systemImage: "gearshape") }
            ScrollingTab(settings: settings).tabItem { Label("Scrolling", systemImage: "arrow.up.and.down") }
            ClickingTab(settings: settings).tabItem { Label("Clicking", systemImage: "hand.tap") }
            AppsTab(settings: settings).tabItem { Label("Apps", systemImage: "square.grid.2x2") }
            ModifiersTab(settings: settings).tabItem { Label("Modifiers", systemImage: "command") }
            BatteryTab(settings: settings, battery: battery).tabItem { Label("Battery", systemImage: "battery.25") }
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
                Toggle("Enable Godmouse", isOn: $settings.enabled)
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
                         : "Godmouse needs Accessibility access to see mouse events.")
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
                Toggle("Treat unidentified touch scrolls as Magic Mouse", isOn: $settings.treatUnknownContinuousAsMagicMouse)
                Text("Only needed if a macOS update breaks device identification. Leave off — otherwise trackpad scrolling can get caught too.")
                    .font(.caption).foregroundStyle(.secondary)
                Toggle("Debug logging", isOn: $settings.debugLogging)
                Text("log stream --predicate 'subsystem == \"com.godmouse.app\"' --level debug")
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
            Section("While clicking") {
                Toggle("Block scrolling while a Magic Mouse button is held", isOn: $settings.blockScrollWhileClicked)
                HStack {
                    Text("Keep blocking after release")
                    Slider(value: Binding(
                        get: { Double(settings.releaseGraceMs) },
                        set: { settings.releaseGraceMs = Int($0.rounded()) }),
                           in: 0...800, step: 50)
                    Text("\(settings.releaseGraceMs) ms").monospacedDigit().frame(width: 60, alignment: .trailing)
                }
                .disabled(!settings.blockScrollWhileClicked)
                Text("Lifting a finger off the shell usually produces a small trailing scroll. This eats it.")
                    .font(.caption).foregroundStyle(.secondary)
            }

            Section("Jitter") {
                HStack {
                    Text("Dead zone")
                    Slider(value: $settings.deadZone, in: 0...40, step: 1)
                    Text(settings.deadZone == 0 ? "Off" : "\(Int(settings.deadZone)) pt")
                        .monospacedDigit().frame(width: 60, alignment: .trailing)
                }
                Text("A gesture must travel this far before anything scrolls, so a resting finger can't nudge the page.")
                    .font(.caption).foregroundStyle(.secondary)

                Toggle("Axis lock", isOn: $settings.axisLock)
                HStack {
                    Text("Commit after")
                    Slider(value: $settings.axisLockThreshold, in: 2...40, step: 1)
                    Text("\(Int(settings.axisLockThreshold)) pt").monospacedDigit().frame(width: 60, alignment: .trailing)
                }
                .disabled(!settings.axisLock)
                Text("Once a scroll commits to vertical or horizontal, the other axis is ignored for the rest of the gesture.")
                    .font(.caption).foregroundStyle(.secondary)
            }

            Section("Behaviour") {
                Toggle("Momentum scrolling", isOn: $settings.momentumEnabled)
                Text("Off = the page stops the moment your finger does. Applies to the Magic Mouse only; macOS's own setting is system-wide.")
                    .font(.caption).foregroundStyle(.secondary)
                Toggle("Ignore horizontal scrolling", isOn: $settings.blockHorizontalScroll)
            }
        }
        .formStyle(.grouped)
    }
}

// MARK: - Clicking

private struct ClickingTab: View {
    @ObservedObject var settings: Settings

    private var config: TapConfig { TapConfig(sensitivity: settings.tapSensitivity) }

    var body: some View {
        Form {
            Section("Tap to click") {
                Toggle("Tap to click", isOn: $settings.tapToClick)
                Text("A light single-finger tap on the surface left-clicks — no need to press the mouse down. Physical clicking keeps working as before.")
                    .font(.caption).foregroundStyle(.secondary)

                HStack {
                    Text("Firm")
                        .font(.caption).foregroundStyle(.secondary)
                    Slider(value: $settings.tapSensitivity, in: 0...1)
                    Text("Light")
                        .font(.caption).foregroundStyle(.secondary)
                }
                .disabled(!settings.tapToClick)
                Text("Firm = only quick, deliberate taps count (fewest accidents). Light = easier to trigger. Right now a tap must finish within \(Int(config.maxDuration * 1000)) ms and move less than \(String(format: "%.0f", config.maxMovement * 100))% of the surface.")
                    .font(.caption).foregroundStyle(.secondary)
            }

            Section("Accident protection") {
                Text("Taps are ignored automatically:")
                    .font(.caption).foregroundStyle(.secondary)
                VStack(alignment: .leading, spacing: 4) {
                    bullet("while — and shortly after — real scrolling (\(Int(config.scrollCooldown * 1000)) ms, follows the slider)")
                    bullet("while a physical button is down, and \(Int(config.buttonCooldown * 1000)) ms after a real click")
                    bullet("when two or more fingers touch the surface")
                    bullet("for resting fingers (too long) and grazes (too small)")
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
            Text("Rules apply while that app is frontmost. Anything left as “Inherit” follows the Scrolling tab.")
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
                LabeledContent("Bundle ID") {
                    Text(rule.bundleID).font(.system(.caption, design: .monospaced))
                        .textSelection(.enabled)
                }
            }
            Section("Overrides") {
                TriToggle(title: "Ignore Magic Mouse scrolling", value: $rule.disableScrollEntirely)
                TriToggle(title: "Block scroll while clicked", value: $rule.blockScrollWhileClicked)
                TriToggle(title: "Ignore horizontal scrolling", value: $rule.blockHorizontalScroll)
                TriToggle(title: "Momentum scrolling", value: $rule.momentumEnabled)
                TriToggle(title: "Axis lock", value: $rule.axisLock)
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
                Text("Inherit").tag(0)
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
            Section("Hold a modifier while scrolling with the Magic Mouse") {
                ForEach(combos, id: \.rawValue) { combo in
                    Picker(combo.label + " scroll", selection: Binding(
                        get: { settings.modifierActions[combo] ?? .normal },
                        set: { action in
                            var m = settings.modifierActions
                            if action == .normal { m.removeValue(forKey: combo) } else { m[combo] = action }
                            settings.modifierActions = m
                        })) {
                            ForEach(ScrollAction.allCases, id: \.self) { Text($0.label).tag($0) }
                        }
                }
            }
            Section {
                Text("“Zoom (⌘)” relabels the scroll as ⌘-scroll, which Figma, Sketch, Preview and most creative apps zoom with. “Zoom (⌃)” drives the macOS system zoom. Only the Magic Mouse is affected — your trackpad and other mice keep their normal behaviour.")
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
            Section("Low battery nudge") {
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
            alert.informativeText = "\(error.localizedDescription)\n\nLaunch at login only works when Godmouse runs from an app bundle (e.g. /Applications)."
            alert.runModal()
        }
        return SMAppService.mainApp.status == .enabled
    }

    /// Removes our own TCC row (`tccutil reset Accessibility <bundle id>`) — allowed for one's own
    /// bundle without admin rights — then re-prompts so the fresh grant matches the current signature.
    static func resetAccessibilityGrant() {
        let bundleID = Bundle.main.bundleIdentifier ?? "com.godmouse.app"
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

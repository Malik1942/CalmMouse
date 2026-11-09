import Foundation
import AppKit
import Combine
import CalmMouseCore

/// UserDefaults-backed settings, observable by the UI.
/// Simple flags stay individual keys so they're scriptable:
///   defaults write com.calmmouse.app blockScrollWhileClicked -bool YES
/// Structured data (per-app rules, modifier actions) is stored as JSON.
final class Settings: ObservableObject {
    static let shared = Settings()
    static let didChange = Notification.Name("CalmMouseSettingsDidChange")
    static let maximumWheelScrollSpeed = 12.0

    private let d = UserDefaults.standard
    private var loading = false

    private enum Key {
        static let enabled = "enabled"
        static let blockScrollWhileClicked = "blockScrollWhileClicked"
        static let releaseGraceMs = "releaseGraceMs"
        static let deadZone = "deadZone"
        static let axisLock = "axisLock"
        static let axisLockThreshold = "axisLockThreshold"
        static let momentumEnabled = "momentumEnabled"
        static let blockHorizontalScroll = "blockHorizontalScroll"
        static let modifierActions = "modifierActions"
        static let appRules = "appRules"
        static let tapToClick = "tapToClick"
        static let tapSensitivity = "tapSensitivity"
        static let tapRightClick = "tapRightClick"
        static let tapRightClickMode = "tapRightClickMode"
        static let tapAndDrag = "tapAndDrag"
        static let twoFingerDrag = "twoFingerDrag"
        static let tapZoneEnabled = "tapZoneEnabled"
        static let tapZoneDepth = "tapZoneDepth"
        static let batteryWarningEnabled = "batteryWarningEnabled"
        static let batteryWarningThreshold = "batteryWarningThreshold"
        static let treatUnknownContinuousAsMagicMouse = "treatUnknownContinuousAsMagicMouse"
        static let mouseType = "mouseType"
        static let rememberedMouseKinds = "rememberedMouseKinds"
        static let reverseWheelScroll = "reverseWheelScroll"
        static let blockScrollWhileWheelPressed = "blockScrollWhileWheelPressed"
        static let smoothWheelScroll = "smoothWheelScroll"
        static let wheelScrollSpeed = "wheelScrollSpeed"
        static let magicMouseTrackingSpeed = "magicMouseTrackingSpeed"
        static let wheelTrackingSpeed = "wheelTrackingSpeed"
        static let customPresets = "customPresets"
        static let onboardingCompleted = "onboardingCompleted"
        static let accessibilityPromptShown = "accessibilityPromptShown"
    }

    /// Whether the first-launch welcome tour has been seen (or dismissed).
    /// Not `@Published` — nothing observes it; it's checked once at launch.
    var onboardingCompleted: Bool {
        get { d.bool(forKey: Key.onboardingCompleted) }
        set { d.set(newValue, forKey: Key.onboardingCompleted) }
    }

    /// Whether the system's own "grant Accessibility access" dialog has been requested once.
    /// macOS queues one dialog per request and never merges them, so asking on every
    /// ungranted launch stacks dialogs — close one and the next is waiting behind it.
    var accessibilityPromptShown: Bool {
        get { d.bool(forKey: Key.accessibilityPromptShown) }
        set { d.set(newValue, forKey: Key.accessibilityPromptShown) }
    }

    /// Every kind of mouse this Mac has had connected. The settings window keeps showing a
    /// kind's options after its mouse disconnects (a Magic Mouse does so whenever it sleeps).
    /// Not `@Published`: `MouseEnvironment` announces the change itself.
    var rememberedMouseKinds: Set<MouseKind> {
        get { Set((d.stringArray(forKey: Key.rememberedMouseKinds) ?? []).compactMap(MouseKind.init(rawValue:))) }
        set { d.set(newValue.map(\.rawValue).sorted(), forKey: Key.rememberedMouseKinds) }
    }

    // MARK: Published state

    @Published var enabled: Bool { didSet { d.set(enabled, forKey: Key.enabled); changed() } }
    @Published var blockScrollWhileClicked: Bool { didSet { d.set(blockScrollWhileClicked, forKey: Key.blockScrollWhileClicked); changed() } }
    @Published var releaseGraceMs: Int { didSet { d.set(releaseGraceMs, forKey: Key.releaseGraceMs); changed() } }
    @Published var deadZone: Double { didSet { d.set(deadZone, forKey: Key.deadZone); changed() } }
    @Published var axisLock: Bool { didSet { d.set(axisLock, forKey: Key.axisLock); changed() } }
    @Published var axisLockThreshold: Double { didSet { d.set(axisLockThreshold, forKey: Key.axisLockThreshold); changed() } }
    @Published var momentumEnabled: Bool { didSet { d.set(momentumEnabled, forKey: Key.momentumEnabled); changed() } }
    @Published var blockHorizontalScroll: Bool { didSet { d.set(blockHorizontalScroll, forKey: Key.blockHorizontalScroll); changed() } }
    @Published var modifierActions: [ModifierCombo: ScrollAction] { didSet { saveModifierActions(); changed() } }
    @Published var appRules: [AppRule] { didSet { saveAppRules(); changed() } }
    @Published var customPresets: [Preset] { didSet { saveCustomPresets(); changed() } }
    @Published var tapToClick: Bool { didSet { d.set(tapToClick, forKey: Key.tapToClick); changed() } }
    @Published var tapSensitivity: Double { didSet { d.set(tapSensitivity, forKey: Key.tapSensitivity); changed() } }
    @Published var tapRightClick: Bool { didSet { d.set(tapRightClick, forKey: Key.tapRightClick); changed() } }
    @Published var tapRightClickMode: RightClickMode { didSet { d.set(tapRightClickMode.rawValue, forKey: Key.tapRightClickMode); changed() } }
    @Published var tapAndDrag: Bool { didSet { d.set(tapAndDrag, forKey: Key.tapAndDrag); changed() } }
    @Published var twoFingerDrag: Bool { didSet { d.set(twoFingerDrag, forKey: Key.twoFingerDrag); changed() } }
    @Published var tapZoneEnabled: Bool { didSet { d.set(tapZoneEnabled, forKey: Key.tapZoneEnabled); changed() } }
    @Published var tapZoneDepth: Double { didSet { d.set(tapZoneDepth, forKey: Key.tapZoneDepth); changed() } }
    @Published var batteryWarningEnabled: Bool { didSet { d.set(batteryWarningEnabled, forKey: Key.batteryWarningEnabled); changed() } }
    @Published var batteryWarningThreshold: Int { didSet { d.set(batteryWarningThreshold, forKey: Key.batteryWarningThreshold); changed() } }
    @Published var treatUnknownContinuousAsMagicMouse: Bool { didSet { d.set(treatUnknownContinuousAsMagicMouse, forKey: Key.treatUnknownContinuousAsMagicMouse); changed() } }
    @Published var mouseType: MouseTypeSetting { didSet { d.set(mouseType.rawValue, forKey: Key.mouseType); changed() } }
    @Published var reverseWheelScroll: Bool { didSet { d.set(reverseWheelScroll, forKey: Key.reverseWheelScroll); changed() } }
    @Published var blockScrollWhileWheelPressed: Bool { didSet { d.set(blockScrollWhileWheelPressed, forKey: Key.blockScrollWhileWheelPressed); changed() } }
    @Published var smoothWheelScroll: Bool { didSet { d.set(smoothWheelScroll, forKey: Key.smoothWheelScroll); changed() } }
    @Published var wheelScrollSpeed: Double { didSet { d.set(wheelScrollSpeed, forKey: Key.wheelScrollSpeed); changed() } }
    // Tracking speed: -1...1, 0 is normal. Not registered with a default on purpose: until the
    // slider has been moved the key is absent, and CalmMouse leaves that mouse's speed alone.
    @Published var magicMouseTrackingSpeed: Double { didSet { d.set(magicMouseTrackingSpeed, forKey: Key.magicMouseTrackingSpeed); changed() } }
    @Published var wheelTrackingSpeed: Double { didSet { d.set(wheelTrackingSpeed, forKey: Key.wheelTrackingSpeed); changed() } }
    /// nil until the user has chosen a speed for that kind of mouse.
    var chosenMagicMouseTrackingSpeed: Double? { d.object(forKey: Key.magicMouseTrackingSpeed) == nil ? nil : magicMouseTrackingSpeed }
    var chosenWheelTrackingSpeed: Double? { d.object(forKey: Key.wheelTrackingSpeed) == nil ? nil : wheelTrackingSpeed }

    private init() {
        Settings.migrateFromGodmouseIfNeeded(d)
        d.register(defaults: [
            Key.enabled: true,
            Key.blockScrollWhileClicked: true,
            Key.releaseGraceMs: 200,
            Key.deadZone: 0.0,
            Key.axisLock: false,
            Key.axisLockThreshold: 10.0,
            Key.momentumEnabled: true,
            Key.blockHorizontalScroll: false,
            Key.tapToClick: false,
            Key.tapSensitivity: 0.5,
            Key.tapRightClick: false,
            Key.tapRightClickMode: RightClickMode.rightSide.rawValue,
            Key.tapAndDrag: false,
            Key.twoFingerDrag: false,
            Key.tapZoneEnabled: false,
            Key.tapZoneDepth: 0.5,
            Key.batteryWarningEnabled: true,
            Key.batteryWarningThreshold: 15,
            // Documented escape hatch (README → Troubleshooting) for Magic Mouse variants the
            // device identification doesn't recognise; defaults-only, no UI.
            Key.treatUnknownContinuousAsMagicMouse: false,
            Key.mouseType: MouseTypeSetting.automatic.rawValue,
            // Wheel-mouse options start off, so supporting wheel mice changes nothing for
            // anyone until they turn one on.
            Key.reverseWheelScroll: false,
            Key.blockScrollWhileWheelPressed: false,
            Key.smoothWheelScroll: false,
            Key.wheelScrollSpeed: 3.0,
        ])
        loading = true
        enabled = d.bool(forKey: Key.enabled)
        blockScrollWhileClicked = d.bool(forKey: Key.blockScrollWhileClicked)
        releaseGraceMs = d.integer(forKey: Key.releaseGraceMs)
        deadZone = d.double(forKey: Key.deadZone)
        axisLock = d.bool(forKey: Key.axisLock)
        axisLockThreshold = d.double(forKey: Key.axisLockThreshold)
        momentumEnabled = d.bool(forKey: Key.momentumEnabled)
        blockHorizontalScroll = d.bool(forKey: Key.blockHorizontalScroll)
        tapToClick = d.bool(forKey: Key.tapToClick)
        tapSensitivity = d.double(forKey: Key.tapSensitivity)
        tapRightClick = d.bool(forKey: Key.tapRightClick)
        tapRightClickMode = RightClickMode(rawValue: d.string(forKey: Key.tapRightClickMode) ?? "") ?? .rightSide
        tapAndDrag = d.bool(forKey: Key.tapAndDrag)
        twoFingerDrag = d.bool(forKey: Key.twoFingerDrag)
        tapZoneEnabled = d.bool(forKey: Key.tapZoneEnabled)
        tapZoneDepth = d.double(forKey: Key.tapZoneDepth)
        batteryWarningEnabled = d.bool(forKey: Key.batteryWarningEnabled)
        batteryWarningThreshold = d.integer(forKey: Key.batteryWarningThreshold)
        treatUnknownContinuousAsMagicMouse = d.bool(forKey: Key.treatUnknownContinuousAsMagicMouse)
        mouseType = MouseTypeSetting(rawValue: d.string(forKey: Key.mouseType) ?? "") ?? .automatic
        reverseWheelScroll = d.bool(forKey: Key.reverseWheelScroll)
        blockScrollWhileWheelPressed = d.bool(forKey: Key.blockScrollWhileWheelPressed)
        smoothWheelScroll = d.bool(forKey: Key.smoothWheelScroll)
        wheelScrollSpeed = d.double(forKey: Key.wheelScrollSpeed)
        magicMouseTrackingSpeed = d.double(forKey: Key.magicMouseTrackingSpeed)
        wheelTrackingSpeed = d.double(forKey: Key.wheelTrackingSpeed)
        modifierActions = Settings.loadModifierActions(d)
        appRules = Settings.loadAppRules(d)
        customPresets = Settings.loadCustomPresets(d)
        loading = false
    }

    // MARK: Derived

    var ruleSet: RuleSet {
        var c = BlockerConfig()
        c.blockScrollWhileClicked = blockScrollWhileClicked
        c.releaseGrace = Double(releaseGraceMs) / 1000
        c.deadZone = deadZone
        c.axisLock = axisLock
        c.axisLockThreshold = axisLockThreshold
        c.momentumEnabled = momentumEnabled
        c.blockHorizontalScroll = blockHorizontalScroll
        c.modifierActions = modifierActions
        c.reverseWheelScroll = reverseWheelScroll
        c.blockScrollWhileWheelPressed = blockScrollWhileWheelPressed
        c.smoothWheelScroll = smoothWheelScroll
        c.wheelScrollSpeed = min(max(wheelScrollSpeed, 1), Settings.maximumWheelScrollSpeed)
        return RuleSet(base: c, appRules: appRules)
    }

    var tapConfig: TapConfig {
        var c = TapConfig(sensitivity: tapSensitivity)
        c.enabled = tapToClick
        c.tapRightClick = tapRightClick
        c.rightClickMode = tapRightClickMode
        c.tapAndDrag = tapAndDrag
        // Follow-up windows (drag arm, double-tap) match the user's own double-click speed setting.
        c.dragWindow = NSEvent.doubleClickInterval
        c.doubleTapWindow = NSEvent.doubleClickInterval
        c.twoFingerDrag = twoFingerDrag
        c.tapZoneEnabled = tapZoneEnabled
        c.tapZoneDepth = min(max(tapZoneDepth, 0.25), 0.75)
        return c
    }

    /// The app used to be Godmouse (com.godmouse.app). On the first launch under the new
    /// bundle ID, adopt the old domain's settings wholesale so nothing is lost in the rename.
    private static func migrateFromGodmouseIfNeeded(_ d: UserDefaults) {
        let newDomain = Bundle.main.bundleIdentifier ?? "com.calmmouse.app"
        guard d.persistentDomain(forName: newDomain) == nil,
              let old = d.persistentDomain(forName: "com.godmouse.app"), !old.isEmpty else { return }
        d.setPersistentDomain(old, forName: newDomain)
    }

    // MARK: Presets

    /// How the Scrolling and Clicking tabs are set right now, as preset values.
    var currentPresetValues: PresetValues {
        var v = PresetValues()
        v.blockScrollWhileClicked = blockScrollWhileClicked
        v.releaseGraceMs = releaseGraceMs
        v.deadZone = deadZone
        v.axisLock = axisLock
        v.axisLockThreshold = axisLockThreshold
        v.momentumEnabled = momentumEnabled
        v.blockHorizontalScroll = blockHorizontalScroll
        v.tapToClick = tapToClick
        v.tapSensitivity = tapSensitivity
        v.tapRightClick = tapRightClick
        v.tapRightClickMode = tapRightClickMode
        v.tapAndDrag = tapAndDrag
        v.twoFingerDrag = twoFingerDrag
        v.tapZoneEnabled = tapZoneEnabled
        v.tapZoneDepth = tapZoneDepth
        return v
    }

    /// The preset (built-in or custom) that matches the current settings exactly, if any.
    /// The moment the user tweaks anything, no preset matches — which is the point:
    /// presets are starting points, not modes.
    var activePreset: Preset? {
        let current = currentPresetValues
        return (Preset.builtIn + customPresets).first { $0.values == current }
    }

    func apply(_ preset: Preset) {
        let v = preset.values
        loading = true   // one change notification at the end instead of thirteen
        blockScrollWhileClicked = v.blockScrollWhileClicked
        releaseGraceMs = v.releaseGraceMs
        deadZone = v.deadZone
        axisLock = v.axisLock
        axisLockThreshold = v.axisLockThreshold
        momentumEnabled = v.momentumEnabled
        blockHorizontalScroll = v.blockHorizontalScroll
        tapToClick = v.tapToClick
        tapSensitivity = v.tapSensitivity
        tapRightClick = v.tapRightClick
        tapRightClickMode = v.tapRightClickMode
        tapAndDrag = v.tapAndDrag
        twoFingerDrag = v.twoFingerDrag
        tapZoneEnabled = v.tapZoneEnabled
        tapZoneDepth = v.tapZoneDepth
        loading = false
        changed()
    }

    /// Saving under an existing custom preset's name updates that preset in place.
    @discardableResult
    func saveCurrentAsPreset(named name: String) -> Preset {
        let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
        var preset = Preset(name: trimmed, symbolName: "star", values: currentPresetValues)
        if let i = customPresets.firstIndex(where: { $0.name.caseInsensitiveCompare(trimmed) == .orderedSame }) {
            preset.id = customPresets[i].id
            customPresets[i] = preset
        } else {
            customPresets.append(preset)
        }
        return preset
    }

    func removePreset(id: UUID) {
        customPresets.removeAll { $0.id == id }
    }

    // MARK: Per-app helpers

    func rule(for bundleID: String) -> AppRule? {
        appRules.first { $0.bundleID.caseInsensitiveCompare(bundleID) == .orderedSame }
    }

    func upsert(_ rule: AppRule) {
        var rules = appRules
        if let i = rules.firstIndex(where: { $0.id == rule.id }) {
            rules[i] = rule
        } else if let i = rules.firstIndex(where: { $0.bundleID.caseInsensitiveCompare(rule.bundleID) == .orderedSame }) {
            rules[i] = rule
        } else {
            rules.append(rule)
        }
        appRules = rules
    }

    func removeRule(id: UUID) {
        appRules.removeAll { $0.id == id }
    }

    /// Menu-bar quick action: ignore Magic Mouse scrolling in this app entirely.
    func setScrollDisabled(_ disabled: Bool, forBundleID bundleID: String, name: String) {
        var rule = self.rule(for: bundleID) ?? AppRule(bundleID: bundleID, name: name)
        rule.name = rule.name.isEmpty ? name : rule.name
        rule.enabled = true
        rule.disableScrollEntirely = disabled ? true : nil
        if rule.isEmpty {
            removeRule(id: rule.id)
        } else {
            upsert(rule)
        }
    }

    // MARK: Persistence of structured values

    private static func loadModifierActions(_ d: UserDefaults) -> [ModifierCombo: ScrollAction] {
        guard let data = d.data(forKey: Key.modifierActions),
              let raw = try? JSONDecoder().decode([Int: ScrollAction].self, from: data) else { return [:] }
        return Dictionary(uniqueKeysWithValues: raw.map { (ModifierCombo(rawValue: $0.key), $0.value) })
    }

    private func saveModifierActions() {
        let raw = Dictionary(uniqueKeysWithValues: modifierActions.map { ($0.key.rawValue, $0.value) })
        d.set(try? JSONEncoder().encode(raw), forKey: Key.modifierActions)
    }

    private static func loadAppRules(_ d: UserDefaults) -> [AppRule] {
        guard let data = d.data(forKey: Key.appRules),
              let rules = try? JSONDecoder().decode([AppRule].self, from: data) else { return [] }
        return rules
    }

    private func saveAppRules() {
        d.set(try? JSONEncoder().encode(appRules), forKey: Key.appRules)
    }

    private static func loadCustomPresets(_ d: UserDefaults) -> [Preset] {
        guard let data = d.data(forKey: Key.customPresets),
              let presets = try? JSONDecoder().decode([Preset].self, from: data) else { return [] }
        return presets
    }

    private func saveCustomPresets() {
        d.set(try? JSONEncoder().encode(customPresets), forKey: Key.customPresets)
    }

    private func changed() {
        guard !loading else { return }
        NotificationCenter.default.post(name: Settings.didChange, object: self)
    }
}

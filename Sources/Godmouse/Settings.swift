import Foundation
import Combine
import GodmouseCore

/// UserDefaults-backed settings, observable by the UI.
/// Simple flags stay individual keys so they're scriptable:
///   defaults write com.godmouse.app blockScrollWhileClicked -bool YES
/// Structured data (per-app rules, modifier actions) is stored as JSON.
final class Settings: ObservableObject {
    static let shared = Settings()
    static let didChange = Notification.Name("GodmouseSettingsDidChange")

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
        static let batteryWarningEnabled = "batteryWarningEnabled"
        static let batteryWarningThreshold = "batteryWarningThreshold"
        static let treatUnknownContinuousAsMagicMouse = "treatUnknownContinuousAsMagicMouse"
        static let debugLogging = "debugLogging"
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
    @Published var tapToClick: Bool { didSet { d.set(tapToClick, forKey: Key.tapToClick); changed() } }
    @Published var tapSensitivity: Double { didSet { d.set(tapSensitivity, forKey: Key.tapSensitivity); changed() } }
    @Published var batteryWarningEnabled: Bool { didSet { d.set(batteryWarningEnabled, forKey: Key.batteryWarningEnabled); changed() } }
    @Published var batteryWarningThreshold: Int { didSet { d.set(batteryWarningThreshold, forKey: Key.batteryWarningThreshold); changed() } }
    @Published var treatUnknownContinuousAsMagicMouse: Bool { didSet { d.set(treatUnknownContinuousAsMagicMouse, forKey: Key.treatUnknownContinuousAsMagicMouse); changed() } }
    @Published var debugLogging: Bool { didSet { d.set(debugLogging, forKey: Key.debugLogging); changed() } }

    private init() {
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
            Key.batteryWarningEnabled: true,
            Key.batteryWarningThreshold: 15,
            Key.treatUnknownContinuousAsMagicMouse: false,
            Key.debugLogging: false,
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
        batteryWarningEnabled = d.bool(forKey: Key.batteryWarningEnabled)
        batteryWarningThreshold = d.integer(forKey: Key.batteryWarningThreshold)
        treatUnknownContinuousAsMagicMouse = d.bool(forKey: Key.treatUnknownContinuousAsMagicMouse)
        debugLogging = d.bool(forKey: Key.debugLogging)
        modifierActions = Settings.loadModifierActions(d)
        appRules = Settings.loadAppRules(d)
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
        return RuleSet(base: c, appRules: appRules)
    }

    var tapConfig: TapConfig {
        var c = TapConfig(sensitivity: tapSensitivity)
        c.enabled = tapToClick
        return c
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

    private func changed() {
        guard !loading else { return }
        NotificationCenter.default.post(name: Settings.didChange, object: self)
    }
}

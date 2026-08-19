import Foundation

// MARK: - Modifiers

/// Framework-free mirror of the modifier flags we care about.
public struct ModifierCombo: OptionSet, Hashable, Codable, Sendable {
    public let rawValue: Int
    public init(rawValue: Int) { self.rawValue = rawValue }

    public static let command = ModifierCombo(rawValue: 1 << 0)
    public static let option  = ModifierCombo(rawValue: 1 << 1)
    public static let control = ModifierCombo(rawValue: 1 << 2)
    public static let shift   = ModifierCombo(rawValue: 1 << 3)

    public static let all: [ModifierCombo] = [.command, .option, .control, .shift]

    public var label: String {
        if isEmpty { return "None" }
        var s = ""
        if contains(.control) { s += "⌃" }
        if contains(.option)  { s += "⌥" }
        if contains(.shift)   { s += "⇧" }
        if contains(.command) { s += "⌘" }
        return s
    }
}

/// What a modifier + scroll combination should do.
public enum ScrollAction: String, Codable, CaseIterable, Sendable {
    /// Leave the event alone (macOS default behaviour for that modifier).
    case normal
    /// Turn vertical scrolling into horizontal scrolling.
    case horizontal
    /// Flip the scroll direction.
    case invert
    /// Re-label the event as ⌘-scroll — what most creative apps (Figma, Sketch, Preview) zoom with.
    case zoomCommand
    /// Re-label the event as ⌃-scroll — macOS system zoom (Accessibility → Zoom).
    case zoomControl
    /// Swallow the scroll entirely while the modifier is held.
    case block

    public var label: String {
        switch self {
        case .normal:      return "Normal scrolling"
        case .horizontal:  return "Horizontal scrolling"
        case .invert:      return "Inverted scrolling"
        case .zoomCommand: return "Zoom (⌘ scroll)"
        case .zoomControl: return "Zoom (⌃ scroll, system)"
        case .block:       return "Do nothing"
        }
    }
}

// MARK: - Base config

public struct BlockerConfig: Equatable, Sendable, Codable {
    /// The headline feature: swallow Magic Mouse scrolling while any of its buttons is held.
    public var blockScrollWhileClicked: Bool = true
    /// Keep blocking for this long after the last button is released — the finger lifting
    /// off the shell almost always produces a little trailing scroll.
    public var releaseGrace: TimeInterval = 0.2

    /// Minimum travel (in points) a gesture must accumulate before it's allowed through.
    /// Kills the micro-jitter you get from a finger resting on the shell. 0 = off.
    public var deadZone: Double = 0

    /// Once a scroll gesture clearly commits to one axis, zero out the other one.
    public var axisLock: Bool = false
    public var axisLockThreshold: Double = 10
    public var axisLockRatio: Double = 1.4

    /// Drop the coasting tail after a Magic Mouse flick (macOS's own setting is system-wide).
    public var momentumEnabled: Bool = true

    /// Ignore sideways scrolling from the Magic Mouse completely.
    public var blockHorizontalScroll: Bool = false

    /// Ignore Magic Mouse scrolling altogether (mostly useful as a per-app rule).
    public var disableScrollEntirely: Bool = false

    /// modifier combination → what scrolling should do while it's held.
    public var modifierActions: [ModifierCombo: ScrollAction] = [:]

    public init() {}

    public func action(for modifiers: ModifierCombo) -> ScrollAction {
        modifierActions[modifiers] ?? .normal
    }
}

// Dictionary keyed by an OptionSet isn't Codable out of the box; store as [rawValue: action].
extension BlockerConfig {
    private enum CodingKeys: String, CodingKey {
        case blockScrollWhileClicked, releaseGrace, deadZone, axisLock, axisLockThreshold,
             axisLockRatio, momentumEnabled, blockHorizontalScroll, disableScrollEntirely,
             modifierActions
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        var s = BlockerConfig()
        s.blockScrollWhileClicked = try c.decodeIfPresent(Bool.self, forKey: .blockScrollWhileClicked) ?? s.blockScrollWhileClicked
        s.releaseGrace = try c.decodeIfPresent(Double.self, forKey: .releaseGrace) ?? s.releaseGrace
        s.deadZone = try c.decodeIfPresent(Double.self, forKey: .deadZone) ?? s.deadZone
        s.axisLock = try c.decodeIfPresent(Bool.self, forKey: .axisLock) ?? s.axisLock
        s.axisLockThreshold = try c.decodeIfPresent(Double.self, forKey: .axisLockThreshold) ?? s.axisLockThreshold
        s.axisLockRatio = try c.decodeIfPresent(Double.self, forKey: .axisLockRatio) ?? s.axisLockRatio
        s.momentumEnabled = try c.decodeIfPresent(Bool.self, forKey: .momentumEnabled) ?? s.momentumEnabled
        s.blockHorizontalScroll = try c.decodeIfPresent(Bool.self, forKey: .blockHorizontalScroll) ?? s.blockHorizontalScroll
        s.disableScrollEntirely = try c.decodeIfPresent(Bool.self, forKey: .disableScrollEntirely) ?? s.disableScrollEntirely
        let raw = try c.decodeIfPresent([Int: ScrollAction].self, forKey: .modifierActions) ?? [:]
        s.modifierActions = Dictionary(uniqueKeysWithValues: raw.map { (ModifierCombo(rawValue: $0.key), $0.value) })
        self = s
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(blockScrollWhileClicked, forKey: .blockScrollWhileClicked)
        try c.encode(releaseGrace, forKey: .releaseGrace)
        try c.encode(deadZone, forKey: .deadZone)
        try c.encode(axisLock, forKey: .axisLock)
        try c.encode(axisLockThreshold, forKey: .axisLockThreshold)
        try c.encode(axisLockRatio, forKey: .axisLockRatio)
        try c.encode(momentumEnabled, forKey: .momentumEnabled)
        try c.encode(blockHorizontalScroll, forKey: .blockHorizontalScroll)
        try c.encode(disableScrollEntirely, forKey: .disableScrollEntirely)
        try c.encode(Dictionary(uniqueKeysWithValues: modifierActions.map { ($0.key.rawValue, $0.value) }),
                     forKey: .modifierActions)
    }
}

// MARK: - Per-app rules

/// Overrides that apply only while a given app is frontmost. `nil` means "inherit".
public struct AppRule: Codable, Equatable, Sendable, Identifiable {
    public var id: UUID
    public var bundleID: String
    public var name: String
    public var enabled: Bool

    public var blockScrollWhileClicked: Bool?
    public var disableScrollEntirely: Bool?
    public var blockHorizontalScroll: Bool?
    public var momentumEnabled: Bool?
    public var axisLock: Bool?
    public var deadZone: Double?

    public init(id: UUID = UUID(), bundleID: String, name: String, enabled: Bool = true,
                blockScrollWhileClicked: Bool? = nil, disableScrollEntirely: Bool? = nil,
                blockHorizontalScroll: Bool? = nil, momentumEnabled: Bool? = nil,
                axisLock: Bool? = nil, deadZone: Double? = nil) {
        self.id = id
        self.bundleID = bundleID
        self.name = name
        self.enabled = enabled
        self.blockScrollWhileClicked = blockScrollWhileClicked
        self.disableScrollEntirely = disableScrollEntirely
        self.blockHorizontalScroll = blockHorizontalScroll
        self.momentumEnabled = momentumEnabled
        self.axisLock = axisLock
        self.deadZone = deadZone
    }

    /// True when the rule doesn't actually override anything.
    public var isEmpty: Bool {
        blockScrollWhileClicked == nil && disableScrollEntirely == nil && blockHorizontalScroll == nil
            && momentumEnabled == nil && axisLock == nil && deadZone == nil
    }

    public func apply(to base: BlockerConfig) -> BlockerConfig {
        guard enabled else { return base }
        var c = base
        if let v = blockScrollWhileClicked { c.blockScrollWhileClicked = v }
        if let v = disableScrollEntirely { c.disableScrollEntirely = v }
        if let v = blockHorizontalScroll { c.blockHorizontalScroll = v }
        if let v = momentumEnabled { c.momentumEnabled = v }
        if let v = axisLock { c.axisLock = v }
        if let v = deadZone { c.deadZone = v }
        return c
    }
}

/// Base settings plus per-app overrides; resolves the config for whichever app is frontmost.
public struct RuleSet: Equatable, Sendable, Codable {
    public var base: BlockerConfig
    public var appRules: [AppRule]

    public init(base: BlockerConfig = BlockerConfig(), appRules: [AppRule] = []) {
        self.base = base
        self.appRules = appRules
    }

    public func rule(forBundleID bundleID: String?) -> AppRule? {
        guard let bundleID else { return nil }
        return appRules.first { $0.enabled && $0.bundleID.caseInsensitiveCompare(bundleID) == .orderedSame }
    }

    public func config(forBundleID bundleID: String?) -> BlockerConfig {
        rule(forBundleID: bundleID)?.apply(to: base) ?? base
    }
}

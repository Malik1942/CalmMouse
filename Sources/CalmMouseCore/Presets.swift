import Foundation

// MARK: - Preset values

/// The knobs a preset sets: everything on the Scrolling and Clicking tabs.
/// Per-app rules, modifier shortcuts and battery settings are deliberately not part
/// of a preset — those are refinements a user layers on top, and switching the
/// overall "feel" of the mouse shouldn't wipe them.
///
/// The zero-argument initializer is the app's factory configuration, so
/// `PresetValues()` doubles as "what a fresh install feels like".
public struct PresetValues: Equatable, Sendable {
    public var blockScrollWhileClicked: Bool = true
    public var releaseGraceMs: Int = 200
    public var deadZone: Double = 0
    public var axisLock: Bool = false
    public var axisLockThreshold: Double = 10
    public var momentumEnabled: Bool = true
    public var blockHorizontalScroll: Bool = false
    public var tapToClick: Bool = false
    public var tapSensitivity: Double = 0.5
    public var tapRightClick: Bool = false
    public var tapRightClickMode: RightClickMode = .rightSide
    public var tapAndDrag: Bool = false
    public var twoFingerDrag: Bool = false
    public var tapZoneEnabled: Bool = false
    public var tapZoneDepth: Double = 0.5

    public init() {}
}

// Missing keys fall back to the factory value, so presets saved by an older
// version keep decoding after new settings are added.
extension PresetValues: Codable {
    private enum CodingKeys: String, CodingKey {
        case blockScrollWhileClicked, releaseGraceMs, deadZone, axisLock, axisLockThreshold,
             momentumEnabled, blockHorizontalScroll, tapToClick, tapSensitivity, tapRightClick,
             tapRightClickMode, tapAndDrag, twoFingerDrag, tapZoneEnabled, tapZoneDepth
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        var v = PresetValues()
        v.blockScrollWhileClicked = try c.decodeIfPresent(Bool.self, forKey: .blockScrollWhileClicked) ?? v.blockScrollWhileClicked
        v.releaseGraceMs = try c.decodeIfPresent(Int.self, forKey: .releaseGraceMs) ?? v.releaseGraceMs
        v.deadZone = try c.decodeIfPresent(Double.self, forKey: .deadZone) ?? v.deadZone
        v.axisLock = try c.decodeIfPresent(Bool.self, forKey: .axisLock) ?? v.axisLock
        v.axisLockThreshold = try c.decodeIfPresent(Double.self, forKey: .axisLockThreshold) ?? v.axisLockThreshold
        v.momentumEnabled = try c.decodeIfPresent(Bool.self, forKey: .momentumEnabled) ?? v.momentumEnabled
        v.blockHorizontalScroll = try c.decodeIfPresent(Bool.self, forKey: .blockHorizontalScroll) ?? v.blockHorizontalScroll
        v.tapToClick = try c.decodeIfPresent(Bool.self, forKey: .tapToClick) ?? v.tapToClick
        v.tapSensitivity = try c.decodeIfPresent(Double.self, forKey: .tapSensitivity) ?? v.tapSensitivity
        v.tapRightClick = try c.decodeIfPresent(Bool.self, forKey: .tapRightClick) ?? v.tapRightClick
        v.tapRightClickMode = try c.decodeIfPresent(RightClickMode.self, forKey: .tapRightClickMode) ?? v.tapRightClickMode
        v.tapAndDrag = try c.decodeIfPresent(Bool.self, forKey: .tapAndDrag) ?? v.tapAndDrag
        v.twoFingerDrag = try c.decodeIfPresent(Bool.self, forKey: .twoFingerDrag) ?? v.twoFingerDrag
        v.tapZoneEnabled = try c.decodeIfPresent(Bool.self, forKey: .tapZoneEnabled) ?? v.tapZoneEnabled
        v.tapZoneDepth = try c.decodeIfPresent(Double.self, forKey: .tapZoneDepth) ?? v.tapZoneDepth
        self = v
    }
}

// MARK: - Preset

/// A named, one-click starting point. Applying one sets the Scrolling and Clicking
/// options; the user is free to tweak everything afterwards.
public struct Preset: Codable, Equatable, Sendable, Identifiable {
    public var id: UUID
    public var name: String
    public var summary: String
    /// SF Symbol shown on the preset's card.
    public var symbolName: String
    public var values: PresetValues

    public init(id: UUID = UUID(), name: String, summary: String = "",
                symbolName: String = "slider.horizontal.3", values: PresetValues) {
        self.id = id
        self.name = name
        self.summary = summary
        self.symbolName = symbolName
        self.values = values
    }
}

// MARK: - Built-in presets

extension Preset {
    /// The headline fix and nothing else — factory configuration.
    public static let justFixClicking = Preset(
        id: UUID(uuidString: "6E1B49F2-0001-4000-8000-000000000001")!,
        name: "Just fix clicking",
        summary: "Pages hold still while you click. Everything else stays the way macOS made it.",
        symbolName: "cursorarrow.click.2",
        values: PresetValues())

    /// For people who hate every kind of accidental scrolling.
    public static let extraSteady: Preset = {
        var v = PresetValues()
        v.releaseGraceMs = 300
        v.deadZone = 8
        v.axisLock = true
        v.momentumEnabled = false
        return Preset(
            id: UUID(uuidString: "6E1B49F2-0001-4000-8000-000000000002")!,
            name: "Extra steady",
            summary: "Small nudges are ignored, scrolling sticks to straight lines, and the page stops the moment your finger does.",
            symbolName: "scope",
            values: v)
    }()

    /// Taps, right-taps and tap-drags, like a trackpad grafted onto the mouse.
    public static let trackpadFeel: Preset = {
        var v = PresetValues()
        v.tapToClick = true
        v.tapRightClick = true
        v.tapAndDrag = true
        return Preset(
            id: UUID(uuidString: "6E1B49F2-0001-4000-8000-000000000003")!,
            name: "Trackpad feel",
            summary: "A light tap clicks, a tap on the right side right-clicks, and tap-then-drag moves things — your mouse starts acting like a trackpad.",
            symbolName: "hand.tap",
            values: v)
    }()

    public static let builtIn: [Preset] = [.justFixClicking, .extraSteady, .trackpadFeel]
}
